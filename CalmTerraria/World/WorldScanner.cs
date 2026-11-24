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
        commands[KeyCombo.WithCtrl(Key.PageDown)] = () => Move(list => list.MoveCategory(1));
        commands[KeyCombo.WithCtrl(Key.PageUp)] = () => Move(list => list.MoveCategory(-1));
        commands[KeyCombo.Plain(Key.PageDown)] = () => Move(list => list.MoveKind(1));
        commands[KeyCombo.Plain(Key.PageUp)] = () => Move(list => list.MoveKind(-1));
        commands[KeyCombo.WithShift(Key.PageDown)] = () => Move(list => list.MoveInstance(1));
        commands[KeyCombo.WithShift(Key.PageUp)] = () => Move(list => list.MoveInstance(-1));
        commands[KeyCombo.Plain(Key.Home)] = () =>
        {
            if (Ready() && _list.Target is { } target)
                JumpRequested?.Invoke(target);
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

    private void Take(bool facingOnly)
    {
        _taken = true;
        IEnumerable<Feature> features = _latest!.Concat(Creatures());
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

    private void Move(Func<ScanList, string> move)
    {
        if (Ready())
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
            else if (!npc.friendly && npc.lifeMax > 5 && !npc.CountsAsACritter && OnScreen(npc.Center, out _, out _))
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
        foreach (var item in Main.ActiveItems)
        {
            if (!OnScreen(item.Center, out var x, out var y))
                continue;
            var (tracked, type) = (item, item.type);
            found.Add(new Feature(Kind(item.Name, TerrariaScanSource.Items), x, y)
            {
                Live = () => tracked.active && tracked.type == type ? TileOf(tracked.Center) : null,
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
