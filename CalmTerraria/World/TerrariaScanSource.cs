using CalmTerraria.Scan;
using Terraria;
using Terraria.ID;
using Terraria.Map;
using Terraria.ObjectData;

namespace CalmTerraria.World;

/// <summary>The loaded world, as the scanner may know it. A tile is
/// reported only where the game's own map has been revealed, which is
/// exactly what a sighted player's map would show them; what is there
/// is then read from the world itself, so one chest is one entry and
/// a mined-out vein is gone.
///
/// What counts as worth listing, and under what, is decided here once
/// per tile type. Names are the map's own legend wherever it has one.</summary>
internal sealed class TerrariaScanSource : IScanSource
{
    public const string Containers = "Containers";
    public const string Waypoints = World.Waypoints.Category;
    public const string Treasure = "Treasure";
    public const string Ores = "Ores";
    public const string Gems = "Gems";
    public const string Stations = "Crafting stations";
    public const string Plants = "Plants";
    public const string Liquids = "Liquids";
    public const string Hazards = "Hazards";
    public const string Places = "Places";
    public const string Players = "Players";
    public const string People = "People";
    public const string Enemies = "Enemies";
    public const string Critters = "Critters";
    public const string Items = "Items";

    public static readonly string[] CategoryOrder =
    {
        Waypoints, Players, Containers, Treasure, Ores, Gems, Stations, Plants, Liquids, Hazards, Places, People, Enemies, Critters, Items,
    };

    private readonly List<ScanKind> _kinds = new();
    /// <summary>The category of each tile type, or null.</summary>
    private readonly string?[] _categoryOfType;
    /// <summary>The place a tile type marks, as a kind index, or -1.</summary>
    private readonly int[] _placeOfType;
    /// <summary>Kinds already made, by the map's legend entry.</summary>
    private readonly Dictionary<int, int> _kindOfLookup = new();
    private readonly int[] _liquidKinds;
    private readonly int _lava;
    private readonly int _spiderNest;
    private readonly int _building;
    private readonly int _desert;

    /// <summary>How far in from the edge of the world the game calls
    /// the surface a beach.</summary>
    private const int BeachTiles = 380;

    /// <summary>Sand is a desert only on the surface and away from the
    /// oceans: a beach is sand too, and so are pockets in the caverns
    /// and the fill of the underground desert, which has its own name.</summary>
    private static bool SurfaceDesert(int x, int y) =>
        y < Main.worldSurface && x >= BeachTiles && x <= Main.maxTilesX - BeachTiles;

    public TerrariaScanSource()
    {
        var types = TileID.Sets.Ore.Length;
        _categoryOfType = new string?[types];
        _placeOfType = new int[types];
        Array.Fill(_placeOfType, -1);

        // Later lines win where a tile is in more than one set.
        foreach (var recipe in Main.recipe)
            foreach (var station in recipe.requiredTile)
                if (station >= 0 && station < types)
                    _categoryOfType[station] = Stations;
        _categoryOfType[TileID.DemonAltar] = Stations;
        for (var type = 0; type < types; type++)
        {
            if (TileID.Sets.Ore[type])
                _categoryOfType[type] = Ores;
            if (TileID.Sets.BasicChest[type] || TileID.Sets.BasicDresser[type])
                _categoryOfType[type] = Containers;
        }
        Mark(Containers, TileID.Pots, TileID.PotsEcho);
        Mark(Gems, TileID.Sapphire, TileID.Ruby, TileID.Emerald, TileID.Topaz, TileID.Amethyst,
            TileID.Diamond, TileID.ExposedGems, TileID.Crystals, TileID.AmberStoneBlock);
        Mark(Treasure, TileID.Heart, TileID.ManaCrystal, TileID.LifeFruit, TileID.ShadowOrbs,
            TileID.PlanteraBulb, TileID.Larva);
        Mark(Plants, TileID.MatureHerbs, TileID.BloomingHerbs, TileID.DyePlants);
        // Trees, each its own entry with its height in tiles.
        for (var type = 0; type < types; type++)
            if (TileID.Sets.IsATreeTrunk[type] || type == TileID.PalmTree)
                _categoryOfType[type] = Plants;
        Mark(Hazards, TileID.Spikes, TileID.WoodenSpikes, TileID.Traps, TileID.Boulder, TileID.Explosives,
            TileID.PressurePlates, TileID.GeyserTrap, TileID.LandMine);

        Place("Dungeon", 200, TileID.BlueDungeonBrick, TileID.GreenDungeonBrick, TileID.PinkDungeonBrick);
        Place("Jungle temple", 100, TileID.LihzahrdBrick);
        Place("Bee hive", 30, TileID.Hive);
        Place("Floating island", 30, TileID.Cloud, TileID.RainCloud);
        Place("Marble cave", 100, TileID.Marble);
        Place("Granite cave", 100, TileID.Granite);
        Place("Glowing mushrooms", 20, TileID.MushroomGrass);
        Place("Living tree", 40, TileID.LivingWood);
        Place("Jungle", 200, TileID.JungleGrass);
        Place("Snow", 300, TileID.SnowBlock, TileID.IceBlock);
        Place("Corruption", 50, TileID.CorruptGrass, TileID.Ebonstone);
        Place("Crimson", 50, TileID.CrimsonGrass, TileID.Crimstone);
        Place("Hallow", 50, TileID.HallowedGrass, TileID.Pearlstone);
        Place("Underground desert", 200, TileID.HardenedSand, TileID.Sandstone);
        Place("Desert", 300, TileID.Sand, TileID.Ebonsand, TileID.Crimsand, TileID.Pearlsand);
        _desert = _kinds.Count - 1;
        _spiderNest = Add(new ScanKind("Spider nest", Places, CellSize: 16, MinTiles: 100));

        // Buildings: what is made of worked wood or brick, or stands
        // in front of a wall somebody put up. The player's own, and
        // the cabins and ruins the world came with - which is where
        // its chests are. Last, so that a dungeon or a temple, which
        // are brick too, keep their own names.
        var built = new List<int>
        {
            TileID.WoodBlock, TileID.Ebonwood, TileID.RichMahogany, TileID.Pearlwood, TileID.Shadewood,
            TileID.SpookyWood, TileID.DynastyWood, TileID.BorealWood, TileID.PalmWood,
        };
        for (var type = 0; type < types; type++)
            if (_placeOfType[type] < 0 && _categoryOfType[type] == null && TileID.Search.TryGetName(type, out var name)
                && name.Contains("Brick", StringComparison.Ordinal))
                built.Add(type);
        _building = Add(new ScanKind("Building", Places, CellSize: 16, MinTiles: 8));
        foreach (var type in built)
            if (_placeOfType[type] < 0)
                _placeOfType[type] = _building;

        _liquidKinds = new[] { "Water", "Lava", "Honey", "Shimmer" }
            .Select(name => Add(new ScanKind(name, Liquids, CellSize: 2, MinTiles: 40)))
            .ToArray();
        // Lava is a hazard before it is a liquid: any of it burns, a
        // film on the floor as much as a lake, so none is too small
        // or too shallow to list.
        _lava = Add(new ScanKind("Lava", Hazards, CellSize: 2, MinTiles: 1));
    }

    private void Mark(string category, params int[] types)
    {
        foreach (var type in types)
            _categoryOfType[type] = category;
    }

    private void Place(string name, int minTiles, params int[] types)
    {
        var kind = Add(new ScanKind(name, Places, CellSize: 16, MinTiles: minTiles));
        foreach (var type in types)
        {
            _placeOfType[type] = kind;
            _categoryOfType[type] = null;
        }
    }

    private int Add(ScanKind kind)
    {
        _kinds.Add(kind);
        return _kinds.Count - 1;
    }

    public int Width => Main.maxTilesX;
    public int Height => Main.maxTilesY;
    public IReadOnlyList<ScanKind> Kinds => _kinds;

    public int KindAt(int x, int y)
    {
        if (!TerrariaPowers.Knows(x, y))
            return -1;
        var tile = Main.tile[x, y];
        if (tile.HasTile)
        {
            int type = tile.TileType;
            if (type < _placeOfType.Length)
            {
                var place = _placeOfType[type];
                if (place == _desert && !SurfaceDesert(x, y))
                    place = -1;
                if (place >= 0)
                    return place;
                if (_categoryOfType[type] is { } category)
                    return ThingAt(x, y, tile, type, category);
            }
            if (Main.tileSolid[type] && !Main.tileSolidTop[type])
                return -1;
        }
        if (tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Lava)
            return _lava;
        if (tile.LiquidAmount >= 128)
            return _liquidKinds[Math.Min(tile.LiquidType, _liquidKinds.Length - 1)];
        if (tile.WallType == WallID.SpiderUnsafe)
            return _spiderNest;
        // A wall somebody put up: the inside of a building.
        return tile.WallType > 0 && tile.WallType < Main.wallHouse.Length && Main.wallHouse[tile.WallType] ? _building : -1;
    }

    private int ThingAt(int x, int y, Tile tile, int type, string category)
    {
        // Ores and gems are veins, and spikes and traps come in runs;
        // everything else here is an object,
        // reported at its top-left tile only.
        var gathers = category is Ores or Gems or Hazards || TileID.Sets.IsATreeTrunk[type] || type == TileID.PalmTree;
        var data = gathers ? null : TileObjectData.GetTileData(tile);
        if (data != null
            && (tile.TileFrameX % data.CoordinateFullWidth != 0 || tile.TileFrameY % data.CoordinateFullHeight != 0))
            return -1;

        var option = 0;
        MapHelper.GetTileBaseOption(x, y, type, tile, ref option);
        var lookup = MapHelper.TileToLookup(type, option);
        if (_kindOfLookup.TryGetValue(lookup, out var kind))
            return kind;
        // An object the game gives no dimensions for is gathered in
        // small squares instead, so each still reads as one entry.
        var cellSize = gathers ? 2 : data != null ? 0 : 2;
        kind = Add(new ScanKind(TileNames.Of(type, option, lookup), category, cellSize, Sized: gathers));
        _kindOfLookup[lookup] = kind;
        return kind;
    }
}
