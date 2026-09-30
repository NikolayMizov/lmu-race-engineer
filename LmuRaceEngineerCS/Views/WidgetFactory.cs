using LmuRaceEngineerCS.Models;
using LmuRaceEngineerCS.Strategy;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace LmuRaceEngineerCS.Views;

/// <summary>
/// Builds lightweight overlay widget content (FrameworkElement + updater Action)
/// for each widget key without requiring separate XAML files.
/// </summary>
internal static class WidgetFactory
{
    private static readonly string[] CompoundShort = ["S", "M", "H", "W"];

    // ── Brush / resource helpers ──────────────────────────────────────────
    static Brush Fg     => B("FgBrush");
    static Brush Dim    => B("DimBrush");
    static Brush Dim2   => B("Dim2Brush");
    static Brush Green  => B("GreenBrush");
    static Brush Yellow => B("YellowBrush");
    static Brush Red    => B("RedBrush");
    static Brush Blue   => B("BlueBrush");
    static Brush Accent => B("AccentBrush");
    static Brush PushBr => B("PushBrush");
    static Brush SaveBr => B("SaveBrush");
    static Brush Brd2   => B("BorderBrush2");
    static Brush B(string k) => (Brush)Application.Current.Resources[k];

    // ── Element helpers ───────────────────────────────────────────────────
    static TextBlock Lbl(string t) => new()
    {
        Text = t, FontFamily = new FontFamily("Segoe UI"),
        FontSize = 11, Foreground = Dim, Margin = new Thickness(0, 0, 0, 1)
    };

    static TextBlock Val(double fs = 15) => new()
    {
        Text = "—", FontFamily = new FontFamily("Consolas"),
        FontSize = fs, Foreground = Fg
    };

    static StackPanel LV(string label, TextBlock val, double rMargin = 0) =>
        new StackPanel { Margin = new Thickness(0, 0, rMargin, 0) }
            .Also(sp => { sp.Children.Add(Lbl(label)); sp.Children.Add(val); });

    static UniformGrid Pair(string l1, TextBlock v1, string l2, TextBlock v2) =>
        new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 4) }
            .Also(ug => { ug.Children.Add(LV(l1, v1, 6)); ug.Children.Add(LV(l2, v2)); });

    static StackPanel Quad(string l1, TextBlock v1, string l2, TextBlock v2,
                            string l3, TextBlock v3, string l4, TextBlock v4) =>
        new StackPanel()
            .Also(sp => { sp.Children.Add(Pair(l1, v1, l2, v2)); sp.Children.Add(Pair(l3, v3, l4, v4)); });

    static Rectangle Sep() => new() { Height = 1, Fill = Brd2, Margin = new Thickness(0, 2, 0, 6) };

    // ── Overlay helpers (bars) ────────────────────────────────────────────
    private static readonly string[] Corners = ["FL", "FR", "RL", "RR"];

    /// <summary>Bar row: [label] [track ....] [value]</summary>
    static Grid BarRow(string label, Grid track, TextBlock val)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var l = Lbl(label); l.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(l, 0); g.Children.Add(l);
        track.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(track, 1); g.Children.Add(track);
        val.Margin = new Thickness(8, 0, 0, 0);
        val.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(val, 2); g.Children.Add(val);
        return g;
    }

    // ── Pedal inputs — shared rolling throttle/brake trace (TinyPedals style) ──
    private static (FrameworkElement, Action<StrategySnapshot>) BuildPedals()
    {
        const double SampleMs = 33;                       // ~30 Hz — 2 Hz snapshot is far too coarse
        const double WindowS  = 6.0;                      // visible history
        const int    Samples  = (int)(WindowS * 1000 / SampleMs);
        // Fixed logical drawing surface. The overlay's Viewbox scales this to the window, so
        // the plot must NOT size itself from its children — otherwise the Polyline's measured
        // bounds grow the canvas every frame and the Viewbox shrinks it (looks like zooming out).
        const double PlotW = 260, PlotH = 100;

        var thr = new double[Samples];
        var brk = new double[Samples];

        var thrLine = new Polyline
        {
            Stroke = Green, StrokeThickness = 1.6,
            StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false,
        };
        var brkLine = new Polyline
        {
            Stroke = Red, StrokeThickness = 1.6,
            StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false,
        };
        var midLine = new Line   // 50 % reference
        {
            Stroke = Brd2, StrokeThickness = 1, IsHitTestVisible = false,
        };

        // Canvas (not Grid): a Canvas ignores its children when measuring, and the explicit
        // Width/Height pin the surface — so the trace can never inflate its own container.
        var plot = new Canvas
        {
            Width = PlotW, Height = PlotH,
            Background = B("BgInset"), ClipToBounds = true,
        };
        plot.Children.Add(midLine);
        plot.Children.Add(thrLine);
        plot.Children.Add(brkLine);

        // Compact readouts, colour-matched to the traces
        var vThr = Val(14); vThr.Foreground = Green;
        var vBrk = Val(14); vBrk.Foreground = Red;
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
        legend.Children.Add(new TextBlock
        {
            Text = "THR", FontFamily = new FontFamily("Segoe UI"), FontSize = 11,
            Foreground = Green, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center
        });
        legend.Children.Add(vThr);
        legend.Children.Add(new TextBlock
        {
            Text = "BRK", FontFamily = new FontFamily("Segoe UI"), FontSize = 11,
            Foreground = Red, Margin = new Thickness(14, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center
        });
        legend.Children.Add(vBrk);

        var root = new StackPanel();
        root.Children.Add(plot);
        root.Children.Add(legend);

        PointCollection Trace(double[] buf)
        {
            var pts = new PointCollection(buf.Length);
            double dx = PlotW / Math.Max(1, buf.Length - 1);
            for (int i = 0; i < buf.Length; i++)
                pts.Add(new Point(i * dx, PlotH - buf[i] * PlotH));
            return pts;
        }

        void Redraw()
        {
            midLine.X1 = 0; midLine.X2 = PlotW;
            midLine.Y1 = midLine.Y2 = PlotH / 2;
            thrLine.Points = Trace(thr);
            brkLine.Points = Trace(brk);
        }

        void Sample()
        {
            if (!App.Telem.TryReadPedals(out double t, out double b)) { t = 0; b = 0; }
            // Shift left and append — the trace scrolls right-to-left.
            Array.Copy(thr, 1, thr, 0, Samples - 1); thr[Samples - 1] = t;
            Array.Copy(brk, 1, brk, 0, Samples - 1); brk[Samples - 1] = b;
            vThr.Text = $"{t * 100:0}%";
            vBrk.Text = $"{b * 100:0}%";
            Redraw();
        }

        // The widget drives itself: the 2 Hz overlay refresh cannot draw a pedal trace.
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(SampleMs)
        };
        timer.Tick += (_, _) => Sample();
        root.Loaded   += (_, _) => timer.Start();
        root.Unloaded += (_, _) => timer.Stop();

        // External 2 Hz updater is a no-op — the internal timer owns this widget.
        return (root, _ => { });
    }

    // ── Steering trace ────────────────────────────────────────────────────
    private static (FrameworkElement, Action<StrategySnapshot>) BuildSteering()
    {
        var vSteer = Val(15);
        var leftFill  = new Rectangle { Fill = Accent, Width = 0, Height = 13,
                                        HorizontalAlignment = HorizontalAlignment.Right };
        var rightFill = new Rectangle { Fill = Accent, Width = 0, Height = 13,
                                        HorizontalAlignment = HorizontalAlignment.Left };
        var leftCell  = new Grid { Height = 13, Background = Brd2 };
        var rightCell = new Grid { Height = 13, Background = Brd2, Margin = new Thickness(2, 0, 0, 0) };
        leftCell.Children.Add(leftFill);
        rightCell.Children.Add(rightFill);

        var track = new Grid { MinWidth = 140 };
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(leftCell, 0);  track.Children.Add(leftCell);
        Grid.SetColumn(rightCell, 1); track.Children.Add(rightCell);

        var root = new StackPanel();
        root.Children.Add(BarRow("STEERING", track, vSteer));
        root.Children.Add(Lbl("left ◀ centre ▶ right"));

        void Update(StrategySnapshot s)
        {
            double st = Math.Clamp(s.Steering, -1, 1);
            vSteer.Text = $"{st * 100:+0;-0;0}%";
            leftFill.Width  = st < 0 ? leftCell.ActualWidth  * -st : 0;
            rightFill.Width = st > 0 ? rightCell.ActualWidth *  st : 0;
        }
        return (root, Update);
    }

    // ── Lockup / wheelspin detector ───────────────────────────────────────
    private static (FrameworkElement, Action<StrategySnapshot>) BuildSlip()
    {
        const double SampleMs = 33;    // ~30 Hz — lockups are often shorter than the 2 Hz poll
        const double HoldMs   = 500;   // keep a brief event up long enough to actually read
        const double SlipThr  = 0.08;  // slip ratio either side of rolling

        var vals = new TextBlock[4];
        for (int i = 0; i < 4; i++) vals[i] = Val(15);

        var root = new StackPanel();
        root.Children.Add(Lbl("WHEEL SLIP"));
        root.Children.Add(Sep());
        root.Children.Add(Quad(Corners[0], vals[0], Corners[1], vals[1],
                               Corners[2], vals[2], Corners[3], vals[3]));

        // Reused each frame so sampling allocates nothing.
        var patch  = new double[4];
        var ground = new double[4];
        var lastEvent = new string?[4];
        var lastAt    = new DateTime[4];

        void Sample()
        {
            bool ok = App.Telem.TryReadSlip(patch, ground, out double speedKph);
            var now = DateTime.UtcNow;

            for (int i = 0; i < 4; i++)
            {
                // Below walking pace, or with no wheel motion, the ratio is meaningless.
                if (!ok || speedKph < 5)
                {
                    vals[i].Text = "—"; vals[i].Foreground = Dim2; continue;
                }

                if (Math.Abs(ground[i]) >= 1.0)
                {
                    double slip = (patch[i] - ground[i]) / Math.Abs(ground[i]);
                    if      (slip >  SlipThr) { lastEvent[i] = "SPIN"; lastAt[i] = now; }
                    else if (slip < -SlipThr) { lastEvent[i] = "LOCK"; lastAt[i] = now; }
                }

                bool holding = lastEvent[i] != null
                            && (now - lastAt[i]).TotalMilliseconds <= HoldMs;
                if (holding)
                {
                    vals[i].Text = lastEvent[i]!;
                    vals[i].Foreground = lastEvent[i] == "LOCK" ? Red : Yellow;
                }
                else { vals[i].Text = "ok"; vals[i].Foreground = Green; }
            }
        }

        // Self-driven: the 2 Hz overlay refresh would miss most lockups entirely.
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(SampleMs)
        };
        timer.Tick += (_, _) => Sample();
        root.Loaded   += (_, _) => timer.Start();
        root.Unloaded += (_, _) => timer.Stop();

        return (root, _ => { });
    }

    // ── Tyre pressure + inner/mid/outer temps ─────────────────────────────
    private static (FrameworkElement, Action<StrategySnapshot>) BuildTyrePress()
    {
        var press = new TextBlock[4];
        var temps = new TextBlock[4];
        var grid  = new UniformGrid { Rows = 2, Columns = 2 };

        for (int i = 0; i < 4; i++)
        {
            press[i] = Val(15);
            temps[i] = Val(12);
            var cell = new StackPanel { Margin = new Thickness(0, 0, 10, 8) };
            cell.Children.Add(Lbl(Corners[i]));
            cell.Children.Add(press[i]);
            cell.Children.Add(temps[i]);
            grid.Children.Add(cell);
        }

        var root = new StackPanel();
        root.Children.Add(Lbl("TYRE PRESSURE  ·  I/M/O TEMP °C"));
        root.Children.Add(Sep());
        root.Children.Add(grid);

        void Update(StrategySnapshot s)
        {
            for (int i = 0; i < 4; i++)
            {
                double p = i < s.TirePressures.Length ? s.TirePressures[i] : 0;
                press[i].Text = p > 0 ? $"{p:0.0} kPa" : "—";

                double ti = i < s.TireTempInner.Length  ? s.TireTempInner[i]  : 0;
                double tm = i < s.TireTempMiddle.Length ? s.TireTempMiddle[i] : 0;
                double to = i < s.TireTempOuter.Length  ? s.TireTempOuter[i]  : 0;
                double opt = i < s.TireOptimalC.Length  ? s.TireOptimalC[i]   : 0;
                temps[i].Text = tm > -50 ? $"{ti:0} / {tm:0} / {to:0}" : "—";

                // Colour on the middle temp vs this compound's optimal window (±8 °C).
                temps[i].Foreground = opt <= 0 || tm <= -50 ? Dim2
                                    : tm < opt - 8 ? Blue      // cold
                                    : tm > opt + 8 ? Red       // overheating
                                                   : Green;    // in window
            }
        }
        return (root, Update);
    }

    // ── Track-limits strikes ──────────────────────────────────────────────
    private static (FrameworkElement, Action<StrategySnapshot>) BuildTrackLimits()
    {
        var vCount = new TextBlock
        {
            Text = "—", FontFamily = new FontFamily("Consolas"), FontSize = 40,
            FontWeight = FontWeights.Bold, Foreground = Fg
        };
        var vSub = new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe UI"), FontSize = 11, Foreground = Dim2
        };

        var root = new StackPanel();
        root.Children.Add(Lbl("TRACK LIMITS"));
        root.Children.Add(vCount);
        root.Children.Add(vSub);

        void Update(StrategySnapshot s)
        {
            int per = s.TrackLimitsPerPenalty;
            if (per <= 0) { vCount.Text = "—"; vSub.Text = "no limit data"; vCount.Foreground = Dim2; return; }

            int used = per > 0 ? s.TrackLimitsSteps % per : s.TrackLimitsSteps;
            int left = Math.Max(0, per - used);
            vCount.Text = $"{used}/{per}";
            vSub.Text   = left == 1 ? "1 strike until penalty!" : $"{left} strikes until penalty";
            vCount.Foreground = left <= 1 ? Red : left == 2 ? Yellow : Green;
        }
        return (root, Update);
    }

    // ── Flat / detached wheel alert ───────────────────────────────────────
    private static (FrameworkElement, Action<StrategySnapshot>) BuildDamage()
    {
        var vStatus = new TextBlock
        {
            Text = "—", FontFamily = new FontFamily("Segoe UI"), FontSize = 20,
            FontWeight = FontWeights.Bold, Foreground = Dim2
        };
        var vDetail = new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe UI"), FontSize = 11,
            Foreground = Dim2, TextWrapping = TextWrapping.Wrap
        };

        var root = new StackPanel();
        root.Children.Add(Lbl("TYRE / WHEEL DAMAGE"));
        root.Children.Add(vStatus);
        root.Children.Add(vDetail);

        void Update(StrategySnapshot s)
        {
            var issues = new List<string>();
            for (int i = 0; i < 4; i++)
            {
                bool det  = i < s.WheelDetached.Length && s.WheelDetached[i];
                bool flat = i < s.TireFlat.Length      && s.TireFlat[i];
                if (det)       issues.Add($"{Corners[i]} DETACHED");
                else if (flat) issues.Add($"{Corners[i]} FLAT");
            }

            if (issues.Count == 0)
            {
                vStatus.Text = "OK"; vStatus.Foreground = Green; vDetail.Text = "no flats or detached wheels";
            }
            else
            {
                vStatus.Text = "DAMAGE"; vStatus.Foreground = Red;
                vDetail.Text = string.Join("  ·  ", issues);
            }
        }
        return (root, Update);
    }

    // Fluent helper so we can Add children inline
    static T Also<T>(this T self, Action<T> configure) { configure(self); return self; }

    // ── Dispatcher ────────────────────────────────────────────────────────
    public static (FrameworkElement content, Action<StrategySnapshot> updater) Build(string key) =>
        key switch
        {
            "position"    => BuildPosition(),
            "lap"         => BuildLap(),
            "gapfwd"      => BuildGapFwd(),
            "gapbhd"      => BuildGapBhd(),
            "pitbox"      => BuildPitBox(),
            "fasterclass" => BuildFasterClass(),
            "fuel"        => BuildFuel(),
            "tire"        => BuildTire(),
            "session"     => BuildSession(),
            "pitwindow"   => BuildPitWindow(),
            "stint"       => BuildStint(),
            "driverswap"  => BuildDriverSwap(),
            "stratmode"    => BuildStratMode(),
            "driveavg"     => BuildDriveAvg(),
            "fuelstrat"    => BuildFuelStrat(),
            "strat-rec"    => BuildStratRec(),
            "strat-pitwin" => BuildStratPitWin(),
            "strat-proj"   => BuildStratProj(),
            "strat-rival"  => BuildStratRival(),
            // ── Driving overlays (Settings → Overlays) ────────────────────────
            "pedals"       => BuildPedals(),
            "steering"     => BuildSteering(),
            "slip"         => BuildSlip(),
            "tyrepress"    => BuildTyrePress(),
            "tracklimits"  => BuildTrackLimits(),
            "damage"       => BuildDamage(),
            _              => (new TextBlock { Text = "?" }, _ => { })
        };

    // ── Position ─────────────────────────────────────────────────────────
    static (FrameworkElement, Action<StrategySnapshot>) BuildPosition()
    {
        var vPos = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 42,
                                    FontWeight = FontWeights.Bold, Foreground = Fg };
        var vClass = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 11,
                                      Foreground = Accent, Margin = new Thickness(0, 2, 0, 0) };
        var sp = new StackPanel();
        sp.Children.Add(Lbl("POSITION"));
        sp.Children.Add(vPos);
        sp.Children.Add(vClass);
        return (sp, s =>
        {
            vPos.Text   = $"P{s.ClassPos}";
            vClass.Text = s.ClassTotal > 0 ? $"OF {s.ClassTotal} IN CLASS" : "";
        });
    }

    // ── Lap ───────────────────────────────────────────────────────────────
    static (FrameworkElement, Action<StrategySnapshot>) BuildLap()
    {
        var vLap = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 42,
                                    FontWeight = FontWeights.Bold, Foreground = Fg };
        var vSub = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 11,
                                    Foreground = Dim, Margin = new Thickness(0, 2, 0, 0) };
        var sp = new StackPanel();
        sp.Children.Add(Lbl("LAP"));
        sp.Children.Add(vLap);
        sp.Children.Add(vSub);
        return (sp, s =>
        {
            vLap.Text = s.CurrentLap.ToString();
            vSub.Text = s.LapsRemaining > 0 ? $"{s.LapsRemaining:F0} REMAINING"
                      : s.TotalLapsEstimate > 0 ? $"~{s.TotalLapsEstimate:F0} TOTAL" : "";
        });
    }

    // ── Gap Ahead — per-lap gain/loss + 5-lap trend ───────────────────────
    static (FrameworkElement, Action<StrategySnapshot>) BuildGapFwd()
    {
        var vPerLap = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 28,
                                       FontWeight = FontWeights.Bold, Foreground = Fg };
        var vAvg5   = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 20,
                                       Foreground = Dim, Margin = new Thickness(0, 2, 0, 0) };
        var sp = new StackPanel();
        sp.Children.Add(Lbl("PER LAP"));
        sp.Children.Add(vPerLap);
        sp.Children.Add(Lbl("5-LAP TREND") .Also(l => l.Margin = new Thickness(0, 6, 0, 1)));
        sp.Children.Add(vAvg5);
        return (sp, s =>
        {
            double gain = -s.AvgLapDeltaFront;
            string sign = gain >= 0 ? "+" : "";
            var    col  = gain > 0 ? Green : gain < 0 ? Red : Dim;
            vPerLap.Text       = gain != 0 ? $"{sign}{gain:F2}s/lap" : "collecting…";
            vPerLap.Foreground = col;
            vAvg5.Text         = gain != 0 ? $"{sign}{gain * 5:F2}s / 5 laps" : "—";
            vAvg5.Foreground   = col;
        });
    }

    // ── Gap Behind — per-lap gain/loss + 5-lap trend ──────────────────────
    static (FrameworkElement, Action<StrategySnapshot>) BuildGapBhd()
    {
        var vPerLap = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 28,
                                       FontWeight = FontWeights.Bold, Foreground = Fg };
        var vAvg5   = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 20,
                                       Foreground = Dim, Margin = new Thickness(0, 2, 0, 0) };
        var sp = new StackPanel();
        sp.Children.Add(Lbl("PER LAP"));
        sp.Children.Add(vPerLap);
        sp.Children.Add(Lbl("5-LAP TREND").Also(l => l.Margin = new Thickness(0, 6, 0, 1)));
        sp.Children.Add(vAvg5);
        return (sp, s =>
        {
            double gain = s.AvgLapDeltaBehind;
            string sign = gain >= 0 ? "+" : "";
            var    col  = gain > 0 ? Green : gain < 0 ? Red : Dim;
            vPerLap.Text       = gain != 0 ? $"{sign}{gain:F2}s/lap" : "collecting…";
            vPerLap.Foreground = col;
            vAvg5.Text         = gain != 0 ? $"{sign}{gain * 5:F2}s / 5 laps" : "—";
            vAvg5.Foreground   = col;
        });
    }

    static Brush GapBr(double gain) => gain == 0 ? Dim : gain > 0 ? Green : Red;

    // ── Pit Box (top-row laps-to-box) ─────────────────────────────────────
    static (FrameworkElement, Action<StrategySnapshot>) BuildPitBox()
    {
        var vPit = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 28,
                                    FontWeight = FontWeights.Bold, Foreground = Fg };
        var vSub = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 11,
                                    Foreground = Dim, Margin = new Thickness(0, 2, 0, 0) };
        var sp = new StackPanel();
        sp.Children.Add(Lbl("PIT WINDOW"));
        sp.Children.Add(vPit);
        sp.Children.Add(vSub);
        return (sp, s =>
        {
            if (s.PitLap.HasValue)
            {
                vPit.Text = $"Lap {s.PitLap:F0}";
                vSub.Text = s.PitLimiter is { Length: > 0 } lim ? lim.ToUpper() : "LAPS TO BOX";
            }
            else { vPit.Text = "—"; vSub.Text = "NO PIT DATA"; }
        });
    }

    // ── Faster Class ──────────────────────────────────────────────────────
    static (FrameworkElement, Action<StrategySnapshot>) BuildFasterClass()
    {
        var vGap = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 28,
                                    FontWeight = FontWeights.Bold, Foreground = Dim2 };
        var vSub = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 11,
                                    Foreground = Dim, Margin = new Thickness(0, 2, 0, 0) };
        var sp = new StackPanel();
        sp.Children.Add(Lbl("FASTER CLASS"));
        sp.Children.Add(vGap);
        sp.Children.Add(vSub);
        return (sp, s =>
        {
            if (s.FasterClassGap.HasValue)
            { vGap.Text = $"{s.FasterClassGap:F1}s"; vSub.Text = "BEHIND"; }
            else
            { vGap.Text = "—"; vSub.Text = "NO TRAFFIC"; }
        });
    }

    // ── Session ───────────────────────────────────────────────────────────
    static (FrameworkElement, Action<StrategySnapshot>) BuildSession()
    {
        var vTime = Val(); var vLaps = Val();
        return (Pair("TIME REMAINING", vTime, "TOTAL LAPS EST.", vLaps), s =>
        {
            vTime.Text = s.SessionTimeRemaining > 0
                ? $"{(int)(s.SessionTimeRemaining / 60)}:{(int)(s.SessionTimeRemaining % 60):D2}" : "—";
            vLaps.Text = s.TotalLapsEstimate > 0 ? $"~{s.TotalLapsEstimate:F0}" : "—";
        });
    }

    // ── Pit Window ────────────────────────────────────────────────────────
    static (FrameworkElement, Action<StrategySnapshot>) BuildPitWindow()
    {
        var vRec = Val(); var vEarly = Val(); var vLate = Val(); var vLim = Val();
        return (Quad("RECOMMENDED", vRec, "EARLIEST SAFE", vEarly,
                     "LATEST SAFE",  vLate, "LIMITED BY",   vLim), s =>
        {
            vRec.Text   = s.PitLap.HasValue   ? $"Lap {s.PitLap:F0}"    : "—";
            vEarly.Text = s.EarliestPit > 0   ? $"Lap {s.EarliestPit:F0}" : "—";
            vLate.Text  = s.LatestPit.HasValue ? $"Lap {s.LatestPit:F0}" : "—";
            vLim.Text   = s.PitLimiter ?? "—";
        });
    }

    // ── Stint Analysis ────────────────────────────────────────────────────
    static (FrameworkElement, Action<StrategySnapshot>) BuildStint()
    {
        var vLap = Val(); var vTime = Val(); var vPred = Val(); var vStops = Val();
        return (Quad("STINT LAP",   vLap,  "STINT TIME", vTime,
                     "PREDICTED",   vPred, "STOPS DONE", vStops), s =>
        {
            vLap.Text   = s.StintLaps.ToString();
            vTime.Text  = s.StintTime > 0
                ? $"{(int)(s.StintTime / 60)}:{(int)(s.StintTime % 60):D2}" : "—";
            vPred.Text  = s.PredictedStint.HasValue ? $"{s.PredictedStint:F1} laps" : "—";
            vStops.Text = s.PitStops.ToString();
        });
    }

    // ── Driver Swap / Handover ────────────────────────────────────────────
    static (FrameworkElement, Action<StrategySnapshot>) BuildDriverSwap()
    {
        var vCurr = Val(); var vPrev = Val(); var vTime = Val(); var vStops = Val();
        return (Quad("CURRENT DRIVER",  vCurr, "PREVIOUS DRIVER", vPrev,
                     "STINT TIME",      vTime, "STOPS DONE",      vStops), s =>
        {
            vCurr.Text  = s.CurrentDriverName.Length  > 0 ? s.CurrentDriverName  : "—";
            vPrev.Text  = s.PreviousDriverName.Length > 0 ? s.PreviousDriverName : "—";
            vTime.Text  = s.StintTime > 0
                ? $"{(int)(s.StintTime / 60)}:{(int)(s.StintTime % 60):D2}" : "—";
            vStops.Text = s.PitStops.ToString();
        });
    }

    // ── Fuel Strategy ─────────────────────────────────────────────────────
    static (FrameworkElement, Action<StrategySnapshot>) BuildFuelStrat()
    {
        var vSave = Val(); var vMin = Val(); var vAdd = Val(); var vAvg = Val();
        return (Quad("SAVE TARGET",     vSave, "MIN FINISH FUEL", vMin,
                     "FUEL TO ADD",     vAdd,  "CURRENT AVG",     vAvg), s =>
        {
            vSave.Text = s.FuelSaveTarget > 0.001 ? $"save {s.FuelSaveTarget * 100:F2}%/lap" : "OK";
            vMin.Text  = s.AvgFuel > 0 ? $"{s.MinFinishFuel * 100:F2}%" : "—";
            vAdd.Text  = s.FuelDeficit > 0 ? $"+{(int)Math.Ceiling(s.FuelDeficit * 100)}% VE" : "OK";
            vAvg.Text  = s.AvgFuel > 0 ? $"{s.AvgFuel * 100:F2}%/lap" : "—";
        });
    }

    // ── Strategy Mode ─────────────────────────────────────────────────────
    static (FrameworkElement, Action<StrategySnapshot>) BuildStratMode()
    {
        var vMode = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"), FontSize = 24, FontWeight = FontWeights.Bold,
            Foreground = Fg, Margin = new Thickness(0, 0, 0, 2)
        };
        var vDesc = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI"), FontSize = 11, Foreground = Dim2,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        };
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
        btnRow.Children.Add(ModeBtn("NORMAL", "normal", Brd2,   Fg));
        btnRow.Children.Add(ModeBtn("PUSH",   "push",   PushBr, Brushes.White));
        btnRow.Children.Add(ModeBtn("SAVE",   "save",   SaveBr, Brushes.Black));

        var sp = new StackPanel();
        sp.Children.Add(vMode);
        sp.Children.Add(vDesc);
        sp.Children.Add(btnRow);

        return (sp, s =>
        {
            var dm = s.DriveMode; var mode = s.StrategyMode;
            vMode.Text      = (dm != "normal" ? dm : mode).ToUpper();
            vMode.Foreground = dm == "push" ? PushBr : dm == "save" ? SaveBr : Fg;
            vDesc.Text = mode switch
            {
                "push"   => "Pushing — higher fuel consumption",
                "save"   => "Saving — lift and coast",
                "extend" => "Surplus — room to push",
                _        => "Targeting planned consumption"
            };
        });
    }

    static Button ModeBtn(string label, string tag, Brush bg, Brush fg)
    {
        var btn = new Button
        {
            Content = label, Tag = tag,
            Background = bg, Foreground = fg,
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Segoe UI"), FontSize = 11,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand
        };
        btn.Click += (_, _) =>
        {
            App.Telem.SetDriveMode((string)btn.Tag);
            App.Tts.Speak((string)btn.Tag == "push" ? "push_mode"
                        : (string)btn.Tag == "save" ? "save_mode" : "normal_mode");
        };
        return btn;
    }

    // ── Drive Mode Averages ───────────────────────────────────────────────
    static (FrameworkElement, Action<StrategySnapshot>) BuildDriveAvg()
    {
        static string FF(double f) => f > 0 ? $"{f * 100:F2}%/lap" : "—";
        static string FT(double secs)
        {
            if (secs <= 0) return "--:--.---";
            int m = (int)(secs / 60); double s = secs % 60;
            return $"{m}:{s:06.3f}";
        }

        var n1 = Val(13); var n2 = Val(13);
        var p1 = Val(13); var p2 = Val(13);
        var s1 = Val(13); var s2 = Val(13);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        for (int i = 0; i < 4; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        void Header(string t, int col) =>
            AddCell(new TextBlock { Text = t, FontFamily = new FontFamily("Segoe UI"), FontSize = 11,
                Foreground = Dim, Margin = new Thickness(0, 0, col < 2 ? 10 : 0, 4) }, col, 0);

        void AddCell(UIElement el, int col, int row)
        { Grid.SetColumn(el, col); Grid.SetRow(el, row); grid.Children.Add(el); }

        void DataCell(TextBlock tb, int col, int row)
        { tb.Margin = new Thickness(0, 3, col < 2 ? 10 : 0, 0); AddCell(tb, col, row); }

        void ModeLabel(string t, int row, Brush fg) =>
            AddCell(new TextBlock { Text = t, FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
                Foreground = fg, Margin = new Thickness(0, 3, 10, 0) }, 0, row);

        Header("MODE", 0); Header("FUEL/LAP", 1); Header("LAP TIME", 2);
        ModeLabel("Normal", 1, Dim2); DataCell(n1, 1, 1); DataCell(n2, 2, 1);
        ModeLabel("Push",   2, PushBr); DataCell(p1, 1, 2); DataCell(p2, 2, 2);
        ModeLabel("Save",   3, SaveBr); DataCell(s1, 1, 3); DataCell(s2, 2, 3);

        return (grid, snap =>
        {
            n1.Text = FF(snap.AvgFuel);    n2.Text = FT(snap.AvgLapTime);
            p1.Text = FF(snap.PushAvgFuel); p2.Text = FT(snap.PushAvgTime);
            s1.Text = FF(snap.SaveAvgFuel); s2.Text = FT(snap.SaveAvgTime);
        });
    }

    // ── Energy / Fuel card ────────────────────────────────────────────────
    static (FrameworkElement, Action<StrategySnapshot>) BuildFuel()
    {
        var vLaps  = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 34,
                                      FontWeight = FontWeights.Bold, Foreground = Blue };
        var vNrg   = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 16,
                                      FontWeight = FontWeights.Bold, Foreground = Green };
        var vPerLap = Val(13); var vAvg3 = Val(13); var vAvg10 = Val(13);
        var vExtra  = Val(13); var vSave  = Val(13);
        var vStrat  = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
                                       FontWeight = FontWeights.SemiBold, Foreground = Dim,
                                       TextWrapping = TextWrapping.Wrap };

        // Top row: fuel laps left + NRG pill
        var topGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var lapsStack = new StackPanel();
        lapsStack.Children.Add(Lbl("FUEL LAPS LEFT"));
        lapsStack.Children.Add(vLaps);
        Grid.SetColumn(lapsStack, 0);
        topGrid.Children.Add(lapsStack);

        var nrgBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x11, 0x11, 0x16)),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        var nrgStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        nrgStack.Children.Add(Lbl("NRG"));
        nrgStack.Children.Add(vNrg);
        nrgBorder.Child = nrgStack;
        Grid.SetColumn(nrgBorder, 1);
        topGrid.Children.Add(nrgBorder);

        var metrics = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 4) };
        metrics.Children.Add(LV("PER LAP",    vPerLap, 6));
        metrics.Children.Add(LV("3-LAP AVG",  vAvg3,   6));
        metrics.Children.Add(LV("10-LAP AVG", vAvg10));

        var extra = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 4) };
        extra.Children.Add(LV("EXTRA STOPS", vExtra, 6));
        extra.Children.Add(LV("SAVE TARGET", vSave));

        var sp = new StackPanel();
        sp.Children.Add(topGrid);
        sp.Children.Add(Sep());
        sp.Children.Add(metrics);
        sp.Children.Add(extra);
        sp.Children.Add(vStrat);

        return (sp, s =>
        {
            double lve = s.LapsOnVe;
            vLaps.Text = lve > 0 ? $"{lve:F1}" : "—";
            vNrg.Text  = s.Ve > 0 ? $"{s.Ve:F1}%" : "—%";
            vNrg.Foreground = s.Ve > 30 ? Green : s.Ve > 15 ? Yellow : Red;
            vPerLap.Text = s.AvgFuel  > 0 ? $"{s.AvgFuel  * 100:F2}%" : "—";
            vAvg3.Text   = s.AvgFuel3 > 0 ? $"{s.AvgFuel3 * 100:F2}%/lap" : "—";
            vAvg10.Text  = s.AvgFuel10> 0 ? $"{s.AvgFuel10* 100:F2}%/lap" : "—";
            vExtra.Text  = s.ExtraStops > 0 ? $"+{s.ExtraStops}" : "0";
            vSave.Text   = s.FuelSaveTarget > 0.005 ? $"save {s.FuelSaveTarget * 100:F2}%/lap" : "OK";

            if (s.ExtraStops > 0)
            { vStrat.Text = $"Add {(int)Math.Ceiling(s.FuelDeficit * 100)}% VE at next stop"; vStrat.Foreground = Red; }
            else if (s.FuelSaveTarget > 0.005)
            { vStrat.Text = $"Lift & coast {s.FuelSaveTarget * 100:F2}%/lap"; vStrat.Foreground = Yellow; }
            else
            { vStrat.Text = "✓ Energy nominal"; vStrat.Foreground = Green; }
        });
    }

    // ── Tire card ─────────────────────────────────────────────────────────
    static (FrameworkElement, Action<StrategySnapshot>) BuildTire()
    {
        var settings = App.Settings;
        var wears = new TextBlock[4];
        var temps = new TextBlock[4];
        var cmpds = new TextBlock[4];
        var dots  = new Ellipse[4];
        string[] names = ["FL", "FR", "RL", "RR"];

        var sp = new StackPanel();
        for (int i = 0; i < 4; i++)
        {
            wears[i] = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 15,
                                        FontWeight = FontWeights.Bold, Foreground = Green };
            temps[i] = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 13,
                                        Foreground = Dim, Margin = new Thickness(8, 0, 0, 0) };
            cmpds[i] = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 11,
                                        Foreground = Accent, Margin = new Thickness(8, 0, 0, 0),
                                        VerticalAlignment = VerticalAlignment.Center };
            dots[i] = new Ellipse { Width = 8, Height = 8, Fill = Green,
                                     VerticalAlignment = VerticalAlignment.Center,
                                     Margin = new Thickness(0, 0, 5, 0) };

            var row = new StackPanel { Orientation = Orientation.Horizontal,
                                        Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(dots[i]);
            row.Children.Add(new TextBlock { Text = names[i], Width = 20, Foreground = Dim,
                FontFamily = new FontFamily("Segoe UI"), FontSize = 12, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(wears[i]);
            row.Children.Add(temps[i]);
            row.Children.Add(cmpds[i]);
            sp.Children.Add(row);
        }

        sp.Children.Add(Sep());
        var vStatus = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
                                       FontWeight = FontWeights.SemiBold, Foreground = Dim,
                                       TextWrapping = TextWrapping.Wrap };
        sp.Children.Add(vStatus);

        return (sp, s =>
        {
            for (int i = 0; i < 4; i++)
            {
                double w  = s.TireWear.Length   > i ? s.TireWear[i]    : 0;
                double tc = s.TireTempsC.Length  > i ? s.TireTempsC[i]  : 0;
                int    ct = s.CompoundTypes.Length > i ? s.CompoundTypes[i] : -1;

                var wc = WearBrush(w, settings);
                dots[i].Fill        = wc;
                wears[i].Text       = $"{w * 100:F0}%";
                wears[i].Foreground = wc;
                temps[i].Text       = $"{tc:F0}°C";
                temps[i].Foreground = TempBrush(tc, settings);
                cmpds[i].Text       = ct >= 0 && ct < CompoundShort.Length ? CompoundShort[ct] : "";
            }

            double minHealth = s.TireWear.Length == 4 ? s.TireWear.Min() * 100 : 100;
            if (minHealth > settings.TireWarnWear)
            { vStatus.Text = "✓ Tires nominal";                          vStatus.Foreground = Green; }
            else if (minHealth > settings.TireCritWear)
            { vStatus.Text = $"⚠ Worn — {minHealth:F0}% health";         vStatus.Foreground = Yellow; }
            else
            { vStatus.Text = $"▼ Critical — {minHealth:F0}% health";     vStatus.Foreground = Red; }
        });
    }

    static Brush WearBrush(double w, AppSettings s)
    {
        double r = w * 100;
        if (r > s.TireWarnWear) return Green;
        if (r > s.TireCritWear) return Yellow;
        return Red;
    }

    static Brush TempBrush(double tc, AppSettings s)
    {
        if (tc < s.TireColdTemp)  return B("BlueBrush");
        if (tc > s.TireHotTemp)   return Red;
        if (tc >= s.TireOptLow && tc <= s.TireOptHigh) return Green;
        return Yellow;
    }

    static Brush ActionBrush(StrategyActionType a) => a switch
    {
        StrategyActionType.PitNowFullFuel  or
        StrategyActionType.PitNowShortFuel => Accent,
        StrategyActionType.PushUndercut    => PushBr,
        StrategyActionType.SaveFuel        => SaveBr,
        StrategyActionType.StayOut         => Green,
        StrategyActionType.CoverRival      => Blue,
        _                                  => Dim,
    };

    // ── Strategy Recommendation (compact HUD) ─────────────────────────────
    static (FrameworkElement, Action<StrategySnapshot>) BuildStratRec()
    {
        var vAction = new TextBlock { FontFamily = new FontFamily("Consolas"),
            FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Fg };
        var vPitLap = new TextBlock { FontFamily = new FontFamily("Consolas"),
            FontSize = 13, Foreground = Fg, Margin = new Thickness(0, 2, 0, 0) };
        var vConf   = new TextBlock { FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11, Foreground = Dim, Margin = new Thickness(0, 2, 0, 0) };

        var sp = new StackPanel();
        sp.Children.Add(Lbl("STRATEGY"));
        sp.Children.Add(vAction);
        sp.Children.Add(Sep());
        sp.Children.Add(vPitLap);
        sp.Children.Add(vConf);

        return (sp, _ =>
        {
            var rec = App.Telem.LatestRecommendation();
            if (rec == null) { vAction.Text = "—"; vPitLap.Text = ""; vConf.Text = ""; return; }

            vAction.Text       = rec.ActionLabel.ToUpper();
            vAction.Foreground = ActionBrush(rec.Action);
            vPitLap.Text       = rec.RecommendedPitLap > 0
                                 ? $"Pit: Lap {rec.RecommendedPitLap}" : "No pit planned";

            double gap = rec.AltScore > 0 ? Math.Abs(rec.TopScore - rec.AltScore) : 0;
            string conf = gap switch { > 0.25 => "HIGH", > 0.10 => "MED", _ => "LOW" };
            vConf.Text       = $"Conf: {conf}  ·  ±{rec.FinishTimeUncertainty:F0}s";
            vConf.Foreground = gap switch { > 0.25 => Green, > 0.10 => Yellow, _ => Dim };
        });
    }

    // ── Pit Window (compact) ──────────────────────────────────────────────
    static (FrameworkElement, Action<StrategySnapshot>) BuildStratPitWin()
    {
        var vRec = Val(); var vEarly = Val(); var vLate = Val(); var vLim = Val();
        var ui   = Quad("REC", vRec, "EARLIEST", vEarly, "LATEST", vLate, "LIMITED BY", vLim);
        return (ui, _ =>
        {
            var rec  = App.Telem.LatestRecommendation();
            var snap = App.Telem.Snapshot();
            vRec.Text   = rec?.RecommendedPitLap > 0 ? $"Lap {rec.RecommendedPitLap}"
                        : snap?.PitLap.HasValue == true ? $"Lap {snap.PitLap:F0}" : "—";
            vEarly.Text = snap?.EarliestPit > 0          ? $"Lap {snap.EarliestPit:F0}" : "—";
            vLate.Text  = snap?.LatestPit.HasValue == true ? $"Lap {snap.LatestPit:F0}"  : "—";
            vLim.Text   = snap?.PitLimiter?.ToUpperInvariant() ?? "—";
        });
    }

    // ── Projections (compact) ─────────────────────────────────────────────
    static (FrameworkElement, Action<StrategySnapshot>) BuildStratProj()
    {
        var vPos   = new TextBlock { FontFamily = new FontFamily("Consolas"),
            FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Fg };
        var vFuel  = Val(); var vCliff = Val();

        var sp = new StackPanel();
        sp.Children.Add(LV("FINISH POS", vPos));
        sp.Children.Add(Sep());
        sp.Children.Add(Pair("FUEL MARGIN", vFuel, "CLIFF RISK", vCliff));

        return (sp, _ =>
        {
            var rec = App.Telem.LatestRecommendation();
            if (rec == null) { vPos.Text = "—"; vFuel.Text = "—"; vCliff.Text = "—"; return; }

            vPos.Text = $"P{rec.ProjectedPosition}";

            vFuel.Text = rec.FuelSafetyMargin >= 0 ? $"{rec.FuelSafetyMargin:F1}L" : "LOW";
            vFuel.Foreground = rec.FuelSafetyMargin switch
                { >= 2 => Green, >= 0.5 => Yellow, _ => Red };

            double cliff = rec.TireCliffRisk;
            vCliff.Text       = cliff switch { > 0.66 => "HIGH", > 0.33 => "MED", _ => "LOW" };
            vCliff.Foreground = cliff switch { > 0.66 => Red,    > 0.33 => Yellow, _ => Green };
        });
    }

    // ── Rival Intel (compact) ─────────────────────────────────────────────
    static (FrameworkElement, Action<StrategySnapshot>) BuildStratRival()
    {
        var vGap   = Val(); var vPit   = Val();
        var vUnder = Val(); var vOver  = Val();
        var ui     = Quad("RIVAL GAP", vGap, "RIVAL PIT EST.", vPit,
                          "UNDERCUT",  vUnder, "OVERCUT",      vOver);
        return (ui, _ =>
        {
            var rec = App.Telem.LatestRecommendation();
            if (rec == null) { vGap.Text = vPit.Text = vUnder.Text = vOver.Text = "—"; return; }

            vGap.Text = rec.NearestRivalGap.HasValue
                        ? $"{rec.NearestRivalGap.Value:F1}s" : "—";
            vPit.Text = rec.RivalEstimatedPitLap.HasValue
                        ? $"~Lap {rec.RivalEstimatedPitLap.Value:F0}" : "—";

            vUnder.Text       = rec.UndercutThreat ? "YES" : "NO";
            vUnder.Foreground = rec.UndercutThreat ? Red : Dim;
            vOver.Text        = rec.OvercutOpportunity ? "YES" : "NO";
            vOver.Foreground  = rec.OvercutOpportunity ? Green : Dim;
        });
    }
}
