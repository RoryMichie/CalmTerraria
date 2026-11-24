using CalmTerraria.Host;
using CalmTerraria.Scan;
using CalmTerraria.Screens;
using Terraria;
using Terraria.ID;

namespace CalmTerraria.World;

/// <summary>What kills by being walked into, said before it is: lava
/// and spikes within a few tiles of the character. Once per approach -
/// said as it comes near, then not again until the character has been
/// clear of that kind for a while.</summary>
internal sealed class HazardWatch
{
    /// <summary>How far to each side, above and below is watched.</summary>
    private const int Across = 5, Above = 2, Below = 5;
    private const int EveryFrames = 6;
    /// <summary>Frames clear of a hazard before it is news again.</summary>
    private const int ForgetFrames = 120;

    private readonly SruiHost _host;
    private readonly Dictionary<string, int> _lastSeen = new();
    private int _frame;

    public HazardWatch(SruiHost host, Shell shell)
    {
        _host = host;
        shell.EnteredWorld += _lastSeen.Clear;
        shell.WorldFrame += Update;
    }

    private static string? Hazard(int x, int y)
    {
        if (!WorldGen.InWorld(x, y))
            return null;
        var tile = Main.tile[x, y];
        if (tile.HasTile)
            return tile.TileType is TileID.Spikes or TileID.WoodenSpikes ? "Spikes" : null;
        return tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Lava ? "Lava" : null;
    }

    private void Update()
    {
        _frame++;
        if (_frame % EveryFrames != 0 || _host.OwnsKeyboard || Main.LocalPlayer.dead)
            return;
        var (px, py) = WorldScanner.PlayerTile();
        var nearest = new Dictionary<string, (int Dx, int Dy)>();
        for (var dy = -Above; dy <= Below; dy++)
        {
            for (var dx = -Across; dx <= Across; dx++)
            {
                if (Hazard(px + dx, py + dy) is not { } name)
                    continue;
                if (!nearest.TryGetValue(name, out var best)
                    || Math.Abs(dx) + Math.Abs(dy) < Math.Abs(best.Dx) + Math.Abs(best.Dy))
                    nearest[name] = (dx, dy);
            }
        }
        foreach (var (name, at) in nearest)
        {
            var news = !_lastSeen.TryGetValue(name, out var seen) || _frame - seen > ForgetFrames;
            _lastSeen[name] = _frame;
            if (news)
                _host.App.Announce($"{name}, {Directions.Offset(at.Dx, at.Dy)}.");
        }
    }
}
