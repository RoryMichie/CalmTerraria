using CalmTerraria.Host;
using CalmTerraria.Scan;
using CalmTerraria.Screens;
using Srui;
using Terraria;

namespace CalmTerraria.World;

/// <summary>Z: where the character is. What a sighted player takes in
/// at a glance from the backdrop and the screen round them, in a few
/// words: the biome and the layer of the world, whether they are in a
/// room or under something, and what the ground does to the left and
/// to the right. Shift+Z is the character's coordinates.</summary>
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
        shell.WorldCommands[KeyCombo.WithShift(Key.Char('z'))] = () => _host.App.Announce(Whereabouts());
    }

    /// <summary>The character's tile, as the game numbers the world:
    /// X from the left edge, Y from the top.</summary>
    private static string Whereabouts()
    {
        var (px, py) = WorldScanner.PlayerTile();
        return $"X {px}, Y {py}.";
    }
    private string Describe()
    {
        var player = Main.LocalPlayer;
        var (px, py) = WorldScanner.PlayerTile();
        var parts = new List<string> { $"{Biome(player)}, {Layer(player)}." };
        var (shelter, enclosed) = Shelter(px, py, player.ZoneOverworldHeight || player.ZoneSkyHeight);
        parts.Add(shelter);
        // Indoors the walls are not the lie of the land.
        if (!enclosed)
            parts.Add(Ground(px, py));
        if (player.lavaWet)
            parts.Add("In lava.");
        else if (player.honeyWet)
            parts.Add("In honey.");
        else if (player.wet)
            parts.Add("In water.");
        return string.Join(" ", parts);
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
        player.ZoneSkyHeight ? "sky"
        : player.ZoneOverworldHeight ? "surface"
        : player.ZoneDirtLayerHeight ? "underground"
        : player.ZoneRockLayerHeight ? "caverns"
        : "underworld";

    private static bool Solid(int x, int y) =>
        Main.tile[x, y] is { HasTile: true } tile && !tile.IsActuated && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType];

    /// <summary>Indoors or out. A room is closed all round by solid
    /// tiles - a shut door counts, an open one does not - and is said
    /// with its size and what it is mostly built of. The game wants a
    /// wall behind every tile of a room for someone to live in it, so
    /// tiles without one are said; a room that has them all says
    /// nothing more. Short of a room: what is overhead, if anything.</summary>
    private static (string Text, bool Enclosed) Shelter(int px, int py, bool surface)
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
            var of = made.Count == 0 ? "" : ", " + made.OrderByDescending(pair => pair.Value).First().Key;
            var walls = bare == 0 ? "" : bare == inside.Count ? " No back wall." : $" {bare} tiles without back wall.";
            return ($"Room, {right - left + 1} wide, {bottom - top + 1} high{of}.{walls}", true);
        }
        for (var y = py - 2; y >= Math.Max(1, py - 40); y--)
        {
            if (Solid(px, y))
                return ($"{TileNames.At(px, y, Main.tile[px, y])} overhead, {py - 1 - y} up.", false);
        }
        return (surface ? "Open sky." : "Nothing close overhead.", false);
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

    /// <summary>What the ground does to each side, as it would be
    /// walked: how far it runs level from the feet, and what ends
    /// that - a step up, a step down, or no floor at all.</summary>
    private static string Ground(int px, int py)
    {
        if (Floor(px, py + 1) is not { } here)
            return "No ground underfoot.";
        string? Side(int direction)
        {
            var run = 0;
            int? floor = here;
            for (var step = 1; step <= Look; step++, run++)
            {
                floor = Floor(px + direction * step, here - 3);
                if (floor != here)
                    break;
            }
            // Level as far as is looked.
            if (floor == here)
                return null;
            var then = floor is not { } at ? "a drop" : at < here ? $"up {here - at}" : $"down {at - here}";
            return run == 0 ? then : $"flat {run}, then {then}";
        }
        var left = Side(-1);
        var right = Side(1);
        if (left == null && right == null)
            return "Flat both ways.";
        return $"Left: {left ?? "flat"}. Right: {right ?? "flat"}.";
    }
}