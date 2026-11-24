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
/// that it says so and carries on as the player walks nearer.</summary>
internal sealed class BuildTool : IBuild
{
    /// <summary>How far a job reaches beyond the game's own, in tiles.
    /// Never past the screen: the game aims where the mouse can be.</summary>
    public const int ReachX = 45, ReachY = 25;

    /// <summary>True while a job runs: the reach is lent, and the
    /// hand changing tools is not read out.</summary>
    public static bool Working { get; private set; }

    private enum Kind { Fill, Outline, Wall, Clear, Unwall, Soak, Pour }

    /// <param name="More">For work that makes more work - water poured
    /// on lava uncovers the lava beneath: called when the targets run
    /// out, for the next ones. Empty when there is nothing left.</param>
    private sealed record Job(Kind Kind, int Item, string Doing, string Failed, List<(int X, int Y)> Targets,
        Func<List<(int X, int Y)>>? More = null);

    private readonly SruiHost _host;
    private readonly Cursor _cursor;
    private readonly WorldScanner _scanner;
    private (int X, int Y)? _a, _b;
    private readonly Dictionary<int, Job> _offered = new();

    private Job? _job;
    private Queue<(int X, int Y)> _queue = new();
    private List<(int X, int Y)> _failed = new(), _far = new();
    private (int X, int Y)? _current;
    private int _budget, _wait, _rounds;
    private bool _progress, _toldFar, _smartCursorWas;
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
                Say("The scanner is not on anything.");
                return;
            }
            var (left, top, right, bottom) = feature.Bounds;
            _a = (left, top);
            _b = (right, bottom);
            Say($"Selected {feature.DisplayName}, {right - left + 1} by {bottom - top + 1}.");
        };
        shell.Build = this;
        shell.WorldCommands[KeyCombo.Plain(Key.Char('x'))] = Mark;
        shell.WorldFrame += Update;
        shell.EnteredWorld += () =>
        {
            _a = _b = null;
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
            End();
            Say($"Stopped, {left} left.");
            return;
        }
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
        _offered.Clear();
        var options = new List<BuildOption>();
        if (_job != null)
            return options;
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
        void Offer(Job job, string name, string plan)
        {
            var id = options.Count;
            _offered[id] = job;
            options.Add(new BuildOption(id, name, plan));
        }
        string Supply(int needed, int type, string name)
        {
            var have = Carried(type);
            return have >= needed
                ? $"You have {have} {name}."
                : $"You have {have} {name}, {needed - have} short.";
        }

        var all = Up().ToList();
        var lava = all.Count(t => Main.tile[t.X, t.Y] is { LiquidAmount: > 0 } tile && tile.LiquidType == LiquidID.Lava);

        if (!held.IsAir && held.createTile >= TileID.Dirt && (Main.tileSolid[held.createTile] || TileID.Sets.Platforms[held.createTile]))
        {
            var name = held.Name;
            var empty = all.Where(t => !Main.tile[t.X, t.Y].HasTile).ToList();
            var occupied = all.Count - empty.Count;
            var note = (occupied > 0 ? $" {occupied} occupied tiles are left as they are." : "")
                + (lava > 0 ? $" Lava in the area, {lava} tiles." : "");
            if (empty.Count > 0)
                Offer(new Job(Kind.Fill, held.type, "Filling", "filled", empty),
                    $"Fill with {name}, {empty.Count} tiles",
                    $"{empty.Count} tiles to fill. {Supply(empty.Count, held.type, name)}{note}");
            var edge = empty.Where(t => t.X == left || t.X == right || t.Y == top || t.Y == bottom).ToList();
            if (edge.Count > 0 && edge.Count < empty.Count)
                Offer(new Job(Kind.Outline, held.type, "Outlining", "placed", edge),
                    $"Outline with {name}, {edge.Count} tiles",
                    $"{edge.Count} tiles round the edge. {Supply(edge.Count, held.type, name)}{note}");
        }

        if (!held.IsAir && held.createWall > 0)
        {
            var bare = all.Where(t => Main.tile[t.X, t.Y].WallType == 0).ToList();
            if (bare.Count > 0)
                Offer(new Job(Kind.Wall, held.type, "Walling", "walled", bare),
                    $"Wall with {held.Name}, {bare.Count} tiles",
                    $"{bare.Count} tiles to wall. {Supply(bare.Count, held.type, held.Name)}"
                    + " A wall needs a block or another wall beside it.");
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
                    Offer(new Job(Kind.Soak, held.type, "Soaking up", "emptied", wet),
                        $"Soak up {(liquid < 0 ? "liquid" : LiquidNames[liquid])} with {held.Name}, {wet.Count} tiles",
                        $"{wet.Count} tiles of liquid. " + (held.type == ItemID.EmptyBucket
                            ? $"Each scoop fills one bucket. {Supply(wet.Count, held.type, held.Name)}"
                            : "A sponge is never used up.")
                        + " Liquid runs in from the sides as it is taken, so the count is a start.");
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
                Offer(new Job(Kind.Pour, held.type, "Pouring water at", "poured", first, Beside),
                    $"Pour water on lava, {lava} tiles of lava",
                    $"{lava} tiles of lava. Water goes in beside it and runs onto it: thick lava turns to obsidian,"
                    + " which a good pickaxe can then clear, and a thin film just goes. It leaves water behind. "
                    + (first.Count == 0 ? "There is no open tile beside the lava to pour into. " : "")
                    + (held.type == ItemID.WaterBucket ? Supply(first.Count, held.type, held.Name) : "The bottomless bucket is never used up."));
            }
        }
        return options;
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

    private void OfferPath(Action<Job, string, string> offer)
    {
        if (_scanner.Selected is not { } feature)
            return;
        var (px, py) = WorldScanner.PlayerTile();
        var target = feature.Nearest(px, py);
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
        var intro = $"A stair to {feature.DisplayName}, {Directions.Offset(target.X - px, target.Y - py)}, {steps.Count} steps. "
            + (open > 0 ? $"{open} steps of it have nothing underfoot: mind the drop. " : "");
        OfferClear(tiles, lava, offer, $"Clear path to {feature.DisplayName}", intro, keepOrder: true, walls: false);
    }

    private void OfferClear(List<(int X, int Y)> all, int lava, Action<Job, string, string> offer,
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
            plan.Add("You would get " + string.Join(", ", yield.OrderByDescending(pair => pair.Value)
                .Select(pair => $"{pair.Value} {pair.Key}")) + ".");
        if (objects.Count > 0)
            plan.Add("Also removed: " + string.Join(", ", objects.OrderBy(name => name)) + ".");
        if (pick < 0)
            plan.Add("You have no pickaxe.");
        else if (tooHard > 0)
            plan.Add($"{tooHard} tiles are too hard for your pickaxe and are left.");
        if (trees > 0)
            plan.Add($"{trees} tiles of tree are left: you have no axe.");
        if (chests > 0)
            plan.Add("A chest is in the area and is left: chests must be emptied and taken by hand.");
        if (hammer > 0)
            plan.Add($"{hammer} tiles need a hammer and are left.");
        if (lava > 0)
            plan.Add($"Lava in the area, {lava} tiles: opening it up lets it run.");
        if (targets.Count == 0)
            plan.Insert(0, title == null ? "Nothing here can be cleared." : "Nothing on the way can be cleared.");
        // Trees first and from the bottom: one cut at the foot brings
        // down everything above it, which is then already done.
        // A path is cut in the order it is walked.
        var ordered = keepOrder ? targets
            : targets.Where(t => Main.tileAxe[Main.tile[t.X, t.Y].TileType]).OrderByDescending(t => t.Y)
                .Concat(targets.Where(t => !Main.tileAxe[Main.tile[t.X, t.Y].TileType])).ToList();
        offer(new Job(Kind.Clear, 0, "Clearing", "cleared", ordered), $"{title ?? "Clear"}, {ordered.Count} tiles",
            (intro ?? "") + string.Join(" ", plan));
        if (!walls)
            return;

        // Walls come off with a hammer, from their open edge inwards.
        if (Best(item => item.hammer) >= 0)
        {
            var walled = all.Where(t => Main.tile[t.X, t.Y].WallType != 0).ToList();
            if (walled.Count > 0)
                offer(new Job(Kind.Unwall, 0, "Taking down", "taken down", walled), $"Remove walls, {walled.Count} tiles",
                    $"{walled.Count} tiles of wall. A wall only comes off where it has an open edge, so they go from the outside in;"
                    + " walls behind solid ground all round cannot be reached.");
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

    public void Start(int id)
    {
        if (!_offered.TryGetValue(id, out var job) || (job.Targets.Count == 0 && job.More == null))
        {
            Say("Nothing to do.");
            return;
        }
        _job = job;
        _queue = new Queue<(int X, int Y)>(job.Targets);
        _failed = new List<(int X, int Y)>();
        _far = new List<(int X, int Y)>();
        _aside = new List<(int X, int Y)>();
        _toldAside = false;
        _current = null;
        _progress = _toldFar = false;
        _wait = 0;
        _tally = _tallyAge = 0;
        _tallyName = "";
        _rounds = 0;
        _cursorWas = _cursor.Tile;
        _smartCursorWas = Main.SmartCursorWanted_Mouse;
        // Smart cursor chooses its own tile; this work chooses each one.
        Main.SmartCursorWanted_Mouse = false;
        Working = true;
        Say($"{job.Doing} {job.Targets.Count} tiles. X stops.");
    }

    private void End()
    {
        if (_job == null)
            return;
        _job = null;
        _current = null;
        Working = false;
        _cursor.Drive(null, false);
        _cursor.MoveSilently(_cursorWas.X, _cursorWas.Y);
        Main.SmartCursorWanted_Mouse = _smartCursorWas;
    }

    private bool Done((int X, int Y) at)
    {
        var tile = Main.tile[at.X, at.Y];
        return _job!.Kind switch
        {
            Kind.Fill or Kind.Outline => tile.HasTile,
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
                return "The hammer is gone";
        }
        else if (_job.Kind == Kind.Clear)
        {
            slot = Main.tileAxe[Main.tile[at.X, at.Y].TileType] ? Best(item => item.axe) : Best(item => item.pick);
            if (slot < 0)
                return "The tool for it is gone";
        }
        else
        {
            slot = inventory[player.selectedItem].type == _job.Item ? player.selectedItem : -1;
            for (var i = 0; i < 50 && slot < 0; i++)
                if (inventory[i].type == _job.Item)
                    slot = i;
            if (slot < 0)
                return "None left";
        }
        if (slot == player.selectedItem)
            return null;
        // A tool cannot be changed mid-swing, and with the button
        // held the swinging never stops: the wrong tool would flail
        // at the tile to no effect. Button up until the hand is free.
        if (player.itemAnimation > 0)
            return Switching;
        if (player.selectedItem >= 10)
            player.selectedItem = 0;
        if (slot < 10)
            player.selectedItem = slot;
        else
            Utils.Swap(ref inventory[slot], ref inventory[player.selectedItem]);
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
        if (_current is { } now)
        {
            if (Done(now))
            {
                _progress = true;
                _toldFar = false;
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
            _toldAside = false;
            return;
        }
        if (_aside.Count > 0)
        {
            // The game will not put a block where a character is.
            if (!_toldAside)
            {
                _toldAside = true;
                Say($"Step aside: {_aside.Count} left where you are standing.");
            }
            Requeue();
            _wait = 20;
            return;
        }
        if (_far.Count > 0)
        {
            if (!_toldFar)
            {
                _toldFar = true;
                var (px, py) = WorldScanner.PlayerTile();
                var nearest = _far.MinBy(t => Math.Abs(t.X - px) + Math.Abs(t.Y - py));
                Say($"Walk closer: {_far.Count + _failed.Count} left, nearest {Directions.Offset(nearest.X - px, nearest.Y - py)}.");
            }
            _queue = new Queue<(int X, int Y)>(_far);
            _far = new List<(int X, int Y)>();
            _wait = 30;
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
        var failed = _failed.Count;
        var done = _job.Failed;
        Shout();
        End();
        Say(failed == 0 ? "Done." : $"Done. {failed} could not be {done}.");
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
            Kind.Wall or Kind.Fill or Kind.Outline => Main.LocalPlayer.HeldItem.Name,
            Kind.Soak => $"tiles of {LiquidNames[Math.Clamp(tile.LiquidType, 0, LiquidNames.Length - 1)]}",
            Kind.Pour => "buckets of water",
            _ => "tiles",
        };
    }

    /// <summary>The foreman. Work is called out as it goes, a run of
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
                Kind.Clear when name == Tree => "There goes a tree.",
                Kind.Clear or Kind.Unwall => _tally == 1 && name != "walls" ? $"Off goes {name}." : $"Off go {_tally} {name}.",
                Kind.Wall => $"Up go {_tally} {name}.",
                Kind.Soak => $"Away go {_tally} {name}.",
                _ => $"In go {_tally} {name}.",
            });
        }
        _tally = 0;
        _tallyAge = 0;
    }

    private List<(int X, int Y)> _aside = new();
    private bool _toldAside;

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
        Say($"{why}, {left} to go.");
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
