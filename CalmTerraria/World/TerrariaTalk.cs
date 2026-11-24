using CalmTerraria.Pack;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.UI;

namespace CalmTerraria.World;

/// <summary>Talking to someone in the world, and reading signs.
///
/// The game keeps a conversation's buttons - what they are called and
/// everything each one does, for every character - inside the code
/// that draws them, where it asks whether the mouse is on one. So
/// this reads the labels as they are drawn, and presses a button by
/// telling that same code, for one frame, that the mouse is on it and
/// down. Every character's replies then work as the game means them
/// to, including those other mods add.</summary>
internal sealed class TerrariaTalk
{
    private string _labelsFor = "";
    private string _first = "", _third = "";
    private bool _happiness;
    /// <summary>The button to press when the buttons are next drawn:
    /// the game's own numbering, 2 the first, 3 the third, 4 the
    /// happiness report.</summary>
    private int _press;

    public TerrariaTalk()
    {
        On_Main.DrawNPCChatButtons += (orig, superColor, chatColor, numLines, focusText, focusText3) =>
        {
            orig(superColor, chatColor, numLines, focusText, focusText3);
            _labelsFor = Main.npcChatText;
            _first = focusText ?? "";
            _third = focusText3 ?? "";
            _happiness = !Main.remixWorld && Main.LocalPlayer.currentShoppingSettings.HappinessReport != "";
            if (_press == 0)
                return;
            Main.npcChatFocus1 = false;
            Main.npcChatFocus2 = _press == 2;
            Main.npcChatFocus3 = _press == 3;
            Main.npcChatFocus4 = _press == 4;
            _press = 0;
            // The click is taken only with the mouse on the panel.
            Main.mouseX = Main.screenWidth / 2;
            Main.mouseY = 110;
            Main.mouseLeft = true;
            Main.mouseLeftRelease = true;
        };
    }

    public static bool Talking =>
        Main.npcChatText != "" && (Main.LocalPlayer.talkNPC >= 0 || Main.LocalPlayer.sign >= 0);

    /// <summary>What is being said, once the game has drawn it and
    /// its buttons are known; null before that, and when nobody is
    /// talking.</summary>
    public Talk? Current(out List<int> buttons)
    {
        buttons = new List<int>();
        if (!Talking || _labelsFor != Main.npcChatText)
            return null;
        var player = Main.LocalPlayer;
        var options = new List<string>();
        var list = buttons;
        // A sign's one button edits it, which is typing into the
        // game's own box: not offered until that can be read.
        if (player.talkNPC >= 0)
        {
            Add(_first, 2);
            Add(_third, 3);
            if (_happiness)
                Add(Terraria.Localization.Language.GetTextValue("UI.NPCCheckHappiness"), 4);
        }
        var speaker = player.talkNPC >= 0 ? Main.npc[player.talkNPC].FullName : "Sign";
        return new Talk(speaker, ItemText.Clean(Main.npcChatText.Replace('\n', ' ')), options);

        void Add(string label, int button)
        {
            var text = ItemText.Clean(label);
            if (text.Length == 0)
                return;
            options.Add(text);
            list.Add(button);
        }
    }

    public void Press(int button) => _press = button;

    public static void End()
    {
        if (Main.npcShop > 0)
            Main.SetNPCShopIndex(0);
        if (Main.LocalPlayer.talkNPC >= 0 || Main.LocalPlayer.sign >= 0)
            Main.CloseNPCChatOrSign();
    }
}

/// <summary>The shop the local player has open.</summary>
internal sealed class TerrariaShop : IShop
{
    public static bool IsOpen => Main.npcShop > 0;

    private static Item[] Stock => Main.instance.shop[Main.npcShop].item;

    public IReadOnlyList<ShopItem> Items()
    {
        var items = new List<ShopItem>();
        if (!IsOpen)
            return items;
        var stock = Stock;
        for (var slot = 0; slot < stock.Length; slot++)
        {
            if (!stock[slot].IsAir)
                items.Add(new ShopItem(slot, stock[slot].AffixName(), stock[slot].stack, Price(stock[slot]),
                    // Something sold once is limited to what the shop has of it.
                    stock[slot].buyOnce ? stock[slot].stack : stock[slot].maxStack));
        }
        return items;
    }

    private static long Cost(Item item)
    {
        Main.LocalPlayer.GetItemExpectedPrice(item, out _, out var buying);
        return buying;
    }

    private static string Price(Item item)
    {
        var cost = Cost(item);
        if (item.shopSpecialCurrency == -1)
            return Coins(cost);
        return item.shopSpecialCurrency == Terraria.ID.CustomCurrencyID.DefenderMedals
            ? $"{cost} defender medals"
            : $"{cost} tokens";
    }

    private static readonly string[] CoinNames = { "copper", "silver", "gold", "platinum" };

    public static string Coins(long value)
    {
        if (value <= 0)
            return "nothing";
        var split = Utils.CoinsSplit(value);
        var parts = new List<string>();
        for (var i = split.Length - 1; i >= 0; i--)
            if (split[i] > 0)
                parts.Add($"{split[i]} {CoinNames[i]}");
        return string.Join(" ", parts);
    }

    /// <summary>Everything the game will spend from: the pack and the
    /// character's own banks.</summary>
    public string Money()
    {
        var player = Main.LocalPlayer;
        long total = 0;
        foreach (var items in new[] { player.inventory, player.bank.item, player.bank2.item, player.bank3.item, player.bank4.item })
        {
            foreach (var item in items)
            {
                if (item.type is >= ItemID.CopperCoin and <= ItemID.PlatinumCoin)
                    total += (long)item.value / 5 * item.stack;
            }
        }
        return Coins(total);
    }

    public string Describe(int slot) => IsOpen ? ItemText.Describe(Stock[slot]) : "";

    public BuyResult Buy(int slot)
    {
        if (!IsOpen || Stock[slot].IsAir || !Main.mouseItem.IsAir)
            return BuyResult.Refused;
        var player = Main.LocalPlayer;
        var item = Stock[slot];
        if (!player.CanAfford(Cost(item), item.shopSpecialCurrency))
            return BuyResult.CantAfford;
        if (!player.ItemSpace(item).CanTakeItemToPersonalInventory)
            return BuyResult.NoRoom;
        // The game's own click on a shop slot: it buys onto the mouse.
        var (left, split, cursor) = (Main.mouseLeft, Main.stackSplit, Main.cursorOverride);
        Main.mouseLeft = true;
        Main.stackSplit = 0;
        Main.cursorOverride = 0;
        try
        {
            HandleShopSlot(null, Stock, slot, false, true);
        }
        finally
        {
            (Main.mouseLeft, Main.stackSplit, Main.cursorOverride) = (left, split, cursor);
        }
        var bought = Main.mouseItem;
        if (bought.IsAir)
            return BuyResult.Refused;
        Main.mouseItem = new Item();
        bought.position = player.Center;
        var over = player.GetItem(Main.myPlayer, bought, GetItemSettings.InventoryUIToInventorySettings);
        if (!over.IsAir)
            player.QuickSpawnItem(new EntitySource_Misc("CalmTerraria shop"), over, over.stack);
        Recipe.FindRecipes();
        return BuyResult.Bought;
    }

    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.StaticMethod, Name = "HandleShopSlot")]
    private static extern void HandleShopSlot(ItemSlot? type, Item[] inv, int slot, bool rightClickIsValid, bool leftClickIsValid);

    public int Sell(int packSlot, int count)
    {
        var inventory = Main.LocalPlayer.inventory;
        var item = inventory[packSlot];
        // Outside a shop the same call trashes, and coins are not sold.
        if (!IsOpen || item.IsAir || item.favorited || item.type is >= ItemID.CopperCoin and <= ItemID.PlatinumCoin)
            return 0;
        count = Math.Min(count, item.stack);
        // The game sells a whole stack: the part being kept is set
        // aside while it does, and put back after.
        var kept = item.stack - count;
        Item? rest = null;
        if (kept > 0)
        {
            rest = item.Clone();
            rest.stack = kept;
            item.stack = count;
        }
        ItemSlot.SellOrTrash(inventory, ItemSlot.Context.InventoryItem, packSlot);
        var sold = inventory[packSlot].IsAir;
        if (rest != null)
        {
            if (sold)
                inventory[packSlot] = rest;
            else
                inventory[packSlot].stack += kept;
        }
        Recipe.FindRecipes();
        return sold ? count : 0;
    }
}
