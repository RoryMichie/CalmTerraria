namespace CalmTerraria.Keys;

/// <summary>The game's own key bindings, where they fight this mod.
///
/// A key the mod uses is hidden from the game while the mod uses it,
/// so most clashes cost nothing. Three do: Shift and Control are not
/// hidden - the game needs them - and the game's defaults put auto
/// select and the smart cursor on them, so every Shift or Control
/// command of the mod's would also swap the held item or flip the
/// smart cursor. And an earlier accessibility mod moved the mouse
/// buttons onto letters, which leaves the game clicking when those
/// letters are typed. Quick mana is moved only because its key, J, is
/// the cursor's, and it would otherwise have no key at all.
///
/// So that nobody has to edit a settings file, these are put right
/// when the mod starts: each moved to a free key if its usual new one
/// is free, and otherwise simply taken off the clashing key.</summary>
public static class GameBindings
{
    /// <summary>The binding, the key it must come off, and the key it
    /// goes to if nothing else has it.</summary>
    private static readonly (string Binding, string Off, string To)[] Moves =
    {
        ("SmartSelect", "LeftShift", "F"),
        ("SmartCursor", "LeftControl", "V"),
        ("QuickMana", "J", "G"),
    };

    /// <summary>Put the bindings right, in place. What was changed,
    /// in words, for the log; empty when nothing needed changing.</summary>
    /// <param name="classic">The classic key layout is in use: J is
    /// not the cursor's, so quick mana stays on it.</param>
    public static List<string> Fix(IDictionary<string, List<string>> bindings, bool classic = false)
    {
        var changes = new List<string>();
        foreach (var (binding, off, to) in Moves)
        {
            if (classic && binding == "QuickMana")
                continue;
            if (!bindings.TryGetValue(binding, out var keys) || !keys.Contains(off))
                continue;
            keys.RemoveAll(key => key == off);
            var free = !bindings.Any(pair => pair.Key != binding && pair.Value.Contains(to));
            if (free && !keys.Contains(to))
                keys.Add(to);
            changes.Add(free ? $"{binding} moved from {off} to {to}" : $"{binding} taken off {off}");
        }
        foreach (var (binding, button) in new[] { ("MouseLeft", "Mouse1"), ("MouseRight", "Mouse2") })
        {
            if (!bindings.TryGetValue(binding, out var keys))
                bindings[binding] = keys = new List<string>();
            // Letters on a mouse button are the old mod's doing.
            var letters = keys.Where(key => !key.StartsWith("Mouse", StringComparison.Ordinal)).ToList();
            if (letters.Count == 0 && keys.Contains(button))
                continue;
            keys.RemoveAll(letters.Contains);
            if (!keys.Contains(button))
                keys.Add(button);
            changes.Add($"{binding} put back on {button}" + (letters.Count > 0 ? $", off {string.Join(" and ", letters)}" : ""));
        }
        return changes;
    }

    /// <summary>On changing to the classic layout: quick mana, if this
    /// mod's move put it on G, goes back to J, where nothing else has
    /// J. Changing back is <see cref="Fix"/>.</summary>
    public static List<string> ToClassic(IDictionary<string, List<string>> bindings)
    {
        var changes = new List<string>();
        if (bindings.TryGetValue("QuickMana", out var keys) && keys.Contains("G") && !keys.Contains("J")
            && !bindings.Any(pair => pair.Value.Contains("J")))
        {
            keys.RemoveAll(key => key == "G");
            keys.Add("J");
            changes.Add("QuickMana moved from G to J");
        }
        return changes;
    }
}
