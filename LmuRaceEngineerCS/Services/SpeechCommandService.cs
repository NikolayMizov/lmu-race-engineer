using LmuRaceEngineerCS.Models;
using System.Globalization;
using System.Speech.Recognition;

namespace LmuRaceEngineerCS.Services;

/// <summary>
/// Listens on the default microphone using Windows SAPI.
/// Fires CommandRecognized(intent) when a phrase passes the confidence threshold.
/// </summary>
public sealed class SpeechCommandService : IDisposable
{
    private SpeechRecognitionEngine? _engine;
    private readonly Dictionary<string, string> _phraseToIntent = new(StringComparer.OrdinalIgnoreCase);
    private bool _running;

    public float ConfidenceThreshold { get; private set; } = 0.6f;
    public bool  IsRunning => _running;

    /// <summary>Human-readable current state for the UI (e.g. "🎙 Listening").</summary>
    public string Status { get; private set; } = "⏹ Off";

    public event EventHandler<string>?  CommandRecognized;
    public event EventHandler<string>?  ErrorOccurred;
    /// <summary>Fires whenever <see cref="Status"/> changes (start / stop / phrase heard / error).</summary>
    public event EventHandler<string>?  StatusChanged;

    /// <summary>True when at least one usable speech recognizer is installed on this PC.</summary>
    public static bool RecognizerInstalled
    {
        get
        {
            try   { return SpeechRecognitionEngine.InstalledRecognizers().Count > 0; }
            catch { return false; }
        }
    }

    private void SetStatus(string s)
    {
        Status = s;
        StatusChanged?.Invoke(this, s);
    }

    // ── Start ─────────────────────────────────────────────────────────────────

    public void Start(IEnumerable<VoiceCommand> commands, float confidenceThreshold)
    {
        if (_running) return;
        try
        {
            ConfidenceThreshold = confidenceThreshold;
            var recognizerInfo = SpeechRecognitionEngine.InstalledRecognizers()
                .FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == "en")
                ?? SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault();

            if (recognizerInfo == null)
            {
                const string msg = "No speech recognizer installed. Add one in Windows Settings → "
                                 + "Time & language → Speech, or install an English speech pack.";
                SetStatus("⚠ No recognizer installed");
                ErrorOccurred?.Invoke(this, msg);
                return;
            }

            _engine = new SpeechRecognitionEngine(recognizerInfo);
            _engine.SpeechRecognized          += OnRecognized;
            _engine.SpeechRecognitionRejected  += OnRejected;

            BuildGrammar(commands);

            _engine.SetInputToDefaultAudioDevice();
            _engine.RecognizeAsync(RecognizeMode.Multiple);
            _running = true;
            SetStatus("🎙 Listening");
        }
        catch (Exception ex)
        {
            SetStatus("⚠ Mic unavailable");
            ErrorOccurred?.Invoke(this, $"Failed to start speech recognition: {ex.Message}");
            _engine?.Dispose();
            _engine = null;
        }
    }

    // ── Reload (live grammar + threshold update) ──────────────────────────────

    public void Reload(IEnumerable<VoiceCommand> commands, float confidenceThreshold)
    {
        ConfidenceThreshold = confidenceThreshold;
        if (_engine == null) return;
        try
        {
            _engine.RecognizeAsyncStop();
            BuildGrammar(commands);
            _engine.RecognizeAsync(RecognizeMode.Multiple);
        }
        catch { /* ignore mid-race reload failures */ }
    }

    // ── Grammar builder ───────────────────────────────────────────────────────

    private void BuildGrammar(IEnumerable<VoiceCommand> commands)
    {
        _phraseToIntent.Clear();
        var choices = new Choices();
        bool any = false;

        foreach (var cmd in commands)
        {
            foreach (var phrase in cmd.Phrases)
            {
                string t = phrase.Trim().ToLowerInvariant();
                if (t.Length == 0) continue;
                _phraseToIntent[t] = cmd.Intent;
                choices.Add(t);
                any = true;
            }
        }

        _engine!.UnloadAllGrammars();
        if (!any) return;

        var gb = new GrammarBuilder(choices);
        _engine.LoadGrammar(new Grammar(gb));
    }

    // ── Recognition handlers ──────────────────────────────────────────────────

    private void OnRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        if (e.Result.Confidence < ConfidenceThreshold) return;
        string text = e.Result.Text.Trim().ToLowerInvariant();
        if (_phraseToIntent.TryGetValue(text, out string? intent))
        {
            SetStatus($"🎙 Heard: “{text}”");
            CommandRecognized?.Invoke(this, intent);
        }
    }

    private void OnRejected(object? sender, SpeechRecognitionRejectedEventArgs e) { }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Stop()
    {
        if (!_running) return;
        try { _engine?.RecognizeAsyncStop(); } catch { }
        _running = false;
        SetStatus("⏹ Off");
    }

    public void Dispose()
    {
        Stop();
        if (_engine != null)
        {
            _engine.SpeechRecognized         -= OnRecognized;
            _engine.SpeechRecognitionRejected -= OnRejected;
            _engine.Dispose();
            _engine = null;
        }
    }
}
