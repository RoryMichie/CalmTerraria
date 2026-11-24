using CalmTerraria.Scan;

namespace CalmTerraria.Tests;

/// <summary>A world drawn in text. Each character is a tile; the
/// legend says what kind a character is. '.' is nothing.</summary>
internal sealed class TextWorld : IScanSource
{
    private readonly string[] _rows;
    private readonly Dictionary<char, int> _kindOf = new();
    private readonly List<ScanKind> _kinds = new();

    public TextWorld(string map, params (char Symbol, ScanKind Kind)[] legend)
    {
        _rows = map.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var (symbol, kind) in legend)
        {
            _kindOf[symbol] = _kinds.Count;
            _kinds.Add(kind);
        }
    }

    public int Width => _rows[0].Length;
    public int Height => _rows.Length;
    public IReadOnlyList<ScanKind> Kinds => _kinds;
    public int KindAt(int x, int y) => _kindOf.TryGetValue(_rows[y][x], out var kind) ? kind : -1;

    public List<Feature> Survey(int columnsPerStep = 3)
    {
        var surveyor = new Surveyor(this);
        while (surveyor.Step(columnsPerStep))
        {
        }
        return surveyor.Finish();
    }
}

public class SurveyTests
{
    private static readonly ScanKind Iron = new("Iron ore", "Ores", CellSize: 1);
    private static readonly ScanKind Chest = new("Chest", "Containers");
    private static readonly ScanKind Water = new("Water", "Liquids", CellSize: 2, MinTiles: 4);

    [Fact]
    public void TouchingTilesAreOneVein()
    {
        var world = new TextWorld("""
            ..........
            .ii....i..
            ..ii...i..
            ..........
            """, ('i', Iron));
        var features = world.Survey().OrderByDescending(f => f.Tiles).ToList();
        Assert.Equal(new[] { 4, 2 }, features.Select(f => f.Tiles));
    }

    [Fact]
    public void DiagonalNeighboursJoin()
    {
        var world = new TextWorld("""
            i...
            .i..
            ..i.
            """, ('i', Iron));
        Assert.Equal(3, Assert.Single(world.Survey()).Tiles);
    }

    [Fact]
    public void AnObjectIsItsOwnEntryEvenBesideAnother()
    {
        var world = new TextWorld("""
            .cc.
            """, ('c', Chest));
        Assert.Equal(2, world.Survey().Count);
    }

    [Fact]
    public void LargerSquaresBridgeGaps()
    {
        // Two tiles apart: separate at square size 1, one pool at 2.
        var world = new TextWorld("""
            ww.ww...
            ww.ww...
            """, ('w', Water));
        Assert.Equal(8, Assert.Single(world.Survey()).Tiles);
    }

    [Fact]
    public void SmallGatheringsAreDropped()
    {
        var world = new TextWorld("""
            w.......ww
            ........ww
            """, ('w', Water));
        Assert.Equal(4, Assert.Single(world.Survey()).Tiles);
    }

    [Fact]
    public void TheStepSizeDoesNotChangeTheResult()
    {
        var map = """
            ..ii......
            ..i...ii..
            ......i...
            """;
        var one = new TextWorld(map, ('i', Iron)).Survey(1).Select(f => f.Tiles).Order();
        var all = new TextWorld(map, ('i', Iron)).Survey(100).Select(f => f.Tiles).Order();
        Assert.Equal(all, one);
    }

    [Fact]
    public void NearestIsTheNearestPartOfAVein()
    {
        var world = new TextWorld("""
            iiiiiiii
            """, ('i', Iron));
        var vein = Assert.Single(world.Survey());
        Assert.Equal((0, 0), vein.Nearest(-5, 0));
        Assert.Equal((7, 0), vein.Nearest(20, 0));
    }
}

public class ScanListTests
{
    private static readonly string[] Order = { "Containers", "Ores" };
    private static readonly ScanKind Iron = new("Iron ore", "Ores", CellSize: 1);
    private static readonly ScanKind Gold = new("Gold ore", "Ores", CellSize: 1);
    private static readonly ScanKind Chest = new("Chest", "Containers");

    // The player stands at the P, which is not a tile kind.
    private const string Map = """
        ..........c.........
        ....................
        .ii.......P....gg...
        .i..................
        ..................i.
        """;

    private static (ScanList List, Func<(int, int)> Move) Build()
    {
        var player = (X: 10, Y: 2);
        var list = new ScanList(Order, () => player);
        list.Refresh(new TextWorld(Map, ('i', Iron), ('g', Gold), ('c', Chest)).Survey());
        return (list, () => player);
    }

    [Fact]
    public void ReadsTheFirstCategorysNearestKind()
    {
        var (list, _) = Build();
        Assert.Equal("Chest, 2 up", list.Read());
    }

    [Fact]
    public void CategoryMoveNamesTheCategoryThenTheKind()
    {
        var (list, _) = Build();
        Assert.Equal("Ores. Gold ore, 2 tiles, 5 right", list.MoveCategory(1));
    }

    [Fact]
    public void KindsAreOrderedByTheirNearestInstance()
    {
        var (list, _) = Build();
        list.MoveCategory(1);
        Assert.Equal("Iron ore, 3 tiles, 8 left, 1 of 2", list.MoveKind(1));
    }

    [Fact]
    public void InstanceMoveDoesNotRepeatTheName()
    {
        var (list, _) = Build();
        list.MoveCategory(1);
        list.MoveKind(1);
        Assert.Equal("8 right 2 down, 2 of 2", list.MoveInstance(1));
    }

    [Fact]
    public void EndsAreSaidAndTheSelectionStays()
    {
        var (list, _) = Build();
        Assert.Equal("top, Containers. Chest, 2 up", list.MoveCategory(-1));
        list.MoveCategory(1);
        Assert.Equal("bottom, Ores. Gold ore, 2 tiles, 5 right", list.MoveCategory(1));
        Assert.Equal("top, Gold ore, 2 tiles, 5 right", list.MoveKind(-1));
    }

    [Fact]
    public void OffsetsFollowThePlayer()
    {
        var player = (X: 10, Y: 2);
        var list = new ScanList(Order, () => player);
        list.Refresh(new TextWorld(Map, ('c', Chest)).Survey());
        player = (10, 0);
        Assert.Equal("Chest, here", list.Read());
        player = (13, 1);
        Assert.Equal("Chest, 3 left 1 up", list.Read());
    }

    [Fact]
    public void RefreshKeepsTheSelectedKindAndInstance()
    {
        var player = (X: 10, Y: 2);
        var list = new ScanList(Order, () => player);
        var world = new TextWorld(Map, ('i', Iron), ('g', Gold), ('c', Chest));
        list.Refresh(world.Survey());
        list.MoveCategory(1);
        list.MoveKind(1);
        list.MoveInstance(1);
        // The player walks right; the far vein is now the near one,
        // and the selection is still that vein.
        player = (17, 4);
        list.Refresh(world.Survey());
        Assert.Equal("Iron ore, 1 right, 1 of 2", list.Read());
    }

    [Fact]
    public void AnEmptyScanSaysSo()
    {
        var list = new ScanList(Order, () => (0, 0));
        list.Refresh(Array.Empty<Feature>());
        Assert.Equal("Nothing found.", list.Read());
        Assert.Equal("Nothing found.", list.MoveKind(1));
        Assert.Null(list.Target);
    }

    [Fact]
    public void AFeaturesOwnNameMakesItsOwnKind()
    {
        var water = new ScanKind("Water", "Ores", CellSize: 1);
        var ocean = new Feature(water, 0, 0) { Name = "Ocean", Anchor = (3, 0) };
        var pond = new Feature(water, 6, 0);
        var list = new ScanList(Order, () => (5, 0));
        list.Refresh(new[] { ocean, pond });
        Assert.Equal("Water, 1 right", list.Read());
        Assert.Equal("Ocean, 2 left", list.MoveKind(1));
    }

    [Fact]
    public void AMovingThingIsReadWhereItIsNowAndSaidGoneWhenItIs()
    {
        var zombie = new ScanKind("Zombie", "Ores");
        (int, int)? at = (8, 0);
        var list = new ScanList(Order, () => (5, 0));
        list.Refresh(new[] { new Feature(zombie, 8, 0) { Live = () => at } });
        Assert.Equal("Zombie, 3 right", list.Read());
        at = (4, 0);
        Assert.Equal("Zombie, 1 left", list.Read());
        at = null;
        Assert.Equal("Zombie, gone", list.Read());
    }

    [Fact]
    public void AnObjectGatheredFromTilesDoesNotSayItsSize()
    {
        var pot = new ScanKind("Pot", "Containers", CellSize: 2, Sized: false);
        var list = new ScanList(Order, () => (0, 0));
        list.Refresh(new TextWorld("""
            ....pp
            ....pp
            """, ('p', pot)).Survey());
        Assert.Equal("Pot, 4 right", list.Read());
    }
}
