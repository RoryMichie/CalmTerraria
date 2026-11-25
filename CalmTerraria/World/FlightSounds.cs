using CalmTerraria.Host;
using CalmTerraria.Screens;
using CalmTerraria.Sonar;
using log4net;
using Microsoft.Xna.Framework;
using Srui.Audio;
using Terraria;

namespace CalmTerraria.World;

/// <summary>Things in the air, heard where they are for as long as
/// they fly: the player's own yoyo, so it can be steered onto an
/// enemy by ear, and what is flying at the player, so it can be
/// stepped out of the way of. Each is a loop on its own voice: to the
/// side it is on, higher above and lower below, louder the nearer.
///
/// The player's own arrows and bullets are left silent. There are
/// many, they are gone in a moment, and where they went is told by
/// what they hit.</summary>
internal sealed class FlightSounds
{
    /// <summary>The game's kinds of weapon that stay out on a string
    /// or a chain and are steered: yoyos, flails, and the chained
    /// blades and harpoons that are thrown out and come back.</summary>
    private static readonly HashSet<int> Tethered = new() { 99, 15, 13 };
    /// <summary>How many things flying at the player are voiced.</summary>
    private const int MaxIncoming = 4;
    /// <summary>Beyond this many tiles, silent.</summary>
    private const float Range = 50f;
    private const float Depth = 8f;

    private readonly SruiHost _host;
    private readonly ILog _log;
    private readonly Dictionary<int, Voice> _voices = new();
    private string? _yoyoWav, _incomingWav;
    private bool _broken;

    public static string Report { get; private set; } = "nothing yet";
    private int _mostYoyos, _mostIncoming;

    private sealed class Voice
    {
        public required SoundEntity Entity;
        public required Sound Sound;
        public required int Identity;
        public bool Seen;
    }

    public FlightSounds(SruiHost host, Shell shell, ILog log)
    {
        _host = host;
        _log = log;
        shell.WorldFrame += Update;
        shell.LeftWorld += Silence;
    }

    private void Update()
    {
        if (_broken)
            return;
        try
        {
            Follow();
        }
        catch (Exception e)
        {
            _broken = true;
            _log.Error("The sounds of things in flight failed; they are off for this session.", e);
        }
    }

    private void Follow()
    {
        var player = Main.LocalPlayer;
        foreach (var voice in _voices.Values)
            voice.Seen = false;
        var quiet = Main.soundVolume <= 0f || player.dead || Main.gamePaused;
        if (!quiet)
        {
            var incoming = new List<Projectile>();
            foreach (var projectile in Main.ActiveProjectiles)
            {
                if (projectile.owner == Main.myPlayer && Tethered.Contains(projectile.aiStyle))
                    Voiced(projectile, yoyo: true, player);
                else if (projectile.hostile && projectile.damage > 0
                    && Vector2.Distance(projectile.Center, player.Center) <= Range * 16f)
                    incoming.Add(projectile);
            }
            foreach (var projectile in incoming
                .OrderBy(projectile => Vector2.DistanceSquared(projectile.Center, player.Center)).Take(MaxIncoming))
                Voiced(projectile, yoyo: false, player);
        }
        foreach (var index in _voices.Where(pair => !pair.Value.Seen).Select(pair => pair.Key).ToList())
        {
            _voices[index].Sound.Stop();
            _voices[index].Entity.Dispose();
            _voices.Remove(index);
        }
        Report = $"{_voices.Count} sounding now, most at once {_mostYoyos} yoyo and {_mostIncoming} incoming";
    }

    private void Voiced(Projectile projectile, bool yoyo, Player player)
    {
        var audio = _host.App.Audio;
        // A slot in the game's list is reused: the same slot with a
        // new identity is a new thing, and starts its sound afresh.
        if (_voices.TryGetValue(projectile.whoAmI, out var voice) && voice.Identity != projectile.identity)
        {
            voice.Sound.Stop();
            voice.Entity.Dispose();
            _voices.Remove(projectile.whoAmI);
            voice = null;
        }
        if (voice == null)
        {
            var folder = Path.Combine(Main.SavePath, "CalmTerraria", "sounds");
            if (_yoyoWav == null)
            {
                Tones.WriteWav(_yoyoWav = Path.Combine(folder, "yoyo.wav"), Tones.Yoyo());
                Tones.WriteWav(_incomingWav = Path.Combine(folder, "incoming.wav"), Tones.Incoming());
            }
            var entity = audio.CreateEntity();
            entity.Hrtf = audio.IsHrtfAvailable;
            var sound = audio.CreateSound(entity.Group);
            sound.Load(yoyo ? _yoyoWav : _incomingWav!);
            sound.Looping = true;
            _voices[projectile.whoAmI] = voice = new Voice { Entity = entity, Sound = sound, Identity = projectile.identity };
            Place(voice, projectile, player);
            sound.Play();
        }
        else
            Place(voice, projectile, player);
        voice.Seen = true;
        _mostYoyos = Math.Max(_mostYoyos, _voices.Values.Count(v => v.Seen) * (yoyo ? 1 : 0));
        if (!yoyo)
            _mostIncoming = Math.Max(_mostIncoming, _voices.Count);
    }

    private static void Place(Voice voice, Projectile projectile, Player player)
    {
        var offset = (projectile.Center - player.Center) / 16f;
        // Direction from position, loudness from distance, as the
        // enemy sonar has it: the listener faces into the screen.
        var direction = Vector3.Normalize(new Vector3(offset.X, Depth, -offset.Y)) * 2f;
        voice.Entity.SetPosition(direction.X, direction.Y, direction.Z);
        var near = 1f - Math.Clamp(offset.Length() / Range, 0f, 1f);
        voice.Sound.BaseVolume = (0.25f + 0.75f * near) * Main.soundVolume;
        // Half a semitone a tile, an octave at most, up being up.
        var semitones = Math.Clamp(-offset.Y * 0.5f, -12f, 12f);
        voice.Sound.Pitch = MathF.Pow(2f, semitones / 12f);
    }

    private void Silence()
    {
        foreach (var voice in _voices.Values)
        {
            voice.Sound.Stop();
            voice.Entity.Dispose();
        }
        _voices.Clear();
    }
}
