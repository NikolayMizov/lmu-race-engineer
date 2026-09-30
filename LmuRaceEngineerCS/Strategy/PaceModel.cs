namespace LmuRaceEngineerCS.Strategy;

/// <summary>
/// Estimates expected lap time under various conditions. No state.
/// </summary>
internal static class PaceModel
{
    /// <summary>
    /// Lap-time penalty (seconds added) from a faster-class car approaching.
    /// Gap &lt; 8 s adds 0.3–1.5 s depending on proximity.
    /// </summary>
    public static double TrafficPenalty(double? fasterClassGap)
    {
        if (fasterClassGap is not double gap || gap >= 8.0) return 0.0;
        // linear: 8 s gap → 0.3 s penalty; 0 s gap → 1.5 s penalty
        return 0.3 + (8.0 - gap) / 8.0 * 1.2;
    }

    /// <summary>
    /// Expected lap time combining base pace, fuel mass penalty, tire degradation, and traffic.
    /// <paramref name="degOnset"/> shifts the degradation curve to match the driver's personal profile.
    /// </summary>
    public static double ExpectedLapTime(double baseLapTime, double fuelFraction,
                                          double tireWear, double? fasterClassGap,
                                          double degOnset = 0.60)
    {
        return baseLapTime
             + FuelModel.LapTimePenaltyFromFuel(fuelFraction)
             + TireModel.LapTimeDelta(tireWear, degOnset)
             + TrafficPenalty(fasterClassGap);
    }
}
