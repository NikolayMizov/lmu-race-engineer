using LmuRaceEngineerCS.Models;
using LmuRaceEngineerCS.Strategy;

namespace LmuRaceEngineerCS.Services;

/// <summary>
/// Converts a recognized voice intent into a TTS response using live telemetry.
/// </summary>
public sealed class IntentResponder
{
    private string _lastResponse = "";

    public void Handle(string intent)
    {
        var snap = App.Telem.Snapshot();

        // Trigger a fresh compute when voice-only mode is active and a strategy intent fires
        if (App.Settings.StrategyVoiceOnly && IsStrategyIntent(intent))
            App.Telem.ForceStrategyCompute();

        var rec  = App.Telem.LatestRecommendation();

        string response = intent switch
        {
            // ── Telemetry ─────────────────────────────────────────────────────
            "pit_window"        => PitWindow(snap, rec),
            "gap_front"         => GapFront(snap),
            "gap_behind"        => GapBehind(snap),
            "energy"            => Energy(snap),
            "laps_remaining"    => LapsRemaining(snap),
            "tire_wear"         => TireWear(snap),
            "tire_temps"        => TireTemps(snap),
            "position"          => Position(snap),
            "pit_stops"         => PitStops(snap),
            "lap_time"          => LapTime(snap),
            "fuel_add"          => FuelAdd(snap),
            "last_lap"          => LastLap(snap),
            "best_lap"          => BestLap(snap),
            "full_update"       => FullUpdate(snap, rec),
            "repeat_last"       => _lastResponse.Length > 0 ? _lastResponse : "Nothing to repeat.",
            "radio_check"       => "radio_check",   // uses pre-cached static phrase
            "mode_stats"        => ModeStats(snap),
            "pace_mode_gain"    => PaceModeGain(snap),
            "stint_length"      => StintLength(snap),
            "brake_temps"       => BrakeTemps(snap),
            "track_conditions"  => TrackConditions(snap),
            "delta"             => Delta(snap),
            "hybrid_charge"     => HybridCharge(snap),
            "driver_name"       => DriverName(snap),
            // ── Strategy ─────────────────────────────────────────────────────
            "strategy_rec"      => StrategyRec(rec),
            "rival_update"      => RivalUpdate(rec),
            "fuel_margin"       => FuelMargin(rec),
            "tire_cliff"        => TireCliff(rec),
            "projected_position"=> ProjectedPosition(rec),
            _                   => "",
        };

        if (response.Length == 0) return;

        if (intent != "repeat_last" && intent != "radio_check")
            _lastResponse = response;
        App.Tts.Speak(response);
    }

    // ── No-data helpers ───────────────────────────────────────────────────────

    private static string NoData()     => "No data. Connect to the game first.";
    private static string NoStratData()=> "Strategy data not yet available.";

    // ── Telemetry handlers ────────────────────────────────────────────────────

    private static string PitWindow(StrategySnapshot? snap, StrategyRecommendation? rec)
    {
        if (snap == null) return NoData();
        int pitLap = rec?.RecommendedPitLap > 0
            ? rec.RecommendedPitLap
            : snap.PitLap.HasValue ? (int)snap.PitLap.Value : 0;

        if (pitLap == 0)
            return "No pit stop required. Current fuel and tires should last to the finish.";

        string latest = snap.LatestPit.HasValue
            ? $"Latest lap {snap.LatestPit.Value:F0}."
            : "";
        string reason = snap.PitLimiter != null ? $" Reason: {snap.PitLimiter}." : "";
        return $"Pit on lap {pitLap}. Earliest lap {snap.EarliestPit:F0}. {latest}{reason}".Trim();
    }

    private static string GapFront(StrategySnapshot? snap)
    {
        if (snap == null) return NoData();
        if (snap.GapFront <= 0) return "No car ahead.";
        double delta = snap.AvgLapDeltaFront;
        string trend = delta < -0.05
            ? $"Closing at {Math.Abs(delta):F2} seconds per lap."
            : delta > 0.05
                ? $"Losing {delta:F2} seconds per lap."
                : "Gap is stable.";
        return $"Gap ahead: {snap.GapFront:F1} seconds. {trend}";
    }

    private static string GapBehind(StrategySnapshot? snap)
    {
        if (snap == null) return NoData();
        if (snap.GapBehind <= 0) return "No car behind.";
        double delta = snap.AvgLapDeltaBehind;
        string trend = delta > 0.05
            ? $"He is closing at {delta:F2} seconds per lap."
            : delta < -0.05
                ? $"You are pulling away at {Math.Abs(delta):F2} per lap."
                : "Gap behind is stable.";
        return $"Gap behind: {snap.GapBehind:F1} seconds. {trend}";
    }

    private static string Energy(StrategySnapshot? snap)
    {
        if (snap == null) return NoData();
        if (snap.Ve <= 0) return "No fuel data yet.";
        string deficit = snap.FuelDeficit > 0.01
            ? $" Need {snap.FuelDeficit * 100:F0}% more VE to finish."
            : "";
        return $"VE at {snap.Ve:F0}%. Good for {snap.LapsOnVe:F1} more laps.{deficit}";
    }

    private static string LapsRemaining(StrategySnapshot? snap)
    {
        if (snap == null) return NoData();
        if (snap.LapsRemaining <= 0) return "Laps remaining not available.";
        return $"Approximately {snap.LapsRemaining:F0} laps to go.";
    }

    private static string TireWear(StrategySnapshot? snap)
    {
        if (snap == null) return NoData();
        if (snap.WorstWear <= 0) return "No tire health data yet.";
        string tireLife = snap.TireLaps.HasValue
            ? $" Estimated {snap.TireLaps.Value:F0} laps left on this set."
            : "";
        // individual corners if available (mWear is tread remaining = health)
        string corners = "";
        double worstHealth = snap.WorstWear * 100;
        if (snap.TireWear.Length == 4)
        {
            worstHealth = snap.TireWear.Min() * 100;
            corners = $" FL {snap.TireWear[0] * 100:F0}, FR {snap.TireWear[1] * 100:F0}," +
                      $" RL {snap.TireWear[2] * 100:F0}, RR {snap.TireWear[3] * 100:F0}.";
        }
        return $"Worst tire health: {worstHealth:F0}%.{tireLife}{corners}";
    }

    private static string TireTemps(StrategySnapshot? snap)
    {
        if (snap == null) return NoData();
        if (snap.TireTempsC.Length < 4) return "No tire temperature data.";
        double[] t = snap.TireTempsC;
        return $"Tire temps: FL {t[0]:F0}, FR {t[1]:F0}, RL {t[2]:F0}, RR {t[3]:F0} degrees.";
    }

    private static string Position(StrategySnapshot? snap)
    {
        if (snap == null) return NoData();
        return $"You are P{snap.ClassPos} of {snap.ClassTotal} in class.";
    }

    private static string PitStops(StrategySnapshot? snap)
    {
        if (snap == null) return NoData();
        string extra = snap.ExtraStops > 0
            ? $" {snap.ExtraStops} more stop{(snap.ExtraStops > 1 ? "s" : "")} required."
            : " No additional stops needed.";
        return $"Pit stops completed: {snap.PitStops}.{extra}";
    }

    private static string LapTime(StrategySnapshot? snap)
    {
        if (snap == null) return NoData();
        if (snap.AvgLapTime <= 0) return "No lap time data yet.";
        return $"Average lap time: {LapTts(snap.AvgLapTime)}.";
    }

    private static string FuelAdd(StrategySnapshot? snap)
    {
        if (snap == null) return NoData();
        if (snap.FuelDeficit > 0.01)
            return $"Add {snap.FuelDeficit * 100:F0}% VE at your next pit. Minimum to finish: {snap.MinFinishFuel * 100:F0}%.";
        return $"No extra fuel needed. Current VE at {snap.Ve:F0}% is sufficient to finish.";
    }

    private static string LastLap(StrategySnapshot? snap)
    {
        if (snap == null) return NoData();
        if (snap.LastLapTime <= 0) return "No last lap time yet.";
        return $"Last lap: {LapTts(snap.LastLapTime)}.";
    }

    private static string BestLap(StrategySnapshot? snap)
    {
        if (snap == null) return NoData();
        if (snap.BestLapTime <= 0) return "No best lap yet.";
        return $"Personal best: {LapTts(snap.BestLapTime)}.";
    }

    private static string FullUpdate(StrategySnapshot? snap, StrategyRecommendation? rec)
    {
        if (snap == null) return NoData();
        string driver = snap.CurrentDriverName.Length > 0 ? $"{snap.CurrentDriverName}." : "";
        string pos    = $"P{snap.ClassPos} of {snap.ClassTotal}.";
        string laps   = snap.LapsRemaining > 0 ? $"{snap.LapsRemaining:F0} laps to go." : "";
        double tireHealth = snap.TireWear.Length == 4 ? snap.TireWear.Min() * 100 : snap.WorstWear * 100;
        string tires  = snap.WorstWear > 0 ? $"Tire health {tireHealth:F0}%." : "";
        string fuel   = snap.Ve > 0 ? $"VE {snap.Ve:F0}%, {snap.LapsOnVe:F1} laps." : "";
        string gapF   = snap.GapFront > 0 ? $"Gap ahead {snap.GapFront:F1}." : "";
        string gapB   = snap.GapBehind > 0 ? $"Gap behind {snap.GapBehind:F1}." : "";
        string strat  = rec != null ? rec.ActionLabel + "." : "";
        return string.Join(" ", new[] { driver, pos, laps, tires, fuel, gapF, gapB, strat }
            .Where(s => s.Length > 0));
    }

    private static string DriverName(StrategySnapshot? snap)
    {
        if (snap == null) return NoData();
        if (snap.CurrentDriverName.Length == 0) return "Driver name not available.";
        string prev = snap.PreviousDriverName.Length > 0
            ? $" Previous driver was {snap.PreviousDriverName}."
            : "";
        return $"Current driver is {snap.CurrentDriverName}.{prev}";
    }

    private static string ModeStats(StrategySnapshot? snap)
    {
        if (snap == null) return NoData();
        bool hasPush = snap.PushAvgTime > 0;
        bool hasSave = snap.SaveAvgTime > 0;
        if (!hasPush && !hasSave) return "No drive mode data recorded yet.";
        string push = hasPush
            ? $"Push: {LapTts(snap.PushAvgTime)} per lap, {snap.PushAvgFuel * 100:F2}% VE."
            : "";
        string save = hasSave
            ? $"Save: {LapTts(snap.SaveAvgTime)} per lap, {snap.SaveAvgFuel * 100:F2}% VE."
            : "";
        return string.Join(" ", new[] { push, save }.Where(s => s.Length > 0));
    }

    private static string PaceModeGain(StrategySnapshot? snap)
    {
        if (snap == null) return NoData();
        if (snap.AvgLapTime <= 0) return "No lap time data yet.";

        var parts = new List<string>();
        if (snap.PushAvgTime > 0)
        {
            double pushGain = snap.AvgLapTime - snap.PushAvgTime;
            parts.Add($"Push gains {pushGain:F2} seconds per lap.");
            if (snap.GapFront > 0 && pushGain > 0)
                parts.Add($"You would close the {snap.GapFront:F1} second gap in roughly {snap.GapFront / pushGain:F0} laps.");
        }
        if (snap.SaveAvgTime > 0)
        {
            double saveLoss = snap.SaveAvgTime - snap.AvgLapTime;
            parts.Add($"Save costs {saveLoss:F2} seconds per lap.");
        }
        return parts.Count > 0 ? string.Join(" ", parts) : "No mode data to compare.";
    }

    private static string StintLength(StrategySnapshot? snap)
    {
        if (snap == null) return NoData();
        string time = snap.StintTime > 0 ? $", {snap.StintTime / 60:F0} minutes" : "";
        return $"Current stint: {snap.StintLaps} laps{time}.";
    }

    private static string BrakeTemps(StrategySnapshot? snap)
    {
        if (snap == null) return NoData();
        if (snap.BrakeTemps.Length < 4) return "No brake temperature data.";
        double[] b = snap.BrakeTemps;
        return $"Brakes: FL {b[0]:F0}, FR {b[1]:F0}, RL {b[2]:F0}, RR {b[3]:F0} degrees.";
    }

    private static string TrackConditions(StrategySnapshot? snap)
    {
        if (snap == null) return NoData();

        string condition = WeatherHelper.ConditionLabel(snap.WeatherCondition);
        string trend = snap.WeatherTrend switch
        {
            WeatherTrend.GettingWetter => " Track getting wetter.",
            WeatherTrend.Drying        => " Track is drying.",
            _                          => "",
        };
        string compound = snap.CompoundAdvice switch
        {
            CompoundAdvice.SwitchToWets  => " Wet tyres recommended.",
            CompoundAdvice.SwitchToDries => " Track dry enough for slick tyres.",
            _                            => "",
        };

        return $"Track {snap.TrackTemp:F0} degrees, ambient {snap.AmbientTemp:F0}. Grip {snap.TrackGrip}%. Conditions {condition}.{trend}{compound}";
    }

    private static string Delta(StrategySnapshot? snap)
    {
        if (snap == null) return NoData();
        if (snap.DeltaBest == 0) return "Delta not available.";
        string sign = snap.DeltaBest > 0 ? "plus" : "minus";
        return $"Delta to best: {sign} {Math.Abs(snap.DeltaBest):F3} seconds.";
    }

    private static string HybridCharge(StrategySnapshot? snap)
    {
        if (snap == null) return NoData();
        if (snap.StateOfCharge <= 0) return "No hybrid data available.";
        return $"Battery at {snap.StateOfCharge * 100:F0}%.";
    }

    // ── Strategy handlers ─────────────────────────────────────────────────────

    private static string StrategyRec(StrategyRecommendation? rec)
    {
        if (rec == null) return NoStratData();
        string pitLap = rec.RecommendedPitLap > 0
            ? $" Pit on lap {rec.RecommendedPitLap}."
            : "";
        return $"{rec.ActionLabel}.{pitLap} {rec.Rationale}";
    }

    private static string RivalUpdate(StrategyRecommendation? rec)
    {
        if (rec == null) return NoStratData();
        if (!rec.NearestRivalGap.HasValue) return "No rival data available.";
        string gap   = $"Nearest rival {rec.NearestRivalGap.Value:F1} seconds.";
        string under = rec.UndercutThreat ? " Undercut threat." : "";
        string over  = rec.OvercutOpportunity ? " Overcut opportunity." : "";
        string pit   = rec.RivalEstimatedPitLap.HasValue
            ? $" Rival expected on lap {rec.RivalEstimatedPitLap.Value:F0}."
            : "";
        return $"{gap}{under}{over}{pit}".Trim();
    }

    private static string FuelMargin(StrategyRecommendation? rec)
    {
        if (rec == null) return NoStratData();
        if (rec.FuelSafetyMargin >= 2)
            return $"Fuel margin: {rec.FuelSafetyMargin:F1} laps. Good.";
        if (rec.FuelSafetyMargin >= 0.5)
            return $"Fuel margin: {rec.FuelSafetyMargin:F1} laps. Tight.";
        return $"Fuel margin: {rec.FuelSafetyMargin:F1} laps. Critical.";
    }

    private static string TireCliff(StrategyRecommendation? rec)
    {
        if (rec == null) return NoStratData();
        string level = rec.TireCliffRisk switch
        {
            > 0.66 => "HIGH",
            > 0.33 => "MEDIUM",
            _      => "LOW",
        };
        return $"Tire cliff risk: {level}.";
    }

    private static string ProjectedPosition(StrategyRecommendation? rec)
    {
        if (rec == null) return NoStratData();
        return $"Projected finish position: P{rec.ProjectedPosition}.";
    }

    // ── Lap time formatter (natural speech) ───────────────────────────────────

    private static string LapTts(double s)
    {
        if (s <= 0) return "no data";
        int    m   = (int)(s / 60);
        double rem = s - m * 60;
        return m > 0 ? $"{m} minute{(m > 1 ? "s" : "")} {rem:F1}" : $"{s:F1} seconds";
    }

    private static bool IsStrategyIntent(string intent) => intent is
        "pit_window" or "strategy_rec" or "rival_update" or
        "fuel_margin" or "tire_cliff"  or "projected_position" or
        "full_update";
}
