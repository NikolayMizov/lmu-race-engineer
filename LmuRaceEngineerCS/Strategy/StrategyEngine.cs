using LmuRaceEngineerCS.Models;
using LmuRaceEngineerCS.Shared;
using System.Runtime.InteropServices;
using System.Text;

namespace LmuRaceEngineerCS.Strategy;

/// <summary>
/// Background strategy engine. Wakes every ~12 seconds, freezes the current
/// StrategySnapshot, scans opponents, simulates up to 7 candidate actions,
/// scores them, and fires <see cref="RecommendationUpdated"/>.
/// </summary>
public sealed class StrategyEngine : IDisposable
{
    // ── Constants ─────────────────────────────────────────────────────────────
    private const int    MaxSimLaps   = 20;
    private const int    MaxPerSide   = 3;   // rivals kept each side of the player

    // Scoring weights (must sum to ≤ 1 for the positive terms)
    private const double WFinishTime  = 0.40;
    private const double WPosition    = 0.25;
    private const double WFuelSafety  = 0.15;
    private const double WTireCliff   = 0.10;
    private const double WTrafficLoss = 0.10;

    // ── Dependencies ──────────────────────────────────────────────────────────
    private MMapReader?                  _mmap;
    private Func<StrategySnapshot?>?     _snapshotSource;

    // ── State ─────────────────────────────────────────────────────────────────
    private volatile StrategyRecommendation? _latest;
    private readonly object                  _lock = new();
    private System.Threading.Timer?          _timer;
    private int                              _intervalMs;

    // ── Events ────────────────────────────────────────────────────────────────
    public event EventHandler<StrategyRecommendation>? RecommendationUpdated;

    // ── Public API ────────────────────────────────────────────────────────────
    public StrategyRecommendation? Latest()
    {
        lock (_lock) return _latest;
    }

    public void Start(MMapReader mmap, Func<StrategySnapshot?> snapshotSource, int intervalMs = 12_000)
    {
        _mmap           = mmap;
        _snapshotSource = snapshotSource;
        _intervalMs     = intervalMs;
        _timer = new System.Threading.Timer(OnTimer, null,
                     TimeSpan.FromMilliseconds(intervalMs),
                     TimeSpan.FromMilliseconds(intervalMs));
    }

    public void Stop()  => _timer?.Change(Timeout.Infinite, Timeout.Infinite);

    public void Dispose()
    {
        Stop();
        _timer?.Dispose();
        _timer = null;
    }

    // ── Public trigger ────────────────────────────────────────────────────────
    /// <summary>Force an immediate recompute regardless of StrategyVoiceOnly setting.</summary>
    public void ForceCompute() => Task.Run(Compute);

    // ── Timer callback ────────────────────────────────────────────────────────
    private void OnTimer(object? state)
    {
        if (App.Settings.StrategyVoiceOnly) return;
        try { Compute(); }
        catch { /* keep timer alive */ }
    }

    private void Compute()
    {
        try
        {
            var snap = _snapshotSource?.Invoke();
            if (snap == null) return;
            if (snap.AvgLapTime <= 0 || snap.LapsRemaining <= 0) return;
            if (!snap.IsRace) return;

            var input = BuildInput(snap);
            if (input.AvgFuelPerLap <= 0) return;

            var candidates = GenerateCandidates(input);
            if (candidates.Count == 0) return;

            var scored = Score(candidates, input);
            var best   = scored[0];
            var alt    = scored.Count > 1 ? scored[1] : null;

            // Opponent tactical flags
            bool undercutThreat     = false;
            bool overcutOpportunity = false;
            double? nearestGap      = null;
            double? rivalPitLap     = null;

            double cliffThr    = RaceInput.PersonalCliffThreshold(input.PersonalDegOnset);
            double nominalStint = input.LapsRemaining > 0 && input.MaxWearRate > 0
                ? (1.0 - cliffThr) / input.MaxWearRate
                : 20;

            // Tactical undercut/overcut — evaluate the two cars ahead and two behind.
            foreach (var opp in OpponentModel.NearbyWindow(input.Opponents, 2))
            {
                if (!undercutThreat)
                    undercutThreat = OpponentModel.IsUndercutThreat(
                        opp, input.PitLossSeconds, input.StintLaps);
                if (!overcutOpportunity)
                    overcutOpportunity = OpponentModel.IsOvercutOpportunity(
                        opp, input.PitLossSeconds, input.AvgLapTime,
                        opp.EstimatedLapTime > 0 ? opp.EstimatedLapTime : input.AvgLapTime);
            }

            // Nearest rival (either side) for the gap / expected-pit readout.
            foreach (var opp in input.Opponents)
            {
                double gap = Math.Abs(opp.GapToPlayer);
                if (nearestGap == null || gap < nearestGap.Value)
                {
                    nearestGap  = gap;
                    rivalPitLap = OpponentModel.EstimatedPitLap(opp, input.AvgFuelPerLap, nominalStint)
                                  is double laps ? input.CurrentLap + laps : (double?)null;
                }
            }

            int lapsToSim = (int)Math.Min(Math.Ceiling(input.LapsRemaining), MaxSimLaps);
            double uncertainty = Math.Sqrt(lapsToSim) * 0.3;

            var rec = new StrategyRecommendation
            {
                Action            = best.ActionType,
                RecommendedPitLap = best.PitOnLap,
                ActionLabel       = ActionLabel(best.ActionType, input.CompoundAdvice),
                Rationale         = best.Rationale,
                TopScore          = best.Score,
                AltScore          = alt?.Score ?? 0,
                ProjectedFinishTime  = best.ProjectedFinishTime,
                ProjectedPosition    = best.ProjectedPosition,
                FuelSafetyMargin     = best.FuelSafetyMargin,
                TireCliffRisk        = best.TireCliffRisk,
                UndercutThreat       = undercutThreat,
                OvercutOpportunity   = overcutOpportunity,
                NearestRivalGap      = nearestGap,
                RivalEstimatedPitLap = rivalPitLap,
                FinishTimeUncertainty = uncertainty,
                CompoundAdvice       = input.CompoundAdvice,
                WeatherCondition     = input.WeatherCondition,
                Candidates           = scored,
                ComputedAt           = DateTimeOffset.Now,
                SnapshotLap          = snap.CurrentLap,
            };

            lock (_lock) { _latest = rec; }
            RecommendationUpdated?.Invoke(this, rec);
        }
        catch { /* keep timer alive on transient errors */ }
    }

    // ── Build input ───────────────────────────────────────────────────────────
    private RaceInput BuildInput(StrategySnapshot snap)
    {
        // ── Tyre-wear polarity conversion ──────────────────────────────────────
        // LMU's mWear (and therefore snap.TireWear / snap.WorstWear) is tread REMAINING:
        // 1.0 = brand-new, counting down as the tyre wears. The strategy models
        // (TireModel/PaceModel) expect wear that ACCUMULATES upward (0 = new, 1 = fully
        // worn), so convert at this boundary. The worst tyre is the one with the LEAST
        // tread remaining = the HIGHEST accumulated wear.
        bool   hasTyre        = snap.TireWear.Length == 4 && snap.TireWear.Max() > 0;
        double worstWearAccum = hasTyre ? Math.Clamp(1.0 - snap.TireWear.Min(), 0.0, 1.0) : 0.0;

        // Wear rate ≈ accumulated wear so far / laps on this set.
        double wearRate = 0;
        if (worstWearAccum > 0 && snap.StintLaps > 0)
            wearRate = worstWearAccum / Math.Max(1, snap.StintLaps);

        const bool isLapBased = false;  // all LMU races are time-based

        double cliffThrBi  = RaceInput.PersonalCliffThreshold(
            snap.PersonalDegOnset > 0 ? snap.PersonalDegOnset : 0.60);
        double wearRateBi  = wearRate;
        int nominalStintEst = wearRateBi > 0
            ? (int)Math.Round((1.0 - cliffThrBi) / wearRateBi) : 20;

        var opponents = ScanOpponents(snap, nominalStintEst);

        return new RaceInput(
            CurrentFuelFraction: snap.Ve / 100.0,
            AvgFuelPerLap:       snap.AvgFuel,
            WorstTireWear:       worstWearAccum,
            MaxWearRate:         wearRate,
            CompoundType:        snap.CompoundTypes.Length > 0 ? snap.CompoundTypes[0] : 0,
            AvgLapTime:          snap.AvgLapTime,
            FasterClassGap:      snap.FasterClassGap,
            CurrentLap:          snap.CurrentLap,
            StintLaps:           snap.StintLaps,
            PitStops:            snap.PitStops,
            ClassPos:            snap.ClassPos,
            GapFront:            snap.GapFront,
            GapBehind:           snap.GapBehind,
            LapsRemaining:       snap.LapsRemaining,
            IsLapBased:          isLapBased,
            SessionTimeRemaining: snap.SessionTimeRemaining,
            IsRace:              snap.IsRace,
            PitLossSeconds:      App.Settings.PitLossSeconds,
            EarliestSafePitLap:  snap.EarliestPit,
            Opponents:           opponents,
            WeatherCondition:    snap.WeatherCondition,
            WeatherTrend:        snap.WeatherTrend,
            CompoundAdvice:      snap.CompoundAdvice,
            PersonalDegOnset:    snap.PersonalDegOnset > 0 ? snap.PersonalDegOnset : 0.60
        );
    }

    // ── Scan opponents ────────────────────────────────────────────────────────
    private unsafe IReadOnlyList<OpponentState> ScanOpponents(StrategySnapshot snap, int nominalStint = 20)
    {
        if (_mmap == null || !_mmap.IsOpen) return Array.Empty<OpponentState>();

        try
        {
            var ses = _mmap.ReadScoringInfo();
            int n   = ses.mNumVehicles;

            // Find player scoring entry for class + position reference
            double playerTimeBehind = 0;
            int    playerPlace      = snap.ClassPos;
            string playerClass      = "";

            for (int i = 0; i < n; i++)
            {
                var v = _mmap.ReadVehicleScoring(i);
                if (v.mIsPlayer != 0)
                {
                    playerTimeBehind = v.mTimeBehindLeader;
                    playerClass      = DecodeClass(ref v);
                    break;
                }
            }

            var list = new List<(double absDist, OpponentState opp)>(n);

            for (int i = 0; i < n; i++)
            {
                var v = _mmap.ReadVehicleScoring(i);
                if (v.mIsPlayer != 0 || v.mPlace == 0) continue;

                string vc = DecodeClass(ref v);
                if (vc != playerClass) continue;

                double gap = v.mTimeBehindLeader - playerTimeBehind;
                // positive gap = rival has more time behind leader = rival is behind us
                // negative gap = rival is ahead of us

                double lapTime = v.mEstimatedLapTime > 0 ? v.mEstimatedLapTime
                               : v.mLastLapTime > 0 ? v.mLastLapTime : snap.AvgLapTime;

                // Estimate stint laps using track-average nominal stint instead of hardcoded 20
                int stintLaps = Math.Max(0, v.mTotalLaps - v.mNumPitstops * nominalStint);

                var opp = new OpponentState(
                    VehicleIndex:      i,
                    DriverName:        DecodeDriver(ref v),
                    ClassPosition:     v.mPlace,
                    TotalLaps:         v.mTotalLaps,
                    TimeBehindLeader:  v.mTimeBehindLeader,
                    GapToPlayer:       -gap,  // positive = ahead of player
                    LastLapTime:       v.mLastLapTime,
                    EstimatedLapTime:  lapTime,
                    NumPitstops:       v.mNumPitstops,
                    IsInPits:          v.mInPits != 0,
                    PitState:          v.mPitState,
                    FuelFraction:      v.mFuelFraction / 255.0,
                    StintLaps:         stintLaps
                );

                list.Add((Math.Abs(gap), opp));
            }

            // Keep a balanced set — the nearest few ahead and the nearest few behind —
            // so undercut/overcut can see cars on both sides of the player.
            var ahead  = list.Where(x => x.opp.GapToPlayer > 0)
                             .OrderBy(x => x.absDist).Take(MaxPerSide);
            var behind = list.Where(x => x.opp.GapToPlayer < 0)
                             .OrderBy(x => x.absDist).Take(MaxPerSide);
            return ahead.Concat(behind).Select(x => x.opp).ToList();
        }
        catch
        {
            return Array.Empty<OpponentState>();
        }
    }

    private static unsafe string DecodeClass(ref LmuVehicleScoring v)
    {
        fixed (byte* p = v.mVehicleClass) return MMapReader.DecodeBytes(p, 32);
    }

    private static unsafe string DecodeDriver(ref LmuVehicleScoring v)
    {
        fixed (byte* p = v.mDriverName) return MMapReader.DecodeBytes(p, 32);
    }

    // ── Generate candidates ───────────────────────────────────────────────────
    private List<StrategyCandidate> GenerateCandidates(RaceInput input)
    {
        var list = new List<StrategyCandidate>(7);

        int lapsToSim = (int)Math.Min(Math.Ceiling(input.LapsRemaining), MaxSimLaps);
        if (lapsToSim <= 0) return list;

        double cliffThrGc  = RaceInput.PersonalCliffThreshold(input.PersonalDegOnset);
        double nominalStint = input.MaxWearRate > 0
            ? (1.0 - cliffThrGc) / input.MaxWearRate
            : 20;

        // 1. Stay out (no pit)
        list.Add(SimulateAction(input, StrategyActionType.StayOut, 0, lapsToSim));

        // 2. Stay out one extra lap (deferred)
        if (lapsToSim > 2)
            list.Add(SimulateAction(input, StrategyActionType.StayOut, 0, lapsToSim, deferred: true));

        bool canPitNow = input.CurrentLap >= input.EarliestSafePitLap;

        // 3. Pit now — full fuel
        if (canPitNow)
            list.Add(SimulateAction(input, StrategyActionType.PitNowFullFuel, input.CurrentLap + 1, lapsToSim));

        // 4. Pit now — short fuel (only when few laps left)
        if (canPitNow && input.LapsRemaining < 15)
            list.Add(SimulateAction(input, StrategyActionType.PitNowShortFuel, input.CurrentLap + 1, lapsToSim));

        // 5. Save fuel (only if StayOut shows tight fuel margin)
        var stayOutCandidate = list.FirstOrDefault(c => c.ActionType == StrategyActionType.StayOut);
        if (stayOutCandidate != null && stayOutCandidate.FuelSafetyMargin < 2.0)
            list.Add(SimulateAction(input, StrategyActionType.SaveFuel, 0, lapsToSim));

        // Cars a pit exchange can affect: two ahead + two behind.
        var nearby = OpponentModel.NearbyWindow(input.Opponents, 2).ToList();

        // 6. Push undercut — pit on next lap
        if (canPitNow)
        {
            bool threat = nearby.Any(o =>
                OpponentModel.IsUndercutThreat(o, input.PitLossSeconds, input.StintLaps));
            if (threat)
                list.Add(SimulateAction(input, StrategyActionType.PushUndercut, input.CurrentLap + 1, lapsToSim));
        }

        // 7. Cover rival — pit one lap after rival's expected pit
        foreach (var opp in nearby)
        {
            bool oppOrty = OpponentModel.IsOvercutOpportunity(
                opp, input.PitLossSeconds, input.AvgLapTime,
                opp.EstimatedLapTime > 0 ? opp.EstimatedLapTime : input.AvgLapTime);
            if (oppOrty)
            {
                double? eplLaps = OpponentModel.EstimatedPitLap(opp, input.AvgFuelPerLap, nominalStint);
                if (eplLaps.HasValue)
                {
                    int coverLap = (int)Math.Round(input.CurrentLap + eplLaps.Value) + 1;
                    if (coverLap > input.CurrentLap && coverLap <= input.CurrentLap + lapsToSim)
                        list.Add(SimulateAction(input, StrategyActionType.CoverRival, coverLap, lapsToSim));
                }
                break; // one cover action is enough
            }
        }

        return list;
    }

    // ── Simulate one action ───────────────────────────────────────────────────
    private static StrategyCandidate SimulateAction(
        RaceInput input,
        StrategyActionType action,
        int pitOnLap,
        int lapsToSim,
        bool deferred = false)
    {
        double fuel     = input.CurrentFuelFraction;
        double wear     = input.WorstTireWear;
        double elapsed  = 0.0;
        double traffic  = 0.0;
        int    pos      = input.ClassPos;
        bool   hasPitted = false;

        // For short fuel: compute minimum fuel amount
        double shortFuel = input.LapsRemaining * input.AvgFuelPerLap * 1.05;
        shortFuel = Math.Min(shortFuel, 1.0);

        double wrongCompoundPenalty = WeatherHelper.PacePenaltyPerLap(input.CompoundAdvice);

        for (int lap = 0; lap < lapsToSim; lap++)
        {
            int    currentLap = input.CurrentLap + lap;
            double lapTime    = PaceModel.ExpectedLapTime(
                                    input.AvgLapTime, fuel, wear, input.FasterClassGap,
                                    input.PersonalDegOnset);

            // Apply wrong-compound pace cost until a pit stop changes the tyre
            if (!hasPitted && wrongCompoundPenalty > 0)
                lapTime += wrongCompoundPenalty;

            double penalty    = PaceModel.TrafficPenalty(input.FasterClassGap);
            traffic  += penalty;
            elapsed  += lapTime;

            // Fuel / wear deltas for this lap
            double fuelDelta = input.AvgFuelPerLap;
            double wearDelta = input.MaxWearRate;

            if (action == StrategyActionType.SaveFuel && !hasPitted)
            {
                fuelDelta -= input.AvgFuelPerLap * 0.015;   // save ~1.5%/lap
                elapsed   += 0.35;                           // 0.35 s slower
            }
            else if (action == StrategyActionType.PushUndercut && !hasPitted)
            {
                fuelDelta += input.AvgFuelPerLap * 0.08;    // +8% fuel usage
                elapsed   -= 0.40;                           // 0.4 s faster
            }

            fuel -= fuelDelta;
            wear  = Math.Min(1.0, wear + wearDelta);

            // Pit stop
            bool isPitLap = pitOnLap > 0 && currentLap == pitOnLap && !hasPitted;
            if (!isPitLap && deferred && lap == 1 && pitOnLap == 0)
                isPitLap = false; // deferred StayOut just means we show one lap of grace

            if (isPitLap)
            {
                hasPitted = true;
                double trafficRisk = input.GapBehind < 5 ? 0.5 : 0.0;
                elapsed += PitLossModel.TruePitLoss(input.PitLossSeconds, trafficRisk);
                wear     = 0.03;   // fresh tires with install wear

                fuel = action == StrategyActionType.PitNowShortFuel
                       ? shortFuel
                       : 1.0;     // full fill

                // Count opponents behind us who are within the pit-loss window
                double pitWindow = input.PitLossSeconds + 5.0;
                int oppInWindow = input.Opponents.Count(o =>
                    o.GapToPlayer < 0 && Math.Abs(o.GapToPlayer) < pitWindow);
                pos += Math.Max(1, oppInWindow);
                pos = Math.Min(pos, input.ClassPos + 3);
            }

            if (fuel < 0) fuel = 0; // ran out — will penalise via safety margin
        }

        double fuelSafetyMargin = fuel / Math.Max(0.001, input.AvgFuelPerLap);
        if (fuelSafetyMargin < 0) fuelSafetyMargin = -999;

        double  personalCliff = RaceInput.PersonalCliffThreshold(input.PersonalDegOnset);
        double? ltc        = TireModel.LapsToCliff(wear, input.MaxWearRate, personalCliff);
        double  cliffRisk  = TireModel.CliffRisk(ltc, lapsToSim);

        string rationale = BuildRationale(action, input, pos, fuelSafetyMargin, pitOnLap);

        return new StrategyCandidate(
            ActionType:          action,
            PitOnLap:            pitOnLap,
            ProjectedFinishTime: elapsed,
            ProjectedPosition:   pos,
            FuelSafetyMargin:    fuelSafetyMargin,
            TireCliffRisk:       cliffRisk,
            TrafficLoss:         traffic,
            Score:               0,   // filled by Score()
            Rationale:           rationale
        );
    }

    // ── Scoring ───────────────────────────────────────────────────────────────
    private static List<StrategyCandidate> Score(
        List<StrategyCandidate> candidates, RaceInput input)
    {
        if (candidates.Count == 0) return candidates;

        // Normalise each metric to 0–1 across the set
        double minTime  = candidates.Min(c => c.ProjectedFinishTime);
        double maxTime  = candidates.Max(c => c.ProjectedFinishTime);
        double minPos   = candidates.Min(c => c.ProjectedPosition);
        double maxPos   = candidates.Max(c => c.ProjectedPosition);
        double minTraff = candidates.Min(c => c.TrafficLoss);
        double maxTraff = candidates.Max(c => c.TrafficLoss);

        double Norm(double v, double lo, double hi, bool lowerBetter)
        {
            if (hi <= lo) return 1.0;
            double n = (v - lo) / (hi - lo);
            return lowerBetter ? 1.0 - n : n;
        }

        var scored = candidates.Select(c =>
        {
            double sc =
                WFinishTime  * Norm(c.ProjectedFinishTime, minTime, maxTime, lowerBetter: true)
              + WPosition    * Norm(c.ProjectedPosition,   minPos,  maxPos,  lowerBetter: true)
              + WFuelSafety  * Math.Clamp(c.FuelSafetyMargin / 3.0, 0.0, 1.0)
              - WTireCliff   * c.TireCliffRisk
              + WTrafficLoss * Norm(c.TrafficLoss, minTraff, maxTraff, lowerBetter: true);

            // Hard penalty for running out of fuel
            if (c.FuelSafetyMargin <= -99) sc -= 10.0;

            return c with { Score = Math.Round(sc, 4) };
        }).OrderByDescending(c => c.Score).ToList();

        return scored;
    }

    // ── Rationale strings ─────────────────────────────────────────────────────
    private static string BuildRationale(
        StrategyActionType action, RaceInput input, int projPos, double fuelMargin, int pitOnLap)
    {
        string pos  = projPos == input.ClassPos ? $"hold P{projPos}"
                    : projPos < input.ClassPos  ? $"gain P{projPos}"
                    : $"exit P{projPos}";

        string cmpLabel = input.CompoundAdvice != CompoundAdvice.None
            ? $"switch to {WeatherHelper.CompoundLabelShort(input.CompoundAdvice)}, "
            : "";

        bool isPitAction = action is StrategyActionType.PitNowFullFuel
                                  or StrategyActionType.PitNowShortFuel
                                  or StrategyActionType.PushUndercut
                                  or StrategyActionType.CoverRival;

        // StayOut with a compound mismatch: warn driver conditions are wrong
        string weatherWarn = (!isPitAction && input.CompoundAdvice != CompoundAdvice.None)
            ? $" — track {WeatherHelper.ConditionLabel(input.WeatherCondition)}, {WeatherHelper.CompoundLabelShort(input.CompoundAdvice)} advised"
            : "";

        return action switch
        {
            StrategyActionType.StayOut =>
                fuelMargin >= 2
                    ? $"Stay out — {pos} with {fuelMargin:F1} lap fuel margin{weatherWarn}"
                    : $"Stay out — {pos}, low fuel ({fuelMargin:F1} laps){weatherWarn}",

            StrategyActionType.PitNowFullFuel =>
                $"Pit lap {pitOnLap} — {cmpLabel}full fuel, {pos} after service",

            StrategyActionType.PitNowShortFuel =>
                $"Pit lap {pitOnLap} — {cmpLabel}short fill, {pos}, lighter car to finish",

            StrategyActionType.SaveFuel =>
                $"Lift & coast — extend stint, {pos}, fuel margin improves{weatherWarn}",

            StrategyActionType.PushUndercut =>
                $"Push & pit lap {pitOnLap} — {cmpLabel}undercut rival, target {pos}",

            StrategyActionType.CoverRival =>
                $"Stay out, pit lap {pitOnLap} — {cmpLabel}cover rival stop, {pos}",

            _ => ""
        };
    }

    private static string ActionLabel(StrategyActionType action, CompoundAdvice advice)
    {
        string compound = advice != CompoundAdvice.None
            ? $" — {WeatherHelper.CompoundLabel(advice)}"
            : "";

        return action switch
        {
            StrategyActionType.StayOut when advice != CompoundAdvice.None
                                        => $"STAY OUT ⚠ {WeatherHelper.CompoundLabel(advice)}",
            StrategyActionType.StayOut         => "STAY OUT",
            StrategyActionType.PitNowFullFuel  => $"PIT NOW{compound}",
            StrategyActionType.PitNowShortFuel => $"SHORT PIT{compound}",
            StrategyActionType.SaveFuel        => "SAVE FUEL",
            StrategyActionType.PushUndercut    => $"UNDERCUT{compound}",
            StrategyActionType.CoverRival      => $"COVER RIVAL{compound}",
            _                                  => "—",
        };
    }
}
