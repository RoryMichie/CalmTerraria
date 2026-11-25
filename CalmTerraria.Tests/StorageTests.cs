using CalmTerraria.Pack;
using Srui.Testing;

namespace CalmTerraria.Tests;

public class StorageTests
{
    private static (TestApp Ui, FakePack Pack, FakeChest Storage) Open()
    {
        var ui = new TestApp();
        var pack = new FakePack().Add("Wood", 300).Add("Acorn", 5);
        var storage = new FakeChest(pack).Add("Gold Bar", 12).Add("Spear").Add("Gold Bar", 3);
        new PackScreen(ui.App, pack, () => { }).Open(storage: storage);
        return (ui, pack, storage);
    }

    [Fact]
    public void StorageComesAfterTheInventoryThenWhatCanBeDoneWithIt()
    {
        var (ui, _, _) = Open();
        ui.Expect("Carried list Acorn, 5");
        ui.Press("tab");
        ui.Expect("Chest list Gold Bar, 15. Material");
        ui.Press("tab");
        ui.Expect("Storage actions list Quick stack");
        ui.Press("tab");
        ui.Expect("Equipment list Head, empty");
    }

    [Fact]
    public void EnterInStorageTakesTheItem()
    {
        var (ui, pack, storage) = Open();
        ui.Press("tab");
        ui.Drain();
        ui.Press("enter");
        ui.Expect("How many, up to 15 edit selected 15");
        ui.Press("enter");
        Assert.Contains("Taken.", ui.Spoken());
        Assert.DoesNotContain(storage.Contents, stack => stack.Name == "Gold Bar");
        Assert.Equal(15, pack.Inventory.Where(stack => stack.Name == "Gold Bar").Sum(stack => stack.Count));
    }

    [Fact]
    public void ShiftEnterInTheInventoryPutsTheItemAwayAndEnterStillHoldsIt()
    {
        var (ui, pack, storage) = Open();
        ui.Press("end");
        ui.Drain();
        ui.Press("enter");
        Assert.Contains("Held.", ui.Spoken());
        ui.Press("shift+enter");
        ui.Expect("How many, up to 300 edit selected 300");
        ui.Press("enter");
        Assert.Contains("Stored.", ui.Spoken());
        Assert.DoesNotContain(pack.Inventory, stack => stack.Name == "Wood");
        Assert.Contains(storage.Contents, stack => stack is { Name: "Wood", Count: 300 });
    }

    [Fact]
    public void FullStorageSaysSo()
    {
        var (ui, _, storage) = Open();
        storage.Full = true;
        ui.Press("shift+enter");
        ui.Drain();
        ui.Press("enter");
        Assert.Contains("No room in storage.", ui.Spoken());
    }

    [Fact]
    public void TheActionsAreTheStoragesOwn()
    {
        var (ui, _, storage) = Open();
        ui.Press("tab");
        ui.Press("tab");
        ui.Press("enter");
        ui.Press("down");
        ui.Press("enter");
        ui.Press("down");
        ui.Press("enter");
        Assert.Equal(new[] { "stack", "deposit", "restock" }, storage.Did);
    }

    [Fact]
    public void WithAChestOpenThereIsNoStorage()
    {
        var ui = new TestApp();
        var pack = new FakePack().Add("Acorn", 5);
        var chest = new FakeChest(pack).Add("Spear");
        var storage = new FakeChest(pack).Add("Gold Bar", 12);
        new PackScreen(ui.App, pack, () => { }).Open(chest, storage: storage);
        ui.Drain();
        ui.Press("tab");
        ui.Press("tab");
        ui.Expect("Carried list Acorn, 5");
        ui.Press("tab");
        ui.Expect("Equipment list Head, empty");
    }
}
