using LmuRaceEngineerCS.Models;
using System.IO;
using System.Text.Json;

namespace LmuRaceEngineerCS;

public class AppSettings
{
    private static readonly string FilePath =
        Path.Combine(AppContext.BaseDirectory, "settings.json");

    // ── Telemetry thresholds ──────────────────────────────────────────────────
    public double TireWarnWear  { get; set; } = 40;   // % remaining life
    public double TireCritWear  { get; set; } = 20;   // % remaining life
    public int    TireColdTemp  { get; set; } = 65;
    public int    TireOptLow    { get; set; } = 70;
    public int    TireOptHigh   { get; set; } = 90;
    public int    TireHotTemp   { get; set; } = 95;
    public int    BrakeWarnTemp { get; set; } = 600;
    public int    BrakeCritTemp { get; set; } = 850;
    public double FuelSafetyLaps { get; set; } = 1.0;
    public int    FuelLapHistory { get; set; } = 5;

    // ── Strategy engine ──────────────────────────────────────────────────────
    public double PitLossSeconds      { get; set; } = 25.0;   // static pit time cost
    public int    StrategyIntervalMs  { get; set; } = 12_000; // recompute period
    public bool   StrategyVoiceOnly   { get; set; } = false;  // skip timer compute; fire only on voice command

    // ── TTS ───────────────────────────────────────────────────────────────────
    public string TtsVoice  { get; set; } = "en-US-GuyNeural";
    public float  TtsVolume { get; set; } = 1.0f;

    // ── UI ────────────────────────────────────────────────────────────────────
    public bool ShowTutorialOnStartup { get; set; } = true;

    // ── Speech recognition ────────────────────────────────────────────────────
    /// <summary>WidgetFactory keys of the driving overlays enabled in Settings → Overlays.</summary>
    public List<string> EnabledOverlays { get; set; } = new();

    public bool  SttEnabled             { get; set; } = true;
    public float SttConfidenceThreshold { get; set; } = 0.60f;
    public List<VoiceCommand> VoiceCommands { get; set; } = DefaultVoiceCommands();

    public static List<VoiceCommand> DefaultVoiceCommands() =>
    [
        // ── Pit strategy ──────────────────────────────────────────────────────
        new() { Intent = "pit_window",   Phrases = ["pit window", "when to pit", "when should i pit", "when do i pit", "laps to pit", "pit lap"] },
        new() { Intent = "fuel_add",     Phrases = ["fuel to add", "how much to add", "add fuel", "litres to add", "liters to add", "fuel at pit"] },
        new() { Intent = "pit_stops",    Phrases = ["pit stops", "how many stops", "stops remaining", "more stops", "how many pits"] },
        new() { Intent = "strategy_rec", Phrases = ["strategy", "what's the strategy", "what should i do", "recommendation", "engineer recommendation", "what's my strategy"] },
        new() { Intent = "rival_update", Phrases = ["rival update", "undercut threat", "overcut opportunity", "what is rival doing", "rival status", "threat check"] },
        new() { Intent = "fuel_margin",  Phrases = ["fuel margin", "fuel buffer", "fuel safety", "fuel to finish", "fuel reserve"] },
        new() { Intent = "tire_cliff",   Phrases = ["tire cliff", "tyre cliff", "cliff risk", "tire failure risk", "are my tires done"] },
        new() { Intent = "projected_position", Phrases = ["projected position", "final position", "where will i finish", "predicted position", "finish position"] },

        // ── Gaps ──────────────────────────────────────────────────────────────
        new() { Intent = "gap_front",    Phrases = ["gap ahead", "car ahead", "car in front", "how far ahead", "gap in front", "am i faster", "am i catching him", "gap to leader"] },
        new() { Intent = "gap_behind",   Phrases = ["gap behind", "car behind", "how close behind", "how far behind", "is he catching me", "gap to car behind"] },

        // ── Fuel and energy ───────────────────────────────────────────────────
        new() { Intent = "energy",       Phrases = ["virtual energy", "how much energy", "energy level", "fuel level", "how much fuel", "ve level", "fuel remaining"] },

        // ── Session info ──────────────────────────────────────────────────────
        new() { Intent = "laps_remaining", Phrases = ["laps left", "laps remaining", "how many laps", "laps to go", "how many laps left"] },
        new() { Intent = "position",     Phrases = ["my position", "what position", "where am i", "what place", "current position", "p what"] },

        // ── Tires ─────────────────────────────────────────────────────────────
        new() { Intent = "tire_wear",    Phrases = ["tire health", "tyre health", "tire wear", "tyre wear", "how are my tires", "how are my tyres", "tire condition", "wear", "tire deg", "tire life", "tyre condition"] },
        new() { Intent = "tire_temps",   Phrases = ["tire temps", "tyre temps", "tire temperature", "tyre temperature", "tire heat", "how hot are my tires", "tyre temperature"] },
        new() { Intent = "stint_length", Phrases = ["stint length", "stint laps", "how long on these tires", "current stint", "how long this stint"] },

        // ── Lap times ─────────────────────────────────────────────────────────
        new() { Intent = "lap_time",     Phrases = ["lap time", "my pace", "average lap", "how fast am i", "current pace", "average pace"] },
        new() { Intent = "last_lap",     Phrases = ["last lap", "last lap time", "what was my last lap", "previous lap"] },
        new() { Intent = "best_lap",     Phrases = ["best lap", "personal best", "my best time", "pb", "fastest lap"] },
        new() { Intent = "delta",        Phrases = ["delta", "delta to best", "how far off my best", "time delta"] },

        // ── Mode and pace ─────────────────────────────────────────────────────
        new() { Intent = "mode_stats",   Phrases = ["mode stats", "push average", "save average", "mode data", "what's my push", "what is my save", "push stats", "save stats"] },
        new() { Intent = "pace_mode_gain", Phrases = ["gain on car ahead", "what will be my gain", "gain with push", "gain with save", "time gain push", "time gain save", "pace gain", "gap gain", "push benefit"] },

        // ── Summaries ─────────────────────────────────────────────────────────
        new() { Intent = "full_update",  Phrases = ["summary", "full update", "full status", "give me everything", "status update", "all data"] },
        new() { Intent = "repeat_last",  Phrases = ["repeat", "say again", "repeat last", "come again", "pardon"] },
        new() { Intent = "radio_check",  Phrases = ["radio check", "comms check", "check radio", "how do you read", "loud and clear"] },

        // ── Temperatures and conditions ───────────────────────────────────────
        new() { Intent = "brake_temps",  Phrases = ["brake temps", "brake temperature", "how hot are my brakes", "brake heat"] },
        new() { Intent = "track_conditions", Phrases = ["track conditions", "weather", "track temp", "grip level", "how's the track", "ambient temperature"] },
        new() { Intent = "hybrid_charge", Phrases = ["battery", "hybrid charge", "state of charge", "battery level", "hybrid level", "ers level"] },
    ];

    // ── Load / Save ───────────────────────────────────────────────────────────
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                var s = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                if (s.VoiceCommands == null || s.VoiceCommands.Count == 0)
                {
                    s.VoiceCommands = DefaultVoiceCommands();
                }
                else
                {
                    // Merge new intents introduced in updates
                    var existing = new HashSet<string>(s.VoiceCommands.Select(v => v.Intent));
                    foreach (var def in DefaultVoiceCommands())
                        if (!existing.Contains(def.Intent))
                            s.VoiceCommands.Add(def);
                }
                return s;
            }
        }
        catch { /* fall through to defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch { /* ignore write errors */ }
    }
}
