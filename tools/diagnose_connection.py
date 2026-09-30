#!/usr/bin/env python3
"""
LMU Connection Diagnostic
Run this while Le Mans Ultimate is running to see exactly what
the shared memory is returning.

Usage:  python diagnose_connection.py
"""

import ctypes
import mmap
import struct
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))

MMAP_NAME = "LMU_Data"
SEPARATOR = "-" * 60


def _open_mmap(name: str, size: int) -> mmap.mmap | None:
    try:
        m = mmap.mmap(-1, size, name)
        print(f"[OK]  mmap opened: '{name}'  size={size:,} bytes")
        return m
    except Exception as exc:
        print(f"[FAIL] mmap open failed: '{name}'  → {exc}")
        return None


def _bytes_nonzero(data: bytes) -> bool:
    return any(b != 0 for b in data)


def diagnose():
    print(SEPARATOR)
    print("LMU Connection Diagnostic")
    print(SEPARATOR)

    # ── 1. Import structs ──────────────────────────────────────────────────────
    try:
        from LmuRaceEngineerPY.pyLMUSharedMemory import lmu_data
        print("[OK]  pyLMUSharedMemory imported")
    except Exception as exc:
        print(f"[FAIL] import error: {exc}")
        return

    total_size = ctypes.sizeof(lmu_data.LMUObjectOut)
    generic_size  = ctypes.sizeof(lmu_data.LMUGeneric)
    scoring_size  = ctypes.sizeof(lmu_data.LMUScoringData)
    telemetry_size = ctypes.sizeof(lmu_data.LMUTelemetryData)
    paths_size    = ctypes.sizeof(lmu_data.LMUPathData)

    print(SEPARATOR)
    print("Struct sizes:")
    print(f"  LMUObjectOut   : {total_size:>12,} bytes  ({total_size/1024:.1f} KB)")
    print(f"  LMUGeneric     : {generic_size:>12,} bytes")
    print(f"  LMUPathData    : {paths_size:>12,} bytes")
    print(f"  LMUScoringData : {scoring_size:>12,} bytes")
    print(f"  LMUTelemetryData:{telemetry_size:>12,} bytes")

    # Field offsets
    gv_offset = lmu_data.LMUObjectOut.generic.offset + \
                lmu_data.LMUGeneric.gameVersion.offset
    print(f"\ngameVersion field offset in LMUObjectOut: {gv_offset} bytes")

    # ── 2. Try to open the mmap ────────────────────────────────────────────────
    print(SEPARATOR)
    print("Attempting to open shared memory…")
    mm = _open_mmap(MMAP_NAME, total_size)
    if mm is None:
        print("\n  The game is probably not running, or shared memory is disabled.")
        print("  In LMU options check: Display → Shared Memory → Enabled")
        return

    # ── 3. Check if any bytes are non-zero ─────────────────────────────────────
    print(SEPARATOR)
    raw = mm[:]
    if not _bytes_nonzero(raw):
        print("[WARN] All bytes are zero — game is running but not writing to mmap.")
        print("       This can happen before you load into a session.")
    else:
        nonzero = sum(1 for b in raw if b != 0)
        print(f"[OK]  mmap contains data — {nonzero:,} / {total_size:,} bytes are non-zero")

    # ── 4. Read gameVersion raw bytes ──────────────────────────────────────────
    gv_raw = raw[gv_offset:gv_offset + 4]
    gv_val = struct.unpack_from("<i", gv_raw)[0]
    print(f"\ngameVersion raw bytes : {gv_raw.hex(' ')}")
    print(f"gameVersion (int32 LE): {gv_val}")
    if gv_val == 0:
        print("  → Zero: game not writing yet, or struct layout mismatch")
    else:
        print(f"  → Non-zero: game IS running and writing  ✓")

    # ── 5. Parse full struct ───────────────────────────────────────────────────
    print(SEPARATOR)
    try:
        mm.seek(0)
        obj = lmu_data.LMUObjectOut.from_buffer_copy(mm)
        print("[OK]  LMUObjectOut.from_buffer_copy succeeded")
    except Exception as exc:
        print(f"[FAIL] from_buffer_copy: {exc}")
        mm.close()
        return

    print("\nKey fields:")
    print(f"  generic.gameVersion      : {obj.generic.gameVersion}")
    print(f"  generic.events.SME_UPDATE_SCORING   : {obj.generic.events.SME_UPDATE_SCORING}")
    print(f"  generic.events.SME_UPDATE_TELEMETRY : {obj.generic.events.SME_UPDATE_TELEMETRY}")

    try:
        track = bytes(obj.scoring.scoringInfo.mTrackName).rstrip(b"\x00").decode("utf-8", errors="replace")
        print(f"  scoring.scoringInfo.mTrackName       : '{track}'")
        print(f"  scoring.scoringInfo.mNumVehicles     : {obj.scoring.scoringInfo.mNumVehicles}")
    except Exception as exc:
        print(f"  [scoring read error] {exc}")

    try:
        print(f"  telemetry.activeVehicles             : {obj.telemetry.activeVehicles}")
        print(f"  telemetry.playerHasVehicle           : {obj.telemetry.playerHasVehicle}")
        print(f"  telemetry.playerVehicleIdx           : {obj.telemetry.playerVehicleIdx}")
    except Exception as exc:
        print(f"  [telemetry read error] {exc}")

    # ── 6. Poll for a few seconds to catch live updates ───────────────────────
    print(SEPARATOR)
    print("Polling mmap for 5 seconds to detect live updates…")
    prev_gv = obj.generic.gameVersion
    changed = False
    for i in range(10):
        time.sleep(0.5)
        mm.seek(0)
        try:
            obj2 = lmu_data.LMUObjectOut.from_buffer_copy(mm)
            gv2 = obj2.generic.gameVersion
            if gv2 != prev_gv:
                print(f"  [{i*0.5:.1f}s] gameVersion changed: {prev_gv} → {gv2}")
                prev_gv = gv2
                changed = True
        except Exception:
            pass

    if not changed:
        print(f"  gameVersion stayed constant at: {prev_gv}")

    # ── 7. Summary ────────────────────────────────────────────────────────────
    print(SEPARATOR)
    if obj.generic.gameVersion != 0:
        print("RESULT: Game is connected and writing shared memory  ✓")
        print("        If the GUI still shows 'Waiting', the issue is in")
        print("        how the GUI reads the snapshot (tick() early-exit).")
    elif _bytes_nonzero(raw):
        print("RESULT: mmap has data but gameVersion=0.")
        print("        Likely a struct layout mismatch — gameVersion field is")
        print(f"        at offset {gv_offset} but the game writes it elsewhere.")
        print("        Check the raw bytes near offset 0 and compare to expected layout.")
        # Print first 64 bytes as hex + ints for manual inspection
        print(f"\n  First 64 bytes (hex): {raw[:64].hex(' ')}")
        print("  As int32 chunks:")
        for i in range(0, 64, 4):
            val = struct.unpack_from("<i", raw, i)[0]
            fval = struct.unpack_from("<f", raw, i)[0]
            print(f"    offset {i:3d}: int32={val:>12}  float32={fval:.6g}")
    else:
        print("RESULT: mmap is all zeros.")
        print("        Either the game is not running, you are in the main menu,")
        print("        or Shared Memory output is disabled in LMU settings.")
        print("\n  → In LMU: Options → Display → Shared Memory  (set to Enabled)")

    mm.close()
    print(SEPARATOR)


if __name__ == "__main__":
    diagnose()
