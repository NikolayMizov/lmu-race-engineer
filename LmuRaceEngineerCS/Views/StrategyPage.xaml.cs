using LmuRaceEngineerCS.Models;
using LmuRaceEngineerCS.Shared;
using LmuRaceEngineerCS.Strategy;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace LmuRaceEngineerCS.Views;

public partial class StrategyPage : System.Windows.Controls.UserControl
{
    private static readonly string[] CompoundShort = ["S", "M", "H", "W"];
    private static readonly string   LapsDir = Path.Combine(AppContext.BaseDirectory, "laps");

    private readonly DispatcherTimer _inputTimer;
    private StrategyRecommendation? _lastRec;
    private readonly Dictionary<string, OverlayWindow> _overlays = new();
    private string _lastHistTrack = "";

    public StrategyPage()
    {
        InitializeComponent();

        // Subscribe to strategy engine output (fires every ~12 s on thread-pool)
        App.Telem.RecommendationUpdated += OnRecommendationUpdated;

        // Poll snapshot for the input summary row at 500 ms — matches rest of app
        _inputTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _inputTimer.Tick += (_, _) => RefreshInputSummary();
        _inputTimer.Start();

        Unloaded += OnUnloaded;

        // Show whatever recommendation is already available (page navigated to mid-race)
        var existing = App.Telem.LatestRecommendation();
        if (existing != null) UpdateRecommendation(existing);
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        App.Telem.RecommendationUpdated -= OnRecommendationUpdated;
        _inputTimer.Stop();
        foreach (var ov in _overlays.Values) ov.Close();
        _overlays.Clear();
    }

    private void OnRecommendationUpdated(object? sender, StrategyRecommendation rec)
        => Dispatcher.InvokeAsync(() => UpdateRecommendation(rec));

    // ── Pop-out (these cards are what the Engineer tab hosts) ─────────────────
    // Routed through App.Overlays so a widget is never opened twice and the window
    // survives tab navigation.
    private void PopOut_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string key) return;
        string title = key switch
        {
            "strat-rec"    => "Strategy Recommendation",
            "strat-pitwin" => "Pit Window",
            "strat-proj"   => "Projections",
            "strat-rival"  => "Rival Intel",
            _              => key,
        };
        App.Overlays.Open(key, title);
    }

    // ── Refresh input summary (500 ms) ────────────────────────────────────────

    private void RefreshInputSummary()
    {
        var snap = App.Telem.Snapshot();
        if (snap == null) return;

        TbInputAvgLap.Text    = snap.AvgLapTime   > 0 ? FormatLapTime(snap.AvgLapTime) : "—";
        TbInputAvgFuel.Text   = snap.AvgFuel       > 0 ? $"{snap.AvgFuel * 100:F2}%" : "—";
        // Tire health = remaining tread (LMU mWear is 1.0 = new); show the lowest corner.
        double minHealth = snap.TireWear.Length == 4 ? snap.TireWear.Min() * 100 : snap.WorstWear * 100;
        TbInputWear.Text      = snap.WorstWear     > 0 ? $"{minHealth:F0}%" : "—";
        TbInputStint.Text     = snap.StintLaps.ToString();
        TbInputLapsLeft.Text  = snap.LapsRemaining > 0 ? $"{snap.LapsRemaining:F1}" : "—";

        // Compound — use front tire
        int ct = snap.CompoundTypes.Length > 0 ? snap.CompoundTypes[0] : -1;
        TbInputCompound.Text = ct >= 0 && ct < CompoundShort.Length ? CompoundShort[ct] : "—";

        // Colour by tire health (lower = worse)
        TbInputWear.Foreground = minHealth switch
        {
            < 20 => Brush("RedBrush"),
            < 40 => Brush("YellowBrush"),
            _    => Brush("GreenBrush"),
        };

        // Colour staleness of recommendation
        if (_lastRec != null)
        {
            double age = (DateTimeOffset.Now - _lastRec.ComputedAt).TotalSeconds;
            TbStratAction.Opacity = age > 60 ? 0.35 : 1.0;
        }

        // Reload historical pace when track changes (cheap: only on change)
        if (snap.TrackName != _lastHistTrack)
        {
            _lastHistTrack = snap.TrackName;
            LoadHistoricalPace(snap.TrackName);
        }
    }

    // ── Historical pace ───────────────────────────────────────────────────────

    private void LoadHistoricalPace(string trackName)
    {
        SpHistoricalRows.Children.Clear();

        if (!Directory.Exists(LapsDir) || trackName.Length == 0)
        {
            TbHistLabel.Text = "No data";
            return;
        }

        string trackKey = RaceUtils.SafeName(trackName);
        var files = Directory.GetFiles(LapsDir, "*.csv")
                             .Where(f => {
                                 string fn = Path.GetFileName(f);
                                 return fn.StartsWith(trackKey, StringComparison.OrdinalIgnoreCase)
                                     && RaceUtils.IsRaceFile(fn);
                             })
                             .ToArray();

        if (files.Length == 0)
        {
            TbHistLabel.Text = $"{trackName}  ·  no history";
            SpHistoricalRows.Children.Add(new TextBlock
            {
                Text = "No previous sessions at this track yet.",
                FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
                Foreground = Brush("DimBrush"),
            });
            return;
        }

        // Load all laps from matching files
        var allLaps = new List<(string Mode, double Time)>();
        foreach (var f in files)
        {
            try
            {
                string[] lines = File.ReadAllLines(f);
                for (int i = 1; i < lines.Length; i++)
                {
                    string[] p = lines[i].Split(',');
                    if (p.Length < 6) continue;
                    string mode = p[1].ToLower() switch
                    {
                        "push" => "push",
                        "save" => "save",
                        _      => "normal",
                    };
                    if (double.TryParse(p[5], NumberStyles.Any, CultureInfo.InvariantCulture,
                            out double t) && t > 10)
                        allLaps.Add((mode, t));
                }
            }
            catch { }
        }

        int totalLaps = allLaps.Count;
        TbHistLabel.Text = $"{trackName}  ·  {files.Length} session{(files.Length == 1 ? "" : "s")}, {totalLaps} laps";

        if (totalLaps == 0) return;

        // Column widths: MODE | LAPS | AVG TIME | BEST TIME | Δ AVG | 10-LAP IMPACT
        int[] w = [85, 45, 100, 100, 85, 140];
        string[] hdrs = ["MODE", "LAPS", "AVG TIME", "BEST TIME", "Δ AVG", "10-LAP GAIN"];

        SpHistoricalRows.Children.Add(MakeHistRow(w, hdrs, isHeader: true));
        SpHistoricalRows.Children.Add(MakeHistSep());

        double normalAvg = HistAvg(allLaps, "normal");

        foreach (var (tag, label) in new[] { ("normal","NORMAL"), ("push","PUSH"), ("save","SAVE") })
        {
            var subset = allLaps.Where(l => l.Mode == tag).Select(l => l.Time).ToList();
            if (subset.Count == 0) continue;
            double avg  = subset.Average();
            double best = subset.Min();
            double delta = normalAvg > 0 ? avg - normalAvg : 0;
            double tenLapImpact = delta * 10;

            string modeColor = tag == "push" ? "#ff6666" : tag == "save" ? "#55ee88" : "#c7c7c7";
            string deltaStr = delta == 0 ? "—" : delta > 0 ? $"+{delta:F3}s" : $"{delta:F3}s";
            string deltaColor = delta < -0.001 ? "#55ee88" : delta > 0.001 ? "#ff6666" : "#939393";

            string impact = delta == 0 ? "—"
                : tenLapImpact < 0 ? $"{tenLapImpact:F1}s faster"
                : $"+{tenLapImpact:F1}s slower";
            string impactColor = tenLapImpact < 0 ? "#55ee88" : tenLapImpact > 0 ? "#ff6666" : "#939393";

            string[] vals = [label, subset.Count.ToString(), FmtLap(avg), FmtLap(best), deltaStr, impact];
            string[] colors = [modeColor, "#939393", "#d0d0d0", "#9b72e8", deltaColor, impactColor];

            SpHistoricalRows.Children.Add(MakeHistRow(w, vals, isHeader: false, colors));
        }
    }

    private static double HistAvg(List<(string Mode, double Time)> laps, string mode)
    {
        var t = laps.Where(l => l.Mode == mode).Select(l => l.Time).ToList();
        return t.Count > 0 ? t.Average() : 0;
    }

    private FrameworkElement MakeHistRow(int[] widths, string[] values,
                                         bool isHeader, string[]? colors = null)
    {
        var grid = new Grid { Margin = new Thickness(0, isHeader ? 0 : 2, 0, 0) };
        foreach (int wi in widths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(wi) });

        for (int i = 0; i < values.Length; i++)
        {
            string hex = colors?[i] ?? (isHeader ? "#606060" : "#939393");
            var tb = new TextBlock
            {
                Text       = values[i],
                FontFamily = isHeader ? new FontFamily("Segoe UI") : new FontFamily("Consolas"),
                FontSize   = isHeader ? 9 : 11,
                FontWeight = isHeader ? FontWeights.Bold : FontWeights.Normal,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                Padding    = new Thickness(0, 1, 6, 1),
            };
            Grid.SetColumn(tb, i);
            grid.Children.Add(tb);
        }
        return grid;
    }

    private FrameworkElement MakeHistSep() => new System.Windows.Shapes.Rectangle
    {
        Height = 1, Margin = new Thickness(0, 3, 0, 3),
        Fill   = Brush("BorderBrush2"),
    };

    private static string FmtLap(double secs) => RaceUtils.FormatLapTime(secs);

    // ── Update recommendation display ─────────────────────────────────────────

    private void UpdateRecommendation(StrategyRecommendation rec)
    {
        _lastRec = rec;

        // ── Top card ──────────────────────────────────────────────────────
        TbStratAction.Text     = rec.ActionLabel;
        TbStratAction.Foreground = ActionColor(rec.Action);
        TbStratRationale.Text  = rec.Rationale;

        TbStratPitLap.Text = rec.RecommendedPitLap > 0
            ? $"Lap {rec.RecommendedPitLap}"
            : "—";

        TbStratUncertainty.Text = $"{rec.FinishTimeUncertainty:F0}";

        double scoreGap = rec.AltScore > 0 ? Math.Abs(rec.TopScore - rec.AltScore) : 0;
        TbStratConfidence.Text = scoreGap switch
        {
            > 0.25 => "HIGH",
            > 0.10 => "MED",
            _      => "LOW",
        };
        TbStratConfidence.Foreground = scoreGap switch
        {
            > 0.25 => Brush("GreenBrush"),
            > 0.10 => Brush("YellowBrush"),
            _      => Brush("DimBrush"),
        };

        TbStratComputedAt.Text = $"Lap {rec.SnapshotLap}  ·  just now";
        TbStratAction.Opacity  = 1.0;

        // ── Pit window card ───────────────────────────────────────────────
        var snap = App.Telem.Snapshot();
        TbPitRecommended.Text = rec.RecommendedPitLap > 0
            ? $"Lap {rec.RecommendedPitLap}"
            : snap?.PitLap.HasValue == true ? $"Lap {snap.PitLap:F0}" : "—";
        TbPitEarliest.Text = snap != null ? $"Lap {snap.EarliestPit:F0}" : "—";
        TbPitLatest.Text   = snap?.LatestPit.HasValue == true ? $"Lap {snap.LatestPit:F0}" : "—";
        TbPitLimiter.Text  = snap?.PitLimiter?.ToUpperInvariant() ?? "—";

        // ── Projections card ──────────────────────────────────────────────
        TbProjPosition.Text    = $"P{rec.ProjectedPosition}";
        TbProjFuelMargin.Text  = rec.FuelSafetyMargin >= 0
            ? $"{rec.FuelSafetyMargin:F1} laps"
            : "LOW";
        TbProjFuelMargin.Foreground = rec.FuelSafetyMargin switch
        {
            >= 2   => Brush("GreenBrush"),
            >= 0.5 => Brush("YellowBrush"),
            _      => Brush("RedBrush"),
        };

        double cliff = rec.TireCliffRisk;
        TbProjCliffRisk.Text = cliff switch
        {
            > 0.66 => "HIGH",
            > 0.33 => "MEDIUM",
            _      => "LOW",
        };
        TbProjCliffRisk.Foreground = cliff switch
        {
            > 0.66 => Brush("RedBrush"),
            > 0.33 => Brush("YellowBrush"),
            _      => Brush("GreenBrush"),
        };

        // ── Rival intel card ──────────────────────────────────────────────
        TbRivalGap.Text = rec.NearestRivalGap.HasValue
            ? $"{rec.NearestRivalGap.Value:F1} s"
            : "—";

        TbRivalPitLap.Text = rec.RivalEstimatedPitLap.HasValue
            ? $"Lap ~{rec.RivalEstimatedPitLap.Value:F0}"
            : "—";

        TbUndercutThreat.Text       = rec.UndercutThreat ? "YES" : "NO";
        TbUndercutThreat.Foreground = rec.UndercutThreat
            ? Brush("RedBrush") : Brush("DimBrush");

        TbOvercutOpp.Text       = rec.OvercutOpportunity ? "YES" : "NO";
        TbOvercutOpp.Foreground = rec.OvercutOpportunity
            ? Brush("GreenBrush") : Brush("DimBrush");

        // ── Candidates table ──────────────────────────────────────────────
        RebuildCandidates(rec.Candidates);
    }

    // ── Candidate rows ────────────────────────────────────────────────────────

    private void RebuildCandidates(IReadOnlyList<StrategyCandidate> candidates)
    {
        SpCandidates.Children.Clear();

        for (int i = 0; i < candidates.Count; i++)
        {
            var c   = candidates[i];
            bool top = i == 0;

            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Background highlight for top candidate
            if (top)
                row.Background = new SolidColorBrush(Color.FromArgb(30, 204, 34, 34));

            void AddCell(int col, string text, Brush fg, bool bold = false)
            {
                var tb = new TextBlock
                {
                    Text       = text,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize   = 14,
                    Foreground = fg,
                    FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Padding    = new Thickness(0, 2, 8, 2),
                };
                Grid.SetColumn(tb, col);
                row.Children.Add(tb);
            }

            string pitLapTxt = c.PitOnLap > 0 ? $"Lap {c.PitOnLap}" : "—";
            string scoreTxt  = $"{c.Score:F3}";
            string fuelTxt   = c.FuelSafetyMargin >= 0 ? $"{c.FuelSafetyMargin:F1}L" : "LOW";
            string cliffTxt  = c.TireCliffRisk switch
            {
                > 0.66 => "HIGH",
                > 0.33 => "MED",
                _      => "LOW",
            };

            Brush actionFg = top ? ActionColor(c.ActionType) : Brush("Dim2Brush");
            Brush scoreFg  = top ? Brush("GreenBrush") : Brush("DimBrush");
            Brush fuelFg   = c.FuelSafetyMargin >= 2 ? Brush("GreenBrush")
                           : c.FuelSafetyMargin >= 0  ? Brush("YellowBrush")
                           : Brush("RedBrush");
            Brush cliffFg  = c.TireCliffRisk switch
            {
                > 0.66 => Brush("RedBrush"),
                > 0.33 => Brush("YellowBrush"),
                _      => Brush("GreenBrush"),
            };

            AddCell(0, ActionLabel(c.ActionType), actionFg, top);
            AddCell(1, pitLapTxt,                 Brush("FgBrush"), top);
            AddCell(2, scoreTxt,                  scoreFg,  top);
            AddCell(3, fuelTxt,                   fuelFg,   false);
            AddCell(4, cliffTxt,                  cliffFg,  false);
            AddCell(5, c.Rationale,               Brush("Dim2Brush"), false);

            SpCandidates.Children.Add(row);

            // Separator between candidates
            if (i < candidates.Count - 1)
                SpCandidates.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Height     = 1,
                    Fill       = Brush("BorderBrush2"),
                    Margin     = new Thickness(0, 2, 0, 4),
                });
        }

        if (candidates.Count == 0)
        {
            SpCandidates.Children.Add(new TextBlock
            {
                Text       = "No candidates — enter a race session",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize   = 14,
                Foreground = Brush("DimBrush"),
                Margin     = new Thickness(0, 8, 0, 0),
            });
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatLapTime(double s)
    {
        int    m   = (int)(s / 60);
        double rem = s - m * 60;
        return m > 0 ? $"{m}:{rem:00.0}" : $"{s:F1} s";
    }

    private static string ActionLabel(StrategyActionType action) => action switch
    {
        StrategyActionType.StayOut         => "STAY OUT",
        StrategyActionType.PitNowFullFuel  => "PIT NOW",
        StrategyActionType.PitNowShortFuel => "SHORT PIT",
        StrategyActionType.SaveFuel        => "SAVE FUEL",
        StrategyActionType.PushUndercut    => "UNDERCUT",
        StrategyActionType.CoverRival      => "COVER RIVAL",
        _                                  => "—",
    };

    private Brush ActionColor(StrategyActionType action) => action switch
    {
        StrategyActionType.PitNowFullFuel  => Brush("AccentBrush"),
        StrategyActionType.PitNowShortFuel => Brush("AccentBrush"),
        StrategyActionType.PushUndercut    => Brush("PushBrush"),
        StrategyActionType.SaveFuel        => Brush("SaveBrush"),
        StrategyActionType.StayOut         => Brush("GreenBrush"),
        StrategyActionType.CoverRival      => Brush("BlueBrush"),
        _                                  => Brush("DimBrush"),
    };

    private Brush Brush(string key)
        => (Brush)Application.Current.Resources[key];
}
