using CalmTerraria.Powers;
using Srui;

namespace CalmTerraria.Pack;

/// <summary>One stack in the player's inventory, as the game holds it.</summary>
/// <param name="Slot">The game's slot, for acting on the stack.</param>
/// <param name="Equippable">Armour or an accessory: Enter wears it.</param>
public sealed record PackStack(int Slot, string Name, int Count, bool Equippable);

/// <summary>The player's belongings, as the pack screen needs them.</summary>
public interface IPack
{
    IReadOnlyList<PackStack> Stacks();

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

    public PackLine(string name) => Name = name;

    [Field]
    public string Value => Count > 1 ? $"{Name}, {Count}" : Name;
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
    private readonly Dictionary<string, PackLine> _lines = new(StringComparer.Ordinal);
    private List<PackLine> _sorted = new();

    public StackLines(Func<IReadOnlyList<PackStack>> stacks) => _stacks = stacks;

    public IReadOnlyList<PackLine> Lines()
    {
        var groups = _stacks()
            .GroupBy(stack => stack.Name, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var changed = groups.Count != _sorted.Count;
        var sorted = new List<PackLine>(groups.Count);
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (!_lines.TryGetValue(group.Key, out var line))
                _lines[group.Key] = line = new PackLine(group.Key);
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
            foreach (var gone in _lines.Keys.Where(name => !groups.Any(group => group.Key == name)).ToList())
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
        !combo.HasModifier && (combo.Key == Describe || Hotbar(combo.Key) >= 0);

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
/// is open, Enter puts the item in the chest.</summary>
public sealed class PackList : ListBox<PackLine>
{
    private readonly IPack _pack;
    private readonly IChest? _chest;

    public PackList(IWidgetContainer parent, IPack pack, IChest? chest = null)
        : base(parent, "Inventory", Array.Empty<PackLine>())
    {
        _pack = pack;
        _chest = chest;
        BindItems(new StackLines(pack.Stacks).Lines);
    }

    public override bool ReservesKey(KeyCombo combo) =>
        (!combo.HasModifier && (combo.Key == Key.Delete || combo.Key == Key.Space || combo.Key == Key.Enter))
        || ItemKeys.Reserves(combo) || base.ReservesKey(combo);

    protected override bool OnInput(in InputEvent input)
    {
        if (Items.Count == 0)
            return base.OnInput(input);
        var line = Items[SelectedIndex];
        if (input.Kind == InputKind.Activate)
        {
            if (_chest != null)
            {
                // Every stack of it. The line leaves for the other list.
                var all = true;
                foreach (var stack in line.Stacks.ToList())
                    all &= _chest.Deposit(stack.Slot);
                Announce(all ? "Deposited." : "No room in the chest.");
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

    /// <summary>Move a stack to the inventory. False when it did not
    /// all fit.</summary>
    bool Take(int slot);

    /// <summary>Move an inventory stack here. False when it did not
    /// all fit.</summary>
    bool Deposit(int packSlot);

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

    public ChestList(IWidgetContainer parent, IChest chest)
        : base(parent, chest.Name, Array.Empty<PackLine>())
    {
        _chest = chest;
        BindItems(new StackLines(chest.Stacks).Lines);
    }

    public override bool ReservesKey(KeyCombo combo) =>
        (!combo.HasModifier && combo.Key == Key.Enter) || ItemKeys.Reserves(combo) || base.ReservesKey(combo);

    protected override bool OnInput(in InputEvent input)
    {
        if (Items.Count == 0)
            return base.OnInput(input);
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
        var all = true;
        foreach (var stack in Items[SelectedIndex].Stacks.ToList())
            all &= _chest.Take(stack.Slot);
        Announce(all ? "Taken." : "No room in the inventory.");
        return true;
    }
}

/// <summary>Something that can be made right now.</summary>
/// <param name="Id">The game's recipe, for making it.</param>
/// <param name="Count">How many one crafting makes.</param>
/// <param name="Needs">The ingredients, as words.</param>
public sealed record Craftable(int Id, string Name, int Count, string Needs);

public interface ICrafting
{
    /// <summary>What the character can make where they stand, with
    /// what they carry.</summary>
    IReadOnlyList<Craftable> Recipes();

    /// <summary>Make it once. False when the game would not.</summary>
    bool Craft(int id);

    /// <summary>What the game's tooltip says about what is made.</summary>
    string Describe(int id);
}

public sealed partial class CraftLine : Element
{
    public Craftable Recipe { get; internal set; }

    public CraftLine(Craftable recipe) => Recipe = recipe;

    [Field]
    public string Value => Recipe.Count > 1 ? $"{Recipe.Name}, {Recipe.Count}" : Recipe.Name;
}

/// <summary>What can be made now: one alphabetical list. Enter makes
/// it once; Space says what it uses; F1 describes what is made.</summary>
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
                _lines[recipes[i].Id] = line = new CraftLine(recipes[i]);
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
            Announce(_crafting.Craft(recipe.Id) ? "Crafted." : "Can't make that now.");
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
    private readonly ICrafting? _crafting;
    private Dialog? _dialog;

    /// <summary>The screen closed, however it closed.</summary>
    public event Action? Closed;

    public PackScreen(SruiApp app, IPack pack, Action saveAndExit, IPowers? powers = null, ICrafting? crafting = null)
    {
        _crafting = crafting;
        _powers = powers == null ? null : new PowersScreen(app, powers);
        _app = app;
        _pack = pack;
        _saveAndExit = saveAndExit;
    }

    public bool IsOpen => _dialog != null;

    private static readonly string[] ChestActions = { "Loot all", "Deposit all", "Quick stack", "Restock", "Rename" };

    /// <param name="notice">Why something the player reached for
    /// did not open - a chest someone else has open. It stands first,
    /// as a tab stop that does nothing, where the chest would be.</param>
    public void Open(IChest? chest = null, string? notice = null)
    {
        if (_dialog != null)
            return;
        var dialog = _dialog = _app.OpenDialog();
        if (notice != null)
            _ = new CustomWidget(dialog, notice);
        if (chest != null)
        {
            _ = new ChestList(dialog, chest);
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
        _ = new PackList(dialog, _pack, chest);
        _ = new EquipList(dialog, _pack);
        if (_crafting != null)
            _ = new CraftList(dialog, _crafting);
        if (_powers is { } powers)
            new Button(dialog, "Powers").Activated += powers.Open;
        var exit = new Button(dialog, "Save and exit");
        exit.Activated += () =>
        {
            _saveAndExit();
            dialog.Close();
        };
        dialog.Closed += () =>
        {
            _dialog = null;
            Closed?.Invoke();
        };
        dialog.AnnounceOpened();
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
