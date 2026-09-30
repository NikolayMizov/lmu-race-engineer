# LMU Race Engineer

A Windows desktop app that reads live telemetry from **Le Mans Ultimate**, recommends pit and fuel strategy, and talks to the driver over voice.

Built with C# / .NET 8 and WPF. The strategy engine simulates candidate decisions in the background, scores them, and explains the best one in plain language.

> Unofficial fan project. Not affiliated with or endorsed by Studio 397 or the Le Mans Ultimate team.

## What it does

- **Live telemetry.** Reads the game's `LMU_Data` shared-memory buffer and maps it into typed C# structs.
- **Strategy engine.** Every ~12 seconds it freezes a snapshot of the race, simulates up to 7 candidate actions (for example pit now, pit in N laps, save fuel) and scores them.
- **Voice engineer.** Speech commands, an intent responder and neural text-to-speech. Fixed phrases are pre-rendered to MP3 so common alerts play instantly.
- **Driver profile learning.** Learns from your past lap CSVs when your tyres fall off, and blends that with a generic model.
- **HUD and analysis pages.** Telemetry, engineer, strategy, monitoring, lap times, driver profile, voice and settings pages, plus a pop-out overlay and a first-run tutorial.
- **Simulator.** A second app that writes the same shared-memory format as the game, so you can develop and test without the game running.

## How the strategy engine works

Scoring weights (from `Strategy/StrategyEngine.cs`):

| Term | Weight |
| --- | --- |
| Finish time | 0.40 |
| Race position | 0.25 |
| Fuel safety | 0.15 |
| Tyre cliff risk | 0.10 (penalty) |
| Traffic loss | 0.10 |

Supporting models live in `LmuRaceEngineerCS/Strategy/`: `FuelModel`, `TireModel`, `PaceModel`, `PitLossModel` and `OpponentModel`. The opponent model checks the two cars ahead and the two behind for undercut and overcut threats.

The profile learner (`Services/ProfileLearner.cs`) weights the driver's own data by `min(1, stints / 5)`, so confidence in the personal profile reaches 100% after five stints. Below that it falls back to a generic tyre-wear onset of 0.60.

```
Game shared memory --> MMapReader --> TelemetryService --> StrategySnapshot
                                                               |
Lap CSVs --> ProfileLearner --> driver profile --+             v
                                                 +--> StrategyEngine
                                            (simulate, score, rank)
                                                               |
                   +-------------------+-----------------------+
                   v                   v                       v
             HUD overlay      Strategy / Telemetry       Voice engineer
                                   pages              (speech > intent > TTS)
```

## Quality

- **25 unit tests** (xUnit) cover the fuel, tyre, pace, pit-loss and opponent models.
- [`AUDIT_FINDINGS.md`](AUDIT_FINDINGS.md) is a written logic audit of the strategy engine, telemetry parsing, settings defaults and profile learner. Its main finding was a tyre-wear polarity bug: the game reports tread *remaining*, but the models expect wear *accumulated*, so a new tyre looked almost fully worn. It is fixed, and tests pin the correct behaviour.
- `test_fixtures/` contains a Python generator of synthetic driver profiles and lap CSVs, so the learner can be checked against known answers.

## Repository layout

```
LmuRaceEngineerCS/         main WPF app (Strategy, Services, Models, Views, Shared)
LmuRaceEngineerCS.Tests/   xUnit tests for the strategy models
LmuSimulator/              WPF app that writes fake LMU shared memory
test_fixtures/             synthetic driver profile generator (Python)
tools/diagnose_connection.py   prints what the shared memory currently contains
sample_data/laps/          example lap CSVs
docs/design/               HTML prototype of the UI
AUDIT_FINDINGS.md          logic audit and fixes
```

## Getting started

**Requirements:** Windows 10/11 and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Python 3 is only needed for the test-fixture and diagnostic scripts.

```bash
dotnet build LmuRaceEngineerCS.sln
dotnet test  LmuRaceEngineerCS.Tests
dotnet run --project LmuRaceEngineerCS
```

**Without the game:** start the simulator first, then the app.

```bash
dotnet run --project LmuSimulator
dotnet run --project LmuRaceEngineerCS
```

**With the game:** start Le Mans Ultimate, load a session, then start the app. If nothing shows up, run `python tools/diagnose_connection.py` while the game is running to see what the shared memory returns.

**Single-file build:**

```bash
dotnet publish LmuRaceEngineerCS -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Voice notes

- Speech recognition uses Windows SAPI (`System.Speech`), so it needs a Windows speech recognizer installed.
- Text-to-speech uses Microsoft's Edge read-aloud WebSocket endpoint. It needs no API key, but it is an **unofficial** endpoint that Microsoft can change or restrict. `EdgeTtsService` is isolated so it can be swapped for another provider.

## Tech stack

C# 12 / .NET 8, WPF and XAML, OxyPlot, NAudio, System.Speech, xUnit, Python 3.

## Credits

- The shared-memory struct layouts mirror the community [pyLMUSharedMemory](https://github.com/TinyPedal/pyLMUSharedMemory) Python definitions (MIT licence), which in turn follow the shared-memory header shipped with the game.
- Fonts: Saira Condensed and JetBrains Mono, both under the SIL Open Font License.

## Author

Nikolay Mizov. Contact: nikolaymizov43@gmail.com
