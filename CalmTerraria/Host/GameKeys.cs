using Srui;
using XnaKeys = Microsoft.Xna.Framework.Input.Keys;

namespace CalmTerraria.Host;

/// <summary>SRUI's keys as the game's, for hiding a claimed key from
/// the game's keyboard state. US positions.</summary>
internal static class GameKeys
{
    public static XnaKeys? Of(Key key)
    {
        if (key.IsChar(out var c))
        {
            if (c is >= 'a' and <= 'z')
                return XnaKeys.A + (c - 'a');
            if (c is >= '0' and <= '9')
                return XnaKeys.D0 + (c - '0');
            return c switch
            {
                '[' => XnaKeys.OemOpenBrackets,
                ']' => XnaKeys.OemCloseBrackets,
                ';' => XnaKeys.OemSemicolon,
                '\'' => XnaKeys.OemQuotes,
                ',' => XnaKeys.OemComma,
                '.' => XnaKeys.OemPeriod,
                '/' => XnaKeys.OemQuestion,
                '\\' => XnaKeys.OemPipe,
                '`' => XnaKeys.OemTilde,
                '-' => XnaKeys.OemMinus,
                '=' => XnaKeys.OemPlus,
                _ => null,
            };
        }
        for (byte n = 1; n <= 12; n++)
            if (key == Key.F(n))
                return XnaKeys.F1 + (n - 1);
        if (key == Key.Enter) return XnaKeys.Enter;
        if (key == Key.Escape) return XnaKeys.Escape;
        if (key == Key.Tab) return XnaKeys.Tab;
        if (key == Key.Space) return XnaKeys.Space;
        if (key == Key.Up) return XnaKeys.Up;
        if (key == Key.Down) return XnaKeys.Down;
        if (key == Key.Left) return XnaKeys.Left;
        if (key == Key.Right) return XnaKeys.Right;
        if (key == Key.Home) return XnaKeys.Home;
        if (key == Key.End) return XnaKeys.End;
        if (key == Key.PageUp) return XnaKeys.PageUp;
        if (key == Key.PageDown) return XnaKeys.PageDown;
        if (key == Key.Delete) return XnaKeys.Delete;
        if (key == Key.Backspace) return XnaKeys.Back;
        return null;
    }
}
