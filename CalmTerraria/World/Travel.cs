using CalmTerraria.Scan;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace CalmTerraria.World;

/// <summary>Putting the character somewhere at once: a power, used by
/// the waypoints and the scanner. The game's own teleport, so a server
/// is told the way a Rod of Discord tells it.
///
/// The character is never put inside the ground or left in mid-air.
/// It is stood on the thing, if there is room to stand on it - so
/// that an item on the ground is picked up - and otherwise on the
/// nearest footing, however far that is, with how far the thing then
/// is said. On a server, a place this computer has not been sent yet
/// is empty until it arrives: the character is held still there, and
/// stood properly once it has.</summary>
internal static class Travel
{
    /// <summary>How far round the tile a place to stand is looked for.</summary>
    private const int Search = 40;

    private static (int X, int Y)? _arriving;
    private static int _arrivingFrames;
    private static Vector2 _held;
    private static Action<string>? _say;
    private static Func<(int X, int Y), bool>? _allowed;

    /// <summary>Go to a tile. What to say about where that left the
    /// character relative to it, or null when they are on it.</summary>
    /// <param name="allowed">Which places to stand will do, by the tile
    /// the feet are on, where not every one will: a job that is about
    /// to put blocks where the character would be.</param>
    public static string? To(int x, int y, Action<string> say, Func<(int X, int Y), bool>? allowed = null)
    {
        _say = say;
        _allowed = allowed;
        if (Main.netMode == NetmodeID.MultiplayerClient && !Loaded(x, y))
        {
            // Not sent yet: go, hold still, and stand properly when
            // the ground has arrived.
            Move(new Vector2(x * 16f, y * 16f - 32f));
            _arriving = (x, y);
            _arrivingFrames = 0;
            _held = Main.LocalPlayer.position;
            return null;
        }
        return Stand(x, y);
    }

    /// <summary>What is said when the character could not be moved.</summary>
    public const string Nowhere = "Can't teleport: nowhere to stand near it.";

    private static string? Stand(int x, int y)
    {
        var player = Main.LocalPlayer;
        if (Footing(x, y) is not { } spot)
            return Nowhere;
        // Feet on the bottom of that tile, body across it and the next.
        Move(new Vector2(spot.X * 16f + 16f - player.width / 2f, (spot.Y + 1) * 16f - player.height));
        if (Touches(spot, x, y))
            return null;
        // From where the character now is, as K would give it.
        var (px, py) = WorldScanner.PlayerTile();
        return Directions.Offset(x - px, y - py);
    }

    private static void Move(Vector2 to)
    {
        var player = Main.LocalPlayer;
        player.Teleport(to, 1);
        player.velocity = Vector2.Zero;
        player.fallStart = (int)(to.Y / 16f);
        if (Main.netMode == NetmodeID.MultiplayerClient)
            NetMessage.SendData(MessageID.TeleportEntity, -1, -1, null, 0, player.whoAmI, to.X, to.Y, 1);
    }

    /// <summary>Once a frame in a world: waiting for the ground.</summary>
    public static void Update()
    {
        if (_arriving is not { } goal)
            return;
        var player = Main.LocalPlayer;
        if (Loaded(goal.X, goal.Y) || ++_arrivingFrames > 600)
        {
            _arriving = null;
            if (Stand(goal.X, goal.Y) is { } said)
                _say?.Invoke(said);
            return;
        }
        player.position = _held;
        player.velocity = Vector2.Zero;
        player.fallStart = (int)(_held.Y / 16f);
    }

    private static bool Loaded(int x, int y) =>
        Main.sectionManager == null || Main.sectionManager.TilesLoaded(x - 2, y - 4, x + 2, y + 4);

    /// <summary>Standing there, the body is over the tile: an item on
    /// it is in reach of the hands.</summary>
    private static bool Touches((int X, int Y) feet, int x, int y) =>
        x >= feet.X - 1 && x <= feet.X + 2 && y >= feet.Y - 3 && y <= feet.Y + 1;

    /// <summary>A tile with room for a body above it and something to
    /// stand on below: one where the body is over the target, if
    /// there is one, and otherwise the nearest.</summary>
    private static (int X, int Y)? Footing(int x, int y)
    {
        (int X, int Y)? best = null;
        var bestScore = long.MaxValue;
        for (var dy = -Search; dy <= Search; dy++)
        {
            for (var dx = -Search; dx <= Search; dx++)
            {
                var spot = (X: x + dx, Y: y + dy);
                long score = dx * dx + dy * dy + (Touches(spot, x, y) ? 0 : 1_000_000);
                if (score >= bestScore || !Stands(spot.X, spot.Y) || _allowed?.Invoke(spot) == false)
                    continue;
                best = spot;
                bestScore = score;
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
                if (WorldGen.SolidTile(column, row) || Main.tile[column, row] is { LiquidAmount: > 0, LiquidType: LiquidID.Lava })
                    return false;
        return Floor(x, y + 1) || Floor(x + 1, y + 1);
    }

    private static bool Floor(int x, int y)
    {
        var tile = Main.tile[x, y];
        return tile.HasTile && !tile.IsActuated && (Main.tileSolid[tile.TileType] || Main.tileSolidTop[tile.TileType]);
    }
}
