# Race Engineer — Logic & Parameter Audit

Scope (as requested): **strategy engine → telemetry parsing → settings defaults → profile learner**. UI excluded.
Convention used everywhere below: **tread health / "remaining"** = 1.0 (new) counting down; **wear** = 0 (new)
counting up. LMU's `mWear` and the CSV `*_wear_%` columns are **remaining**, confirmed by your real
`laps/Circuit_de_Barcelona_…Race….csv` (≈98.6 fresh, decreasing).

Legend: **[FIXED]** applied + covered by tests · **[REPORT]** left for you to apply/edit.

---

## 0. Headline — tyre-wear polarity  **[FIXED]**

The pure models (`TireModel`, `PaceModel`) are written correctly for **accumulating wear**
(`remaining = 1 − wearFraction`, cliff at high wear). The bug was that they were **fed remaining
tread directly**, so a *new* tyre (~0.98 remaining) looked ~98 % worn → false cliff, garbage pace
penalty, nonsensical pit timing. Fixed at the two consumption boundaries:

- **`Strategy/StrategyEngine.cs` → `BuildInput`** — now converts to accumulating wear:
  `worstWearAccum = 1 − TireWear.Min()` (worst tyre = least tread), and `wearRate = worstWearAccum / stintLaps`.
  `WorstTireWear` and `MaxWearRate` fed to `RaceInput` are now correct. Snapshot fields are **unchanged**,
  so the tyre-**health** displays (which correctly show remaining tread) are untouched.
- **`Services/ProfileLearner.cs` → `ParseCsv`** — CSV columns converted to worst accumulating wear
  `1 − min(remaining)/100`. Downstream stint-splitting (pit = wear drop) and onset detection (walk low→high)
  are now correct with no other change. `CsvRow.MaxWear` renamed `WorstWear`.

**Validation:** `LmuRaceEngineerCS.Tests` (25 tests, all green) pins the model behaviour, incl. an explicit
polarity guard; and the C# learner, fed regenerated real-polarity fixtures, re-learned the exact designed
onsets (Barcelona 0.48/3 stints, Le Mans 0.70/5).

---

## 1. Strategy engine

- **[FIXED]** wear polarity — see §0. This was the only correctness bug in the engine.
- **[FIXED]** `FuelModel.LapTimePenaltyFromFuel` — comment corrected, and the cost raised to
  `0.01` s/lap per 1 % above the 50 % baseline, i.e. **~0.50 s/lap at a full tank**. (`Strategy/FuelModel.cs`)
- **[FIXED]** `OpponentModel` undercut/overcut — `olderTires` renamed `fresherTires`; both checks are now
  side-agnostic (absolute gap) and the engine evaluates them over the **two cars ahead and two behind**
  (`OpponentModel.NearbyWindow`), so a classic undercut threat from the car *behind* is now caught.
  `ScanOpponents` keeps a balanced set (nearest few each side). (`Strategy/OpponentModel.cs`, `StrategyEngine.cs`)
- **[OK]** Scoring weights (`0.40 finish / 0.25 pos / 0.15 fuel / 0.10 traffic`, minus `0.10 cliff`),
  fuel-out hard penalty (−10), uncertainty `√lapsToSim·0.3`, candidate generation — all internally
  consistent, no correctness issue.

## 2. Telemetry parsing

- **[FIXED]** `Models/RaceState.cs` (`OnNewLap`) — delta corrected to `LapStartWear[i] − currentWear[i]`
  (per-lap tread loss, positive), so `WearRate[i]` now populates instead of staying 0 forever.
- **[FIXED]** `Services/TelemetryService.cs` — `TireLaps` now computed against the **worst** corner
  (`ww = min(mWear)`) down to this driver's personal pace-cliff (`RaceInput.PersonalCliffThreshold`):
  `tl = max(0, (ww − cliffRemaining) / wr)`. `WorstWear` now truly holds the least-remaining corner.
  Clarification: `TireLaps` is a **display-only** readout — laps until the worst tyre reaches the pace
  cliff (not until bald); the strategy engine still recomputes its own wear rate for pit decisions, so this
  figure may lag the engine slightly and is intentionally not decision-critical.
- **[OK]** VE/fuel handling (`Ve = mVirtualEnergy·100`, `AvgFuel` fractions), gaps, temps, sector splits —
  no polarity or unit issues found.

## 3. Settings defaults

All sane; nothing to change:

| Setting | Default | Note |
|---|---|---|
| `TireWarnWear` / `TireCritWear` | 40 / 20 | **% remaining life** (health), used correctly as health thresholds |
| `TireColdTemp`/`OptLow`/`OptHigh`/`HotTemp` | 65 / 70 / 90 / 95 °C | reasonable slick window |
| `BrakeWarnTemp` / `BrakeCritTemp` | 600 / 850 °C | reasonable |
| `PitLossSeconds` | 25 | reasonable; track-dependent — user-editable |
| `FuelSafetyLaps` | 1.0 | conservative buffer |
| `FuelLapHistory` | 5 | matches rolling-average use |
| `StrategyIntervalMs` | 12 000 | matches engine cadence comment |

Only nit: the labels formerly said "Wear"; the UI now reads "Health" and the values mean **% remaining**.

## 4. Profile learner

- **[FIXED]** CSV polarity — see §0.
- **[FIXED]** onset-bucket resolution (via suggestion **(a)**, grip/performance). `DetectOnset` no longer
  snaps to a physical-tread bucket edge: it bins wear only to smooth noise, then reads the onset off the
  **lap-time-delta (grip) curve** — finding where mean delta crosses `0.30 s` and interpolating the wear at
  the crossing, and extrapolating the last-two-sample trend when a narrow real stint never reaches the
  threshold. This lets profiles personalise from realistic 0.05–0.20 wear bands instead of sitting on the
  generic 0.60. Verified on the synthetic archetypes (designed 0.40/0.70 → learned ≈0.44/0.74).
  (`Services/ProfileLearner.cs`; Python mirror in `test_fixtures/make_test_profile.py` kept in sync.)
- **[OK]** Bayesian blend `weight = min(1, stints/5)`, clamp `[0.35, 0.85]`, `MinStintLaps = 4`,
  `WearDropPitReset = 0.05`, `OnsetThresholdS = 0.30` — consistent.

---

## Fixtures / data touched
- Regenerated `bin/…/laps/*_Race_*.csv` in correct **remaining-tread** polarity (generator
  `test_fixtures/make_test_profile.py` updated to emit tread remaining).
- Deleted stale `driver_profile.json` so it re-learns; confirmed correct re-learn on launch.
- Old wrong-polarity synthetic CSVs removed. The `…_Test_…` demo was already correct polarity (kept).

## Not verified against live game
Fixes are validated by unit tests + reasoning + the regenerated fixtures. They assume live `mWear` is on the
**0–1** scale (your CSVs). If a live session ever shows tyre values on a different scale (you once saw a
150–900 chart), the absolute numbers would need a normalisation pass — flag me a real CSV if so.
