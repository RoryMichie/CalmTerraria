using System.Diagnostics;
using log4net;
using Microsoft.Xna.Framework.Input;
using Srui;
using Terraria;
using XnaKeys = Microsoft.Xna.Framework.Input.Keys;

namespace CalmTerraria.Host;

/// <summary>A headless SRUI app living inside the game: the game owns
/// the window and the loop, and once a frame this pushes the keyboard
/// in, ticks the app, and lets a speech reader say what came out.
///
/// The keyboard has one owner at a time. While SRUI owns it the game
/// is shown an empty keyboard, so no key can reach both; when it is
/// handed back, the keys still down stay hidden from the game until
/// they come up, so the Escape that closed a screen is not also the
/// Escape that opens the game's menu. While the game owns it, a key
/// the mod's own commands claim is hidden from the game the same way.
///
/// Everything here runs on the game's main thread.</summary>
internal sealed class SruiHost : IDisposable
{
    private readonly ILog _log;
    private readonly SdlKeyboard _keyboard;
    private readonly Queue<KeyboardEvent> _injected = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly HashSet<XnaKeys> _hiddenUntilReleased = new();
    private SpeechReader? _reader;

    public SruiApp App { get; } = SruiApp.Headless();

    public bool OwnsKeyboard { get; private set; }

    /// <summary>A key pressed while the game owns the keyboard: the
    /// seam for the mod's commands in a world. True claims the key, and
    /// the game does not see it.</summary>
    public Func<KeyCombo, bool>? KeyWhileGameOwns { get; set; }

    /// <summary>A key released while the game owns the keyboard.</summary>
    public Action<Key>? KeyReleasedWhileGameOwns { get; set; }

    /// <summary>Every utterance, as it is spoken.</summary>
    public event Action<string>? Spoke;

    /// <summary>Keys the game is shown as held whatever the keyboard
    /// says, while it owns the keyboard. For the autopilot.</summary>
    public HashSet<XnaKeys> HeldForGame { get; } = new();

    /// <summary>Milliseconds since the host started.</summary>
    public ulong Now => (ulong)_clock.ElapsedMilliseconds;

    public SruiHost(ILog log)
    {
        _log = log;
        _keyboard = new SdlKeyboard();
        App.AddReader(new LogReader(log, text => Spoke?.Invoke(text)));
        try
        {
            // Prism wants to be created on the thread that speaks.
            _reader = new SpeechReader();
            App.AddReader(_reader);
            _log.Info($"Speech backend: {_reader.Voice.BackendName}");
        }
        catch (Exception e)
        {
            _log.Error("Speech is unavailable.", e);
        }
    }

    public void TakeKeyboard() => OwnsKeyboard = true;

    public void ReleaseKeyboard()
    {
        if (!OwnsKeyboard)
            return;
        OwnsKeyboard = false;
        foreach (var key in Keyboard.GetState().GetPressedKeys())
            _hiddenUntilReleased.Add(key);
    }

    /// <summary>Feeds an event in as though the keyboard had produced
    /// it. For the autopilot.</summary>
    public void Inject(in KeyboardEvent ev) => _injected.Enqueue(ev);

    /// <summary>One frame. Call after the game has read the keyboard
    /// for this frame, so what it read can be replaced.</summary>
    public void Frame()
    {
        while (_injected.TryDequeue(out var injected))
            Dispatch(injected);
        while (_keyboard.TryRead(out var ev))
            Dispatch(ev);
        App.TickAt(Now);
        HideKeysFromGame();
    }

    private void Dispatch(in KeyboardEvent ev)
    {
        if (ev.Key is { Phase: KeyPhase.Release } release)
        {
            // Always delivered: the app pairs it with a press it saw
            // and ignores one it did not.
            App.HandleKey(release);
            if (!OwnsKeyboard)
                KeyReleasedWhileGameOwns?.Invoke(new Key(release.Key));
            App.DispatchEvents();
            return;
        }
        if (!OwnsKeyboard)
        {
            if (ev.Key is { } pressed)
            {
                var combo = KeyCombo.FromFlat(pressed.Key, pressed.Mods);
                if (KeyWhileGameOwns?.Invoke(combo) == true)
                {
                    App.Interrupt();
                    if (GameKeys.Of(combo.Key) is { } key)
                        _hiddenUntilReleased.Add(key);
                    App.DispatchEvents();
                }
            }
            return;
        }
        if (ev.Key is { } physical)
        {
            App.Interrupt();
            App.HandleKey(physical);
        }
        if (ev.Input is { } input)
            App.HandleInput(input);
        // One tick per input, as SRUI's own loop ends them.
        App.DispatchEvents();
    }

    private void HideKeysFromGame()
    {
        if (OwnsKeyboard)
        {
            Main.keyState = new KeyboardState();
            return;
        }
        if (_hiddenUntilReleased.Count == 0 && HeldForGame.Count == 0)
            return;
        var down = Main.keyState.GetPressedKeys();
        _hiddenUntilReleased.IntersectWith(down);
        Main.keyState = new KeyboardState(down
            .Where(key => !_hiddenUntilReleased.Contains(key))
            .Concat(HeldForGame)
            .Distinct()
            .ToArray());
    }

    /// <summary>Stops listening to the keyboard. Safe from any thread,
    /// and must happen before the mod's code is unloaded; the rest of
    /// the teardown is <see cref="Dispose"/>, on the main thread.</summary>
    public void Detach() => _keyboard.Dispose();

    public void Dispose()
    {
        _keyboard.Dispose();
        App.Dispose();
        _reader?.Dispose();
        _reader = null;
    }
}
