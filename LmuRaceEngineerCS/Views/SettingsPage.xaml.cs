using LmuRaceEngineerCS.Models;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace LmuRaceEngineerCS.Views;

public partial class SettingsPage : UserControl
{
    private readonly AppSettings _s = App.Settings;
    private readonly List<SettingRow> _tireRows;
    private readonly List<VoiceCmdRow> _cmdRows;

    /// <summary>
    /// One checkbox per overlay in <see cref="Services.OverlayManager.Catalog"/>. Ticking saves
    /// the choice and opens the overlay straight away so it can be positioned; the session
    /// sync then reopens enabled overlays automatically at the start of each session.
    /// </summary>
    private void BuildOverlayList()
    {
        foreach (var def in Services.OverlayManager.Catalog)
        {
            var chk = new CheckBox
            {
                Content   = def.Title,
                IsChecked = _s.EnabledOverlays.Contains(def.Key),
                Tag       = def.Key,
                Margin    = new Thickness(0, 0, 0, 2),
                Foreground = (System.Windows.Media.Brush)FindResource("FgBrush"),
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontSize  = 13,
                Cursor    = System.Windows.Input.Cursors.Hand,
            };
            chk.Click += Overlay_Toggled;

            var hint = new TextBlock
            {
                Text       = def.Description,
                Style      = (Style)FindResource("Hint"),
                Margin     = new Thickness(22, 0, 0, 10),
            };

            OverlayList.Children.Add(chk);
            OverlayList.Children.Add(hint);
        }
    }

    private void Overlay_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox chk || chk.Tag is not string key) return;
        bool on = chk.IsChecked == true;

        if (on) { if (!_s.EnabledOverlays.Contains(key)) _s.EnabledOverlays.Add(key); }
        else    { _s.EnabledOverlays.Remove(key); }

        _s.Save();
        App.Overlays.Set(key, on);
    }

    public SettingsPage()
    {
        InitializeComponent();

        _tireRows =
        [
            new("Tire Warn Health",     _s.TireWarnWear.ToString("F0"), "% health"),
            new("Tire Critical Health", _s.TireCritWear.ToString("F0"), "% health"),
            new("Tire Cold Temp",     _s.TireColdTemp.ToString(),     "°C"),
            new("Tire Optimal Low",   _s.TireOptLow.ToString(),       "°C"),
            new("Tire Optimal High",  _s.TireOptHigh.ToString(),      "°C"),
            new("Tire Hot Temp",      _s.TireHotTemp.ToString(),      "°C"),
            new("Brake Warn Temp",    _s.BrakeWarnTemp.ToString(),    "°C"),
            new("Brake Critical Temp",_s.BrakeCritTemp.ToString(),    "°C"),
        ];
        TireSettings.ItemsSource = _tireRows;

        BuildOverlayList();

        FuelSafetyBox.Text  = _s.FuelSafetyLaps.ToString("F1");
        FuelHistoryBox.Text = _s.FuelLapHistory.ToString();

        // Strategy engine
        StratIntervalBox.Text        = _s.StrategyIntervalMs.ToString();
        StratVoiceOnlyCheck.IsChecked = _s.StrategyVoiceOnly;

        // TTS
        SelectComboByContent(VoiceCombo, _s.TtsVoice);
        VolumeSlider.Value = _s.TtsVolume;
        VolumeSlider.ValueChanged += (_, e) =>
        {
            VolLabel.Text    = $"{e.NewValue:F2}";
            App.Audio.Volume = (float)e.NewValue;
        };

        // STT
        SttEnabledCheck.IsChecked = _s.SttEnabled;
        ConfidenceSlider.Value    = _s.SttConfidenceThreshold;
        ConfLabel.Text            = $"{_s.SttConfidenceThreshold * 100:F0}%";
        ConfidenceSlider.ValueChanged += (_, e) => ConfLabel.Text = $"{e.NewValue * 100:F0}%";

        // Voice commands list
        _cmdRows = _s.VoiceCommands
            .Select(v => new VoiceCmdRow(v.Intent, string.Join(", ", v.Phrases)))
            .ToList();
        VoiceCmdList.ItemsSource = _cmdRows;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _s.TtsVoice = ((ComboBoxItem)VoiceCombo.SelectedItem)?.Content?.ToString()
                          ?? _s.TtsVoice;
            _s.TtsVolume = (float)VolumeSlider.Value;

            if (_tireRows.Count >= 8)
            {
                _s.TireWarnWear  = ParseD(_tireRows[0].Value, _s.TireWarnWear);
                _s.TireCritWear  = ParseD(_tireRows[1].Value, _s.TireCritWear);
                _s.TireColdTemp  = ParseI(_tireRows[2].Value, _s.TireColdTemp);
                _s.TireOptLow    = ParseI(_tireRows[3].Value, _s.TireOptLow);
                _s.TireOptHigh   = ParseI(_tireRows[4].Value, _s.TireOptHigh);
                _s.TireHotTemp   = ParseI(_tireRows[5].Value, _s.TireHotTemp);
                _s.BrakeWarnTemp = ParseI(_tireRows[6].Value, _s.BrakeWarnTemp);
                _s.BrakeCritTemp = ParseI(_tireRows[7].Value, _s.BrakeCritTemp);
            }

            _s.FuelSafetyLaps = ParseD(FuelSafetyBox.Text,  _s.FuelSafetyLaps);
            _s.FuelLapHistory = ParseI(FuelHistoryBox.Text,  _s.FuelLapHistory);

            // Strategy engine
            _s.StrategyIntervalMs = ParseI(StratIntervalBox.Text, _s.StrategyIntervalMs);
            _s.StrategyVoiceOnly  = StratVoiceOnlyCheck.IsChecked == true;

            // STT settings
            _s.SttEnabled             = SttEnabledCheck.IsChecked == true;
            _s.SttConfidenceThreshold = (float)ConfidenceSlider.Value;
            _s.VoiceCommands = _cmdRows
                .Select(r => new VoiceCommand
                {
                    Intent  = r.Intent,
                    Phrases = r.Phrases
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                })
                .Where(v => v.Phrases.Length > 0)
                .ToList();

            _s.Save();
            App.Audio.Volume = _s.TtsVolume;
            App.ApplySttSettings();

            SaveStatus.Text = $"Saved at {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            SaveStatus.Text = $"Error: {ex.Message}";
        }
    }

    private void ResetProfile_Click(object sender, RoutedEventArgs e)
    {
        App.Learner.ResetProfile();
        ResetProfileStatus.Text = $"Reset at {DateTime.Now:HH:mm:ss}";
    }

    private static double ParseD(string s, double def)
        => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : def;

    private static int ParseI(string s, int def)
        => int.TryParse(s, out int v) ? v : def;

    private static void SelectComboByContent(ComboBox cb, string value)
    {
        foreach (ComboBoxItem item in cb.Items)
        {
            if (item.Content?.ToString() == value)
            { cb.SelectedItem = item; return; }
        }
        if (cb.Items.Count > 0) cb.SelectedIndex = 0;
    }

    // ── Row models ────────────────────────────────────────────────────────────

    private class SettingRow(string label, string value, string unit)
    {
        public string Label { get; } = label;
        public string Value { get; set; } = value;
        public string Unit  { get; } = unit;
    }

    private class VoiceCmdRow(string intent, string phrases)
    {
        public string Intent        { get; } = intent;
        public string DisplayIntent { get; } = CultureInfo.InvariantCulture.TextInfo
            .ToTitleCase(intent.Replace('_', ' '));
        public string Phrases       { get; set; } = phrases;
    }
}
