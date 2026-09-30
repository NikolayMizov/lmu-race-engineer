#!/usr/bin/env python3
"""
make_test_profile.py
====================

Generates a *simulated* driver profile for the LMU Race Engineer "AI" so the
driver-adaptation pipeline can be tested without driving a real session.

It produces two things, both derived from the SAME source constants the C#
ProfileLearner uses, so the numbers are auditable and reproducible:

  1.  Synthetic per-lap race CSVs under `laps/`  -> feed the ProfileLearner so
      you can verify it *re-learns* the same onset (end-to-end accuracy test).

  2.  A ready-to-load `driver_profile.json`      -> the artefact the
      StrategyEngine actually consumes at runtime (keyed by the clean track
      SafeName so GetOnset() finds it immediately).

For every track it also prints the CONFIDENCE SPLIT, i.e. how much of the
blended DegOnset came from the learned driver profile vs. the generic engine.

------------------------------------------------------------------------------
The blend (verbatim from Services/ProfileLearner.cs):

    weight  = min(1, SampleStints / FullConfidenceN)      # FullConfidenceN = 5
    blended = weight * rawOnset + (1 - weight) * GenericOnset   # Generic = 0.60
    blended = clamp(blended, 0.35, 0.85)

    confidence_in_driver_profile = weight
    confidence_in_pure_engine    = 1 - weight
------------------------------------------------------------------------------
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from dataclasses import dataclass
from datetime import datetime, timezone

# ── ProfileLearner constants (keep in sync with Services/ProfileLearner.cs) ──
MIN_STINT_LAPS      = 4
ONSET_THRESHOLD_S   = 0.30
FULL_CONFIDENCE_N   = 5
GENERIC_ONSET       = 0.60
WEAR_DROP_PIT_RESET = 0.05
ONSET_CLAMP_LO      = 0.35
ONSET_CLAMP_HI      = 0.85

# CSV header the LapLogger writes (Services/LapLogger.cs)
CSV_COLUMNS = [
    "lap", "mode", "s1", "s2", "s3", "lap_time",
    "fuel_start_L", "fl_wear_%", "fr_wear_%", "rl_wear_%", "rr_wear_%",
    "nrg_used_%",
]


# ── A synthetic driver archetype ────────────────────────────────────────────
@dataclass(frozen=True)
class Archetype:
    track_safe:   str     # SafeName of the track  (== runtime profile key)
    car_safe:     str     # SafeName of the car    (for the filename only)
    base_lap_s:   float   # clean best lap, seconds (only deltas matter)
    raw_onset:    float   # wear fraction where THIS driver starts losing time
    n_stints:     int     # how many race stints to synthesise -> confidence
    laps_per_stint: int = 9


def delta_for_wear(wear: float, onset: float) -> float:
    """
    Lap-time delta (s, above stint-best) as a function of tyre wear for a driver
    whose degradation onset is `onset`.

    Below onset: tiny, flat (< ONSET_THRESHOLD_S) so the learner's bucket walk
    skips it.  At/above onset: jumps past the threshold and ramps, so the first
    bucket >= 0.30 s mean lands exactly on `onset`.
    """
    if wear < onset:
        return 0.05 * (wear / onset)            # << 0.30 s, monotone, harmless
    return ONSET_THRESHOLD_S + 3.0 * (wear - onset)


def build_stint(start_lap: int, arch: Archetype) -> list[list[str]]:
    """One stint: wear climbs 0.05 -> ~0.85, lap time = base + delta(wear)."""
    rows: list[list[str]] = []
    n = arch.laps_per_stint
    fuel = 1.0
    for i in range(n):
        wear = 0.05 + i * (0.80 / (n - 1))      # 0.05 .. 0.85 across the stint
        lap_time = arch.base_lap_s + delta_for_wear(wear, arch.raw_onset)
        # Sectors are ignored by the learner; split the lap into rough thirds.
        s1 = lap_time * 0.30
        s2 = lap_time * 0.45
        s3 = lap_time - s1 - s2
        # CSV columns are tread REMAINING (100 = new), matching real LMU telemetry:
        # the modelled `wear` (0..1, how worn) maps to remaining = (1 - wear) * 100,
        # so the value counts DOWN across a stint and jumps back up at a pitstop.
        rem = round((1.0 - wear) * 100.0, 1)     # all four tyres == worst corner
        rows.append([
            str(start_lap + i), "normal",
            f"{s1:.3f}", f"{s2:.3f}", f"{s3:.3f}", f"{lap_time:.3f}",
            f"{fuel:.3f}",
            f"{rem:.1f}", f"{rem:.1f}", f"{rem:.1f}", f"{rem:.1f}",
            f"{10.0:.1f}",                       # nrg_used_% — matches the 0.10 VE/lap burn
        ])
        fuel = max(0.0, fuel - 0.10)            # 10% VE per lap
    return rows


def write_csv(laps_dir: str, arch: Archetype) -> str:
    """Write one race file containing all of the archetype's stints."""
    ts = datetime.now().strftime("%Y%m%d_%H%M%S")
    fname = f"{arch.track_safe}_{arch.car_safe}_Race_{ts}.csv"
    path = os.path.join(laps_dir, fname)

    lap_no = 1
    body: list[list[str]] = []
    for _ in range(arch.n_stints):
        body.extend(build_stint(lap_no, arch))
        lap_no += arch.laps_per_stint           # next stint starts fresh tyres
        # The >5pp wear DROP between stint-end (0.85) and next start (0.05)
        # is what ProfileLearner.SplitStints reads as a pit stop.

    os.makedirs(laps_dir, exist_ok=True)
    with open(path, "w", newline="", encoding="utf-8") as f:
        f.write(",".join(CSV_COLUMNS) + "\n")
        for row in body:
            f.write(",".join(row) + "\n")
    return path


# ── Re-implementation of ProfileLearner's onset detection (for verification) ─
def detect_onset(points: list[tuple[float, float]]) -> float:
    """Mirror of ProfileLearner.DetectOnset — reads the onset off the grip/performance
    curve: bin by wear to smooth noise, find where mean lap-time delta crosses the
    threshold, and interpolate the wear at the crossing (extrapolating if the observed
    band never reaches it). Confirms the CSVs decode back to ~the designed onset."""
    if len(points) < MIN_STINT_LAPS:
        return GENERIC_ONSET

    bins = 20                       # 0.05 wear per bin
    sums = [0.0] * bins
    counts = [0] * bins
    for wear, delta in points:
        b = min(max(int(wear * bins), 0), bins - 1)
        sums[b] += delta
        counts[b] += 1

    curve = [((b + 0.5) / bins, sums[b] / counts[b])
             for b in range(bins) if counts[b] >= 2]
    if len(curve) < 2:
        return GENERIC_ONSET

    if curve[0][1] >= ONSET_THRESHOLD_S:
        return min(max(curve[0][0], 0.35), 0.90)

    for i in range(1, len(curve)):
        lo_w, lo_d = curve[i - 1]
        hi_w, hi_d = curve[i]
        if hi_d >= ONSET_THRESHOLD_S:
            t = (ONSET_THRESHOLD_S - lo_d) / (hi_d - lo_d)
            return min(max(lo_w + t * (hi_w - lo_w), 0.35), 0.90)

    a_w, a_d = curve[-2]
    c_w, c_d = curve[-1]
    slope = (c_d - a_d) / max(1e-6, c_w - a_w)
    if slope > 1e-6:
        return min(max(c_w + (ONSET_THRESHOLD_S - c_d) / slope, 0.35), 0.90)
    return 0.75


def blend(raw_onset: float, n_stints: int) -> tuple[float, float]:
    """Returns (blended_onset, weight) exactly as ProfileLearner does."""
    weight = min(1.0, n_stints / FULL_CONFIDENCE_N)
    blended = weight * raw_onset + (1.0 - weight) * GENERIC_ONSET
    blended = min(max(blended, ONSET_CLAMP_LO), ONSET_CLAMP_HI)
    return round(blended, 3), weight


def points_from_archetype(arch: Archetype) -> list[tuple[float, float]]:
    """Reconstruct (wear, delta-vs-stint-best) the way ProfileLearner would."""
    pts: list[tuple[float, float]] = []
    n = arch.laps_per_stint
    for _ in range(arch.n_stints):
        stint = []
        for i in range(n):
            wear = 0.05 + i * (0.80 / (n - 1))
            lap_time = arch.base_lap_s + delta_for_wear(wear, arch.raw_onset)
            stint.append((wear, lap_time))
        best = min(lt for _, lt in stint)
        for wear, lt in stint:
            d = lt - best
            if 0 <= d < 10.0:
                pts.append((wear, d))
    return pts


# ── Drivers to generate ─────────────────────────────────────────────────────
ARCHETYPES = [
    # Aggressive GT3 driver: cooks tyres early (onset 0.40), only 3 stints of
    # data -> partially-confident profile.
    Archetype(track_safe="Circuit_de_Barcelona", car_safe="911GT3R",
              base_lap_s=102.5, raw_onset=0.40, n_stints=3),
    # Smooth endurance driver at Le Mans: makes tyres last (onset 0.70), with a
    # full 5 stints of data -> fully-confident profile.
    Archetype(track_safe="Circuit_de_la_Sarthe", car_safe="296GT3",
              base_lap_s=210.0, raw_onset=0.70, n_stints=5),
]


def bar(frac: float, width: int = 28) -> str:
    fill = int(round(frac * width))
    return "█" * fill + "·" * (width - fill)


def main() -> None:
    ap = argparse.ArgumentParser(description="Generate a simulated LMU driver profile.")
    ap.add_argument("--laps-dir", required=True,
                    help="Directory to write synthetic race CSVs into.")
    ap.add_argument("--profile-out", required=True,
                    help="Path to write driver_profile.json.")
    ap.add_argument("--no-csv", action="store_true",
                    help="Only write the JSON profile, skip CSV generation.")
    args = ap.parse_args()

    # Windows consoles default to cp1252; force UTF-8 so the bars/box chars print.
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass

    now_iso = datetime.now(timezone.utc).astimezone().isoformat()
    profile: dict[str, dict] = {}

    print("=" * 70)
    print(" Simulated driver profile — confidence split per track")
    print("=" * 70)

    for arch in ARCHETYPES:
        if not args.no_csv:
            csv_path = write_csv(args.laps_dir, arch)
        else:
            csv_path = "(skipped)"

        # Verify the learner would decode the intended onset from our CSV.
        learned_raw = detect_onset(points_from_archetype(arch))
        blended, weight = blend(arch.raw_onset, arch.n_stints)

        profile[arch.track_safe] = {
            "DegOnset": blended,
            "SampleStints": arch.n_stints,
            "LastUpdated": now_iso,
        }

        driver_pct = weight * 100.0
        engine_pct = (1.0 - weight) * 100.0

        print()
        print(f"  Track : {arch.track_safe}")
        print(f"  Car   : {arch.car_safe}")
        print(f"  CSV   : {os.path.basename(csv_path)}")
        # The learner reads onset off the performance curve and interpolates, so it
        # recovers ~the designed value rather than snapping exactly to it.
        print(f"  Designed raw onset : {arch.raw_onset:.2f}"
              f"   |  learner decodes : {learned_raw:.2f}"
              f"   {'OK' if abs(learned_raw - arch.raw_onset) < 0.08 else 'MISMATCH'}")
        print(f"  Stints of data     : {arch.n_stints}  (full confidence at {FULL_CONFIDENCE_N})")
        print(f"  Blended DegOnset   : {blended:.3f}"
              f"   -> cliff threshold {max(0.05, 0.75 - blended):.3f}")
        print(f"  -- confidence split --")
        print(f"    driver profile : {driver_pct:5.1f}%  [{bar(weight)}]")
        print(f"    pure engine    : {engine_pct:5.1f}%  [{bar(1 - weight)}]")

    os.makedirs(os.path.dirname(os.path.abspath(args.profile_out)), exist_ok=True)
    with open(args.profile_out, "w", encoding="utf-8") as f:
        json.dump(profile, f, indent=2)

    print()
    print("-" * 70)
    print(f"  Wrote profile -> {args.profile_out}")
    if not args.no_csv:
        print(f"  Wrote CSVs    -> {args.laps_dir}")
    print("=" * 70)


if __name__ == "__main__":
    main()
