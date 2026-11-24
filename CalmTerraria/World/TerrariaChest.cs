using CalmTerraria.Pack;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.UI;

namespace CalmTerraria.World;

/// <summary>Whatever container the local player has open: a chest in
/// the world, or one of their own. Every move goes through what the
/// game's chest screen calls, which is also what tells a server.</summary>
internal sealed class TerrariaChest : IChest
{
    private const int ChestSlots = 40;
    private const int SyncChestItem = 32;

    private static Player Player => Main.LocalPlayer;

    public static bool IsOpen => Player.chest != -1;

    public bool CanRename => Player.chest >= 0;

    public string Name
    {
        get
        {
            var chest = Player.chest;
            switch (chest)
            {
                case -2: return "Piggy bank";
                case -3: return "Safe";
                case -4: return "Defender's forge";
                case -5: return "Void vault";
            }
            if (chest < 0 || Main.chest[chest] is not { } opened)
                return "Chest";
            if (!string.IsNullOrWhiteSpace(opened.name))
                return opened.name;
            return TileNames.At(opened.x, opened.y, Main.tile[opened.x, opened.y]);
        }
    }

    private static Item[] Contents(out bool sync)
    {
        ChestUI.GetContainerUsageInfo(out sync, out var items);
        return items;
    }

    public IReadOnlyList<PackStack> Stacks()
    {
        var stacks = new List<PackStack>();
        if (!IsOpen)
            return stacks;
        var items = Contents(out _);
        for (var slot = 0; slot < ChestSlots && slot < items.Length; slot++)
        {
            if (!items[slot].IsAir)
                stacks.Add(new PackStack(slot, items[slot].AffixName(), items[slot].stack, false));
        }
        return stacks;
    }

    public bool Take(int slot)
    {
        if (!IsOpen)
            return false;
        var items = Contents(out var sync);
        var player = Player;
        items[slot].position = player.Center;
        items[slot] = player.GetItem(Main.myPlayer, items[slot], GetItemSettings.LootAllSettings);
        if (sync)
            NetMessage.SendData(SyncChestItem, -1, -1, null, player.chest, slot);
        Recipe.FindRecipes();
        return items[slot].IsAir;
    }

    public bool Deposit(int packSlot)
    {
        if (!IsOpen)
            return false;
        var item = Player.inventory[packSlot];
        // The game's shift-click on an inventory item with a chest open.
        ChestUI.TryPlacingInChest(item, justCheck: false, ItemSlot.Context.InventoryItem);
        Recipe.FindRecipes();
        return item.IsAir;
    }

    public void LootAll()
    {
        ChestUI.LootAll();
        Recipe.FindRecipes();
    }

    public void DepositAll()
    {
        ChestUI.DepositAll(ContainerTransferContext.FromUnknown(Player));
        Recipe.FindRecipes();
    }

    public void QuickStack()
    {
        ChestUI.QuickStack(ContainerTransferContext.FromUnknown(Player));
        Recipe.FindRecipes();
    }

    public void Restock()
    {
        ChestUI.Restock();
        Recipe.FindRecipes();
    }

    public void Rename(string name)
    {
        var player = Player;
        if (player.chest < 0 || Main.chest[player.chest] is not { } chest || chest.name == name)
            return;
        chest.name = name;
        // The game sends the new name when it sees this.
        if (Main.netMode == NetmodeID.MultiplayerClient)
            player.editedChestName = true;
    }
}

/// <summary>What the local player can craft, as the game works it
/// out: from what they carry and what they stand beside.</summary>
internal sealed class TerrariaCrafting : ICrafting
{
    public IReadOnlyList<Craftable> Recipes()
    {
        var recipes = new List<Craftable>(Main.numAvailableRecipes);
        for (var i = 0; i < Main.numAvailableRecipes; i++)
        {
            var id = Main.availableRecipe[i];
            var recipe = Main.recipe[id];
            recipes.Add(new Craftable(id, recipe.createItem.Name, recipe.createItem.stack, Needs(recipe)));
        }
        return recipes;
    }

    private static string Needs(Recipe recipe) =>
        string.Join(", ", recipe.requiredItem.Select(item =>
            $"{item.stack} {(recipe.ProcessGroupsForText(item.type, out var group) ? group : item.Name)}"));

    public bool Craft(int id)
    {
        var available = false;
        for (var i = 0; i < Main.numAvailableRecipes && !available; i++)
            available = Main.availableRecipe[i] == id;
        // The game crafts onto the mouse; it has to be free to take it.
        if (!available || !Main.mouseItem.IsAir)
            return false;
        var player = Main.LocalPlayer;
        Main.CraftItem(Main.recipe[id]);
        var made = Main.mouseItem;
        Main.mouseItem = new Item();
        if (!made.IsAir)
        {
            made.position = player.Center;
            var left = player.GetItem(Main.myPlayer, made, GetItemSettings.InventoryUIToInventorySettings);
            // No room: it falls at the player's feet, as it would
            // from a closed inventory.
            if (!left.IsAir)
                player.QuickSpawnItem(new EntitySource_Misc("CalmTerraria crafting"), left, left.stack);
        }
        Recipe.FindRecipes();
        return true;
    }
}
