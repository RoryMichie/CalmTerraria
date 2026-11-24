using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace CalmTerraria.World;

/// <summary>Putting the character somewhere at once: a power, used by
/// the waypoints and the scanner. The game's own teleport, so a server
/// is told the way a Rod of Discord tells it.</summary>
internal static class Travel
{
    /// <summary>How far round the tile a place to stand is looked for.</summary>
    private const int Search = 8;

    /// <summary>Stand the local player at, or as near as there is
    /// footing to, a tile: a chest is in the ground's way and a vein
    /// of ore is inside the rock.</summary>
    public static void To(int x, int y)
    {
        var player = Main.LocalPlayer;
        var (sx, sy) = Footing(x, y) ?? (x, y);
        // Feet on the bottom of that tile, body across it and the next.
        var to = new Vector2(sx * 16f + 16f - player.width / 2f, (sy + 1) * 16f - player.height);
        player.Teleport(to, 1);
        player.velocity = Vector2.Zero;
        if (Main.netMode == NetmodeID.MultiplayerClient)
            NetMessage.SendData(MessageID.TeleportEntity, -1, -1, null, 0, player.whoAmI, to.X, to.Y, 1);
    }

    /// <summary>The nearest tile with room for a body above it and
    /// something to stand on below.</summary>
    private static (int X, int Y)? Footing(int x, int y)
    {
        (int X, int Y)? best = null;
        var bestDistance = int.MaxValue;
        for (var dy = -Search; dy <= Search; dy++)
        {
            for (var dx = -Search; dx <= Search; dx++)
            {
                var distance = dx * dx + dy * dy;
                if (distance >= bestDistance || !Stands(x + dx, y + dy))
                    continue;
                best = (x + dx, y + dy);
                bestDistance = distance;
            }
        }
        return best;
    }

    private static bool Stands(int x, int y)
    {
        if (!WorldGen.InWorld(x, y - 2, 2) || !WorldGen.InWorld(x + 1, y + 1, 2))
            return false;
        for (var column = x; column <= x + 1; column++)
            for (var row = y - 2; row <= y; row++)
                if (WorldGen.SolidTile(column, row))
                    return false;
        return Floor(x, y + 1) || Floor(x + 1, y + 1);
    }

    private static bool Floor(int x, int y)
    {
        var tile = Main.tile[x, y];
        return tile.HasTile && !tile.IsActuated && (Main.tileSolid[tile.TileType] || Main.tileSolidTop[tile.TileType]);
    }
}
