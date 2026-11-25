using Srui;

namespace CalmTerraria.Keys;

/// <summary>The classic key layout, for players who know the earlier
/// accessibility mod by hand: the cursor on O, K, L and semicolon, use
/// on I, interact on P.
///
/// The mod's commands are bound once, to the keys of its own layout.
/// The classic layout is a translation in front of that: the key that
/// was pressed is turned into the key of the mod's layout that does
/// the same thing, or into nothing where the classic layout leaves the
/// key to the game.</summary>
public static class Keymap
{
    /// <summary>Pressed in the classic layout, and what that is in the
    /// mod's own.</summary>
    private static readonly Dictionary<char, char> Classic = new()
    {
        // The cursor: an arrow cluster one key right of I J K L.
        ['o'] = 'i',
        ['k'] = 'j',
        ['l'] = 'm',
        [';'] = 'l',
        // What is under the cursor, with Shift and Control as on K.
        [','] = 'k',
        // The two mouse buttons.
        ['i'] = '[',
        ['p'] = ']',
    };

    /// <summary>The keys of the mod's own layout that the classic one
    /// does not use for the same thing, and are not given another
    /// meaning above: these go to the game. J is its quick mana.</summary>
    private const string LeftToTheGame = "ujnm";

    /// <summary>The key in the mod's own layout, or null when the
    /// classic layout leaves this key to the game.</summary>
    public static Key? FromClassic(Key key)
    {
        if (!key.IsChar(out var c))
            return key;
        c = char.ToLowerInvariant(c);
        if (Classic.TryGetValue(c, out var to))
            return Key.Char(to);
        return LeftToTheGame.Contains(c) ? null : key;
    }

    public static KeyCombo? FromClassic(KeyCombo combo) =>
        FromClassic(combo.Key) is { } key ? combo with { Key = key } : null;
}
