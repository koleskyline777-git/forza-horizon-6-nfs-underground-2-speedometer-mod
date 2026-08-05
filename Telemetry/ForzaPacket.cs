namespace Nfsu2ForzaHud.Telemetry;

/// <summary>
/// FH6 Data Out is a fixed 324-byte little-endian Horizon packet
/// (FH4/FH5 layout + CarGroup/Smashable* at 232–243).
/// </summary>
public static class ForzaPacket
{
    public const int Size = 324;

    // Key offsets (validated Horizon layout).
    public const int OffIsRaceOn = 0;
    public const int OffEngineMaxRpm = 8;
    public const int OffEngineIdleRpm = 12;
    public const int OffCurrentEngineRpm = 16;
    public const int OffSpeed = 256;
    public const int OffPower = 260;
    public const int OffBoost = 284;
    public const int OffGear = 319;
}

public sealed class TelemetryFrame
{
    public bool IsRaceOn { get; init; }
    public float EngineMaxRpm { get; init; }
    public float EngineIdleRpm { get; init; }
    public float CurrentEngineRpm { get; init; }
    public float SpeedMs { get; init; }
    public float BoostPsi { get; init; }
    public int Gear { get; init; }
    public float PowerWatts { get; init; }

    public float SpeedMph => SpeedMs * 2.2369362920544f;
    public float SpeedKph => SpeedMs * 3.6f;

    public float RpmNorm
    {
        get
        {
            var max = EffectiveMaxRpm;
            if (max <= 1f) return 0f;
            return Math.Clamp(CurrentEngineRpm / max, 0f, 1.05f);
        }
    }

    /// <summary>
    /// Dial full-scale RPM from the car/bike (FH6 bikes can hit ~20k).
    /// Falls back to 8000 when telemetry has no max yet.
    /// </summary>
    public float EffectiveMaxRpm
    {
        get
        {
            var max = EngineMaxRpm;
            if (max < 1000f) max = 8000f;
            return Math.Clamp(max, 4000f, 20000f);
        }
    }

    /// <summary>Map current RPM across the dial: 0 at idle side, 1.0 at EngineMaxRpm (≤20k).</summary>
    public float TachDial01(float? faceMaxRpm = null) =>
        Math.Clamp(CurrentEngineRpm / Math.Max(faceMaxRpm ?? EffectiveMaxRpm, 1f), 0f, 1.05f);

    /// <summary>Boost dial across -30..+30 visual range.</summary>
    public float BoostDial01
    {
        get
        {
            var psi = Math.Clamp(BoostPsi, -30f, 30f);
            return (psi + 30f) / 60f;
        }
    }

    public string GearLabel => Gear switch
    {
        0 => "R",
        >= 11 => "N",
        _ => Gear.ToString()
    };

    public static TelemetryFrame? TryParse(ReadOnlySpan<byte> data)
    {
        if (data.Length != ForzaPacket.Size) return null;

        static float F32(ReadOnlySpan<byte> s, int o) => BitConverter.ToSingle(s.Slice(o, 4));
        static int I32(ReadOnlySpan<byte> s, int o) => BitConverter.ToInt32(s.Slice(o, 4));

        return new TelemetryFrame
        {
            IsRaceOn = I32(data, ForzaPacket.OffIsRaceOn) != 0,
            EngineMaxRpm = F32(data, ForzaPacket.OffEngineMaxRpm),
            EngineIdleRpm = F32(data, ForzaPacket.OffEngineIdleRpm),
            CurrentEngineRpm = F32(data, ForzaPacket.OffCurrentEngineRpm),
            SpeedMs = F32(data, ForzaPacket.OffSpeed),
            PowerWatts = F32(data, ForzaPacket.OffPower),
            BoostPsi = F32(data, ForzaPacket.OffBoost),
            Gear = data[ForzaPacket.OffGear]
        };
    }

    public static TelemetryFrame Demo(double t)
    {
        // Alternate car (~8.5k) and bike (~14–20k) so high-redline mapping is visible.
        var bike = ((int)(t / 12.0) % 2) == 1;
        var maxRpm = bike ? 18000f : 8500f;
        var rpm = 1200f + (float)(Math.Abs(Math.Sin(t * 1.3)) * (maxRpm - 1400f));
        var speed = (float)(Math.Abs(Math.Sin(t * 0.35)) * (bike ? 55f : 70f)); // m/s
        var gear = 1 + (int)(rpm / (maxRpm / 5f));
        return new TelemetryFrame
        {
            IsRaceOn = true,
            EngineMaxRpm = maxRpm,
            EngineIdleRpm = bike ? 1500f : 900f,
            CurrentEngineRpm = rpm,
            SpeedMs = speed,
            BoostPsi = (float)(Math.Sin(t) * 12f),
            Gear = Math.Clamp(gear, 1, bike ? 6 : 6),
            PowerWatts = rpm * 40f
        };
    }
}
