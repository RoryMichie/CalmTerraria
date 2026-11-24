using CalmTerraria.Powers;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalmTerraria.World;

/// <summary>The powers, done to the game. Omniscience is remembered
/// between sessions, since it is a way of playing; god mode is not,
/// since it is a thing to switch on for a while.</summary>
internal sealed class TerrariaPowers : IPowers
{
    private static string SettingsPath => Path.Combine(Main.SavePath, "CalmTerraria", "omniscience.txt");

    private static bool? _omniscient;

    public static bool Omniscient
    {
        get => _omniscient ??= File.Exists(SettingsPath);
        private set
        {
            _omniscient = value;
            if (value)
                File.WriteAllText(SettingsPath, "The scanner and the cursor know the whole world.");
            else
                File.Delete(SettingsPath);
        }
    }

    /// <summary>Whether the player is allowed to know what is on this
    /// tile: it is on their map, or they know everything.</summary>
    public static bool Knows(int x, int y) => Omniscient || Main.Map.IsRevealed(x, y);

    public static bool GodModeOn { get; set; }

    public bool Omniscience
    {
        get => Omniscient;
        set => Omniscient = value;
    }

    public bool GodMode
    {
        get => GodModeOn;
        set => GodModeOn = value;
    }

    private Dictionary<string, int>? _items, _creatures;
    private List<string>? _itemNames, _creatureNames;

    public IReadOnlyList<string> Items()
    {
        if (_itemNames == null)
        {
            _items = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var type = 1; type < ItemLoader.ItemCount; type++)
            {
                if (ItemID.Sets.Deprecated[type])
                    continue;
                var name = Lang.GetItemNameValue(type);
                if (!string.IsNullOrWhiteSpace(name))
                    _items.TryAdd(name, type);
            }
            _itemNames = _items.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        return _itemNames;
    }

    public int FullStack(string item) => ContentSamples.ItemsByType[_items![item]].maxStack;

    public void Give(string item, int count) =>
        Main.LocalPlayer.QuickSpawnItem(new EntitySource_DebugCommand("CalmTerraria powers"), _items![item], count);

    public IReadOnlyList<string> Creatures()
    {
        if (_creatureNames == null)
        {
            _creatures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var type = 1; type < NPCLoader.NPCCount; type++)
            {
                var name = Lang.GetNPCNameValue(type);
                // Unnamed internals read "???": nothing to ask for.
                if (name.Any(char.IsLetter))
                    _creatures.TryAdd(name, type);
            }
            _creatureNames = _creatures.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        return _creatureNames;
    }

    /// <summary>How far off a spawned creature appears, in tiles.</summary>
    private const int SpawnDistance = 12;

    public bool Spawn(string creature)
    {
        // On a server the world's creatures are the server's to make.
        if (Main.netMode != NetmodeID.SinglePlayer)
            return false;
        var player = Main.LocalPlayer;
        NPC.NewNPC(new EntitySource_DebugCommand("CalmTerraria powers"),
            (int)player.Center.X + player.direction * SpawnDistance * 16, (int)player.Bottom.Y, _creatures![creature]);
        return true;
    }

    private static IEnumerable<Player> Others() =>
        Main.player.Where(player => player is { active: true } && player.whoAmI != Main.myPlayer);

    public IReadOnlyList<string> Players() =>
        Others().Select(player => player.name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();

    public bool TeleportTo(string player)
    {
        var target = Others().FirstOrDefault(other => other.name == player);
        if (target == null || target.dead)
            return false;
        // The game's own player-to-player teleport, as a wormhole
        // potion does it: it is the server that moves the character.
        Main.LocalPlayer.UnityTeleport(target.position);
        return true;
    }

    public IReadOnlyList<string> Waypoints() =>
        World.Waypoints.Instance?.Points.Select(point => point.Name).ToList() ?? new List<string>();

    public bool TeleportToWaypoint(string waypoint) => World.Waypoints.Instance?.TeleportTo(waypoint) == true;

    public bool SetTime(bool day)
    {
        if (Main.netMode != NetmodeID.SinglePlayer)
            return false;
        Main.dayTime = day;
        Main.time = 0;
        return true;
    }
}

/// <summary>God mode has to be said again every frame: the game
/// forgets it as each frame's effects are reset.</summary>
internal sealed class PowersPlayer : ModPlayer
{
    public override void ResetEffects()
    {
        if (TerrariaPowers.GodModeOn && Player.whoAmI == Main.myPlayer)
            Player.creativeGodMode = true;
    }
}
