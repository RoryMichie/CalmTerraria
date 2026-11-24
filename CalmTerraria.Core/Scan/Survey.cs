namespace CalmTerraria.Scan;

/// <summary>What a kind of thing is, for the scanner.</summary>
/// <param name="Name">What it is called, singular: "Iron ore".</param>
/// <param name="Category">The category it is listed under.</param>
/// <param name="CellSize">Zero for a thing that is one object (a
/// chest): every tile reported is its own entry. Otherwise tiles of
/// this kind are gathered into one entry when they fall in adjoining
/// squares this many tiles across, so a larger number bridges wider
/// gaps (a vein of ore: 2; a biome: 16).</param>
/// <param name="MinTiles">A gathering smaller than this is dropped:
/// a puddle is not a pool.</param>
/// <param name="Sized">Whether how many tiles there are is worth
/// saying. True for a vein or a lake; false for objects that are
/// gathered only because the game does not say where one ends.</param>
public sealed record ScanKind(string Name, string Category, int CellSize = 0, int MinTiles = 1, bool Sized = true);

/// <summary>The world as the scanner sees it. The implementation
/// decides what the player is entitled to know: a tile the game's map
/// has not revealed reports nothing.</summary>
public interface IScanSource
{
    int Width { get; }
    int Height { get; }

    /// <summary>The kind of thing at a tile, as an index into
    /// <see cref="Kinds"/>, or -1 for nothing of interest. A one-object
    /// kind is reported at one of its tiles only.</summary>
    int KindAt(int x, int y);

    /// <summary>May grow while a survey is being taken.</summary>
    IReadOnlyList<ScanKind> Kinds { get; }
}

/// <summary>One entry: a chest, a vein, a lake.</summary>
public sealed class Feature
{
    /// <summary>One point per square the feature occupies (a single
    /// point for an object), so "the nearest part of it" is cheap.</summary>
    private readonly (int X, int Y)[] _points;

    internal Feature(ScanKind kind, int tiles, (int X, int Y)[] points, (int Left, int Top, int Right, int Bottom) bounds)
    {
        Kind = kind;
        Tiles = tiles;
        _points = points;
        Bounds = bounds;
    }

    /// <summary>A feature that is not from the tile survey: a
    /// creature, an item on the ground.</summary>
    public Feature(ScanKind kind, int x, int y)
        : this(kind, 1, new[] { (x, y) }, (x, y, x, y))
    {
    }

    public ScanKind Kind { get; }

    /// <summary>How many tiles of it there are. One for an object.</summary>
    public int Tiles { get; }

    public (int Left, int Top, int Right, int Bottom) Bounds { get; }

    /// <summary>What it is called when that is not the kind's name: an
    /// ocean among bodies of water.</summary>
    public string? Name { get; set; }

    /// <summary>Where the entry points when that is not its nearest
    /// part: an ocean's shore.</summary>
    public (int X, int Y)? Anchor { get; set; }

    /// <summary>For something that moves: where it is now, or null
    /// once it is gone. The snapshot keeps the entry either way.</summary>
    public Func<(int X, int Y)?>? Live { get; set; }

    /// <summary>A moving thing that is no longer there.</summary>
    public bool Gone => Live != null && Live() == null;

    /// <summary>The size worth saying, or null.</summary>
    public int? SaidTiles => Kind.Sized && Tiles > 1 ? Tiles : null;

    public string DisplayName => Name ?? Kind.Name;

    public IReadOnlyList<(int X, int Y)> Points => _points;

    /// <summary>Where to go for it, from a position: the anchor, or
    /// the nearest part.</summary>
    public (int X, int Y) Nearest(int fromX, int fromY)
    {
        if (Live?.Invoke() is { } now)
            return now;
        if (Anchor is { } anchor)
            return anchor;
        var best = _points[0];
        var bestDistance = long.MaxValue;
        foreach (var point in _points)
        {
            long dx = point.X - fromX, dy = point.Y - fromY;
            var distance = dx * dx + dy * dy;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = point;
            }
        }
        return best;
    }
}

/// <summary>Walks a world a few columns at a time and gathers what it
/// finds into features. One pass is one survey; the caller decides how
/// much of a frame each step may take.</summary>
public sealed class Surveyor
{
    private readonly IScanSource _source;
    private readonly List<Feature> _objects = new();
    /// <summary>Squares seen so far, by kind and square coordinates,
    /// each naming its node in the union-find below.</summary>
    private readonly Dictionary<(int Kind, int X, int Y), int> _squares = new();
    private readonly List<int> _parent = new();
    private readonly List<int> _tiles = new();
    private readonly List<(int Kind, int X, int Y)> _first = new();
    private int _column;

    public Surveyor(IScanSource source) => _source = source;

    public bool Done => _column >= _source.Width;

    /// <summary>How far through the world the pass is, 0 to 1.</summary>
    public double Progress => _source.Width == 0 ? 1.0 : Math.Min(1.0, (double)_column / _source.Width);

    /// <summary>Survey the next columns. False once the pass is done.</summary>
    public bool Step(int columns)
    {
        var end = Math.Min(_source.Width, _column + columns);
        var kinds = _source.Kinds;
        for (; _column < end; _column++)
        {
            for (var y = 0; y < _source.Height; y++)
            {
                var kind = _source.KindAt(_column, y);
                if (kind < 0)
                    continue;
                var size = kinds[kind].CellSize;
                if (size <= 0)
                    _objects.Add(new Feature(kinds[kind], _column, y));
                else
                    Gather(kind, _column, y, size);
            }
        }
        return !Done;
    }

    private void Gather(int kind, int x, int y, int size)
    {
        var key = (kind, x / size, y / size);
        if (_squares.TryGetValue(key, out var node))
        {
            _tiles[node]++;
            return;
        }
        node = _parent.Count;
        _parent.Add(node);
        _tiles.Add(1);
        _first.Add((kind, x, y));
        _squares[key] = node;
        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        {
            if ((dx != 0 || dy != 0)
                && _squares.TryGetValue((kind, key.Item2 + dx, key.Item3 + dy), out var neighbour))
                Union(node, neighbour);
        }
    }

    private int Find(int node)
    {
        while (_parent[node] != node)
        {
            _parent[node] = _parent[_parent[node]];
            node = _parent[node];
        }
        return node;
    }

    private void Union(int a, int b)
    {
        a = Find(a);
        b = Find(b);
        if (a != b)
            _parent[b] = a;
    }

    /// <summary>The finished pass. Call once <see cref="Done"/>.</summary>
    public List<Feature> Finish()
    {
        var kinds = _source.Kinds;
        var features = new List<Feature>(_objects);
        var members = new Dictionary<int, List<int>>();
        for (var node = 0; node < _parent.Count; node++)
        {
            var root = Find(node);
            if (!members.TryGetValue(root, out var list))
                members[root] = list = new List<int>();
            list.Add(node);
        }
        foreach (var list in members.Values)
        {
            var kind = kinds[_first[list[0]].Kind];
            var tiles = 0;
            var points = new (int X, int Y)[list.Count];
            int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
            for (var i = 0; i < list.Count; i++)
            {
                var (_, x, y) = _first[list[i]];
                tiles += _tiles[list[i]];
                points[i] = (x, y);
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
            if (tiles >= kind.MinTiles)
                features.Add(new Feature(kind, tiles, points, (left, top, right, bottom)));
        }
        return features;
    }
}
