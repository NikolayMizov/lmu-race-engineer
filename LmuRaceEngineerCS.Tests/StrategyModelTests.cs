using LmuRaceEngineerCS.Strategy;
using Xunit;

namespace LmuRaceEngineerCS.Tests;

/// <summary>
/// Pins the pure strategy-model math to the CORRECT tyre-wear convention:
/// wear ACCUMULATES upward (0 = new, 1 = fully worn). These tests also document the
/// polarity fix — LMU reports tread REMAINING, which must be converted (1 − remaining)
/// before it reaches these models.
/// </summary>
public class TireModelTests
{
    [Fact] public void NewTyre_NoDegPenalty()
        => Assert.Equal(0.0, TireModel.LapTimeDelta(0.0));

    [Fact] public void AtGenericOnset_NoPenaltyYet()
        => Assert.Equal(0.0, TireModel.LapTimeDelta(0.60), 3);

    [Fact] public void PastCliff_MaxPenalty()
        => Assert.Equal(1.5, TireModel.LapTimeDelta(0.95), 3);

    [Fact] public void MidDegradation_PartialPenalty()
        => Assert.InRange(TireModel.LapTimeDelta(0.75), 0.01, 1.49);

    // ── Polarity guard ────────────────────────────────────────────────────────
    // A near-new tyre reads ~0.98 tread REMAINING. Fed straight in (the old bug) that
    // looks like 98 % worn → full cliff penalty. Converted to wear (1 − 0.98 = 0.02) it
    // correctly reads as new → no penalty.
    [Fact] public void Polarity_FreshTreadConverted_IsNoPenalty()
    {
        double remaining = 0.98;
        double wearAccum = 1.0 - remaining;              // == StrategyEngine.BuildInput
        Assert.Equal(0.0, TireModel.LapTimeDelta(wearAccum));
    }

    [Fact] public void Polarity_RawRemainingWouldMisfire()
    {
        double remaining = 0.98;                          // pre-fix path
        Assert.Equal(1.5, TireModel.LapTimeDelta(remaining), 3);   // false cliff
    }

    [Fact] public void WornTyre_HasPenalty()
        => Assert.True(TireModel.LapTimeDelta(1.0 - 0.30) > 0);   // 70 % worn

    [Fact] public void LapsToCliff_FreshTyre_IsFar()
    {
        double? laps = TireModel.LapsToCliff(currentWear: 0.05, wearRatePerLap: 0.01);
        Assert.NotNull(laps);
        Assert.True(laps > 10);
    }

    [Fact] public void LapsToCliff_ZeroRate_IsNull()
        => Assert.Null(TireModel.LapsToCliff(0.5, 0.0));

    [Fact] public void ProjectWear_CapsAtOne()
        => Assert.Equal(1.0, TireModel.ProjectWear(0.9, 0.1, 5));

    [Fact] public void CliffRisk_PastCliff_IsMax()
        => Assert.Equal(1.0, TireModel.CliffRisk(0, 10));
}

public class CliffThresholdTests
{
    [Fact] public void Generic_MapsToDefault()
        => Assert.Equal(0.15, RaceInput.PersonalCliffThreshold(0.60), 3);

    [Fact] public void LowerOnset_PitsEarlier()
        => Assert.True(RaceInput.PersonalCliffThreshold(0.40) > RaceInput.PersonalCliffThreshold(0.70));

    [Fact] public void NeverBelowFloor()
        => Assert.True(RaceInput.PersonalCliffThreshold(0.95) >= 0.05);
}

public class FuelModelTests
{
    [Fact] public void LapsRemaining_Basic()
        => Assert.Equal(10.0, FuelModel.LapsRemaining(1.0, 0.10), 3);

    [Fact] public void LapsRemaining_ZeroConsumption_IsZero()
        => Assert.Equal(0.0, FuelModel.LapsRemaining(1.0, 0.0));

    [Fact] public void FuelNeeded_Basic()
        => Assert.Equal(0.5, FuelModel.FuelNeeded(5, 0.10), 3);

    [Fact] public void Penalty_BelowBaseline_IsZero()
        => Assert.Equal(0.0, FuelModel.LapTimePenaltyFromFuel(0.40));

    [Fact] public void Penalty_FullTank_IsPositive()
        => Assert.True(FuelModel.LapTimePenaltyFromFuel(1.0) > 0);
}

public class PaceModelTests
{
    [Fact] public void WornIsSlowerThanFresh()
    {
        double fresh = PaceModel.ExpectedLapTime(100, 0.5, 0.05, null);
        double worn  = PaceModel.ExpectedLapTime(100, 0.5, 0.80, null);
        Assert.True(worn > fresh);
    }

    [Fact] public void HeavierFuelIsSlower()
    {
        double light = PaceModel.ExpectedLapTime(100, 0.30, 0.05, null);
        double heavy = PaceModel.ExpectedLapTime(100, 1.00, 0.05, null);
        Assert.True(heavy > light);
    }

    [Fact] public void TrafficPenalty_NoTraffic_IsZero()
        => Assert.Equal(0.0, PaceModel.TrafficPenalty(null));

    [Fact] public void TrafficPenalty_Close_IsPositive()
        => Assert.True(PaceModel.TrafficPenalty(2.0) > 0);
}

public class PitLossModelTests
{
    [Fact] public void Base_IsStaticLoss()
        => Assert.Equal(25.0, PitLossModel.TruePitLoss(25.0), 3);

    [Fact] public void FullTrafficRisk_AddsPremium()
        => Assert.Equal(35.0, PitLossModel.TruePitLoss(25.0, 1.0), 3);
}
