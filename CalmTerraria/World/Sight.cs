using Terraria;

namespace CalmTerraria.World;

/// <summary>What the character can see without being told to light a
/// torch. The cursor and the scanner know what is on the game's map,
/// and the game maps a tile once it has been lit on screen; a sighted
/// player keeps the dark lit without a thought, and a player who
/// cannot see the dark cannot. So the character is taken to carry a
/// light: what it would show is put on the map as they go, and stays
/// there, as what a torch has shown stays.
///
/// It spreads as light does. Through open space it reaches about
/// twelve tiles; solid ground stops it three times as fast, so rock is
/// seen about four tiles deep beside the character and only its face
/// at the far edge. A vein of ore is known when it is that near to
/// open space the light has reached - not through twelve tiles of
/// stone.</summary>
internal static class Sight
{
    /// <summary>How far the light goes through open space, in tiles.</summary>
    public const int Reach = 12;

    // In half tiles, so that a diagonal step can cost one and a half.
    private const int Budget = Reach * 2, Open = 2, OpenDiagonal = 3, SolidTimes = 3;
    private const int Side = Reach * 2 + 1;

    private static readonly int[,] Cost = new int[Side, Side];
    private static readonly Queue<(int X, int Y)> Queue = new();
    private static (int X, int Y) _at;
    private static uint _when;

    private static bool Blocks(int x, int y) =>
        Main.tile[x, y] is { HasTile: true } tile && !tile.IsActuated && Main.tileBlockLight[tile.TileType];

    /// <summary>Once a frame in a world. The work is done when the
    /// character has moved a tile, and twice a second besides, for
    /// what has been dug or built in the meantime.</summary>
    public static void Update()
    {
        if (TerrariaPowers.Omniscient || Main.LocalPlayer.dead)
            return;
        var at = WorldScanner.PlayerTile();
        if (at == _at && Main.GameUpdateCount - _when < 30)
            return;
        _at = at;
        _when = Main.GameUpdateCount;
        if (!WorldGen.InWorld(at.X, at.Y, Reach + 2))
            return;

        for (var i = 0; i < Side; i++)
            for (var j = 0; j < Side; j++)
                Cost[i, j] = int.MaxValue;
        Cost[Reach, Reach] = 0;
        Queue.Clear();
        Queue.Enqueue((Reach, Reach));
        while (Queue.TryDequeue(out var from))
        {
            var cost = Cost[from.X, from.Y];
            int wx = at.X + from.X - Reach, wy = at.Y + from.Y - Reach;
            Show(wx, wy);
            var open = !Blocks(wx, wy);
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    int nx = from.X + dx, ny = from.Y + dy;
                    if (nx < 0 || ny < 0 || nx >= Side || ny >= Side)
                        continue;
                    // The face of whatever the light falls on is seen,
                    // however little light is left.
                    if (open)
                        Show(wx + dx, wy + dy);
                    var step = dx != 0 && dy != 0 ? OpenDiagonal : Open;
                    if (Blocks(wx + dx, wy + dy))
                        step *= SolidTimes;
                    if (cost + step > Budget || cost + step >= Cost[nx, ny])
                        continue;
                    Cost[nx, ny] = cost + step;
                    Queue.Enqueue((nx, ny));
                }
            }
        }
    }

    private static void Show(int x, int y)
    {
        if (!Main.Map.IsRevealed(x, y))
            Main.Map.Update(x, y, byte.MaxValue);
    }
}
