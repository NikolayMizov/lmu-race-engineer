namespace LmuSimulator;

public enum RainTrendMode { Static, GettingWetter, Drying }

public class SimState
{
    // ── Race position / timing ────────────────────────────────────────────────
    public int    Lap           = 2;
    public int    MaxLaps       = 40;
    public int    PitStops      = 0;
    public bool   InPits        = false;
    public double CurrentET     = 92.0;

    // ── Gaps ──────────────────────────────────────────────────────────────────
    public float  GapFront      = 2.5f;
    public float  GapBehind     = 1.8f;

    // ── Energy / fuel (0-1) ───────────────────────────────────────────────────
    public double VirtualEnergy = 0.95;
    public float  StateOfCharge = 0.8f;
    public double FuelPerLap    = 0.025;   // fraction of total VE per lap

    // ── Tires (wear 0=new → 1=dead) ──────────────────────────────────────────
    public float  TireWear      = 0.05f;
    public double WearPerLap    = 0.018;   // fraction per lap
    public double BrakeTemp     = 350.0;

    // ── Lap times ─────────────────────────────────────────────────────────────
    public double LapTimeSecs   = 90.0;
    public double LastLapTime   = 90.2;
    public double BestLapTime   = 89.8;
    public double DeltaBest     = 0.12;
    public double LapTimeJitter = 0.3;

    // ── Weather ───────────────────────────────────────────────────────────────
    public float          Rain           = 0.0f;
    public double         AvgPathWetness = 0.0;
    public double         DarkCloud      = 0.1;
    public double         AmbientTemp    = 22.0;
    public double         TrackTemp      = 35.0;
    public byte           TrackGrip      = 95;
    public RainTrendMode  RainTrend      = RainTrendMode.Static;
    public double         RainRatePerLap = 0.05;   // change in Rain per lap when trend active

    // ── Misc ──────────────────────────────────────────────────────────────────
    public double GapJitter     = 0.15;

    // ─────────────────────────────────────────────────────────────────────────

    public void AdvanceLap()
    {
        Lap++;
        CurrentET += LapTimeSecs;

        // Lap time
        double jitter = (Random.Shared.NextDouble() - 0.5) * 2 * LapTimeJitter;
        LastLapTime   = LapTimeSecs + jitter;
        if (LastLapTime < BestLapTime) BestLapTime = LastLapTime;
        DeltaBest     = (Random.Shared.NextDouble() - 0.35) * 0.6;

        // Fuel & tires
        VirtualEnergy = Math.Max(0, VirtualEnergy - FuelPerLap);
        TireWear      = Math.Min(1f, TireWear + (float)WearPerLap);
        BrakeTemp     = Math.Clamp(BrakeTemp + (Random.Shared.NextDouble() - 0.4) * 20, 150, 850);

        // Gap jitter
        GapFront  = Math.Max(0, GapFront  + (float)((Random.Shared.NextDouble() - 0.5) * 2 * GapJitter));
        GapBehind = Math.Max(0, GapBehind + (float)((Random.Shared.NextDouble() - 0.5) * 2 * GapJitter));

        // Weather trend
        switch (RainTrend)
        {
            case RainTrendMode.GettingWetter:
                Rain           = Math.Min(1f, Rain + (float)RainRatePerLap);
                AvgPathWetness = Math.Min(0.95, AvgPathWetness + Rain * 0.08);
                DarkCloud      = Math.Min(1.0, DarkCloud + 0.02);
                break;
            case RainTrendMode.Drying:
                Rain           = Math.Max(0f, Rain - (float)RainRatePerLap);
                AvgPathWetness = Math.Max(0, AvgPathWetness - 0.04);
                DarkCloud      = Math.Max(0, DarkCloud - 0.02);
                break;
            default:
                // Static: passive wetness dissipation only
                if (Rain < 0.03f)
                    AvgPathWetness = Math.Max(0, AvgPathWetness - 0.015);
                break;
        }
    }

    public void ApplyScenario(string tag)
    {
        InPits = false;
        switch (tag)
        {
            case "fresh":
                Lap = 2; MaxLaps = 40; PitStops = 0;
                VirtualEnergy = 0.99; FuelPerLap = 0.025;
                TireWear = 0.01f;     WearPerLap = 0.018;
                Rain = 0; AvgPathWetness = 0; DarkCloud = 0.1;
                RainTrend = RainTrendMode.Static;
                GapFront = 0; GapBehind = 0;
                LastLapTime = 90.0; BestLapTime = 89.9; LapTimeSecs = 90;
                TrackTemp = 35; AmbientTemp = 22; TrackGrip = 95;
                BrakeTemp = 300; CurrentET = 92;
                break;

            case "mid":
                Lap = 15; MaxLaps = 40; PitStops = 0;
                VirtualEnergy = 0.62; FuelPerLap = 0.025;
                TireWear = 0.32f;     WearPerLap = 0.018;
                Rain = 0; AvgPathWetness = 0; DarkCloud = 0.2;
                RainTrend = RainTrendMode.Static;
                GapFront = 2.5f; GapBehind = 1.8f;
                LastLapTime = 90.4; BestLapTime = 89.8; LapTimeSecs = 90;
                TrackTemp = 38; AmbientTemp = 24; TrackGrip = 97;
                BrakeTemp = 420; CurrentET = 15 * 90.0;
                break;

            case "lowfuel":
                Lap = 33; MaxLaps = 40; PitStops = 1;
                VirtualEnergy = 0.07; FuelPerLap = 0.025;
                TireWear = 0.58f;     WearPerLap = 0.018;
                Rain = 0; AvgPathWetness = 0; DarkCloud = 0.1;
                RainTrend = RainTrendMode.Static;
                GapFront = 1.2f; GapBehind = 0.7f;
                LastLapTime = 90.6; BestLapTime = 89.8; LapTimeSecs = 90;
                TrackTemp = 36; AmbientTemp = 23; TrackGrip = 96;
                BrakeTemp = 440; CurrentET = 33 * 90.0;
                break;

            case "rain":
                Lap = 10; MaxLaps = 40; PitStops = 0;
                VirtualEnergy = 0.77; FuelPerLap = 0.027;
                TireWear = 0.19f;     WearPerLap = 0.020;
                Rain = 0.32f; AvgPathWetness = 0.22; DarkCloud = 0.85;
                RainTrend = RainTrendMode.GettingWetter; RainRatePerLap = 0.05;
                GapFront = 3.0f; GapBehind = 2.5f;
                LastLapTime = 94.5; BestLapTime = 89.8; LapTimeSecs = 94;
                TrackTemp = 22; AmbientTemp = 18; TrackGrip = 75;
                BrakeTemp = 320; CurrentET = 10 * 90.0;
                break;

            case "tirecliff":
                Lap = 23; MaxLaps = 40; PitStops = 0;
                VirtualEnergy = 0.44; FuelPerLap = 0.025;
                TireWear = 0.88f;     WearPerLap = 0.022;  // accelerated wear (cliff)
                Rain = 0; AvgPathWetness = 0; DarkCloud = 0.15;
                RainTrend = RainTrendMode.Static;
                GapFront = 0.5f; GapBehind = 0.3f;
                LastLapTime = 92.8; BestLapTime = 89.8; LapTimeSecs = 92;
                TrackTemp = 37; AmbientTemp = 24; TrackGrip = 96;
                BrakeTemp = 460; CurrentET = 23 * 90.0;
                break;

            case "wet":
                Lap = 5; MaxLaps = 40; PitStops = 0;
                VirtualEnergy = 0.87; FuelPerLap = 0.024;
                TireWear = 0.09f;     WearPerLap = 0.015;
                Rain = 0.78f; AvgPathWetness = 0.68; DarkCloud = 0.95;
                RainTrend = RainTrendMode.Drying; RainRatePerLap = 0.04;
                GapFront = 4.0f; GapBehind = 3.5f;
                LastLapTime = 97.5; BestLapTime = 89.8; LapTimeSecs = 97;
                TrackTemp = 18; AmbientTemp = 15; TrackGrip = 60;
                BrakeTemp = 270; CurrentET = 5 * 90.0;
                break;
        }
    }
}
