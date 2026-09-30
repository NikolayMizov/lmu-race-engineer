namespace LmuRaceEngineerCS.Shared;

/// <summary>
/// Shared utility methods used across Services, Strategy, and Views.
/// Centralises logic that was duplicated in LapLogger, ProfileLearner, and StrategyPage.
/// </summary>
public static class RaceUtils
{
    /// <summary>
    /// Converts a raw string to a filesystem-safe name: keeps letters, digits, '-', '_';
    /// replaces everything else with '_'.
    /// </summary>
    public static string SafeName(string s) =>
        new(s.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());

    /// <summary>Formats seconds as m:ss.mmm (e.g. 92.345 → "1:32.345").</summary>
    public static string FormatLapTime(double secs)
    {
        if (secs <= 0) return "--:--.---";
        int m = (int)(secs / 60);
        double s = secs % 60;
        // .NET custom numeric format (NOT printf): 2 integer digits + 3 decimals → "46.630".
        // (The old "06.3f" was a printf-style specifier that .NET mis-parsed into "4663f".)
        return $"{m}:{s:00.000}";
    }

    /// <summary>
    /// Formats a lap-time string that may be a raw decimal seconds value.
    /// If the string cannot be parsed, it is returned unchanged.
    /// </summary>
    public static string FormatLapTimeStr(string raw)
    {
        if (!double.TryParse(raw, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double secs))
            return raw;
        return FormatLapTime(secs);
    }

    /// <summary>Returns true when the CSV filename looks like a race session.</summary>
    public static bool IsRaceFile(string path)
    {
        string name = System.IO.Path.GetFileName(path).ToLowerInvariant();
        return name.Contains("_race") || name.Contains("_race2")
            || name.Contains("_race3") || name.Contains("_race4");
    }

    /// <summary>
    /// Returns true when the CSV filename is a valid source for driver-profile learning:
    /// race or practice sessions. Quali is excluded — single hot/out-laps are unrepresentative
    /// of tyre degradation and short runs are already dropped by the stint-length filter.
    /// </summary>
    public static bool IsProfileSourceFile(string path)
    {
        string name = System.IO.Path.GetFileName(path).ToLowerInvariant();
        return IsRaceFile(path) || name.Contains("_practice");
    }
}
