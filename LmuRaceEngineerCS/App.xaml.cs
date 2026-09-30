using LmuRaceEngineerCS.Services;
using System.IO;
using System.Windows;

namespace LmuRaceEngineerCS;

public partial class App : Application
{
    // Singletons accessible by all pages
    public static AudioService        Audio    { get; private set; } = null!;
    public static EdgeTtsService      Tts      { get; private set; } = null!;
    public static TelemetryService    Telem    { get; private set; } = null!;
    public static SpeechCommandService? Stt    { get; private set; }
    public static ProfileLearner      Learner  { get; private set; } = null!;
    public static OverlayManager      Overlays { get; private set; } = null!;

    private static readonly IntentResponder _responder = new();

    private AppSettings _settings = AppSettings.Load();
    public static AppSettings Settings => ((App)Current)._settings;

    private CancellationTokenSource _learnCts = new();
    public static bool IsLearning { get; private set; }

    // ── Voice-command (STT) status surfaced to the UI ─────────────────────────
    public static string SttStatus { get; private set; } = "⏹ Off";
    public static event Action<string>? SttStatusChanged;
    private static void SetSttStatus(string s)
    {
        SttStatus = s;
        SttStatusChanged?.Invoke(s);
    }

    // ── Edit-Layout mode (data-box resizing) ──────────────────────────────────
    private static bool _editLayout;
    /// <summary>When true, data boxes across the app show a resize grip and can be dragged.</summary>
    public static bool EditLayout
    {
        get => _editLayout;
        set { if (_editLayout == value) return; _editLayout = value; EditLayoutChanged?.Invoke(value); }
    }
    public static event Action<bool>? EditLayoutChanged;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string cacheDir = Path.Combine(AppContext.BaseDirectory, "audio_cache");

        Audio = new AudioService();
        Audio.Volume = _settings.TtsVolume;

        Tts = new EdgeTtsService(Audio, cacheDir);
        Tts.Voice = _settings.TtsVoice;
        Tts.Start();

        // Load historical driver profile on a background thread — non-blocking
        Learner = new ProfileLearner();
        var cts = _learnCts;
        Task.Run(() =>
        {
            IsLearning = true;
            try   { if (!cts.Token.IsCancellationRequested) Learner.LearnAll(); }
            finally { IsLearning = false; }
        }, cts.Token);

        Overlays = new OverlayManager();

        Telem = new TelemetryService(Audio, Tts);
        Telem.Start();

        if (_settings.SttEnabled)
            StartStt();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _learnCts.Cancel();
        Overlays?.CloseAll();
        Stt?.Dispose();
        Telem.Stop();
        Tts.Dispose();
        Audio.Dispose();
        _settings.Save();
        base.OnExit(e);
    }

    // ── STT lifecycle (called from Settings page on save) ─────────────────────

    public static void ApplySttSettings()
    {
        if (Settings.SttEnabled)
        {
            if (Stt == null) StartStt();
            else
            {
                Stt.Reload(Settings.VoiceCommands, Settings.SttConfidenceThreshold);
                SetSttStatus(Stt.Status);
            }
        }
        else
        {
            Stt?.Dispose();
            Stt = null;
            SetSttStatus("⏹ Off");
        }
    }

    /// <summary>Toggle voice-command listening from the UI and persist the choice.</summary>
    public static void SetVoiceCommandsEnabled(bool enabled)
    {
        Settings.SttEnabled = enabled;
        ApplySttSettings();
        Settings.Save();
    }

    private static void StartStt()
    {
        if (!SpeechCommandService.RecognizerInstalled)
        {
            SetSttStatus("⚠ No recognizer installed");
            return;
        }
        Stt = new SpeechCommandService();
        Stt.CommandRecognized += (_, intent) => _responder.Handle(intent);
        Stt.ErrorOccurred     += (_, msg)    => SetSttStatus($"⚠ {msg}");
        Stt.StatusChanged     += (_, msg)    => SetSttStatus(msg);
        Stt.Start(Settings.VoiceCommands, Settings.SttConfidenceThreshold);
    }
}
