using LmuRaceEngineerCS.Models;
using LmuRaceEngineerCS.Shared;
using System.Globalization;
using System.IO;

namespace LmuRaceEngineerCS.Services;

/// <summary>
/// Analyses historical race and practice CSV data to learn per-track tire-degradation
/// onset for this driver.  The result fine-tunes the strategy engine's pit-timing without
/// ever overriding current-session wear-rate data.
///
/// Algorithm (per track):
///   1. Parse all matching CSV files.
///   2. Split rows into stints (reset when max wear drops >5 pp = pitstop).
///   3. Discard stints shorter than MIN_STINT_LAPS.
///   4. Within each stint, normalise lap times to the stint best.
///   5. Bin (max_wear, delta_time) into 10 pp buckets.
///   6. DegOnset = lowest wear bucket with mean delta > ONSET_THRESHOLD_S.
///   7. Bayesian-blend result with generic 0.60 weighted by sample count.
/// </summary>
public sealed class ProfileLearner
{
    // ── Tuning constants ──────────────────────────────────────────────────────
    private const int    MinStintLaps      = 4;      // ignore very short stints
    private const double OnsetThresholdS   = 0.30;   // seconds above best → deg starts
    private const int    FullConfidenceN   = 5;      // stints for 100 % confidence
    private const double GenericOnset      = 0.60;   // fallback when no data
    private const double WearDropPitReset  = 0.05;   // 5 pp wear drop = new stint

    // ── State ─────────────────────────────────────────────────────────────────
    private static readonly string LapsDir =
        Path.Combine(AppContext.BaseDirectory, "laps");

    private readonly DriverProfile _profile;
    private readonly object        _lock = new();

    public DriverProfile Profile => _profile;

    // ── Constructor ───────────────────────────────────────────────────────────
    public ProfileLearner()
    {
        _profile = DriverProfile.Load();
    }

    /// <summary>
    /// Re-analyse all CSVs for every track found in <c>laps/</c>.
    /// Call once at startup on a background thread.
    /// </summary>
    public void LearnAll()
    {
        if (!Directory.Exists(LapsDir)) return;

        var trackKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Phase 1: use known profile keys (most accurate — set by LearnTrack with real track name)
        lock (_lock)
        {
            foreach (var (track, _) in _profile.All())
                trackKeys.Add(track);
        }

        // Phase 2: for new CSVs not yet in the profile, extract a heuristic key
        foreach (var f in Directory.GetFiles(LapsDir, "*.csv"))
        {
            string fname = Path.GetFileName(f);
            bool matched = trackKeys.Any(k =>
                fname.StartsWith(k + "_", StringComparison.OrdinalIgnoreCase));
            if (!matched)
            {
                string key = TrackKeyFromFile(fname);
                if (key.Length > 0) trackKeys.Add(key);
            }
        }

        foreach (var key in trackKeys)
            LearnTrack(key);
    }

    /// <summary>
    /// Re-analyse all CSVs whose filename starts with <paramref name="safeName"/>_.
    /// Call after each race session completes to refresh the current track.
    /// </summary>
    public void LearnTrack(string safeName)
    {
        if (!Directory.Exists(LapsDir)) return;

        string prefix = safeName + "_";
        var files = Directory.GetFiles(LapsDir, "*.csv")
                             .Where(f => Path.GetFileName(f).StartsWith(prefix,
                                         StringComparison.OrdinalIgnoreCase))
                             .ToArray();

        if (files.Length == 0) return;

        // Use race and practice sessions for profile (quali excluded — see IsProfileSourceFile)
        var sourceFiles = files.Where(IsProfileSourceFile).ToArray();
        if (sourceFiles.Length == 0) return;

        var allPoints = new List<(double wear, double delta)>();
        int stintCount = 0;

        foreach (var file in sourceFiles)
        {
            var rows = ParseCsv(file);
            if (rows.Count < MinStintLaps) continue;

            foreach (var stint in SplitStints(rows))
            {
                if (stint.Count < MinStintLaps) continue;

                double best = stint.Min(r => r.LapTime);
                // Collect (max_wear, delta_time) for this stint
                foreach (var r in stint)
                {
                    double wear = r.WorstWear;  // accumulated wear 0–1
                    double delta = r.LapTime - best;
                    if (delta >= 0 && delta < 10.0)   // sanity gate: ignore >10s outliers
                        allPoints.Add((wear, delta));
                }
                stintCount++;
            }
        }

        if (stintCount == 0) return;

        double rawOnset = DetectOnset(allPoints);

        // Bayesian blend: weight ramps from 0→1 as stintCount grows to FullConfidenceN
        double weight  = Math.Min(1.0, stintCount / (double)FullConfidenceN);
        double blended = weight * rawOnset + (1.0 - weight) * GenericOnset;
        blended = Math.Clamp(blended, 0.35, 0.85);  // hard bounds for safety

        var profile = new TrackProfile
        {
            DegOnset     = Math.Round(blended, 3),
            SampleStints = stintCount,
            LastUpdated  = DateTimeOffset.Now,
        };

        lock (_lock)
        {
            _profile.Set(safeName, profile);
            _profile.Save();
        }
    }

    /// <summary>Clears all learned profiles in memory and saves an empty profile to disk.</summary>
    public void ResetProfile()
    {
        lock (_lock)
        {
            _profile.Clear();
            _profile.Save();
        }
    }

    /// <summary>Returns the blended onset for a track, or the generic value if unknown.</summary>
    public double GetOnset(string trackName)
    {
        string key = SafeName(trackName);
        lock (_lock)
        {
            return _profile.Get(key)?.DegOnset ?? GenericOnset;
        }
    }

    /// <summary>True when at least 2 stints of data exist for this track.</summary>
    public bool IsProfileActive(string trackName)
    {
        string key = SafeName(trackName);
        lock (_lock)
        {
            return (_profile.Get(key)?.SampleStints ?? 0) >= 2;
        }
    }

    // ── CSV parsing ───────────────────────────────────────────────────────────
    private record CsvRow(int Lap, double LapTime, double WorstWear);

    private static List<CsvRow> ParseCsv(string path)
    {
        var rows = new List<CsvRow>();
        try
        {
            using var sr = new StreamReader(path);
            string? header = sr.ReadLine();
            if (header is null) return rows;

            // Expected: lap,mode,s1,s2,s3,lap_time,fuel_start_L,fl_wear_%,fr_wear_%,rl_wear_%,rr_wear_%
            while (!sr.EndOfStream)
            {
                string? line = sr.ReadLine();
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',');
                if (parts.Length < 11) continue;

                if (!int.TryParse(parts[0].Trim(), out int lap)) continue;
                if (!TryF(parts[5], out double lapTime))  continue;
                if (lapTime < 10) continue;   // bogus lap

                TryF(parts[7],  out double fl);
                TryF(parts[8],  out double fr);
                TryF(parts[9],  out double rl);
                TryF(parts[10], out double rr);
                // CSV columns are tread REMAINING (100 = new). Convert the worst (least
                // remaining) corner to accumulated wear 0–1 so the onset/stint logic below
                // sees wear that climbs as the tyre ages (and drops at a pitstop).
                double minRemaining = Math.Min(Math.Min(fl, fr), Math.Min(rl, rr));
                double worstWear    = Math.Clamp(1.0 - minRemaining / 100.0, 0.0, 1.0);

                rows.Add(new CsvRow(lap, lapTime, worstWear));
            }
        }
        catch { /* skip unreadable files */ }
        return rows;
    }

    private static bool TryF(string s, out double v) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    // ── Stint splitting ───────────────────────────────────────────────────────
    private static List<List<CsvRow>> SplitStints(List<CsvRow> rows)
    {
        var stints  = new List<List<CsvRow>>();
        var current = new List<CsvRow>();

        for (int i = 0; i < rows.Count; i++)
        {
            if (i == 0)
            {
                current.Add(rows[i]);
                continue;
            }

            // Detect pit stop: max wear drops more than 5 pp → new tires
            double prev = rows[i - 1].WorstWear;
            double curr = rows[i].WorstWear;
            if (prev - curr > WearDropPitReset && current.Count > 0)
            {
                stints.Add(current);
                current = new List<CsvRow>();
            }
            current.Add(rows[i]);
        }
        if (current.Count > 0) stints.Add(current);
        return stints;
    }

    // ── Onset detection ───────────────────────────────────────────────────────
    /// <summary>
    /// Estimates the wear fraction at which this driver starts losing significant lap
    /// time. Physical tread barely moves across a real stint (it may only span
    /// ~0.05–0.20 accumulated wear), so binning purely by tread piles almost every lap
    /// into the lowest bucket and the onset rarely trips. Instead we read the onset off
    /// the GRIP/PERFORMANCE curve: bin by wear only to smooth noise, then find where the
    /// mean lap-time delta crosses <see cref="OnsetThresholdS"/> and interpolate the wear
    /// at that crossing. If the curve never reaches the threshold within the observed
    /// band but is trending up, we extrapolate from its last two samples so a short,
    /// narrow-range real stint still personalises the profile.
    /// </summary>
    private static double DetectOnset(List<(double wear, double delta)> points)
    {
        if (points.Count < MinStintLaps) return GenericOnset;

        // Fine bins (0.05 wear) purely to average out per-lap noise.
        const int Bins = 20;
        var sums   = new double[Bins];
        var counts = new int[Bins];
        foreach (var (wear, delta) in points)
        {
            int bin = Math.Clamp((int)(wear * Bins), 0, Bins - 1);
            sums[bin]   += delta;
            counts[bin] += 1;
        }

        // Collapse to the populated (wearCentre, meanDelta) performance curve, low→high.
        var curve = new List<(double wear, double meanDelta)>();
        for (int b = 0; b < Bins; b++)
            if (counts[b] >= 2)
                curve.Add(((b + 0.5) / Bins, sums[b] / counts[b]));

        if (curve.Count < 2) return GenericOnset;

        // Already degrading at the earliest sample ⇒ very early onset.
        if (curve[0].meanDelta >= OnsetThresholdS)
            return Math.Clamp(curve[0].wear, 0.35, 0.90);

        // First segment whose mean delta crosses the threshold → interpolate the wear.
        for (int i = 1; i < curve.Count; i++)
        {
            var lo = curve[i - 1];
            var hi = curve[i];
            if (hi.meanDelta >= OnsetThresholdS)
            {
                double t = (OnsetThresholdS - lo.meanDelta) / (hi.meanDelta - lo.meanDelta);
                return Math.Clamp(lo.wear + t * (hi.wear - lo.wear), 0.35, 0.90);
            }
        }

        // Never crossed within the observed band. Extrapolate the trend of the last two
        // samples to estimate where it *would* cross, so narrow-range data still adapts.
        var a = curve[^2];
        var c = curve[^1];
        double slope = (c.meanDelta - a.meanDelta) / Math.Max(1e-6, c.wear - a.wear);
        if (slope > 1e-6)
            return Math.Clamp(c.wear + (OnsetThresholdS - c.meanDelta) / slope, 0.35, 0.90);

        // Flat / improving performance ⇒ tyres hold up; driver can run long.
        return 0.75;
    }

    // ── File helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the track safe-name prefix from a CSV filename.
    /// File format: {SafeTrack}_{SafeCar}_{SafeSession}_{yyyyMMdd}_{HHmmss}.csv
    /// The last two segments are always the timestamp (8 + 6 digits), and the session
    /// type is one segment before that. We strip those known-fixed suffixes and use
    /// everything before as the track+car prefix, then let LearnTrack's prefix matching
    /// handle the rest. As a safe heuristic we take up to (total − 4) segments so that
    /// at minimum the timestamp and session are excluded.
    /// </summary>
    private static string TrackKeyFromFile(string filename)
    {
        int dot = filename.LastIndexOf('.');
        string name = dot > 0 ? filename[..dot] : filename;

        var parts = name.Split('_');
        // Leave at least 4 trailing segments for: car, session, date, time
        int maxParts = Math.Max(1, parts.Length - 4);
        return string.Join("_", parts.Take(maxParts));
    }

    private static bool IsRaceFile(string path) => RaceUtils.IsRaceFile(path);

    private static bool IsProfileSourceFile(string path) => RaceUtils.IsProfileSourceFile(path);

    /// <summary>Delegates to <see cref="RaceUtils.SafeName"/> — shared implementation.</summary>
    public static string SafeName(string s) => RaceUtils.SafeName(s);
}
