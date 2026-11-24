using Srui;

namespace CalmTerraria.Pack;

/// <summary>How many: the box that comes up when more than one of
/// something could be given, bought, sold, stored or taken. It opens
/// on the usual answer, selected, so Enter alone does the usual
/// thing and typing a number replaces it.</summary>
public static class Quantity
{
    /// <param name="usual">What the box starts on.</param>
    /// <param name="most">The most that could be asked for.</param>
    /// <param name="then">Called with the number, once it is a
    /// number in range. Escape calls nothing.</param>
    public static void Ask(SruiApp app, int usual, int most, Action<int> then)
    {
        if (most <= 1)
        {
            then(1);
            return;
        }
        var dialog = app.OpenDialog();
        var box = new EditBox(dialog, $"How many, up to {most}", Math.Clamp(usual, 1, most).ToString())
        {
            SelectAllOnFocus = true,
        };
        var ok = new Button(dialog, "OK");
        app.SetPrimary(ok);
        ok.Activated += () =>
        {
            if (!int.TryParse(box.Text.Trim(), out var count) || count < 1 || count > most)
            {
                app.Announce($"A number from 1 to {most}.");
                return;
            }
            dialog.Close();
            then(count);
        };
        dialog.AnnounceOpened();
    }

    /// <summary>Spread a count over an item's stacks, smallest first,
    /// moving each with <paramref name="move"/>, which says how many
    /// of what it was asked for it managed. The total moved.</summary>
    public static int Over(IReadOnlyList<PackStack> stacks, int count, Func<int, int, int> move)
    {
        var moved = 0;
        foreach (var stack in stacks.ToList())
        {
            if (moved >= count)
                break;
            var want = Math.Min(stack.Count, count - moved);
            var did = move(stack.Slot, want);
            moved += did;
            if (did < want)
                break;
        }
        return moved;
    }
}
