using System.Runtime.InteropServices;

namespace LmuRaceEngineerCS.Shared;

// All structs mirror the Python ctypes definitions with _pack_ = 4.
// bool fields use byte (1 byte, matching c_bool).
// char arrays use fixed byte[N] (matching c_char).
// Pack=4 aligns each field to min(4, sizeof(field)).

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct LmuVect3
{
    public double x, y, z;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public unsafe struct LmuWheel
{
    public double mSuspensionDeflection;
    public double mRideHeight;
    public double mSuspForce;
    public double mBrakeTemp;
    public double mBrakePressure;
    public double mRotation;
    public double mLateralPatchVel;
    public double mLongitudinalPatchVel;
    public double mLateralGroundVel;
    public double mLongitudinalGroundVel;
    public double mCamber;
    public double mLateralForce;
    public double mLongitudinalForce;
    public double mTireLoad;
    public double mGripFract;
    public double mPressure;
    public fixed double mTemperature[3];
    public double mWear;
    public fixed byte mTerrainName[16];
    public byte mSurfaceType;
    public byte mFlat;       // c_bool → byte
    public byte mDetached;   // c_bool → byte
    public byte mStaticUndeflectedRadius;
    public double mVerticalTireDeflection;
    public double mWheelYLocation;
    public double mToe;
    public double mTireCarcassTemperature;
    public fixed double mTireInnerLayerTemperature[3];
    public float mOptimalTemp;
    public byte mCompoundIndex;
    public byte mCompoundType;
    public fixed byte mExpansion[18];
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public unsafe struct LmuVehicleTelemetry
{
    public int    mID;
    public double mDeltaTime;
    public double mElapsedTime;
    public int    mLapNumber;
    public double mLapStartET;
    public fixed byte mVehicleName[64];
    public fixed byte mTrackName[64];
    public LmuVect3 mPos;
    public LmuVect3 mLocalVel;
    public LmuVect3 mLocalAccel;
    // mOri: LMUVect3*3 — store as 3 separate fields
    public LmuVect3 mOri0, mOri1, mOri2;
    public LmuVect3 mLocalRot;
    public LmuVect3 mLocalRotAccel;
    public int    mGear;
    public double mEngineRPM;
    public double mEngineWaterTemp;
    public double mEngineOilTemp;
    public double mClutchRPM;
    public double mUnfilteredThrottle;
    public double mUnfilteredBrake;
    public double mUnfilteredSteering;
    public double mUnfilteredClutch;
    public double mFilteredThrottle;
    public double mFilteredBrake;
    public double mFilteredSteering;
    public double mFilteredClutch;
    public double mSteeringShaftTorque;
    public double mFront3rdDeflection;
    public double mRear3rdDeflection;
    public double mFrontWingHeight;
    public double mFrontRideHeight;
    public double mRearRideHeight;
    public double mDrag;
    public double mFrontDownforce;
    public double mRearDownforce;
    public double mFuel;
    public double mEngineMaxRPM;
    public byte   mScheduledStops;
    public byte   mOverheating;   // c_bool
    public byte   mDetached;      // c_bool
    public byte   mHeadlights;    // c_bool
    public fixed byte mDentSeverity[8];
    public double mLastImpactET;
    public double mLastImpactMagnitude;
    public LmuVect3 mLastImpactPos;
    public double mEngineTorque;
    public int    mCurrentSector;
    public byte   mSpeedLimiter;
    public byte   mMaxGears;
    public byte   mFrontTireCompoundIndex;
    public byte   mRearTireCompoundIndex;
    public double mFuelCapacity;
    public byte   mFrontFlapActivated;
    public byte   mRearFlapActivated;
    public byte   mRearFlapLegalStatus;
    public byte   mIgnitionStarter;
    public fixed byte mFrontTireCompoundName[18];
    public fixed byte mRearTireCompoundName[18];
    public byte   mSpeedLimiterAvailable;
    public byte   mAntiStallActivated;
    public fixed byte mUnused[2];
    public float  mVisualSteeringWheelRange;
    public double mRearBrakeBias;
    public double mTurboBoostPressure;
    public fixed float mPhysicsToGraphicsOffset[3];
    public float  mPhysicalSteeringWheelRange;
    public double mDeltaBest;
    public double mBatteryChargeFraction;
    public double mElectricBoostMotorTorque;
    public double mElectricBoostMotorRPM;
    public double mElectricBoostMotorTemperature;
    public double mElectricBoostWaterTemperature;
    public byte   mElectricBoostMotorState;
    public byte   mLapInvalidated;   // c_bool
    public byte   mABSActive;        // c_bool
    public byte   mTCActive;         // c_bool
    public byte   mSpeedLimiterActive; // c_bool
    public byte   mWiperState;
    public byte   mTC;
    public byte   mTCMax;
    public byte   mTCSlip;
    public byte   mTCSlipMax;
    public byte   mTCCut;
    public byte   mTCCutMax;
    public byte   mABS;
    public byte   mABSMax;
    public byte   mMotorMap;
    public byte   mMotorMapMax;
    public byte   mMigration;
    public byte   mMigrationMax;
    public byte   mFrontAntiSway;
    public byte   mFrontAntiSwayMax;
    public byte   mRearAntiSway;
    public byte   mRearAntiSwayMax;
    public byte   mLiftAndCoastProgress;
    public byte   mTrackLimitsSteps;
    public float  mRegen;
    public float  mStateOfCharge;
    public float  mVirtualEnergy;
    public float  mTimeGapCarAhead;
    public float  mTimeGapCarBehind;
    public float  mTimeGapPlaceAhead;
    public float  mTimeGapPlaceBehind;
    public fixed byte mVehicleModel[30];
    public byte   mVehicleClass;
    public byte   mVehicleChampionship;
    public fixed byte mExpansion[20];
    // mWheels: LMUWheel*4
    public LmuWheel mWheel0, mWheel1, mWheel2, mWheel3;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public unsafe struct LmuVehicleScoring
{
    public int    mID;
    public fixed byte mDriverName[32];
    public fixed byte mVehicleName[64];
    public short  mTotalLaps;
    public sbyte  mSector;
    public sbyte  mFinishStatus;
    public double mLapDist;
    public double mPathLateral;
    public double mTrackEdge;
    public double mBestSector1;
    public double mBestSector2;
    public double mBestLapTime;
    public double mLastSector1;
    public double mLastSector2;
    public double mLastLapTime;
    public double mCurSector1;
    public double mCurSector2;
    public short  mNumPitstops;
    public short  mNumPenalties;
    public byte   mIsPlayer;   // c_bool
    public sbyte  mControl;
    public byte   mInPits;     // c_bool
    public byte   mPlace;
    public fixed byte mVehicleClass[32];
    public double mTimeBehindNext;
    public int    mLapsBehindNext;
    public double mTimeBehindLeader;
    public int    mLapsBehindLeader;
    public double mLapStartET;
    public LmuVect3 mPos;
    public LmuVect3 mLocalVel;
    public LmuVect3 mLocalAccel;
    public LmuVect3 mOri0, mOri1, mOri2;
    public LmuVect3 mLocalRot;
    public LmuVect3 mLocalRotAccel;
    public byte   mHeadlights;
    public byte   mPitState;
    public byte   mServerScored;
    public byte   mIndividualPhase;
    public int    mQualification;
    public double mTimeIntoLap;
    public double mEstimatedLapTime;
    public fixed byte mPitGroup[24];
    public byte   mFlag;
    public byte   mUnderYellow;   // c_bool
    public byte   mCountLapFlag;
    public byte   mInGarageStall; // c_bool
    public fixed byte mUpgradePack[16];
    public float  mPitLapDist;
    public float  mBestLapSector1;
    public float  mBestLapSector2;
    public ulong  mSteamID;
    public fixed byte mVehFilename[32];
    public short  mAttackMode;
    public byte   mFuelFraction;
    public byte   mDRSState;   // c_bool
    public fixed byte mExpansion[4];
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public unsafe struct LmuScoringInfo
{
    public fixed byte mTrackName[64];
    public int    mSession;
    public double mCurrentET;
    public double mEndET;
    public int    mMaxLaps;
    public double mLapDist;
    public fixed byte mResultsStreamPointer[8];
    public int    mNumVehicles;
    public byte   mGamePhase;
    public sbyte  mYellowFlagState;
    public fixed byte mSectorFlag[3];
    public byte   mStartLight;
    public byte   mNumRedLights;
    public byte   mInRealtime;   // c_bool
    public fixed byte mPlayerName[32];
    public fixed byte mPlrFileName[64];
    public double mDarkCloud;
    public double mRaining;
    public double mAmbientTemp;
    public double mTrackTemp;
    public LmuVect3 mWind;
    public double mMinPathWetness;
    public double mMaxPathWetness;
    public byte   mGameMode;
    public byte   mIsPasswordProtected; // c_bool
    public ushort mServerPort;
    public uint   mServerPublicIP;
    public int    mMaxPlayers;
    public fixed byte mServerName[32];
    public float  mStartET;
    public double mAvgPathWetness;
    public float  mSessionTimeRemaining;
    public float  mTimeOfDay;
    public byte   mIsFixedSetup;     // c_bool
    public byte   mTrackGripLevel;
    public byte   mCloudCoverage;
    public byte   mTrackLimitsStepsPerPenalty;
    public byte   mTrackLimitsStepsPerPoint;
    public fixed byte mExpansion[187];
    public fixed byte mVehiclePointer[8];
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public unsafe struct LmuApplicationState
{
    public ulong  mAppWindow;
    public uint   mWidth;
    public uint   mHeight;
    public uint   mRefreshRate;
    public uint   mWindowed;
    public byte   mOptionsLocation;
    public fixed byte mOptionsPage[31];
    public fixed byte mExpansion[204];
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct LmuEvent
{
    public uint SME_ENTER, SME_EXIT, SME_STARTUP, SME_SHUTDOWN;
    public uint SME_LOAD, SME_UNLOAD, SME_START_SESSION, SME_END_SESSION;
    public uint SME_ENTER_REALTIME, SME_EXIT_REALTIME;
    public uint SME_UPDATE_SCORING, SME_UPDATE_TELEMETRY;
    public uint SME_INIT_APPLICATION, SME_UNINIT_APPLICATION;
    public uint SME_SET_ENVIRONMENT, SME_FFB;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct LmuGeneric
{
    public LmuEvent events;
    public int    gameVersion;
    public float  FFBTorque;
    public LmuApplicationState appInfo;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public unsafe struct LmuPathData
{
    public fixed byte userData[260];
    public fixed byte customVariables[260];
    public fixed byte stewardResults[260];
    public fixed byte playerProfile[260];
    public fixed byte pluginsFolder[260];
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public unsafe struct LmuScoringData
{
    public LmuScoringInfo scoringInfo;
    public fixed byte scoringStreamSize[12];
    // vehScoringInfo: LmuVehicleScoring * 104
    // We embed an inline array via a fixed-size approach using an unsafe trick.
    // Actual layout: 104 consecutive LmuVehicleScoring structs after scoringStreamSize.
    // We'll read them individually via the MMapReader accessor.
    public fixed byte _vehScoringInfoRaw[1]; // placeholder; accessed via pointer arithmetic
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public unsafe struct LmuTelemetryData
{
    public byte   activeVehicles;
    public byte   playerVehicleIdx;
    public byte   playerHasVehicle;  // c_bool
    public byte   _pad;
    // telemInfo: LmuVehicleTelemetry * 104
    public fixed byte _telemInfoRaw[1]; // placeholder; accessed via pointer arithmetic
}
