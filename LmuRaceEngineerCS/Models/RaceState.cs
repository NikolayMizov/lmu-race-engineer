namespace LmuRaceEngineerCS.Models;

/// <summary>
/// Mutable race state updated each polling cycle. Direct port of Python RaceState dataclass.
/// Not thread-safe — only mutated on the telemetry thread.
/// </summary>
public class RaceState
{
    private static int FuelLapHistory => App.Settings.FuelLapHistory;
    private const int RecordMaxLen      = 15;
    private const int ModeLapMaxLen     = 10;
    private const int GapHistoryLen     = 6;
    private const int WeatherHistoryLen = 20;

    // ── Lap detection ─────────────────────────────────────────────────────────
    public int    PrevLap       { get; set; }
    public bool   FormationSkipped { get; set; }   // true once the race's formation/out lap was dropped
    public double LapStartFuel  { get; set; } = -1.0;
    public double[] LapStartWear { get; set; } = new double[4];

    // ── Race-start reference ──────────────────────────────────────────────────
    public double RaceStartFuel    { get; set; } = -1.0;
    public bool   RaceThrottleFired { get; set; }
    public int    LastSession       { get; set; } = -1;

    // ── Stint counters ────────────────────────────────────────────────────────
    public int  LapsOnTires  { get; set; }
    public int  PitCount     { get; set; }
    public bool WasInPits    { get; set; }

    // ── Weather trend ─────────────────────────────────────────────────────────
    private readonly Queue<(double Raining, double AvgWetness)> _weatherHistory = new(WeatherHistoryLen);
    public WeatherTrend WeatherTrend { get; private set; } = WeatherTrend.Stable;

    public void UpdateWeather(double raining, double avgWetness)
    {
        if (_weatherHistory.Count >= WeatherHistoryLen) _weatherHistory.Dequeue();
        _weatherHistory.Enqueue((raining, avgWetness));

        if (_weatherHistory.Count < 6) { WeatherTrend = WeatherTrend.Stable; return; }

        var vals     = _weatherHistory.ToArray();
        int half     = vals.Length / 2;
        double recent = vals.TakeLast(half).Average(v => v.Raining + v.AvgWetness);
        double older  = vals.Take(half).Average(v => v.Raining + v.AvgWetness);
        double delta  = recent - older;

        WeatherTrend = delta >  0.06 ? WeatherTrend.GettingWetter
                     : delta < -0.06 ? WeatherTrend.Drying
                     : WeatherTrend.Stable;
    }

    // ── Driver identity ───────────────────────────────────────────────────────
    public string CurrentDriverName  { get; set; } = "";
    public string PreviousDriverName { get; set; } = "";

    /// <summary>
    /// Called on driver swap: wipes per-stint lap/fuel averages so the new
    /// driver's data starts fresh, but leaves session-level counters intact.
    /// </summary>
    public void ClearStintStats()
    {
        _lapRecords.Clear();
        foreach (var q in _modeLaps.Values) q.Clear();
        WearRate = new double[4];
        for (int i = 0; i < 4; i++) _wearHistory[i].Clear();
    }

    // ── Drive mode ────────────────────────────────────────────────────────────
    public string DriveMode { get; set; } = "normal";

    private readonly Dictionary<string, Queue<LapRecord>> _modeLaps = new()
    {
        ["normal"] = new Queue<LapRecord>(ModeLapMaxLen),
        ["push"]   = new Queue<LapRecord>(ModeLapMaxLen),
        ["save"]   = new Queue<LapRecord>(ModeLapMaxLen),
    };

    // ── Lap history ───────────────────────────────────────────────────────────
    private readonly Queue<LapRecord> _lapRecords = new(RecordMaxLen);
    public IReadOnlyCollection<LapRecord> LapRecords => _lapRecords;

    public void AddLapRecord(LapRecord r)
    {
        if (_lapRecords.Count >= RecordMaxLen) _lapRecords.Dequeue();
        _lapRecords.Enqueue(r);
        var q = _modeLaps[r.Mode];
        if (q.Count >= ModeLapMaxLen) q.Dequeue();
        q.Enqueue(r);
    }

    // ── Wear rates ────────────────────────────────────────────────────────────
    public double[] WearRate { get; set; } = new double[4];

    private readonly Queue<double>[] _wearHistory =
        Enumerable.Range(0, 4).Select(_ => new Queue<double>(3)).ToArray();

    // ── Stint timing ─────────────────────────────────────────────────────────
    public double StintStartTime { get; set; } = -1.0;
    public int    StintStartLap  { get; set; }

    // ── Gap tracking ─────────────────────────────────────────────────────────
    public int    PrevSector      { get; set; } = -1;
    public double SectorGapFront  { get; set; }
    public double SectorGapBehind { get; set; }
    public double GapFrontDelta   { get; set; }
    public double GapBehindDelta  { get; set; }

    private readonly Queue<double> _gapFrontHistory  = new(GapHistoryLen);
    private readonly Queue<double> _gapBehindHistory = new(GapHistoryLen);

    public void OnSectorChange(int sector, double gapFront, double gapBehind)
    {
        if (PrevSector >= 0)
        {
            if (SectorGapFront  > 0) GapFrontDelta  = gapFront  - SectorGapFront;
            if (SectorGapBehind > 0) GapBehindDelta = gapBehind - SectorGapBehind;
        }
        if (sector == 0 && PrevSector == 2)
        {
            if (gapFront  > 0) { if (_gapFrontHistory.Count  >= GapHistoryLen) _gapFrontHistory.Dequeue();  _gapFrontHistory.Enqueue(gapFront); }
            if (gapBehind > 0) { if (_gapBehindHistory.Count >= GapHistoryLen) _gapBehindHistory.Dequeue(); _gapBehindHistory.Enqueue(gapBehind); }
        }
        PrevSector      = sector;
        SectorGapFront  = gapFront;
        SectorGapBehind = gapBehind;
    }

    // ── Computed properties ───────────────────────────────────────────────────
    public double AvgLapDeltaFront
    {
        get
        {
            var vals = _gapFrontHistory.ToArray();
            if (vals.Length < 2) return 0;
            var diffs = Enumerable.Range(0, vals.Length - 1).Select(i => vals[i + 1] - vals[i]).ToArray();
            return diffs.Average();
        }
    }

    public double AvgLapDeltaBehind
    {
        get
        {
            var vals = _gapBehindHistory.ToArray();
            if (vals.Length < 2) return 0;
            var diffs = Enumerable.Range(0, vals.Length - 1).Select(i => vals[i + 1] - vals[i]).ToArray();
            return diffs.Average();
        }
    }

    public double AvgFuelPerLap
    {
        get
        {
            var vals = _lapRecords.Select(r => r.FuelUsed).Where(f => f > 0).TakeLast(FuelLapHistory).ToArray();
            return vals.Length == 0 ? 0 : vals.Average();
        }
    }

    public double AvgFuel3Lap
    {
        get
        {
            var vals = _lapRecords.Select(r => r.FuelUsed).Where(f => f > 0).TakeLast(3).ToArray();
            return vals.Length == 0 ? 0 : vals.Average();
        }
    }

    public double AvgFuel10Lap
    {
        get
        {
            var vals = _lapRecords.Select(r => r.FuelUsed).Where(f => f > 0).TakeLast(10).ToArray();
            return vals.Length == 0 ? 0 : vals.Average();
        }
    }

    public double AvgLapTime
    {
        get
        {
            var vals = _lapRecords.Select(r => r.Time).Where(t => t > 0).TakeLast(FuelLapHistory).ToArray();
            return vals.Length == 0 ? 0 : vals.Average();
        }
    }

    public double ModeAvgFuel(string mode)
    {
        if (!_modeLaps.TryGetValue(mode, out var q)) return 0;
        var vals = q.Select(r => r.FuelUsed).Where(f => f > 0).ToArray();
        return vals.Length >= 2 ? vals.Average() : 0;
    }

    public double ModeAvgTime(string mode)
    {
        if (!_modeLaps.TryGetValue(mode, out var q)) return 0;
        var vals = q.Select(r => r.Time).Where(t => t > 0).ToArray();
        return vals.Length >= 2 ? vals.Average() : 0;
    }

    public double ActiveAvgFuel
    {
        get
        {
            var ma = ModeAvgFuel(DriveMode);
            return ma > 0 ? ma : AvgFuelPerLap;
        }
    }

    public double ActiveAvgTime
    {
        get
        {
            var mt = ModeAvgTime(DriveMode);
            return mt > 0 ? mt : AvgLapTime;
        }
    }

    // ── Mutations ─────────────────────────────────────────────────────────────
    public void OnNewLap(int lap, double lastLapTime, double currentFuel, double[] currentWear, bool record = true)
    {
        if (record && PrevLap > 0 && LapStartFuel >= 0)
        {
            double fuelUsed = LapStartFuel - currentFuel;
            if (fuelUsed > 0 && fuelUsed < 0.5 && lastLapTime > 10)
                AddLapRecord(new LapRecord(PrevLap, lastLapTime, fuelUsed, DriveMode));

            if (LapsOnTires > 0)
            {
                for (int i = 0; i < 4; i++)
                {
                    // currentWear is tread REMAINING (1.0 = new), which DECREASES as the
                    // tyre wears, so per-lap tread loss = start − current (positive).
                    double delta = LapStartWear[i] - currentWear[i];
                    if (delta > 0)
                    {
                        if (_wearHistory[i].Count >= 3) _wearHistory[i].Dequeue();
                        _wearHistory[i].Enqueue(delta);
                        WearRate[i] = _wearHistory[i].Average();
                    }
                }
            }
        }
        PrevLap       = lap;
        LapStartFuel  = currentFuel;
        LapStartWear  = (double[])currentWear.Clone();
        LapsOnTires  += 1;
    }

    public void OnPitExit(double currentFuel, double[] currentWear)
    {
        LapsOnTires   = 0;
        PitCount     += 1;
        LapStartFuel  = currentFuel;
        LapStartWear  = (double[])currentWear.Clone();
        WearRate      = new double[4];
        for (int i = 0; i < 4; i++) _wearHistory[i].Clear();
        StintStartTime = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
        StintStartLap  = PrevLap;
    }
}
