using LmuRaceEngineerCS.Shared;
using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace LmuRaceEngineerCS.Views;

/// <summary>
/// Lap-by-lap log: one row per lap with time, sector splits, tire health used that
/// lap (avg of 4 corners), and tire health at lap start (avg-of-4 or lowest-corner,
/// user's choice). A lap-time chart sits above it; hovering a lap shows its detail.
///
/// Tire health = remaining tread (LMU mWear: 1.0 = new), so the raw CSV wear columns
/// ARE health (new ≈ 100%). "Used this lap" = health(start n) − health(start n+1).
/// </summary>
public partial class LapsPage : UserControl
{
    private const double PitMargin     = 5.0;    // health % rise between laps ⇒ tires changed
    private const double FuelPitMargin = 0.02;   // VE fraction rise between laps ⇒ refuelled

    private readonly string _lapsDir = Path.Combine(AppContext.BaseDirectory, "laps");
    private string?        _currentFile;
    private List<LapRow>   _rows = [];
    private string         _sessionFilter = "All";
    private bool           _useLowest;       // false = avg of 4, true = lowest corner
    private bool           _ready;

    public LapsPage()
    {
        InitializeComponent();
        FilterCombo.SelectionChanged += FilterCombo_Changed;
        AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => SaveLayout()));
        Loaded   += (_, _) => LoadLayout();
        Unloaded += (_, _) => SaveLayout();
        _ready = true;
        RefreshFiles();
    }

    // ── Layout persistence ────────────────────────────────────────────────────

    private static GridLength Star(double v) => new(Math.Max(1, v), GridUnitType.Star);

    private void SaveLayout()
    {
        var grid = (Grid)Content;
        double chartH = grid.RowDefinitions[2].ActualHeight;
        double tableH = grid.RowDefinitions[4].ActualHeight;
        if (chartH == 0 && tableH == 0) return;
        var s = LayoutState.Load();
        s.LapsRows = [chartH, tableH];
        s.Save();
    }

    private void LoadLayout()
    {
        var s = LayoutState.Load();
        if (s.LapsRows.Length != 2) return;
        var grid = (Grid)Content;
        grid.RowDefinitions[2].Height = Star(s.LapsRows[0]);
        grid.RowDefinitions[4].Height = Star(s.LapsRows[1]);
    }

    // ── File management ─────────────────────────────────────────────────────────

    private void RefreshFiles()
    {
        SessionCombo.Items.Clear();
        if (!Directory.Exists(_lapsDir)) return;

        var files = Directory.GetFiles(_lapsDir, "*.csv")
                             .OrderByDescending(File.GetLastWriteTime)
                             .Where(f => MatchesFilter(Path.GetFileName(f)))
                             .ToArray();
        foreach (var f in files)
            SessionCombo.Items.Add(Path.GetFileName(f));

        if (SessionCombo.Items.Count > 0) SessionCombo.SelectedIndex = 0;
        else { _rows = []; LapTable.ItemsSource = null; BuildChart(_rows); }
    }

    private bool MatchesFilter(string fn)
    {
        string lower = fn.ToLowerInvariant();
        return _sessionFilter switch
        {
            "Race"     => lower.Contains("_race"),
            "Practice" => lower.Contains("_practice"),
            "Quali"    => lower.Contains("_qual"),
            _          => true,
        };
    }

    private void LoadFile(string fileName)
    {
        _currentFile = Path.Combine(_lapsDir, fileName);
        _rows = BuildRows(ReadRaw(_currentFile));
        ApplyHealthBasis();   // sets HealthText + table + chart
    }

    // ── CSV → raw laps ──────────────────────────────────────────────────────────
    private record Raw(int Lap, string Mode, string S1, string S2, string S3,
                       double TimeSecs, double FuelStart, double[] Corners);

    private static List<Raw> ReadRaw(string path)
    {
        var raws = new List<Raw>();
        if (!File.Exists(path)) return raws;
        try
        {
            string[] lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i++)
            {
                string[] p = lines[i].Split(',');
                if (p.Length < 11) continue;
                if (!int.TryParse(p[0].Trim(), out int lap)) continue;
                double.TryParse(p[5].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double t);
                if (t <= 10) continue;

                // fuel_start_L = Virtual-Energy fraction at lap start (0–1); −1 = missing.
                double fuelStart = double.TryParse(p[6].Trim(), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double fs) ? fs : -1.0;

                double[] corners =
                [
                    ParseD(p[7]), ParseD(p[8]), ParseD(p[9]), ParseD(p[10]),
                ];
                raws.Add(new Raw(lap, (p[1].Trim().Length > 0 ? p[1].Trim() : "normal"),
                                 RaceUtils.FormatLapTimeStr(p[2]),
                                 RaceUtils.FormatLapTimeStr(p[3]),
                                 RaceUtils.FormatLapTimeStr(p[4]),
                                 t, fuelStart, corners));
            }
        }
        catch { }
        // TODO (pending Niky's stricter data review): some real logs contain near-duplicate
        // rows — consecutive laps with identical sector/lap times (e.g. Imola race game-laps
        // 11≈12, 20≈21, 27≈28). Likely a telemetry double-report. Do NOT de-duplicate yet:
        // confirm the pattern against more sessions first, then decide whether to drop dupes
        // here (e.g. collapse consecutive rows with identical lap_time + wear) or fix at the
        // logger. Left as-is on purpose for now.
        // Keep the file's chronological (driven) order — the logger appends one row per lap
        // as it happens. Sorting by lap number corrupts the order when the game's lap counter
        // resets (formation / pit-out laps counted before the green flag, then the race
        // restarts at lap 1) — exactly the pit-start case.
        return raws;
    }

    // ── Raw → display rows (flags, usage) ───────────────────────────────────────

    /// <summary>Avg and lowest health across the reporting corners (values ≤ 0 = no reading).</summary>
    private static (double avg, double min) Health(double[] corners)
    {
        var v = corners.Where(x => x > 0).ToList();
        return v.Count == 0 ? (double.NaN, double.NaN) : (v.Average(), v.Min());
    }

    private static List<LapRow> BuildRows(List<Raw> raws)
    {
        int n = raws.Count;
        var avg = new double[n];
        var min = new double[n];
        var fuel = new double[n];
        for (int i = 0; i < n; i++)
        {
            (avg[i], min[i]) = Health(raws[i].Corners);
            fuel[i] = raws[i].FuelStart;   // VE fraction at lap start, −1 = missing
        }

        bool Valid(int i)  => i >= 0 && i < n && !double.IsNaN(avg[i]);
        bool ValidF(int i) => i >= 0 && i < n && fuel[i] >= 0;

        // Pit indicators between consecutive laps a→b:
        //   tyre change = health jumps up (>PitMargin); refuel = energy jumps up (>FuelPitMargin).
        // A stop is flagged on EITHER, so a fuel-only stop (tyres kept) is still marked IN/OUT.
        bool TyrePit(int a, int b)    => Valid(a) && Valid(b) && avg[b] > avg[a] + PitMargin;
        bool FuelPit(int a, int b)    => ValidF(a) && ValidF(b) && fuel[b] > fuel[a] + FuelPitMargin;
        bool PitBetween(int a, int b) => TyrePit(a, b) || FuelPit(a, b);

        var rows = new List<LapRow>(n);
        for (int i = 0; i < n; i++)
        {
            bool isOut = i == 0 || PitBetween(i - 1, i);
            bool isIn  = i < n - 1 && PitBetween(i, i + 1);

            // Tyre health used this lap — "PIT" only when tyres were actually changed.
            string used;
            if (i == n - 1)                     used = "FIN";
            else if (TyrePit(i, i + 1))         used = "PIT";
            else if (Valid(i) && Valid(i + 1))
            {
                double u = Math.Max(0, avg[i] - avg[i + 1]);
                used = u.ToString("0.0", CultureInfo.InvariantCulture);
            }
            else used = "—";

            // Energy @ lap start (VE %) and VE used this lap — "PIT" only when refuelled.
            double fs = raws[i].FuelStart;
            string energy = fs >= 0 ? $"{fs * 100:0}%" : "—";
            string nrgUsed;
            if (i == n - 1)                     nrgUsed = "—";
            else if (FuelPit(i, i + 1))         nrgUsed = "PIT";
            else if (ValidF(i) && ValidF(i + 1))
                nrgUsed = ((fuel[i] - fuel[i + 1]) * 100).ToString("0.0", CultureInfo.InvariantCulture);
            else nrgUsed = "—";

            rows.Add(new LapRow
            {
                Seq      = i + 1,                 // driven order (robust to lap-counter resets)
                LapNum   = raws[i].Lap,
                Lap      = raws[i].Lap.ToString(),
                Flag     = isOut ? "OUT" : isIn ? "IN" : "",
                Mode     = Cap(raws[i].Mode),
                ModeKey  = ModeKey(raws[i].Mode),
                Time     = RaceUtils.FormatLapTime(raws[i].TimeSecs),
                TimeSecs = raws[i].TimeSecs,
                S1 = raws[i].S1, S2 = raws[i].S2, S3 = raws[i].S3,
                Used     = used,
                AvgStart = avg[i],
                MinStart = min[i],
                Energy   = energy,
                NrgUsed  = nrgUsed,
            });
        }
        return rows;
    }

    // ── Health basis toggle (avg of 4 vs lowest corner) ─────────────────────────

    private void ApplyHealthBasis()
    {
        foreach (var r in _rows)
        {
            double h = _useLowest ? r.MinStart : r.AvgStart;
            r.HealthText = double.IsNaN(h) ? "—" : $"{h:0}%";
        }
        LapTable.ItemsSource = _rows;
        LapTable.Items.Refresh();
        BuildChart(_rows);
    }

    // ── Chart: X = lap, Y = lap time, hover = detail ────────────────────────────

    private void BuildChart(List<LapRow> rows)
    {
        var valid = rows.Where(r => r.TimeSecs > 10).OrderBy(r => r.Seq).ToList();
        ChartEmpty.Visibility = valid.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

        var model = new PlotModel
        {
            Background              = OxyColor.Parse("#161616"),
            PlotAreaBorderColor     = OxyColor.Parse("#242424"),
            PlotAreaBorderThickness = new OxyThickness(1),
            TextColor               = OxyColor.Parse("#939393"),
            IsLegendVisible         = false,
            Padding                 = new OxyThickness(8, 6, 16, 4),
            DefaultFontSize         = 10,
        };

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom, Title = "Lap", MinimumMajorStep = 1,
            Minimum = valid.Count > 0 ? valid[0].Seq - 0.5 : 0.5,
            TitleColor = OxyColor.Parse("#939393"), AxislineColor = OxyColor.Parse("#242424"),
            TicklineColor = OxyColor.Parse("#242424"), MajorGridlineColor = OxyColor.Parse("#1e1e1e"),
            MajorGridlineStyle = LineStyle.Solid, TextColor = OxyColor.Parse("#939393"), MinorTickSize = 0,
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left, Title = "Lap time",
            LabelFormatter = FmtAxisTime,
            TitleColor = OxyColor.Parse("#939393"), AxislineColor = OxyColor.Parse("#242424"),
            TicklineColor = OxyColor.Parse("#242424"), MajorGridlineColor = OxyColor.Parse("#1e1e1e"),
            MajorGridlineStyle = LineStyle.Solid, TextColor = OxyColor.Parse("#939393"), MinorTickSize = 0,
        });

        if (valid.Count == 0) { LapChart.Model = model; return; }

        // Connecting line (snaps hover to whole laps)
        var line = new LineSeries
        {
            Color = OxyColor.Parse("#2e2e2e"), StrokeThickness = 1.5, MarkerType = MarkerType.None,
            CanTrackerInterpolatePoints = false, RenderInLegend = false,
            // The coloured scatter dots sit on top and own the hover tooltip (they carry the
            // m:ss.mmm-formatted time in their Tag); the line only shows if you hover between laps.
            TrackerFormatString = "Lap {2:0}",
        };
        foreach (var r in valid) line.Points.Add(new DataPoint(r.Seq, r.TimeSecs));
        model.Series.Add(line);

        (string key, string label, OxyColor color)[] modes =
        [
            ("normal", "Normal", OxyColor.Parse("#c7c7c7")),
            ("push",   "Push",   OxyColor.Parse("#ff3333")),
            ("save",   "Save",   OxyColor.Parse("#33ee77")),
        ];
        foreach (var (key, label, color) in modes)
        {
            var pts = valid.Where(r => r.ModeKey == key).ToList();
            if (pts.Count == 0) continue;
            var sc = NewScatter(label, color, 5, filled: true);
            foreach (var r in pts) sc.Points.Add(Point(r));
            model.Series.Add(sc);
        }

        // Best lap — purple highlight
        double best = valid.Min(r => r.TimeSecs);
        var bestSc = NewScatter("Best lap", OxyColor.Parse("#9b72e8"), 7, filled: true);
        foreach (var r in valid.Where(r => Math.Abs(r.TimeSecs - best) < 0.001))
            bestSc.Points.Add(Point(r));
        model.Series.Add(bestSc);

        LapChart.Model = model;
    }

    private ScatterSeries NewScatter(string title, OxyColor color, double size, bool filled) => new()
    {
        Title = title, MarkerType = MarkerType.Circle, MarkerSize = size,
        MarkerFill = filled ? color : OxyColors.Transparent,
        MarkerStroke = OxyColors.Transparent,
        TrackerFormatString = "{0}  ·  Lap {2:0}\n{Tag}",
    };

    private ScatterPoint Point(LapRow r)
    {
        double h = _useLowest ? r.MinStart : r.AvgStart;
        string health = double.IsNaN(h) ? "—" : $"{h:0}%";
        // Tag carries the lap time in m:ss.mmm plus tyre health, shown verbatim in the tooltip.
        string tag = $"{RaceUtils.FormatLapTime(r.TimeSecs)}   ·   tyre {health}";
        return new ScatterPoint(r.Seq, r.TimeSecs, double.NaN, double.NaN, tag);
    }

    private static string FmtAxisTime(double secs)
    {
        if (secs <= 0) return "";
        int m = (int)(secs / 60);
        int s = (int)(secs % 60);
        return $"{m}:{s:D2}";
    }

    // ── Event handlers ─────────────────────────────────────────────────────────

    private void SessionCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready && SessionCombo.SelectedItem is string fn) LoadFile(fn);
    }

    private void HealthBasis_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _useLowest = HealthBasisCombo.SelectedIndex == 1;
        ApplyHealthBasis();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshFiles();

    private void FilterCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (FilterCombo.SelectedItem is ComboBoxItem item)
            _sessionFilter = item.Content?.ToString() ?? "All";
        RefreshFiles();
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(_lapsDir)) return;
        var result = MessageBox.Show(
            "Delete all lap CSV files?", "Clear All",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        foreach (var f in Directory.GetFiles(_lapsDir, "*.csv"))
            try { File.Delete(f); } catch { }
        RefreshFiles();
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFile == null || !File.Exists(_currentFile)) return;
        var dlg = new SaveFileDialog
        {
            Filter   = "CSV files|*.csv",
            FileName = Path.GetFileName(_currentFile),
        };
        if (dlg.ShowDialog() == true)
            File.Copy(_currentFile, dlg.FileName, overwrite: true);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static double ParseD(string s)
        => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : 0;

    private static string ModeKey(string mode) => mode.ToLowerInvariant() switch
    {
        "push" => "push",
        "save" => "save",
        _      => "normal",
    };

    private static string Cap(string mode) => ModeKey(mode) switch
    {
        "push" => "Push",
        "save" => "Save",
        _      => "Normal",
    };

    // ── Row model ──────────────────────────────────────────────────────────────

    private class LapRow
    {
        public int    Seq      { get; set; }     // 1-based driven order (chart X, ordering)
        public int    LapNum   { get; set; }
        public string Lap      { get; set; } = "";
        public string Flag     { get; set; } = "";
        public string Mode     { get; set; } = "";
        public string ModeKey  { get; set; } = "normal";
        public double TimeSecs { get; set; }
        public string Time     { get; set; } = "";
        public string S1       { get; set; } = "";
        public string S2       { get; set; } = "";
        public string S3       { get; set; } = "";
        public string Used     { get; set; } = "";       // health used this lap (avg of 4)
        public double AvgStart { get; set; } = double.NaN;
        public double MinStart { get; set; } = double.NaN;
        public string HealthText { get; set; } = "";     // health at lap start, per basis toggle
        public string Energy   { get; set; } = "";       // virtual energy at lap start (%)
        public string NrgUsed  { get; set; } = "";       // virtual energy consumed this lap (%)
    }
}
