using CalmTerraria.Host;
using Srui;
using Srui.Audio;
using Terraria;

namespace CalmTerraria.Spike;

/// <summary>The proof that the pieces work inside the game, and nothing
/// more: F2 opens a screen of ordinary SRUI widgets, spoken through
/// Prism, that owns the keyboard until Escape closes it. Each widget
/// exercises one thing the real mod depends on. Deleted when the first
/// real screen exists.</summary>
internal sealed class SpikeScreen
{
    private readonly SruiHost _host;
    private bool _built;
    private SoundEntity? _source;
    private Sound? _ping;

    public SpikeScreen(SruiHost host)
    {
        _host = host;
        host.KeyWhileGameOwns = combo =>
        {
            if (combo == KeyCombo.Plain(Key.F(2)))
                Open();
        };
        host.App.UnhandledInput = input =>
        {
            if (input.Kind != InputKind.Dismiss)
                return false;
            Close();
            return true;
        };
        host.App.Announce("CalmTerraria loaded. F2 opens the test screen.");
    }

    private void Open()
    {
        _host.TakeKeyboard();
        if (_built)
        {
            _host.App.ReannounceWithContext();
            return;
        }
        _built = true;
        var app = _host.App;
        var group = new Group(app, "CalmTerraria test");
        var backend = new Button(group, "Speech backend");
        backend.Activated += () => app.Announce(_host.SpeechBackend);
        var left = new Button(group, "Sound on the left");
        left.Activated += () => Ping(-3.0f);
        var right = new Button(group, "Sound on the right");
        right.Activated += () => Ping(3.0f);
        new EditBox(group, "Typing");
        var close = new Button(group, "Close");
        close.Activated += Close;
        app.EnsureFocus();
    }

    private void Close()
    {
        _host.ReleaseKeyboard();
        _host.App.Announce("Closed.");
    }

    private void Ping(float x)
    {
        var audio = _host.App.Audio;
        if (_ping == null)
        {
            var wav = Path.Combine(Main.SavePath, "CalmTerraria", "ping.wav");
            WritePing(wav);
            // Face +Y, so positions along X read as left and right.
            audio.SetListener(0.0f, 0.0f, 0.0f, 90.0f);
            _source = audio.CreateEntity();
            _source.Hrtf = audio.IsHrtfAvailable;
            _ping = audio.CreateSound(_source.Group);
            _ping.Load(wav);
        }
        _source!.SetPosition(x, 2.0f, 0.0f);
        _ping.Stop();
        _ping.Play();
    }

    private static void WritePing(string path)
    {
        const int sampleRate = 44100;
        const int frames = sampleRate * 3 / 10;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + frames * 2);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(frames * 2);
        for (var i = 0; i < frames; i++)
        {
            var t = i / (float)sampleRate;
            var sample = MathF.Sin(2.0f * MathF.PI * 880.0f * t) * MathF.Exp(-8.0f * t) * 0.5f;
            writer.Write((short)(sample * short.MaxValue));
        }
    }
}
