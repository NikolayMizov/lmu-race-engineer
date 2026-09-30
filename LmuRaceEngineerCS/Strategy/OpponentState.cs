namespace LmuRaceEngineerCS.Strategy;

/// <summary>
/// Immutable snapshot of a single rival vehicle, captured at strategy-engine tick time.
/// </summary>
public record OpponentState(
    int    VehicleIndex,
    string DriverName,
    int    ClassPosition,
    int    TotalLaps,
    double TimeBehindLeader,    // raw mTimeBehindLeader
    double GapToPlayer,         // positive = rival is ahead of player (smaller TimeBehindLeader)
    double LastLapTime,
    double EstimatedLapTime,
    int    NumPitstops,
    bool   IsInPits,
    int    PitState,
    double FuelFraction,        // mFuelFraction / 255.0, 0–1
    int    StintLaps            // TotalLaps - NumPitstops * estimated avg stint
);
