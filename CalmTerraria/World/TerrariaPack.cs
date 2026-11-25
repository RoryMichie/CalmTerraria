using CalmTerraria.Pack;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.UI;

namespace CalmTerraria.World;

/// <summary>The local player's belongings, for the pack screen. Every
/// action goes through what the game's own inventory does for the
/// same click, so its rules - what may be worn where, what a swap
/// displaces - stay the game's.</summary>
internal sealed class TerrariaPack : IPack
{
    /// <summary>The main inventory, coins and ammunition; the slot
    /// after them is the mouse's.</summary>
    private const int Slots = 58;
    private const int HotbarSlots = 10;

    private static Player Player => Main.LocalPlayer;

    public int FreeSlots
    {
        get
        {
            var free = 0;
            for (var slot = 0; slot < 50; slot++)
                if (Player.inventory[slot].IsAir)
                    free++;
            return free;
        }
    }

    public IReadOnlyList<PackStack> Stacks()
    {
        var stacks = new List<PackStack>();
        var inventory = Player.inventory;
        for (var slot = 0; slot < Slots; slot++)
        {
            var item = inventory[slot];
            if (!item.IsAir)
                stacks.Add(new PackStack(slot, item.AffixName(), item.stack, Wearable(item),
                    slot >= 54 ? "ammo slot" : slot >= 50 ? "coin slot" : null));
        }
        return stacks;
    }

    private static bool Wearable(Item item) =>
        item.headSlot >= 0 || item.bodySlot >= 0 || item.legSlot >= 0 || item.accessory
        || (item.shoot > 0 && Main.projHook[item.shoot])
        || item.mountType != -1
        || (item.buffType > 0 && (Main.vanityPet[item.buffType] || Main.lightPet[item.buffType]));

    public void Hold(int slot)
    {
        // Only the hotbar can be held from. Anything else changes
        // places with what is in the hand, until another slot is chosen.
        Hand.Take(slot);
        SoundEngine.PlaySound(SoundID.Grab);
    }

    public string Describe(int slot)
    {
        var item = Player.inventory[slot];
        var about = ItemText.Describe(item);
        // With a shop open, what it would fetch comes first.
        if (!TerrariaShop.IsOpen || item.IsAir)
            return about;
        return about.Length > 0 ? $"{Worth(item)} {about}" : Worth(item);
    }

    /// <summary>What a shop gives for one of the item, by the game's
    /// own sum: a fifth of its value, or what was paid for it if it
    /// was bought here just now.</summary>
    private static string Worth(Item item)
    {
        Player.GetItemExpectedPrice(item, out var selling, out var buying);
        if (selling <= 0)
            return "Worth nothing.";
        var each = Main.shopSellbackHelper.GetAmount(item) > 0 ? buying : Math.Max(1, selling / 5);
        return item.stack > 1 ? $"Sells for {TerrariaShop.Coins(each)} each." : $"Sells for {TerrariaShop.Coins(each)}.";
    }

    public void ToHotbar(int slot, int hotbar)
    {
        var inventory = Player.inventory;
        if (slot != hotbar)
            Utils.Swap(ref inventory[slot], ref inventory[hotbar]);
        SoundEngine.PlaySound(SoundID.Grab);
    }

    /// <summary>Set by whoever owns the speech, for what is only
    /// known after the screen has moved on.</summary>
    public static Action<string>? Say { get; set; }

    public UseResult Use(int slot)
    {
        var item = Player.inventory[slot];
        if (item.IsAir || item.useStyle <= 0)
            return UseResult.NotUsable;
        // Nothing is used while the game stands still.
        if (Main.gamePaused)
            return UseResult.Paused;
        if (Player.itemAnimation > 0 || ItemUse.Active)
            return UseResult.Busy;
        return ItemUse.Begin(slot, text => Say?.Invoke(text)) ? UseResult.Started : UseResult.Busy;
    }

    public bool Open(int slot)
    {
        var inventory = Player.inventory;
        var item = inventory[slot];
        // Only what the game opens on a right-click. On anything else
        // the same click would pick one up onto the mouse.
        if (item.IsAir || !Terraria.ModLoader.ItemLoader.CanRightClick(item) || !Main.mouseItem.IsAir || Player.itemAnimation > 0)
            return false;
        var (right, release) = (Main.mouseRight, Main.mouseRightRelease);
        Main.mouseRight = true;
        Main.mouseRightRelease = true;
        try
        {
            ItemSlot.RightClick(inventory, ItemSlot.Context.InventoryItem, slot);
        }
        finally
        {
            (Main.mouseRight, Main.mouseRightRelease) = (right, release);
        }
        Recipe.FindRecipes();
        return true;
    }

    public bool Equip(int slot)
    {
        var inventory = Player.inventory;
        var before = inventory[slot];
        // The game's right-click on an inventory item.
        ItemSlot.SwapEquip(inventory, ItemSlot.Context.InventoryItem, slot);
        return !ReferenceEquals(before, inventory[slot]);
    }

    public void Trash(int slot)
    {
        var player = Player;
        // The game's trash holds one item, recoverable until the next.
        player.trashItem = player.inventory[slot].Clone();
        player.inventory[slot].TurnToAir();
        SoundEngine.PlaySound(SoundID.Grab);
        Recipe.FindRecipes();
    }

    private static readonly string[] MiscNames = { "Pet", "Light pet", "Minecart", "Mount", "Hook" };

    /// <summary>The equipment slots in the order they are listed:
    /// which of the player's arrays, and where in it.</summary>
    private static List<(string Name, Item[] Array, int Index)> EquipSlots()
    {
        var player = Player;
        var slots = new List<(string, Item[], int)>
        {
            ("Head", player.armor, 0),
            ("Body", player.armor, 1),
            ("Legs", player.armor, 2),
        };
        var accessory = 1;
        for (var i = 3; i < 10; i++)
        {
            if (player.IsItemSlotUnlockedAndUsable(i))
                slots.Add(($"Accessory {accessory++}", player.armor, i));
        }
        for (var i = 0; i < MiscNames.Length; i++)
            slots.Add((MiscNames[i], player.miscEquips, i));
        return slots;
    }

    public IReadOnlyList<(string Slot, string? Item)> Equipment() =>
        EquipSlots()
            .Select(slot => (slot.Name, slot.Array[slot.Index].IsAir ? null : slot.Array[slot.Index].AffixName()))
            .ToList();

    public bool Unequip(int index)
    {
        var (_, array, at) = EquipSlots()[index];
        var inventory = Player.inventory;
        // Into the pack before the hotbar, so the hand is not changed.
        var free = -1;
        for (var slot = HotbarSlots; slot < 50 && free < 0; slot++)
            if (inventory[slot].IsAir)
                free = slot;
        for (var slot = 0; slot < HotbarSlots && free < 0; slot++)
            if (inventory[slot].IsAir)
                free = slot;
        if (free < 0)
            return false;
        inventory[free] = array[at];
        array[at] = new Item();
        SoundEngine.PlaySound(SoundID.Grab);
        Recipe.FindRecipes();
        return true;
    }
}
