using LmuRaceEngineerCS.Models;

namespace LmuRaceEngineerCS.Strategy;

/// <summary>
/// Frozen snapshot of all inputs the strategy engine needs for one computation cycle.
/// Assembled from StrategySnapshot + a live opponent scan at cycle start.
/// </summary>
public record RaceInput(
    // ── Player fuel ───────────────────────────────────────────────────────────
    double CurrentFuelFraction,   // Ve / 100
    double AvgFuelPerLap,         // VE fraction per lap

    // ── Player tires ──────────────────────────────────────────────────────────
    double WorstTireWear,         // 0–1
    double MaxWearRate,           // wear fraction added per lap
    int    CompoundType,          // front compound index

    // ── Player pace ───────────────────────────────────────────────────────────
    double AvgLapTime,            // seconds
    double? FasterClassGap,       // seconds; null if no traffic

    // ── Player stint / session ────────────────────────────────────────────────
    int    CurrentLap,
    int    StintLaps,
    int    PitStops,
    int    ClassPos,
    double GapFront,              // seconds to car ahead
    double GapBehind,             // seconds to car behind

    // ── Session ───────────────────────────────────────────────────────────────
    double LapsRemaining,
    bool   IsLapBased,
    double SessionTimeRemaining,
    bool   IsRace,

    // ── Pit ───────────────────────────────────────────────────────────────────
    double PitLossSeconds,
    double EarliestSafePitLap,

    // ── Opponents ─────────────────────────────────────────────────────────────
    IReadOnlyList<OpponentState> Opponents,

    // ── Weather ───────────────────────────────────────────────────────────────
    WeatherCondition WeatherCondition,
    WeatherTrend     WeatherTrend,
    CompoundAdvice   CompoundAdvice,

    // ── Driver profile (learned, always present) ──────────────────────────────
    /// <summary>
    /// Wear fraction where this driver personally starts losing significant time (0–1).
    /// 0.60 = generic model; lower = earlier onset; higher = later onset.
    /// Current-session wear rate remains dominant; this only shifts the time-loss curve.
    /// </summary>
    double PersonalDegOnset
)
{
    /// <summary>
    /// Returns the wear fraction at which the cliff is considered reached.
    /// Generic onset 0.60 → 0.15 (original hardcoded value).
    /// Lower onset → larger threshold → pit earlier.
    /// </summary>
    public static double PersonalCliffThreshold(double onset) =>
        Math.Max(0.05, 0.75 - onset);
}
