using CalmTerraria.Pack;
using Srui.Testing;

namespace CalmTerraria.Tests;

internal sealed class FakeShop : IShop
{
    private readonly FakePack _pack;
    public int Coins { get; set; } = 500;
    public bool PackFull { get; set; }
    public List<ShopItem> Stock { get; } = new()
    {
        new ShopItem(0, "Torch", 1, "50 copper", 9999),
        new ShopItem(1, "Mining Helmet", 1, "4 gold"),
    };
    public List<int> Sold { get; } = new();

    public FakeShop(FakePack pack) => _pack = pack;

    public IReadOnlyList<ShopItem> Items() => Stock;

    public BuyResult Buy(int slot)
    {
        var cost = slot == 0 ? 50 : 40000;
        if (cost > Coins)
            return BuyResult.CantAfford;
        if (PackFull)
            return BuyResult.NoRoom;
        Coins -= cost;
        _pack.Add(Stock.Single(item => item.Slot == slot).Name);
        return BuyResult.Bought;
    }

    public int Sell(int packSlot, int count)
    {
        var index = _pack.Inventory.FindIndex(s => s.Slot == packSlot);
        var stack = _pack.Inventory[index];
        if (stack.Name == "Acorn")
            return 0;
        if (count >= stack.Count)
            _pack.Inventory.RemoveAt(index);
        else
            _pack.Inventory[index] = stack with { Count = stack.Count - count };
        Sold.Add(packSlot);
        Coins += 10;
        return count;
    }

    public string Money() => $"{Coins} copper";
    public string Describe(int slot) => "Provides light";
}

public class TalkTests
{
    [Fact]
    public void WhatIsSaidStandsFirstAndTheRepliesFollow()
    {
        var ui = new TestApp();
        int? chosen = null;
        new PackScreen(ui.App, new FakePack().Add("Acorn", 5), () => { }).Open(
            talk: new Talk("Angler", "I've got a job for you.", new[] { "Quest", "Happiness" }),
            choose: index => chosen = index);
        ui.Expect("Angler: I've got a job for you.");
        ui.Press("enter");
        ui.ExpectNoSpeech();
        ui.Press("tab");
        ui.Expect("Replies list Quest");
        ui.Press("down");
        ui.Drain();
        ui.Press("enter");
        Assert.Equal(1, chosen);
        ui.Press("tab");
        ui.Expect("Carried list Acorn, 5");
    }

    [Fact]
    public void SomeoneWithNothingToAskHasNoRepliesList()
    {
        var ui = new TestApp();
        new PackScreen(ui.App, new FakePack().Add("Acorn", 5), () => { }).Open(
            talk: new Talk("Sign", "Keep out.", Array.Empty<string>()));
        ui.Expect("Sign: Keep out.");
        ui.Press("tab");
        ui.Expect("Carried list Acorn, 5");
    }
}

public class ShopTests
{
    private static (TestApp Ui, FakePack Pack, FakeShop Shop) Open()
    {
        var ui = new TestApp();
        var pack = new FakePack().Add("Wood", 300).Add("Wood", 47).Add("Acorn", 5);
        var shop = new FakeShop(pack);
        new PackScreen(ui.App, pack, () => { }).Open(shop: shop);
        return (ui, pack, shop);
    }

    [Fact]
    public void TheShopIsAlphabeticalWithPrices()
    {
        var (ui, _, _) = Open();
        ui.Expect("Shop list Mining Helmet, 4 gold");
        ui.Press("down");
        ui.Expect("Torch, 50 copper");
    }

    [Fact]
    public void BuyingSaysWhatIsLeftAndFailingSaysWhy()
    {
        var (ui, pack, shop) = Open();
        ui.Drain();
        ui.Press("enter");
        ui.Expect("Not enough money. You have 500 copper.");
        ui.Press("down");
        ui.Drain();
        ui.Press("enter");
        ui.Drain();
        ui.Press("enter");
        Assert.Contains("Bought. 450 copper left.", ui.Spoken());
        Assert.Contains(pack.Inventory, s => s.Name == "Torch");
        ui.Press("enter");
        ui.Type("3");
        ui.Drain();
        ui.Press("enter");
        Assert.Contains("Bought. 300 copper left.", ui.Spoken());
        shop.PackFull = true;
        ui.Press("enter");
        ui.Drain();
        ui.Press("enter");
        Assert.Contains("No room in the inventory.", ui.Spoken());
    }

    [Fact]
    public void InAShopEnterInTheInventorySellsEveryStack()
    {
        var (ui, pack, shop) = Open();
        ui.Press("tab");
        ui.Expect("Carried list Acorn, 5");
        ui.Press("enter");
        ui.Drain();
        ui.Press("enter");
        Assert.Contains("Can't sell that.", ui.Spoken());
        ui.Press("end");
        ui.Drain();
        ui.Press("enter");
        ui.Drain();
        ui.Press("enter");
        Assert.Contains("Sold. 520 copper.", ui.Spoken());
        Assert.Equal(2, shop.Sold.Count);
        Assert.DoesNotContain(pack.Inventory, s => s.Name == "Wood");
    }
}
