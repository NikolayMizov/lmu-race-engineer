using LmuRaceEngineerCS.Strategy;

namespace LmuRaceEngineerCS.Models;

/// <summary>
/// Output record published by StrategyEngine every ~12 seconds.
/// Consumed by StrategyPage.
/// </summary>
public record StrategyRecommendation
{
    // ── Top recommendation ────────────────────────────────────────────────────
    public StrategyActionType Action            { get; init; }
    public int                RecommendedPitLap { get; init; }  // 0 = stay out
    public string             ActionLabel       { get; init; } = "";
    public string             Rationale         { get; init; } = "";

    // ── Scores ────────────────────────────────────────────────────────────────
    public double TopScore { get; init; }
    public double AltScore { get; init; }   // second-best candidate score

    // ── Projections for best action ───────────────────────────────────────────
    public double ProjectedFinishTime { get; init; }   // simulated total race seconds
    public int    ProjectedPosition   { get; init; }
    public double FuelSafetyMargin    { get; init; }   // laps of buffer
    public double TireCliffRisk       { get; init; }   // 0–1

    // ── Opponent context ──────────────────────────────────────────────────────
    public bool    UndercutThreat       { get; init; }
    public bool    OvercutOpportunity   { get; init; }
    public double? NearestRivalGap      { get; init; }
    public double? RivalEstimatedPitLap { get; init; }

    // ── Uncertainty band ─────────────────────────────────────────────────────
    public double FinishTimeUncertainty { get; init; }  // ±seconds heuristic

    // ── All ranked candidates ─────────────────────────────────────────────────
    public IReadOnlyList<StrategyCandidate> Candidates { get; init; }
        = Array.Empty<StrategyCandidate>();

    // ── Weather / compound advice ─────────────────────────────────────────────
    public CompoundAdvice   CompoundAdvice   { get; init; }
    public WeatherCondition WeatherCondition { get; init; }

    // ── Metadata ──────────────────────────────────────────────────────────────
    public DateTimeOffset ComputedAt  { get; init; }
    public int            SnapshotLap { get; init; }
}
