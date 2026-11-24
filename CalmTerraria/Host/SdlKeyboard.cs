using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using SDL2;
using Srui;

namespace CalmTerraria.Host;

/// <summary>What the keyboard did, in SRUI's terms. A key transition
/// carries the physical key and, on a press, the logical input its
/// keydown maps to; text carries the typed character.</summary>
internal readonly record struct KeyboardEvent(
    KeyInput? Key, InputEvent? Input, bool FocusLost = false);

/// <summary>The game's keyboard, read where SRUI's own host reads it:
/// SDL's event stream, which has what the game's polled keyboard state
/// throws away - auto-repeat, the order of presses within a frame, and
/// the text a key typed under the user's layout. The game owns the
/// window and pumps the events; an event watch sees each one as it is
/// queued. Events are translated on arrival and collected for the main
/// thread to drain once a frame.</summary>
internal sealed class SdlKeyboard : IDisposable
{
    private readonly ConcurrentQueue<KeyboardEvent> _events = new();
    /// <summary>Held in a field: SDL keeps only the function pointer,
    /// and a collected delegate is a crash on the next keypress.</summary>
    private readonly SDL.SDL_EventFilter _watch;
    private bool _modifiersHeld;
    /// <summary>The combo of a printable keydown whose character
    /// arrives as the next text event, so the TypeChar carries it.</summary>
    private (uint Key, Mods Mods)? _pendingTyped;
    private bool _watching;

    public unsafe SdlKeyboard()
    {
        _watch = OnEvent;
        SDL.SDL_AddEventWatch(_watch, IntPtr.Zero);
        _watching = true;
    }

    /// <summary>Must run before the mod's code is unloaded.</summary>
    public void Dispose()
    {
        if (!_watching)
            return;
        _watching = false;
        SDL.SDL_DelEventWatch(_watch, IntPtr.Zero);
    }

    public bool TryRead(out KeyboardEvent ev) => _events.TryDequeue(out ev);

    private unsafe int OnEvent(IntPtr userdata, IntPtr sdlEvent)
    {
        // Native code is the caller; nothing may escape.
        try
        {
            var ev = (SDL.SDL_Event*)sdlEvent;
            switch (ev->type)
            {
                case SDL.SDL_EventType.SDL_KEYDOWN:
                    KeyDown(ev->key.keysym.sym, ev->key.keysym.mod, ev->key.repeat != 0);
                    break;
                case SDL.SDL_EventType.SDL_KEYUP:
                    KeyUp(ev->key.keysym.sym, ev->key.keysym.mod);
                    break;
                case SDL.SDL_EventType.SDL_TEXTINPUT:
                    Text(Marshal.PtrToStringUTF8((IntPtr)ev->text.text));
                    break;
                case SDL.SDL_EventType.SDL_WINDOWEVENT
                    when ev->window.windowEvent == SDL.SDL_WindowEventID.SDL_WINDOWEVENT_FOCUS_LOST:
                    _pendingTyped = null;
                    _modifiersHeld = false;
                    _events.Enqueue(new KeyboardEvent(null, null, FocusLost: true));
                    break;
            }
        }
        catch
        {
        }
        return 1;
    }

    private void KeyDown(SDL.SDL_Keycode sym, SDL.SDL_Keymod keymod, bool repeat)
    {
        var mods = ModsOf(keymod);
        _modifiersHeld = (mods & (Mods.Ctrl | Mods.Alt | Mods.Win)) != 0;
        if (KeyOf(sym) is not { } key)
            return;
        var physical = new KeyInput(key.Code, mods, repeat ? KeyPhase.Repeat : KeyPhase.Press);
        if (IsModifier(key))
        {
            _events.Enqueue(new KeyboardEvent(physical, null));
            return;
        }
        var input = KeyCombo.FromFlat(key.Code, mods).ToInput();
        _pendingTyped = input is null && !_modifiersHeld && (key.IsChar(out _) || key == Key.Space)
            ? (key.Code, mods)
            : null;
        _events.Enqueue(new KeyboardEvent(physical, input));
    }

    private void KeyUp(SDL.SDL_Keycode sym, SDL.SDL_Keymod keymod)
    {
        var mods = ModsOf(keymod);
        _modifiersHeld = (mods & (Mods.Ctrl | Mods.Alt | Mods.Win)) != 0;
        if (KeyOf(sym) is { } key)
            _events.Enqueue(new KeyboardEvent(new KeyInput(key.Code, mods, KeyPhase.Release), null));
    }

    private void Text(string? text)
    {
        var pending = _pendingTyped;
        _pendingTyped = null;
        if (_modifiersHeld || string.IsNullOrEmpty(text))
            return;
        // Exactly one scalar, not a control character.
        var rune = Rune.GetRuneAt(text, 0);
        if (rune.Utf16SequenceLength != text.Length || Rune.IsControl(rune))
            return;
        _events.Enqueue(new KeyboardEvent(null, new InputEvent(
            InputKind.TypeChar, (uint)rune.Value, pending?.Key ?? 0, pending?.Mods ?? Mods.None)));
    }

    private static bool IsModifier(Key key) =>
        key == Key.Shift || key == Key.Ctrl || key == Key.Alt || key == Key.Win;

    private static Mods ModsOf(SDL.SDL_Keymod keymod) =>
        ((keymod & SDL.SDL_Keymod.KMOD_CTRL) != 0 ? Mods.Ctrl : Mods.None)
        | ((keymod & SDL.SDL_Keymod.KMOD_ALT) != 0 ? Mods.Alt : Mods.None)
        | ((keymod & SDL.SDL_Keymod.KMOD_SHIFT) != 0 ? Mods.Shift : Mods.None)
        | ((keymod & SDL.SDL_Keymod.KMOD_GUI) != 0 ? Mods.Win : Mods.None);

    private static Key? KeyOf(SDL.SDL_Keycode sym)
    {
        // Letters, digits, and punctuation are their ASCII codepoints.
        var code = (uint)sym;
        if (code is >= 'a' and <= 'z' or >= '0' and <= '9')
            return Key.Char((char)code);
        switch (code)
        {
            case '[' or ']' or ';' or '\'' or ',' or '.' or '/' or '\\' or '`' or '-' or '=':
                return Key.Char((char)code);
        }
        if (sym is >= SDL.SDL_Keycode.SDLK_F1 and <= SDL.SDL_Keycode.SDLK_F12)
            return Key.F((byte)(sym - SDL.SDL_Keycode.SDLK_F1 + 1));
        return sym switch
        {
            SDL.SDL_Keycode.SDLK_UP => Key.Up,
            SDL.SDL_Keycode.SDLK_DOWN => Key.Down,
            SDL.SDL_Keycode.SDLK_LEFT => Key.Left,
            SDL.SDL_Keycode.SDLK_RIGHT => Key.Right,
            SDL.SDL_Keycode.SDLK_HOME => Key.Home,
            SDL.SDL_Keycode.SDLK_END => Key.End,
            SDL.SDL_Keycode.SDLK_PAGEUP => Key.PageUp,
            SDL.SDL_Keycode.SDLK_PAGEDOWN => Key.PageDown,
            SDL.SDL_Keycode.SDLK_RETURN or SDL.SDL_Keycode.SDLK_KP_ENTER => Key.Enter,
            SDL.SDL_Keycode.SDLK_ESCAPE => Key.Escape,
            SDL.SDL_Keycode.SDLK_TAB => Key.Tab,
            SDL.SDL_Keycode.SDLK_SPACE => Key.Space,
            SDL.SDL_Keycode.SDLK_BACKSPACE => Key.Backspace,
            SDL.SDL_Keycode.SDLK_DELETE => Key.Delete,
            SDL.SDL_Keycode.SDLK_APPLICATION or SDL.SDL_Keycode.SDLK_MENU => Key.Applications,
            SDL.SDL_Keycode.SDLK_LSHIFT or SDL.SDL_Keycode.SDLK_RSHIFT => Key.Shift,
            SDL.SDL_Keycode.SDLK_LCTRL or SDL.SDL_Keycode.SDLK_RCTRL => Key.Ctrl,
            SDL.SDL_Keycode.SDLK_LALT or SDL.SDL_Keycode.SDLK_RALT => Key.Alt,
            SDL.SDL_Keycode.SDLK_LGUI or SDL.SDL_Keycode.SDLK_RGUI => Key.Win,
            _ => null,
        };
    }
}
