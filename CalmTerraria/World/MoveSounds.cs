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
    }

    private void Update()
    {
        var player = Main.LocalPlayer;
        if (_broken || player.dead || Main.gamePaused)
            return;
        // The heart: quicker the less health is left.
        var health = player.statLifeMax2 > 0 ? player.statLife / (float)player.statLifeMax2 : 1f;
        if (health < LowHealth && _host.Now >= _nextBeat)
        {
            Beats++;
            Play(ref _heartWav, "heartbeat", Tones.Heartbeat, Volume);
            _nextBeat = _host.Now + (ulong)(FastBeatMs + (SlowBeatMs - FastBeatMs) * (health / LowHealth));
        }
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
