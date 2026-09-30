using LmuRaceEngineerCS.Services;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace LmuRaceEngineerCS.Views;

/// <summary>
/// Read-only table of every learned per-track driver profile. Each row shows the
/// learned driving style, stints done, degradation onset and the driver-vs-engine
/// confidence split; expanding a row reveals tailored driving / strategy tips.
/// Data comes from <see cref="App.Learner"/> — the same profile the StrategyEngine uses.
/// </summary>
public partial class DriverProfilePage : UserControl
{
    public DriverProfilePage()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadRows();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadRows();

    private void LoadRows()
    {
        var rows = new List<DriverProfileRow>();
        int totalStints = 0;

        foreach (var (track, p) in App.Learner.Profile.All())
        {
            rows.Add(DriverProfileRow.From(track, p.DegOnset, p.SampleStints, p.LastUpdated));
            totalStints += p.SampleStints;
        }

        // Most-driven tracks first, then alphabetically.
        rows.Sort((a, b) => b.Stints != a.Stints
            ? b.Stints.CompareTo(a.Stints)
            : string.Compare(a.Track, b.Track, StringComparison.OrdinalIgnoreCase));

        ProfileTable.ItemsSource = rows;
        TbTrackCount.Text = rows.Count.ToString();
        TbStintCount.Text = totalStints.ToString();

        bool empty = rows.Count == 0;
        TbEmpty.Visibility       = empty ? Visibility.Visible : Visibility.Collapsed;
        ProfileTable.Visibility  = empty ? Visibility.Collapsed : Visibility.Visible;
    }
}

/// <summary>One table row — a single track's learned profile, pre-formatted for binding.</summary>
public sealed class DriverProfileRow : INotifyPropertyChanged
{
    private bool _isExpanded;
    /// <summary>True when the row's tips panel is open (toggled by clicking the row).</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; PropertyChanged?.Invoke(this, new(nameof(IsExpanded))); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Track            { get; init; } = "";
    public string Style            { get; init; } = "";
    public Brush  StyleBrush       { get; init; } = Brushes.Gray;
    public int    Stints           { get; init; }
    public string OnsetLabel       { get; init; } = "";
    public string CliffLabel       { get; init; } = "";
    public string ConfidenceLabel  { get; init; } = "";
    public string ConfidenceDetail { get; init; } = "";
    public string Updated          { get; init; } = "";
    public double DriverConfFraction { get; init; }
    public double EngineConfFraction { get; init; }
    public IReadOnlyList<TipRow> Tips { get; init; } = Array.Empty<TipRow>();

    public static DriverProfileRow From(string trackKey, double degOnset, int stints,
                                        DateTimeOffset updated)
    {
        double driver = ProfileInsights.DriverConfidence(stints);
        double engine = ProfileInsights.EngineConfidence(stints);
        int    cliffPct = (int)Math.Round(ProfileInsights.CliffThreshold(degOnset) * 100);

        return new DriverProfileRow
        {
            Track              = trackKey.Replace('_', ' ').Trim(),
            Style              = ProfileInsights.StyleLabel(degOnset),
            StyleBrush         = HexBrush(ProfileInsights.StyleColorHex(degOnset)),
            Stints             = stints,
            OnsetLabel         = degOnset.ToString("0.00", CultureInfo.InvariantCulture),
            CliffLabel         = $"{cliffPct}%",
            ConfidenceLabel    = $"{driver * 100:0}%  ·  {engine * 100:0}%",
            ConfidenceDetail   = $"{driver * 100:0}% from your data, {engine * 100:0}% from the generic " +
                                 $"0.60 model  ·  {stints} stint{(stints == 1 ? "" : "s")} analysed",
            Updated            = updated == DateTimeOffset.MinValue ? "—" : updated.LocalDateTime.ToString("yyyy-MM-dd"),
            DriverConfFraction = driver,
            EngineConfFraction = engine,
            Tips               = ProfileInsights.BuildTips(degOnset, stints)
                                     .Select(TipRow.From).ToList(),
        };
    }

    private static Brush HexBrush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}

/// <summary>One tip card inside a row's expansion.</summary>
public sealed class TipRow
{
    public string Icon      { get; init; } = "";
    public string Title     { get; init; } = "";
    public string Body      { get; init; } = "";
    public Brush  IconBrush { get; init; } = Brushes.Gray;

    public static TipRow From(ProfileInsights.Tip tip)
    {
        (string icon, string brushKey) = tip.Kind switch
        {
            ProfileInsights.TipKind.Driving    => ("🏎", "BlueBrush"),
            ProfileInsights.TipKind.Strategy   => ("🏁", "PurpleBrush"),
            ProfileInsights.TipKind.Confidence => ("📊", "YellowBrush"),
            _                                  => ("•",  "FgBrush"),
        };

        return new TipRow
        {
            Icon      = icon,
            Title     = tip.Title,
            Body      = tip.Body,
            IconBrush = (Brush)Application.Current.Resources[brushKey],
        };
    }
}

/// <summary>Converts a 0–1 fraction into a star <see cref="GridLength"/> for the split bar.</summary>
public sealed class FractionToStarConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double f = value is double d ? d : 0.0;
        return new GridLength(Math.Max(0.0, f), GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
