using Microsoft.Win32;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace LmuSimulator;

public partial class MainWindow : Window
{
    private readonly SimWriter _writer = new();
    private readonly SimState  _state  = new();

    private readonly DispatcherTimer _writeTimer = new();
    private readonly DispatcherTimer _lapTimer   = new();

    private bool _autoRunning;
    // Start true so the slider ValueChanged handlers that fire during InitializeComponent
    // (when Minimum coerces Value) are suppressed until the first SyncAll runs — otherwise
    // they touch sibling elements that don't exist yet. SyncAll clears it.
    private bool _sync = true;

    // ── CSV replay ────────────────────────────────────────────────────────────
    private record ReplayLap(int Lap, double Ve, double AccumWear, double LapTime, bool Pitted);
    private List<ReplayLap>? _replay;
    private int    _replayIdx;
    private string _replayName = "";

    public MainWindow()
    {
        InitializeComponent();

        _writeTimer.Interval = TimeSpan.FromMilliseconds(500);
        _writeTimer.Tick    += (_, _) => FlushState();

        _lapTimer.Tick += (_, _) => AdvanceLap();

        _state.ApplyScenario("mid");
        SyncAll();
    }

    // ── MMF ───────────────────────────────────────────────────────────────────

    private void BtnCreate_Click(object sender, RoutedEventArgs e)
    {
        if (_writer.IsOpen)
        {
            StopAuto();
            _writeTimer.Stop();
            _writer.Dispose();
            SetStatus(false, "Stopped");
            BtnCreate.Content       = "Create MMF";
            BtnAutoToggle.IsEnabled = false;
            BtnNextLap.IsEnabled    = false;
            return;
        }

        if (!_writer.TryCreate())
        {
            SetStatus(false, "Failed to create MMF");
            return;
        }

        SetStatus(true, "Running");
        BtnCreate.Content       = "Stop";
        BtnAutoToggle.IsEnabled = true;
        BtnNextLap.IsEnabled    = true;
        FlushState();
        _writeTimer.Start();
    }

    // ── Auto-advance ──────────────────────────────────────────────────────────

    private void BtnAutoToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_autoRunning) StopAuto();
        else              StartAuto();
    }

    private void StartAuto()
    {
        double divisor = SpeedBox.SelectedIndex switch { 0 => 1.0, 1 => 5.0, 2 => 10.0, _ => 30.0 };
        _lapTimer.Interval = TimeSpan.FromSeconds(_state.LapTimeSecs / divisor);
        _lapTimer.Start();
        _autoRunning             = true;
        BtnAutoToggle.Content    = "⏹ Stop Auto";
        BtnAutoToggle.Background = new SolidColorBrush(Color.FromRgb(0x80, 0x20, 0x20));
    }

    private void StopAuto()
    {
        _lapTimer.Stop();
        _autoRunning             = false;
        BtnAutoToggle.Content    = "▶ Auto";
        BtnAutoToggle.Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x6b, 0x2e));
    }

    private void AdvanceLap()
    {
        if (_replay != null)
        {
            if (_replayIdx + 1 >= _replay.Count) { StopAuto(); return; }  // end of file
            ApplyReplayRow(++_replayIdx);
        }
        else _state.AdvanceLap();
        SyncAll();
        FlushState();
    }

    private void BtnNextLap_Click(object sender, RoutedEventArgs e) => AdvanceLap();

    // ── CSV replay ────────────────────────────────────────────────────────────

    private void BtnLoadCsv_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Load a laps CSV to replay",
            Filter = "Lap CSV (*.csv)|*.csv|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() == true) LoadReplay(dlg.FileName);
    }

    private void BtnClearReplay_Click(object sender, RoutedEventArgs e)
    {
        StopAuto();
        _replay     = null;
        _replayName = "";
        UpdateReplayStatus();
    }

    /// <summary>Parse a laps CSV into replay frames and start at lap 1.</summary>
    private void LoadReplay(string path)
    {
        var parsed = new List<(int lap, double ve, double wear, double lt)>();
        try
        {
            string[] lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i++)
            {
                string[] p = lines[i].Split(',');
                if (p.Length < 11) continue;
                if (!int.TryParse(p[0].Trim(), out int lap)) continue;
                if (!TryD(p[5], out double lt) || lt <= 10) continue;
                TryD(p[6], out double ve);                       // VE fraction at lap start
                double worstRem = Math.Min(Math.Min(D(p[7]), D(p[8])),
                                           Math.Min(D(p[9]), D(p[10]))) / 100.0;
                double accum = Math.Clamp(1.0 - worstRem, 0.0, 1.0);
                parsed.Add((lap, ve, accum, lt));
            }
        }
        catch { }

        parsed.Sort((a, b) => a.lap.CompareTo(b.lap));

        var laps = new List<ReplayLap>();
        double prevVe = -1, prevWear = -1;
        foreach (var r in parsed)
        {
            bool pitted = (prevVe   >= 0 && r.ve   > prevVe   + 0.02)   // refuelled
                       || (prevWear >= 0 && r.wear < prevWear - 0.05);  // fresh tyres
            laps.Add(new ReplayLap(r.lap, r.ve, r.wear, r.lt, pitted));
            prevVe = r.ve; prevWear = r.wear;
        }

        if (laps.Count == 0)
        {
            ReplayStatus.Text = "CSV had no usable laps.";
            return;
        }

        StopAuto();
        _replay        = laps;
        _replayName    = Path.GetFileName(path);
        _replayIdx     = 0;
        _state.MaxLaps = Math.Max(laps[^1].Lap, laps.Count);
        _state.PitStops = 0;
        _state.CurrentET = 0;
        _state.BestLapTime = laps.Min(l => l.LapTime);
        ApplyReplayRow(0);
        SyncAll();
        FlushState();
    }

    private void ApplyReplayRow(int idx)
    {
        var r = _replay![idx];
        if (r.Pitted) _state.PitStops++;
        _state.Lap           = r.Lap;
        _state.VirtualEnergy = r.Ve;
        _state.TireWear      = (float)r.AccumWear;   // accumulated; SimWriter inverts to remaining
        _state.LapTimeSecs   = r.LapTime;
        _state.LastLapTime   = r.LapTime;
        if (r.LapTime < _state.BestLapTime) _state.BestLapTime = r.LapTime;
        _state.CurrentET    += r.LapTime;
        _state.InPits        = r.Pitted;
        UpdateReplayStatus();
    }

    private void UpdateReplayStatus()
    {
        ReplayStatus.Text = _replay == null
            ? "No replay loaded — using scenarios / sliders"
            : $"Replay: {_replayName}  ·  lap {_replay[_replayIdx].Lap}  ({_replayIdx + 1}/{_replay.Count})";
    }

    private static bool TryD(string s, out double v) =>
        double.TryParse(s.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out v);
    private static double D(string s) => TryD(s, out double v) ? v : 0;

    // ── Scenarios ─────────────────────────────────────────────────────────────

    private void Scenario_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string tag)
        {
            StopAuto();
            _replay = null;             // scenarios exit CSV-replay mode
            UpdateReplayStatus();
            _state.ApplyScenario(tag);
            SyncAll();
            FlushState();
        }
    }

    // ── Sliders — Race Position ───────────────────────────────────────────────

    private void SldLap_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sync) return;
        _state.Lap = (int)SldLap.Value;
        SldLapVal.Text = _state.Lap.ToString();
        RefreshLiveDisplay(); FlushState();
    }

    private void SldMaxLaps_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sync) return;
        _state.MaxLaps = (int)SldMaxLaps.Value;
        SldMaxLapsVal.Text = _state.MaxLaps.ToString();
        FlushState();
    }

    private void SldGapF_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sync) return;
        _state.GapFront = (float)SldGapF.Value;
        SldGapFVal.Text = $"{SldGapF.Value:F1}s";
        FlushState();
    }

    private void SldGapB_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sync) return;
        _state.GapBehind = (float)SldGapB.Value;
        SldGapBVal.Text = $"{SldGapB.Value:F1}s";
        FlushState();
    }

    private void SldPitStops_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sync) return;
        _state.PitStops = (int)SldPitStops.Value;
        SldPitStopsVal.Text = _state.PitStops.ToString();
        FlushState();
    }

    // ── Sliders — Energy & Tires ──────────────────────────────────────────────

    private void SldVe_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sync) return;
        _state.VirtualEnergy = SldVe.Value / 100.0;
        SldVeVal.Text = $"{SldVe.Value:F0}%";
        RefreshLiveDisplay(); FlushState();
    }

    private void SldFuelRate_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sync) return;
        _state.FuelPerLap = SldFuelRate.Value / 100.0;
        SldFuelRateVal.Text = $"{SldFuelRate.Value:F1}%";
        FlushState();
    }

    private void SldWear_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sync) return;
        _state.TireWear = (float)(SldWear.Value / 100.0);
        SldWearVal.Text = $"{SldWear.Value:F0}%";
        RefreshLiveDisplay(); FlushState();
    }

    private void SldWearRate_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sync) return;
        _state.WearPerLap = SldWearRate.Value / 100.0;
        SldWearRateVal.Text = $"{SldWearRate.Value:F1}%";
        FlushState();
    }

    private void SldBrake_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sync) return;
        _state.BrakeTemp = SldBrake.Value;
        SldBrakeVal.Text = $"{SldBrake.Value:F0}°";
        FlushState();
    }

    // ── Sliders — Weather & Track ─────────────────────────────────────────────

    private void SldRain_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sync) return;
        _state.Rain = (float)SldRain.Value;
        SldRainVal.Text = $"{SldRain.Value:F2}";
        RefreshLiveDisplay(); FlushState();
    }

    private void SldWetness_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sync) return;
        _state.AvgPathWetness = SldWetness.Value;
        SldWetnessVal.Text = $"{SldWetness.Value:F2}";
        FlushState();
    }

    private void SldCloud_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sync) return;
        _state.DarkCloud = SldCloud.Value;
        SldCloudVal.Text = $"{SldCloud.Value:F2}";
        FlushState();
    }

    private void SldTrackTemp_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sync) return;
        _state.TrackTemp = SldTrackTemp.Value;
        SldTrackTempVal.Text = $"{SldTrackTemp.Value:F0}°C";
        FlushState();
    }

    private void SldAmbTemp_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sync) return;
        _state.AmbientTemp = SldAmbTemp.Value;
        SldAmbTempVal.Text = $"{SldAmbTemp.Value:F0}°C";
        FlushState();
    }

    private void SldGrip_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sync) return;
        _state.TrackGrip = (byte)SldGrip.Value;
        SldGripVal.Text = $"{(int)SldGrip.Value}%";
        FlushState();
    }

    private void CboRainTrend_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_sync) return;
        _state.RainTrend = CboRainTrend.SelectedIndex switch
        {
            1 => RainTrendMode.GettingWetter,
            2 => RainTrendMode.Drying,
            _ => RainTrendMode.Static,
        };
        RefreshLiveDisplay();
    }

    private void SldRainRate_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sync) return;
        _state.RainRatePerLap = SldRainRate.Value;
        SldRainRateVal.Text = $"{SldRainRate.Value:F2}";
    }

    // ── Sliders — Lap Times & Sim Params ─────────────────────────────────────

    private void SldLapTime_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sync) return;
        _state.LapTimeSecs = SldLapTime.Value;
        SldLapTimeVal.Text = $"{SldLapTime.Value:F1}s";
        FlushState();
    }

    private void SldLastLap_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sync) return;
        _state.LastLapTime = SldLastLap.Value;
        SldLastLapVal.Text = $"{SldLastLap.Value:F1}s";
        FlushState();
    }

    private void SldBestLap_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sync) return;
        _state.BestLapTime = SldBestLap.Value;
        SldBestLapVal.Text = $"{SldBestLap.Value:F1}s";
        FlushState();
    }

    private void SldDelta_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sync) return;
        _state.DeltaBest = SldDelta.Value;
        string sign = SldDelta.Value >= 0 ? "+" : "";
        SldDeltaVal.Text = $"{sign}{SldDelta.Value:F2}s";
        FlushState();
    }

    private void SldJitter_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sync) return;
        _state.LapTimeJitter = SldJitter.Value;
        SldJitterVal.Text = $"±{SldJitter.Value:F1}s";
    }

    private void SldGapJitter_Changed(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sync) return;
        _state.GapJitter = SldGapJitter.Value;
        SldGapJitterVal.Text = $"±{SldGapJitter.Value:F1}s";
    }

    // ── Pit / lap invalid ─────────────────────────────────────────────────────

    private void PitBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string tag)
        {
            _state.InPits = tag == "enter";
            if (tag == "exit") _state.PitStops++;
            SyncAll();
            FlushState();
        }
    }

    private void ChkLapInvalid_Changed(object sender, RoutedEventArgs e) => FlushState();

    // ── Sync helpers ──────────────────────────────────────────────────────────

    private void SyncAll()
    {
        _sync = true;

        // Race position
        SldLap.Value         = _state.Lap;
        SldMaxLaps.Value     = _state.MaxLaps;
        SldGapF.Value        = _state.GapFront;
        SldGapB.Value        = _state.GapBehind;
        SldPitStops.Value    = _state.PitStops;

        SldLapVal.Text       = _state.Lap.ToString();
        SldMaxLapsVal.Text   = _state.MaxLaps.ToString();
        SldGapFVal.Text      = $"{_state.GapFront:F1}s";
        SldGapBVal.Text      = $"{_state.GapBehind:F1}s";
        SldPitStopsVal.Text  = _state.PitStops.ToString();

        // Energy & tires
        SldVe.Value          = _state.VirtualEnergy * 100;
        SldFuelRate.Value    = _state.FuelPerLap    * 100;
        SldWear.Value        = _state.TireWear      * 100;
        SldWearRate.Value    = _state.WearPerLap    * 100;
        SldBrake.Value       = _state.BrakeTemp;

        SldVeVal.Text        = $"{_state.VirtualEnergy * 100:F0}%";
        SldFuelRateVal.Text  = $"{_state.FuelPerLap    * 100:F1}%";
        SldWearVal.Text      = $"{_state.TireWear      * 100:F0}%";
        SldWearRateVal.Text  = $"{_state.WearPerLap    * 100:F1}%";
        SldBrakeVal.Text     = $"{_state.BrakeTemp:F0}°";

        // Weather
        SldRain.Value        = _state.Rain;
        SldWetness.Value     = _state.AvgPathWetness;
        SldCloud.Value       = _state.DarkCloud;
        SldTrackTemp.Value   = _state.TrackTemp;
        SldAmbTemp.Value     = _state.AmbientTemp;
        SldGrip.Value        = _state.TrackGrip;
        SldRainRate.Value    = _state.RainRatePerLap;
        CboRainTrend.SelectedIndex = _state.RainTrend switch
        {
            RainTrendMode.GettingWetter => 1,
            RainTrendMode.Drying        => 2,
            _                           => 0,
        };

        SldRainVal.Text      = $"{_state.Rain:F2}";
        SldWetnessVal.Text   = $"{_state.AvgPathWetness:F2}";
        SldCloudVal.Text     = $"{_state.DarkCloud:F2}";
        SldTrackTempVal.Text = $"{_state.TrackTemp:F0}°C";
        SldAmbTempVal.Text   = $"{_state.AmbientTemp:F0}°C";
        SldGripVal.Text      = $"{_state.TrackGrip}%";
        SldRainRateVal.Text  = $"{_state.RainRatePerLap:F2}";

        // Lap times
        SldLapTime.Value     = _state.LapTimeSecs;
        SldLastLap.Value     = _state.LastLapTime;
        SldBestLap.Value     = _state.BestLapTime;
        SldDelta.Value       = _state.DeltaBest;
        SldJitter.Value      = _state.LapTimeJitter;
        SldGapJitter.Value   = _state.GapJitter;

        SldLapTimeVal.Text   = $"{_state.LapTimeSecs:F1}s";
        SldLastLapVal.Text   = $"{_state.LastLapTime:F1}s";
        SldBestLapVal.Text   = $"{_state.BestLapTime:F1}s";
        string sign          = _state.DeltaBest >= 0 ? "+" : "";
        SldDeltaVal.Text     = $"{sign}{_state.DeltaBest:F2}s";
        SldJitterVal.Text    = $"±{_state.LapTimeJitter:F1}s";
        SldGapJitterVal.Text = $"±{_state.GapJitter:F1}s";

        _sync = false;
        RefreshLiveDisplay();
    }

    private void RefreshLiveDisplay()
    {
        DispLap.Text  = _state.Lap.ToString();
        DispVe.Text   = $"{_state.VirtualEnergy * 100:F0}";
        DispWear.Text = $"{_state.TireWear * 100:F0}";
        DispRain.Text = $"{_state.Rain:F2}";
        DispTrend.Text = _state.RainTrend switch
        {
            RainTrendMode.GettingWetter => "↑ wet",
            RainTrendMode.Drying        => "↓ dry",
            _                           => "—",
        };
    }

    private void FlushState()
    {
        if (!_writer.IsOpen) return;
        _writer.Write(_state);
    }

    private void SetStatus(bool active, string text)
    {
        StatusDot.Fill = new SolidColorBrush(active
            ? Color.FromRgb(0x22, 0xc5, 0x5e)
            : Color.FromRgb(0xef, 0x44, 0x44));
        StatusTxt.Text = text;
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _lapTimer.Stop();
        _writeTimer.Stop();
        _writer.Dispose();
    }
}
