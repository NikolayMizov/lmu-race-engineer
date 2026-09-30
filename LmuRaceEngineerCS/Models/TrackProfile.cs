using System.IO;
using System.Text.Json;

namespace LmuRaceEngineerCS.Models;

/// <summary>
/// Learned tire-degradation characteristics for one driver at one track.
/// All values derived from historical race CSV data.
/// </summary>
public record TrackProfile
{
    /// <summary>
    /// Wear fraction (0–1) at which this driver personally starts losing significant
    /// lap time. Generic model uses 0.60; values below mean earlier degradation onset.
    /// </summary>
    public double DegOnset { get; init; } = 0.60;

    /// <summary>Number of qualifying stints (≥ 4 laps) used to compute this profile.</summary>
    public int SampleStints { get; init; }

    public DateTimeOffset LastUpdated { get; init; } = DateTimeOffset.MinValue;
}

/// <summary>
/// Collection of per-track profiles for the driver. Persisted as JSON.
/// Thread-safe for reads after startup; writes are serialised by ProfileLearner.
/// </summary>
public class DriverProfile
{
    private readonly Dictionary<string, TrackProfile> _tracks =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly string ProfilePath =
        Path.Combine(AppContext.BaseDirectory, "driver_profile.json");

    /// <summary>Returns the profile for <paramref name="trackKey"/> or null if not yet learned.</summary>
    public TrackProfile? Get(string trackKey) =>
        _tracks.TryGetValue(trackKey, out var p) ? p : null;

    public void Set(string trackKey, TrackProfile profile) =>
        _tracks[trackKey] = profile;

    public IEnumerable<(string Track, TrackProfile Profile)> All() =>
        _tracks.Select(kv => (kv.Key, kv.Value));

    public void Clear() => _tracks.Clear();

    public static DriverProfile Load()
    {
        try
        {
            if (!File.Exists(ProfilePath)) return new DriverProfile();
            string json = File.ReadAllText(ProfilePath);
            var dict = JsonSerializer.Deserialize<Dictionary<string, TrackProfile>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var dp = new DriverProfile();
            if (dict != null)
                foreach (var kv in dict)
                    dp._tracks[kv.Key] = kv.Value;
            return dp;
        }
        catch { return new DriverProfile(); }
    }

    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(_tracks,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ProfilePath, json);
        }
        catch { /* non-critical */ }
    }
}
