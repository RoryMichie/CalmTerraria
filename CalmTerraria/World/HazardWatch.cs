using CalmTerraria.Host;
using CalmTerraria.Scan;
using CalmTerraria.Screens;
using Terraria;
using Terraria.ID;

namespace CalmTerraria.World;

/// <summary>What kills by being walked into, said before it is: lava,
/// spikes and a standing rolling cactus within a few tiles of the
/// character. Once per approach - said as it comes near, then not
/// again until the character has been clear of that kind for a while.
///
/// And what has come loose and is rolling: a boulder or a rolling
/// cactus, said once with where it is, the moment it starts. It is on
/// the character in about a second, so that is all the warning there
/// is time for; the sound of it coming is the flight sounds' business.</summary>
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
            return tile.TileType switch
            {
                TileID.Spikes or TileID.WoodenSpikes => "Spikes",
                TileID.RollingCactus => "Rolling cactus",
                _ => null,
            };
        return tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Lava ? "Lava" : null;
    }

    /// <summary>How far off something rolling is news.</summary>
    private const float RollingAcross = 45f, RollingUpDown = 25f;
    /// <summary>The game's way of moving a boulder.</summary>
    private const int BoulderStyle = 25;

    private readonly HashSet<int> _rolling = new();

    /// <summary>For autopilot scripts: how many were called out.</summary>
    public static int Rolled { get; private set; }

    private void Rolling()
    {
        var player = Main.LocalPlayer;
        var live = new HashSet<int>();
        foreach (var projectile in Main.ActiveProjectiles)
        {
            if (!projectile.hostile || projectile.damage <= 0 || projectile.aiStyle != BoulderStyle)
                continue;
            var offset = (projectile.Center - player.Center) / 16f;
            if (Math.Abs(offset.X) > RollingAcross || Math.Abs(offset.Y) > RollingUpDown)
                continue;
            live.Add(projectile.identity);
            if (!_rolling.Add(projectile.identity))
                continue;
            Rolled++;
            _host.App.Announce($"{projectile.Name} loose, {Directions.Offset((int)MathF.Round(offset.X), (int)MathF.Round(offset.Y))}.");
        }
        _rolling.IntersectWith(live);
    }

    private void Update()
    {
        _frame++;
        if (!Main.LocalPlayer.dead)
            Rolling();
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
