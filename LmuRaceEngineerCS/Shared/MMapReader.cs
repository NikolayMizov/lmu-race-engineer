using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace LmuRaceEngineerCS.Shared;

/// <summary>
/// Opens the LMU_Data memory-mapped file and provides typed accessors.
/// Mirrors the Python lmu_mmap.MMapControl(access_mode=1).
/// </summary>
public sealed class MMapReader : IDisposable
{
    // LMU may create the MMF in either the session or global namespace
    private static readonly string[] MmfNames = { "LMU_Data", "Global\\LMU_Data" };
    private const int    MaxVehicles      = 104;

    private MemoryMappedFile?             _mmf;
    private MemoryMappedViewAccessor?     _accessor;
    private unsafe byte*                  _basePtr;

    // Cached sizes
    private static readonly int SzVehicleTelemetry = Marshal.SizeOf<LmuVehicleTelemetry>();
    private static readonly int SzVehicleScoring   = Marshal.SizeOf<LmuVehicleScoring>();
    private static readonly int SzScoringInfo      = Marshal.SizeOf<LmuScoringInfo>();
    private static readonly int SzGeneric          = Marshal.SizeOf<LmuGeneric>();
    private static readonly int SzPathData         = Marshal.SizeOf<LmuPathData>();
    private static readonly int SzScoringData;
    private static readonly int SzTelemetryHeader  = 4; // activeVehicles + playerVehicleIdx + playerHasVehicle + pad

    // Byte offsets within the flat LMU_Data buffer
    private readonly int _offGeneric;
    private readonly int _offPaths;
    private readonly int _offScoring;
    private readonly int _offTelemetry;
    private readonly int _offVehScoring;  // within scoring block
    private readonly int _offTelemInfo;   // within telemetry block

    // Total buffer size this build's struct layout expects to read.
    private readonly long _expectedTotalSize;

    static MMapReader()
    {
        // LmuScoringData = LmuScoringInfo + 12 (scoringStreamSize) + 104*SzVehicleScoring + 65536 (scoringStream)
        SzScoringData = SzScoringInfo + 12 + MaxVehicles * Marshal.SizeOf<LmuVehicleScoring>() + 65536;
    }

    public MMapReader()
    {
        _offGeneric    = 0;
        _offPaths      = _offGeneric  + SzGeneric;
        _offScoring    = _offPaths    + SzPathData;
        _offTelemetry  = _offScoring  + SzScoringData;
        _offVehScoring = SzScoringInfo + 12;  // within scoring block
        _offTelemInfo  = SzTelemetryHeader;   // within telemetry block

        // generic + paths + scoring block + telemetry header + 104 telemetry structs
        _expectedTotalSize = (long)_offTelemetry + SzTelemetryHeader
                           + (long)MaxVehicles * SzVehicleTelemetry;
    }

    public bool   IsOpen        { get; private set; }
    public string LastOpenError { get; private set; } = "";

    // ── Layout sanity guard (future-proofing against a shared-memory layout change) ──
    public long   MappedBytes   { get; private set; }   // actual mapped view size
    public long   ExpectedBytes => _expectedTotalSize;
    public int    GameVersion   { get; private set; }
    public bool   LayoutSuspect { get; private set; }
    public string LayoutInfo    { get; private set; } = "";

    public bool TryOpen()
    {
        foreach (string name in MmfNames)
        {
            try
            {
                _mmf      = MemoryMappedFile.OpenExisting(name);
                _accessor = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                unsafe
                {
                    byte* ptr = null;
                    _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
                    _basePtr = ptr;
                }
                MappedBytes   = (long)_accessor.SafeMemoryMappedViewHandle.ByteLength;
                IsOpen        = true;
                LastOpenError = "";
                return true;
            }
            catch (FileNotFoundException) { Dispose(); /* try next name */ }
            catch (UnauthorizedAccessException)
            {
                LastOpenError = "access_denied";
                Dispose();
                return false;
            }
            catch (Exception ex)
            {
                LastOpenError = ex.GetType().Name;
                Dispose();
                return false;
            }
        }
        LastOpenError = "not_found";
        return false;
    }

    // ── Generic (game version check) ─────────────────────────────────────────────
    public unsafe bool GameRunning()
    {
        if (!IsOpen) return false;
        var gen = Read<LmuGeneric>(_basePtr + _offGeneric);
        return gen.gameVersion != 0;
    }

    /// <summary>
    /// Cheap sanity guard against a future LMU patch shifting the shared-memory layout.
    /// The app reads at hardcoded offsets, so a layout change would return garbage silently;
    /// this flags <see cref="LayoutSuspect"/> (+ <see cref="LayoutInfo"/>) so callers can pause
    /// instead of acting on bad data. Call while the game is running.
    /// </summary>
    public unsafe void ValidateLayout()
    {
        if (!IsOpen) { LayoutSuspect = false; LayoutInfo = ""; return; }

        GameVersion = Read<LmuGeneric>(_basePtr + _offGeneric).gameVersion;
        int numVeh  = Read<LmuScoringInfo>(_basePtr + _offScoring).mNumVehicles;

        // Mapped view is page-rounded UP, so "< expected" is a reliable lower-bound failure.
        bool tooSmall = MappedBytes > 0 && MappedBytes < _expectedTotalSize;
        // mNumVehicles lives at a fixed offset; garbage here means the offsets have shifted.
        // 0 is legitimate (menus / cars not loaded), so only out-of-range values are suspect.
        bool badCount = numVeh < 0 || numVeh > MaxVehicles;

        LayoutSuspect = tooSmall || badCount;
        LayoutInfo = $"game v{GameVersion}; mapped {MappedBytes}B / expected {_expectedTotalSize}B; cars {numVeh}"
                   + (tooSmall ? " [BUFFER TOO SMALL]" : badCount ? " [CAR COUNT OUT OF RANGE]" : "");
    }

    // ── Telemetry header ──────────────────────────────────────────────────────────
    public unsafe (byte activeVehicles, byte playerIdx, bool playerHasVehicle) ReadTelemHeader()
    {
        byte* p = _basePtr + _offTelemetry;
        return (p[0], p[1], p[2] != 0);
    }

    public unsafe LmuVehicleTelemetry ReadTelemetry(int vehicleIdx)
    {
        byte* p = _basePtr + _offTelemetry + _offTelemInfo + vehicleIdx * SzVehicleTelemetry;
        return Read<LmuVehicleTelemetry>(p);
    }

    // ── Scoring ───────────────────────────────────────────────────────────────────
    public unsafe LmuScoringInfo ReadScoringInfo()
    {
        return Read<LmuScoringInfo>(_basePtr + _offScoring);
    }

    public unsafe LmuVehicleScoring ReadVehicleScoring(int vehicleIdx)
    {
        byte* p = _basePtr + _offScoring + _offVehScoring + vehicleIdx * SzVehicleScoring;
        return Read<LmuVehicleScoring>(p);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────
    private static unsafe T Read<T>(byte* ptr) where T : unmanaged
        => *(T*)ptr;

    /// <summary>Decode a null-terminated byte array to a UTF-8 string.</summary>
    public static unsafe string DecodeBytes(byte* ptr, int maxLen)
    {
        int len = 0;
        while (len < maxLen && ptr[len] != 0) len++;
        return Encoding.UTF8.GetString(ptr, len);
    }

    public void Dispose()
    {
        unsafe { _basePtr = null; }
        _accessor?.SafeMemoryMappedViewHandle.ReleasePointer();
        _accessor?.Dispose();
        _mmf?.Dispose();
        _mmf      = null;
        _accessor = null;
        IsOpen    = false;
    }
}
