namespace CalmTerraria.Sonar;

/// <summary>The sonar's sounds, made from arithmetic so there is no
/// asset to lose and every property is a number here. They are
/// placeholders by intent: short, plain, and level-checked by a test,
/// to be replaced by designed sounds without touching anything else.</summary>
public static class Tones
{
    public const int SampleRate = 48000;

    /// <summary>The loudest any tone gets, of full scale.</summary>
    public const float Level = 0.35f;

    /// <summary>A contact: a soft blip that falls a little in pitch,
    /// round enough to hear many times a second.</summary>
    public static float[] Contact() => Blip(frequency: 620f, fall: 0.85f, seconds: 0.07f, decay: 55f, harmonics: 0.15f);

    /// <summary>A contact the held weapon reaches: the same gesture,
    /// brighter and higher, so it reads as the same thing made urgent.</summary>
    public static float[] InReach() => Blip(frequency: 930f, fall: 0.9f, seconds: 0.07f, decay: 50f, harmonics: 0.6f);

    /// <summary>Leaving the ground: a soft rise.</summary>
    public static float[] Jump() => Blip(frequency: 420f, fall: 1.8f, seconds: 0.11f, decay: 22f, harmonics: 0.25f);

    /// <summary>Coming back to it: a low thud that drops away.</summary>
    public static float[] Land() => Blip(frequency: 210f, fall: 0.5f, seconds: 0.14f, decay: 26f, harmonics: 0.6f);

    /// <summary>Walking into something solid: a short dull knock.</summary>
    public static float[] Bump() => Blip(frequency: 170f, fall: 0.6f, seconds: 0.07f, decay: 60f, harmonics: 0.9f);

    /// <summary>A fish on the line: two quick high notes, the second
    /// higher. The game's own cue is the splash of the float dipping,
    /// which is easy to lose under everything else.</summary>
    public static float[] Bite()
    {
        var first = Blip(frequency: 880f, fall: 1f, seconds: 0.05f, decay: 40f, harmonics: 0.2f);
        var second = Blip(frequency: 1180f, fall: 1f, seconds: 0.06f, decay: 35f, harmonics: 0.2f);
        return first.Concat(new float[(int)(SampleRate * 0.03f)]).Concat(second).ToArray();
    }

    /// <summary>Low health: a heartbeat, two soft thumps, the second a
    /// little lower. Pitched where small speakers and headphones still
    /// carry it, not down where a real one sits.</summary>
    public static float[] Heartbeat()
    {
        var first = Blip(frequency: 280f, fall: 0.9f, seconds: 0.05f, decay: 45f, harmonics: 0.2f);
        var second = Blip(frequency: 245f, fall: 0.9f, seconds: 0.06f, decay: 40f, harmonics: 0.2f);
        var gap = new float[(int)(SampleRate * 0.075f)];
        return first.Concat(gap).Concat(second).ToArray();
    }

    /// <summary>A tone that wavers, made to be played round and
    /// round: its length holds a whole number of the tone's cycles
    /// and of its wavers, so the end runs into the start without a
    /// click. It has no fade, being a loop.</summary>
    private static float[] Wobble(float frequency, float wavers, float depth, float seconds, float level)
    {
        var samples = new float[(int)(SampleRate * seconds)];
        for (var i = 0; i < samples.Length; i++)
        {
            var t = i / (double)SampleRate;
            // The pitch sways and the loudness with it.
            var phase = 2.0 * Math.PI * frequency * t + depth * Math.Sin(2.0 * Math.PI * wavers * t);
            var swell = 0.75 + 0.25 * Math.Cos(2.0 * Math.PI * wavers * t);
            samples[i] = (float)(Math.Sin(phase) * swell * level);
        }
        return samples;
    }

    /// <summary>The player's own yoyo, out on its string: a soft
    /// waver, to be heard where the yoyo is.</summary>
    public static float[] Yoyo() => Wobble(frequency: 340f, wavers: 6f, depth: 2.2f, seconds: 0.5f, level: Level * 0.7f);

    /// <summary>Something flying at the player: higher and quicker,
    /// so it is never taken for the yoyo.</summary>
    public static float[] Incoming() => Wobble(frequency: 760f, wavers: 14f, depth: 1.4f, seconds: 0.5f, level: Level * 0.7f);

    private static float[] Blip(float frequency, float fall, float seconds, float decay, float harmonics)
    {
        var samples = new float[(int)(SampleRate * seconds)];
        var phase = 0.0;
        for (var i = 0; i < samples.Length; i++)
        {
            var t = i / (float)SampleRate;
            var progress = i / (float)samples.Length;
            // The pitch glides from the start frequency down to fall times it.
            var now = frequency * (1f - (1f - fall) * progress);
            phase += 2.0 * Math.PI * now / SampleRate;
            // A few milliseconds of rise, so the start does not click.
            var attack = Math.Min(1f, t / 0.003f);
            var envelope = attack * MathF.Exp(-decay * t);
            var tone = Math.Sin(phase) + harmonics * Math.Sin(2.0 * phase) + harmonics * 0.5 * Math.Sin(3.0 * phase);
            samples[i] = (float)(tone * envelope * Level / (1.0 + 1.5 * harmonics));
        }
        // And a short fade at the end, for the same reason.
        var fade = Math.Min(samples.Length, SampleRate / 500);
        for (var i = 0; i < fade; i++)
            samples[samples.Length - 1 - i] *= i / (float)fade;
        return samples;
    }

    public static float Peak(float[] samples) => samples.Max(MathF.Abs);

    public static float Rms(float[] samples) =>
        MathF.Sqrt(samples.Sum(sample => sample * sample) / samples.Length);

    /// <summary>Writes mono 16-bit PCM.</summary>
    public static void WriteWav(string path, float[] samples)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + samples.Length * 2);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples.Length * 2);
        foreach (var sample in samples)
            writer.Write((short)(Math.Clamp(sample, -1f, 1f) * short.MaxValue));
    }
}
