using LmuRaceEngineerCS.Models;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace LmuRaceEngineerCS.Views;

public partial class OverlayWindow : Window
{
    private readonly Action<StrategySnapshot> _updater;
    private readonly DispatcherTimer _timer;
    private readonly string _boundsKey;

    public OverlayWindow(string title, FrameworkElement content, Action<StrategySnapshot> updater)
    {
        InitializeComponent();
        TbTitle.Text = title.ToUpper();
        WidgetHost.Content = content;
        _updater = updater;
        _boundsKey = title;

        RestoreOverlayBounds();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) =>
        {
            var snap = App.Telem.Snapshot();
            if (snap != null) _updater(snap);
        };
        _timer.Start();
        Closed += (_, _) => { _timer.Stop(); SaveOverlayBounds(); };
    }

    // ── Bounds persistence (per widget title) ─────────────────────────────────
    private void RestoreOverlayBounds()
    {
        var st = LayoutState.Load();
        if (st.OverlayBounds.TryGetValue(_boundsKey, out var b) && b.Length == 4)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left   = b[0];
            Top    = b[1];
            Width  = Math.Max(MinWidth,  b[2]);
            Height = Math.Max(MinHeight, b[3]);
        }
    }

    private void SaveOverlayBounds()
    {
        var st = LayoutState.Load();
        st.OverlayBounds[_boundsKey] = new[] { Left, Top, Width, Height };
        st.Save();
    }

    private void DragBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    // ── Resize handlers ───────────────────────────────────────────────────────

    private void ResizeS_Delta(object s, DragDeltaEventArgs e) =>
        Height = Math.Max(MinHeight, Height + e.VerticalChange);

    private void ResizeE_Delta(object s, DragDeltaEventArgs e) =>
        Width = Math.Max(MinWidth, Width + e.HorizontalChange);

    private void ResizeSE_Delta(object s, DragDeltaEventArgs e)
    {
        Width  = Math.Max(MinWidth,  Width  + e.HorizontalChange);
        Height = Math.Max(MinHeight, Height + e.VerticalChange);
    }

    private void ResizeN_Delta(object s, DragDeltaEventArgs e)
    {
        double h = Height - e.VerticalChange;
        if (h >= MinHeight) { Top += e.VerticalChange; Height = h; }
    }

    private void ResizeW_Delta(object s, DragDeltaEventArgs e)
    {
        double w = Width - e.HorizontalChange;
        if (w >= MinWidth) { Left += e.HorizontalChange; Width = w; }
    }

    private void ResizeNE_Delta(object s, DragDeltaEventArgs e)
    {
        Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        double h = Height - e.VerticalChange;
        if (h >= MinHeight) { Top += e.VerticalChange; Height = h; }
    }

    private void ResizeNW_Delta(object s, DragDeltaEventArgs e)
    {
        double w = Width - e.HorizontalChange;
        if (w >= MinWidth) { Left += e.HorizontalChange; Width = w; }
        double h = Height - e.VerticalChange;
        if (h >= MinHeight) { Top += e.VerticalChange; Height = h; }
    }

    private void ResizeSW_Delta(object s, DragDeltaEventArgs e)
    {
        double w = Width - e.HorizontalChange;
        if (w >= MinWidth) { Left += e.HorizontalChange; Width = w; }
        Height = Math.Max(MinHeight, Height + e.VerticalChange);
    }
}
