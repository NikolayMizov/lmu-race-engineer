using LmuRaceEngineerCS.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LmuRaceEngineerCS.Views;

/// <summary>
/// Engineer command view. Reuses the existing <see cref="StrategyPage"/> verbatim — the strategy
/// panel is identical for a driver and a non-driving engineer, and runs off the same learned
/// driver profile and strategy engine — and adds a driver-swap handover strip on top.
/// Read-only: shared memory cannot write pit settings back into LMU.
/// </summary>
public partial class EngineerPage : UserControl
{
    private readonly Action<StrategySnapshot> _handoverUpdater;
    private readonly DispatcherTimer _timer;

    public EngineerPage()
    {
        InitializeComponent();

        // Handover card — reuse the shared widget builder.
        var (content, updater) = WidgetFactory.Build("driverswap");
        HandoverHost.Content = content;
        _handoverUpdater = updater;

        // Host the strategy panel unchanged; it manages its own subscriptions/timer.
        StrategyHost.Content = new StrategyPage();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        Loaded   += (_, _) => Refresh();
        Unloaded += (_, _) => _timer.Stop();
    }

    private void Refresh()
    {
        var snap = App.Telem.Snapshot();
        if (snap != null) _handoverUpdater(snap);
    }

    // Pop the handover card out to its own always-on-top window. Routed through
    // App.Overlays so it is tracked centrally alongside the Settings overlays and
    // survives navigating away from this tab.
    private void PopOut_Click(object sender, RoutedEventArgs e)
        => App.Overlays.Open("driverswap", "Driver Handover");
}
