namespace LmuRaceEngineerCS.Models;

public enum WeatherCondition { Dry, Damp, Wet }
public enum WeatherTrend     { Stable, GettingWetter, Drying }
// LMU has only slicks and wets — no intermediate compound.
public enum CompoundAdvice { None, SwitchToWets, SwitchToDries }

public static class WeatherHelper
{
    // ── Condition classification ──────────────────────────────────────────────
    public static WeatherCondition Classify(double raining, double avgWetness)
    {
        if (raining >= 0.25 || avgWetness >= 0.35) return WeatherCondition.Wet;
        if (raining >= 0.05 || avgWetness >= 0.12) return WeatherCondition.Damp;
        return WeatherCondition.Dry;
    }

    // ── Compound advice — slicks vs wets only ─────────────────────────────────
    public static CompoundAdvice GetCompoundAdvice(
        WeatherCondition condition, WeatherTrend trend,
        double raining, double avgWetness, double darkCloud) =>
        condition switch
        {
            // Any meaningful rain or wet surface → wets
            WeatherCondition.Wet  => CompoundAdvice.SwitchToWets,
            WeatherCondition.Damp => CompoundAdvice.SwitchToWets,
            // Dry but deteriorating rapidly with heavy overcast
            WeatherCondition.Dry when trend == WeatherTrend.GettingWetter && darkCloud > 0.6
                                 => CompoundAdvice.SwitchToWets,
            // Track has dried out — switch back to slicks
            WeatherCondition.Dry when trend == WeatherTrend.Drying
                                   && avgWetness < 0.08 && raining < 0.02
                                 => CompoundAdvice.SwitchToDries,
            _                    => CompoundAdvice.None,
        };

    // ── Expected pace loss (s/lap) from wrong compound ────────────────────────
    public static double PacePenaltyPerLap(CompoundAdvice advice) => advice switch
    {
        CompoundAdvice.SwitchToWets  => 4.5,   // slicks in rain
        CompoundAdvice.SwitchToDries => 2.0,   // wets in dry
        _                            => 0.0,
    };

    // ── Label strings ─────────────────────────────────────────────────────────
    public static string CompoundLabel(CompoundAdvice advice) => advice switch
    {
        CompoundAdvice.SwitchToWets  => "SWITCH TO WETS",
        CompoundAdvice.SwitchToDries => "SWITCH TO SLICKS",
        _                            => "",
    };

    public static string CompoundLabelShort(CompoundAdvice advice) => advice switch
    {
        CompoundAdvice.SwitchToWets  => "wet tyres",
        CompoundAdvice.SwitchToDries => "slick tyres",
        _                            => "",
    };

    public static string ConditionLabel(WeatherCondition condition) => condition switch
    {
        WeatherCondition.Wet  => "wet",
        WeatherCondition.Damp => "damp",
        _                     => "dry",
    };

    public static string TrendLabel(WeatherTrend trend) => trend switch
    {
        WeatherTrend.GettingWetter => "getting wetter",
        WeatherTrend.Drying        => "drying",
        _                          => "stable",
    };
}
