using System.Windows;
using System.Windows.Controls;

namespace LmuRaceEngineerCS.Views;

public partial class VoicePage : UserControl
{
    public VoicePage()
    {
        InitializeComponent();

        // Show whatever is already in the log
        RefreshLog();

        // Voice-command controls: reflect current state and subscribe to live status
        ChkListen.IsChecked = App.Settings.SttEnabled;
        SttStatusText.Text  = App.SttStatus;
        App.SttStatusChanged += OnSttStatus;

        // Subscribe for live updates
        App.Tts.LogUpdated += OnLogUpdated;
        Unloaded += (_, _) =>
        {
            App.Tts.LogUpdated  -= OnLogUpdated;
            App.SttStatusChanged -= OnSttStatus;
        };
    }

    private void OnSttStatus(string status)
        => Dispatcher.InvokeAsync(() => SttStatusText.Text = status);

    private void ChkListen_Click(object sender, RoutedEventArgs e)
        => App.SetVoiceCommandsEnabled(ChkListen.IsChecked == true);

    private void OnLogUpdated(object? sender, EventArgs e)
        => Dispatcher.InvokeAsync(RefreshLog);

    private void RefreshLog()
    {
        var entries = App.Tts.GetLog();
        TbLog.Text = string.Join("\n", entries);
        LogScroll.ScrollToBottom();
    }

    private void Speak_Click(object sender, RoutedEventArgs e)
    {
        string text = TtsTestBox.Text.Trim();
        if (text.Length > 0)
            App.Tts.Speak(text);
    }

    private void Shortcut_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string key)
            App.Tts.Speak(key);
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        // Clearing the in-memory log requires access to the service internals;
        // easiest is to just blank the display — the service list keeps running.
        TbLog.Text = "";
    }
}
