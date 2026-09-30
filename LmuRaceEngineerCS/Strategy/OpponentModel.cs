namespace LmuRaceEngineerCS.Strategy;

/// <summary>
/// Pure functions for interpreting opponent state and detecting tactical windows.
/// </summary>
internal static class OpponentModel
{
    /// <summary>
    /// Estimates in how many laps from now the opponent is likely to pit,
    /// based on their remaining fuel fraction and the nominal stint length.
    /// Returns <c>null</c> when data is insufficient.
    /// </summary>
    public static double? EstimatedPitLap(OpponentState opp, double avgFuelPerLap,
                                           double nominalStintLaps)
    {
        // Prefer fuel-based estimate if avgFuelPerLap is known
        if (avgFuelPerLap > 0 && opp.FuelFraction > 0)
            return opp.FuelFraction / avgFuelPerLap;

        // Fall back to stint-length heuristic
        if (nominalStintLaps > 0 && opp.StintLaps >= 0)
            return Math.Max(0, nominalStintLaps - opp.StintLaps);

        return null;
    }

    /// <summary>
    /// Returns true when the opponent is in a position to undercut us: they are
    /// within the pit-loss window — just ahead OR just behind — and on tyres that
    /// are not fresher than ours, so a stop for fresh rubber would let them jump us.
    /// (A classic undercut threat is often the car immediately behind, so this is
    /// evaluated for both sides; the caller bounds it to the nearby window.)
    /// </summary>
    public static bool IsUndercutThreat(OpponentState opp, double pitLoss, int playerStintLaps)
    {
        bool insidePitLoss = Math.Abs(opp.GapToPlayer) < pitLoss;
        bool fresherTires  = opp.StintLaps < playerStintLaps;  // fewer stint laps = fresher rubber
        return insidePitLoss && !fresherTires;
    }

    /// <summary>
    /// Returns true when we can overcut a rival: they are within the pit-loss window
    /// — just ahead OR just behind — and are expected to pit soon, so staying out on
    /// our current tyres could yield track position across their stop, provided our
    /// pace is not materially slower than theirs. Evaluated for both sides; the caller
    /// bounds it to the nearby window.
    /// </summary>
    public static bool IsOvercutOpportunity(OpponentState opp, double pitLoss,
                                             double playerLapTime, double oppLapTime)
    {
        double gapAbs = Math.Abs(opp.GapToPlayer);    // how far apart we are, either side
        return gapAbs < pitLoss && playerLapTime <= oppLapTime + 1.5;
    }

    /// <summary>
    /// Selects the rivals that a pit exchange can realistically affect: the nearest
    /// <paramref name="perSide"/> cars ahead of the player and the nearest
    /// <paramref name="perSide"/> behind, ordered by absolute gap. Used to bound the
    /// undercut / overcut checks to (by default) two cars ahead and two behind.
    /// </summary>
    public static IEnumerable<OpponentState> NearbyWindow(
        IReadOnlyList<OpponentState> opponents, int perSide = 2)
    {
        var ahead  = opponents.Where(o => o.GapToPlayer > 0)
                              .OrderBy(o => o.GapToPlayer).Take(perSide);
        var behind = opponents.Where(o => o.GapToPlayer < 0)
                              .OrderBy(o => Math.Abs(o.GapToPlayer)).Take(perSide);
        return ahead.Concat(behind);
    }
}
