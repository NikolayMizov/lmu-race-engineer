namespace LmuRaceEngineerCS.Strategy;

/// <summary>
/// Linear tire degradation model with cliff detection. No state; all inputs explicit.
/// </summary>
internal static class TireModel
{
    /// <summary>Projected wear fraction (0–1) after <paramref name="laps"/> more laps.</summary>
    public static double ProjectWear(double currentWear, double wearRatePerLap, int laps)
        => Math.Min(1.0, currentWear + wearRatePerLap * laps);

    /// <summary>
    /// Expected lap-time delta (seconds added) due to tire degradation.
    /// Piecewise linear curve anchored at <paramref name="degOnset"/> (wear fraction
    /// where time loss begins) with a fixed 0.30 span to max penalty (1.5 s).
    ///
    /// Generic defaults: onset = 0.60 (40 % remaining), max at 0.90 (10 % remaining).
    /// Personal onset shifts the entire curve earlier/later to match driver behaviour.
    /// </summary>
    /// <param name="wearFraction">Current worst-tire wear 0–1.</param>
    /// <param name="degOnset">Wear fraction where time loss begins (0–1). Default 0.60.</param>
    public static double LapTimeDelta(double wearFraction, double degOnset = 0.60)
    {
        double remaining       = 1.0 - wearFraction;
        double onsetRemaining  = 1.0 - degOnset;       // remaining life at onset
        const double Span      = 0.30;
        double cliffRemaining  = Math.Max(0.0, onsetRemaining - Span);

        if (remaining >= onsetRemaining) return 0.0;
        if (remaining <= cliffRemaining) return 1.5;
        return (onsetRemaining - remaining) / Span * 1.5;
    }

    /// <summary>
    /// Laps until wear crosses <paramref name="cliffThreshold"/> (remaining life).
    /// Returns <c>null</c> if the cliff is never reached within the simulation window or wear rate is zero.
    /// </summary>
    public static double? LapsToCliff(double currentWear, double wearRatePerLap,
                                       double cliffThreshold = 0.15)
    {
        if (wearRatePerLap <= 0) return null;
        double remaining = 1.0 - currentWear;
        double cliffWear  = 1.0 - cliffThreshold;   // absolute wear when cliff is hit
        if (currentWear >= cliffWear) return 0;      // already past cliff
        return (cliffWear - currentWear) / wearRatePerLap;
    }

    /// <summary>
    /// Maps laps-to-cliff into a 0–1 risk value.
    /// 0 = safely far from cliff; 1 = at or past cliff during simulation.
    /// </summary>
    public static double CliffRisk(double? lapsToCliff, double lapsRemaining)
    {
        if (lapsToCliff == null) return 0.0;
        if (lapsToCliff.Value <= 0) return 1.0;
        return Math.Clamp(1.0 - lapsToCliff.Value / Math.Max(1, lapsRemaining), 0.0, 1.0);
    }
}
