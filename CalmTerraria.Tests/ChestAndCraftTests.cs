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

    public int Take(int slot, int count)
    {
        if (PackFull)
            return 0;
        var index = Contents.FindIndex(s => s.Slot == slot);
        var stack = Contents[index];
        if (count >= stack.Count)
            Contents.RemoveAt(index);
        else
            Contents[index] = stack with { Count = stack.Count - count };
        _pack.Add(stack.Name, count);
        return count;
    }

    public int Deposit(int packSlot, int count)
    {
        if (Full)
            return 0;
        var index = _pack.Inventory.FindIndex(s => s.Slot == packSlot);
        var stack = _pack.Inventory[index];
        if (count >= stack.Count)
            _pack.Inventory.RemoveAt(index);
        else
            _pack.Inventory[index] = stack with { Count = stack.Count - count };
        Add(stack.Name, count);
        return count;
    }

    public void LootAll() => Did.Add("loot");
    public void DepositAll() => Did.Add("deposit");
    public void QuickStack() => Did.Add("stack");
    public void Restock() => Did.Add("restock");
    public void Rename(string name) => Name = name;
    public string Describe(int slot) => "Material";

    public int TakeOne(int slot)
    {
        if (Take(slot, 1) == 0)
            return -1;
        return _pack.Inventory[^1].Slot;
    }

    public List<(int Slot, int Hotbar)> Hotbarred { get; } = new();

    public bool ToHotbar(int slot, int hotbar)
    {
        if (PackFull)
            return false;
        Hotbarred.Add((slot, hotbar));
        return true;
    }
}

internal sealed class FakeCrafting : ICrafting
{
    public List<Craftable> Available { get; } = new()
    {
        new Craftable(7, "Work Bench", 1, "10 Wood"),
        new Craftable(3, "Torch", 3, "1 Wood, 1 Gel", 4),
    };
    public List<int> Made { get; } = new();
    public bool Refuse { get; set; }

    public IReadOnlyList<Craftable> Recipes() => Available;

    public string Describe(int id) => "Provides light";

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
        ui.Expect("Chest list Gold Bar, 15. Material");
    }

    [Fact]
    public void TheWalkPutsTheChestFirst()
    {
        var (ui, _, _) = Open();
        ui.Drain();
        ui.Press("tab");
        ui.Expect("Chest actions list Loot all");
        ui.Press("tab");
        ui.Expect("Carried list Acorn, 5");
        ui.Press("tab");
        ui.Expect("Equipment list Head, empty");
    }

    [Fact]
    public void EnterTakesEveryStackOfTheItem()
    {
        var (ui, pack, chest) = Open();
        ui.Drain();
        ui.Press("enter");
        ui.Drain();
        ui.Press("enter");
        ui.Expect("Taken.", "Chest list Spear. Material");
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
        ui.Drain();
        ui.Press("enter");
        Assert.Contains("No room in the inventory.", ui.Spoken());
    }

    [Fact]
    public void ADigitTakesStraightToTheHotbarOrSaysThereIsNoRoom()
    {
        var (ui, _, chest) = Open();
        ui.Drain();
        ui.Type('1');
        ui.Expect("Hotbar 1.");
        Assert.Equal(0, chest.Hotbarred.Single().Hotbar);
        chest.PackFull = true;
        ui.Type('2');
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
        ui.Drain();
        ui.Press("enter");
        Assert.Contains("Deposited.", ui.Spoken());
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
        ui.Drain();
        ui.Press("enter");
        Assert.Contains("No room in the chest.", ui.Spoken());
    }

    [Fact]
    public void ANumberTypedIntoTheBoxTakesThatManyAndTheRestStay()
    {
        var (ui, pack, chest) = Open();
        ui.Press("enter");
        ui.Expect("How many, up to 15 edit selected 15");
        ui.Type("4");
        ui.Drain();
        ui.Press("enter");
        Assert.Contains("Taken.", ui.Spoken());
        Assert.Equal(4, pack.Inventory.Where(s => s.Name == "Gold Bar").Sum(s => s.Count));
        Assert.Equal(11, chest.Contents.Where(s => s.Name == "Gold Bar").Sum(s => s.Count));
    }

    [Fact]
    public void TheBoxRefusesWhatIsNotANumberInRangeAndEscapeMovesNothing()
    {
        var (ui, pack, chest) = Open();
        ui.Press("enter");
        ui.Type("99");
        ui.Drain();
        ui.Press("enter");
        ui.Expect("A number from 1 to 15.");
        ui.Press("escape");
        Assert.Equal(15, chest.Contents.Where(s => s.Name == "Gold Bar").Sum(s => s.Count));
        Assert.DoesNotContain(pack.Inventory, s => s.Name == "Gold Bar");
    }

    [Fact]
    public void OneOfSomethingIsNotAskedAbout()
    {
        var (ui, pack, _) = Open();
        ui.Press("down");
        ui.Drain();
        ui.Press("enter");
        Assert.Contains("Taken.", ui.Spoken());
        Assert.Contains(pack.Inventory, s => s.Name == "Spear");
    }

    [Fact]
    public void LeftBracketInAChestTakesOneOutAndUsesIt()
    {
        var (ui, pack, chest) = Open();
        ui.Drain();
        ui.Type('[');
        Assert.Single(pack.Used);
        Assert.Equal(14, chest.Contents.Where(s => s.Name == "Gold Bar").Sum(s => s.Count));
        chest.PackFull = true;
        ui.Type('[');
        ui.Expect("No room in the inventory.");
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
    public void AChestSomeoneElseHasOpenIsATabStopThatSaysSoAndDoesNothing()
    {
        var ui = new TestApp();
        new PackScreen(ui.App, new FakePack().Add("Acorn", 5), () => { }).Open(notice: "Chest, in use by Austin");
        ui.Expect("Chest, in use by Austin");
        ui.Press("enter");
        ui.ExpectNoSpeech();
        ui.Press("tab");
        ui.Expect("Carried list Acorn, 5");
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
        ui.Expect("Crafting list Torch, 3. Provides light");
        ui.Press("down");
        ui.Expect("Work Bench. Provides light");
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
        ui.Drain();
        ui.Press("enter");
        Assert.Contains("Crafted.", ui.Spoken());
        Assert.Equal(new[] { 3 }, crafting.Made);
    }

    [Fact]
    public void WhatCouldBeMadeSeveralTimesAsksHowManyStartingAtOne()
    {
        var (ui, crafting) = Open();
        ui.Drain();
        ui.Press("enter");
        ui.Expect("How many, up to 4 edit selected 1");
        ui.Type("3");
        ui.Drain();
        ui.Press("enter");
        Assert.Contains("Crafted.", ui.Spoken());
        Assert.Equal(new[] { 3, 3, 3 }, crafting.Made);
    }

    [Fact]
    public void ARefusalIsSpoken()
    {
        var (ui, crafting) = Open();
        crafting.Refuse = true;
        ui.Drain();
        ui.Press("enter");
        ui.Drain();
        ui.Press("enter");
        Assert.Contains("Can't make that now.", ui.Spoken());
    }
}
