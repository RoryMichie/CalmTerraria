using System.Diagnostics;
using CalmTerraria.Host;
using CalmTerraria.Scan;
using CalmTerraria.Screens;
using log4net;
using Srui;
using Terraria;

namespace CalmTerraria.World;

/// <summary>The scanner, in a world. A survey of the revealed map runs
/// all the time in the background, a little each frame; the player's
/// list is a snapshot of the latest finished one, taken when they ask.
/// Creatures and dropped items are not in the survey - they move - and
/// are added as they stand at the moment of the snapshot.</summary>
internal sealed class WorldScanner
{
    /// <summary>How much of each frame the survey may take.</summary>
    private static readonly TimeSpan FrameBudget = TimeSpan.FromMilliseconds(1.5);
    private const int ColumnsPerStep = 8;
    /// <summary>Frames to rest between one pass and the next.</summary>
    private const int RestFrames = 120;

    private readonly SruiHost _host;
    private readonly ILog _log;
    private readonly ScanList _list;
    private TerrariaScanSource? _source;
    private Surveyor? _surveyor;
    private List<Feature>? _latest;
    private readonly Stopwatch _passClock = new();
    private int _passFrames;
    private int _rest;
    private bool _taken;

    /// <summary>The player asked to go to the selected entry.</summary>
    public event Action<(int X, int Y)>? JumpRequested;

    /// <summary>Features from outside the world itself: waypoints.</summary>
    public Func<IEnumerable<Feature>>? Extra { get; set; }

    /// <summary>The place - biome, dungeon, building - that a tile is
    /// in, as the background survey last had it: the smallest whose
    /// bounds hold the tile. Buildings are left to the room check.</summary>
    public Feature? PlaceAt(int x, int y)
    {
        Feature? best = null;
        long bestArea = long.MaxValue;
        foreach (var feature in _latest ?? new List<Feature>())
        {
            if (feature.Kind.Category != TerrariaScanSource.Places || feature.Kind.Name == "Building")
                continue;
            var (left, top, right, bottom) = feature.Bounds;
            if (x < left || x > right || y < top - 20 || y > bottom + 20)
                continue;
            var area = (long)(right - left + 1) * (bottom - top + 1);
            if (area < bestArea)
                (best, bestArea) = (feature, area);
        }
        return best;
    }

    /// <summary>What the scanner is on.</summary>
    public Feature? Selected => _taken ? _list.Current : null;

    /// <summary>Take the list again, silently, because something in
    /// it was added or removed.</summary>
    public void Retake()
    {
        if (_latest != null)
            Take(_facingOnly);
    }

    public WorldScanner(SruiHost host, Shell shell, ILog log)
    {
        _host = host;
        _log = log;
        _list = new ScanList(TerrariaScanSource.CategoryOrder, PlayerTile);
        shell.EnteredWorld += () =>
        {
            _source = new TerrariaScanSource();
            _surveyor = null;
            _latest = null;
            _taken = false;
            _rest = 0;
            _list.Refresh(Array.Empty<Feature>());
        };
        shell.LeftWorld += () =>
        {
            _source = null;
            _surveyor = null;
            _latest = null;
        };
        shell.WorldFrame += Update;

        var commands = shell.WorldCommands;
        commands[KeyCombo.Plain(Key.End)] = () => Refresh(facingOnly: false);
        commands[KeyCombo.WithShift(Key.End)] = () => Refresh(facingOnly: true);
        commands[KeyCombo.WithCtrl(Key.PageDown)] = () => Move(list => list.MoveCategory(1), fresh: true);
        commands[KeyCombo.WithCtrl(Key.PageUp)] = () => Move(list => list.MoveCategory(-1), fresh: true);
        commands[KeyCombo.Plain(Key.PageDown)] = () => Move(list => list.MoveKind(1));
        commands[KeyCombo.Plain(Key.PageUp)] = () => Move(list => list.MoveKind(-1));
        commands[KeyCombo.WithShift(Key.PageDown)] = () => Move(list => list.MoveInstance(1));
        commands[KeyCombo.WithShift(Key.PageUp)] = () => Move(list => list.MoveInstance(-1));
        // Ctrl+Home: the character goes, not the cursor. A power.
        commands[KeyCombo.WithCtrl(Key.Home)] = () =>
        {
            if (!Ready())
                return;
            _usedAt = _host.Now;
            if (_list.Target is not { } target)
            {
                _host.App.Announce("Nothing selected.");
                return;
            }
            // Something that moved on - an item picked up, a creature
            // gone - is not a place to go any more.
            if (_list.Current is { Gone: true } gone)
            {
                _host.App.Announce($"{gone.DisplayName} is gone.");
                return;
            }
            var where = Travel.To(target.X, target.Y, text => _host.App.Announce(text));
            // The cursor comes too, onto the thing itself.
            JumpRequested?.Invoke(target);
            // Where it could only be stood near: how far off it is.
            if (where != null)
                _host.App.Announce(where);
        };
        commands[KeyCombo.Plain(Key.Home)] = () =>
        {
            // Never refreshed here: it goes to what was last read.
            if (Ready() && _list.Current is { Gone: true } gone)
                _host.App.Announce($"{gone.DisplayName} is gone.");
            else if (Ready() && _list.Target is { } target)
                JumpRequested?.Invoke(target);
            _usedAt = _host.Now;
        };
    }

    public static (int X, int Y) PlayerTile()
    {
        var center = Main.LocalPlayer.Center;
        return ((int)(center.X / 16f), (int)(center.Y / 16f));
    }

    private void Update()
    {
        if (_source == null)
            return;
        if (_surveyor == null)
        {
            if (_rest-- > 0)
                return;
            _surveyor = new Surveyor(_source);
            _passClock.Restart();
            _passFrames = 0;
        }
        _passFrames++;
        var frame = Stopwatch.StartNew();
        while (_surveyor.Step(ColumnsPerStep) && frame.Elapsed < FrameBudget)
        {
        }
        if (!_surveyor.Done)
            return;
        var first = _latest == null;
        _latest = Refine(_surveyor.Finish());
        _surveyor = null;
        _rest = RestFrames;
        if (first)
            _log.Info($"Scanner: first pass took {_passClock.ElapsedMilliseconds} ms over {_passFrames} frames, {_latest.Count} features.");
    }

    /// <summary>Whether there is a list to act on, taking the first
    /// snapshot if the player has not asked for one yet. Says why not
    /// otherwise.</summary>
    private bool Ready()
    {
        if (_latest == null)
        {
            var percent = (int)((_surveyor?.Progress ?? 0.0) * 100.0);
            _host.App.Announce($"Scanning, {percent} percent.");
            return false;
        }
        if (!_taken)
            Take(facingOnly: false);
        return true;
    }

    /// <summary>When a scanner key was last pressed, and whether the
    /// list in hand is of the facing side only.</summary>
    private ulong _usedAt;
    private bool _facingOnly;
    /// <summary>Left alone this long, the list is retaken at the next
    /// scanner key: what it says should be about now.</summary>
    private const ulong StaleMs = 5000;

    /// <summary>How far round the player is surveyed afresh, on the
    /// spot, each time the list is taken.</summary>
    private const int NearAcross = 160, NearDown = 100;

    /// <summary>The most tiles one entry is counted over again to see
    /// whether it is as it was listed.</summary>
    private const int CountLimit = 4096;

    /// <summary>The background survey takes seconds to go round the
    /// whole world, so what it last saw near the player may be what
    /// the player has since dug out, poured on or built. So the part
    /// of the world round the player is surveyed again here, whole,
    /// and takes the place of what the background survey has for it.
    /// A thing that reaches past the edge of that part - a lake, a
    /// biome - keeps the background survey's account of it.</summary>
    private IEnumerable<Feature> Fresh()
    {
        if (_source == null)
            return _latest!;
        var (px, py) = PlayerTile();
        int left = Math.Max(0, px - NearAcross), top = Math.Max(0, py - NearDown);
        int right = Math.Min(_source.Width - 1, px + NearAcross), bottom = Math.Min(_source.Height - 1, py + NearDown);
        var survey = new Surveyor(_source, left, top, right, bottom);
        while (survey.Step(64))
        {
        }
        bool Inside(Feature feature) =>
            feature.Bounds.Left > left + 2 && feature.Bounds.Right < right - 2
            && feature.Bounds.Top > top + 2 && feature.Bounds.Bottom < bottom - 2;
        var near = Refine(survey.Finish()).Where(Inside).ToList();
        // What is here can be smashed, mined or cut down between one
        // scanner key and the next, and the list is then retaken
        // before it is read. Liquids are left out: they are never
        // still, and a list that moved with them would never settle.
        var source = _source;
        foreach (var feature in near)
        {
            if (feature.Kind.Category == TerrariaScanSource.Liquids || feature.Kind.Name == "Lava")
                continue;
            var (kind, points, tiles) = (feature.Kind, feature.Points, feature.Tiles);
            bool Is(int x, int y) => source.KindAt(x, y) is >= 0 and var at && ReferenceEquals(source.Kinds[at], kind);
            var size = kind.CellSize;
            // Counted again, tile for tile, over the squares it was
            // gathered from and no further than was surveyed: one tile
            // out of a vein is a different vein. Something too big for
            // that to be quick is tried at one tile in each square.
            if (size > 0 && points.Count * size * size <= CountLimit)
                feature.Exists = () =>
                {
                    var count = 0;
                    foreach (var (x, y) in points)
                    {
                        int x0 = Math.Max(left, x / size * size), y0 = Math.Max(top, y / size * size);
                        int x1 = Math.Min(right, x / size * size + size - 1), y1 = Math.Min(bottom, y / size * size + size - 1);
                        for (var column = x0; column <= x1; column++)
                        for (var row = y0; row <= y1; row++)
                            if (Is(column, row))
                                count++;
                    }
                    return count == tiles;
                };
            else
                feature.Exists = () =>
                {
                    foreach (var (x, y) in points)
                        if (!Is(x, y))
                            return false;
                    return true;
                };
        }
        return _latest!.Where(feature => !Inside(feature)).Concat(near);
    }

    private void Take(bool facingOnly)
    {
        _taken = true;
        _facingOnly = facingOnly;
        _usedAt = _host.Now;
        IEnumerable<Feature> features = Fresh().Concat(Creatures());
        if (Extra != null)
            features = features.Concat(Extra());
        if (facingOnly)
        {
            var (px, py) = PlayerTile();
            var direction = Main.LocalPlayer.direction;
            features = features.Where(feature => Math.Sign(feature.Nearest(px, py).X - px) == direction);
        }
        _list.Refresh(features);
    }

    private void Refresh(bool facingOnly)
    {
        if (_latest == null)
        {
            Ready();
            return;
        }
        Take(facingOnly);
        _host.App.Announce(_list.Read());
    }

    /// <param name="fresh">Retake the list first whatever its age:
    /// changing category does.</param>
    private void Move(Func<ScanList, string> move, bool fresh = false)
    {
        if (!Ready())
            return;
        // The list never changes under a run of key presses, only
        // before the first of one. The selection stays on its kind.
        // And never with something in it that has since been picked up
        // or killed: a list of what is gone is a list of ghosts.
        if (fresh || _host.Now - _usedAt >= StaleMs || _list.AnyGone)
            Take(_facingOnly);
        _usedAt = _host.Now;
        _host.App.Announce(move(_list));
    }

    /// <summary>The survey knows tiles; this is where what is known
    /// about the world as a whole is added. A large body of water at
    /// the edge of the world is an ocean, and what is worth pointing at
    /// is its shore.</summary>
    private static List<Feature> Refine(List<Feature> features)
    {
        const int oceanTiles = 1500;
        const int edge = 400;
        foreach (var feature in features)
        {
            // A chest the player has named is found by that name: it
            // is its own entry among the containers, name first.
            if (feature.Kind.Category == TerrariaScanSource.Containers)
            {
                var index = Chest.FindChest(feature.Bounds.Left, feature.Bounds.Top);
                if (index >= 0 && Main.chest[index] is { } chest && !string.IsNullOrWhiteSpace(chest.name))
                    feature.Name = $"{chest.name.Trim()}, {feature.Kind.Name}";
                continue;
            }
            if (feature.Kind.Name != "Water" || feature.Tiles < oceanTiles || feature.Bounds.Top > Main.worldSurface)
                continue;
            var left = feature.Bounds.Left < edge;
            var right = feature.Bounds.Right > Main.maxTilesX - edge;
            if (!left && !right)
                continue;
            feature.Name = "Ocean";
            // The shore: the surface of the water, as far inland as it reaches.
            var surface = feature.Points.Where(point => point.Y <= feature.Bounds.Top + 6);
            feature.Anchor = left
                ? surface.MaxBy(point => point.X)
                : surface.MinBy(point => point.X);
        }
        return features;
    }

    /// <summary>What moves. Townspeople are on the map wherever they
    /// are; enemies and dropped items only where a screen would show
    /// them.</summary>
    private static List<Feature> Creatures()
    {
        var found = new List<Feature>();
        var (px, py) = PlayerTile();
        var acrossTiles = Main.screenWidth / 32 + 2;
        var downTiles = Main.screenHeight / 32 + 2;
        bool OnScreen(Microsoft.Xna.Framework.Vector2 center, out int x, out int y)
        {
            x = (int)(center.X / 16f);
            y = (int)(center.Y / 16f);
            return Math.Abs(x - px) <= acrossTiles && Math.Abs(y - py) <= downTiles;
        }

        var kinds = new Dictionary<(string, string), ScanKind>();
        ScanKind Kind(string name, string category) =>
            kinds.TryGetValue((name, category), out var kind)
                ? kind
                : kinds[(name, category)] = new ScanKind(name, category);

        static (int, int) TileOf(Microsoft.Xna.Framework.Vector2 center) =>
            ((int)(center.X / 16f), (int)(center.Y / 16f));

        foreach (var npc in Main.ActiveNPCs)
        {
            string category;
            if (npc.townNPC)
                category = TerrariaScanSource.People;
            else if (!OnScreen(npc.Center, out _, out _))
                continue;
            else if (npc.CountsAsACritter || npc.catchItem > 0)
                // The small harmless things: what a bug net is for.
                category = TerrariaScanSource.Critters;
            else if (!npc.friendly && npc.lifeMax > 5)
                category = TerrariaScanSource.Enemies;
            else
                continue;
            var (x, y) = TileOf(npc.Center);
            var (tracked, type) = (npc, npc.type);
            found.Add(new Feature(Kind(npc.TypeName, category), x, y)
            {
                Live = () => tracked.active && tracked.type == type ? TileOf(tracked.Center) : null,
            });
        }
        // The other players, wherever they are: the game shows them
        // on the map at any distance.
        for (var i = 0; i < Main.maxPlayers; i++)
        {
            var other = Main.player[i];
            if (other is not { active: true } || i == Main.myPlayer)
                continue;
            var (x, y) = TileOf(other.Center);
            var (tracked, name) = (other, other.name);
            found.Add(new Feature(Kind(other.dead ? $"{name}, dead" : name, TerrariaScanSource.Players), x, y)
            {
                Live = () => tracked.active && tracked.name == name ? TileOf(tracked.Center) : null,
            });
        }
        // A summoned piggy bank or void lens: a container that floats.
        foreach (var projectile in Main.ActiveProjectiles)
        {
            if (!TileReader.IsFloatingContainer(projectile))
                continue;
            var (x, y) = TileOf(projectile.Center);
            var (tracked, type) = (projectile, projectile.type);
            found.Add(new Feature(Kind(projectile.Name, TerrariaScanSource.Containers), x, y)
            {
                Live = () => tracked.active && tracked.type == type ? TileOf(tracked.Center) : null,
            });
        }
        foreach (var item in Main.ActiveItems)
        {
            if (!OnScreen(item.Center, out var x, out var y))
                continue;
            // The game puts a new, empty item in the slot when one is
            // picked up, and leaves the old one looking as it did: it
            // is there only while the slot still holds that very item.
            var (tracked, slot) = (item, item.whoAmI);
            found.Add(new Feature(Kind(item.Name, TerrariaScanSource.Items), x, y)
            {
                Live = () => ReferenceEquals(Main.item[slot], tracked) && tracked.active && tracked.stack > 0
                    ? TileOf(tracked.Center)
                    : null,
            });
        }
        return found;
    }

    /// <summary>Commands for autopilot scripts.</summary>
    public void RegisterDebug(Autopilot autopilot)
    {
        // Reveals the whole map, as though every tile had been seen:
        // the scanner's worst case. Test worlds only.
        autopilot.Register("reveal", _ =>
        {
            if (!Main.worldName.StartsWith("calmtest", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("reveal is for test worlds");
            var clock = Stopwatch.StartNew();
            for (var x = 0; x < Main.maxTilesX; x++)
            for (var y = 0; y < Main.maxTilesY; y++)
                Main.Map.Update(x, y, byte.MaxValue);
            _latest = null;
            _surveyor = null;
            _rest = 0;
            _taken = false;
            _log.Info($"Scanner: revealed {Main.maxTilesX} by {Main.maxTilesY} in {clock.ElapsedMilliseconds} ms.");
        });
        // Writes the latest survey to the log, a line per category.
        autopilot.Register("survey", _ =>
        {
            if (_latest == null)
            {
                _log.Info("Scanner: no survey yet.");
                return;
            }
            foreach (var category in _latest.GroupBy(feature => feature.Kind.Category))
            {
                var kinds = category
                    .GroupBy(feature => feature.DisplayName)
                    .OrderByDescending(kind => kind.Count())
                    .Select(kind => kind.First().Kind.CellSize > 0
                        ? $"{kind.Key} x{kind.Count()} ({kind.Sum(feature => feature.Tiles)} tiles)"
                        : $"{kind.Key} x{kind.Count()}");
                _log.Info($"Scanner: {category.Key}: {string.Join(", ", kinds)}");
            }
        });
    }
}
