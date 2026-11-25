using CalmTerraria.Sonar;

namespace CalmTerraria.Tests;

public class SonarClockTests
{
    private static int Pulses(SonarClock clock, Func<ulong, Contact[]> contactsAt, ulong forMs, int? id = null)
    {
        var count = 0;
        for (ulong now = 0; now < forMs; now += 16)
            count += clock.Update(now, contactsAt(now)).Count(ping => id == null || ping.Contact.Id == id);
        return count;
    }

    [Fact]
    public void ANearContactPulsesFarMoreOftenThanAFarOne()
    {
        var settings = new SonarSettings();
        var near = Pulses(new SonarClock(settings), _ => new[] { new Contact(1, 2, 0, false, false) }, 3000);
        var far = Pulses(new SonarClock(settings), _ => new[] { new Contact(1, 58, 0, false, false) }, 3000);
        // About one every second and a half at the edge of range;
        // about three a second two tiles away.
        Assert.InRange(far, 2, 3);
        Assert.InRange(near, 8, 12);
    }

    [Fact]
    public void ANewContactPulsesAtOnce()
    {
        var clock = new SonarClock(new SonarSettings());
        Assert.Single(clock.Update(5000, new[] { new Contact(7, 40, 0, false, false) }));
    }

    [Fact]
    public void AContactThatRushesInDoesNotWaitOutItsFarInterval()
    {
        var clock = new SonarClock(new SonarSettings());
        clock.Update(0, new[] { new Contact(1, 58, 0, false, false) });
        // 300 ms later it is beside the listener: far interval 1.5 s, near 0.13 s.
        Assert.Empty(clock.Update(100, new[] { new Contact(1, 58, 0, false, false) }));
        Assert.Single(clock.Update(300, new[] { new Contact(1, 1, 0, false, false) }));
    }

    [Fact]
    public void OnlyTheNearestFewSound()
    {
        var settings = new SonarSettings { MaxContacts = 2 };
        var contacts = new[]
        {
            new Contact(1, 30, 0, false, false),
            new Contact(2, 5, 0, false, false),
            new Contact(3, 10, 0, false, false),
        };
        var pings = new SonarClock(settings).Update(0, contacts);
        Assert.Equal(new[] { 2, 3 }, pings.Select(ping => ping.Contact.Id));
    }

    [Fact]
    public void BeyondRangeIsSilent()
    {
        var clock = new SonarClock(new SonarSettings { Range = 60 });
        Assert.Empty(clock.Update(0, new[] { new Contact(1, 61, 0, false, false) }));
    }

    [Fact]
    public void FartherIsQuieterButNeverSilentAndOccludedQuieterStill()
    {
        var settings = new SonarSettings();
        Ping Only(Contact contact) => new SonarClock(settings).Update(0, new[] { contact })[0];
        var near = Only(new Contact(1, 1, 0, false, false));
        var far = Only(new Contact(1, 59, 0, false, false));
        var hidden = Only(new Contact(1, 59, 0, true, false));
        Assert.True(near.Gain > far.Gain);
        Assert.True(far.Gain >= settings.FarGain);
        Assert.True(hidden.Gain < far.Gain && hidden.Gain > 0.1f);
    }

    [Fact]
    public void AboveIsHigherAndBelowIsLowerWithinLimits()
    {
        var settings = new SonarSettings();
        Ping Only(float dy) => new SonarClock(settings).Update(0, new[] { new Contact(1, 0, dy, false, false) })[0];
        Assert.True(Only(-10).Semitones > 0);
        Assert.True(Only(10).Semitones < 0);
        Assert.Equal(settings.MaxSemitones, Only(-50).Semitones);
    }
}

public class TonesTests
{
    public static TheoryData<string, float[]> All => new()
    {
        { "contact", Tones.Contact() },
        { "in reach", Tones.InReach() },
        { "jump", Tones.Jump() },
        { "land", Tones.Land() },
        { "heartbeat", Tones.Heartbeat() },
        { "bump", Tones.Bump() },
    };

    /// <summary>Nothing the sonar plays may be loud: about a third of
    /// full scale at the very most, and brief.</summary>
    [Theory]
    [MemberData(nameof(All))]
    public void EveryToneIsQuietAndShort(string name, float[] samples)
    {
        Assert.True(Tones.Peak(samples) <= Tones.Level + 0.01f, $"{name} peaks at {Tones.Peak(samples)}");
        Assert.True(Tones.Peak(samples) >= 0.05f, $"{name} is nearly silent");
        Assert.True(samples.Length <= Tones.SampleRate / 5, $"{name} is longer than a fifth of a second");
    }

    public static TheoryData<string, float[]> Loops => new()
    {
        { "yoyo", Tones.Yoyo() },
        { "incoming", Tones.Incoming() },
    };

    /// <summary>A loop is quiet, and its end meets its start: the step
    /// from the last sample round to the first is no bigger than any
    /// step inside it.</summary>
    [Theory]
    [MemberData(nameof(Loops))]
    public void EveryLoopIsQuietAndSeamless(string name, float[] samples)
    {
        Assert.True(Tones.Peak(samples) <= Tones.Level, $"{name} peaks at {Tones.Peak(samples)}");
        var largest = 0f;
        for (var i = 1; i < samples.Length; i++)
            largest = MathF.Max(largest, MathF.Abs(samples[i] - samples[i - 1]));
        Assert.True(MathF.Abs(samples[0] - samples[^1]) <= largest * 1.05f, $"{name} clicks where it loops");
    }

    [Theory]
    [MemberData(nameof(All))]
    public void EveryToneStartsAndEndsAtSilence(string name, float[] samples)
    {
        Assert.True(MathF.Abs(samples[0]) < 0.01f, $"{name} starts at {samples[0]}");
        Assert.True(MathF.Abs(samples[^1]) < 0.01f, $"{name} ends at {samples[^1]}");
    }
}
