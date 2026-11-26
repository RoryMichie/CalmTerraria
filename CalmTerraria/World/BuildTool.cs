using CalmTerraria.Host;
using CalmTerraria.Pack;
using CalmTerraria.Scan;
using CalmTerraria.Screens;
using Srui;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalmTerraria.World;

/// <summary>Work on an area. X marks one corner at the cursor, X again
/// the other, X a third time lets the area go. The pack screen then
/// leads with a Build list of what can be done to that area with what
/// is held: fill it, outline it, wall it, clear it, soak up or pour a
/// liquid. Each job says what it would take and give before it starts.
///
/// The work is done by playing the game: the held item is aimed at one
/// tile after another and used, as a hand on a mouse would. So it
/// costs the blocks, takes the time, and obeys what the tool can
/// break. What it does not obey is arm's length - while a job runs the
/// player reaches anything on screen - and when tiles are further than
/// that the job takes the character to them itself.</summary>
internal sealed class BuildTool : IBuild
{
    /// <summary>How far a job reaches beyond the game's own, in tiles.
    /// Never past the screen: the game aims where the mouse can be.</summary>
    public const int ReachX = 45, ReachY = 25;

    /// <summary>True while a job runs: the reach is lent, and the
    /// hand changing tools is not read out.</summary>
    public static bool Working { get; private set; }

    private enum Kind { Fill, Outline, Wall, Clear, Unwall, Soak, Pour }

    /// <param name="Then">The job that follows this one: building,
    /// after what was in its way has been cleared.</param>
    /// <param name="More">For work that makes more work - water poured
    /// on lava uncovers the lava beneath: called when the targets run
    /// out, for the next ones. Empty when there is nothing left.</param>
    private sealed record Job(Kind Kind, int Item, string Doing, string Failed, List<(int X, int Y)> Targets,
        Func<List<(int X, int Y)>>? More = null, Func<Job>? Then = null);

    private readonly SruiHost _host;
    private readonly Cursor _cursor;
    private readonly WorldScanner _scanner;
    private (int X, int Y)? _a, _b;
    /// <summary>What the marked area is, when it was marked from the
    /// scanner.</summary>
    private string? _markedName;
    private Feature? _markedFeature;
    private readonly Dictionary<string, Job> _offered = new();
    /// <summary>The list asks many times a frame; the world is looked
    /// over once a frame.</summary>
    private List<BuildOption>? _options;
    private ulong _optionsFrame;

    private delegate void Offering(string key, Job job, string name, string about);

    private Job? _job;
    private Queue<(int X, int Y)> _queue = new();
    private List<(int X, int Y)> _failed = new(), _far = new();
    private (int X, int Y)? _current;
    private int _budget, _wait, _rounds;
    private bool _progress, _smartCursorWas;
    private float _zoomWas = 1f;
    private (int X, int Y) _cursorWas;

    public BuildTool(SruiHost host, Shell shell, Cursor cursor, WorldScanner scanner)
    {
        _host = host;
        _cursor = cursor;
        _scanner = scanner;
        // Shift+X: the area is whatever the scanner is on - the whole
        // vein, the whole pool, the whole run of spikes.
        shell.WorldCommands[KeyCombo.WithShift(Key.Char('x'))] = () =>
        {
            if (_job != null)
            {
                Say("A job is running. X stops it.");
                return;
            }
            if (scanner.Selected is not { } feature)
            {
                Say("Nothing selected in the scanner.");
                return;
            }
            var (left, top, right, bottom) = feature.Bounds;
            _a = (left, top);
            _b = (right, bottom);
            _markedName = feature.DisplayName;
            _markedFeature = feature;
            Say($"Selected {feature.DisplayName}, {right - left + 1} by {bottom - top + 1}.");
        };
        shell.Build = this;
        shell.WorldCommands[KeyCombo.Plain(Key.Char('x'))] = Mark;
        shell.WorldFrame += Update;
        shell.EnteredWorld += () =>
        {
            _a = _b = null;
            _markedName = null;
            _markedFeature = null;
            End();
        };
        shell.LeftWorld += End;
    }

    private void Say(string text) => _host.App.Announce(text);

    private void Mark()
    {
        if (_job != null)
        {
            var left = _queue.Count + _failed.Count + _far.Count + _aside.Count + (_current == null ? 0 : 1);
            Shout();
            if (_fetching)
            {
                End();
                Say("Stopped fetching.");
                return;
            }
            End();
            Say($"Stopped, {left} left.");
            return;
        }
        _markedName = null;
        _markedFeature = null;
        if (_a == null)
        {
            _a = _cursor.Tile;
            Say("First corner.");
        }
        else if (_b == null)
        {
            _b = _cursor.Tile;
            var (left, top, right, bottom) = Area();
            Say($"Second corner, {right - left + 1} by {bottom - top + 1}.");
        }
        else
        {
            _a = _b = null;
            Say("Selection cleared.");
        }
    }

    private (int Left, int Top, int Right, int Bottom) Area()
    {
        var (a, b) = (_a!.Value, _b!.Value);
        return (Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }

    public string Title
    {
        get
        {
            if (_a == null || _b == null)
                return "Build";
            var (left, top, right, bottom) = Area();
            return $"Build, {right - left + 1} by {bottom - top + 1}";
        }
    }

    private static readonly string[] LiquidNames = { "water", "lava", "honey", "shimmer" };

    /// <summary>How much of what a job uses is carried, against what
    /// it needs. The job's name has already said what the thing is.</summary>
    private static string Supply(int needed, int type)
    {
        var have = Carried(type);
        return have >= needed ? $"You have {have}." : $"You have {have}, {needed - have} short.";
    }

    private static int Carried(int type)
    {
        var total = 0;
        var inventory = Main.LocalPlayer.inventory;
        for (var slot = 0; slot < 50; slot++)
            if (inventory[slot].type == type)
                total += inventory[slot].stack;
        return total;
    }

    public IReadOnlyList<BuildOption> Options()
    {
        if (_options == null || _optionsFrame != _host.Frames)
        {
            _options = Survey();
            _optionsFrame = _host.Frames;
        }
        return _options;
    }

    private List<BuildOption> Survey()
    {
        _offered.Clear();
        var options = new List<BuildOption>();
        if (_job != null)
            return options;
        OfferVein(Offer);
        OfferPath(Offer);
        if (_a == null || _b == null)
            return options;
        var (left, top, right, bottom) = Area();
        left = Math.Max(left, 1);
        top = Math.Max(top, 1);
        right = Math.Min(right, Main.maxTilesX - 2);
        bottom = Math.Min(bottom, Main.maxTilesY - 2);
        var player = Main.LocalPlayer;
        var held = player.HeldItem;

        // Building goes from the ground up, so each tile has something
        // under it; taking away goes from the top down.
        IEnumerable<(int X, int Y)> Up()
        {
            for (var y = bottom; y >= top; y--)
                for (var x = left; x <= right; x++)
                    yield return (x, y);
        }
        IEnumerable<(int X, int Y)> Down()
        {
            for (var y = top; y <= bottom; y++)
                for (var x = left; x <= right; x++)
                    yield return (x, y);
        }
        void Offer(string key, Job job, string name, string about)
        {
            _offered[key] = job;
            options.Add(new BuildOption(key, name, about.Trim()));
        }

        var all = Up().ToList();
        var lava = all.Count(t => Main.tile[t.X, t.Y] is { LiquidAmount: > 0 } tile && tile.LiquidType == LiquidID.Lava);

        if (!held.IsAir && held.createTile >= TileID.Dirt && (Main.tileSolid[held.createTile] || TileID.Sets.Platforms[held.createTile]))
        {
            var name = held.Name;
            var item = held.type;
            var rim = all.Where(t => t.X == left || t.X == right || t.Y == top || t.Y == bottom).ToList();
            // A job of building, over some of the area: what is in the
            // way and can be cut down or dug out - trees, plants, loose
            // things, not solid ground, which is as good as a wall, and
            // not chests - goes first, then the blocks go in.
            void Build(string key, Kind kind, List<(int X, int Y)> over, string doing, string done, string title)
            {
                var open = over.Where(t => Open(t.X, t.Y)).ToList();
                var inWay = over.Where(t => InWay(t.X, t.Y)).ToList();
                var needed = open.Count + inWay.Count;
                if (needed == 0)
                    return;
                var solid = over.Count - needed;
                var names = inWay.Select(t => Main.tileAxe[Main.tile[t.X, t.Y].TileType] ? "Tree" : TileNames.At(t.X, t.Y, Main.tile[t.X, t.Y]))
                    .Distinct().OrderBy(n => n).ToList();
                var plan = Supply(needed, item)
                    + (inWay.Count > 0 ? $" Clears first: {string.Join(", ", names)}." : "")
                    + (solid > 0 ? $" Skips {solid} solid tiles." : "")
                    + (lava > 0 ? $" {lava} tiles of lava." : "");
                Job Placing() => new Job(kind, item, doing, done, over.Where(t => Open(t.X, t.Y)).ToList());
                var job = inWay.Count == 0
                    ? Placing()
                    : new Job(Kind.Clear, 0, "Clearing the way,", "cleared",
                        inWay.Where(t => Main.tileAxe[Main.tile[t.X, t.Y].TileType]).OrderByDescending(t => t.Y)
                            .Concat(inWay.Where(t => !Main.tileAxe[Main.tile[t.X, t.Y].TileType])).ToList(),
                        Then: Placing);
                Offer(key, job, $"{title} with {name}, {needed} tiles", plan);
            }
            Build("fill", Kind.Fill, all, "Filling", "filled", "Fill");
            if (rim.Count < all.Count)
                Build("outline", Kind.Outline, rim, "Outlining", "placed", "Outline");
            OfferLevel(left, top, right, bottom, item, name, Offer);
        }

        if (!held.IsAir && held.createWall > 0)
        {
            var bare = all.Where(t => Main.tile[t.X, t.Y].WallType == 0).ToList();
            if (bare.Count > 0)
                Offer("wall", new Job(Kind.Wall, held.type, "Walling", "walled", bare),
                    $"Wall with {held.Name}, {bare.Count} tiles", Supply(bare.Count, held.type));
        }

        OfferClear(Down().ToList(), lava, Offer);

        if (!held.IsAir)
        {
            int? soaks = held.type switch
            {
                ItemID.EmptyBucket => -1,
                ItemID.SuperAbsorbantSponge => LiquidID.Water,
                ItemID.LavaAbsorbantSponge => LiquidID.Lava,
                _ => null,
            };
            if (soaks is { } liquid)
            {
                var wet = Down().Where(t => Main.tile[t.X, t.Y] is { LiquidAmount: > 0 } tile
                    && (liquid < 0 || tile.LiquidType == liquid)).ToList();
                if (wet.Count > 0)
                    Offer("soak", new Job(Kind.Soak, held.type, "Soaking up", "emptied", wet),
                        $"Soak up {(liquid < 0 ? "liquid" : LiquidNames[liquid])} with {held.Name}, {wet.Count} tiles",
                        held.type == ItemID.EmptyBucket ? Supply(wet.Count, held.type) : "");
            }
            if (held.type is ItemID.WaterBucket or ItemID.BottomlessBucket && lava > 0)
            {
                // The game will not pour water into lava, only into an
                // empty tile: so it goes in beside the lava, above it
                // for choice, and runs onto it.
                List<(int X, int Y)> Beside()
                {
                    var spots = new HashSet<(int X, int Y)>();
                    foreach (var (x, y) in Down())
                    {
                        if (!IsLava(x, y))
                            continue;
                        foreach (var spot in new[] { (x, y - 1), (x - 1, y), (x + 1, y) })
                        {
                            if (Pourable(spot.Item1, spot.Item2))
                            {
                                spots.Add(spot);
                                break;
                            }
                        }
                    }
                    return spots.OrderBy(spot => spot.Y).ThenBy(spot => spot.X).ToList();
                }
                var first = Beside();
                Offer("pour", new Job(Kind.Pour, held.type, "Pouring water at", "poured", first, Beside),
                    $"Pour water on lava, {lava} tiles of lava",
                    "Turns it to obsidian. "
                    + (first.Count == 0 ? "No open tile beside the lava. " : "")
                    + (held.type == ItemID.WaterBucket ? Supply(first.Count, held.type) : ""));
            }
        }
        return options;
    }

    /// <summary>Where a block can go: nothing there, or only what a
    /// block breaks as it is placed - grass, vines, pots.</summary>
    private static bool Open(int x, int y)
    {
        var tile = Main.tile[x, y];
        return !tile.HasTile || Main.tileCut[tile.TileType];
    }

    /// <summary>In the way of building and fit to clear: a tree, or
    /// something that is not solid ground. Not a chest.</summary>
    private static bool InWay(int x, int y)
    {
        var tile = Main.tile[x, y];
        if (!tile.HasTile || Main.tileCut[tile.TileType])
            return false;
        var type = tile.TileType;
        if (TileID.Sets.BasicChest[type] || TileID.Sets.BasicDresser[type] || Main.tileHammer[type])
            return false;
        return Main.tileAxe[type] || !Main.tileSolid[type] || Main.tileSolidTop[type];
    }

    private static bool IsLava(int x, int y) =>
        WorldGen.InWorld(x, y) && Main.tile[x, y] is { LiquidAmount: > 0 } tile && tile.LiquidType == LiquidID.Lava;

    /// <summary>The game's own rule for where a bucket of water may
    /// be emptied: nothing solid there and no liquid yet.</summary>
    private static bool Pourable(int x, int y)
    {
        if (!WorldGen.InWorld(x, y, 2))
            return false;
        var tile = Main.tile[x, y];
        return tile.LiquidAmount == 0
            && !(tile.HasTile && !tile.IsActuated && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType]);
    }

    /// <summary>Clearing: what would come out, and what stands in the way.</summary>
    /// <summary>The way to what the scanner is on, as a walkable
    /// cut through the ground: a stair, one tile along for each tile
    /// up or down - which a character climbs without jumping - and
    /// level once the height is right. Where the thing is straight
    /// above or below, the stair doubles back on itself, never a
    /// shaft to fall down.</summary>
    private static List<(int X, int Y)> Route((int X, int Y) from, (int X, int Y) to, int sway)
    {
        var steps = new List<(int X, int Y)>();
        var (x, y) = from;
        var away = 0;
        while ((x, y) != to && steps.Count < 2000)
        {
            int dx = to.X - x, dy = to.Y - y;
            if (dy == 0)
                x += Math.Sign(dx);
            else
            {
                if (away == 0 && dx == 0)
                    away = Math.Min(6, (Math.Abs(dy) + 1) / 2);
                if (away > 0)
                {
                    x += sway;
                    away--;
                }
                else
                    x += Math.Sign(dx);
                y += Math.Sign(dy);
            }
            steps.Add((x, y));
        }
        return steps;
    }

    /// <summary>How far under the line holes are filled.</summary>
    private const int LevelDepth = 8;

    /// <summary>Flat ground: everything in the marked area is cleared
    /// away, and every hole under it is filled with the held block,
    /// so that the bottom edge of the area is one level floor to walk
    /// and build on. For ground that has been dug to pieces.</summary>
    private void OfferLevel(int left, int top, int right, int bottom, int item, string name, Offering offer)
    {
        var player = Main.LocalPlayer;
        var above = new List<(int X, int Y)>();
        int kept = 0;
        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                var tile = Main.tile[x, y];
                if (!tile.HasTile)
                    continue;
                var type = tile.TileType;
                var cut = Main.tileAxe[type] ? Best(i => i.axe) >= 0 : Best(i => i.pick) >= 0 && player.HasEnoughPickPowerToHurtTile(x, y);
                if (TileID.Sets.BasicChest[type] || TileID.Sets.BasicDresser[type] || Main.tileHammer[type] || !cut)
                    kept++;
                else
                    above.Add((x, y));
            }
        }
        List<(int X, int Y)> Holes()
        {
            var holes = new List<(int X, int Y)>();
            // From the bottom of each column up, so each block has one under it.
            for (var y = Math.Min(bottom + LevelDepth, Main.maxTilesY - 3); y > bottom; y--)
                for (var x = left; x <= right; x++)
                    if (Open(x, y))
                        holes.Add((x, y));
            return holes;
        }
        var under = Holes();
        if (above.Count == 0 && under.Count == 0)
            return;
        var ordered = above.Where(t => Main.tileAxe[Main.tile[t.X, t.Y].TileType]).OrderByDescending(t => t.Y)
            .Concat(above.Where(t => !Main.tileAxe[Main.tile[t.X, t.Y].TileType])).ToList();
        Job Filling() => new Job(Kind.Fill, item, "Filling the ground,", "filled", Holes());
        var job = ordered.Count == 0 ? Filling() : new Job(Kind.Clear, 0, "Levelling: clearing", "cleared", ordered, Then: Filling);
        offer("level", job, $"Level with {name}, {right - left + 1} wide",
            $"Clears {above.Count} tiles, fills {under.Count} holes below. {Supply(under.Count, item)}"
            + (kept > 0 ? $" Leaves {kept} it can't clear." : ""));
    }

    /// <summary>The vein miner: for something marked from the scanner
    /// that is made of one kind of tile - ore, gems, a run of spikes -
    /// every tile of it that touches, and nothing round it. Mined as
    /// all clearing is, with the character's own pickaxe, tile by
    /// tile, so it is the same on a server as alone.</summary>
    private void OfferVein(Offering offer)
    {
        if (_markedFeature is not { } feature)
            return;
        var player = Main.LocalPlayer;
        var (px, py) = WorldScanner.PlayerTile();
        // What it is made of: the kind of tile under one of its points.
        int type = -1;
        var open = new Queue<(int X, int Y)>();
        var vein = new HashSet<(int X, int Y)>();
        var (left, top, right, bottom) = feature.Bounds;
        foreach (var point in feature.Points)
        {
            for (var x = point.X; x <= point.X + 1; x++)
            {
                for (var y = point.Y; y <= point.Y + 1; y++)
                {
                    if (!WorldGen.InWorld(x, y, 2) || !Main.tile[x, y].HasTile)
                        continue;
                    var here = Main.tile[x, y].TileType;
                    if (type < 0 && Main.tileSolid[here] && !Main.tileFrameImportant[here] && x >= left && x <= right && y >= top && y <= bottom
                        && TileNames.At(x, y, Main.tile[x, y]) == feature.Kind.Name)
                        type = here;
                    if (here == type && vein.Add((x, y)))
                        open.Enqueue((x, y));
                }
            }
        }
        if (type < 0)
            return;
        // Out from there to every tile of the same kind that touches.
        while (open.Count > 0 && vein.Count < 4000)
        {
            var (x, y) = open.Dequeue();
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    var next = (X: x + dx, Y: y + dy);
                    if (WorldGen.InWorld(next.X, next.Y, 2) && Main.tile[next.X, next.Y] is { HasTile: true } tile
                        && tile.TileType == type && vein.Add(next))
                        open.Enqueue(next);
                }
            }
        }
        var name = feature.DisplayName;
        var tooHard = Best(item => item.pick) < 0 ? vein.Count : vein.Count(t => !player.HasEnoughPickPowerToHurtTile(t.X, t.Y));
        // Nearest first, so what is in reach goes while the rest is walked to.
        var targets = vein.Where(t => player.HasEnoughPickPowerToHurtTile(t.X, t.Y))
            .OrderBy(t => Math.Abs(t.X - px) + Math.Abs(t.Y - py)).ToList();
        var nearest = vein.MinBy(t => Math.Abs(t.X - px) + Math.Abs(t.Y - py));
        var plan = $"Nearest {Directions.Offset(nearest.X - px, nearest.Y - py)}."
            + (Best(item => item.pick) < 0 ? " No pickaxe."
                : tooHard > 0 ? $" {tooHard} too hard for your pickaxe." : "");
        offer("mine", new Job(Kind.Clear, 0, "Mining", "mined", targets), $"Mine {name}, {targets.Count} tiles", plan);
    }

    private void OfferPath(Offering offer)
    {
        // To what is marked: the one tile, after a first X, or the
        // nearest tile of the area. Something from the scanner is
        // marked with Shift+X, and is then named.
        if (_a is not { } first)
            return;
        var (px, py) = WorldScanner.PlayerTile();
        var target = first;
        if (_b != null)
        {
            var (left, top, right, bottom) = Area();
            target = (Math.Clamp(px, left, right), Math.Clamp(py + 1, top, bottom));
        }
        var what = _markedName ?? (_b == null ? "the marked tile" : "the marked area");
        // From the feet, to standing where the thing is.
        var from = (X: px, Y: py + 1);
        var distance = Math.Abs(target.X - from.X) + Math.Abs(target.Y - from.Y);
        if (distance < 3 || distance > 800)
            return;
        var steps = Route(from, target, Main.LocalPlayer.direction);
        var tiles = new List<(int X, int Y)>();
        var seen = new HashSet<(int X, int Y)>();
        // Each step is the height of the character and one more, the
        // headroom a stair needs.
        foreach (var (x, y) in steps)
            for (var row = y - 3; row <= y; row++)
                if (WorldGen.InWorld(x, row, 2) && seen.Add((x, row)))
                    tiles.Add((x, row));
        var open = steps.Count(step => WorldGen.InWorld(step.X, step.Y + 1, 2)
            && !WorldGen.SolidTile(step.X, step.Y + 1) && !seen.Contains((step.X, step.Y + 1)));
        var lava = tiles.Count(t => IsLava(t.X, t.Y));
        var intro = $"{steps.Count} steps, {Directions.Offset(target.X - px, target.Y - py)}. "
            + (open > 0 ? $"{open} steps over a drop. " : "");
        OfferClear(tiles, lava, offer, $"Clear path to {what}", intro, keepOrder: true, walls: false);
    }

    private void OfferClear(List<(int X, int Y)> all, int lava, Offering offer,
        string? title = null, string? intro = null, bool keepOrder = false, bool walls = true)
    {
        var player = Main.LocalPlayer;
        var pick = Best(item => item.pick);
        var axe = Best(item => item.axe);
        var targets = new List<(int X, int Y)>();
        var yield = new Dictionary<string, int>();
        var objects = new HashSet<string>();
        int tooHard = 0, chests = 0, hammer = 0, trees = 0;
        foreach (var (x, y) in all)
        {
            var tile = Main.tile[x, y];
            if (!tile.HasTile)
                continue;
            var type = tile.TileType;
            if (TileID.Sets.BasicChest[type] || TileID.Sets.BasicDresser[type])
            {
                chests++;
                continue;
            }
            if (Main.tileHammer[type])
            {
                hammer++;
                continue;
            }
            if (Main.tileAxe[type])
            {
                if (axe < 0)
                {
                    trees++;
                    continue;
                }
            }
            else if (pick < 0 || !player.HasEnoughPickPowerToHurtTile(x, y))
            {
                tooHard++;
                continue;
            }
            targets.Add((x, y));
            var name = TileNames.At(x, y, tile);
            if (Main.tileFrameImportant[type])
                objects.Add(name);
            else
                yield[name] = yield.GetValueOrDefault(name) + 1;
        }
        if (targets.Count == 0 && tooHard + chests + hammer + trees == 0)
            return;
        var plan = new List<string>();
        if (yield.Count > 0)
            plan.Add("Gives " + string.Join(", ", yield.OrderByDescending(pair => pair.Value)
                .Select(pair => $"{pair.Value} {pair.Key}")) + ".");
        if (objects.Count > 0)
            plan.Add("Removes " + string.Join(", ", objects.OrderBy(name => name)) + ".");
        if (pick < 0)
            plan.Add("No pickaxe.");
        else if (tooHard > 0)
            plan.Add($"{tooHard} too hard for your pickaxe.");
        if (trees > 0)
            plan.Add("No axe for the trees.");
        if (chests > 0)
            plan.Add("Leaves the chest.");
        if (hammer > 0)
            plan.Add($"{hammer} need a hammer.");
        if (lava > 0)
            plan.Add($"{lava} tiles of lava.");
        if (targets.Count == 0)
            plan.Insert(0, "Nothing can be cleared.");
        // Trees first and from the bottom: one cut at the foot brings
        // down everything above it, which is then already done.
        // A path is cut in the order it is walked.
        var ordered = keepOrder ? targets
            : targets.Where(t => Main.tileAxe[Main.tile[t.X, t.Y].TileType]).OrderByDescending(t => t.Y)
                .Concat(targets.Where(t => !Main.tileAxe[Main.tile[t.X, t.Y].TileType])).ToList();
        offer(title == null ? "clear" : "path", new Job(Kind.Clear, 0, "Clearing", "cleared", ordered), $"{title ?? "Clear"}, {ordered.Count} tiles",
            (intro ?? "") + string.Join(" ", plan));
        if (!walls)
            return;

        // Walls come off with a hammer, from their open edge inwards.
        if (Best(item => item.hammer) >= 0)
        {
            var walled = all.Where(t => Main.tile[t.X, t.Y].WallType != 0).ToList();
            if (walled.Count > 0)
                offer("unwall", new Job(Kind.Unwall, 0, "Taking down", "taken down", walled), $"Remove walls, {walled.Count} tiles", "");
        }
    }

    /// <summary>The slot of the best tool by some measure, or -1.</summary>
    private static int Best(Func<Item, int> power)
    {
        var inventory = Main.LocalPlayer.inventory;
        var best = -1;
        for (var slot = 0; slot < 50; slot++)
            if (!inventory[slot].IsAir && power(inventory[slot]) > 0 && (best < 0 || power(inventory[slot]) > power(inventory[best])))
                best = slot;
        return best;
    }

    public void Start(string key)
    {
        // The job as the world is now, not as it was when last listed.
        _options = Survey();
        _optionsFrame = _host.Frames;
        if (!_offered.TryGetValue(key, out var job) || (job.Targets.Count == 0 && job.More == null))
        {
            Say("Nothing to do.");
            return;
        }
        _cursorWas = _cursor.Tile;
        _smartCursorWas = Main.SmartCursorWanted_Mouse;
        // Smart cursor chooses its own tile; this work chooses each one.
        Main.SmartCursorWanted_Mouse = false;
        // The reach is the screen, so the screen is made as much of
        // the world as it can be for as long as the work lasts.
        _zoomWas = Main.GameZoomTarget;
        Main.GameZoomTarget = 1f;
        Working = true;
        _dug = null;
        _stoodBy = false;
        // Work that leaves nothing more to do there lets the area go
        // when it is done. Clearing, a path, taking walls down and
        // moving liquid are as often the first of two jobs.
        _letsGo = key is "fill" or "outline" or "level" or "wall" or "mine";
        Begin(job);
    }

    private void Begin(Job job)
    {
        _job = job;
        if (job.Kind == Kind.Clear && job.Targets.Count > 0)
        {
            var (l, t, r, b) = (job.Targets.Min(p => p.X) - 10, job.Targets.Min(p => p.Y) - 10,
                job.Targets.Max(p => p.X) + 10, job.Targets.Max(p => p.Y) + 12);
            _dug = _dug is { } had
                ? (Math.Min(had.Left, l), Math.Min(had.Top, t), Math.Max(had.Right, r), Math.Max(had.Bottom, b))
                : (l, t, r, b);
        }
        _queue = new Queue<(int X, int Y)>(job.Targets);
        _failed = new List<(int X, int Y)>();
        _far = new List<(int X, int Y)>();
        _aside = new List<(int X, int Y)>();
        _movedAside = false;
        _movedFor = null;
        _current = null;
        _progress = false;
        _wait = 0;
        _tally = _tallyAge = 0;
        _tallyName = "";
        _rounds = 0;
        _walk = Walk.No;
        _gaveUpWalking = false;
        Say($"{job.Doing} {job.Targets.Count} tiles.");
    }

    private void End()
    {
        if (_job == null)
            return;
        _job = null;
        _current = null;
        _walk = Walk.No;
        _fetching = false;
        WalkDirection = 0;
        WalkJump = false;
        Working = false;
        _cursor.Drive(null, false);
        _cursor.MoveSilently(_cursorWas.X, _cursorWas.Y);
        Main.SmartCursorWanted_Mouse = _smartCursorWas;
        // A tool the job took from the pack goes back there.
        Hand.Return();
        Main.GameZoomTarget = _zoomWas;
    }

    private bool Done((int X, int Y) at)
    {
        var tile = Main.tile[at.X, at.Y];
        return _job!.Kind switch
        {
            Kind.Fill or Kind.Outline => tile.HasTile && !Main.tileCut[tile.TileType],
            Kind.Wall => tile.WallType != 0,
            Kind.Clear => !tile.HasTile,
            Kind.Unwall => tile.WallType == 0,
            // A bucket cannot lift the last of a puddle.
            Kind.Soak => tile.LiquidAmount < (_job.Item == ItemID.EmptyBucket ? (byte)200 : (byte)1),
            // Poured, or the lava it was for has gone.
            Kind.Pour => tile.LiquidAmount > 0
                || !(IsLava(at.X, at.Y + 1) || IsLava(at.X - 1, at.Y) || IsLava(at.X + 1, at.Y)),
            _ => true,
        };
    }

    private static bool Reachable((int X, int Y) at)
    {
        if (!Main.LocalPlayer.IsInTileInteractionRange(at.X, at.Y, TileReachCheckSettings.Simple))
            return false;
        var world = new Microsoft.Xna.Framework.Vector2(at.X * 16f + 8f, at.Y * 16f + 8f);
        var screen = Microsoft.Xna.Framework.Vector2.Transform(world - Main.screenPosition, Main.GameViewMatrix.ZoomMatrix);
        return screen.X >= 16f && screen.Y >= 16f && screen.X < Main.screenWidth - 16f && screen.Y < Main.screenHeight - 16f;
    }

    /// <summary>The hand is still swinging the last tool: wait.</summary>
    private const string Switching = "";

    /// <summary>Put what this tile needs in the hand. Null when it is
    /// there, <see cref="Switching"/> while the last swing finishes,
    /// otherwise why the job cannot go on.</summary>
    private string? Equip((int X, int Y) at)
    {
        var player = Main.LocalPlayer;
        var inventory = player.inventory;
        int slot;
        if (_job!.Kind == Kind.Unwall)
        {
            slot = Best(item => item.hammer);
            if (slot < 0)
                return "No hammer";
        }
        else if (_job.Kind == Kind.Clear)
        {
            slot = Main.tileAxe[Main.tile[at.X, at.Y].TileType] ? Best(item => item.axe) : Best(item => item.pick);
            if (slot < 0)
                return Main.tileAxe[Main.tile[at.X, at.Y].TileType] ? "No axe" : "No pickaxe";
        }
        else
        {
            slot = inventory[player.selectedItem].type == _job.Item ? player.selectedItem : -1;
            for (var i = 0; i < 50 && slot < 0; i++)
                if (inventory[i].type == _job.Item)
                    slot = i;
            if (slot < 0)
                return $"Out of {Lang.GetItemNameValue(_job.Item)}";
        }
        if (slot == player.selectedItem)
            return null;
        // A tool cannot be changed mid-swing, and with the button
        // held the swinging never stops: the wrong tool would flail
        // at the tile to no effect. Button up until the hand is free.
        if (player.itemAnimation > 0)
            return Switching;
        Hand.Take(slot);
        return null;
    }

    private void Update()
    {
        if (_job == null)
            return;
        // A screen is up: the hands are busy elsewhere.
        if (_host.OwnsKeyboard)
        {
            _cursor.Drive(null, false);
            return;
        }
        var player = Main.LocalPlayer;
        if (player.dead)
        {
            End();
            return;
        }
        if (_tally > 0 && ++_tallyAge >= 240)
            Shout();
        KeepWalking();
        if (_fetching)
        {
            Fetch();
            return;
        }
        if (_current is { } now)
        {
            if (Done(now))
            {
                _progress = true;
                _gaveUpWalking = false;
                _movedAside = false;
                _current = null;
                Tally(_currentName);
            }
            else if (--_budget <= 0 || !Reachable(now))
            {
                (Reachable(now) ? _failed : _far).Add(now);
                _current = null;
            }
            else
            {
                var why = Equip(now);
                if (why == Switching)
                {
                    _budget++;
                    _cursor.Drive(now, false);
                    return;
                }
                if (why != null)
                {
                    Stop(why);
                    return;
                }
                // Let go for a moment now and then: some things are
                // used once per press.
                _cursor.Drive(now, _budget % 24 >= 2);
                return;
            }
        }
        while (_queue.Count > 0)
        {
            var next = _queue.Dequeue();
            if (Done(next))
                continue;
            if (!Reachable(next))
            {
                _far.Add(next);
                continue;
            }
            // What cannot go in yet is passed over at once, not tried
            // for half a second each: a tall outline is mostly tiles
            // waiting for the one below them.
            if (Placing && InTheWay(next))
            {
                _aside.Add(next);
                continue;
            }
            if (Placing && !Attaches(next))
            {
                _failed.Add(next);
                continue;
            }
            if (Equip(next) is { Length: > 0 } why)
            {
                _queue.Enqueue(next);
                Stop(why);
                return;
            }
            _current = next;
            _currentName = Name(next);
            _budget = _job.Kind switch
            {
                Kind.Clear or Kind.Unwall => 300,
                Kind.Soak or Kind.Pour => 48,
                _ => 30,
            };
            _cursor.Drive(next, false);
            return;
        }
        // The end of a pass over what was left.
        _cursor.Drive(null, false);
        if (_wait > 0)
        {
            _wait--;
            return;
        }
        if ((_failed.Count > 0 || _aside.Count > 0) && _progress)
        {
            // Something went in: what would not attach before may now.
            Requeue();
            _progress = false;
            return;
        }
        if (_aside.Count > 0)
        {
            // The game will not put a block where a character is. The
            // character is walked out of the way; where walking will
            // not do it - walled in, an edge, lava - they are stood on
            // the nearest ground that is clear of the work.
            if (_walk != Walk.No)
            {
            }
            else if (!_gaveUpWalking)
                StartWalking(Walk.Aside, _aside[0]);
            else if (!_movedAside)
            {
                _movedAside = true;
                var pending = new HashSet<(int X, int Y)>(_job.Targets.Where(t => !Done(t)));
                bool Clear((int X, int Y) feet)
                {
                    for (var column = feet.X; column <= feet.X + 1; column++)
                    for (var row = feet.Y - 2; row <= feet.Y; row++)
                        if (pending.Contains((column, row)))
                            return false;
                    return true;
                }
                var (px, py) = WorldScanner.PlayerTile();
                if (Travel.To(px, py, _ => { }, Clear) != Travel.Nowhere)
                    Say("Moved aside.");
            }
            else
            {
                // Nowhere to stand that is out of the way.
                _failed.AddRange(_aside);
                _aside = new List<(int X, int Y)>();
            }
            if (_aside.Count > 0)
            {
                Requeue();
                _wait = 10;
                return;
            }
        }
        if (_far.Count > 0)
        {
            var (px, py) = WorldScanner.PlayerTile();
            var nearest = _far.MinBy(t => Math.Abs(t.X - px) + Math.Abs(t.Y - py));
            _wait = 10;
            if (!_gaveUpWalking && _walk == Walk.No)
                StartWalking(Walk.Closer, nearest);
            // Walking is sideways and stops at an edge. What it cannot
            // get to, the character is put beside: on the thing where
            // there is room, on the nearest footing where there is not.
            if (_gaveUpWalking)
            {
                if (_movedFor == nearest || Travel.To(nearest.X, nearest.Y, _ => { }) == Travel.Nowhere)
                {
                    // Beside it, and it is still out of reach.
                    _far.Remove(nearest);
                    _failed.Add(nearest);
                }
                else
                {
                    Say("Moved closer.");
                    _wait = 30;
                }
                _movedFor = nearest;
                _gaveUpWalking = false;
            }
            _queue = new Queue<(int X, int Y)>(_far);
            _far = new List<(int X, int Y)>();
            return;
        }
        // Work that makes more work: give what was poured a moment to
        // run, then look again.
        if (_job.More != null && _failed.Count == 0 && _rounds++ < 60)
        {
            var more = _job.More();
            if (more.Count > 0)
            {
                _queue = new Queue<(int X, int Y)>(more);
                _wait = 45;
                return;
            }
        }
        if (_job.Then != null)
        {
            Shout();
            var next = _job.Then();
            if (next.Targets.Count > 0)
            {
                Begin(next);
                return;
            }
        }
        Shout();
        StandByWork();
        // What was dug or cut down lies where it fell, which with a
        // long reach is not where the character is: go and get it.
        if (_dug is { } dug)
        {
            _fetching = true;
            _fetchFrames = 0;
            (_fetchLeft, _fetchTop, _fetchRight, _fetchBottom) = dug;
            return;
        }
        Finish();
    }

    private void Finish(string? also = null)
    {
        var failed = _failed.Count;
        var done = _job!.Failed;
        // Where the work is from where the character has ended up, if
        // the job walked them any way off from it.
        string? where = null;
        if (_job.Targets.Count > 0)
        {
            var (px, py) = WorldScanner.PlayerTile();
            var nearest = _job.Targets.MinBy(t => Math.Abs(t.X - px) + Math.Abs(t.Y - py));
            if (Math.Abs(nearest.X - px) > 6 || Math.Abs(nearest.Y - py) > 8)
                where = $"It is {Directions.Offset(nearest.X - px, nearest.Y - py)}.";
            else if (_stoodBy)
                where = "Moved beside it.";
        }
        End();
        // Done with, and nothing left over to try again: the area is
        // let go, so the next X is a first corner.
        if (failed == 0 && _letsGo)
        {
            _a = _b = null;
            _markedName = null;
            _markedFeature = null;
        }
        Say((failed == 0 ? "Done." : $"Done. {failed} could not be {done}.")
            + (also == null ? "" : " " + also) + (where == null ? "" : " " + where));
    }

    private bool _letsGo, _stoodBy;

    /// <summary>The character ends beside what was done. A job reaches
    /// a long way and may have carried them about; when it is over they
    /// are stood by the nearest of the work - just outside it, where
    /// the work was building, and not shut in.</summary>
    private void StandByWork()
    {
        var targets = _job!.Targets;
        if (targets.Count == 0)
            return;
        var (px, py) = WorldScanner.PlayerTile();
        var nearest = targets.MinBy(t => Math.Abs(t.X - px) + Math.Abs(t.Y - py));
        if (Math.Abs(nearest.X - px) <= 6 && Math.Abs(nearest.Y - py) <= 8)
            return;
        Func<(int X, int Y), bool>? outside = null;
        if (_job.Kind is Kind.Fill or Kind.Outline)
        {
            var (left, top, right, bottom) = (targets.Min(t => t.X), targets.Min(t => t.Y), targets.Max(t => t.X), targets.Max(t => t.Y));
            // The body is two tiles across and three up from the feet.
            outside = feet => feet.X + 1 < left || feet.X > right || feet.Y < top || feet.Y - 2 > bottom;
        }
        if (Travel.To(nearest.X, nearest.Y, _ => { }, outside) != Travel.Nowhere)
            _stoodBy = true;
    }

    // ── Fetching ──
    /// <summary>For autopilot scripts: what the fetching went after.</summary>
    public static string Report { get; private set; } = "";

    private bool _fetching;
    /// <summary>Where this run of jobs has dug, for its drops.</summary>
    private (int Left, int Top, int Right, int Bottom)? _dug;
    private int _fetchFrames, _fetchLeft, _fetchRight, _fetchTop, _fetchBottom, _fetchType;
    private Item? _fetchItem;
    private readonly HashSet<Item> _fetchSkipped = new();

    /// <summary>Whether what is being fetched is still on the ground.
    /// A picked-up item is replaced in the game's list by a new,
    /// empty one; the old one goes on looking as it did.</summary>
    private bool StillLying() =>
        _fetchItem is { } item && ReferenceEquals(Main.item[item.whoAmI], item) && item.active && item.stack > 0;

    private bool Dropped(Item item)
    {
        var x = (int)(item.Center.X / 16f);
        var y = (int)(item.Center.Y / 16f);
        // Where the work was, or already brought to the character.
        return (x >= _fetchLeft && x <= _fetchRight && y >= _fetchTop && y <= _fetchBottom)
            || Microsoft.Xna.Framework.Vector2.Distance(item.Center, Main.LocalPlayer.Center) < 48f;
    }

    /// <summary>After clearing: what was dug or cut down is brought
    /// to the character, from wherever the work was. With a long
    /// reach that can be forty tiles off, down a hole or up a tree,
    /// and the pockmarked ground it has to be fetched over is the
    /// reason for clearing in the first place. A power, like the long
    /// reach itself. What the pack has no room for stays, and is
    /// counted.</summary>
    private void Fetch()
    {
        var player = Main.LocalPlayer;
        var lying = 0;
        foreach (var item in Main.ActiveItems)
        {
            if (!Dropped(item))
                continue;
            lying++;
            if (!player.ItemSpace(item).CanTakeItem)
                continue;
            item.Center = player.Center;
            item.velocity = Microsoft.Xna.Framework.Vector2.Zero;
            item.noGrabDelay = 0;
            if (Main.netMode == NetmodeID.MultiplayerClient)
                NetMessage.SendData(MessageID.SyncItem, -1, -1, null, item.whoAmI);
        }
        // A moment for the last of them to be taken up.
        if (lying > 0 && ++_fetchFrames < 45)
            return;
        _fetching = false;
        Finish(lying > 0 ? $"{lying} drops left where they are: no room for them." : null);
    }

    private const string Tree = "tree";
    private string _currentName = "";
    private string _tallyName = "";
    private int _tally, _tallyAge;

    /// <summary>What a tile is called in the running commentary,
    /// taken before the work changes it.</summary>
    private string Name((int X, int Y) at)
    {
        var tile = Main.tile[at.X, at.Y];
        return _job!.Kind switch
        {
            Kind.Clear => Main.tileAxe[tile.TileType] ? Tree : TileNames.At(at.X, at.Y, tile),
            Kind.Unwall => "walls",
            // What the job places, whatever is in the hand this instant.
            Kind.Wall or Kind.Fill or Kind.Outline => Lang.GetItemNameValue(_job.Item),
            Kind.Soak => $"tiles of {LiquidNames[Math.Clamp(tile.LiquidType, 0, LiquidNames.Length - 1)]}",
            Kind.Pour => "buckets of water",
            _ => "tiles",
        };
    }

    /// <summary>Progress reports. Work is said as it goes, a run of
    /// one thing at a time: when the thing changes, every few seconds
    /// of the same, and at the end.</summary>
    private void Tally(string name)
    {
        if (name != _tallyName)
            Shout();
        _tallyName = name;
        _tally++;
        // A tree is an event; nobody counts trees in runs.
        if (name == Tree)
            Shout();
    }

    private void Shout()
    {
        if (_tally > 0 && _job != null)
        {
            var name = _tallyName;
            Say(_job.Kind switch
            {
                Kind.Clear when name == Tree => "Tree felled.",
                Kind.Clear => $"Cleared {_tally} {name}.",
                Kind.Unwall => $"Removed {_tally} {name}.",
                Kind.Soak => $"Soaked up {_tally} {name}.",
                Kind.Pour => $"Poured {_tally} {name}.",
                _ => $"Placed {_tally} {name}.",
            });
        }
        _tally = 0;
        _tallyAge = 0;
    }

    private List<(int X, int Y)> _aside = new();
    /// <summary>Whether the character has been stood clear of the
    /// work since anything last went in, and the far tile they were
    /// last put beside.</summary>
    private bool _movedAside;
    private (int X, int Y)? _movedFor;

    // ── Walking ──
    // The job walks the character itself: out of the way of a block
    // that is to go where they stand, and nearer to what is out of
    // reach. It walks as a player does - left, right, a jump at a
    // step - and will not walk into lava or off a drop. Where it
    // cannot walk, the character is put there: the job never asks
    // the player to do its walking.

    private enum Walk { No, Aside, Closer, Fetch }

    private Walk _walk;
    private (int X, int Y) _walkGoal;
    private int _walkDirection, _stuckFrames, _jumpFrames, _hops, _walkFrames;
    private float _walkLastX;
    private bool _gaveUpWalking;

    /// <summary>What the job wants of the character's legs this
    /// frame, for the hook that turns keys into controls.</summary>
    public static int WalkDirection { get; private set; }
    public static bool WalkJump { get; private set; }

    public static void InjectMovement()
    {
        if (!Working || WalkDirection == 0)
            return;
        var controls = Terraria.GameInput.PlayerInput.Triggers.Current;
        if (WalkDirection < 0)
            controls.Left = true;
        else
            controls.Right = true;
        if (WalkJump)
            controls.Jump = true;
    }

    private void StartWalking(Walk why, (int X, int Y) goal)
    {
        var player = Main.LocalPlayer;
        var (px, _) = WorldScanner.PlayerTile();
        _walk = why;
        _walkGoal = goal;
        if (why == Walk.Aside)
        {
            // Out of the area by the nearer side, so as not to be
            // walled in by the work.
            var (left, _, right, _) = _a != null && _b != null ? Area() : (goal.X, 0, goal.X, 0);
            _walkDirection = px - left <= right - px ? -1 : 1;
        }
        else
        {
            var (_, py) = WorldScanner.PlayerTile();
            // Walking is sideways. A tile that is out of reach because
            // it is too far above or below - off the top of the screen,
            // on a tall job - is no nearer for any amount of it, and
            // walking towards its column only carries the character
            // off across the world.
            var visible = (int)(Main.screenHeight / 32f / Math.Max(0.1f, Main.GameViewMatrix.Zoom.Y)) - 2;
            if (Math.Abs(goal.X - px) <= 1 || Math.Abs(goal.Y - py) > Math.Min(visible, ReachY))
            {
                GiveUpWalking();
                return;
            }
            _walkDirection = Math.Sign(goal.X - px);
        }
        _stuckFrames = _jumpFrames = _hops = _walkFrames = 0;
        _walkLastX = player.position.X;
        // Said only when it cannot be done: walking that is happening
        // is heard in the footsteps, and needs no announcing.
    }

    private void GiveUpWalking()
    {
        _walk = Walk.No;
        _gaveUpWalking = true;
        WalkDirection = 0;
        WalkJump = false;
    }

    /// <summary>One frame of walking. The work goes on meanwhile.</summary>
    private void KeepWalking()
    {
        if (_walk == Walk.No)
        {
            WalkDirection = 0;
            WalkJump = false;
            return;
        }
        var player = Main.LocalPlayer;
        var (px, py) = WorldScanner.PlayerTile();
        var arrived = _walk switch
        {
            Walk.Aside => !_aside.Concat(_queue).Any(InTheWay) && !(_current is { } now && InTheWay(now)) && ++_walkFrames > 12,
            // Until it is in the pack, or gone some other way.
            _ => Reachable(_walkGoal),
        };
        if (arrived)
        {
            _walk = Walk.No;
            WalkDirection = 0;
            WalkJump = false;
            return;
        }
        // No further than it is any use to go: to the tile's own
        // column when walking closer, a few steps when stepping aside.
        _walkFrames++;
        var overshot = _walk == Walk.Closer
            ? Math.Abs(_walkGoal.X - px) <= 1 || Math.Sign(_walkGoal.X - px) != _walkDirection
            : _walkFrames > 400;
        if (overshot)
        {
            GiveUpWalking();
            return;
        }
        // Not into lava, and not off an edge with nothing under it.
        var ahead = px + _walkDirection;
        var lava = false;
        for (var row = py - 1; row <= py + 3 && !lava; row++)
            lava = IsLava(ahead, row) || IsLava(ahead + _walkDirection, row);
        var floor = false;
        for (var row = py + 1; row <= py + 8 && !floor; row++)
            floor = WorldGen.InWorld(ahead, row) && Main.tile[ahead, row] is { HasTile: true } tile
                && (Main.tileSolid[tile.TileType] || Main.tileSolidTop[tile.TileType]);
        if (lava || !floor)
        {
            GiveUpWalking();
            return;
        }
        // Against a step: jump it. Against a wall: stop trying.
        if (Math.Abs(player.position.X - _walkLastX) < 0.5f)
            _stuckFrames++;
        else
            _stuckFrames = 0;
        _walkLastX = player.position.X;
        if (_stuckFrames >= 12 && _jumpFrames == 0)
        {
            if (++_hops > 4)
            {
                GiveUpWalking();
                return;
            }
            _jumpFrames = 14;
            _stuckFrames = 0;
        }
        if (_jumpFrames > 0)
            _jumpFrames--;
        WalkDirection = _walkDirection;
        WalkJump = _jumpFrames > 0;
    }

    private bool Placing => _job!.Kind is Kind.Fill or Kind.Outline or Kind.Wall;

    /// <summary>Everything not yet done goes round again, in the
    /// job's own order - for building, from the ground up.</summary>
    private void Requeue()
    {
        var left = new HashSet<(int X, int Y)>(_failed.Concat(_far).Concat(_aside));
        _queue = new Queue<(int X, int Y)>(_job!.Targets.Where(left.Contains).Concat(left.Except(_job.Targets)));
        _failed = new List<(int X, int Y)>();
        _far = new List<(int X, int Y)>();
        _aside = new List<(int X, int Y)>();
    }

    /// <summary>A block cannot go where a character stands; a wall can.</summary>
    private bool InTheWay((int X, int Y) at)
    {
        if (_job!.Kind == Kind.Wall)
            return false;
        var tile = new Microsoft.Xna.Framework.Rectangle(at.X * 16, at.Y * 16, 16, 16);
        for (var i = 0; i < Main.maxPlayers; i++)
            if (Main.player[i] is { active: true, dead: false } player && player.Hitbox.Intersects(tile))
                return true;
        return false;
    }

    /// <summary>The game's rule for where a block or a wall will
    /// hold: beside a block or a wall, or, for a block, on a wall.</summary>
    private bool Attaches((int X, int Y) at)
    {
        if (_job!.Kind != Kind.Wall && Main.tile[at.X, at.Y].WallType != 0)
            return true;
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            var beside = Main.tile[at.X + dx, at.Y + dy];
            if (beside.HasTile || beside.WallType != 0)
                return true;
        }
        return false;
    }

    private void Stop(string why)
    {
        Shout();
        var left = _queue.Count + _failed.Count + _far.Count + _aside.Count + (_current == null ? 0 : 1);
        End();
        Say($"{why}. Stopped, {left} left.");
    }
}

/// <summary>While a job runs, the player reaches what is on screen.</summary>
internal sealed class BuildPlayer : ModPlayer
{
    public override void ResetEffects()
    {
        if (!BuildTool.Working || Player.whoAmI != Main.myPlayer)
            return;
        Player.tileRangeX += BuildTool.ReachX;
        Player.tileRangeY += BuildTool.ReachY;
    }
}
