using LmuRaceEngineerCS.Shared;
using System.Globalization;
using System.IO;

namespace LmuRaceEngineerCS.Services;

/// <summary>
/// Writes one CSV row per completed lap to a per-session file under laps/.
/// Direct port of Python LapLogger.
/// </summary>
public sealed class LapLogger : IDisposable
{
    // NOTE: fuel_start_L holds the Virtual-Energy FRACTION at lap start (0–1; 1.0 = full),
    // not litres — kept under the historical name for CSV back-compatibility. nrg_used_%
    // (appended last so existing column indices are unchanged) is the VE consumed during
    // the lap, as a percentage.
    private static readonly string[] Columns =
    [
        "lap", "mode", "s1", "s2", "s3", "lap_time",
        "fuel_start_L",
        "fl_wear_%", "fr_wear_%", "rl_wear_%", "rr_wear_%",
        "nrg_used_%",
    ];

    private readonly string _logDir = Path.Combine(AppContext.BaseDirectory, "laps");
    private StreamWriter?   _writer;
    private string          _sessionKey = "";

    public void StartSession(string track, string car, string sessionType)
    {
        string key = $"{track}|{car}|{sessionType}";
        if (key == _sessionKey) return;
        Close();
        _sessionKey = key;
        Directory.CreateDirectory(_logDir);
        string ts   = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string name = $"{Safe(track)}_{Safe(car)}_{Safe(sessionType)}_{ts}.csv";
        _writer = new StreamWriter(Path.Combine(_logDir, name), append: false);
        _writer.WriteLine(string.Join(",", Columns));
        _writer.Flush();
    }

    public void LogLap(int lapNum, double lapTime, double s1, double s2Cum,
                       double fuelStart, double fuelUsed, double[] wearStart, string mode = "normal")
    {
        if (_writer is null || lapTime <= 10) return;

        string s1t  = s1     > 0             ? F3(s1)                    : "";
        string s2t  = s2Cum  > s1 && s1 > 0  ? F3(s2Cum - s1)           : "";
        string s3t  = s2Cum  > 0 && lapTime > s2Cum ? F3(lapTime - s2Cum): "";

        var row = new string[]
        {
            lapNum.ToString(),
            mode,
            s1t, s2t, s3t,
            F3(lapTime),
            F3(fuelStart),
            F1(wearStart.Length > 0 ? wearStart[0] * 100 : 0),
            F1(wearStart.Length > 1 ? wearStart[1] * 100 : 0),
            F1(wearStart.Length > 2 ? wearStart[2] * 100 : 0),
            F1(wearStart.Length > 3 ? wearStart[3] * 100 : 0),
            // VE consumed this lap, %. Negative on a refuel/pit lap → clamp to 0.
            F1(Math.Max(0.0, fuelUsed) * 100),
        };
        _writer.WriteLine(string.Join(",", row));
        _writer.Flush();
    }

    private void Close()
    {
        _writer?.Dispose();
        _writer     = null;
        _sessionKey = "";
    }

    public void Dispose() => Close();

    private static string Safe(string s) => RaceUtils.SafeName(s);

    private static string F3(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
    private static string F1(double v) => v.ToString("F1", CultureInfo.InvariantCulture);
}
