namespace CalmTerraria.Scan;

/// <summary>The scanner the player holds: a snapshot of features as
/// three levels - category, kind, instance - with a cursor at each,
/// and the words for where the cursor is.
///
/// The snapshot only changes on <see cref="Refresh"/>, so nothing
/// moves under the cursor. Instances are ordered by how far they were
/// from the player at the refresh, and kinds by their nearest
/// instance; the offsets that are spoken are always from where the
/// player is now.</summary>
public sealed class ScanList
{
    private sealed record Kind(string Name, List<Feature> Instances);
    private sealed record Category(string Name, List<Kind> Kinds);

    private readonly IReadOnlyList<string> _categoryOrder;
    private readonly Func<(int X, int Y)> _player;
    private List<Category> _categories = new();
    private int _category, _kind, _instance;

    /// <param name="categoryOrder">The categories in the order they
    /// are walked. A feature in a category not named here is not
    /// listed.</param>
    /// <param name="player">Where the player is now, in tiles.</param>
    public ScanList(IReadOnlyList<string> categoryOrder, Func<(int X, int Y)> player)
    {
        _categoryOrder = categoryOrder;
        _player = player;
    }

    public bool IsEmpty => _categories.Count == 0;

    /// <summary>Whether anything in the list has gone since it was
    /// taken: an item picked up, a creature killed.</summary>
    public bool AnyGone => _categories.Any(category => category.Kinds.Any(kind => kind.Instances.Any(feature => feature.Gone)));

    /// <summary>The selected instance, if there is one.</summary>
    public Feature? Current => IsEmpty ? null : _categories[_category].Kinds[_kind].Instances[_instance];

    /// <summary>Where the selected instance is: its nearest part.</summary>
    public (int X, int Y)? Target
    {
        get
        {
            if (Current is not { } feature)
                return null;
            var (x, y) = _player();
            return feature.Nearest(x, y);
        }
    }

    /// <summary>Replace the snapshot. The selection stays on the same
    /// kind where that kind still exists, and on the instance nearest
    /// to where the selected one was.</summary>
    public void Refresh(IEnumerable<Feature> features)
    {
        var (px, py) = _player();
        var keptCategory = IsEmpty ? null : _categories[_category].Name;
        var keptKind = IsEmpty ? null : _categories[_category].Kinds[_kind].Name;
        var keptAt = Target;

        long Distance(Feature feature)
        {
            var (x, y) = feature.Nearest(px, py);
            long dx = x - px, dy = y - py;
            return dx * dx + dy * dy;
        }

        _categories = features
            .GroupBy(feature => feature.Kind.Category)
            .Where(group => _categoryOrder.Contains(group.Key))
            .OrderBy(group => IndexOf(_categoryOrder, group.Key))
            .Select(group => new Category(group.Key, group
                .GroupBy(feature => feature.DisplayName)
                .Select(kind => new Kind(kind.Key, kind.OrderBy(Distance).ToList()))
                .OrderBy(kind => Distance(kind.Instances[0]))
                .ThenBy(kind => kind.Name, StringComparer.Ordinal)
                .ToList()))
            .ToList();

        _category = Math.Max(0, _categories.FindIndex(category => category.Name == keptCategory));
        _kind = 0;
        _instance = 0;
        if (IsEmpty)
            return;
        var kinds = _categories[_category].Kinds;
        var sameKind = _categories[_category].Name == keptCategory
            ? kinds.FindIndex(kind => kind.Name == keptKind)
            : -1;
        if (sameKind < 0 || keptAt is not { } at)
            return;
        _kind = sameKind;
        var instances = kinds[_kind].Instances;
        var best = long.MaxValue;
        for (var i = 0; i < instances.Count; i++)
        {
            var (x, y) = instances[i].Nearest(at.X, at.Y);
            long dx = x - at.X, dy = y - at.Y;
            if (dx * dx + dy * dy < best)
            {
                best = dx * dx + dy * dy;
                _instance = i;
            }
        }
    }

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i] == value)
                return i;
        return -1;
    }

    private const string Nothing = "Nothing found.";

    /// <summary>The selection, as moving onto its kind reads it.</summary>
    public string Read() => IsEmpty ? Nothing : ReadKind();

    /// <summary>Move between categories and read the category and the
    /// kind landed on.</summary>
    public string MoveCategory(int delta)
    {
        if (IsEmpty)
            return Nothing;
        var edge = Step(ref _category, delta, _categories.Count);
        if (edge == null)
        {
            _kind = 0;
            _instance = 0;
        }
        return $"{edge}{_categories[_category].Name}. {ReadKind()}";
    }

    /// <summary>Move between kinds in the category and read the one
    /// landed on, at its nearest instance.</summary>
    public string MoveKind(int delta)
    {
        if (IsEmpty)
            return Nothing;
        var edge = Step(ref _kind, delta, _categories[_category].Kinds.Count);
        if (edge == null)
            _instance = 0;
        return edge + ReadKind();
    }

    /// <summary>Move between instances of the kind and read the one
    /// landed on. Its name is the kind's, already heard, and is not
    /// said again.</summary>
    public string MoveInstance(int delta)
    {
        if (IsEmpty)
            return Nothing;
        var instances = _categories[_category].Kinds[_kind].Instances;
        var edge = Step(ref _instance, delta, instances.Count);
        var feature = instances[_instance];
        var size = feature.SaidTiles is { } tiles ? $"{tiles} tiles, " : "";
        return $"{edge}{size}{Offset(feature)}, {_instance + 1} of {instances.Count}";
    }

    /// <summary>Moves an index and stays inside; returns what is said
    /// before the reading when the move ran into an end.</summary>
    private static string? Step(ref int index, int delta, int count)
    {
        var next = index + delta;
        if (next < 0)
            return "top, ";
        if (next >= count)
            return "bottom, ";
        index = next;
        return null;
    }

    private string ReadKind()
    {
        var kind = _categories[_category].Kinds[_kind];
        var feature = kind.Instances[_instance];
        var size = feature.SaidTiles is { } tiles ? $", {tiles} tiles" : "";
        var position = kind.Instances.Count > 1 ? $", {_instance + 1} of {kind.Instances.Count}" : "";
        return $"{kind.Name}{size}, {Offset(feature)}{position}";
    }

    private string Offset(Feature feature)
    {
        if (feature.Gone)
            return "gone";
        var (px, py) = _player();
        var (x, y) = feature.Nearest(px, py);
        return Directions.Offset(x - px, y - py);
    }
}

/// <summary>Where something is from somewhere else, in the words the
/// whole mod uses. The world is seen from the side: left and right,
/// up and down, counted in tiles.</summary>
public static class Directions
{
    public static string Offset(int dx, int dy)
    {
        if (dx == 0 && dy == 0)
            return "here";
        var across = dx == 0 ? "" : $"{Math.Abs(dx)} {(dx < 0 ? "left" : "right")}";
        var along = dy == 0 ? "" : $"{Math.Abs(dy)} {(dy < 0 ? "up" : "down")}";
        return across.Length > 0 && along.Length > 0 ? $"{across} {along}" : across + along;
    }
}
