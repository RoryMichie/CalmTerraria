using CalmTerraria.Pack;
using Srui.Testing;

namespace CalmTerraria.Tests;

internal sealed class FakeChest : IChest
{
    private readonly FakePack _pack;
    public List<PackStack> Contents { get; } = new();
    public string Name { get; private set; } = "Chest";
    public bool PackFull { get; set; }
    public bool Full { get; set; }
    public bool CanRename { get; set; } = true;
    public List<string> Did { get; } = new();
    private int _nextSlot;

    public FakeChest(FakePack pack) => _pack = pack;

    public FakeChest Add(string name, int count = 1)
    {
        Contents.Add(new PackStack(_nextSlot++, name, count, false));
        return this;
    }

    public IReadOnlyList<PackStack> Stacks() => Contents;

    public bool Take(int slot)
    {
        if (PackFull)
            return false;
        var stack = Contents.Single(s => s.Slot == slot);
        Contents.Remove(stack);
        _pack.Add(stack.Name, stack.Count);
        return true;
    }

    public bool Deposit(int packSlot)
    {
        if (Full)
            return false;
        var stack = _pack.Inventory.Single(s => s.Slot == packSlot);
        _pack.Inventory.Remove(stack);
        Add(stack.Name, stack.Count);
        return true;
    }

    public void LootAll() => Did.Add("loot");
    public void DepositAll() => Did.Add("deposit");
    public void QuickStack() => Did.Add("stack");
    public void Restock() => Did.Add("restock");
    public void Rename(string name) => Name = name;
}

internal sealed class FakeCrafting : ICrafting
{
    public List<Craftable> Available { get; } = new()
    {
        new Craftable(7, "Work Bench", 1, "10 Wood"),
        new Craftable(3, "Torch", 3, "1 Wood, 1 Gel"),
    };
    public List<int> Made { get; } = new();
    public bool Refuse { get; set; }

    public IReadOnlyList<Craftable> Recipes() => Available;

    public bool Craft(int id)
    {
        if (Refuse)
            return false;
        Made.Add(id);
        return true;
    }
}

public class ChestTests
{
    private static (TestApp Ui, FakePack Pack, FakeChest Chest) Open()
    {
        var ui = new TestApp();
        var pack = new FakePack().Add("Wood", 300).Add("Wood", 47).Add("Acorn", 5);
        var chest = new FakeChest(pack).Add("Gold Bar", 12).Add("Spear").Add("Gold Bar", 3);
        new PackScreen(ui.App, pack, () => { }).Open(chest);
        return (ui, pack, chest);
    }

    [Fact]
    public void OpensOnTheChestWithStacksMerged()
    {
        var (ui, _, _) = Open();
        ui.Expect("Chest list Gold Bar, 15");
    }

    [Fact]
    public void TheWalkPutsTheChestFirst()
    {
        var (ui, _, _) = Open();
        ui.Drain();
        ui.Press("tab");
        ui.Expect("Chest actions list Loot all");
        ui.Press("tab");
        ui.Expect("Inventory list Acorn, 5");
        ui.Press("tab");
        ui.Expect("Equipment list Head, empty");
    }

    [Fact]
    public void EnterTakesEveryStackOfTheItem()
    {
        var (ui, pack, chest) = Open();
        ui.Drain();
        ui.Press("enter");
        ui.Expect("Taken.", "Spear");
        Assert.Equal(15, pack.Inventory.Where(s => s.Name == "Gold Bar").Sum(s => s.Count));
        Assert.Single(chest.Contents);
    }

    [Fact]
    public void TakingWithNoRoomSaysSo()
    {
        var (ui, _, chest) = Open();
        chest.PackFull = true;
        ui.Drain();
        ui.Press("enter");
        ui.Expect("No room in the inventory.");
    }

    [Fact]
    public void WithAChestOpenEnterInTheInventoryDeposits()
    {
        var (ui, pack, chest) = Open();
        ui.Press("tab");
        ui.Press("tab");
        ui.Press("end");
        ui.Drain();
        ui.Press("enter");
        ui.Expect("Deposited.", "Acorn, 5");
        Assert.DoesNotContain(pack.Inventory, s => s.Name == "Wood");
        Assert.Equal(347, chest.Contents.Where(s => s.Name == "Wood").Sum(s => s.Count));
    }

    [Fact]
    public void AFullChestSaysSo()
    {
        var (ui, _, chest) = Open();
        chest.Full = true;
        ui.Press("tab");
        ui.Press("tab");
        ui.Drain();
        ui.Press("enter");
        ui.Expect("No room in the chest.");
    }

    [Fact]
    public void TheActionsConfirmWhatWasDone()
    {
        var (ui, _, chest) = Open();
        ui.Press("tab");
        ui.Drain();
        ui.Press("enter");
        ui.Expect("Looted.");
        ui.Press("down");
        ui.Press("down");
        ui.Drain();
        ui.Press("enter");
        ui.Expect("Stacked.");
        Assert.Equal(new[] { "loot", "stack" }, chest.Did);
    }

    [Fact]
    public void RenamingIsTypedAndEnterSubmits()
    {
        var (ui, _, chest) = Open();
        ui.Press("tab");
        ui.Press("end");
        ui.Expect("Rename");
        ui.Drain();
        ui.Press("enter");
        ui.Type("Ores");
        ui.Drain();
        ui.Press("enter");
        Assert.Equal("Ores", chest.Name);
        Assert.Contains("Renamed.", ui.Spoken());
    }

    [Fact]
    public void APiggyBankHasNoRename()
    {
        var ui = new TestApp();
        var pack = new FakePack();
        var chest = new FakeChest(pack) { CanRename = false };
        new PackScreen(ui.App, pack, () => { }).Open(chest);
        ui.Press("tab");
        ui.Drain();
        ui.Press("end");
        ui.Expect("Restock");
    }
}

public class CraftTests
{
    private static (TestApp Ui, FakeCrafting Crafting) Open()
    {
        var ui = new TestApp();
        var crafting = new FakeCrafting();
        new PackScreen(ui.App, new FakePack(), () => { }, crafting: crafting).Open();
        ui.Press("tab");
        ui.Drain();
        ui.Press("tab");
        return (ui, crafting);
    }

    [Fact]
    public void TheListIsAlphabeticalAndSaysHowManyAreMade()
    {
        var (ui, _) = Open();
        ui.Expect("Crafting list Torch, 3");
        ui.Press("down");
        ui.Expect("Work Bench");
    }

    [Fact]
    public void SpaceSaysWhatItUses()
    {
        var (ui, _) = Open();
        ui.Drain();
        ui.Press("space");
        ui.Expect("1 Wood, 1 Gel");
    }

    [Fact]
    public void EnterMakesItOnce()
    {
        var (ui, crafting) = Open();
        ui.Drain();
        ui.Press("enter");
        ui.Expect("Crafted.");
        Assert.Equal(new[] { 3 }, crafting.Made);
    }

    [Fact]
    public void ARefusalIsSpoken()
    {
        var (ui, crafting) = Open();
        crafting.Refuse = true;
        ui.Drain();
        ui.Press("enter");
        ui.Expect("Can't make that now.");
    }
}
