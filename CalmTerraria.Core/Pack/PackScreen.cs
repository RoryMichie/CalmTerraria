using CalmTerraria.Powers;
using Srui;

namespace CalmTerraria.Pack;

/// <summary>One stack in the player's inventory, as the game holds it.</summary>
/// <param name="Slot">The game's slot, for acting on the stack.</param>
/// <param name="Equippable">Armour or an accessory: Enter wears it.</param>
/// <param name="Where">For a stack in one of the game's special
/// slots, which it is: "coin slot", "ammo slot". Null in the main
/// inventory.</param>
public sealed record PackStack(int Slot, string Name, int Count, bool Equippable, string? Where = null);

public enum UseResult { Started, Paused, Busy, NotUsable }

/// <summary>The player's belongings, as the pack screen needs them.</summary>
public interface IPack
{
    IReadOnlyList<PackStack> Stacks();

    /// <summary>How many empty slots the inventory has left.</summary>
    int FreeSlots { get; }

    /// <summary>What is worn, in a fixed order: the slot's name and
    /// the item in it, or null for an empty slot.</summary>
    IReadOnlyList<(string Slot, string? Item)> Equipment();

    /// <summary>Put the stack in the player's hand.</summary>
    void Hold(int slot);

    /// <summary>Wear it, swapping with whatever was worn. False when
    /// the game would not.</summary>
    bool Equip(int slot);

    void Trash(int slot);

    /// <summary>What the game's tooltip says about the stack, without
    /// its name.</summary>
    string Describe(int slot);

    /// <summary>Put the stack in a hotbar slot, 0 to 9; what was
    /// there takes the stack's place.</summary>
    void ToHotbar(int slot, int hotbar);

    /// <summary>Use one of the stack as if it were held and the mouse
    /// clicked, without changing what is held: drink it, eat it,
    /// throw it. The game says how it went when it has gone.</summary>
    UseResult Use(int slot);

    /// <summary>Open it, as a right-click in the game's inventory
    /// does: a bag, a crate. False when it is not a thing that opens.</summary>
    bool Open(int slot);

    /// <summary>Take off what is worn in an equipment slot. False
    /// when there is no room for it.</summary>
    bool Unequip(int index);
}

/// <summary>A line of the inventory list: every stack of one item,
/// as one line with their total. The game's slots are not shown;
/// which stack an action takes is the list's business.</summary>
public sealed partial class PackLine : Element
{
    public string Name { get; }
    public int Count { get; internal set; }
    public bool Equippable { get; internal set; }
    /// <summary>The stacks behind the line, smallest first.</summary>
    internal List<PackStack> Stacks { get; set; } = new();

    /// <summary>The special slots the line's stacks are in, or null.</summary>
    public string? Where { get; }

    public PackLine(string name, string? where = null)
    {
        Name = name;
        Where = where;
    }

    // A full stop before the description, where there is one, so the
    // two are heard as two.
    [Field]
    public string Value => (Count > 1 ? $"{Name}, {Count}" : Name) + (Where == null ? "" : $", {Where}")
        + (About.Length > 0 ? "." : "");

    /// <summary>What the game says about the item, read after it
    /// each time the line is landed on.</summary>
    internal Func<string>? Describe { get; set; }

    [Field]
    public string About => Describe?.Invoke() ?? "";
}

/// <summary>A line of the equipment list.</summary>
public sealed partial class EquipLine : Element
{
    public string Slot { get; }
    public string? Item { get; internal set; }

    public EquipLine(string slot) => Slot = slot;

    [Field]
    public string Value => $"{Slot}, {Item ?? "empty"}";
}

/// <summary>Stacks, as alphabetical lines with each item's stacks
/// merged. A line keeps its object for as long as its item is there
/// at all, so a list's cursor stays with the item while counts change
/// around it.</summary>
internal sealed class StackLines
{
    private readonly Func<IReadOnlyList<PackStack>> _stacks;
    private readonly Dictionary<(string Name, string? Where), PackLine> _lines = new();
    private List<PackLine> _sorted = new();

    private readonly Func<int, string> _describe;

    public StackLines(Func<IReadOnlyList<PackStack>> stacks, Func<int, string> describe)
    {
        _stacks = stacks;
        _describe = describe;
    }

    public IReadOnlyList<PackLine> Lines()
    {
        // The main inventory by name. After it, what is in the game's
        // coin and ammunition slots, in the game's own order: kept
        // apart and at the end, since it is there to be spent and
        // fired, not looked through.
        var groups = _stacks()
            .GroupBy(stack => (stack.Name, stack.Where))
            .OrderBy(group => group.Key.Where == null ? 0 : 1)
            .ThenBy(group => group.Key.Where == null ? 0 : group.Min(stack => stack.Slot))
            .ThenBy(group => group.Key.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var changed = groups.Count != _sorted.Count;
        var sorted = new List<PackLine>(groups.Count);
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (!_lines.TryGetValue(group.Key, out var line))
            {
                var made = line = new PackLine(group.Key.Name, group.Key.Where);
                made.Describe = () => made.Stacks.Count == 0 ? "" : _describe(made.Stacks[^1].Slot);
                _lines[group.Key] = line;
            }
            line.Count = group.Sum(stack => stack.Count);
            line.Equippable = group.Any(stack => stack.Equippable);
            line.Stacks = group.OrderBy(stack => stack.Count).ToList();
            sorted.Add(line);
            changed |= i >= _sorted.Count || !ReferenceEquals(_sorted[i], line);
        }
        // The same list object while nothing moved: the framework asks often.
        if (changed)
        {
            _sorted = sorted;
            foreach (var gone in _lines.Keys.Where(key => !groups.Any(group => group.Key == key)).ToList())
                _lines.Remove(gone);
        }
        return _sorted;
    }
}

/// <summary>The keys every list of items shares.</summary>
internal static class ItemKeys
{
    public static readonly Key Describe = Key.F(1);

    public static bool Reserves(KeyCombo combo) =>
        !combo.HasModifier && (combo.Key == Describe || Hotbar(combo.Key) >= 0
            || combo.Key == Key.Char('[') || combo.Key == Key.Char(']'));

    /// <summary>The brackets on an item are the mouse buttons on it,
    /// as they are in the world: left uses it, without its being
    /// held first; right opens it, if it is a thing that opens.</summary>
    public static string? Brackets(in InputEvent input, IPack pack, Func<int> slot)
    {
        if (input.IsChar('['))
        {
            var at = slot();
            if (at < 0)
                return "No room in the inventory.";
            return pack.Use(at) switch
            {
                UseResult.Started => "",
                UseResult.Paused => "Can't use items while paused. Open this screen with Tab instead of Escape.",
                UseResult.Busy => "Still using the previous item.",
                _ => "Can't be used.",
            };
        }
        if (input.IsChar(']'))
        {
            var at = slot();
            if (at < 0)
                return "No room in the inventory.";
            return pack.Open(at) ? "Opened." : "Nothing to open.";
        }
        return null;
    }

    private static int Hotbar(Key key)
    {
        for (var i = 0; i < 10; i++)
            if (key == Key.Char((char)('0' + i)))
                return i;
        return -1;
    }

    /// <summary>The hotbar slot a typed digit names, 0 to 9, or -1:
    /// 1 is the first slot and 0 the tenth, as on the keyboard.</summary>
    public static int Hotbar(in InputEvent input)
    {
        for (var i = 0; i < 10; i++)
            if (input.IsChar((char)('0' + i)))
                return (i + 9) % 10;
        return -1;
    }

    public static string Said(string description) =>
        string.IsNullOrWhiteSpace(description) ? "No description." : description;
}

/// <summary>The inventory: one alphabetical list. Enter does the
/// natural thing with the item - wears what can be worn, holds
/// anything else - Space always holds, Delete trashes one stack, a
/// digit puts it in that hotbar slot, F1 describes it. While a chest
/// is open, Enter puts the item in the chest; in a shop, Enter sells
/// it.</summary>
public sealed class PackList : ListBox<PackLine>
{
    private readonly IPack _pack;
    private readonly IChest? _chest;
    private readonly IShop? _shop;
    private readonly IChest? _storage;

    private static readonly KeyCombo Store = KeyCombo.WithShift(Key.Enter);

    /// <param name="storage">The chests near the character as one
    /// store: Shift+Enter puts the item away in it.</param>
    public PackList(IWidgetContainer parent, IPack pack, IChest? chest = null, IShop? shop = null, IChest? storage = null)
        : base(parent, pack.FreeSlots == 0 ? "Carried, full" : "Carried", Array.Empty<PackLine>())
    {
        _pack = pack;
        _chest = chest;
        _shop = shop;
        _storage = storage;
        BindItems(new StackLines(pack.Stacks, pack.Describe).Lines);
    }

    public override bool ReservesKey(KeyCombo combo) =>
        (!combo.HasModifier && (combo.Key == Key.Delete || combo.Key == Key.Space || combo.Key == Key.Enter))
        || (_storage != null && combo == Store)
        || ItemKeys.Reserves(combo) || base.ReservesKey(combo);

    protected override bool OnInput(in InputEvent input)
    {
        if (Items.Count == 0)
            return base.OnInput(input);
        var line = Items[SelectedIndex];
        if (_storage != null && input.Is(Store))
        {
            var storage = _storage;
            Quantity.Ask(App, line.Count, line.Count, count =>
            {
                var stored = Quantity.Over(line.Stacks, count, storage.Deposit);
                Announce(stored == count ? "Stored."
                    : stored > 0 ? $"Stored {stored}. No room in storage."
                    : "No room in storage.");
            });
            return true;
        }
        if (input.Kind == InputKind.Activate)
        {
            if (_shop != null)
            {
                // The shop keeps what it is sold, to be bought back.
                var shop = _shop;
                Quantity.Ask(App, line.Count, line.Count, count =>
                {
                    var sold = Quantity.Over(line.Stacks, count, shop.Sell);
                    Announce(sold == count ? $"Sold. {shop.Money()}."
                        : sold > 0 ? $"Sold {sold}. {shop.Money()}."
                        : "Can't sell that.");
                });
                return true;
            }
            if (_chest != null)
            {
                var chest = _chest;
                Quantity.Ask(App, line.Count, line.Count, count =>
                {
                    var stored = Quantity.Over(line.Stacks, count, chest.Deposit);
                    Announce(stored == count ? "Deposited."
                        : stored > 0 ? $"Deposited {stored}. No room in the chest."
                        : "No room in the chest.");
                });
                return true;
            }
            if (!line.Equippable)
                return Hold(line);
            // The line leaves this list for the equipment list, where
            // focus is not: said, since nothing here shows it.
            Announce(_pack.Equip(line.Stacks[0].Slot) ? "Equipped." : "Can't wear that now.");
            return true;
        }
        if (input.IsChar(' '))
            return Hold(line);
        if (ItemKeys.Brackets(input, _pack, () => line.Stacks[0].Slot) is { } said)
        {
            if (said.Length > 0)
                Announce(said);
            return true;
        }
        if (input.Is(ItemKeys.Describe))
        {
            Announce(ItemKeys.Said(_pack.Describe(line.Stacks[^1].Slot)));
            return true;
        }
        if (ItemKeys.Hotbar(input) is >= 0 and var hotbar)
        {
            _pack.ToHotbar(line.Stacks[^1].Slot, hotbar);
            Announce($"Hotbar {(hotbar + 1) % 10}.");
            return true;
        }
        if (input.Is(Key.Delete))
        {
            _pack.Trash(line.Stacks[0].Slot);
            Announce("Trashed.");
            return true;
        }
        return base.OnInput(input);
    }

    private bool Hold(PackLine line)
    {
        // The largest stack: the one a player would reach for.
        _pack.Hold(line.Stacks[^1].Slot);
        Announce("Held.");
        return true;
    }
}

/// <summary>What is worn. Enter takes the piece off.</summary>
public sealed class EquipList : ListBox<EquipLine>
{
    private readonly IPack _pack;
    private readonly List<EquipLine> _lines = new();

    public EquipList(IWidgetContainer parent, IPack pack)
        : base(parent, "Equipment", Array.Empty<EquipLine>())
    {
        _pack = pack;
        BindItems(Lines);
    }

    private IReadOnlyList<EquipLine> Lines()
    {
        var worn = _pack.Equipment();
        if (_lines.Count != worn.Count)
        {
            _lines.Clear();
            _lines.AddRange(worn.Select(slot => new EquipLine(slot.Slot)));
        }
        for (var i = 0; i < worn.Count; i++)
            _lines[i].Item = worn[i].Item;
        return _lines;
    }

    public override bool ReservesKey(KeyCombo combo) =>
        (!combo.HasModifier && combo.Key == Key.Enter) || base.ReservesKey(combo);

    protected override bool OnInput(in InputEvent input)
    {
        if (input.Kind != InputKind.Activate || Items.Count == 0)
            return base.OnInput(input);
        var line = Items[SelectedIndex];
        // Taken off, the line itself changes to "empty" and is read;
        // only failure needs words.
        if (line.Item == null)
            Announce("Nothing to take off.");
        else if (!_pack.Unequip(SelectedIndex))
            Announce("No room in the inventory.");
        return true;
    }
}

/// <summary>An open container: a chest in the world, or one of the
/// character's own - piggy bank, safe and the rest.</summary>
public interface IChest
{
    string Name { get; }
    IReadOnlyList<PackStack> Stacks();

    /// <summary>Move that many of a stack to the inventory. How many
    /// went: fewer when they did not all fit.</summary>
    int Take(int slot, int count);

    /// <summary>Move that many of an inventory stack here. How many
    /// went.</summary>
    int Deposit(int packSlot, int count);

    /// <summary>Move one of a stack to the inventory, to be used
    /// there. The inventory slot it went to, or -1 with no room.</summary>
    int TakeOne(int slot);

    string Describe(int slot);

    /// <summary>Put the stack in a hotbar slot, 0 to 9; what was
    /// there goes to the inventory. False, and nothing moved, when
    /// the inventory has no room for it.</summary>
    bool ToHotbar(int slot, int hotbar);

    void LootAll();
    void DepositAll();
    void QuickStack();
    void Restock();

    /// <summary>Only chests in the world have names to change.</summary>
    bool CanRename { get; }
    void Rename(string name);
}

/// <summary>What is in the open chest. Enter takes all of the item,
/// a digit puts it in that hotbar slot, F1 describes it.</summary>
public sealed class ChestList : ListBox<PackLine>
{
    private readonly IChest _chest;

    private readonly IPack? _pack;

    public ChestList(IWidgetContainer parent, IChest chest, IPack? pack = null)
        : base(parent, chest.Name, Array.Empty<PackLine>())
    {
        _chest = chest;
        _pack = pack;
        BindItems(new StackLines(chest.Stacks, chest.Describe).Lines);
    }

    public override bool ReservesKey(KeyCombo combo) =>
        (!combo.HasModifier && combo.Key == Key.Enter) || ItemKeys.Reserves(combo) || base.ReservesKey(combo);

    protected override bool OnInput(in InputEvent input)
    {
        if (Items.Count == 0)
            return base.OnInput(input);
        // From a chest, one of it comes out first: only what is
        // carried can be used.
        if (_pack != null && ItemKeys.Brackets(input, _pack, () => _chest.TakeOne(Items[SelectedIndex].Stacks[0].Slot)) is { } said)
        {
            if (said.Length > 0)
                Announce(said);
            return true;
        }
        if (input.Is(ItemKeys.Describe))
        {
            Announce(ItemKeys.Said(_chest.Describe(Items[SelectedIndex].Stacks[^1].Slot)));
            return true;
        }
        if (ItemKeys.Hotbar(input) is >= 0 and var hotbar)
        {
            Announce(_chest.ToHotbar(Items[SelectedIndex].Stacks[^1].Slot, hotbar)
                ? $"Hotbar {(hotbar + 1) % 10}."
                : "No room in the inventory.");
            return true;
        }
        if (input.Kind != InputKind.Activate)
            return base.OnInput(input);
        var line = Items[SelectedIndex];
        Quantity.Ask(App, line.Count, line.Count, count =>
        {
            var taken = Quantity.Over(line.Stacks, count, _chest.Take);
            Announce(taken == count ? "Taken."
                : taken > 0 ? $"Taken {taken}. No room in the inventory."
                : "No room in the inventory.");
        });
        return true;
    }
}

/// <summary>Something that can be made right now.</summary>
/// <param name="Id">The game's recipe, for making it.</param>
/// <param name="Count">How many one crafting makes.</param>
/// <param name="Needs">The ingredients, as words.</param>
/// <param name="Most">How many times over it could be made with
/// what is to hand.</param>
public sealed record Craftable(int Id, string Name, int Count, string Needs, int Most = 1);

public interface ICrafting
{
    /// <summary>What the character can make where they stand, with
    /// what they carry.</summary>
    IReadOnlyList<Craftable> Recipes();

    /// <summary>Make it once. False when the game would not.</summary>
    bool Craft(int id);

    /// <summary>Why it cannot be made just now though the makings
    /// are there - no room for it - or null.</summary>
    string? Blocked(int id);

    /// <summary>What the game's tooltip says about what is made.</summary>
    string Describe(int id);
}

public sealed partial class CraftLine : Element
{
    public Craftable Recipe { get; internal set; }

    public CraftLine(Craftable recipe) => Recipe = recipe;

    [Field]
    public string Value => (Recipe.Count > 1 ? $"{Recipe.Name}, {Recipe.Count}" : Recipe.Name) + (About.Length > 0 ? "." : "");

    internal Func<int, string>? Describe { get; set; }

    [Field]
    public string About => Describe?.Invoke(Recipe.Id) ?? "";
}

/// <summary>What can be made now: one alphabetical list. Enter makes
/// it, asking how many times where it could be made more than once; Space says what it uses; F1 describes what is made.</summary>
public sealed class CraftList : ListBox<CraftLine>
{
    private readonly ICrafting _crafting;
    private readonly Dictionary<int, CraftLine> _lines = new();
    private List<CraftLine> _sorted = new();

    public CraftList(IWidgetContainer parent, ICrafting crafting)
        : base(parent, "Crafting", Array.Empty<CraftLine>())
    {
        _crafting = crafting;
        BindItems(Lines);
    }

    private IReadOnlyList<CraftLine> Lines()
    {
        var recipes = _crafting.Recipes()
            .OrderBy(recipe => recipe.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(recipe => recipe.Id)
            .ToList();
        var changed = recipes.Count != _sorted.Count;
        var sorted = new List<CraftLine>(recipes.Count);
        for (var i = 0; i < recipes.Count; i++)
        {
            if (!_lines.TryGetValue(recipes[i].Id, out var line))
                _lines[recipes[i].Id] = line = new CraftLine(recipes[i]) { Describe = _crafting.Describe };
            line.Recipe = recipes[i];
            sorted.Add(line);
            changed |= i >= _sorted.Count || !ReferenceEquals(_sorted[i], line);
        }
        if (changed)
        {
            _sorted = sorted;
            foreach (var gone in _lines.Keys.Where(id => !recipes.Any(recipe => recipe.Id == id)).ToList())
                _lines.Remove(gone);
        }
        return _sorted;
    }

    public override bool ReservesKey(KeyCombo combo) =>
        (!combo.HasModifier && (combo.Key == Key.Space || combo.Key == Key.Enter || combo.Key == ItemKeys.Describe))
        || base.ReservesKey(combo);

    protected override bool OnInput(in InputEvent input)
    {
        if (Items.Count == 0)
            return base.OnInput(input);
        var recipe = Items[SelectedIndex].Recipe;
        if (input.Kind == InputKind.Activate)
        {
            // Asked first: a thing with nowhere to go is not made.
            if (_crafting.Blocked(recipe.Id) is { } blocked)
            {
                Announce(blocked);
                return true;
            }
            Quantity.Ask(App, 1, recipe.Most, count =>
            {
                var made = 0;
                while (made < count && _crafting.Craft(recipe.Id))
                    made++;
                Announce(made == count ? "Crafted."
                    : made > 0 ? $"Crafted {made}. Can't make more."
                    : "Can't make that now.");
            });
            return true;
        }
        if (input.IsChar(' '))
        {
            Announce(recipe.Needs);
            return true;
        }
        if (input.Is(ItemKeys.Describe))
        {
            Announce(ItemKeys.Said(_crafting.Describe(recipe.Id)));
            return true;
        }
        return base.OnInput(input);
    }
}

/// <summary>Sends what the player types to the game's chat.</summary>
public interface IChat
{
    void Send(string text);
}

public sealed partial class ChatLine : Element
{
    public ChatLine(string text) => Value = text;

    [Field]
    public string Value { get; }
}

/// <summary>The game's messages, kept: chat, deaths, events. Oldest
/// first; the oldest go once there are too many.</summary>
public sealed class ChatLog
{
    private const int Kept = 200;
    private List<ChatLine> _lines = new();

    public IReadOnlyList<ChatLine> Lines => _lines;

    public void Add(string text)
    {
        // A new list each time: the framework tells change by identity.
        var lines = new List<ChatLine>(_lines.Skip(Math.Max(0, _lines.Count + 1 - Kept))) { new(text) };
        _lines = lines;
    }

    public void Clear() => _lines = new();
}

/// <summary>Where a message is typed. Enter sends it and empties the
/// box; there is no send button.</summary>
public sealed class ChatBox : EditBox
{
    private readonly IChat _chat;

    public ChatBox(IWidgetContainer parent, IChat chat) : base(parent, "Message") => _chat = chat;

    public override bool ReservesKey(KeyCombo combo) =>
        (!combo.HasModifier && combo.Key == Key.Enter) || base.ReservesKey(combo);

    protected override bool OnInput(in InputEvent input)
    {
        if (input.Kind != InputKind.Activate)
            return base.OnInput(input);
        var text = Text.Trim();
        if (text.Length > 0)
        {
            _chat.Send(text);
            Text = "";
        }
        return true;
    }
}

/// <summary>The world itself, as a stop in the ring: tabbing onto it
/// puts the screen away and gives the keys back to the game.</summary>
internal sealed class WorldStop : CustomWidget
{
    private readonly Action _leave;
    public bool Armed { get; set; }

    public WorldStop(IWidgetContainer parent, Action leave) : base(parent, "World") => _leave = leave;

    protected override void OnFocusGained()
    {
        if (Armed)
            Post(_leave);
    }
}

/// <summary>What someone in the world is saying, and what can be
/// said back.</summary>
/// <param name="Options">The dialogue's buttons, without Close:
/// leaving the screen is how a conversation is closed.</param>
public sealed record Talk(string Speaker, string Text, IReadOnlyList<string> Options);

/// <summary>Something a shop sells.</summary>
/// <param name="Price">As words: "1 gold 50 silver".</param>
/// <param name="Most">The most worth asking for at once: one for
/// what does not stack.</param>
public sealed record ShopItem(int Slot, string Name, int Count, string Price, int Most = 1);

public enum BuyResult { Bought, CantAfford, NoRoom, Refused }

public interface IShop
{
    IReadOnlyList<ShopItem> Items();

    /// <summary>Buy one, into the inventory.</summary>
    BuyResult Buy(int slot);

    /// <summary>Sell that many of an inventory stack. How many were
    /// sold: none when the shop will not take it.</summary>
    int Sell(int packSlot, int count);

    /// <summary>What the player has to spend, as words.</summary>
    string Money();

    string Describe(int slot);
}

public sealed partial class ShopLine : Element
{
    public ShopItem Item { get; internal set; }

    public ShopLine(ShopItem item) => Item = item;

    [Field]
    public string Value => (Item.Count > 1 ? $"{Item.Name}, {Item.Count}, {Item.Price}" : $"{Item.Name}, {Item.Price}")
        + (About.Length > 0 ? "." : "");

    internal Func<int, string>? Describe { get; set; }

    [Field]
    public string About => Describe?.Invoke(Item.Slot) ?? "";
}

/// <summary>What the shop sells, alphabetical, each with its price.
/// Enter buys one and says what money is left; F1 describes.</summary>
public sealed class ShopList : ListBox<ShopLine>
{
    private readonly IShop _shop;
    private readonly Dictionary<int, ShopLine> _lines = new();
    private List<ShopLine> _sorted = new();

    public ShopList(IWidgetContainer parent, IShop shop)
        : base(parent, "Shop", Array.Empty<ShopLine>())
    {
        _shop = shop;
        BindItems(Lines);
    }

    private IReadOnlyList<ShopLine> Lines()
    {
        var items = _shop.Items()
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Slot)
            .ToList();
        var changed = items.Count != _sorted.Count;
        var sorted = new List<ShopLine>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            if (!_lines.TryGetValue(items[i].Slot, out var line) || line.Item.Name != items[i].Name)
                _lines[items[i].Slot] = line = new ShopLine(items[i]) { Describe = _shop.Describe };
            line.Item = items[i];
            sorted.Add(line);
            changed |= i >= _sorted.Count || !ReferenceEquals(_sorted[i], line);
        }
        if (changed)
            _sorted = sorted;
        return _sorted;
    }

    public override bool ReservesKey(KeyCombo combo) =>
        (!combo.HasModifier && (combo.Key == Key.Enter || combo.Key == ItemKeys.Describe)) || base.ReservesKey(combo);

    protected override bool OnInput(in InputEvent input)
    {
        if (Items.Count == 0)
            return base.OnInput(input);
        var item = Items[SelectedIndex].Item;
        if (input.Is(ItemKeys.Describe))
        {
            Announce(ItemKeys.Said(_shop.Describe(item.Slot)));
            return true;
        }
        if (input.Kind != InputKind.Activate)
            return base.OnInput(input);
        Quantity.Ask(App, 1, item.Most, count =>
        {
            var bought = 0;
            var result = BuyResult.Bought;
            while (bought < count && (result = _shop.Buy(item.Slot)) == BuyResult.Bought)
                bought++;
            var why = result switch
            {
                BuyResult.Bought => "",
                BuyResult.CantAfford => $"Not enough money. You have {_shop.Money()}.",
                BuyResult.NoRoom => "No room in the inventory.",
                _ => "Can't buy that.",
            };
            Announce(bought == count ? $"Bought. {_shop.Money()} left."
                : bought > 0 ? $"Bought {bought}. {why}"
                : why);
        });
        return true;
    }
}

/// <summary>A job that can be done to the area the player marked.</summary>
/// <param name="Key">Which job it is, the same for as long as it is
/// offered whatever its counts do: "fill", "clear".</param>
/// <param name="Name">What the list says: "Fill with Dirt Block, 60 tiles".</param>
/// <param name="About">What doing it would take and give, and what
/// is in the way, briefly: read after the name.</param>
public sealed record BuildOption(string Key, string Name, string About);

/// <summary>The area the player marked in the world and what can be
/// done to it with what they hold.</summary>
public interface IBuild
{
    /// <summary>The area, as the list's name: "Build, 12 by 5".</summary>
    string Title { get; }

    /// <summary>What can be done now. Asked often: the list follows
    /// what is held and what the world has become.</summary>
    IReadOnlyList<BuildOption> Options();

    void Start(string key);
}

/// <summary>A line of the build list: the job, then what it takes.</summary>
public sealed partial class BuildLine : Element
{
    public BuildOption Option { get; internal set; }

    public BuildLine(BuildOption option) => Option = option;

    [Field]
    public string Value => Option.Name + (About.Length > 0 ? "." : "");

    [Field]
    public string About => Option.About;
}

/// <summary>The jobs as lines that keep their object for as long as
/// the job is offered, so the cursor stays on a job while its counts
/// change.</summary>
internal sealed class BuildLines
{
    private readonly IBuild _build;
    private readonly Dictionary<string, BuildLine> _lines = new();
    private List<BuildLine> _shown = new();

    public BuildLines(IBuild build) => _build = build;

    public IReadOnlyList<BuildLine> Lines()
    {
        var options = _build.Options();
        var changed = options.Count != _shown.Count;
        var shown = new List<BuildLine>(options.Count);
        for (var i = 0; i < options.Count; i++)
        {
            if (!_lines.TryGetValue(options[i].Key, out var line))
                _lines[options[i].Key] = line = new BuildLine(options[i]);
            line.Option = options[i];
            shown.Add(line);
            changed |= i >= _shown.Count || !ReferenceEquals(_shown[i], line);
        }
        // The same list object while nothing moved: the framework asks often.
        if (changed)
        {
            _shown = shown;
            foreach (var gone in _lines.Keys.Where(key => !options.Any(option => option.Key == key)).ToList())
                _lines.Remove(gone);
        }
        return _shown;
    }
}

/// <summary>Which stop a freshly opened pack screen starts on.</summary>
public enum PackFocus { First, Last, Chat }

/// <summary>The screen behind Escape in a world: the inventory, what
/// is worn, what can be made, the powers, and the way out of the world.
/// Each is one tab stop. Opened on a chest, the chest and what can be
/// done with it come first.</summary>
public sealed class PackScreen
{
    private readonly SruiApp _app;
    private readonly IPack _pack;
    private readonly Action _saveAndExit;
    private readonly PowersScreen? _powers;
    private readonly CalmTerraria.Settings.SettingsScreen? _settings;
    private readonly ICrafting? _crafting;
    private readonly ChatLog? _chatLog;
    private readonly IChat? _chat;
    private Dialog? _dialog;

    /// <summary>The screen closed, however it closed.</summary>
    public event Action? Closed;

    public PackScreen(SruiApp app, IPack pack, Action saveAndExit, IPowers? powers = null, ICrafting? crafting = null,
        ChatLog? chatLog = null, IChat? chat = null, CalmTerraria.Settings.SettingsScreen? settings = null)
    {
        _settings = settings;
        _chatLog = chatLog;
        _chat = chat;
        _crafting = crafting;
        _powers = powers == null ? null : new PowersScreen(app, powers);
        _app = app;
        _pack = pack;
        _saveAndExit = saveAndExit;
    }

    public bool IsOpen => _dialog != null;

    private static readonly string[] ChestActions = { "Loot all", "Deposit all", "Quick stack", "Restock", "Rename" };
    private static readonly string[] StorageActions = { "Quick stack", "Deposit all", "Restock" };

    /// <param name="notice">Why something the player reached for
    /// did not open - a chest someone else has open. It stands first,
    /// as a tab stop that does nothing, where the chest would be.</param>
    /// <param name="talk">Someone speaking: their words stand first,
    /// as a stop that does nothing, then what can be said back.</param>
    /// <param name="choose">Called with the index of the reply chosen.</param>
    /// <param name="storage">The chests near the character, as one
    /// container: a list after the inventory of everything in them,
    /// and what can be done with the lot.</param>
    public void Open(IChest? chest = null, string? notice = null, PackFocus focus = PackFocus.First,
        Talk? talk = null, Action<int>? choose = null, IShop? shop = null, IBuild? build = null, IChest? storage = null)
    {
        if (_dialog != null)
            return;
        var dialog = _dialog = _app.OpenDialog();
        // The ring runs from the world round to the world: past either
        // end is back in the game.
        var world = new WorldStop(dialog, () =>
        {
            // The screen is gone before its last stop could be read.
            _app.Announce("World");
            dialog.Close();
        });
        Widget? first = null;
        ChatBox? box = null;
        if (notice != null)
            first = new CustomWidget(dialog, notice);
        if (talk != null)
        {
            first ??= new CustomWidget(dialog, $"{talk.Speaker}: {talk.Text}");
            if (talk.Options.Count > 0)
            {
                var replies = new ListBox(dialog, "Replies", talk.Options, activateItems: true);
                replies.Activated += () => choose?.Invoke(replies.SelectedIndex);
            }
        }
        if (shop != null)
            first ??= new ShopList(dialog, shop);
        if (chest != null)
        {
            first ??= new ChestList(dialog, chest, _pack);
            if (first is not ChestList)
                _ = new ChestList(dialog, chest, _pack);
            var actions = new ListBox(dialog, "Chest actions",
                chest.CanRename ? ChestActions : ChestActions[..^1], activateItems: true);
            actions.Activated += () =>
            {
                switch (actions.SelectedIndex)
                {
                    case 0: chest.LootAll(); _app.Announce("Looted."); break;
                    case 1: chest.DepositAll(); _app.Announce("Deposited."); break;
                    case 2: chest.QuickStack(); _app.Announce("Stacked."); break;
                    case 3: chest.Restock(); _app.Announce("Restocked."); break;
                    case 4: Rename(chest); break;
                }
            };
        }
        if (build != null && build.Options().Count > 0)
        {
            // Each job reads what it would take after its name, and
            // Enter starts it: the screen is put away, back to the
            // world where the work happens.
            var jobs = new ListBox<BuildLine>(dialog, build.Title, Array.Empty<BuildLine>(), activateItems: true);
            jobs.BindItems(new BuildLines(build).Lines);
            jobs.Activated += () =>
            {
                if (jobs.SelectedItem is not { } line)
                    return;
                dialog.Close();
                build.Start(line.Option.Key);
            };
            first ??= jobs;
        }
        // Storage is for when nothing else is in front of the player.
        if (chest != null || shop != null || talk != null)
            storage = null;
        var pack = new PackList(dialog, _pack, chest, shop, storage);
        first ??= pack;
        if (storage != null)
        {
            _ = new ChestList(dialog, storage, _pack);
            var actions = new ListBox(dialog, "Storage actions", StorageActions, activateItems: true);
            actions.Activated += () =>
            {
                switch (actions.SelectedIndex)
                {
                    case 0: storage.QuickStack(); _app.Announce("Stacked."); break;
                    case 1: storage.DepositAll(); _app.Announce("Deposited."); break;
                    case 2: storage.Restock(); _app.Announce("Restocked."); break;
                }
            };
        }
        _ = new EquipList(dialog, _pack);
        if (_crafting != null)
            _ = new CraftList(dialog, _crafting);
        if (_powers is { } powers)
            new Button(dialog, "Powers").Activated += powers.Open;
        if (_settings is { } settings)
            new Button(dialog, "Settings").Activated += settings.Open;
        var exit = new Button(dialog, "Save and exit");
        exit.Activated += () =>
        {
            _saveAndExit();
            dialog.Close();
        };
        // Chat comes last, so that from the world it is one step
        // backwards and the things done with the hands are forwards.
        if (_chatLog != null && _chat != null)
        {
            var log = _chatLog;
            var messages = new ListBox<ChatLine>(dialog, "Chat", Array.Empty<ChatLine>());
            messages.BindItems(() => log.Lines);
            // The newest is the one worth landing on.
            messages.SelectedIndex = int.MaxValue;
            box = new ChatBox(dialog, _chat);
        }
        dialog.Closed += () =>
        {
            _dialog = null;
            Closed?.Invoke();
        };
        Widget start = focus switch
        {
            PackFocus.Last => (Widget?)box ?? exit,
            PackFocus.Chat when box != null => box,
            _ => first,
        };
        start.Focus();
        dialog.AnnounceOpened();
        world.Armed = true;
    }

    public void Close() => _dialog?.Close(nested: true);

    private void Rename(IChest chest)
    {
        var dialog = _app.OpenDialog();
        var name = new EditBox(dialog, "Name", chest.Name) { SelectAllOnFocus = true };
        var rename = new Button(dialog, "Rename");
        _app.SetPrimary(rename);
        rename.Activated += () =>
        {
            chest.Rename(name.Text.Trim());
            dialog.Close();
            _app.Announce("Renamed.");
        };
        dialog.AnnounceOpened();
    }
}
