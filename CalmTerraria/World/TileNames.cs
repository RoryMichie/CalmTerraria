using Terraria;
using Terraria.ID;
using Terraria.Map;

namespace CalmTerraria.World;

/// <summary>What a tile is called. The game has no one answer: the
/// map's legend names most things worth naming, and for the rest the
/// item that places the tile has a name.</summary>
internal static class TileNames
{
    private static Dictionary<(int Type, int Style), int>? _itemOfTile;
    private static Dictionary<int, int>? _itemOfWall;

    /// <param name="option">The map's variant of the tile, from
    /// <see cref="MapHelper.GetTileBaseOption"/>.</param>
    /// <param name="lookup">The map legend entry for the tile and
    /// option.</param>
    public static string Of(int type, int option, int lookup)
    {
        if (type is TileID.Pots or TileID.PotsEcho)
            return "Pot";
        var name = Lang.GetMapObjectName(lookup);
        if (!string.IsNullOrWhiteSpace(name))
            return name;
        if (ItemOfTile(type, option) is { } item)
            return Lang.GetItemNameValue(item);
        return TileID.Search.TryGetName(type, out var internalName) ? Spaced(internalName) : $"Tile {type}";
    }

    /// <summary>The name of the tile at a position, whatever it is.</summary>
    public static string At(int x, int y, Tile tile)
    {
        var option = 0;
        MapHelper.GetTileBaseOption(x, y, tile.TileType, tile, ref option);
        return Of(tile.TileType, option, MapHelper.TileToLookup(tile.TileType, option));
    }

    public static string OfWall(int wall)
    {
        _itemOfWall ??= ContentSamples.ItemsByType.Values
            .Where(item => item.createWall > 0)
            .GroupBy(item => item.createWall)
            .ToDictionary(group => group.Key, group => group.Min(item => item.type));
        if (_itemOfWall.TryGetValue(wall, out var item))
            return Lang.GetItemNameValue(item);
        return WallID.Search.TryGetName(wall, out var internalName) ? Spaced(internalName) + " wall" : "Wall";
    }

    private static int? ItemOfTile(int type, int style)
    {
        _itemOfTile ??= ContentSamples.ItemsByType.Values
            .Where(item => item.createTile >= 0)
            .GroupBy(item => (item.createTile, item.placeStyle))
            .ToDictionary(group => group.Key, group => group.Min(item => item.type));
        if (_itemOfTile.TryGetValue((type, style), out var exact))
            return exact;
        return _itemOfTile.TryGetValue((type, 0), out var plain) ? plain : null;
    }

    /// <summary>"LihzahrdBrick" as "Lihzahrd brick".</summary>
    private static string Spaced(string name)
    {
        var text = new System.Text.StringBuilder();
        foreach (var c in name)
        {
            if (char.IsUpper(c) && text.Length > 0)
                text.Append(' ').Append(char.ToLowerInvariant(c));
            else
                text.Append(c);
        }
        return text.ToString();
    }
}
