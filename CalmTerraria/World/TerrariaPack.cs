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

    public IReadOnlyList<PackStack> Stacks()
    {
        var stacks = new List<PackStack>();
        var inventory = Player.inventory;
        for (var slot = 0; slot < Slots; slot++)
        {
            var item = inventory[slot];
            if (!item.IsAir)
                stacks.Add(new PackStack(slot, item.AffixName(), item.stack, Wearable(item)));
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
        var player = Player;
        // Only the hotbar can be held from. Anything else changes
        // places with what is in the hand now.
        if (player.selectedItem >= HotbarSlots)
            player.selectedItem = 0;
        if (slot < HotbarSlots)
            player.selectedItem = slot;
        else
            Utils.Swap(ref player.inventory[slot], ref player.inventory[player.selectedItem]);
        SoundEngine.PlaySound(SoundID.Grab);
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
