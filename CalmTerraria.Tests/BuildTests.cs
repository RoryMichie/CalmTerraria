using CalmTerraria.Pack;
using Srui.Testing;

namespace CalmTerraria.Tests;

internal sealed class FakeBuild : IBuild
{
    public string Title => "Build, 12 by 5";
    public List<BuildOption> Jobs { get; } = new()
    {
        new BuildOption("fill", "Fill with Dirt Block, 60 tiles", "You have 136."),
        new BuildOption("clear", "Clear, 37 tiles", "Gives 30 Dirt, 7 Stone. 4 tiles of lava."),
    };
    public string? Started { get; private set; }
    public IReadOnlyList<BuildOption> Options() => Jobs;
    public void Start(string key) => Started = key;
}

public class BuildListTests
{
    [Fact]
    public void TheBuildListComesFirstAndEachJobReadsWhatItTakes()
    {
        var ui = new TestApp();
        var build = new FakeBuild();
        var screen = new PackScreen(ui.App, new FakePack().Add("Acorn", 5), () => { });
        screen.Open(build: build);
        ui.Expect("Build, 12 by 5 list Fill with Dirt Block, 60 tiles. You have 136.");
        ui.Press("down");
        ui.Expect("Clear, 37 tiles. Gives 30 Dirt, 7 Stone. 4 tiles of lava.");
    }

    [Fact]
    public void EnterStartsTheJobAndPutsTheScreenAway()
    {
        var ui = new TestApp();
        var build = new FakeBuild();
        var screen = new PackScreen(ui.App, new FakePack(), () => { });
        screen.Open(build: build);
        ui.Press("down");
        ui.Press("enter");
        Assert.Equal("clear", build.Started);
        Assert.False(screen.IsOpen);
    }

    [Fact]
    public void TheListFollowsWhatCanBeDoneWhileItIsOpen()
    {
        var ui = new TestApp();
        var build = new FakeBuild();
        var screen = new PackScreen(ui.App, new FakePack(), () => { });
        screen.Open(build: build);
        ui.Press("down");
        ui.Drain();
        // Another block in the hand: the fill is of something else,
        // and the cursor stays on Clear.
        build.Jobs[0] = new BuildOption("fill", "Fill with Stone Block, 60 tiles", "You have 12, 48 short.");
        build.Jobs.Add(new BuildOption("unwall", "Remove walls, 9 tiles", ""));
        ui.Press("up");
        ui.Expect("Fill with Stone Block, 60 tiles. You have 12, 48 short.");
        ui.Press("end");
        ui.Expect("Remove walls, 9 tiles");
    }

    [Fact]
    public void WithNothingToDoThereIsNoBuildList()
    {
        var ui = new TestApp();
        var build = new FakeBuild();
        build.Jobs.Clear();
        new PackScreen(ui.App, new FakePack().Add("Acorn", 5), () => { }).Open(build: build);
        ui.Expect("Carried list Acorn, 5");
    }
}
