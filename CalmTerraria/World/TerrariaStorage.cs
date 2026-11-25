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
/// Single player only. On a server this computer is told what is in a
/// chest only while the character has it open, one chest at a time, so
/// there is nothing to list.</summary>
internal sealed class TerrariaStorage : IChest
{
    /// <summary>How far to each side, and above and below, a chest
    /// counts as near, in tiles.</summary>
    public const int Across = 50, UpDown = 30;

    private const int ChestSlots = 40;

    private static Player Player => Main.LocalPlayer;

    private static List<int> _near = new();
    private static uint _nearAt = uint.MaxValue;
    private static (int X, int Y) _nearFrom;
    private static bool _nearEverywhere;

    /// <summary>The chests in reach, nearest first. Looked for once
    /// for each place the character stands, however often it is asked.</summary>
    public static IReadOnlyList<int> Chests()
    {
        if (Main.netMode != NetmodeID.SinglePlayer || Main.gameMenu)
            return Array.Empty<int>();
        var at = WorldScanner.PlayerTile();
        // The power: every chest in the world, wherever the character is.
        var everywhere = TerrariaPowers.StorageEverywhere;
        if (_nearAt == Main.GameUpdateCount && _nearFrom == at && _nearEverywhere == everywhere)
            return _near;
        _nearEverywhere = everywhere;
        var found = new List<int>();
        for (var i = 0; i < Main.maxChests; i++)
        {
            if (Main.chest[i] is not { } chest)
                continue;
            if (!everywhere && (Math.Abs(chest.x - at.X) > Across || Math.Abs(chest.y - at.Y) > UpDown))
                continue;
            if (!WorldGen.InWorld(chest.x, chest.y) || Chest.IsLocked(chest.x, chest.y))
                continue;
            found.Add(i);
        }
        found.Sort((a, b) => Distance(a, at).CompareTo(Distance(b, at)));
        _near = found;
        _nearAt = Main.GameUpdateCount;
        _nearFrom = at;
        return found;
    }

    private static int Distance(int chest, (int X, int Y) from) =>
        Math.Abs(Main.chest[chest].x - from.X) + Math.Abs(Main.chest[chest].y - from.Y);

    public static bool Available => Chests().Count > 0;

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
            var count = Chests().Count;
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
                foreach (var held in Main.chest[chest].item)
                {
                    if (held.IsAir || held.type != going.type || held.stack >= held.maxStack)
                        continue;
                    ItemLoader.TryStackItems(held, going, out _);
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
                going.TurnToAir();
                return;
            }
        }
    }

    public string Describe(int slot) => ItemText.Describe(At(slot));

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
                foreach (var stored in Main.chest[chest].item)
                {
                    if (stored.IsAir || stored.type != carried.type || carried.stack >= carried.maxStack)
                        continue;
                    ItemLoader.TryStackItems(carried, stored, out _);
                    if (stored.stack <= 0)
                        stored.TurnToAir();
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
        };
        return Player.whoAmI == Main.myPlayer ? TerrariaStorage.Materials().ToList() : null!;
    }
}
