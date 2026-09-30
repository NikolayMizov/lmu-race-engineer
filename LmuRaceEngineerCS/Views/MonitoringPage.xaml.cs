using LmuRaceEngineerCS.Models;
using LmuRaceEngineerCS.Shared;
using LmuRaceEngineerCS.Strategy;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace LmuRaceEngineerCS.Views;

public partial class MonitoringPage : UserControl
{
    private static readonly string[] CompoundShort = ["S", "M", "H", "W"];
    private static readonly string[] TireCorner   = ["FL", "FR", "RL", "RR"];

    private readonly DispatcherTimer _timer;
    private StrategyRecommendation?  _lastRec;

    public MonitoringPage()
    {
        InitializeComponent();

        App.Telem.RecommendationUpdated += OnRecommendationUpdated;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        Loaded   += (_, _) =>
        {
            var existing = App.Telem.LatestRecommendation();
            if (existing != null) UpdateRecommendation(existing);
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            App.Telem.RecommendationUpdated -= OnRecommendationUpdated;
        };
    }

    private void OnRecommendationUpdated(object? sender, StrategyRecommendation rec)
        => Dispatcher.InvokeAsync(() => UpdateRecommendation(rec));

    private void Refresh()
    {
        var snap = App.Telem.Snapshot();
        if (snap != null) Update(snap);
    }

    private void Update(StrategySnapshot s)
    {
        var settings = App.Settings;

        // ── Session card ──────────────────────────────────────────────────────
        MonTrack.Text   = s.TrackName.Length > 0 ? s.TrackName : "—";
        MonSession.Text = SessionName(s.Session);

        if (s.SessionTimeRemaining > 0)
        {
            int h   = (int)(s.SessionTimeRemaining / 3600);
            int m   = (int)((s.SessionTimeRemaining % 3600) / 60);
            int sec = (int)(s.SessionTimeRemaining % 60);
            MonTimeRem.Text = h > 0 ? $"{h}:{m:D2}:{sec:D2}" : $"{m}:{sec:D2}";
        }
        else MonTimeRem.Text = "—";

        MonTotalLaps.Text = s.TotalLapsEstimate > 0 ? $"~{s.TotalLapsEstimate:F0}" : "—";

        // ── Pit window card ───────────────────────────────────────────────────
        if (s.PitLap.HasValue)
        {
            double pitLap   = s.PitLap.Value;
            double lapsLeft = pitLap - s.CurrentLap;
            double avgLap   = s.AvgLapTime;
            double secs     = lapsLeft * avgLap;

            MonTimeToPit.Text = secs > 0 && avgLap > 0
                ? FormatCountdown(secs)
                : (lapsLeft >= 0 ? $"{lapsLeft:F0} laps" : "NOW");
            MonTimeToPit.Foreground = secs < 60 ? Red : secs < 180 ? Yellow : Green;
        }
        else
        {
            MonTimeToPit.Text       = "—";
            MonTimeToPit.Foreground = Dim;
        }

        MonPitRec.Text    = s.PitLap.HasValue   ? $"Lap {s.PitLap:F0}"      : "—";
        MonPitEarly.Text  = s.EarliestPit > 0   ? $"Lap {s.EarliestPit:F0}" : "—";
        MonPitLatest.Text = s.LatestPit.HasValue ? $"Lap {s.LatestPit:F0}"  : "—";
        MonPitLimiter.Text = s.PitLimiter ?? "—";

        // ── Position cards ────────────────────────────────────────────────────
        MonPos.Text   = $"P{s.ClassPos}";
        MonPosOf.Text = $"of {s.ClassTotal} in class";

        if (s.GapFront <= 0)
        {
            MonGapFwd.Text       = "No car ahead";
            MonGapFwd.Foreground = Dim;
            MonGainFwd.Text      = "";
        }
        else
        {
            MonGapFwd.Text       = $"{s.GapFront:F2}s";
            MonGapFwd.Foreground = Fg;
            double gain = -s.AvgLapDeltaFront;
            if (gain != 0)
            {
                string sign = gain > 0 ? "+" : "";
                MonGainFwd.Text       = $"{sign}{gain:F2}s/lap";
                MonGainFwd.Foreground = gain > 0 ? Green : Red;
            }
            else
            {
                MonGainFwd.Text       = "collecting…";
                MonGainFwd.Foreground = Dim;
            }
        }

        if (s.GapBehind <= 0)
        {
            MonGapBhd.Text       = "No car behind";
            MonGapBhd.Foreground = Dim;
            MonGainBhd.Text      = "";
        }
        else
        {
            MonGapBhd.Text       = $"{s.GapBehind:F2}s";
            MonGapBhd.Foreground = Fg;
            double gain = s.AvgLapDeltaBehind;
            if (gain != 0)
            {
                string sign = gain > 0 ? "+" : "";
                MonGainBhd.Text       = $"{sign}{gain:F2}s/lap";
                MonGainBhd.Foreground = gain > 0 ? Green : Red;
            }
            else
            {
                MonGainBhd.Text       = "collecting…";
                MonGainBhd.Foreground = Dim;
            }
        }

        // ── Tire card ─────────────────────────────────────────────────────────
        var tireBlocks = new[] { MonTireFL, MonTireFR, MonTireRL, MonTireRR };
        var tempBlocks = new[] { MonTempFL, MonTempFR, MonTempRL, MonTempRR };
        var bars       = new ProgressBar[] { MonBarFL, MonBarFR, MonBarRL, MonBarRR };
        for (int i = 0; i < 4; i++)
        {
            double w   = s.TireWear.Length > i  ? s.TireWear[i]  : 0;
            double tc  = s.TireTempsC.Length > i ? s.TireTempsC[i] : 0;
            int    ct  = s.CompoundTypes.Length > i ? s.CompoundTypes[i] : -1;
            string cmp = ct >= 0 && ct < CompoundShort.Length ? CompoundShort[ct] : "";

            double health = w * 100;   // mWear is tread remaining (1.0 = new) = health
            var    col    = WearColor(health, settings);
            tireBlocks[i].Text       = $"{health:F0}%{(cmp.Length > 0 ? " " + cmp : "")}";
            tireBlocks[i].Foreground = col;
            tempBlocks[i].Text       = tc > 0 ? $"{tc:F0}°" : "—";
            tempBlocks[i].Foreground = TempColor(tc, settings);
            bars[i].Value            = health;
            bars[i].Foreground       = col;
        }

        MonStintLap.Text = s.StintLaps.ToString();

        if (s.PredictedStint.HasValue)
        {
            double remaining = s.PredictedStint.Value - s.StintLaps;
            MonPredStint.Text       = remaining > 0
                ? $"{remaining:F0} laps left"
                : "at limit";
            MonPredStint.Foreground = remaining > 5 ? Fg : remaining > 0 ? Yellow : Red;
        }
        else MonPredStint.Text = "—";

        double worstHealth = s.TireWear.Length == 4 ? s.TireWear.Min() * 100 : 100;
        if (worstHealth > settings.TireWarnWear)
        { MonTireStatus.Text = "✓ Tires nominal";                                MonTireStatus.Foreground = Green; }
        else if (worstHealth > settings.TireCritWear)
        { MonTireStatus.Text = $"⚠ Worn — worst tire {worstHealth:F0}% health";  MonTireStatus.Foreground = Yellow; }
        else
        { MonTireStatus.Text = $"▼ Critical — worst tire {worstHealth:F0}% health"; MonTireStatus.Foreground = Red; }

        // ── Fuel card ─────────────────────────────────────────────────────────
        MonVe.Text       = s.Ve > 0 ? $"{s.Ve:F1}%" : "—";
        MonVe.Foreground = s.Ve > 30 ? Green : s.Ve > 15 ? Yellow : Red;

        MonLapsVe.Text     = s.LapsOnVe > 0 ? $"{s.LapsOnVe:F1}" : "—";
        MonPerLap.Text     = s.AvgFuel > 0   ? $"{s.AvgFuel * 100:F2}%/lap" : "—";
        MonExtraStops.Text = s.ExtraStops > 0 ? $"+{s.ExtraStops}" : "0";

        if (s.ExtraStops > 0)
        { MonFuelStatus.Text = $"Add {(int)Math.Ceiling(s.FuelDeficit * 100)}% VE at next stop"; MonFuelStatus.Foreground = Red; }
        else if (s.FuelSaveTarget > 0.005)
        { MonFuelStatus.Text = $"Save {s.FuelSaveTarget * 100:F2}%/lap";  MonFuelStatus.Foreground = Yellow; }
        else
        { MonFuelStatus.Text = "✓ Energy nominal";                          MonFuelStatus.Foreground = Green; }

        // ── Lap times card ────────────────────────────────────────────────────
        MonLastLap.Text  = FmtTime(s.LastLapTime);
        MonBestLap.Text  = FmtTime(s.BestLapTime);
        MonAvgLap.Text   = FmtTime(s.AvgLapTime);

        double db  = s.DeltaBest;
        MonDeltaBest.Text       = db != 0 ? $"{(db > 0 ? "+" : "")}{db:F3} s" : "—";
        MonDeltaBest.Foreground = db <= 0 ? Green : Red;
    }

    private void UpdateRecommendation(StrategyRecommendation rec)
    {
        _lastRec = rec;

        bool stale = (DateTimeOffset.Now - rec.ComputedAt).TotalSeconds > 60;
        var  col   = ActionColor(rec.Action);

        MonStratAction.Text       = rec.ActionLabel.Length > 0 ? rec.ActionLabel : "—";
        MonStratAction.Foreground = stale ? Dim : col;
        MonStratAction.Opacity    = stale ? 0.4 : 1.0;

        MonStratRationale.Text = rec.Rationale.Length > 0 ? rec.Rationale : "—";

        MonFuelMargin.Text = rec.FuelSafetyMargin > 0
            ? $"{rec.FuelSafetyMargin:F1} laps"
            : "—";
        MonFuelMargin.Foreground = rec.FuelSafetyMargin > 3 ? Green
            : rec.FuelSafetyMargin > 1 ? Yellow : Red;

        double cr = rec.TireCliffRisk;
        MonCliffRisk.Text = cr > 0 ? $"{cr * 100:F0}%" : "—";
        MonCliffRisk.Foreground = cr < 0.3 ? Green : cr < 0.6 ? Yellow : Red;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FmtTime(double secs)
    {
        if (secs <= 0) return "--:--.---";
        int m = (int)(secs / 60);
        double s = secs % 60;
        return $"{m}:{s:06.3f}";
    }

    private static string FormatCountdown(double secs)
    {
        int m = (int)(secs / 60);
        int s = (int)(secs % 60);
        return m > 0 ? $"{m}:{s:D2}" : $"{s}s";
    }

    private static string SessionName(int session) => session switch
    {
        1 => "Practice",
        2 => "Qualifying",
        3 or 4 or 5 or 6 => "Race",
        _ => "—"
    };

    private static Brush WearColor(double lifeLeft, AppSettings s)
    {
        if (lifeLeft > s.TireWarnWear) return Green;
        if (lifeLeft > s.TireCritWear) return Yellow;
        return Red;
    }

    private static Brush TempColor(double t, AppSettings s)
    {
        if (t <= 0) return Dim;
        if (t < s.TireColdTemp) return Blue;
        if (t <= s.TireOptHigh) return Green;
        if (t < s.TireHotTemp) return Yellow;
        return Red;
    }

    private Brush ActionColor(StrategyActionType action) => action switch
    {
        StrategyActionType.PitNowFullFuel  => Accent,
        StrategyActionType.PitNowShortFuel => Accent,
        StrategyActionType.PushUndercut    => Brush("PushBrush"),
        StrategyActionType.SaveFuel        => Brush("SaveBrush"),
        StrategyActionType.StayOut         => Green,
        StrategyActionType.CoverRival      => Blue,
        _                                  => Dim
    };

    private static Brush Green  => Brush("GreenBrush");
    private static Brush Yellow => Brush("YellowBrush");
    private static Brush Red    => Brush("RedBrush");
    private static Brush Blue   => Brush("BlueBrush");
    private static Brush Dim    => Brush("DimBrush");
    private static Brush Fg     => Brush("FgBrush");
    private static Brush Accent => Brush("AccentBrush");

    private static Brush Brush(string key)
        => (Brush)App.Current.Resources[key];
}
