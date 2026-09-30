namespace LmuRaceEngineerCS.Strategy;

/// <summary>
/// Estimates the true time cost of stopping for a pit service. No state.
/// </summary>
internal static class PitLossModel
{
    /// <summary>
    /// True cost of a pit stop: static in-lane time plus a traffic-rejoin risk premium.
    /// </summary>
    /// <param name="staticLossSeconds">Configured pit lane time (default 25 s).</param>
    /// <param name="trafficRisk">0–1 value. 1.0 adds 10 s of expected rejoin delay.</param>
    public static double TruePitLoss(double staticLossSeconds, double trafficRisk = 0.0)
        => staticLossSeconds + Math.Clamp(trafficRisk, 0.0, 1.0) * 10.0;
}
