using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using LmuRaceEngineerCS.Shared;

namespace LmuSimulator;

/// <summary>
/// Creates the LMU_Data named MMF and writes synthetic race data that the
/// main app reads via MMapReader.  Buffer layout mirrors MMapReader exactly.
/// </summary>
public sealed class SimWriter : IDisposable
{
    private const string MmfName     = "LMU_Data";
    private const int    MaxVehicles = 104;
    private const int    NumVeh      = 3;        // player + 2 opponents
    private const int    MmfSize     = 8 * 1024 * 1024; // 8 MB

    // Struct sizes — computed once, match MMapReader's static fields
    private static readonly int SzGeneric          = Marshal.SizeOf<LmuGeneric>();
    private static readonly int SzPathData         = Marshal.SizeOf<LmuPathData>();
    private static readonly int SzScoringInfo      = Marshal.SizeOf<LmuScoringInfo>();
    private static readonly int SzVehicleScoring   = Marshal.SizeOf<LmuVehicleScoring>();
    private static readonly int SzVehicleTelemetry = Marshal.SizeOf<LmuVehicleTelemetry>();
    private static readonly int SzScoringData;

    // Field offsets within LmuVehicleScoring (Pack=4 layout)
    // mID(4) | mDriverName[32](+4) | mVehicleName[64](+36) | mTotalLaps(short,+100) |
    // mSector(+102) | mFinishStatus(+103) | 11×double(+104..+191) |
    // mNumPitstops(short,+192) | mNumPenalties(+194) | mIsPlayer(+196) |
    // mControl(+197) | mInPits(+198) | mPlace(+199) | mVehicleClass[32](+200)
    private static readonly int OffDriverNameInScoring   = 4;
    private static readonly int OffVehicleClassInScoring;

    // Buffer offsets (match MMapReader constructor + static ctor)
    private readonly int _offGeneric;
    private readonly int _offScoring;
    private readonly int _offTelemetry;
    private readonly int _offVehScoring;   // within scoring block
    private readonly int _offTelemInfo;    // within telemetry block

    private MemoryMappedFile?         _mmf;
    private MemoryMappedViewAccessor? _accessor;
    private unsafe byte*              _ptr;

    static SimWriter()
    {
        SzScoringData = SzScoringInfo + 12 + MaxVehicles * Marshal.SizeOf<LmuVehicleScoring>() + 65536;

        // Prefer Marshal.OffsetOf; fall back to manual layout calculation
        try { OffVehicleClassInScoring = (int)Marshal.OffsetOf<LmuVehicleScoring>("mVehicleClass"); }
        catch { OffVehicleClassInScoring = 200; }
    }

    public SimWriter()
    {
        int offPaths   = SzGeneric;
        _offGeneric    = 0;
        _offScoring    = offPaths + SzPathData;
        _offTelemetry  = _offScoring + SzScoringData;
        _offVehScoring = SzScoringInfo + 12;
        _offTelemInfo  = 4;
    }

    public bool IsOpen { get; private set; }

    public bool TryCreate()
    {
        try
        {
            _mmf      = MemoryMappedFile.CreateOrOpen(MmfName, MmfSize, MemoryMappedFileAccess.ReadWrite);
            _accessor = _mmf.CreateViewAccessor(0, MmfSize, MemoryMappedFileAccess.ReadWrite);
            unsafe
            {
                byte* p = null;
                _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
                _ptr = p;
            }
            IsOpen = true;
            return true;
        }
        catch
        {
            Dispose();
            return false;
        }
    }

    public unsafe void Write(SimState s)
    {
        if (_ptr == null) return;

        // ── 1. Generic — gameVersion != 0 signals "game running" ─────────────
        var gen = new LmuGeneric { gameVersion = 1 };
        *(LmuGeneric*)(_ptr + _offGeneric) = gen;

        // ── 2. Scoring info ───────────────────────────────────────────────────
        double endET = s.MaxLaps * s.LapTimeSecs;
        var ses = new LmuScoringInfo
        {
            mSession              = 10,  // Race
            mGamePhase            = 5,   // green flag
            mInRealtime           = 1,
            mNumVehicles          = NumVeh,
            mMaxLaps              = s.MaxLaps,
            mCurrentET            = s.CurrentET,
            mEndET                = endET,
            mAmbientTemp          = s.AmbientTemp,
            mTrackTemp            = s.TrackTemp,
            mRaining              = s.Rain,
            mAvgPathWetness       = s.AvgPathWetness,
            mDarkCloud            = s.DarkCloud,
            mTrackGripLevel       = s.TrackGrip,
            mSessionTimeRemaining = (float)Math.Max(0, endET - s.CurrentET),
            mTrackLimitsStepsPerPenalty = 4,
        };
        *(LmuScoringInfo*)(_ptr + _offScoring) = ses;
        // mTrackName is the first field of LmuScoringInfo → offset 0 within the block
        WriteStr(_ptr + _offScoring, 64, "Le Mans");

        // ── 3. Vehicle scoring ────────────────────────────────────────────────
        // Player (index 0): place 2 in class
        var sc0 = new LmuVehicleScoring
        {
            mIsPlayer         = 1,
            mPlace            = 2,
            mTotalLaps        = (short)s.Lap,
            mLastLapTime      = s.LastLapTime,
            mBestLapTime      = s.BestLapTime,
            mNumPitstops      = (short)s.PitStops,
            mEstimatedLapTime = s.LapTimeSecs,
            mTimeBehindLeader = 5.0,
            mTimeBehindNext   = 5.0,
            mInPits           = (byte)(s.InPits ? 1 : 0),
        };
        PutVehScoring(0, ref sc0, "LMP2", "Player");

        // Opponent 1: place 1 (ahead of player)
        var sc1 = new LmuVehicleScoring
        {
            mIsPlayer = 0, mPlace = 1,
            mTotalLaps        = (short)s.Lap,
            mLastLapTime      = s.LapTimeSecs - 0.3,
            mBestLapTime      = s.BestLapTime - 0.3,
            mEstimatedLapTime = s.LapTimeSecs - 0.3,
            mTimeBehindLeader = 0.0,
        };
        PutVehScoring(1, ref sc1, "LMP2", "Opp1");

        // Opponent 2: place 3 (behind player)
        var sc2 = new LmuVehicleScoring
        {
            mIsPlayer = 0, mPlace = 3,
            mTotalLaps        = (short)s.Lap,
            mLastLapTime      = s.LapTimeSecs + 0.5,
            mBestLapTime      = s.BestLapTime + 0.5,
            mEstimatedLapTime = s.LapTimeSecs + 0.5,
            mTimeBehindLeader = 10.0,
            mTimeBehindNext   = 5.0,
        };
        PutVehScoring(2, ref sc2, "LMP2", "Opp2");

        // ── 4. Telemetry header [activeVehicles, playerIdx, hasVehicle, pad] ──
        byte* hdr  = _ptr + _offTelemetry;
        hdr[0] = NumVeh;  // activeVehicles
        hdr[1] = 0;       // playerVehicleIdx
        hdr[2] = 1;       // playerHasVehicle = true
        hdr[3] = 0;

        // ── 5. Player telemetry ────────────────────────────────────────────────
        // Tire temps: game stores °C + 275.15
        double tireTemp = 82.0 + 275.15;
        double brakeFl  = s.BrakeTemp;
        double brakeRr  = s.BrakeTemp * 0.85;
        // mWear is tread REMAINING (1.0 = new), matching the real game and the app's
        // post-audit convention. SimState.TireWear is accumulated (0 = new), so invert.
        float  wear     = Math.Clamp(1f - s.TireWear, 0f, 1f);

        var telem = new LmuVehicleTelemetry
        {
            mID                 = 0,
            mLapNumber          = s.Lap,
            mElapsedTime        = s.CurrentET,
            mVirtualEnergy      = (float)s.VirtualEnergy,
            mStateOfCharge      = s.StateOfCharge,
            mDeltaBest          = s.DeltaBest,
            mTimeGapPlaceAhead  = s.GapFront,
            mTimeGapPlaceBehind = s.GapBehind,
            mFilteredThrottle   = 0.5,
            mRearBrakeBias      = 0.55,
        };
        SetWheel(ref telem.mWheel0, wear,         tireTemp,     brakeFl);
        SetWheel(ref telem.mWheel1, wear * 0.97f, tireTemp + 2, brakeFl * 0.95);
        SetWheel(ref telem.mWheel2, wear * 0.95f, tireTemp - 3, brakeRr);
        SetWheel(ref telem.mWheel3, wear * 0.93f, tireTemp - 4, brakeRr * 0.95);

        *(LmuVehicleTelemetry*)(_ptr + _offTelemetry + _offTelemInfo) = telem;

        // Minimal opponent telemetry so activeVehicles count is consistent
        var opp = new LmuVehicleTelemetry { mID = 1, mLapNumber = s.Lap };
        *(LmuVehicleTelemetry*)(_ptr + _offTelemetry + _offTelemInfo + SzVehicleTelemetry) = opp;
        opp.mID = 2;
        *(LmuVehicleTelemetry*)(_ptr + _offTelemetry + _offTelemInfo + 2 * SzVehicleTelemetry) = opp;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private unsafe void PutVehScoring(int idx, ref LmuVehicleScoring sc,
                                      string cls, string driverName)
    {
        byte* p = _ptr + _offScoring + _offVehScoring + idx * SzVehicleScoring;
        *(LmuVehicleScoring*)p = sc;
        WriteStr(p + OffDriverNameInScoring,   32, driverName);
        WriteStr(p + OffVehicleClassInScoring, 32, cls);
    }

    private static unsafe void SetWheel(ref LmuWheel w, float wear, double carcassTemp, double brakeTemp)
    {
        w.mWear = wear;
        w.mTireCarcassTemperature = carcassTemp;
        w.mBrakeTemp = brakeTemp;
        w.mCompoundType = 0;
    }

    private static unsafe void WriteStr(byte* dest, int maxLen, string s)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(s);
        int len = Math.Min(bytes.Length, maxLen - 1);
        for (int i = 0; i < len; i++) dest[i] = bytes[i];
        dest[len] = 0;
    }

    public void Dispose()
    {
        // Signal "game not running" before releasing
        unsafe
        {
            if (_ptr != null)
            {
                var gen = new LmuGeneric { gameVersion = 0 };
                *(LmuGeneric*)(_ptr + _offGeneric) = gen;
            }
            _ptr = null;
        }
        _accessor?.SafeMemoryMappedViewHandle.ReleasePointer();
        _accessor?.Dispose();
        _mmf?.Dispose();
        _accessor = null;
        _mmf      = null;
        IsOpen    = false;
    }
}
