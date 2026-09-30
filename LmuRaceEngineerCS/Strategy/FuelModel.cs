namespace LmuRaceEngineerCS.Strategy;

/// <summary>
/// Pure fuel prediction functions. No state; all inputs explicit.
/// </summary>
internal static class FuelModel
{
    /// <summary>Laps of fuel remaining given current VE fraction and per-lap consumption.</summary>
    public static double LapsRemaining(double fuelFraction, double avgFuelPerLap)
        => avgFuelPerLap > 0 ? fuelFraction / avgFuelPerLap : 0;

    /// <summary>VE fraction needed to cover the given number of laps.</summary>
    public static double FuelNeeded(double laps, double avgFuelPerLap)
        => laps * avgFuelPerLap;

    /// <summary>
    /// Approximate lap-time penalty from fuel mass.
    /// Baseline = 50 % VE. Each 1 % above baseline costs 0.01 s/lap, i.e. a full
    /// tank (100 % VE, 50 % above baseline) is worth ~0.50 s/lap.
    /// </summary>
    public static double LapTimePenaltyFromFuel(double fuelFraction)
    {
        double pct = fuelFraction * 100.0;
        double above = Math.Max(0, pct - 50.0);
        return above * 0.01;  // 0.01 s/lap per 1% above 50% → 0.50 s/lap at full tank
    }
}
