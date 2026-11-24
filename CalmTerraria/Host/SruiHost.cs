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
/// Escape that opens the game's menu.
///
/// Everything here runs on the game's main thread.</summary>
internal sealed class SruiHost : IDisposable
{
    private readonly ILog _log;
    private readonly SdlKeyboard _keyboard;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly HashSet<XnaKeys> _hiddenUntilReleased = new();
    private SpeechReader? _reader;

    public SruiApp App { get; } = SruiApp.Headless();

    public bool OwnsKeyboard { get; private set; }

    /// <summary>A key pressed while the game owns the keyboard: the
    /// seam for the commands that open a screen. The game sees the key
    /// too.</summary>
    public Action<KeyCombo>? KeyWhileGameOwns { get; set; }

    public SruiHost(ILog log)
    {
        _log = log;
        _keyboard = new SdlKeyboard();
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

    public string SpeechBackend => _reader?.Voice.BackendName ?? "none";

    public void TakeKeyboard() => OwnsKeyboard = true;

    public void ReleaseKeyboard()
    {
        if (!OwnsKeyboard)
            return;
        OwnsKeyboard = false;
        foreach (var key in Keyboard.GetState().GetPressedKeys())
            _hiddenUntilReleased.Add(key);
    }

    /// <summary>One frame. Call after the game has read the keyboard
    /// for this frame, so what it read can be replaced.</summary>
    public void Frame()
    {
        while (_keyboard.TryRead(out var ev))
            Dispatch(ev);
        App.TickAt((ulong)_clock.ElapsedMilliseconds);
        HideKeysFromGame();
    }

    private void Dispatch(in KeyboardEvent ev)
    {
        if (ev.Key is { Phase: KeyPhase.Release } release)
        {
            // Always delivered: the app pairs it with a press it saw
            // and ignores one it did not.
            App.HandleKey(release);
            App.DispatchEvents();
            return;
        }
        if (!OwnsKeyboard)
        {
            if (ev.Key is { Phase: KeyPhase.Press } press)
                KeyWhileGameOwns?.Invoke(KeyCombo.FromFlat(press.Key, press.Mods));
            return;
        }
        if (ev.Key is { } key)
        {
            App.Interrupt();
            App.HandleKey(key);
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
        if (_hiddenUntilReleased.Count == 0)
            return;
        var down = Main.keyState.GetPressedKeys();
        _hiddenUntilReleased.IntersectWith(down);
        if (_hiddenUntilReleased.Count > 0)
            Main.keyState = new KeyboardState(
                down.Where(key => !_hiddenUntilReleased.Contains(key)).ToArray());
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
