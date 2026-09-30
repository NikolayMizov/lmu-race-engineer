# Simulated driver profile — test fixture

Generates a fake driver profile so you can test how the "AI" (StrategyEngine)
adapts to a driver, without driving a real session.

## What it produces

| Output | Consumed by | Purpose |
| --- | --- | --- |
| `laps/<Track>_<Car>_Race_*.csv` | `ProfileLearner` | Synthetic race stints — lets the learner **re-learn** the profile (end-to-end accuracy test). |
| `driver_profile.json` | `StrategyEngine` (via `ProfileLearner.GetOnset`) | The runtime artefact. Keyed by the track **SafeName** so it loads immediately. |

## Run it

```bash
python make_test_profile.py \
  --laps-dir   "../LmuRaceEngineerCS/bin/Debug/net8.0-windows/laps" \
  --profile-out "../LmuRaceEngineerCS/bin/Debug/net8.0-windows/driver_profile.json"
```

`--profile-out` must land in the app's `AppContext.BaseDirectory` (same folder as
`LmuRaceEngineer.exe`). Add `--no-csv` to write only the JSON.

## The confidence split

Straight from `Services/ProfileLearner.cs`:

```
weight  = min(1, SampleStints / 5)              # FullConfidenceN = 5
blended = weight * rawOnset + (1 - weight) * 0.60   # GenericOnset = 0.60
blended = clamp(blended, 0.35, 0.85)

confidence_in_driver_profile = weight
confidence_in_pure_engine    = 1 - weight
```

Two archetypes ship in the script:

| Track | Driver | rawOnset | Stints | DegOnset | Driver % | Engine % |
| --- | --- | --- | --- | --- | --- | --- |
| Circuit_de_Barcelona | aggressive, early deg | 0.40 | 3 | 0.480 | **60%** | 40% |
| Circuit_de_la_Sarthe | smooth endurance | 0.70 | 5 | 0.700 | **100%** | 0% |

Edit `ARCHETYPES` in the script to dial onset / stint count and see the split move.
Confidence reaches 100% at 5 stints; below that the generic 0.60 model fills the gap.

## Note on the runtime key

`StrategyEngine` looks the profile up by `SafeName(loadedTrackName)`. The JSON keys
above must match whatever LMU reports the track as. If your sim names Barcelona
differently, rename the key in `driver_profile.json` (or in `ARCHETYPES`) to match.
