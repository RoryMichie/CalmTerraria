using CalmTerraria.Pack;
using Srui.Testing;

namespace CalmTerraria.Tests;

internal sealed class FakeBuild : IBuild
{
    public string Title => "Build, 12 by 5";
    public List<BuildOption> Jobs { get; } = new()
    {
        new BuildOption(1, "Fill with Dirt Block, 60 tiles", "60 tiles to fill. You have 136 Dirt Block."),
        new BuildOption(2, "Clear, 37 tiles", "You would get 30 Dirt, 7 Stone. Lava in the area."),
    };
    public int? Started { get; private set; }
    public IReadOnlyList<BuildOption> Options() => Jobs;
    public void Start(int id) => Started = id;
}

public class BuildListTests
{
    [Fact]
    public void TheBuildListComesFirstAndAJobShowsItsPlanBeforeItStarts()
    {
        var ui = new TestApp();
        var build = new FakeBuild();
        var screen = new PackScreen(ui.App, new FakePack().Add("Acorn", 5), () => { });
        screen.Open(build: build);
        ui.Expect("Build, 12 by 5 list Fill with Dirt Block, 60 tiles");
        ui.Press("down");
        ui.Expect("Clear, 37 tiles");
        ui.Press("enter");
        ui.Expect("You would get 30 Dirt, 7 Stone. Lava in the area.");
        Assert.Null(build.Started);
        ui.Press("tab");
        ui.Expect("Start button");
        ui.Press("enter");
        Assert.Equal(2, build.Started);
        Assert.False(screen.IsOpen);
    }

    [Fact]
    public void EscapeFromThePlanGoesBackToTheListAndStartsNothing()
    {
        var ui = new TestApp();
        var build = new FakeBuild();
        var screen = new PackScreen(ui.App, new FakePack(), () => { });
        screen.Open(build: build);
        ui.Press("enter");
        ui.Drain();
        ui.Press("escape");
        Assert.Null(build.Started);
        Assert.True(screen.IsOpen);
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
