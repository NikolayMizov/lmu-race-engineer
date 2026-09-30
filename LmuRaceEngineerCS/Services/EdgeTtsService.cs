using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace LmuRaceEngineerCS.Services;

/// <summary>
/// Edge TTS via Microsoft's WebSocket synthesis endpoint.
/// Zero cost, no API key — uses the same backend as Microsoft Edge browser.
///
/// LATENCY STRATEGY:
///   Static phrases (fixed text, e.g. "Lap invalidated") are pre-generated
///   as MP3 files at startup and played instantly from disk.
///   Dynamic phrases (containing live data) hit the API on-demand, with
///   typical round-trip latency of 200–600 ms on a decent connection.
/// </summary>
public sealed class EdgeTtsService : IDisposable
{
    // ── Edge TTS WebSocket constants ──────────────────────────────────────────
    private const string TrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    private const string WssUrl      = "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1";
    private const string ChromiumVer  = "143";
    private const string SecMsGecVer  = "1-143.0.3650.75";
    private const string UserAgent    = $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{ChromiumVer}.0.0.0 Safari/537.36 Edg/{ChromiumVer}.0.0.0";
    private const string AudioFormat  = "audio-24khz-48kbitrate-mono-mp3";

    // Voice configuration — change to any en-* Neural voice from Edge TTS list.
    public string Voice { get; set; } = "en-US-GuyNeural";
    public string Rate  { get; set; } = "+15%";   // speech rate relative to default

    // ── Static phrase registry ────────────────────────────────────────────────
    // Key → (cacheId, text to synthesize)
    private static readonly Dictionary<string, string> StaticPhrases = new()
    {
        ["lap_invalid"]  = "Lap invalidated.",
        ["track_limit"]  = "One more track limit will cause a penalty.",
        ["push_mode"]    = "Push mode activated.",
        ["save_mode"]    = "Save mode activated.",
        ["normal_mode"]  = "Back to normal mode.",
        ["radio_check"]  = "Loud and clear.",
        ["no_data"]      = "Not enough data yet.",
        ["leading"]      = "You are leading your class.",
    };

    // ── Queue ─────────────────────────────────────────────────────────────────
    // Items are either a static key (e.g. "push_mode") or a dynamic sentence.
    private readonly BlockingCollection<string> _queue = new(8);
    private readonly AudioService  _audio;
    private readonly string        _cacheDir;
    private readonly Dictionary<string, string> _staticFiles = new();  // key → file path

    private Thread?       _workerThread;
    private volatile bool _running;

    // ── In-memory log ─────────────────────────────────────────────────────────
    private readonly List<string> _log    = new();
    private const int             LogCap  = 500;
    public  event EventHandler?   LogUpdated;
    public  IReadOnlyList<string> GetLog() { lock (_log) return _log.ToList(); }

    // ── Constructor ───────────────────────────────────────────────────────────
    public EdgeTtsService(AudioService audio, string cacheDir)
    {
        _audio    = audio;
        _cacheDir = cacheDir;
        Directory.CreateDirectory(cacheDir);
    }

    // ── Start (runs pre-gen then opens worker) ────────────────────────────────
    public void Start()
    {
        _running      = true;
        _workerThread = new Thread(WorkerLoop) { IsBackground = true, Name = "TtsWorker" };
        _workerThread.Start();

        // Pre-generate in a separate background task so startup is non-blocking.
        Task.Run(PreGenerateStaticsAsync);
    }

    public void Speak(string keyOrText)
    {
        if (!_running) return;
        bool queued = false;
        try { queued = _queue.TryAdd(keyOrText, 0); }
        catch { /* queue full or disposed — drop */ }
        string label = StaticPhrases.TryGetValue(keyOrText, out var txt) ? $"\"{txt}\"" : $"\"{keyOrText}\"";
        LogTts(queued ? $"Queued {label}" : $"Dropped (queue full) {label}");
    }

    // ── Worker loop ───────────────────────────────────────────────────────────
    private void WorkerLoop()
    {
        foreach (var item in _queue.GetConsumingEnumerable())
        {
            if (!_running) break;
            try { PlayItem(item); }
            catch (Exception ex)
            {
                LogTts($"TTS error for '{item}': {ex.Message}");
            }
        }
    }

    private void PlayItem(string item)
    {
        // Is it a static key with a pre-generated file?
        if (_staticFiles.TryGetValue(item, out var path) && File.Exists(path))
        {
            _audio.PlayMp3File(path);
            return;
        }

        // Determine the text to speak.
        string text = StaticPhrases.TryGetValue(item, out var staticText) ? staticText : item;

        // Synthesize via Edge TTS API.
        byte[] mp3 = SynthesizeAsync(text).GetAwaiter().GetResult();
        if (mp3.Length > 0)
            _audio.PlayMp3Bytes(mp3);
    }

    // ── Pre-generate static WAVs ──────────────────────────────────────────────
    private async Task PreGenerateStaticsAsync()
    {
        foreach (var (key, text) in StaticPhrases)
        {
            string filePath = Path.Combine(_cacheDir, $"static_{key}_{SanitizeVoice(Voice)}.mp3");

            if (!File.Exists(filePath))
            {
                try
                {
                    LogTts($"Pre-gen: {key}");
                    byte[] mp3 = await SynthesizeAsync(text);
                    if (mp3.Length > 0)
                        await File.WriteAllBytesAsync(filePath, mp3);
                }
                catch (Exception ex)
                {
                    LogTts($"Pre-gen failed for '{key}': {ex.Message}");
                    continue;
                }
            }

            lock (_staticFiles) { _staticFiles[key] = filePath; }
            LogTts($"Pre-gen ready: {key} → {filePath}");
        }

        LogTts("All static phrases ready.");
    }

    // ── Edge TTS WebSocket synthesis ──────────────────────────────────────────
    public async Task<byte[]> SynthesizeAsync(string text, CancellationToken ct = default)
    {
        string connectionId = Guid.NewGuid().ToString("N").ToUpper();
        string secMsGec    = GenerateSecMsGec();
        string muid        = GenerateMuid();
        string url = $"{WssUrl}?TrustedClientToken={TrustedClientToken}" +
                     $"&ConnectionId={connectionId}" +
                     $"&Sec-MS-GEC={secMsGec}" +
                     $"&Sec-MS-GEC-Version={SecMsGecVer}";

        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Pragma", "no-cache");
        ws.Options.SetRequestHeader("Cache-Control", "no-cache");
        ws.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
        ws.Options.SetRequestHeader("User-Agent", UserAgent);
        ws.Options.SetRequestHeader("Accept-Encoding", "gzip, deflate, br, zstd");
        ws.Options.SetRequestHeader("Accept-Language", "en-US,en;q=0.9");
        ws.Options.SetRequestHeader("Cookie", $"muid={muid};");

        // SocketsHttpHandler bypasses WinHTTP on Windows, which silently strips
        // custom headers like Origin — required by the Edge TTS endpoint.
        using var invoker = new HttpMessageInvoker(new SocketsHttpHandler());
        await ws.ConnectAsync(new Uri(url), invoker, ct);

        // Send speech.config
        string ts        = UtcTimestamp();
        string configMsg =
            $"X-Timestamp:{ts}\r\n" +
            $"Content-Type:application/json; charset=utf-8\r\n" +
            $"Path:speech.config\r\n\r\n" +
            $"{{\"context\":{{\"synthesis\":{{\"audio\":{{" +
            $"\"metadataoptions\":{{\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"}}," +
            $"\"outputFormat\":\"{AudioFormat}\"}}}}}}}}";

        await SendText(ws, configMsg, ct);

        // Send SSML
        string requestId = Guid.NewGuid().ToString("N").ToUpper();
        string escapedText = System.Security.SecurityElement.Escape(text) ?? text;
        string ssml =
            $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>" +
            $"<voice name='{Voice}'><prosody rate='{Rate}'>{escapedText}</prosody></voice></speak>";
        string ssmlMsg =
            $"X-RequestId:{requestId}\r\n" +
            $"Content-Type:application/ssml+xml\r\n" +
            $"X-Timestamp:{ts}\r\n" +
            $"Path:ssml\r\n\r\n{ssml}";

        await SendText(ws, ssmlMsg, ct);

        // Receive audio — accumulate fragments until turn.end or close.
        var audioChunks = new List<byte[]>();
        var recvBuf     = new byte[65536];
        var msgBuf      = new List<byte>();  // assembles fragmented messages

        try
        {
            while (ws.State == WebSocketState.Open)
            {
                ValueWebSocketReceiveResult result;
                try
                {
                    result = await ws.ReceiveAsync(recvBuf.AsMemory(), ct);
                }
                catch (WebSocketException)
                {
                    // Server closed abruptly after turn.end — normal Edge TTS behaviour.
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                // Accumulate fragment bytes
                msgBuf.AddRange(recvBuf[..result.Count]);

                if (!result.EndOfMessage)
                    continue;   // wait for the rest of this message

                byte[] msg = msgBuf.ToArray();
                msgBuf.Clear();

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    if (Encoding.UTF8.GetString(msg).Contains("Path:turn.end"))
                        break;
                }
                else if (result.MessageType == WebSocketMessageType.Binary && msg.Length > 2)
                {
                    // First 2 bytes = big-endian uint16 header length
                    int headerLen  = (msg[0] << 8) | msg[1];
                    int audioStart = 2 + headerLen;
                    if (audioStart < msg.Length)
                    {
                        byte[] chunk = msg[audioStart..];
                        audioChunks.Add(chunk);
                    }
                }
            }
        }
        finally
        {
            // Best-effort close — server may have already shut down its side.
            try
            {
                if (ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
            catch { /* ignore — audio is already collected */ }
        }

        return Combine(audioChunks);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static async Task SendText(ClientWebSocket ws, string msg, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(msg);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    private static string UtcTimestamp()
        => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    private static string SanitizeVoice(string voice)
        => voice.Replace("-", "_").Replace(" ", "_");

    private static byte[] Combine(List<byte[]> chunks)
    {
        int total = chunks.Sum(c => c.Length);
        byte[] result = new byte[total];
        int offset = 0;
        foreach (var c in chunks)
        {
            Buffer.BlockCopy(c, 0, result, offset, c.Length);
            offset += c.Length;
        }
        return result;
    }

    /// <summary>
    /// Generates the Sec-MS-GEC token: SHA256( roundedWindowsTicks + token ).
    /// Algorithm from rany2/edge-tts drm.py.
    /// </summary>
    private static string GenerateSecMsGec()
    {
        const long WIN_EPOCH = 11644473600L; // seconds between 1601-01-01 and 1970-01-01
        double unixSecs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        double ticks = unixSecs + WIN_EPOCH;
        ticks -= ticks % 300;           // round down to nearest 5 minutes
        ticks *= 10_000_000.0;          // convert to 100-nanosecond intervals
        string strToHash = $"{ticks:F0}{TrustedClientToken}";
        byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(strToHash));
        return Convert.ToHexString(hash); // uppercase by default in .NET 5+
    }

    private static string GenerateMuid()
    {
        // Random 16-byte hex (32 chars), uppercase — matches Python secrets.token_hex(16).upper()
        byte[] buf = new byte[16];
        Random.Shared.NextBytes(buf);
        return Convert.ToHexString(buf);
    }

    private void LogTts(string msg)
    {
        string entry = $"{DateTime.Now:HH:mm:ss}  {msg}";
        lock (_log)
        {
            _log.Add(entry);
            if (_log.Count > LogCap) _log.RemoveAt(0);
        }
        LogUpdated?.Invoke(this, EventArgs.Empty);
    }

    // ── IDisposable ───────────────────────────────────────────────────────────
    public void Dispose()
    {
        _running = false;
        _queue.CompleteAdding();
    }
}
