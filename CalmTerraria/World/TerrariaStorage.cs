using CalmTerraria.Pack;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalmTerraria.World;

/// <summary>Every chest near the character, as one container: what
/// is in all of them in one list, taken from and put away in without
/// walking to each and opening it, and crafted from where the
/// character stands.
///
/// Near is about what is on screen; with the omnipresent storage
/// power it is every chest in the world. Locked chests are left out. Things
/// put away go onto a stack of the same thing where there is one, then
/// into a chest that already holds that thing, then into the nearest
/// chest with room.
///
/// On a server this computer is told what is in a chest only when the
/// character opens it. So while the pack screen is up, each chest in
/// reach is opened and shut again in one breath - the server does not
/// mind how far away the character is - and what it held is what is
/// listed. Nothing is drawn or heard of that here; other players see
/// the lids move. A chest somebody has open is left out until they
/// shut it, and is then looked into afresh. Chests rigged to a trap
/// are never touched, since opening one sets it off.
///
/// Two players doing this at once must not work from old lists. The
/// server tells everyone when a chest is opened, though not what was
/// done: so a change is followed by opening the chest twice running,
/// which nobody's hands do, and anyone who sees that looks again.</summary>
internal sealed class TerrariaStorage : IChest
{
    /// <summary>How far to each side, and above and below, a chest
    /// counts as near, in tiles.</summary>
    public const int Across = 50, UpDown = 30;

    private const int ChestSlots = 40;

    private static Player Player => Main.LocalPlayer;

    private const int RequestChestOpen = 31, SyncChestItem = 32, SyncPlayerChest = 33, SyncPlayerChestIndex = 80;

    private static bool Remote => Main.netMode == NetmodeID.MultiplayerClient;

    private static List<int> _near = new();
    private static List<int> _usable = new();
    private static uint _nearAt = uint.MaxValue;
    private static (int X, int Y) _nearFrom;
    private static bool _nearEverywhere;

    /// <summary>On a server: the pack screen is up with storage in it.</summary>
    private static bool _active;
    /// <summary>On a server: the chests whose contents are known.</summary>
    private static readonly HashSet<int> _known = new();
    /// <summary>Chests asked for, with how many answers are still to
    /// come and when the last was asked.</summary>
    private static readonly Dictionary<int, (int Answers, uint At)> _pending = new();
    /// <summary>Asked for and never answered: not asked again while
    /// the screen is up.</summary>
    private static readonly HashSet<int> _silent = new();
    /// <summary>Changed this frame: the others are told at its end.</summary>
    private static readonly HashSet<int> _written = new();
    /// <summary>A chest's contents arrived: what can be made has changed.</summary>
    private static bool _recipesStale;
    /// <summary>The chest each other player last opened, and when.</summary>
    private static readonly Dictionary<int, (int Chest, uint At)> _opened = new();

    /// <summary>The chests in reach, nearest first, whether or not
    /// their contents are known. Looked for once for each place the
    /// character stands, however often it is asked.</summary>
    private static List<int> Near()
    {
        if (Main.netMode == NetmodeID.Server || Main.gameMenu)
            return _near = new();
        var at = WorldScanner.PlayerTile();
        // The power: every chest in the world, wherever the character is.
        var everywhere = TerrariaPowers.StorageEverywhere;
        if (_nearAt == Main.GameUpdateCount && _nearFrom == at && _nearEverywhere == everywhere)
            return _near;
        _nearEverywhere = everywhere;
        var remote = Remote;
        var found = new List<int>();
        for (var i = 0; i < Main.maxChests; i++)
        {
            if (Main.chest[i] is not { } chest)
                continue;
            if (!everywhere && (Math.Abs(chest.x - at.X) > Across || Math.Abs(chest.y - at.Y) > UpDown))
                continue;
            if (!WorldGen.InWorld(chest.x, chest.y) || Chest.IsLocked(chest.x, chest.y))
                continue;
            // Opening is how a server is asked, and opening these
            // springs the trap; and one in use is somebody else's.
            if (remote && (WorldGen.IsChestRigged(chest.x, chest.y) || Chest.UsingChest(i) != -1))
                continue;
            found.Add(i);
        }
        found.Sort((a, b) => Distance(a, at).CompareTo(Distance(b, at)));
        _near = found;
        _usable = remote ? found.Where(chest => _active && _known.Contains(chest)).ToList() : found;
        _nearAt = Main.GameUpdateCount;
        _nearFrom = at;
        return found;
    }

    /// <summary>The chests that can be used now: all those in reach,
    /// or on a server those of them whose contents are known.</summary>
    public static IReadOnlyList<int> Chests()
    {
        Near();
        return _usable;
    }

    private static void Forget() => _nearAt = uint.MaxValue;

    /// <summary>The pack screen is going up with nothing else in
    /// front: on a server, everything in reach is looked into afresh.</summary>
    public static void Begin()
    {
        if (!Remote)
            return;
        _active = true;
        _known.Clear();
        _silent.Clear();
        Forget();
    }

    public static void End()
    {
        if (!_active)
            return;
        Flush();
        _active = false;
        Forget();
        Recipe.FindRecipes();
    }

    /// <summary>Ask the server what is in a chest, as opening it
    /// does, and shut it again at once.</summary>
    private static void Ask(int chest)
    {
        NetMessage.SendData(RequestChestOpen, -1, -1, null, Main.chest[chest].x, Main.chest[chest].y);
        NetMessage.SendData(SyncPlayerChest, -1, -1, null, -1);
        _pending[chest] = (_pending.GetValueOrDefault(chest).Answers + 1, Main.GameUpdateCount);
    }

    /// <summary>A slot of a chest was changed here: the server is told.</summary>
    private static void Wrote(int chest, int slot)
    {
        if (!Remote)
            return;
        NetMessage.SendData(SyncChestItem, -1, -1, null, chest, slot);
        _written.Add(chest);
    }

    /// <summary>The sign to other players that chests were changed:
    /// each opened twice running. The answers are the chest as the
    /// server now has it.</summary>
    private static void Flush()
    {
        foreach (var chest in _written)
        {
            Ask(chest);
            Ask(chest);
        }
        _written.Clear();
    }

    /// <summary>Once a frame in a world.</summary>
    public static void Tick()
    {
        if (!Remote)
            return;
        Flush();
        if (_recipesStale)
        {
            _recipesStale = false;
            Recipe.FindRecipes();
        }
        var now = Main.GameUpdateCount;
        foreach (var (chest, (_, at)) in _pending.ToList())
        {
            if (now - at < 180)
                continue;
            _pending.Remove(chest);
            _silent.Add(chest);
        }
        if (!_active)
            return;
        // Somebody has it open: what they do in it is not told.
        for (var i = 0; i < Main.maxPlayers; i++)
            if (i != Main.myPlayer && Main.player[i] is { active: true, chest: >= 0 } other && _known.Remove(other.chest))
                Forget();
        // A few at a time, so a world of chests is not one burst.
        var budget = 20;
        foreach (var chest in Near())
        {
            if (_known.Contains(chest) || _pending.ContainsKey(chest) || _silent.Contains(chest))
                continue;
            Ask(chest);
            if (--budget == 0)
                break;
        }
    }

    /// <summary>A message from the server, before the game has it.
    /// True when it was an answer to this storage's own asking, which
    /// the game must not see: it would take the chest for opened.</summary>
    public static bool Hear(byte type, BinaryReader reader)
    {
        if (!Remote || (type != SyncPlayerChest && type != SyncPlayerChestIndex))
            return false;
        var start = reader.BaseStream.Position;
        try
        {
            if (type == SyncPlayerChest)
            {
                int chest = reader.ReadInt16();
                if (!_pending.TryGetValue(chest, out var asked))
                    return false;
                if (asked.Answers <= 1)
                    _pending.Remove(chest);
                else
                    _pending[chest] = (asked.Answers - 1, asked.At);
                _known.Add(chest);
                Forget();
                // Not here, in the middle of a message; and not left
                // to the game, which looks again only while drawing
                // its own inventory.
                _recipesStale = true;
                return true;
            }
            int player = reader.ReadByte();
            int opened = reader.ReadInt16();
            if (player == Main.myPlayer || opened < 0)
                return false;
            // Twice running: they changed it.
            if (_opened.TryGetValue(player, out var last) && last.Chest == opened && Main.GameUpdateCount - last.At < 30)
            {
                _known.Remove(opened);
                _silent.Remove(opened);
                Forget();
            }
            _opened[player] = (opened, Main.GameUpdateCount);
            return false;
        }
        finally
        {
            // Left where it was found, for the game to read.
            reader.BaseStream.Position = start;
        }
    }
    private static int Distance(int chest, (int X, int Y) from) =>
        Math.Abs(Main.chest[chest].x - from.X) + Math.Abs(Main.chest[chest].y - from.Y);

    /// <summary>On a server, before anything is known: there are
    /// chests to ask about.</summary>
    public static bool Available => (Remote ? Near().Count : Chests().Count) > 0;

    /// <summary>What crafting may use besides what is carried: all of
    /// it, but for a chest that is open, which the game counts itself.</summary>
    public static IEnumerable<Item> Materials()
    {
        foreach (var chest in Chests())
        {
            if (chest == Player.chest)
                continue;
            foreach (var item in Main.chest[chest].item)
                if (item is { IsAir: false })
                    yield return item;
        }
    }

    public string Name
    {
        get
        {
            var count = Remote ? Near().Count : Chests().Count;
            return count == 1 ? "Storage, 1 chest" : $"Storage, {count} chests";
        }
    }

    public bool CanRename => false;

    private static Item At(int slot) => Main.chest[slot / ChestSlots].item[slot % ChestSlots];

    public IReadOnlyList<PackStack> Stacks()
    {
        var stacks = new List<PackStack>();
        foreach (var chest in Chests())
        {
            var items = Main.chest[chest].item;
            for (var slot = 0; slot < ChestSlots && slot < items.Length; slot++)
                if (items[slot] is { IsAir: false } item)
                    stacks.Add(new PackStack(chest * ChestSlots + slot, item.AffixName(), item.stack, false));
        }
        return stacks;
    }

    public int Take(int slot, int count)
    {
        var stack = At(slot);
        if (stack.IsAir)
            return 0;
        var player = Player;
        count = Math.Min(count, stack.stack);
        // The part that is going, as a stack of its own.
        var going = stack.Clone();
        going.stack = count;
        going.position = player.Center;
        var left = player.GetItem(Main.myPlayer, going, GetItemSettings.LootAllSettings);
        var moved = count - (left.IsAir ? 0 : left.stack);
        stack.stack -= moved;
        if (stack.stack <= 0)
            stack.TurnToAir();
        if (moved > 0)
            Wrote(slot / ChestSlots, slot % ChestSlots);
        Recipe.FindRecipes();
        return moved;
    }

    public int TakeOne(int slot)
    {
        var type = At(slot).type;
        if (Take(slot, 1) == 0)
            return -1;
        // Where it went: the smallest stack of it, so a lone one that
        // found an empty slot is the one used.
        var inventory = Player.inventory;
        var at = -1;
        for (var i = 0; i < 50; i++)
            if (inventory[i].type == type && (at < 0 || inventory[i].stack < inventory[at].stack))
                at = i;
        return at;
    }

    public int Deposit(int packSlot, int count)
    {
        var moved = Store(packSlot, count);
        if (moved > 0)
            Terraria.Audio.SoundEngine.PlaySound(SoundID.Grab);
        Recipe.FindRecipes();
        return moved;
    }

    private static int Store(int packSlot, int count)
    {
        var stack = Player.inventory[packSlot];
        if (stack.IsAir)
            return 0;
        count = Math.Min(count, stack.stack);
        var going = stack.Clone();
        going.stack = count;
        Put(going);
        var moved = count - (going.IsAir ? 0 : going.stack);
        stack.stack -= moved;
        if (stack.stack <= 0)
            stack.TurnToAir();
        return moved;
    }

    /// <summary>Put a stack away. What would not fit is left in it.</summary>
    private static void Put(Item going)
    {
        var chests = Chests();
        // Onto stacks of the same thing first.
        if (going.maxStack > 1)
        {
            foreach (var chest in chests)
            {
                var items = Main.chest[chest].item;
                for (var slot = 0; slot < ChestSlots && slot < items.Length; slot++)
                {
                    var held = items[slot];
                    if (held.IsAir || held.type != going.type || held.stack >= held.maxStack)
                        continue;
                    if (ItemLoader.TryStackItems(held, going, out _))
                        Wrote(chest, slot);
                    if (going.IsAir || going.stack <= 0)
                        return;
                }
            }
        }
        // Then a free slot: beside more of the same, or else nearest.
        var type = going.type;
        var ordered = chests.Where(chest => Main.chest[chest].item.Any(item => !item.IsAir && item.type == type))
            .Concat(chests.Where(chest => !Main.chest[chest].item.Any(item => !item.IsAir && item.type == type)));
        foreach (var chest in ordered)
        {
            var items = Main.chest[chest].item;
            for (var slot = 0; slot < ChestSlots && slot < items.Length; slot++)
            {
                if (!items[slot].IsAir)
                    continue;
                items[slot] = going.Clone();
                Wrote(chest, slot);
                going.TurnToAir();
                return;
            }
        }
    }

    public string Describe(int slot) => ItemText.Describe(At(slot));

    /// <summary>Crafting used some of this, out of one of the chests.</summary>
    public static void Used(Item item)
    {
        if (!Remote)
            return;
        foreach (var chest in Chests())
        {
            var at = Array.IndexOf(Main.chest[chest].item, item);
            if (at < 0)
                continue;
            Wrote(chest, at);
            return;
        }
    }

    public bool ToHotbar(int slot, int hotbar)
    {
        var items = Main.chest[slot / ChestSlots].item;
        var inventory = Player.inventory;
        if (items[slot % ChestSlots].IsAir)
            return false;
        if (!inventory[hotbar].IsAir)
        {
            // What was in the hotbar slot goes to the pack, not to storage.
            var free = -1;
            for (var i = 10; i < 50 && free < 0; i++)
                if (inventory[i].IsAir)
                    free = i;
            if (free < 0)
                return false;
            inventory[free] = inventory[hotbar];
        }
        inventory[hotbar] = items[slot % ChestSlots];
        items[slot % ChestSlots] = new Item();
        Wrote(slot / ChestSlots, slot % ChestSlots);
        Terraria.Audio.SoundEngine.PlaySound(SoundID.Grab);
        Recipe.FindRecipes();
        return true;
    }

    /// <summary>Not offered: everything from every chest would not fit.</summary>
    public void LootAll()
    {
    }

    /// <summary>The main inventory but for the hotbar and what is
    /// marked as a favourite.</summary>
    private static IEnumerable<int> Loose()
    {
        var inventory = Player.inventory;
        for (var slot = 10; slot < 50; slot++)
            if (!inventory[slot].IsAir && !inventory[slot].favorited)
                yield return slot;
    }

    public void DepositAll()
    {
        foreach (var slot in Loose().ToList())
            Store(slot, Player.inventory[slot].stack);
        Terraria.Audio.SoundEngine.PlaySound(SoundID.Grab);
        Recipe.FindRecipes();
    }

    /// <summary>Only what storage already has some of.</summary>
    public void QuickStack()
    {
        var kept = new HashSet<int>(Materials().Select(item => item.type));
        foreach (var slot in Loose().ToList())
            if (kept.Contains(Player.inventory[slot].type))
                Store(slot, Player.inventory[slot].stack);
        Terraria.Audio.SoundEngine.PlaySound(SoundID.Grab);
        Recipe.FindRecipes();
    }

    /// <summary>Stacks that are carried are topped up from storage.</summary>
    public void Restock()
    {
        var inventory = Player.inventory;
        for (var slot = 0; slot < 58; slot++)
        {
            var carried = inventory[slot];
            if (carried.IsAir || carried.maxStack <= 1 || carried.stack >= carried.maxStack)
                continue;
            foreach (var chest in Chests())
            {
                var items = Main.chest[chest].item;
                for (var at = 0; at < ChestSlots && at < items.Length; at++)
                {
                    var stored = items[at];
                    if (stored.IsAir || stored.type != carried.type || carried.stack >= carried.maxStack)
                        continue;
                    var moved = ItemLoader.TryStackItems(carried, stored, out _);
                    if (stored.stack <= 0)
                        stored.TurnToAir();
                    if (moved)
                        Wrote(chest, at);
                }
            }
        }
        Terraria.Audio.SoundEngine.PlaySound(SoundID.Grab);
        Recipe.FindRecipes();
    }

    public void Rename(string name)
    {
    }
}

/// <summary>Crafting uses what is in storage as it uses what is
/// carried: the game asks for more materials here, and takes what it
/// uses out of the chests itself.</summary>
internal sealed class StoragePlayer : ModPlayer
{
    public override IEnumerable<Item> AddMaterialsForCrafting(out ItemConsumedCallback itemConsumedCallback)
    {
        itemConsumedCallback = (item, _) =>
        {
            if (item.stack <= 0 && !item.IsAir)
                item.TurnToAir();
            TerrariaStorage.Used(item);
        };
        return Player.whoAmI == Main.myPlayer ? TerrariaStorage.Materials().ToList() : null!;
    }
}

/// <summary>Hears the server's answers about chests before the game does.</summary>
internal sealed class StorageNet : ModSystem
{
    public override bool HijackGetData(ref byte messageType, ref BinaryReader reader, int playerNumber) =>
        TerrariaStorage.Hear(messageType, reader);
}