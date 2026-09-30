using System.IO;
using NAudio.Wave;

namespace LmuRaceEngineerCS.Services;

/// <summary>
/// Thread-safe audio playback using NAudio.
/// Plays MP3 data from files or byte arrays.
/// Exposes IsPlaying so the voice capture loop can suppress mic input during TTS.
/// </summary>
public sealed class AudioService : IDisposable
{
    private readonly object _playLock = new();
    private volatile bool   _isPlaying;

    public bool IsPlaying => _isPlaying;
    public float Volume   { get; set; } = 1.0f;

    // ── Play MP3 from a file path ─────────────────────────────────────────────
    public void PlayMp3File(string filePath)
    {
        Task.Run(() =>
        {
            lock (_playLock)
            {
                try
                {
                    _isPlaying = true;
                    using var reader  = new Mp3FileReader(filePath);
                    using var output  = new WaveOutEvent();
                    output.Volume     = Volume;
                    output.Init(reader);
                    output.Play();
                    while (output.PlaybackState == PlaybackState.Playing)
                        Thread.Sleep(50);
                }
                catch { /* swallow — don't crash if file is corrupt */ }
                finally { _isPlaying = false; }
            }
        });
    }

    // ── Play MP3 from byte array ──────────────────────────────────────────────
    public void PlayMp3Bytes(byte[] mp3Data)
    {
        Task.Run(() =>
        {
            lock (_playLock)
            {
                try
                {
                    _isPlaying = true;
                    using var ms      = new MemoryStream(mp3Data);
                    using var reader  = new Mp3FileReader(ms);
                    using var output  = new WaveOutEvent();
                    output.Volume     = Volume;
                    output.Init(reader);
                    output.Play();
                    while (output.PlaybackState == PlaybackState.Playing)
                        Thread.Sleep(50);
                }
                catch { /* swallow */ }
                finally
                {
                    Thread.Sleep(300);   // brief gap between phrases
                    _isPlaying = false;
                }
            }
        });
    }

    public void Dispose() { }
}
