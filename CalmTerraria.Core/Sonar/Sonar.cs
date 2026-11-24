namespace CalmTerraria.Sonar;

/// <summary>Something the sonar may sound: where it is from the
/// listener in tiles (right and down positive), whether solid ground
/// is in the way, and whether the held weapon reaches it.</summary>
public readonly record struct Contact(int Id, float Dx, float Dy, bool Occluded, bool InReach)
{
    public float Distance => MathF.Sqrt(Dx * Dx + Dy * Dy);
}

/// <summary>One pulse to play, now.</summary>
/// <param name="Gain">0 to 1, before the player's volume.</param>
/// <param name="Semitones">Pitch offset: up for a contact above the
/// listener, down for one below.</param>
public readonly record struct Ping(Contact Contact, float Gain, float Semitones);

/// <summary>Every number the sonar's behaviour turns on, in one place.</summary>
public sealed class SonarSettings
{
    /// <summary>Tiles. Nothing farther sounds. About a screen's width
    /// from the centre, so the sonar hears what a screen shows.</summary>
    public float Range { get; set; } = 60f;

    /// <summary>The most contacts sounding at once, nearest first.
    /// More than a handful stops being a picture.</summary>
    public int MaxContacts { get; set; } = 5;

    /// <summary>Milliseconds between pulses for a contact on top of
    /// the listener.</summary>
    public float NearIntervalMs { get; set; } = 130f;

    /// <summary>Milliseconds between pulses for a contact at the edge
    /// of range.</summary>
    public float FarIntervalMs { get; set; } = 1500f;

    /// <summary>How the interval grows with distance. Below 1, the
    /// rate changes most close in, where it matters; 1 is a straight
    /// line.</summary>
    public float IntervalCurve { get; set; } = 0.6f;

    /// <summary>Gain at the edge of range. A far contact is quieter
    /// but never silent: the rate, not the loudness, says how far.</summary>
    public float FarGain { get; set; } = 0.3f;

    /// <summary>Gain multiplier for a contact behind solid ground.
    /// The muffling itself is the audio layer's.</summary>
    public float OccludedGain { get; set; } = 0.6f;

    /// <summary>Semitones of pitch per tile above the listener.</summary>
    public float SemitonesPerTile { get; set; } = 0.5f;

    /// <summary>The most the pitch moves, either way.</summary>
    public float MaxSemitones { get; set; } = 12f;
}

/// <summary>Decides when each contact pulses. A contact pulses faster
/// the nearer it is, like a parking sensor, so the rate alone tells
/// distance and a closing enemy is heard closing. The interval is
/// measured from a contact's last pulse against where it is now, so
/// one that rushes in does not wait out a long gap it was given when
/// it was far away.</summary>
public sealed class SonarClock
{
    private readonly SonarSettings _settings;
    private readonly Dictionary<int, ulong> _lastPulse = new();
    private readonly List<Ping> _pings = new();
    private readonly List<Contact> _chosen = new();
    private readonly List<int> _forgotten = new();

    public SonarClock(SonarSettings settings) => _settings = settings;

    public float IntervalMs(float distance)
    {
        var t = Math.Clamp(distance / _settings.Range, 0f, 1f);
        return _settings.NearIntervalMs
            + (_settings.FarIntervalMs - _settings.NearIntervalMs) * MathF.Pow(t, _settings.IntervalCurve);
    }

    /// <summary>The pulses due at this moment. The list is reused
    /// between calls.</summary>
    public IReadOnlyList<Ping> Update(ulong nowMs, IReadOnlyList<Contact> contacts)
    {
        _pings.Clear();
        _chosen.Clear();
        foreach (var contact in contacts)
        {
            if (contact.Distance <= _settings.Range)
                _chosen.Add(contact);
        }
        _chosen.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        if (_chosen.Count > _settings.MaxContacts)
            _chosen.RemoveRange(_settings.MaxContacts, _chosen.Count - _settings.MaxContacts);

        _forgotten.Clear();
        foreach (var id in _lastPulse.Keys)
        {
            if (!_chosen.Exists(contact => contact.Id == id))
                _forgotten.Add(id);
        }
        foreach (var id in _forgotten)
            _lastPulse.Remove(id);

        foreach (var contact in _chosen)
        {
            var distance = contact.Distance;
            if (_lastPulse.TryGetValue(contact.Id, out var last) && nowMs - last < IntervalMs(distance))
                continue;
            _lastPulse[contact.Id] = nowMs;
            var gain = 1f - (1f - _settings.FarGain) * Math.Clamp(distance / _settings.Range, 0f, 1f);
            if (contact.Occluded)
                gain *= _settings.OccludedGain;
            var semitones = Math.Clamp(
                -contact.Dy * _settings.SemitonesPerTile, -_settings.MaxSemitones, _settings.MaxSemitones);
            _pings.Add(new Ping(contact, gain, semitones));
        }
        return _pings;
    }

    public void Reset() => _lastPulse.Clear();
}
