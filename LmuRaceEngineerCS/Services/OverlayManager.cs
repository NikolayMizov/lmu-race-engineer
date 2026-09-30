using LmuRaceEngineerCS.Views;

namespace LmuRaceEngineerCS.Services;

/// <summary>
/// Central registry of pop-out overlay windows keyed by <see cref="WidgetFactory"/> key.
/// Lets Settings → Overlays toggle driving overlays app-wide, auto-opens the enabled ones
/// once a session goes live, and closes them when it ends. Pages can still pop individual
/// widgets out on demand through the same registry, so a widget is never opened twice.
/// Overlay size/position persists per title via <see cref="OverlayWindow"/>.
/// </summary>
public sealed class OverlayManager
{
    public sealed record OverlayDef(string Key, string Title, string Description);

    /// <summary>Driving overlays offered in Settings — none duplicate the in-game HUD.</summary>
    public static readonly OverlayDef[] Catalog =
    [
        new("pedals",      "Pedal Inputs",          "Throttle & brake position, gear and speed"),
        new("steering",    "Steering Trace",        "Steering input — reveals corrections / scrub"),
        new("slip",        "Lockup / Wheelspin",    "Per-corner lockup and wheelspin detector"),
        new("tyrepress",   "Tyre Pressure & Temps", "Pressure plus inner/mid/outer temps vs optimal"),
        new("tracklimits", "Track Limits",          "Strikes used before the next penalty"),
        new("damage",      "Tyre / Wheel Damage",   "Flat or detached wheel alert"),
    ];

    private readonly Dictionary<string, OverlayWindow> _open = new();
    private bool? _lastSessionActive;

    public bool IsOpen(string key) => _open.ContainsKey(key);

    public static string TitleFor(string key)
        => Catalog.FirstOrDefault(d => d.Key == key)?.Title ?? key;

    /// <summary>Open an overlay (or focus it when <paramref name="activate"/> and already open).</summary>
    public void Open(string key, string? title = null, bool activate = true)
    {
        if (_open.TryGetValue(key, out var existing))
        {
            if (activate) existing.Activate();
            return;
        }

        var (content, updater) = WidgetFactory.Build(key);
        var win = new OverlayWindow(title ?? TitleFor(key), content, updater);
        win.Closed += (_, _) => _open.Remove(key);
        _open[key] = win;
        win.Show();
    }

    public void Close(string key)
    {
        if (!_open.TryGetValue(key, out var win)) return;
        _open.Remove(key);
        win.Close();
    }

    public void Set(string key, bool on)
    {
        if (on) Open(key);
        else    Close(key);
    }

    public void CloseAll()
    {
        foreach (var win in _open.Values.ToList()) win.Close();
        _open.Clear();
    }

    /// <summary>
    /// Opens the enabled overlays when a session becomes active and closes them when it ends.
    /// Only acts on the transition, so an overlay the user closes mid-session stays closed.
    /// </summary>
    public void SyncToSession(bool sessionActive)
    {
        if (_lastSessionActive == sessionActive) return;
        _lastSessionActive = sessionActive;

        foreach (string key in App.Settings.EnabledOverlays.ToList())
        {
            if (sessionActive) Open(key, activate: false);
            else               Close(key);
        }
    }
}
