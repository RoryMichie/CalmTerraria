using Microsoft.Xna.Framework;
using Terraria;

namespace CalmTerraria.World;

/// <summary>What is at a tile, in a word or two: the thing a sighted
/// player would say they were pointing at. A creature before an item
/// on the ground, before the block or object, before liquid, before
/// the wall behind. A tile the map has not revealed is unknown.</summary>
internal static class TileReader
{
    /// <summary>The things a player summons that hang in the air and
    /// are opened like a chest: the money trough's flying piggy bank
    /// and the void lens.</summary>
    public static bool IsFloatingContainer(Projectile projectile) =>
        projectile.active && projectile.type is Terraria.ID.ProjectileID.FlyingPiggyBank or Terraria.ID.ProjectileID.VoidLens;

    /// <summary>A floating container touching an area of the world.</summary>
    public static Projectile? Floating(Rectangle area)
    {
        foreach (var projectile in Main.ActiveProjectiles)
        {
            if (IsFloatingContainer(projectile) && projectile.Hitbox.Intersects(area))
                return projectile;
        }
        return null;
    }

    public static string Describe(int x, int y)
    {
        if (!WorldGen.InWorld(x, y))
            return "Edge of the world";
        if (!TerrariaPowers.Knows(x, y))
            return "Unknown";
        var area = new Rectangle(x * 16, y * 16, 16, 16);
        // The character is in the world too, three tiles of them: a
        // cursor on them reads them, and then what they stand in.
        if (Body(area) is { } body)
        {
            var behind = Ground(x, y);
            return behind == "Air" ? body : $"{body}, {behind}";
        }
        foreach (var npc in Main.ActiveNPCs)
        {
            if (npc.Hitbox.Intersects(area))
                return npc.GivenOrTypeName;
        }
        if (Floating(area) is { } floating)
            return floating.Name;
        foreach (var item in Main.ActiveItems)
        {
            if (item.Hitbox.Intersects(area))
                // Lying loose, not placed: a work bench on the ground is
                // not a work bench standing there.
                return (item.stack > 1 ? $"{item.Name}, {item.stack}" : item.Name) + ", dropped";
        }
        return Ground(x, y);
    }

    /// <summary>What the world itself has at a tile: the block, the
    /// liquid, the wall behind, or air.</summary>
    private static string Ground(int x, int y)
    {
        var tile = Main.tile[x, y];
        if (tile.HasTile)
            return TileNames.At(x, y, tile);
        if (tile.LiquidAmount > 0)
            return tile.LiquidType switch { 1 => "Lava", 2 => "Honey", 3 => "Shimmer", _ => "Water" };
        return tile.WallType > 0 ? TileNames.OfWall(tile.WallType) : "Air";
    }

    /// <summary>Whose body is on a tile, and which part of it: the
    /// player's own head, body or legs, or another player by name. A
    /// body that only brushes the tile's edge does not count.</summary>
    private static string? Body(Rectangle area)
    {
        for (var i = 0; i < Main.maxPlayers; i++)
        {
            var player = Main.player[i];
            if (player is not { active: true } || player.dead)
                continue;
            var overlap = Rectangle.Intersect(player.Hitbox, area);
            if (overlap.Width < 6 || overlap.Height < 4)
                continue;
            var third = (overlap.Center.Y - player.Hitbox.Top) * 3 / Math.Max(1, player.Hitbox.Height);
            var part = third switch
            {
                <= 0 => "head",
                1 => "body",
                _ => "legs",
            };
            return i == Main.myPlayer ? $"Your {part}" : $"{player.name}'s {part}";
        }
        return null;
    }
}
