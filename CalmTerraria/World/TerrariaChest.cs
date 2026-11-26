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

    public int Take(int slot, int count)
    {
        if (!IsOpen)
            return 0;
        var items = Contents(out var sync);
        var player = Player;
        var stack = items[slot];
        if (stack.IsAir)
            return 0;
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
        if (sync)
            NetMessage.SendData(SyncChestItem, -1, -1, null, player.chest, slot);
        Recipe.FindRecipes();
        return moved;
    }

    public int TakeOne(int slot)
    {
        if (!IsOpen)
            return -1;
        var type = Contents(out _)[slot].type;
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
        if (!IsOpen)
            return 0;
        var stack = Player.inventory[packSlot];
        if (stack.IsAir)
            return 0;
        count = Math.Min(count, stack.stack);
        var going = stack.Clone();
        going.stack = count;
        // The game's shift-click on an inventory item with a chest open.
        ChestUI.TryPlacingInChest(going, justCheck: false, ItemSlot.Context.InventoryItem);
        var moved = count - (going.IsAir ? 0 : going.stack);
        stack.stack -= moved;
        if (stack.stack <= 0)
            stack.TurnToAir();
        Recipe.FindRecipes();
        return moved;
    }

    public string Describe(int slot) => IsOpen ? ItemText.Describe(Contents(out _)[slot]) : "";

    public bool ToHotbar(int slot, int hotbar)
    {
        if (!IsOpen)
            return false;
        var items = Contents(out var sync);
        var inventory = Player.inventory;
        if (!inventory[hotbar].IsAir)
        {
            // What was in the hotbar slot goes to the pack, not the chest.
            var free = -1;
            for (var i = 10; i < 50 && free < 0; i++)
                if (inventory[i].IsAir)
                    free = i;
            if (free < 0)
                return false;
            inventory[free] = inventory[hotbar];
        }
        inventory[hotbar] = items[slot];
        items[slot] = new Item();
        if (sync)
            NetMessage.SendData(SyncChestItem, -1, -1, null, Player.chest, slot);
        Terraria.Audio.SoundEngine.PlaySound(SoundID.Grab);
        Recipe.FindRecipes();
        return true;
    }

    public void LootAll()
    {
        ChestUI.LootAll();
        Recipe.FindRecipes();
    }

    /// <summary>The game's own button puts money away only where
    /// there is money already, so that a chest is not handed a purse
    /// by accident. The character's own - piggy bank, safe and the
    /// rest - are where money is kept: there it always goes in, and
    /// the game then makes change of it as it does.</summary>
    public void DepositAll()
    {
        if (Player.chest < -1)
        {
            var inventory = Player.inventory;
            for (var slot = 0; slot < 58; slot++)
                if (inventory[slot].type is >= ItemID.CopperCoin and <= ItemID.PlatinumCoin && !inventory[slot].favorited)
                    Deposit(slot, inventory[slot].stack);
        }
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
            recipes.Add(new Craftable(id, recipe.createItem.Name, recipe.createItem.stack, Needs(recipe), Most(recipe)));
        }
        return recipes;
    }

    /// <summary>How many times over the ingredients to hand would
    /// make it: what is carried, and what is in a chest that is open,
    /// which the game crafts from too. An estimate - the game has the
    /// last word, one crafting at a time.</summary>
    private static int Most(Recipe recipe)
    {
        var player = Main.LocalPlayer;
        IEnumerable<Item> hand = player.inventory.Take(58);
        if (TerrariaChest.IsOpen)
        {
            ChestUI.GetContainerUsageInfo(out _, out var chest);
            hand = hand.Concat(chest);
        }
        hand = hand.Concat(TerrariaStorage.Materials());
        var items = hand.Where(item => !item.IsAir).ToList();
        var most = 9999;
        foreach (var needed in recipe.requiredItem)
        {
            if (needed.IsAir || needed.stack <= 0)
                continue;
            var have = items.Where(item => item.type == needed.type || recipe.AcceptedByItemGroups(item.type, needed.type))
                .Sum(item => (long)item.stack);
            most = (int)Math.Min(most, have / needed.stack);
        }
        return Math.Max(1, most);
    }

    private static string Needs(Recipe recipe) =>
        string.Join(", ", recipe.requiredItem.Select(item =>
            $"{item.stack} {(recipe.ProcessGroupsForText(item.type, out var group) ? group : item.Name)}"));

    public string Describe(int id) => ItemText.Describe(Main.recipe[id].createItem);

    public string? Blocked(int id) =>
        Main.LocalPlayer.ItemSpace(Main.recipe[id].createItem).CanTakeItem ? null : "No room in the inventory.";

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
