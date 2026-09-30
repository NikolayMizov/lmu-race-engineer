using LmuRaceEngineerCS.Models;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LmuRaceEngineerCS.Views;

public partial class TelemetryPage : System.Windows.Controls.UserControl
{
    private static readonly string[] CompoundShort = ["S", "M", "H", "W"];

    private readonly DispatcherTimer _timer;
    private readonly Ellipse[] _dots;
    private readonly System.Windows.Controls.TextBlock[] _wears, _temps, _cmpds;
    private readonly System.Windows.Controls.ProgressBar[] _bars;

    public TelemetryPage()
    {
        InitializeComponent();

        _dots  = [DotFL, DotFR, DotRL, DotRR];
        _wears = [WearFL, WearFR, WearRL, WearRR];
        _temps = [TempFL, TempFR, TempRL, TempRR];
        _cmpds = [CmpdFL, CmpdFR, CmpdRL, CmpdRR];
        _bars  = [BarFL, BarFR, BarRL, BarRR];

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        Unloaded += (_, _) => _timer.Stop();
    }

    private void Refresh()
    {
        var snap = App.Telem.Snapshot();
        if (snap == null) return;
        Update(snap);
    }

    private void Update(StrategySnapshot s)
    {
        var settings = App.Settings;

        // ── Strip cards ───────────────────────────────────────────────────────
        TbPos.Text     = $"P{s.ClassPos}";
        TbPosNote.Text = s.ClassTotal > 0 ? $"of {s.ClassTotal} in class" : "";
        TbLap.Text     = s.CurrentLap.ToString();
        TbLapTotal.Text = s.LapsRemaining > 0 ? $"{s.LapsRemaining:F0} laps left"
                        : s.TotalLapsEstimate > 0 ? $"~{s.TotalLapsEstimate:F0} total" : "";
        TbLastLap.Text = FmtTime(s.LastLapTime);
        TbBestLap.Text = FmtTime(s.BestLapTime);
        TbGapAhead.Text  = s.GapFront  > 0 ? $"{s.GapFront:F2}s"  : "—";
        TbGapBehind.Text = s.GapBehind > 0 ? $"{s.GapBehind:F2}s" : "—";

        // Gap ahead — distinguish no car vs collecting vs live delta (#28)
        if (s.GapFront <= 0)
        {
            TbGapAheadDelta.Text       = "No car ahead";
            TbGapAheadDelta.Foreground = Dim;
            TbGapAheadAvg5.Text        = "—";
            TbGapAheadAvg5.Foreground  = Dim;
        }
        else
        {
            double gainFwd  = -s.AvgLapDeltaFront;
            string fwdSign  = gainFwd >= 0 ? "+" : "";
            var    fwdColor = gainFwd > 0 ? Green : gainFwd < 0 ? Red : Dim;
            TbGapAheadDelta.Text       = gainFwd != 0 ? $"{fwdSign}{gainFwd:F2}s/lap" : "collecting…";
            TbGapAheadDelta.Foreground = fwdColor;
            TbGapAheadAvg5.Text        = gainFwd != 0 ? $"{fwdSign}{gainFwd * 5:F2}s / 5 laps" : "—";
            TbGapAheadAvg5.Foreground  = fwdColor;
        }

        // Gap behind — distinguish no car vs collecting vs live delta (#28)
        if (s.GapBehind <= 0)
        {
            TbGapBehindDelta.Text       = "No car behind";
            TbGapBehindDelta.Foreground = Dim;
            TbGapBehindAvg5.Text        = "—";
            TbGapBehindAvg5.Foreground  = Dim;
        }
        else
        {
            double gainBhd  = s.AvgLapDeltaBehind;
            string bhdSign  = gainBhd >= 0 ? "+" : "";
            var    bhdColor = gainBhd > 0 ? Green : gainBhd < 0 ? Red : Dim;
            TbGapBehindDelta.Text       = gainBhd != 0 ? $"{bhdSign}{gainBhd:F2}s/lap" : "collecting…";
            TbGapBehindDelta.Foreground = bhdColor;
            TbGapBehindAvg5.Text        = gainBhd != 0 ? $"{bhdSign}{gainBhd * 5:F2}s / 5 laps" : "—";
            TbGapBehindAvg5.Foreground  = bhdColor;
        }

        // Delta to best (live) and delta to last (last lap vs rolling average) (#3)
        double deltaBest = s.DeltaBest;
        string dBestSign = deltaBest > 0 ? "+" : "";
        TbDBest.Text       = $"{dBestSign}{deltaBest:F3} s";
        TbDBest.Foreground = deltaBest <= 0 ? Green : Red;

        double deltaLast = s.DeltaLast;
        string dLastSign = deltaLast > 0 ? "+" : "";
        TbDLast.Text       = deltaLast != 0 ? $"{dLastSign}{deltaLast:F3} s" : "—";
        TbDLast.Foreground = deltaLast < 0 ? Green : deltaLast > 0 ? Red : Dim;

        // ── Stint row ─────────────────────────────────────────────────────────
        TbStintLap.Text  = s.StintLaps.ToString();
        TbStintTime.Text = s.StintTime > 0 ? FormatStintTime(s.StintTime) : "—";

        if (s.PitLap.HasValue)
        {
            string lim = s.PitLimiter ?? "";
            TbPitWnd.Text = $"Lap {s.PitLap:F0} ({lim})";
        }
        else TbPitWnd.Text = "—";

        TbFClass.Text = s.FasterClassGap.HasValue
            ? $"{s.FasterClassGap:F1}s behind"
            : "—";

        // ── Fuel card ─────────────────────────────────────────────────────────
        double lve    = s.LapsOnVe;
        string fuelTxt = lve > 0 ? $"{lve:F1}" : "—";
        TbFuelLaps.Text = fuelTxt;

        // Progress bar width
        if (FuelBar.Parent is System.Windows.FrameworkElement barParent && barParent.ActualWidth > 0)
        {
            double fracFull = s.Ve / 100.0;
            FuelBar.Width   = Math.Max(0, barParent.ActualWidth * fracFull);
            FuelBar.Fill    = fracFull > 0.3 ? Green : fracFull > 0.15 ? Yellow : Red;
        }

        TbVeRem.Text      = s.Ve > 0    ? $"{s.Ve:F1}%" : "—";
        TbPerLap.Text     = s.AvgFuel > 0 ? $"{s.AvgFuel * 100:F2}%" : "—";
        TbExtraStops.Text = s.ExtraStops > 0 ? $"+{s.ExtraStops}" : "0";

        TbAvg3.Text    = s.AvgFuel3 > 0  ? $"{s.AvgFuel3  * 100:F2}%/lap" : "—";
        TbAvg10.Text   = s.AvgFuel10 > 0 ? $"{s.AvgFuel10 * 100:F2}%/lap" : "—";
        TbSaveTgt.Text = s.FuelSaveTarget > 0.001
            ? $"save {s.FuelSaveTarget * 100:F2}%/lap" : "OK";

        // Strategy message
        string strat = "";
        if (s.ExtraStops > 0)
            strat = $"Add {(int)Math.Ceiling(s.FuelDeficit * 100)}% VE at next stop";
        else if (s.FuelSaveTarget > 0.005)
            strat = $"Lift & coast to save {s.FuelSaveTarget * 100:F2}%/lap";
        else
            strat = "✓ Energy nominal";
        TbFuelStrat.Text = strat;
        TbFuelStrat.Foreground = s.ExtraStops > 0 ? Red :
                                  s.FuelSaveTarget > 0.005 ? Yellow : Green;

        double soc   = s.StateOfCharge;
        double regen = s.Regen;
        TbSoC.Text   = soc > 0 ? $"SoC  {soc:F1}%" : "";
        TbSoC.Foreground = soc > 30 ? Green : soc > 15 ? Yellow : Red;
        TbRegen.Text = regen != 0 ? $"Regen  {regen:F1} kW" : "";

        // ── Tire card ─────────────────────────────────────────────────────────
        for (int i = 0; i < 4; i++)
        {
            double w  = s.TireWear.Length > i ? s.TireWear[i]  : 0;
            double tc = s.TireTempsC.Length > i ? s.TireTempsC[i] : 0;
            int    ct = s.CompoundTypes.Length > i ? s.CompoundTypes[i] : -1;

            var wColor = WearColor(w, settings);
            _dots[i].Fill       = wColor;
            _wears[i].Text      = $"{w * 100:F0}%";
            _wears[i].Foreground = wColor;
            _temps[i].Text      = $"{tc:F0}°C";
            _temps[i].Foreground = TempColor(tc, settings);
            _cmpds[i].Text      = ct >= 0 && ct < CompoundShort.Length ? CompoundShort[ct] : "";
            _bars[i].Value      = w * 100;
            _bars[i].Foreground = wColor;
        }

        double minHealth = s.TireWear.Length == 4 ? s.TireWear.Min() * 100 : 100;
        if (minHealth > settings.TireWarnWear)
        { TbTireStrat.Text = "✓ Tires nominal";                          TbTireStrat.Foreground = Green; }
        else if (minHealth > settings.TireCritWear)
        { TbTireStrat.Text = $"⚠ Worn — {minHealth:F0}% health";         TbTireStrat.Foreground = Yellow; }
        else
        { TbTireStrat.Text = $"▼ Critical — {minHealth:F0}% health";     TbTireStrat.Foreground = Red; }

        // ── Strategy section ─────────────────────────────────────────────────
        // Session
        SvSessTime.Text = s.SessionTimeRemaining > 0
            ? $"{(int)(s.SessionTimeRemaining / 60)}:{(int)(s.SessionTimeRemaining % 60):D2}" : "—";
        SvTotLaps.Text  = s.TotalLapsEstimate > 0 ? $"~{s.TotalLapsEstimate:F0}" : "—";

        // Pit window
        SvPitRec.Text     = s.PitLap.HasValue   ? $"Lap {s.PitLap:F0}" : "—";
        SvPitEarly.Text   = s.EarliestPit > 0   ? $"Lap {s.EarliestPit:F0}" : "—";
        SvPitLate.Text    = s.LatestPit.HasValue ? $"Lap {s.LatestPit:F0}" : "—";
        SvPitLimiter.Text = s.PitLimiter ?? "—";

        // Stint analysis
        SvStintLap.Text  = s.StintLaps.ToString();
        SvStintTime.Text = s.StintTime > 0
            ? $"{(int)(s.StintTime / 60)}:{(int)(s.StintTime % 60):D2}" : "—";
        SvStintPred.Text = s.PredictedStint.HasValue ? $"{s.PredictedStint:F1} laps" : "—";
        SvPitStops.Text  = s.PitStops.ToString();

        // Drive mode averages
        static string FmtFuel(double f) => f > 0 ? $"{f * 100:F2}%/lap" : "—";
        DmNormal1.Text = FmtFuel(s.AvgFuel);
        DmNormal2.Text = FmtTime(s.AvgLapTime);
        DmPush1.Text   = FmtFuel(s.PushAvgFuel);
        DmPush2.Text   = FmtTime(s.PushAvgTime);
        DmSave1.Text   = FmtFuel(s.SaveAvgFuel);
        DmSave2.Text   = FmtTime(s.SaveAvgTime);

        // Fuel strategy
        SvFuelSave.Text = s.FuelSaveTarget > 0.001
            ? $"save {s.FuelSaveTarget * 100:F2}%/lap" : "OK";
        SvFuelMin.Text  = s.AvgFuel > 0 ? $"{s.MinFinishFuel * 100:F2}%" : "—";
        SvFuelAdd.Text  = s.FuelDeficit > 0
            ? $"+{(int)Math.Ceiling(s.FuelDeficit * 100)}% VE" : "OK";
        SvFuelAvg.Text  = s.AvgFuel > 0 ? $"{s.AvgFuel * 100:F2}%/lap" : "—";
    }

    // ── Pop-out overlays ──────────────────────────────────────────────────────
    private readonly Dictionary<string, OverlayWindow> _overlays = new();

    private void PopOut_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string key) return;

        if (_overlays.TryGetValue(key, out var existing))
        {
            existing.Activate();
            return;
        }

        var (content, updater) = WidgetFactory.Build(key);
        string title = key switch
        {
            "position"    => "Position",
            "lap"         => "Lap",
            "gapfwd"      => "Gap Ahead",
            "gapbhd"      => "Gap Behind",
            "pitbox"      => "Pit Window",
            "fasterclass" => "Faster Class",
            "fuel"        => "Energy Strategy",
            "tire"        => "Tire Health",
            "session"     => "Session",
            "pitwindow"   => "Pit Window Detail",
            "stint"       => "Stint Analysis",
            "driveavg"    => "Drive Mode Averages",
            "fuelstrat"   => "Fuel Strategy",
            _             => key
        };

        var overlay = new OverlayWindow(title, content, updater);
        overlay.Closed += (_, _) => _overlays.Remove(key);
        _overlays[key] = overlay;
        overlay.Show();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static string FmtTime(double secs)
    {
        if (secs <= 0) return "--:--.---";
        int m = (int)(secs / 60);
        double s = secs % 60;
        return $"{m}:{s:06.3f}";
    }

    private static string FormatStintTime(double secs)
    {
        int m = (int)(secs / 60);
        int s = (int)(secs % 60);
        return $"{m}:{s:D2}";
    }

    private static Brush WearColor(double w, AppSettings s)
    {
        double remaining = w * 100;
        if (remaining > s.TireWarnWear) return Green;
        if (remaining > s.TireCritWear) return Yellow;
        return Red;
    }

    private static Brush TempColor(double tc, AppSettings s)
    {
        if (tc < s.TireColdTemp)  return Blue;
        if (tc > s.TireHotTemp)   return Red;
        if (tc >= s.TireOptLow && tc <= s.TireOptHigh) return Green;
        return Yellow;
    }

    // ── Brush shorthands ──────────────────────────────────────────────────────
    private static Brush Green  => (Brush)App.Current.Resources["GreenBrush"];
    private static Brush Yellow => (Brush)App.Current.Resources["YellowBrush"];
    private static Brush Red    => (Brush)App.Current.Resources["RedBrush"];
    private static Brush Blue   => (Brush)App.Current.Resources["BlueBrush"];
    private static Brush Dim    => (Brush)App.Current.Resources["DimBrush"];

}
