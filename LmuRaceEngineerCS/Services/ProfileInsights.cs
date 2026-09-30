using LmuRaceEngineerCS.Models;

namespace LmuRaceEngineerCS.Services;

/// <summary>
/// Turns a learned <see cref="TrackProfile"/> into human-readable insight:
/// a driving-style label, the driver-vs-engine confidence split, and a set of
/// driving / strategy tips. Pure functions — no state, safe to call from the UI.
///
/// The confidence split mirrors <see cref="ProfileLearner"/> exactly:
///   weight = min(1, SampleStints / FullConfidenceN); the strategy engine blends
///   the learned onset by <c>weight</c> and the generic 0.60 model by 1-weight.
/// </summary>
public static class ProfileInsights
{
    public const double GenericOnset     = 0.60;   // ProfileLearner.GenericOnset
    public const int    FullConfidenceN  = 5;      // ProfileLearner.FullConfidenceN

    /// <summary>One actionable tip, grouped by category for the UI.</summary>
    public readonly record struct Tip(TipKind Kind, string Title, string Body);

    public enum TipKind { Driving, Strategy, Confidence }

    /// <summary>Fraction of the blended onset that comes from this driver's own data (0–1).</summary>
    public static double DriverConfidence(int sampleStints) =>
        Math.Min(1.0, sampleStints / (double)FullConfidenceN);

    /// <summary>Fraction still supplied by the generic strategy-engine model (0–1).</summary>
    public static double EngineConfidence(int sampleStints) =>
        1.0 - DriverConfidence(sampleStints);

    /// <summary>Wear fraction the engine treats as the cliff (see RaceInput.PersonalCliffThreshold).</summary>
    public static double CliffThreshold(double degOnset) =>
        Math.Max(0.05, 0.75 - degOnset);

    /// <summary>Short driving-style label derived from where this driver hits the tyre cliff.</summary>
    public static string StyleLabel(double degOnset) => degOnset switch
    {
        <= 0.45 => "Aggressive — early deg",
        <= 0.57 => "Moderately aggressive",
        <  0.63 => "Balanced (≈ generic)",
        _       => "Smooth — tyre saver",
    };

    /// <summary>Hex colour matching the style band, for UI accenting.</summary>
    public static string StyleColorHex(double degOnset) => degOnset switch
    {
        <= 0.45 => "#e05555",   // red — burns tyres
        <= 0.57 => "#e0a23a",   // amber
        <  0.63 => "#c7c7c7",   // neutral grey
        _       => "#33ee77",   // green — kind to tyres
    };

    /// <summary>Builds the full tip list shown in the row's expansion panel.</summary>
    public static IReadOnlyList<Tip> BuildTips(double degOnset, int sampleStints)
    {
        var tips = new List<Tip>();

        int    onsetPct  = (int)Math.Round(degOnset * 100);
        int    cliffPct   = (int)Math.Round(CliffThreshold(degOnset) * 100);
        double driverConf = DriverConfidence(sampleStints);

        // ── Driving-style guidance ────────────────────────────────────────────
        if (degOnset <= 0.45)
        {
            tips.Add(new Tip(TipKind.Driving,
                "You're cooking the tyres early",
                $"Significant lap-time loss starts around {onsetPct}% wear — earlier than the " +
                $"generic {GenericOnset:P0} baseline. Smoother throttle pickup, gentler trail-braking " +
                "and easing mid-corner load will push the onset later and lengthen your stints."));
        }
        else if (degOnset >= 0.63)
        {
            tips.Add(new Tip(TipKind.Driving,
                "You're easy on the tyres",
                $"Real time loss only kicks in past ~{onsetPct}% wear. You can confidently run long " +
                "stints and lean on used rubber where others have to pit — a genuine strategic asset."));
        }
        else
        {
            tips.Add(new Tip(TipKind.Driving,
                "Balanced tyre usage",
                $"Your degradation onset (~{onsetPct}%) sits close to the generic model. There's room " +
                "to gain at either end: smoother inputs to extend stints, or controlled aggression " +
                "early in a stint while the tyre is fresh."));
        }

        // ── Strategy implications ─────────────────────────────────────────────
        if (degOnset <= 0.45)
        {
            tips.Add(new Tip(TipKind.Strategy,
                "Engine pits you earlier",
                $"Your cliff threshold is {cliffPct}% wear, so the strategy engine shortens stints and " +
                "favours pitting before the drop-off. An extra stop on fresher tyres will usually beat " +
                "nursing worn ones to the flag — don't fight the early-pit calls."));
        }
        else if (degOnset >= 0.63)
        {
            tips.Add(new Tip(TipKind.Strategy,
                "Overcut & long-stint friendly",
                "Because you hold pace deep into a stint, the engine will lean toward longer stints and " +
                "overcuts (staying out while rivals pit). Track-position gambles and one-fewer-stop plans " +
                "are more viable for you than for the average driver."));
        }
        else
        {
            tips.Add(new Tip(TipKind.Strategy,
                "Flexible pit window",
                $"With a {cliffPct}% cliff threshold your pit window is fairly wide — react to undercut/" +
                "overcut pressure from rivals rather than committing to a fixed lap. The engine's " +
                "'cover rival' calls are well-suited to your profile."));
        }

        // ── Confidence / data-coverage note ───────────────────────────────────
        if (sampleStints <= 0)
        {
            tips.Add(new Tip(TipKind.Confidence,
                "No data yet — running on the generic model",
                $"The engine is using the generic {GenericOnset:P0} onset until you complete race stints " +
                "here. Drive a few full stints to start personalising the strategy."));
        }
        else if (driverConf < 1.0)
        {
            int    pct       = (int)Math.Round(driverConf * 100);
            int    enginePct = 100 - pct;
            int    needed    = FullConfidenceN - sampleStints;
            tips.Add(new Tip(TipKind.Confidence,
                $"Profile is {pct}% learned",
                $"The blended onset is {pct}% your data and {enginePct}% the generic 0.60 model " +
                $"({sampleStints}/{FullConfidenceN} stints). Drive {needed} more race " +
                $"stint{(needed == 1 ? "" : "s")} here to reach full confidence."));
        }
        else
        {
            tips.Add(new Tip(TipKind.Confidence,
                "Fully learned profile",
                $"{sampleStints} stints analysed — the engine uses your personal degradation curve with " +
                "no generic fallback. Strategy calls here are tuned entirely to how you drive."));
        }

        return tips;
    }
}
