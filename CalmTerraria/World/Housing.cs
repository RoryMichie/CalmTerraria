using CalmTerraria.Scan;
using Terraria;
using Terraria.Enums;
using Terraria.ID;

namespace CalmTerraria.World;

/// <summary>Why a room is not a home, where the game does not say or
/// says too little. The game's own check is run, and its findings read:
/// how big it found the room, where it found the gap, and - when the
/// room is whole and furnished and still refused - why nobody could
/// stand in it. That last is the one the game calls only "not valid
/// housing": someone moving in needs a solid floor tile to stand on
/// with solid floor either side, and that place is marked down for
/// everything near it. A door within two tiles costs more than a
/// small room has to spend.</summary>
internal static class Housing
{
    /// <summary>What is wrong with the room at a tile, or null when
    /// the game's own answer is the one to give.</summary>
    /// <param name="gap">Where the hole in the room is, when that is
    /// what is wrong.</param>
    public static string? Diagnose(int x, int y, out (int X, int Y)? gap)
    {
        gap = null;
        if (!WorldGen.InWorld(x, y, 12))
            return null;
        if (!WorldGen.StartRoomCheck(x, y))
        {
            switch (WorldGen.roomCheckFailureReason)
            {
                case TownNPCRoomCheckFailureReason.RoomCheckStartedInASolidTile:
                    return "The cursor is on a solid tile. Put it on an empty tile inside the room.";
                case TownNPCRoomCheckFailureReason.RoomIsTooSmall:
                    return $"Too small: {WorldGen.numRoomTiles} tiles counting its walls, floor and ceiling, and it needs 60.";
                case TownNPCRoomCheckFailureReason.RoomIsTooBig:
                    return "Too big: more than 750 tiles. It may be open to another room or a cave through a missing block.";
                case TownNPCRoomCheckFailureReason.HoleInWallIsTooBig when WorldGen.numRoomTiles > 0:
                    // The tile the game was looking at when it gave up.
                    var at = (X: WorldGen.roomX[WorldGen.numRoomTiles - 1], Y: WorldGen.roomY[WorldGen.numRoomTiles - 1]);
                    gap = at;
                    return $"Not closed in: a gap {Directions.Offset(at.X - x, at.Y - y)} of the cursor. "
                        + "It needs a block there, or wall behind it. Cursor moved to it.";
                default:
                    return null;
            }
        }
        // Furniture missing: the game names it.
        if (!WorldGen.RoomNeeds(WorldGen.prioritizedTownNPCType))
            return null;
        WorldGen.ScoreRoom(-1, WorldGen.prioritizedTownNPCType);
        // Fit to live in, or somebody's already: the game says.
        if (WorldGen.hiScore != 0)
            return null;
        return Standing(x, y);
    }

    private static bool Solid(int x, int y) =>
        Main.tile[x, y] is { HasTile: true } tile && !tile.IsActuated && Main.tileSolid[tile.TileType];

    /// <summary>The room is whole and has what it needs, and nobody
    /// can stand in it: why. The places and the marking are the
    /// game's own, counted here so that the reason can be said.</summary>
    private static string? Standing(int x, int y)
    {
        int left = WorldGen.roomX1, right = WorldGen.roomX2, top = WorldGen.roomY1, bottom = WorldGen.roomY2;
        var inside = right - left + 1;
        if (inside < 3)
            return $"Too narrow: {inside} wide inside. Someone needs a floor tile to stand on that is not against a wall, so 3 wide at the least.";
        var best = int.MinValue;
        var why = new List<string>();
        var where = (X: 0, Y: 0);
        bool platforms = false, blocked = false;
        for (var i = left + 1; i < right; i++)
        {
            for (var j = top + 2; j < bottom + 2; j++)
            {
                if (Main.tile[i, j] is not { HasTile: true } floor || floor.IsActuated)
                    continue;
                if (Main.tileSolidTop[floor.TileType])
                {
                    platforms = true;
                    continue;
                }
                if (!Main.tileSolid[floor.TileType] || !Solid(i - 1, j) || !Solid(i + 1, j))
                    continue;
                if (Collision.SolidTiles(i - 1, i + 1, j - 3, j - 1))
                {
                    blocked = true;
                    continue;
                }
                int doors = 0, chests = 0, onIt = 0, walls = 0, things = 0;
                for (var k = i - 2; k <= i + 2; k++)
                {
                    for (var l = j - 4; l < j; l++)
                    {
                        if (Main.tile[k, l] is not { HasTile: true } tile || tile.IsActuated || TileID.Sets.IgnoredInHouseScore[tile.TileType])
                            continue;
                        var type = tile.TileType;
                        var openDoor = WorldGen.IsOpenDoorAnchorFrame(k, l);
                        if (type == TileID.OpenDoor && !openDoor)
                            continue;
                        if (k == i)
                            onIt++;
                        else if (TileID.Sets.BasicChest[type])
                            chests++;
                        else if (type is TileID.ClosedDoor or TileID.TallGateClosed or TileID.TallGateOpen || openDoor)
                            doors++;
                        else if (Main.tileSolid[type])
                            walls++;
                        else
                            things++;
                    }
                }
                var score = 50 - 20 * doors - 5 * walls + 5 * things;
                if (score > 0 && chests > 0)
                    score = Math.Max(1, score - 30 * chests);
                if (score > 0 && onIt > 0)
                    score = Math.Max(0, score - 15 * onIt);
                if (score <= best)
                    continue;
                best = score;
                where = (i, j - 1);
                why.Clear();
                if (doors > 0)
                    why.Add("a door within 2 tiles of it");
                if (onIt > 0)
                    why.Add("furniture standing on it");
                if (walls >= 8)
                    why.Add("walls close on both sides");
            }
        }
        if (best == int.MinValue)
        {
            return platforms
                ? "Nowhere to stand: the floor is platforms. Someone needs three solid blocks in a row to stand on."
                : blocked
                    ? "Nowhere to stand: every part of the floor has a block within three tiles above it."
                    : "Nowhere to stand: it needs three solid floor blocks in a row, with three clear tiles above them.";
        }
        // The count here says it is fine: the reason is another, and the game's.
        if (best > 0 || why.Count == 0)
            return null;
        return $"Nowhere good to stand. The best place is {Directions.Offset(where.X - x, where.Y - y)} of the cursor, "
            + $"and it has {string.Join(" and ", why)}. "
            + "Someone needs a floor tile with nothing on it, three or more tiles from any door.";
    }
}
