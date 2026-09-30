namespace LmuRaceEngineerCS.Strategy;

public enum StrategyActionType
{
    StayOut,
    PitNowFullFuel,
    PitNowShortFuel,    // fill only enough to reach the end with safety buffer
    SaveFuel,
    PushUndercut,
    CoverRival
}

/// <summary>
/// One simulated strategy candidate with its projected outcome and score.
/// </summary>
public record StrategyCandidate(
    StrategyActionType ActionType,
    int                PitOnLap,             // lap to pit; 0 = no pit in this window
    double             ProjectedFinishTime,  // cumulative elapsed seconds over simulation
    int                ProjectedPosition,    // estimated finishing class position
    double             FuelSafetyMargin,     // laps of fuel remaining at end; negative = ran out
    double             TireCliffRisk,        // 0–1; 1 = hit or exceeded cliff during sim
    double             TrafficLoss,          // total seconds lost to slower traffic in sim
    double             Score,                // weighted composite (higher = better)
    string             Rationale            // short human-readable explanation
);
