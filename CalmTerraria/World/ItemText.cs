using System.Text.RegularExpressions;
using Terraria;

namespace CalmTerraria.World;

/// <summary>The game's words about things, made fit to speak.</summary>
internal static partial class ItemText
{
    private const int MaxLines = 60;

    /// <summary>What the game's tooltip says about an item, as the
    /// game composes it - damage, defence, what it does - without the
    /// name, which the list has already said.</summary>
    public static string Describe(Item item)
    {
        if (item.IsAir)
            return "";
        var lines = new string[MaxLines];
        var names = new string[MaxLines];
        var prefix = new bool[MaxLines];
        var badPrefix = new bool[MaxLines];
        int yoyoLogo = -1, researchLine = -1, count = 1;
        Main.MouseText_DrawItemTooltip_GetLinesInfo(item, ref yoyoLogo, ref researchLine, item.knockBack,
            ref count, lines, prefix, badPrefix, names, out _);
        var said = new List<string>();
        for (var i = 1; i < count && i < MaxLines; i++)
        {
            if (i == yoyoLogo || i == researchLine)
                continue;
            var line = Clean(lines[i] ?? "");
            if (line.Length > 0)
                said.Add(line);
        }
        return string.Join(". ", said);
    }

    /// <summary>The game's text markup, taken out: coloured text
    /// keeps its text, an item tag becomes the item's name, pictures
    /// of buttons go.</summary>
    public static string Clean(string text) =>
        Tag().Replace(text, match =>
        {
            var body = match.Groups[2].Value;
            return match.Groups[1].Value.ToLowerInvariant() switch
            {
                "i" or "item" => int.TryParse(body, out var type) && type > 0 ? Lang.GetItemNameValue(type) : "",
                "g" or "glyph" => "",
                _ => body,
            };
        }).Trim();

    [GeneratedRegex(@"\[(\w+)(?:/[^:\]]*)?:([^\]]*)\]")]
    private static partial Regex Tag();

    /// <summary>A stack as a line of speech: its name, and how many
    /// when there is more than one.</summary>
    public static string Stack(Item item) =>
        item.IsAir ? "Empty" : item.stack > 1 ? $"{item.AffixName()}, {item.stack}" : item.AffixName();
}
