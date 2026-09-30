using LmuRaceEngineerCS.Models;
using LmuRaceEngineerCS.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace LmuRaceEngineerCS;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _headerTimer;
    private readonly Dictionary<string, Button> _navBtns;
    private string _currentPage = "";

    public MainWindow()
    {
        InitializeComponent();

        _navBtns = new()
        {
            ["telemetry"]  = BtnTelemetry,
            ["engineer"]   = BtnEngineer,
            ["profiles"]   = BtnProfiles,
            ["monitoring"] = BtnMonitoring,
            ["laps"]       = BtnLaps,
            ["voice"]      = BtnVoice,
            ["settings"]   = BtnSettings,
        };

        // Reflect current edit-layout state on the toggle
        BtnEditLayout.IsChecked = App.EditLayout;

        // Subscribe to snapshot updates for header bar
        App.Telem.SnapshotUpdated += OnSnapshotUpdated;

        // Refresh header every 500 ms
        _headerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _headerTimer.Tick += (_, _) => RefreshHeader();
        _headerTimer.Start();

        NavigateTo("telemetry");

        if (App.Settings.ShowTutorialOnStartup)
            Loaded += (_, _) => new TutorialWindow { Owner = this }.Show();
    }

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        bool visible = SidebarCol.Width.Value > 0;
        SidebarCol.Width = visible ? new GridLength(0) : new GridLength(220);
        Sidebar.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private void EditLayout_Click(object sender, RoutedEventArgs e)
        => App.EditLayout = BtnEditLayout.IsChecked == true;

    private void NavClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
            NavigateTo(tag);
    }

    private void NavigateTo(string page)
    {
        if (_currentPage == page) return;
        _currentPage = page;

        UserControl newPage = page switch
        {
            "telemetry"  => new TelemetryPage(),
            "engineer"   => new EngineerPage(),
            "profiles"   => new DriverProfilePage(),
            "monitoring" => new MonitoringPage(),
            "laps"       => new LapsPage(),
            "voice"      => new VoicePage(),
            "settings"   => new SettingsPage(),
            _             => new TelemetryPage(),
        };
        PageFrame.Children.Clear();
        PageFrame.Children.Add(newPage);

        foreach (var (key, btn) in _navBtns)
        {
            bool active = key == page;
            btn.Background = active ? (Brush)App.Current.Resources["NavActiveBrush"]
                                     : Brushes.Transparent;
            btn.Foreground = active ? Brushes.White
                                     : (Brush)App.Current.Resources["Dim2Brush"];
        }
    }

    private void OnSnapshotUpdated(object? sender, StrategySnapshot snap)
    {
        // Cross-thread dispatch — snap fires on the telemetry thread
        Dispatcher.BeginInvoke(() => UpdateHeader(snap));
    }

    private void RefreshHeader()
    {
        var snap = App.Telem.Snapshot();

        // Auto-open the overlays enabled in Settings once a session is live (and close
        // them when it ends). Only fires on the transition, so manual closes stick.
        App.Overlays.SyncToSession(snap != null);

        if (snap != null)
        {
            UpdateHeader(snap);
        }
        else
        {
            // No snapshot yet — show the diagnostic so the user can see exactly
            // which step is blocking (MMF missing, game not running, no car, etc.)
            string diag    = App.Telem.DiagnosticStatus;
            bool   mmfOpen = App.Telem.IsConnected;
            bool   suspect = App.Telem.IsLayoutSuspect;   // shared-memory layout changed
            TbHeader.Text       = diag;
            TbHeader.Foreground = suspect
                ? (Brush)Resources["RedBrush"]
                : (Brush)Resources["YellowBrush"];
            ConnDot.Fill        = suspect ? (Brush)Resources["RedBrush"]
                                : mmfOpen ? (Brush)Resources["YellowBrush"]
                                          : (Brush)Resources["DimBrush"];
            TbConn.Text         = suspect ? "Update needed" : mmfOpen ? "Waiting…" : "Disconnected";
            TbConn.Foreground   = suspect ? (Brush)Resources["RedBrush"]
                                : mmfOpen ? (Brush)Resources["YellowBrush"]
                                          : (Brush)Resources["DimBrush"];
        }
    }

    private void UpdateHeader(StrategySnapshot snap)
    {
        if (!App.Telem.IsConnected)
        {
            // Show the specific diagnostic so user knows exactly what to fix
            string diag = App.Telem.DiagnosticStatus;
            TbHeader.Text       = diag.Length > 0 ? diag : "Disconnected — start LMU and load a session.";
            TbHeader.Foreground = (Brush)Resources["YellowBrush"];
            ConnDot.Fill        = (Brush)Resources["DimBrush"];
            TbConn.Text         = "Disconnected";
            TbConn.Foreground   = (Brush)Resources["DimBrush"];
            return;
        }

        ConnDot.Fill      = (Brush)Resources["GreenBrush"];
        TbConn.Text       = "Connected";
        TbConn.Foreground = (Brush)Resources["GreenBrush"];

        string track = snap.TrackName.Length > 0 ? snap.TrackName : "—";
        string lapTxt;
        if (snap.SessionTimeRemaining > 30)
        {
            int rm = (int)(snap.SessionTimeRemaining / 60);
            int rs = (int)(snap.SessionTimeRemaining % 60);
            lapTxt = $"Lap {snap.CurrentLap}  ·  {rm}:{rs:D2} left";
        }
        else lapTxt = $"Lap {snap.CurrentLap}";

        string dm     = snap.DriveMode;
        string modeTxt = dm == "normal" ? "" : $"  ◀ {dm.ToUpper()} ▶";

        TbHeader.Text = $"{track}  ·  P{snap.ClassPos}/{snap.ClassTotal}  ·  {lapTxt}{modeTxt}";
        TbHeader.Foreground = dm == "push"
            ? (Brush)Resources["PushBrush"]
            : dm == "save"
                ? (Brush)Resources["SaveBrush"]
                : (Brush)Resources["FgBrush"];

        if (snap.LapInvalid)
        {
            TbConn.Text       = "LAP INVALID";
            TbConn.Foreground = (Brush)Resources["RedBrush"];
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (App.IsLearning)
        {
            var result = MessageBox.Show(
                "Profile learning is in progress — it will be cancelled if you close now.\n\nClose anyway?",
                "Profile Learning in Progress",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.No)
            { e.Cancel = true; return; }
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _headerTimer.Stop();
        App.Telem.SnapshotUpdated -= OnSnapshotUpdated;
        base.OnClosed(e);
    }
}
