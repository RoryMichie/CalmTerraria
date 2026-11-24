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

/// <summary>The inventory: one alphabetical list. Enter does the
/// natural thing with the item - wears what can be worn, holds
/// anything else - Space always holds, Delete trashes one stack.</summary>
public sealed class PackList : ListBox<PackLine>
{
    private readonly IPack _pack;
    private readonly Dictionary<string, PackLine> _lines = new(StringComparer.Ordinal);
    private List<PackLine> _sorted = new();

    public PackList(IWidgetContainer parent, IPack pack)
        : base(parent, "Inventory", Array.Empty<PackLine>())
    {
        _pack = pack;
        BindItems(Lines);
    }

    /// <summary>The lines for the inventory as it is now. A line keeps
    /// its object for as long as its item is held at all, so the
    /// cursor stays with the item while counts change around it.</summary>
    private IReadOnlyList<PackLine> Lines()
    {
        var groups = _pack.Stacks()
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

    public override bool ReservesKey(KeyCombo combo) =>
        (!combo.HasModifier && (combo.Key == Key.Delete || combo.Key == Key.Space || combo.Key == Key.Enter))
        || base.ReservesKey(combo);

    protected override bool OnInput(in InputEvent input)
    {
        if (Items.Count == 0)
            return base.OnInput(input);
        var line = Items[SelectedIndex];
        if (input.Kind == InputKind.Activate)
        {
            if (!line.Equippable)
                return Hold(line);
            // The line leaves this list for the equipment list, where
            // focus is not: said, since nothing here shows it.
            Announce(_pack.Equip(line.Stacks[0].Slot) ? "Equipped." : "Can't wear that now.");
            return true;
        }
        if (input.IsChar(' '))
            return Hold(line);
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

/// <summary>The screen behind Escape in a world: the inventory, what
/// is worn, the powers, and the way out of the world. Each is one tab
/// stop.</summary>
public sealed class PackScreen
{
    private readonly SruiApp _app;
    private readonly IPack _pack;
    private readonly Action _saveAndExit;
    private readonly PowersScreen? _powers;
    private Dialog? _dialog;

    /// <summary>The screen closed, however it closed.</summary>
    public event Action? Closed;

    public PackScreen(SruiApp app, IPack pack, Action saveAndExit, IPowers? powers = null)
    {
        _powers = powers == null ? null : new PowersScreen(app, powers);
        _app = app;
        _pack = pack;
        _saveAndExit = saveAndExit;
    }

    public bool IsOpen => _dialog != null;

    public void Open()
    {
        if (_dialog != null)
            return;
        var dialog = _dialog = _app.OpenDialog();
        _ = new PackList(dialog, _pack);
        _ = new EquipList(dialog, _pack);
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
}
