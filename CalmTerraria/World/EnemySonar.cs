using CalmTerraria.Host;
using CalmTerraria.Screens;
using CalmTerraria.Sonar;
using log4net;
using Microsoft.Xna.Framework;
using Srui.Audio;
using Terraria;
using Terraria.ModLoader;

namespace CalmTerraria.World;

/// <summary>Enemies, heard. Each nearby enemy pulses from where it is:
/// left and right by position, above and below by pitch as well, the
/// pulse rate rising as it closes, muffled when solid ground stands
/// between, and a brighter pulse once the held melee weapon would
/// reach it.
///
/// When each pulse falls and how loud is <see cref="SonarClock"/>'s,
/// which knows nothing of the game; this gathers the enemies and
/// plays what the clock asks for.</summary>
internal sealed class EnemySonar
{
    /// <summary>How far in front of the listener the plane of the
    /// world sits, in tiles. An enemy this far to the side is heard at
    /// forty-five degrees; smaller spreads near enemies wider.</summary>
    private const float Depth = 8f;
    /// <summary>Where the muffling of an enemy behind ground begins.</summary>
    private const double OccludedCutoffHz = 700.0;

    private readonly SruiHost _host;
    private readonly ILog _log;
    private readonly SonarClock _clock;
    private readonly List<Contact> _contacts = new();
    private readonly Dictionary<int, Voice> _voices = new();
    private string? _contactWav, _reachWav;
    private bool _broken;

    public SonarSettings Settings { get; } = new();

    /// <summary>For autopilot scripts: write each pulse to the log.</summary>
    public bool LogPulses { get; set; }

    private sealed class Voice
    {
        public required SoundEntity Entity;
        public bool Occluded;
        public ulong LastUsed;
    }

    public EnemySonar(SruiHost host, Shell shell, ILog log)
    {
        _host = host;
        _log = log;
        _clock = new SonarClock(Settings);
        shell.WorldFrame += Update;
        shell.LeftWorld += Silence;
    }

    private void Update()
    {
        if (_broken || Main.soundVolume <= 0f)
            return;
        var player = Main.LocalPlayer;
        _contacts.Clear();
        if (!player.dead)
        {
            var reach = MeleeReach(player);
            foreach (var npc in Main.ActiveNPCs)
            {
                if (npc.friendly || npc.townNPC || npc.lifeMax <= 5 || npc.damage <= 0 || npc.CountsAsACritter)
                    continue;
                var offset = (npc.Center - player.Center) / 16f;
                if (Math.Abs(offset.X) > Settings.Range || Math.Abs(offset.Y) > Settings.Range)
                    continue;
                var occluded = !Collision.CanHitLine(
                    player.position, player.width, player.height, npc.position, npc.width, npc.height);
                var inReach = reach > 0f && Gap(player.Hitbox, npc.Hitbox) <= reach;
                _contacts.Add(new Contact(npc.whoAmI, offset.X, offset.Y, occluded, inReach));
            }
        }
        var pings = _clock.Update(_host.Now, _contacts);
        if (pings.Count > 0)
        {
            try
            {
                foreach (var ping in pings)
                    Play(ping);
            }
            catch (Exception e)
            {
                _broken = true;
                _log.Error("The sonar's audio failed; it is off for this session.", e);
            }
        }
        Prune();
    }

    /// <summary>How far, in pixels, the held weapon's swing reaches
    /// beyond the player's body, or zero when what is held is not
    /// swung. An estimate from the item's size: enough to say "now".</summary>
    private static float MeleeReach(Player player)
    {
        var item = player.HeldItem;
        if (item.IsAir || item.damage <= 0 || item.noMelee || !item.CountsAsClass(DamageClass.Melee))
            return 0f;
        return Math.Max(item.width, item.height) * item.scale + 8f;
    }

    /// <summary>The distance between two boxes, edge to edge.</summary>
    private static float Gap(Rectangle a, Rectangle b)
    {
        float dx = Math.Max(0, Math.Max(a.Left - b.Right, b.Left - a.Right));
        float dy = Math.Max(0, Math.Max(a.Top - b.Bottom, b.Top - a.Bottom));
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private void Play(in Ping ping)
    {
        var audio = _host.App.Audio;
        if (_contactWav == null)
        {
            var folder = Path.Combine(Main.SavePath, "CalmTerraria", "sounds");
            Tones.WriteWav(_contactWav = Path.Combine(folder, "sonar-contact.wav"), Tones.Contact());
            Tones.WriteWav(_reachWav = Path.Combine(folder, "sonar-reach.wav"), Tones.InReach());
            // Face +Y, so positions along X read as left and right.
            audio.SetListener(0f, 0f, 0f, 90f);
        }
        var contact = ping.Contact;
        if (!_voices.TryGetValue(contact.Id, out var voice))
        {
            var entity = audio.CreateEntity();
            entity.Hrtf = audio.IsHrtfAvailable;
            _voices[contact.Id] = voice = new Voice { Entity = entity };
        }
        voice.LastUsed = _host.Now;
        if (voice.Occluded != contact.Occluded)
        {
            voice.Occluded = contact.Occluded;
            voice.Entity.Group.SetFxChain(contact.Occluded
                ? new SoundEffect[] { new SoundEffect.Lowpass(OccludedCutoffHz, 2) }
                : null);
        }
        // Direction only: every source sits at the same distance, so
        // loudness is the clock's to decide and not the spatializer's.
        var direction = Vector3.Normalize(new Vector3(contact.Dx, Depth, -contact.Dy)) * 2f;
        voice.Entity.SetPosition(direction.X, direction.Y, direction.Z);

        var sound = audio.CreateSound(voice.Entity.Group, oneshot: true);
        sound.Load(contact.InReach ? _reachWav! : _contactWav);
        sound.BaseVolume = ping.Gain * Main.soundVolume;
        sound.Pitch = MathF.Pow(2f, ping.Semitones / 12f);
        sound.Play();

        if (LogPulses)
            _log.Info($"Sonar: {Main.npc[contact.Id].TypeName} {contact.Dx:0} right {contact.Dy:0} down, "
                + $"gain {ping.Gain:0.00}, {ping.Semitones:+0.0;-0.0} st"
                + (contact.Occluded ? ", occluded" : "") + (contact.InReach ? ", in reach" : ""));
    }

    /// <summary>An enemy that has not pulsed for a while is gone or
    /// out of range; its place in the mix is given back.</summary>
    private void Prune()
    {
        if (_voices.Count == 0)
            return;
        List<int>? stale = null;
        foreach (var (id, voice) in _voices)
        {
            if (_host.Now - voice.LastUsed > 4000)
                (stale ??= new List<int>()).Add(id);
        }
        if (stale == null)
            return;
        foreach (var id in stale)
        {
            _voices[id].Entity.Dispose();
            _voices.Remove(id);
        }
    }

    private void Silence()
    {
        foreach (var voice in _voices.Values)
            voice.Entity.Dispose();
        _voices.Clear();
        _clock.Reset();
    }
}
