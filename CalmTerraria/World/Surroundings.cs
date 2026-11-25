using CalmTerraria.Host;
using CalmTerraria.Scan;
using CalmTerraria.Screens;
using Srui;
using Terraria;

namespace CalmTerraria.World;

/// <summary>Z: where the character is. What a sighted player takes in
/// at a glance from the backdrop and the screen round them: the
/// biome and how far it runs, the layer of the world, whether they
/// are indoors and in what, and how level the ground is to either
/// side. One line, the most telling thing first.</summary>
internal sealed class Surroundings
{
    /// <summary>How far to each side the lie of the ground is taken.</summary>
    private const int Look = 30;

    private readonly SruiHost _host;
    private readonly WorldScanner _scanner;

    public Surroundings(SruiHost host, Shell shell, WorldScanner scanner)
    {
        _host = host;
        _scanner = scanner;
        shell.WorldCommands[KeyCombo.Plain(Key.Char('z'))] = () => _host.App.Announce(Describe());
    }

    private string Describe()
    {
        var player = Main.LocalPlayer;
        var (px, py) = WorldScanner.PlayerTile();
        var parts = new List<string> { Biome(player) + ", " + Layer(player) };

        // How far the biome runs, where the scanner has it as a place.
        if (_scanner.PlaceAt(px, py) is { } place)
        {
            var (left, _, right, _) = place.Bounds;
            parts.Add($"{place.DisplayName} runs {Math.Max(0, px - left)} left and {Math.Max(0, right - px)} right");
        }

        var centre = px - Main.maxTilesX / 2;
        parts.Add(centre == 0 ? "at the world's centre" : $"{Math.Abs(centre)} {(centre < 0 ? "left" : "right")} of the world's centre");

        var shelter = Shelter(px, py);
        parts.Add(shelter);
        // Indoors the walls are not the lie of the land.
        if (!shelter.StartsWith("Enclosed", StringComparison.Ordinal) && Ground(px, py) is { } ground)
            parts.Add(ground);
        if (player.lavaWet)
            parts.Add("standing in lava");
        else if (player.honeyWet)
            parts.Add("standing in honey");
        else if (player.wet)
            parts.Add("standing in water");
        return string.Join(". ", parts.Select(part => char.ToUpperInvariant(part[0]) + part[1..])) + ".";
    }

    /// <summary>The most particular thing true of where they stand:
    /// a dungeon before a corruption, a corruption before a forest.</summary>
    private static string Biome(Player player)
    {
        if (player.ZoneDungeon) return "Dungeon";
        if (player.ZoneLihzhardTemple) return "Jungle temple";
        if (player.ZoneTowerSolar || player.ZoneTowerVortex || player.ZoneTowerNebula || player.ZoneTowerStardust) return "Celestial pillar";
        if (player.ZoneMeteor) return "Meteorite";
        if (player.ZoneShimmer) return "Aether";
        if (player.ZoneHive) return "Bee hive";
        if (player.ZoneGlowshroom) return "Glowing mushrooms";
        if (player.ZoneGraveyard) return "Graveyard";
        if (player.ZoneCorrupt) return "Corruption";
        if (player.ZoneCrimson) return "Crimson";
        if (player.ZoneHallow) return "Hallow";
        if (player.ZoneUndergroundDesert) return "Underground desert";
        if (player.ZoneGranite) return "Granite cave";
        if (player.ZoneMarble) return "Marble cave";
        if (player.ZoneGemCave) return "Gem cave";
        if (player.ZoneBeach) return "Ocean";
        if (player.ZoneJungle) return "Jungle";
        if (player.ZoneSnow) return "Snow";
        if (player.ZoneDesert) return "Desert";
        if (player.ZoneUnderworldHeight) return "Underworld";
        return player.ZoneOverworldHeight || player.ZoneSkyHeight ? "Forest" : "Plain ground";
    }

    private static string Layer(Player player) =>
        player.ZoneSkyHeight ? "in the sky"
        : player.ZoneOverworldHeight ? "on the surface"
        : player.ZoneDirtLayerHeight ? "underground"
        : player.ZoneRockLayerHeight ? "in the caverns"
        : "in the underworld";

    private static bool Solid(int x, int y) =>
        Main.tile[x, y] is { HasTile: true } tile && !tile.IsActuated && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType];

    /// <summary>Indoors or out. Enclosed is closed all round by
    /// solid tiles - a shut door counts, an open one does not - with
    /// its size and what it is mostly built of. Whether it has a wall
    /// behind it throughout, which the game wants of a room for
    /// someone to live in, is said too. Short of enclosed: whether
    /// there is anything overhead.</summary>
    private static string Shelter(int px, int py)
    {
        const int limit = 900, reach = 70;
        var inside = new HashSet<(int X, int Y)>();
        var made = new Dictionary<string, int>();
        var open = new Queue<(int X, int Y)>();
        var enclosed = WorldGen.InWorld(px, py, reach + 2) && !Solid(px, py);
        if (enclosed)
        {
            inside.Add((px, py));
            open.Enqueue((px, py));
        }
        while (enclosed && open.Count > 0)
        {
            var (x, y) = open.Dequeue();
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var next = (X: x + dx, Y: y + dy);
                if (Solid(next.X, next.Y))
                {
                    var name = TileNames.At(next.X, next.Y, Main.tile[next.X, next.Y]);
                    made[name] = made.GetValueOrDefault(name) + 1;
                    continue;
                }
                if (!inside.Add(next))
                    continue;
                // Too far or too much: this is the outdoors, or a cave.
                if (inside.Count > limit || Math.Abs(next.X - px) > reach || Math.Abs(next.Y - py) > reach)
                {
                    enclosed = false;
                    break;
                }
                open.Enqueue(next);
            }
        }
        if (enclosed)
        {
            int left = inside.Min(t => t.X), right = inside.Max(t => t.X), top = inside.Min(t => t.Y), bottom = inside.Max(t => t.Y);
            var bare = inside.Count(t => Main.tile[t.X, t.Y].WallType == 0);
            var of = made.Count == 0 ? "" : ", mostly " + string.Join(" and ", made.OrderByDescending(pair => pair.Value).Take(2).Select(pair => pair.Key));
            var walls = bare == 0 ? "walled behind throughout" : bare == inside.Count ? "no wall behind it" : $"{bare} of its {inside.Count} tiles have no wall behind";
            return $"Enclosed, {right - left + 1} wide and {bottom - top + 1} high{of}; {walls}";
        }
        for (var y = py - 2; y >= Math.Max(1, py - 40); y--)
        {
            if (Solid(px, y))
                return $"Not enclosed. {TileNames.At(px, y, Main.tile[px, y])} overhead, {py - 1 - y} above the head";
        }
        return "In the open, nothing overhead";
    }

    /// <summary>The first solid tile at or below a height in a column.</summary>
    private static int? Floor(int x, int from)
    {
        if (!WorldGen.InWorld(x, from, 2))
            return null;
        // Inside the ground at this height: the surface is above.
        var y = from;
        while (y > from - 12 && WorldGen.SolidTile(x, y))
            y--;
        if (y != from)
            return y + 1;
        for (; y <= from + 25 && y < Main.maxTilesY - 2; y++)
            if (WorldGen.SolidTile(x, y) || (Main.tile[x, y] is { HasTile: true } tile && Main.tileSolidTop[tile.TileType]))
                return y;
        return null;
    }

    /// <summary>How level the ground is: how far it runs flat each
    /// way from the feet, and how much it rises and falls beyond.</summary>
    private static string? Ground(int px, int py)
    {
        if (Floor(px, py + 1) is not { } here)
            return "No ground under the feet";
        int Flat(int direction)
        {
            var run = 0;
            for (var step = 1; step <= Look; step++, run++)
                if (Floor(px + direction * step, here - 3) != here)
                    break;
            return run;
        }
        int left = Flat(-1), right = Flat(1);
        int rise = 0, drop = 0, gaps = 0;
        for (var x = px - Look; x <= px + Look; x++)
        {
            if (Floor(x, here - 3) is { } floor)
            {
                rise = Math.Max(rise, here - floor);
                drop = Math.Max(drop, floor - here);
            }
            else
                gaps++;
        }
        var flat = left >= Look && right >= Look
            ? $"Ground flat for {Look} and more each way"
            : $"Ground flat {left} left and {right} right";
        if (rise == 0 && drop == 0 && gaps == 0)
            return flat;
        var beyond = new List<string>();
        if (rise > 0)
            beyond.Add($"rises {rise}");
        if (drop > 0)
            beyond.Add($"drops {drop}");
        if (gaps > 0)
            beyond.Add($"{gaps} columns with no floor");
        return $"{flat}; within {Look} it {string.Join(", ", beyond)}";
    }
}
