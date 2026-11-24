using Microsoft.Xna.Framework;
using Terraria;

namespace CalmTerraria.World;

/// <summary>What is at a tile, in a word or two: the thing a sighted
/// player would say they were pointing at. A creature before an item
/// on the ground, before the block or object, before liquid, before
/// the wall behind. A tile the map has not revealed is unknown.</summary>
internal static class TileReader
{
    public static string Describe(int x, int y)
    {
        if (!WorldGen.InWorld(x, y))
            return "Edge of the world";
        if (!TerrariaPowers.Knows(x, y))
            return "Unknown";
        var area = new Rectangle(x * 16, y * 16, 16, 16);
        foreach (var npc in Main.ActiveNPCs)
        {
            if (npc.Hitbox.Intersects(area))
                return npc.GivenOrTypeName;
        }
        foreach (var item in Main.ActiveItems)
        {
            if (item.Hitbox.Intersects(area))
                return item.stack > 1 ? $"{item.Name}, {item.stack}" : item.Name;
        }
        var tile = Main.tile[x, y];
        if (tile.HasTile)
            return TileNames.At(x, y, tile);
        if (tile.LiquidAmount > 0)
            return tile.LiquidType switch { 1 => "Lava", 2 => "Honey", 3 => "Shimmer", _ => "Water" };
        return tile.WallType > 0 ? TileNames.OfWall(tile.WallType) : "Air";
    }
}
