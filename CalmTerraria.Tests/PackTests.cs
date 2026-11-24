using CalmTerraria.Pack;
using Srui;
using Srui.Testing;

namespace CalmTerraria.Tests;

internal sealed class FakePack : IPack
{
    public List<PackStack> Inventory { get; } = new();
    public List<(string Slot, string? Item)> Worn { get; } = new()
    {
        ("Head", null), ("Body", "Wood Breastplate"), ("Legs", null),
    };
    public int? Held { get; private set; }
    public bool Full { get; set; }
    private int _nextSlot = 100;

    public FakePack Add(string name, int count = 1, bool equippable = false)
    {
        Inventory.Add(new PackStack(_nextSlot++, name, count, equippable));
        return this;
    }

    public IReadOnlyList<PackStack> Stacks() => Inventory;
    public IReadOnlyList<(string Slot, string? Item)> Equipment() => Worn;
    public void Hold(int slot) => Held = slot;

    public bool Equip(int slot)
    {
        var stack = Inventory.Single(s => s.Slot == slot);
        var index = Worn.FindIndex(w => w.Slot == "Head");
        Inventory.Remove(stack);
        if (Worn[index].Item is { } old)
            Add(old, equippable: true);
        Worn[index] = ("Head", stack.Name);
        return true;
    }

    public void Trash(int slot) => Inventory.RemoveAll(s => s.Slot == slot);
    public string Describe(int slot) => Inventory.Single(s => s.Slot == slot).Name == "Acorn" ? "" : "7 melee damage. Material";
    public List<(int Slot, int Hotbar)> Hotbarred { get; } = new();
    public void ToHotbar(int slot, int hotbar) => Hotbarred.Add((slot, hotbar));

    public bool Unequip(int index)
    {
        if (Full)
            return false;
        Add(Worn[index].Item!, equippable: true);
        Worn[index] = (Worn[index].Slot, null);
        return true;
    }
}

public class PackScreenTests
{
    private static (TestApp Ui, FakePack Pack, PackScreen Screen) Open(Action<FakePack>? fill = null)
    {
        var ui = new TestApp();
        var pack = new FakePack();
        if (fill == null)
            pack.Add("Wood", 300).Add("Copper Pickaxe").Add("Wood", 47).Add("Acorn", 5).Add("Copper Helmet", equippable: true);
        else
            fill(pack);
        var screen = new PackScreen(ui.App, pack, () => { });
        screen.Open();
        return (ui, pack, screen);
    }

    [Fact]
    public void OpensOnTheInventoryAlphabeticalWithStacksMerged()
    {
        var (ui, _, _) = Open();
        ui.Expect("Carried list Acorn, 5");
        ui.Press("down");
        ui.Expect("Copper Helmet");
        ui.Press("down");
        ui.Expect("Copper Pickaxe");
        ui.Press("down");
        ui.Expect("Wood, 347");
    }

    [Fact]
    public void TheWalk()
    {
        var (ui, _, _) = Open();
        ui.Drain();
        ui.Press("tab");
        ui.Expect("Equipment list Head, empty");
        ui.Press("tab");
        ui.Expect("Save and exit button");
    }

    [Fact]
    public void EnterHoldsAnOrdinaryItemFromItsLargestStack()
    {
        var (ui, pack, _) = Open();
        ui.Press("end");
        ui.Press("enter");
        ui.Expect("Held.");
        Assert.Equal(pack.Inventory.Single(s => s.Count == 300).Slot, pack.Held);
    }

    [Fact]
    public void EnterWearsWhatCanBeWornAndTheListMovesOn()
    {
        var (ui, pack, _) = Open();
        ui.Press("down");
        ui.Press("enter");
        ui.Expect("Equipped.", "Copper Pickaxe");
        Assert.Equal("Copper Helmet", pack.Worn[0].Item);
    }

    [Fact]
    public void SpaceHoldsEvenWhatCanBeWorn()
    {
        var (ui, pack, _) = Open();
        ui.Press("down");
        ui.Press("space");
        ui.Expect("Held.");
        Assert.NotNull(pack.Held);
    }

    [Fact]
    public void DeleteTrashesOneStackAndTheLineSaysWhatIsLeft()
    {
        var (ui, pack, _) = Open();
        ui.Press("end");
        ui.Press("delete");
        ui.Expect("Trashed.", "Wood, 300");
        Assert.Single(pack.Inventory, s => s.Name == "Wood");
    }

    [Fact]
    public void TakingOffAPieceReadsTheEmptiedSlotAndNothingElse()
    {
        var (ui, pack, _) = Open();
        ui.Press("tab");
        ui.Press("down");
        ui.Expect("Body, Wood Breastplate");
        ui.Press("enter");
        ui.Expect("Body, empty");
        Assert.Contains(pack.Inventory, s => s.Name == "Wood Breastplate");
    }

    [Fact]
    public void TakingOffWithNoRoomSaysSo()
    {
        var (ui, pack, _) = Open();
        pack.Full = true;
        ui.Press("tab");
        ui.Press("down");
        ui.Press("enter");
        ui.Expect("No room in the inventory.");
    }

    [Fact]
    public void AnEmptyInventoryIsStillAScreen()
    {
        var (ui, _, _) = Open(_ => { });
        var spoken = ui.Spoken();
        Assert.Single(spoken);
        Assert.StartsWith("Carried list", spoken[0]);
    }

    [Fact]
    public void ADigitPutsTheLargestStackInThatHotbarSlot()
    {
        var (ui, pack, _) = Open();
        ui.Press("end");
        ui.Drain();
        ui.Type('3');
        ui.Expect("Hotbar 3.");
        ui.Type('0');
        ui.Expect("Hotbar 0.");
        var wood = pack.Inventory.Single(s => s.Count == 300).Slot;
        Assert.Equal(new[] { (wood, 2), (wood, 9) }, pack.Hotbarred);
    }

    [Fact]
    public void F1DescribesAndSaysSoWhenThereIsNothingToSay()
    {
        var (ui, _, _) = Open();
        ui.Drain();
        ui.Press("f1");
        ui.Expect("No description.");
        ui.Press("down");
        ui.Drain();
        ui.Press("f1");
        ui.Expect("7 melee damage. Material");
    }

    [Fact]
    public void EscapeClosesAndSaysSo()
    {
        var (ui, _, screen) = Open();
        var closed = false;
        screen.Closed += () => closed = true;
        ui.Press("escape");
        Assert.True(closed);
        Assert.False(screen.IsOpen);
    }
}
