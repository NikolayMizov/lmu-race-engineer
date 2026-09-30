namespace LmuRaceEngineerCS.Models;

/// <summary>
/// Immutable snapshot of all computed race data, produced by TelemetryService
/// and consumed by the UI and voice assistant. Mirrors the Python _snapshot dict.
/// </summary>
public record StrategySnapshot
{
    // ── Energy / fuel ─────────────────────────────────────────────────────────
    public double Ve            { get; init; }  // VE percent 0-100
    public double LapsOnVe      { get; init; }
    public double RsFuel        { get; init; }
    public double AvgFuel       { get; init; }
    public double AvgFuel3      { get; init; }
    public double AvgFuel10     { get; init; }
    public double FuelSaveTarget { get; init; }
    public double MinFinishFuel { get; init; }
    public double FuelDeficit   { get; init; }  // extra VE fraction needed
    public int    ExtraStops    { get; init; }
    public int    FuelLaps      { get; init; }  // ceil laps from fuel
    public bool   IsRace        { get; init; }

    // ── Lap times ─────────────────────────────────────────────────────────────
    public double AvgLapTime    { get; init; }
    public double LastLapTime   { get; init; }
    public double BestLapTime   { get; init; }

    // ── Gaps ─────────────────────────────────────────────────────────────────
    public double GapFront           { get; init; }
    public double GapBehind          { get; init; }
    public double AvgLapDeltaFront   { get; init; }
    public double AvgLapDeltaBehind  { get; init; }

    // ── Session ───────────────────────────────────────────────────────────────
    public double LapsRemaining        { get; init; }
    public double SessionTimeRemaining { get; init; }
    public double TotalLapsEstimate    { get; init; }
    public int    CurrentLap           { get; init; }
    public string TrackName            { get; init; } = "";
    public int    Session              { get; init; }
    public string CurrentDriverName    { get; init; } = "";
    public string PreviousDriverName   { get; init; } = "";

    // ── Position ─────────────────────────────────────────────────────────────
    public int ClassPos   { get; init; }
    public int ClassTotal { get; init; }
    public int PitStops   { get; init; }

    // ── Pit window ───────────────────────────────────────────────────────────
    public double? PitLap       { get; init; }
    public string? PitLimiter   { get; init; }
    public double  EarliestPit  { get; init; }
    public double? LatestPit    { get; init; }

    // ── Stint ─────────────────────────────────────────────────────────────────
    public int     StintLaps      { get; init; }
    public double  StintTime      { get; init; }
    public double? PredictedStint { get; init; }

    // ── Strategy mode ─────────────────────────────────────────────────────────
    public string StrategyMode { get; init; } = "target";
    public string DriveMode    { get; init; } = "normal";
    public double PushAvgFuel  { get; init; }
    public double SaveAvgFuel  { get; init; }
    public double PushAvgTime  { get; init; }
    public double SaveAvgTime  { get; init; }

    // ── Tires ─────────────────────────────────────────────────────────────────
    public double   WorstWear   { get; init; }
    public double?  TireLaps    { get; init; }
    public double[] TireWear    { get; init; } = new double[4];
    public double[] TireTempsC  { get; init; } = new double[4];
    public int[]    CompoundTypes { get; init; } = new int[4];
    public double[] BrakeTemps  { get; init; } = new double[4];

    // ── Hybrid ────────────────────────────────────────────────────────────────
    public double StateOfCharge { get; init; }
    public double Regen         { get; init; }

    // ── Driver aids ───────────────────────────────────────────────────────────
    public bool AbsActive   { get; init; }
    public bool TcActive    { get; init; }
    public bool LapInvalid  { get; init; }
    public int  TrackLimitsSteps      { get; init; }
    public int  TrackLimitsPerPenalty { get; init; }

    // ── Driver inputs (overlay widgets) ───────────────────────────────────────
    public double Throttle { get; init; }          // 0–1
    public double Brake    { get; init; }          // 0–1
    public double Clutch   { get; init; }          // 0–1
    public double Steering { get; init; }          // −1 (left) … +1 (right)
    public int    Gear     { get; init; }
    public double SpeedKph { get; init; }

    // ── Per-wheel detail (overlay widgets), order FL FR RL RR ─────────────────
    public double[] TirePressures  { get; init; } = new double[4];  // kPa
    public double[] GripFract      { get; init; } = new double[4];  // 0–1
    public double[] TireTempInner  { get; init; } = new double[4];  // °C
    public double[] TireTempMiddle { get; init; } = new double[4];  // °C
    public double[] TireTempOuter  { get; init; } = new double[4];  // °C
    public double[] TireOptimalC   { get; init; } = new double[4];  // °C
    /// <summary>Contact-patch longitudinal velocity — vs ground velocity gives slip.</summary>
    public double[] PatchVelLong   { get; init; } = new double[4];
    public double[] GroundVelLong  { get; init; } = new double[4];
    public bool[]   TireFlat       { get; init; } = new bool[4];
    public bool[]   WheelDetached  { get; init; } = new bool[4];

    // ── Traffic ───────────────────────────────────────────────────────────────
    public double? FasterClassGap { get; init; }

    // ── Sector times ─────────────────────────────────────────────────────────
    public double LastSector1 { get; init; }
    public double LastSector2 { get; init; }
    public double BestSector1 { get; init; }
    public double BestSector2 { get; init; }
    public double DeltaBest   { get; init; }
    /// <summary>Last completed lap time minus rolling average — approximate delta-to-last.</summary>
    public double DeltaLast   { get; init; }

    // ── Driver profile ────────────────────────────────────────────────────────
    /// <summary>Learned wear onset from historical CSVs (0–1). 0 = use generic 0.60.</summary>
    public double PersonalDegOnset  { get; init; }
    /// <summary>True when ≥ 2 race stints have been analysed for this track.</summary>
    public bool   PersonalDegActive { get; init; }

    // ── Weather ───────────────────────────────────────────────────────────────
    public int    TrackGrip      { get; init; }
    public int    CloudCoverage  { get; init; }
    public double AmbientTemp    { get; init; }
    public double TrackTemp      { get; init; }
    public double Raining        { get; init; }   // 0–1 rain intensity
    public double AvgPathWetness { get; init; }   // 0–1 track surface wetness
    public double DarkCloud      { get; init; }   // 0–1 cloud darkness (rain proximity)
    public WeatherTrend   WeatherTrend   { get; init; }
    public WeatherCondition WeatherCondition { get; init; }
    public CompoundAdvice CompoundAdvice { get; init; }
}
