using CalmTerraria.Powers;
using Srui.Testing;

namespace CalmTerraria.Tests;

internal sealed class FakePowers : IPowers
{
    public bool Omniscience { get; set; }
    public bool GodMode { get; set; }
    public bool OnServer { get; set; }
    public List<string> Given { get; } = new();
    public List<string> Spawned { get; } = new();
    public bool? Day { get; private set; }

    public IReadOnlyList<string> Items() => new[] { "Acorn", "Copper Pickaxe", "Dirt Block", "Torch" };
    public void Give(string item) => Given.Add(item);
    public IReadOnlyList<string> Creatures() => new[] { "Blue Slime", "Zombie" };

    public bool Spawn(string creature)
    {
        if (!OnServer)
            Spawned.Add(creature);
        return !OnServer;
    }

    public bool SetTime(bool day)
    {
        if (!OnServer)
            Day = day;
        return !OnServer;
    }
}

public class PowersScreenTests
{
    private static (TestApp Ui, FakePowers Powers) Open()
    {
        var ui = new TestApp();
        var powers = new FakePowers();
        new PowersScreen(ui.App, powers).Open();
        return (ui, powers);
    }

    [Fact]
    public void OpensOnOmniscienceAndSpaceSwitchesIt()
    {
        var (ui, powers) = Open();
        ui.Expect("Omniscience check box not checked");
        ui.Press("space");
        ui.Expect("checked");
        Assert.True(powers.Omniscience);
    }

    [Fact]
    public void TheWalk()
    {
        var (ui, _) = Open();
        ui.Drain();
        ui.Press("tab");
        ui.Expect("God mode check box not checked");
        ui.Press("tab");
        ui.Expect("Give item button");
        ui.Press("tab");
        ui.Expect("Spawn creature button");
        ui.Press("tab");
        ui.Expect("Make it day button");
        ui.Press("tab");
        ui.Expect("Make it night button");
    }

    [Fact]
    public void GivingIsFoundByTypingAndTheListStaysForMore()
    {
        var (ui, powers) = Open();
        ui.Press("tab");
        ui.Press("tab");
        ui.Drain();
        ui.Press("enter");
        ui.Expect("Items list Acorn");
        ui.Type("to");
        ui.Expect("Torch");
        ui.Press("enter");
        ui.Expect("Given.");
        ui.Press("up");
        ui.Expect("Dirt Block");
        ui.Press("enter");
        ui.Expect("Given.");
        Assert.Equal(new[] { "Torch", "Dirt Block" }, powers.Given);
    }

    [Fact]
    public void SpawningOnAServerIsRefusedAloud()
    {
        var (ui, powers) = Open();
        powers.OnServer = true;
        ui.Press("tab");
        ui.Press("tab");
        ui.Press("tab");
        ui.Press("enter");
        ui.Drain();
        ui.Press("enter");
        ui.Expect("Not on a server.");
        Assert.Empty(powers.Spawned);
    }

    [Fact]
    public void TheTimeButtonsSayWhatItIsNow()
    {
        var (ui, powers) = Open();
        for (var i = 0; i < 5; i++)
            ui.Press("tab");
        ui.Drain();
        ui.Press("enter");
        ui.Expect("Night.");
        Assert.False(powers.Day);
    }
}
