using LmuRaceEngineerCS.Models;
using LmuRaceEngineerCS.Shared;
using LmuRaceEngineerCS.Strategy;
using System.Diagnostics;

namespace LmuRaceEngineerCS.Services;

/// <summary>
/// Background polling service. Reads LMU shared memory, updates RaceState,
/// computes a StrategySnapshot, and fires voice alerts via AudioService.
/// Direct C# port of race_engineer.py RaceEngineer.tick() / update().
/// </summary>
public sealed class TelemetryService : IDisposable
{
    // ── Constants ─────────────────────────────────────────────────────────────
    private const int    PollMs            = 500;     // 2 Hz
    private const double FuelSafetyLaps    = 1.0;
    private const double VeFuelRatio       = 0.95;
    private const double ScanWindowSecs    = 45.0;    // faster-class scan

    private static readonly HashSet<int> RaceSessions = [10, 11, 12, 13];

    private static readonly Dictionary<int, string> SessionNames = new()
    {
        [0]  = "Test Day",
        [1]  = "Practice 1", [2]  = "Practice 2", [3]  = "Practice 3", [4]  = "Practice 4",
        [5]  = "Qual 1",     [6]  = "Qual 2",     [7]  = "Qual 3",     [8]  = "Qual 4",
        [9]  = "Warmup",
        [10] = "Race",       [11] = "Race 2",     [12] = "Race 3",     [13] = "Race 4",
    };

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly MMapReader     _mmap;
    private readonly RaceState      _state    = new();
    private readonly LapLogger      _lapLog   = new();
    private readonly AudioService   _audio;
    private readonly EdgeTtsService _tts;

    private StrategySnapshot? _snapshot;
    private readonly object   _lock = new();

    private Thread?  _pollThread;
    private volatile bool _running;

    private StrategyEngine _strategyEngine = new();

    // Alert guard timestamps (monotonic seconds)
    private double _lapInvalidAlerted;
    private double _trackLimitAlerted;

    // ── Events ────────────────────────────────────────────────────────────────
    public event EventHandler<StrategySnapshot>?      SnapshotUpdated;
    public event EventHandler<StrategyRecommendation>? RecommendationUpdated;

    // ── Constructor ───────────────────────────────────────────────────────────
    public TelemetryService(AudioService audio, EdgeTtsService tts)
    {
        _audio = audio;
        _tts   = tts;
        _mmap  = new MMapReader();
    }

    // ── Public API ────────────────────────────────────────────────────────────
    public bool   IsConnected      => _mmap.IsOpen;
    public bool   IsLayoutSuspect  => _mmap.LayoutSuspect;
    public string LayoutInfo       => _mmap.LayoutInfo;
    public int    GameVersion      => _mmap.GameVersion;
    public string DriveMode        => _state.DriveMode;
    public string DiagnosticStatus { get; private set; } = "Searching for LMU_Data…";

    private int _lastLoggedVersion = -1;   // log the shared-memory layout once per game version
    private volatile int _playerTelemIdx = -1;

    /// <summary>
    /// Cheap, high-rate read of just the player's pedal inputs. The main poll runs at 2 Hz,
    /// which is far too coarse to draw a pedal trace, so the pedal overlay samples this
    /// directly at ~30 Hz. Reads a single telemetry struct — no snapshot rebuild.
    /// Returns false when there is nothing trustworthy to read.
    /// </summary>
    public bool TryReadPedals(out double throttle, out double brake)
    {
        throttle = brake = 0;
        int idx = _playerTelemIdx;
        if (idx < 0 || !_mmap.IsOpen || _mmap.LayoutSuspect) return false;
        try
        {
            var t = _mmap.ReadTelemetry(idx);
            throttle = Math.Clamp(t.mFilteredThrottle, 0, 1);
            brake    = Math.Clamp(t.mFilteredBrake,    0, 1);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Cheap, high-rate read of the per-wheel longitudinal velocities (FL FR RL RR) plus car
    /// speed, for the lockup / wheelspin overlay. Lockups are routinely shorter than the 2 Hz
    /// poll interval, so that overlay samples this directly at ~30 Hz. Caller supplies the
    /// arrays so nothing is allocated per frame. Returns false when there is nothing to read.
    /// </summary>
    public bool TryReadSlip(double[] patchOut, double[] groundOut, out double speedKph)
    {
        speedKph = 0;
        int idx = _playerTelemIdx;
        if (idx < 0 || !_mmap.IsOpen || _mmap.LayoutSuspect) return false;
        if (patchOut.Length < 4 || groundOut.Length < 4) return false;
        try
        {
            var t = _mmap.ReadTelemetry(idx);
            for (int i = 0; i < 4; i++)
            {
                var w = WheelN(ref t, i);
                patchOut[i]  = w.mLongitudinalPatchVel;
                groundOut[i] = w.mLongitudinalGroundVel;
            }
            speedKph = Math.Sqrt(t.mLocalVel.x * t.mLocalVel.x
                               + t.mLocalVel.y * t.mLocalVel.y
                               + t.mLocalVel.z * t.mLocalVel.z) * 3.6;
            return true;
        }
        catch { return false; }
    }

    public StrategySnapshot? Snapshot()
    {
        lock (_lock) { return _snapshot; }
    }

    public StrategyRecommendation? LatestRecommendation() => _strategyEngine.Latest();
    public void ForceStrategyCompute() => _strategyEngine.ForceCompute();

    public void SetDriveMode(string mode) => _state.DriveMode = mode;

    public void Start()
    {
        _strategyEngine.RecommendationUpdated += OnRecommendationUpdated;
        _strategyEngine.Start(_mmap, Snapshot, App.Settings.StrategyIntervalMs);

        _running     = true;
        _pollThread  = new Thread(PollLoop) { IsBackground = true, Name = "TelemetryPoll" };
        _pollThread.Start();
    }

    public void Stop()
    {
        _strategyEngine.Stop();
        _running = false;
        _mmap.Dispose();
    }

    private void OnRecommendationUpdated(object? sender, StrategyRecommendation rec)
        => RecommendationUpdated?.Invoke(this, rec);

    // ── Poll loop ─────────────────────────────────────────────────────────────
    private void PollLoop()
    {
        // Drop process priority (match Python behavior)
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; }
        catch { /* ignore */ }

        while (_running)
        {
            Thread.Sleep(PollMs);
            try { Tick(); }
            catch { /* keep running on transient errors */ }
        }
    }

    private void Tick()
    {
        if (!_mmap.IsOpen)
        {
            _mmap.TryOpen();
            DiagnosticStatus = _mmap.IsOpen ? "Connected — loading session…"
                : _mmap.LastOpenError == "access_denied"
                    ? "Access denied — right-click Race Engineer → Run as Administrator."
                : _mmap.LastOpenError == "not_found"
                    ? "LMU not found. Start LMU, go to Settings → Plugins, enable Shared Memory, then load a session."
                : _mmap.LastOpenError.Length > 0
                    ? $"Cannot open LMU shared memory: {_mmap.LastOpenError}."
                    : "Searching for LMU shared memory…";
            return;
        }

        if (!_mmap.GameRunning())
        {
            DiagnosticStatus = "LMU_Data found — waiting for session to start…";
            return;
        }

        // Future-proofing: bail out (rather than read garbage) if a game update ever shifts
        // the shared-memory layout out from under our hardcoded struct offsets.
        _mmap.ValidateLayout();
        if (_mmap.GameVersion != _lastLoggedVersion)
        {
            _lastLoggedVersion = _mmap.GameVersion;
            System.Diagnostics.Debug.WriteLine($"[LMU shared-memory] {_mmap.LayoutInfo}");
        }
        if (_mmap.LayoutSuspect)
        {
            DiagnosticStatus = "⚠ Shared-memory layout unexpected — this LMU version likely "
                             + $"needs an app update. Telemetry paused. [{_mmap.LayoutInfo}]";
            lock (_lock) { _snapshot = null; }
            return;
        }

        var (activeVeh, headerIdx, playerHasVehicle) = _mmap.ReadTelemHeader();

        unsafe
        {
            var ses = _mmap.ReadScoringInfo();
            int numVehicles = ses.mNumVehicles;
            if (numVehicles == 0)
            {
                DiagnosticStatus = "Session active — waiting for cars to load…";
                return;
            }

            // Resolve which vehicle is ours.
            //
            // Driver mode (playerHasVehicle=true): headerIdx is authoritative.
            //
            // Engineer/spectator mode (playerHasVehicle=false): LMU only populates
            // full tire telemetry for the assigned car; other slots are zeroed.
            // Scan for the entry whose front-tire carcass temperature has a real
            // value (raw double = °C + 275.15, so anything > 200 is real data;
            // zero means the slot is unpopulated).  Fall back to mVirtualEnergy
            // for the very start of a session before tires reach temperature.
            int playerIdx = -1;

            if (!playerHasVehicle)
            {
                for (int i = 0; i < numVehicles; i++)
                {
                    var tv = _mmap.ReadTelemetry(i);
                    bool hasTires = WheelN(ref tv, 0).mTireCarcassTemperature > 200.0
                                 || WheelN(ref tv, 1).mTireCarcassTemperature > 200.0;
                    bool hasFuel  = tv.mVirtualEnergy > 0.001f;
                    if (hasTires || hasFuel) { playerIdx = i; break; }
                }
            }

            // Driver mode, or engineer scan found nothing: trust the header index.
            if (playerIdx < 0 && headerIdx < numVehicles)
                playerIdx = headerIdx;

            // Last resort: mIsPlayer flag.
            if (playerIdx < 0)
            {
                for (int i = 0; i < numVehicles; i++)
                {
                    var v = _mmap.ReadVehicleScoring(i);
                    if (v.mIsPlayer != 0) { playerIdx = i; break; }
                }
            }

            if (playerIdx < 0)
            {
                DiagnosticStatus = $"Session active ({numVehicles} cars) — " +
                                   $"no car assigned (headerIdx={headerIdx}, activeVeh={activeVeh}). " +
                                   "Select your team car in LMU.";
                return;
            }

            _playerTelemIdx = playerIdx;   // cached for TryReadPedals (high-rate overlays)

            var t  = _mmap.ReadTelemetry(playerIdx);
            var sc = _mmap.ReadVehicleScoring(playerIdx);

            double gapFront  = t.mTimeGapPlaceAhead  > 0 ? (double)t.mTimeGapPlaceAhead  : 0.0;
            double gapBehind = t.mTimeGapPlaceBehind > 0 ? (double)t.mTimeGapPlaceBehind : 0.0;

            Update(ref t, ref sc, ref ses, gapFront, gapBehind);

            // ── Proactive alerts ──────────────────────────────────────────────
            double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

            bool nowInvalid = t.mLapInvalidated != 0;
            if (nowInvalid && now - _lapInvalidAlerted > 30.0)
            {
                _tts.Speak("lap_invalid");
                _lapInvalidAlerted = now;
            }

            int perPen = ses.mTrackLimitsStepsPerPenalty;
            int steps  = t.mTrackLimitsSteps;
            if (perPen > 0 && steps > 0)
            {
                int remaining = perPen - (steps % perPen);
                if (remaining == 1 && now - _trackLimitAlerted > 30.0)
                {
                    _tts.Speak("track_limit");
                    _trackLimitAlerted = now;
                }
            }

            // ── Build snapshot ────────────────────────────────────────────────
            string trackNameCached = DecodeScoringTrack(ref ses); // decode once per tick (#18)
            string playerClass = DecodeScoringClass(ref sc);

            var classVehicles = CollectClassVehicles(ref ses, playerClass, numVehicles);
            classVehicles.Sort((a, b) => a.place.CompareTo(b.place));

            int classIdx = classVehicles.FindIndex(v => v.isPlayer);
            int classPos = classIdx >= 0 ? classIdx + 1 : 1;
            int leaderLap = classVehicles.Count > 0 ? classVehicles[0].totalLaps + 1 : t.mLapNumber;

            double avg  = _state.ActiveAvgFuel;
            double fuel = t.mVirtualEnergy;
            double ve   = fuel * 100.0;
            double rt   = ses.mEndET - ses.mCurrentET;
            double avlt = _state.ActiveAvgTime;
            if (avlt <= 0 && sc.mEstimatedLapTime > 10) avlt = sc.mEstimatedLapTime;

            // mWear is tread REMAINING (1.0 = new), so the WORST corner is the one with
            // the LEAST tread left. WorstWear carries that remaining-life figure.
            double ww = 1.0; for (int i = 0; i < 4; i++) ww = Math.Min(ww, WheelN(ref t, i).mWear);
            // Average of the two most-worn tyres for a more robust wear RATE — per-lap
            // tread LOSS (positive), as produced by RaceState.OnNewLap (#9).
            var sortedWr = _state.WearRate.OrderByDescending(w => w).ToArray();
            double wr = sortedWr.Length >= 2
                ? (sortedWr[0] + sortedWr[1]) / 2.0
                : sortedWr.Length == 1 ? sortedWr[0] : 0;
            // TireLaps (display only): laps until the WORST corner falls from its current
            // remaining tread down to this driver's pace-cliff threshold — NOT until it is
            // fully bald. cliffRemaining is remaining-life at the cliff (onset 0.60 → 0.15).
            // The strategy engine recomputes its own rate for pit decisions; this is the
            // dash readout, so it may lag the engine slightly and is not decision-critical.
            double onsetForDisplay = App.Learner.GetOnset(trackNameCached);
            double cliffRemaining  = RaceInput.PersonalCliffThreshold(
                onsetForDisplay > 0 ? onsetForDisplay : 0.60);
            double? tl = wr > 0 ? Math.Max(0.0, (ww - cliffRemaining) / wr) : null;

            int    ml  = ses.mMaxLaps;
            bool   vlr = ml > 0 && ml <= 500;
            int    cur = t.mLapNumber;
            double lr, fl;

            if (rt > 30 && avlt > 10)
            {
                double lbyt = rt / avlt;
                fl = Math.Ceiling(leaderLap + lbyt) - Math.Max(0, leaderLap - cur);
                lr = lbyt;
            }
            else if (vlr)
            {
                lr = Math.Max(0, ml - cur);
                fl = (int)lr + (int)FuelSafetyLaps;
            }
            else { lr = 0; fl = 0; }

            double lve = avg > 0 ? fuel / avg : 0;
            double fn  = fl > 0 && avg > 0 ? fl * avg : 0;
            double fd  = Math.Max(0, fn - fuel);
            int    es  = fd > 0 ? (int)Math.Ceiling(fd / VeFuelRatio) : 0;

            // Pit window
            double?  pitLap     = null;
            string?  pitLimiter = null;
            double?  tireEntry  = tl;
            double?  veEntry    = avg > 0 ? lve : null;
            var candidates = new Dictionary<string, double>();
            if (veEntry.HasValue)  candidates["energy"] = veEntry.Value;
            if (tireEntry.HasValue) candidates["tires"] = tireEntry.Value;
            if (candidates.Count > 0)
            {
                var best = candidates.MinBy(kv => kv.Value);
                pitLap     = cur + best.Value;
                pitLimiter = best.Key;
            }

            // Stint
            double stintTime      = _state.StintStartTime > 0 ? Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency - _state.StintStartTime : 0;
            double? predictedStint = null;
            var pCands = new List<double>();
            if (avg > 0) pCands.Add(lve);
            if (tl.HasValue) pCands.Add(tl.Value);
            if (pCands.Count > 0) predictedStint = pCands.Min();
            double? latestPit = predictedStint.HasValue ? cur + predictedStint.Value : null;
            double  earliestPit = cur + Math.Max(1, 3 - _state.LapsOnTires);

            // Fuel strategy extras
            double avg3        = _state.AvgFuel3Lap;
            double avg10       = _state.AvgFuel10Lap;
            double saveTarget  = avg > 0 && lr > 1 && fuel > 0 ? Math.Max(0, avg - fuel / lr) : 0;
            double minFinish   = avg * FuelSafetyLaps;

            // Strategy mode
            string stratMode;
            if      (_state.DriveMode == "push") stratMode = "push";
            else if (_state.DriveMode == "save") stratMode = "save";
            else if (lve > 0 && lr > 0 && lve > lr + 3) stratMode = "extend";
            else stratMode = "target";

            // Faster-class traffic
            double? fasterGap = FasterClassInfo(ref ses, numVehicles, playerClass, sc.mTimeBehindLeader);

            // Tire temps (convert from Kelvin offset: game stores C+275.15)
            var tireTemps = new double[4];
            for (int i = 0; i < 4; i++)
                tireTemps[i] = WheelN(ref t, i).mTireCarcassTemperature - 275.15;

            var tireWear = new double[4];
            var brakeTemps = new double[4];
            var compounds = new int[4];
            // Per-wheel detail for the overlay widgets
            var pressures = new double[4];
            var gripFract = new double[4];
            var tInner    = new double[4];
            var tMiddle   = new double[4];
            var tOuter    = new double[4];
            var tOptimal  = new double[4];
            var patchVel  = new double[4];
            var groundVel = new double[4];
            var flat      = new bool[4];
            var detached  = new bool[4];
            for (int i = 0; i < 4; i++)
            {
                var w = WheelN(ref t, i);
                tireWear[i]   = w.mWear;
                brakeTemps[i] = w.mBrakeTemp;
                compounds[i]  = w.mCompoundType;

                pressures[i]  = w.mPressure;
                gripFract[i]  = w.mGripFract;
                // mTemperature[0..2] = inner / middle / outer, stored as °C + 275.15
                tInner[i]     = w.mTemperature[0] - 275.15;
                tMiddle[i]    = w.mTemperature[1] - 275.15;
                tOuter[i]     = w.mTemperature[2] - 275.15;
                tOptimal[i]   = w.mOptimalTemp;
                patchVel[i]   = w.mLongitudinalPatchVel;
                groundVel[i]  = w.mLongitudinalGroundVel;
                flat[i]       = w.mFlat != 0;
                detached[i]   = w.mDetached != 0;
            }

            // Speed from the local velocity vector (m/s → km/h)
            double speedKph = Math.Sqrt(t.mLocalVel.x * t.mLocalVel.x
                                      + t.mLocalVel.y * t.mLocalVel.y
                                      + t.mLocalVel.z * t.mLocalVel.z) * 3.6;

            // Update weather trend history before building snapshot
            _state.UpdateWeather(ses.mRaining, ses.mAvgPathWetness);

            var snap = new StrategySnapshot
            {
                Ve            = ve,
                LapsOnVe      = lve,
                RsFuel        = _state.RaceStartFuel,
                AvgFuel       = avg,
                AvgFuel3      = avg3,
                AvgFuel10     = avg10,
                FuelSaveTarget = saveTarget,
                MinFinishFuel = minFinish,
                FuelDeficit   = fd,
                ExtraStops    = es,
                FuelLaps      = (int)fl,
                IsRace        = RaceSessions.Contains(ses.mSession),
                AvgLapTime    = avlt,
                LastLapTime   = sc.mLastLapTime,
                BestLapTime   = sc.mBestLapTime,
                GapFront      = gapFront,
                GapBehind     = gapBehind,
                AvgLapDeltaFront  = _state.AvgLapDeltaFront,
                AvgLapDeltaBehind = _state.AvgLapDeltaBehind,
                LapsRemaining     = lr,
                SessionTimeRemaining = Math.Max(0, rt),
                TotalLapsEstimate    = lr > 0 ? leaderLap + lr : 0,
                CurrentLap  = cur,
                TrackName   = trackNameCached,
                Session     = ses.mSession,
                ClassPos    = classPos,
                ClassTotal  = classVehicles.Count,
                PitStops    = sc.mNumPitstops,
                PitLap      = pitLap,
                PitLimiter  = pitLimiter,
                EarliestPit = earliestPit,
                LatestPit   = latestPit,
                StintLaps   = _state.LapsOnTires,
                StintTime   = stintTime,
                PredictedStint = predictedStint,
                StrategyMode = stratMode,
                DriveMode    = _state.DriveMode,
                PushAvgFuel  = _state.ModeAvgFuel("push"),
                SaveAvgFuel  = _state.ModeAvgFuel("save"),
                PushAvgTime  = _state.ModeAvgTime("push"),
                SaveAvgTime  = _state.ModeAvgTime("save"),
                WorstWear    = ww,
                TireLaps     = tl,
                TireWear     = tireWear,
                TireTempsC   = tireTemps,
                CompoundTypes = compounds,
                BrakeTemps   = brakeTemps,
                StateOfCharge = t.mStateOfCharge,
                Regen         = t.mRegen,
                AbsActive     = t.mABSActive != 0,
                TcActive      = t.mTCActive  != 0,
                LapInvalid    = t.mLapInvalidated != 0,
                TrackLimitsSteps      = t.mTrackLimitsSteps,
                TrackLimitsPerPenalty = ses.mTrackLimitsStepsPerPenalty,

                // Driver inputs + per-wheel detail (overlay widgets)
                Throttle = t.mFilteredThrottle,
                Brake    = t.mFilteredBrake,
                Clutch   = t.mFilteredClutch,
                Steering = t.mFilteredSteering,
                Gear     = t.mGear,
                SpeedKph = speedKph,
                TirePressures  = pressures,
                GripFract      = gripFract,
                TireTempInner  = tInner,
                TireTempMiddle = tMiddle,
                TireTempOuter  = tOuter,
                TireOptimalC   = tOptimal,
                PatchVelLong   = patchVel,
                GroundVelLong  = groundVel,
                TireFlat       = flat,
                WheelDetached  = detached,

                FasterClassGap = fasterGap,
                LastSector1 = sc.mLastSector1,
                LastSector2 = sc.mLastSector2,
                BestSector1 = sc.mBestLapSector1,
                BestSector2 = sc.mBestLapSector2,
                DeltaBest   = t.mDeltaBest,
                DeltaLast   = avlt > 0 && sc.mLastLapTime > 10
                    ? sc.mLastLapTime - avlt : 0,
                TrackGrip     = ses.mTrackGripLevel,
                CloudCoverage = ses.mCloudCoverage,
                AmbientTemp   = ses.mAmbientTemp,
                TrackTemp     = ses.mTrackTemp,
                Raining       = ses.mRaining,
                AvgPathWetness  = ses.mAvgPathWetness,
                DarkCloud       = ses.mDarkCloud,
                WeatherTrend    = _state.WeatherTrend,
                WeatherCondition = WeatherHelper.Classify(ses.mRaining, ses.mAvgPathWetness),
                CompoundAdvice   = WeatherHelper.GetCompoundAdvice(
                    WeatherHelper.Classify(ses.mRaining, ses.mAvgPathWetness),
                    _state.WeatherTrend,
                    ses.mRaining, ses.mAvgPathWetness, ses.mDarkCloud),
                CurrentDriverName  = _state.CurrentDriverName,
                PreviousDriverName = _state.PreviousDriverName,
                PersonalDegOnset   = App.Learner.GetOnset(trackNameCached),
                PersonalDegActive  = App.Learner.IsProfileActive(trackNameCached),
            };

            string driverLabel = _state.CurrentDriverName.Length > 0
                ? $" · {_state.CurrentDriverName}" : "";
            DiagnosticStatus = $"Connected{driverLabel}";

            lock (_lock) { _snapshot = snap; }
            SnapshotUpdated?.Invoke(this, snap);

        }
    }

    // ── State update (mirrors Python update()) ────────────────────────────────
    private unsafe void Update(ref LmuVehicleTelemetry t, ref LmuVehicleScoring sc,
                               ref LmuScoringInfo ses, double gapFront, double gapBehind)
    {
        int    lap  = t.mLapNumber;
        double fuel = t.mVirtualEnergy;
        var    wear = new double[4];
        for (int i = 0; i < 4; i++) wear[i] = WheelN(ref t, i).mWear;
        int sessId = ses.mSession;

        if (sessId != _state.LastSession)
        {
            // Re-learn profile if the session that just ended was a race (#17)
            if (RaceSessions.Contains(_state.LastSession) && _state.LastSession >= 0)
            {
                string tn = DecodeScoringTrack(ref ses);
                string sn = ProfileLearner.SafeName(tn);
                Task.Run(() => App.Learner.LearnTrack(sn));
            }

            _state.RaceStartFuel     = -1.0;
            _state.RaceThrottleFired = false;
            _state.FormationSkipped  = false;   // re-arm formation-lap skip for the new session
            _state.LastSession       = sessId;

            // Start a new lap-log session on session change only (#40)
            _lapLog.StartSession(
                DecodeScoringTrack(ref ses),
                DecodeTelemVehicleName(ref t),
                SessionNames.GetValueOrDefault(sessId, $"Session {sessId}"));
        }

        if (RaceSessions.Contains(sessId) && fuel > 0)
        {
            if (!_state.RaceThrottleFired)
            {
                _state.RaceStartFuel = fuel;
                if (t.mFilteredThrottle > 0)
                {
                    _state.RaceThrottleFired = true;
                    // Initialise StintStartTime for the opening stint (#26)
                    if (_state.StintStartTime < 0)
                        _state.StintStartTime = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                }
            }
        }

        bool inPits = sc.mInPits != 0;
        if (_state.WasInPits && !inPits)
        {
            _state.OnPitExit(fuel, wear);

            // Driver swap detection: compare driver name on pit exit
            string exitDriver = DecodeDriverName(ref sc);
            if (_state.CurrentDriverName.Length > 0
                && exitDriver.Length > 0
                && exitDriver != _state.CurrentDriverName)
            {
                _state.PreviousDriverName = _state.CurrentDriverName;
                _state.CurrentDriverName  = exitDriver;
                _state.ClearStintStats();
                // Speak short surname for brevity
                string shortName = exitDriver.Contains(' ')
                    ? exitDriver[(exitDriver.LastIndexOf(' ') + 1)..]
                    : exitDriver;
                _tts.Speak($"Driver swap. {shortName} is in the car.");
            }
            else if (exitDriver.Length > 0)
            {
                _state.CurrentDriverName = exitDriver;
            }
        }
        _state.WasInPits = inPits;

        // Seed driver name on first tick so it is never blank
        if (_state.CurrentDriverName.Length == 0)
            _state.CurrentDriverName = DecodeDriverName(ref sc);

        if (lap != _state.PrevLap && lap > 0)
        {
            // Don't log the formation / pit-out lap as a racing lap. Skip exactly one such
            // lap per race session, detected two ways:
            //   • standing start — the game's very first lap (PrevLap == 1), or
            //   • pit / formation start — the lap counter counts formation laps then RESETS
            //     to 1 at the green flag, so the completing lap number drops (lap < PrevLap).
            bool counterReset = _state.PrevLap > 0 && lap < _state.PrevLap;
            bool isFormation  = RaceSessions.Contains(sessId)
                                && !_state.FormationSkipped
                                && (counterReset || _state.PrevLap == 1);

            if (isFormation)
            {
                _state.FormationSkipped = true;
                _state.WearRate = new double[4]; // clear stale wear rate on formation lap (#7)
            }

            if (!isFormation && _state.PrevLap > 0 && _state.LapStartFuel >= 0)
            {
                _lapLog.LogLap(
                    lapNum:    _state.PrevLap,
                    lapTime:   sc.mLastLapTime,
                    s1:        sc.mLastSector1,
                    s2Cum:     sc.mLastSector2,
                    fuelStart: _state.LapStartFuel,
                    fuelUsed:  _state.LapStartFuel - fuel,   // VE at lap start − VE now
                    wearStart: _state.LapStartWear,
                    mode:      _state.DriveMode);
            }
            _state.OnNewLap(lap, sc.mLastLapTime, fuel, wear, record: !isFormation);
        }

        int sector = sc.mSector;
        if (sector != _state.PrevSector)
            _state.OnSectorChange(sector, gapFront, gapBehind);
    }

    // ── Helper: collect same-class vehicles ───────────────────────────────────
    private unsafe List<(int totalLaps, byte place, bool isPlayer)> CollectClassVehicles(
        ref LmuScoringInfo ses, string playerClass, int numVehicles)
    {
        var result = new List<(int, byte, bool)>();
        for (int i = 0; i < numVehicles; i++)
        {
            var v = _mmap.ReadVehicleScoring(i);
            if (v.mPlace == 0) continue;
            string vc = DecodeScoringClass(ref v);
            if (vc == playerClass)
                result.Add((v.mTotalLaps, v.mPlace, v.mIsPlayer != 0));
        }
        return result;
    }

    // ── Helper: faster-class detection ───────────────────────────────────────
    private unsafe double? FasterClassInfo(ref LmuScoringInfo ses, int numVehicles,
                                           string playerClass, double playerTimeBehindLeader)
    {
        double? closest = null;
        for (int i = 0; i < numVehicles; i++)
        {
            var v = _mmap.ReadVehicleScoring(i);
            if (v.mIsPlayer != 0 || v.mInPits != 0) continue;
            string vc = DecodeScoringClass(ref v);
            if (vc == playerClass) continue;
            double gap = v.mTimeBehindLeader - playerTimeBehindLeader;
            if (gap > 0 && gap < ScanWindowSecs)
            {
                if (!closest.HasValue || gap < closest.Value)
                    closest = gap;
            }
        }
        return closest;
    }

    // ── Unsafe struct helpers ─────────────────────────────────────────────────
    private static LmuWheel WheelN(ref LmuVehicleTelemetry t, int n)
        => n switch { 0 => t.mWheel0, 1 => t.mWheel1, 2 => t.mWheel2, _ => t.mWheel3 };

    private static unsafe string DecodeScoringClass(ref LmuVehicleScoring sc)
    {
        fixed (byte* p = sc.mVehicleClass) return MMapReader.DecodeBytes(p, 32);
    }

    private static unsafe string DecodeDriverName(ref LmuVehicleScoring sc)
    {
        fixed (byte* p = sc.mDriverName) return MMapReader.DecodeBytes(p, 32);
    }

    private static unsafe string DecodeTelemVehicleName(ref LmuVehicleTelemetry t)
    {
        fixed (byte* p = t.mVehicleName) return MMapReader.DecodeBytes(p, 64);
    }

    private static unsafe string DecodeScoringTrack(ref LmuScoringInfo ses)
    {
        fixed (byte* p = ses.mTrackName) return MMapReader.DecodeBytes(p, 64);
    }

    // ── IDisposable ───────────────────────────────────────────────────────────
    public void Dispose()
    {
        Stop();
        _strategyEngine.Dispose();
        _mmap.Dispose();
        _lapLog.Dispose();
    }
}
