using Terraria;

namespace CalmTerraria.World;

/// <summary>Holding something that is not in the hotbar. The game
/// holds only from the hotbar, so the thing changes places with what
/// is in the selected slot - but as a guest: when the player selects
/// another slot, or something else is held, or the job that wanted
/// the tool is done, the two change back, and the hotbar is as the
/// player arranged it. A slot that was empty simply keeps the thing.</summary>
internal static class Hand
{
    /// <summary>The hotbar slot with a guest in it, the slot the
    /// guest came from, and what it turned out of the hotbar.</summary>
    private static (int Hot, int Back, Item Displaced)? _guest;

    /// <summary>Hold what is in this inventory slot.</summary>
    public static void Take(int slot)
    {
        var player = Main.LocalPlayer;
        var inventory = player.inventory;
        if (_guest is { } guest)
        {
            // Already in the hand.
            if (slot == guest.Hot && player.selectedItem == guest.Hot)
                return;
            // What the guest turned out is wanted: once they change
            // back it is in the hotbar again.
            var returned = Return();
            if (returned && slot == guest.Back)
                slot = guest.Hot;
            else if (returned && slot == guest.Hot)
                slot = guest.Back;
        }
        if (player.selectedItem >= 10)
            player.selectedItem = 0;
        if (slot < 10)
        {
            player.selectedItem = slot;
            return;
        }
        var displaced = inventory[player.selectedItem];
        Utils.Swap(ref inventory[slot], ref inventory[player.selectedItem]);
        if (!displaced.IsAir)
            _guest = (player.selectedItem, slot, displaced);
    }

    /// <summary>The guest goes back where it came from, if things
    /// are still where they were put. True when they changed back.</summary>
    public static bool Return()
    {
        if (_guest is not { } guest)
            return false;
        _guest = null;
        var inventory = Main.LocalPlayer.inventory;
        if (!ReferenceEquals(inventory[guest.Back], guest.Displaced))
            return false;
        Utils.Swap(ref inventory[guest.Hot], ref inventory[guest.Back]);
        return true;
    }

    /// <summary>Once a frame in a world: another slot was selected.</summary>
    public static void Update()
    {
        if (_guest is { } guest && Main.LocalPlayer.selectedItem != guest.Hot && Main.LocalPlayer.itemAnimation == 0)
            Return();
    }
}