using CalmTerraria.Host;
using CalmTerraria.Screens;
using CalmTerraria.Sonar;
using log4net;
using Srui.Audio;
using Terraria;

namespace CalmTerraria.World;

/// <summary>Leaving the ground and coming back to it, heard: the game
/// has no sound for a jump, and a landing only shares the footstep's.
/// The landing is louder the faster the fall.</summary>
internal sealed class MoveSounds
{
    /// <summary>Frames off the ground before coming back counts as a
    /// landing: stairs and slopes lift the feet for a frame or two.</summary>
    private const int AirborneFrames = 6;
    private const float Volume = 1f;

    /// <summary>Health, as a share of the most there can be, under
    /// which the heart is heard; and how long between beats just
    /// under it and at the very end.</summary>
    private const float LowHealth = 0.3f;
    private const float SlowBeatMs = 1300f, FastBeatMs = 500f;
    private string? _heartWav;
    private ulong _nextBeat;

    private readonly SruiHost _host;
    private readonly ILog _log;
    private string? _jumpWav, _landWav;
    private bool _grounded = true;
    private int _airborne;
    private float _fallSpeed;
    private bool _broken;

    public static int Jumps { get; private set; }
    public static int Landings { get; private set; }
    public static int Beats { get; private set; }

    public MoveSounds(SruiHost host, Shell shell, ILog log)
    {
        _host = host;
        _log = log;
        shell.EnteredWorld += () =>
        {
            _grounded = true;
            _airborne = 0;
        };
        shell.WorldFrame += Update;
        shell.LeftWorld += Quiet;
    }

    private void Update()
    {
        var player = Main.LocalPlayer;
        if (_broken || player.dead || Main.gamePaused)
        {
            Quiet();
            return;
        }
        // The heart: quicker the less health is left.
        var health = player.statLifeMax2 > 0 ? player.statLife / (float)player.statLifeMax2 : 1f;
        if (health < LowHealth && _host.Now >= _nextBeat)
        {
            Beats++;
            Play(ref _heartWav, "heartbeat", Tones.Heartbeat, Volume);
            _nextBeat = _host.Now + (ulong)(FastBeatMs + (SlowBeatMs - FastBeatMs) * (health / LowHealth));
        }
        Bump(player);
        Fall(player);
        Dodge(player);
        // A fish on the player's own line: the float is being pulled.
        var biting = false;
        foreach (var projectile in Main.ActiveProjectiles)
        {
            if (projectile.owner == Main.myPlayer && projectile.bobber && projectile.ai[1] < 0f)
                biting = true;
        }
        if (biting && !_wasBiting)
        {
            Bites++;
            Play(ref _biteWav, "bite", Tones.Bite, Volume);
        }
        _wasBiting = biting;
        var grounded = player.velocity.Y == 0f;
        if (!grounded)
        {
            // Going up off the ground is a jump; walking off an edge is not.
            if (_grounded && player.velocity.Y < -1f && !player.mount.Active && player.grappling[0] < 0)
            {
                Jumps++;
                Play(ref _jumpWav, "jump", Tones.Jump, Volume);
            }
            _airborne++;
            _fallSpeed = player.velocity.Y;
        }
        else
        {
            if (!_grounded && _airborne >= AirborneFrames)
            {
                Landings++;
                // From a hop to terminal velocity.
                var hard = Math.Clamp(_fallSpeed / 10f, 0f, 1f);
                Play(ref _landWav, "land", Tones.Land, Volume * (0.5f + 0.5f * hard));
            }
            _airborne = 0;
        }
        _grounded = grounded;
    }

    private Sound? _falling, _fallingFar;
    private int _fallFrames;

    public static int Falls { get; private set; }
    public static int HardFalls { get; private set; }

    /// <summary>The game's own measure: a fall of more than this many
    /// tiles hurts, unless something worn prevents it.</summary>
    internal const int SafeFall = 25;

    /// <summary>Falling: a whistle for as long as it lasts, sinking in
    /// pitch as the fall goes on, and fluttering once the fall is
    /// long enough to hurt at the bottom. It starts a third of a
    /// second in, so a hop or a step down is not a fall.</summary>
    private void Fall(Player player)
    {
        var falling = player.velocity.Y > 4f && !player.mount.Active && player.grappling[0] < 0 && !player.pulley;
        _fallFrames = falling ? _fallFrames + 1 : 0;
        var sounding = _fallFrames >= 20 && Main.soundVolume > 0f;
        var fallen = Math.Max(0f, player.position.Y / 16f - player.fallStart);
        var hurts = sounding && !player.noFallDmg && fallen > SafeFall + player.extraFall;
        try
        {
            if (sounding && _falling == null)
            {
                var folder = Path.Combine(Main.SavePath, "CalmTerraria", "sounds");
                var audio = _host.App.Audio;
                Sound Loop(string name, float[] samples)
                {
                    var path = Path.Combine(folder, name + ".wav");
                    Tones.WriteWav(path, samples);
                    var sound = audio.CreateSound();
                    sound.Load(path);
                    sound.Looping = true;
                    return sound;
                }
                _falling = Loop("falling", Tones.Falling());
                _fallingFar = Loop("falling-far", Tones.FallingFar());
            }
            if (_falling == null || _fallingFar == null)
                return;
            Set(_falling, sounding && !hurts, fallen, ref _wasFalling, () => Falls++);
            Set(_fallingFar, hurts, fallen, ref _wasFallingFar, () => HardFalls++);
        }
        catch (Exception e)
        {
            _broken = true;
            _log.Error("The falling whistle failed; movement sounds are off for this session.", e);
        }
    }

    private bool _wasFalling, _wasFallingFar;

    /// <summary>A whistle must not outlast the fall: dead, paused or
    /// out of the world, it stops.</summary>
    private void Quiet()
    {
        if (_wasFalling)
            _falling?.Stop();
        if (_wasFallingFar)
            _fallingFar?.Stop();
        _wasFalling = _wasFallingFar = false;
        _fallFrames = 0;
    }

    private static void Set(Sound sound, bool on, float fallen, ref bool was, Action started)
    {
        if (on)
        {
            // An octave down over sixty tiles of falling.
            sound.Pitch = MathF.Pow(2f, -Math.Clamp(fallen / 60f, 0f, 1f));
            sound.BaseVolume = Volume * Main.soundVolume;
            if (!was)
            {
                sound.Play();
                started();
            }
        }
        else if (was)
            sound.Stop();
        was = on;
    }

    private string? _bumpWav;
    private SoundEntity? _leftSide, _rightSide;
    private int _pressing, _against;
    private bool _bumped;

    public static int Bumps { get; private set; }
    public static int Bites { get; private set; }
    private string? _biteWav;
    private bool _wasBiting;

    /// <summary>Walking into a wall: a knock, from the side the wall
    /// is on, once for each time it is walked into. Pressing on
    /// against it says nothing more; stepping away and back does.</summary>
    private void Bump(Player player)
    {
        var direction = player.controlLeft == player.controlRight ? 0 : player.controlLeft ? -1 : 1;
        if (direction == 0)
        {
            _pressing = 0;
            _bumped = false;
            return;
        }
        // A new push the other way is a new chance to knock.
        if (direction != _against)
        {
            _against = direction;
            _pressing = 0;
            _bumped = false;
        }
        var stopped = Math.Abs(player.velocity.X) < 0.01f && Math.Abs(player.position.X - player.oldPosition.X) < 0.01f;
        if (!stopped)
        {
            _pressing = 0;
            _bumped = false;
            return;
        }
        // Stopped with the key down is not yet a wall: a frozen or
        // webbed character stops too. Something solid has to be there.
        if (_bumped || ++_pressing < 3
            || !Collision.SolidCollision(player.position + new Microsoft.Xna.Framework.Vector2(direction * 6f, 0f), player.width, player.height - 18))
            return;
        _bumped = true;
        Bumps++;
        if (Main.soundVolume <= 0f)
            return;
        try
        {
            var audio = _host.App.Audio;
            if (_bumpWav == null)
            {
                Tones.WriteWav(_bumpWav = Path.Combine(Main.SavePath, "CalmTerraria", "sounds", "bump.wav"), Tones.Bump());
                _leftSide = audio.CreateEntity();
                _rightSide = audio.CreateEntity();
                _leftSide.Hrtf = _rightSide.Hrtf = audio.IsHrtfAvailable;
                // The listener faces into the screen; a wall is to one side.
                _leftSide.SetPosition(-1.6f, 1.2f, 0f);
                _rightSide.SetPosition(1.6f, 1.2f, 0f);
            }
            var sound = audio.CreateSound((direction < 0 ? _leftSide! : _rightSide!).Group, oneshot: true);
            sound.Load(_bumpWav);
            sound.BaseVolume = Volume * Main.soundVolume;
            sound.Play();
        }
        catch (Exception e)
        {
            _broken = true;
            _log.Error("The wall knock failed; movement sounds are off for this session.", e);
        }
    }

    // ── Something rolling at the character ──
    // A boulder or a rolling cactus is on the character in a second or
    // two and is quicker than they are: it is jumped, and the jump has
    // to be timed. A jump is clear of it for about two thirds of a
    // second, so the cue comes a little under half a second before it
    // arrives - time to hear it and press, and still be up when the
    // thing goes under. How long until it arrives is worked out each
    // frame from how far off it is, how fast the two are closing, and
    // how fast that is growing: the thing gathers speed all the way,
    // and taken at its speed of the moment it would be a third late.

    public static int Dodges { get; private set; }

    /// <summary>The game's way of moving a boulder.</summary>
    private const int BoulderStyle = 25;
    /// <summary>How long before it arrives the cue sounds, in frames.</summary>
    private const float DodgeFrames = 27f;

    private string? _dodgeWav;
    /// <summary>What has been cued, and for how many frames; -1 once
    /// it has arrived.</summary>
    private readonly Dictionary<int, int> _cued = new();
    /// <summary>How fast each was closing a frame ago.</summary>
    private readonly Dictionary<int, float> _closing = new();

    private void Dodge(Player player)
    {
        var live = new HashSet<int>();
        foreach (var projectile in Main.ActiveProjectiles)
        {
            if (!projectile.hostile || projectile.damage <= 0 || projectile.aiStyle != BoulderStyle)
                continue;
            live.Add(projectile.identity);
            var across = projectile.Center.X - player.Center.X;
            // How fast the space between them is shrinking, and how
            // much of it there is, edge to edge.
            var closing = -Math.Sign(across) * (projectile.velocity.X - player.velocity.X);
            var gap = Math.Abs(across) - (projectile.width + player.width) / 2f;
            var gathering = _closing.TryGetValue(projectile.identity, out var before) ? Math.Clamp(closing - before, 0f, 0.2f) : 0f;
            _closing[projectile.identity] = closing;
            if (_cued.TryGetValue(projectile.identity, out var since))
            {
                if (closing <= 0f && gap > 32f)
                    // Gone by or turned back: news again if it returns.
                    _cued.Remove(projectile.identity);
                else if (since >= 0 && gap <= 0f)
                {
                    _log.Info($"Dodge: arrived {since} frames after the cue.");
                    _cued[projectile.identity] = -1;
                }
                else if (since >= 0)
                    _cued[projectile.identity] = since + 1;
                continue;
            }
            // On the character's own level: it would hit them where they stand.
            var level = Math.Abs(projectile.Center.Y - player.Center.Y) < (projectile.height + player.height) / 2f + 8f;
            if (!level || closing <= 0f || gap <= 0f)
                continue;
            // gap = closing * t + gathering * t * t / 2, for t.
            var frames = gathering > 0.001f
                ? (MathF.Sqrt(closing * closing + 2f * gathering * gap) - closing) / gathering
                : gap / closing;
            if (frames > DodgeFrames)
                continue;
            _cued[projectile.identity] = 0;
            Dodges++;
            _log.Info($"Dodge: cue with it {gap / 16f:0.0} tiles off, closing at {closing:0.0}.");
            Play(ref _dodgeWav, "dodge", Tones.Dodge, Volume);
        }
        foreach (var gone in _cued.Keys.Where(identity => !live.Contains(identity)).ToList())
            _cued.Remove(gone);
        foreach (var gone in _closing.Keys.Where(identity => !live.Contains(identity)).ToList())
            _closing.Remove(gone);
    }

    private void Play(ref string? wav, string name, Func<float[]> tone, float volume)
    {
        if (Main.soundVolume <= 0f)
            return;
        try
        {
            if (wav == null)
                Tones.WriteWav(wav = Path.Combine(Main.SavePath, "CalmTerraria", "sounds", name + ".wav"), tone());
            var sound = _host.App.Audio.CreateSound(oneshot: true);
            sound.Load(wav);
            sound.BaseVolume = volume * Main.soundVolume;
            sound.Play();
        }
        catch (Exception e)
        {
            _broken = true;
            _log.Error("The jump and landing sounds failed; they are off for this session.", e);
        }
    }
}
