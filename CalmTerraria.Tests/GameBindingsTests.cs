using CalmTerraria.Keys;

namespace CalmTerraria.Tests;

public class GameBindingsTests
{
    private static Dictionary<string, List<string>> Defaults() => new()
    {
        ["MouseLeft"] = new() { "Mouse1" },
        ["MouseRight"] = new() { "Mouse2" },
        ["SmartSelect"] = new() { "LeftShift" },
        ["SmartCursor"] = new() { "LeftControl" },
        ["QuickMana"] = new() { "J" },
        ["QuickHeal"] = new() { "H" },
    };

    [Fact]
    public void TheGamesDefaultsAreMovedOffShiftControlAndJ()
    {
        var bindings = Defaults();
        var changes = GameBindings.Fix(bindings);
        Assert.Equal(new[] { "F" }, bindings["SmartSelect"]);
        Assert.Equal(new[] { "V" }, bindings["SmartCursor"]);
        Assert.Equal(new[] { "G" }, bindings["QuickMana"]);
        Assert.Equal(3, changes.Count);
    }

    [Fact]
    public void ASecondRunChangesNothing()
    {
        var bindings = Defaults();
        GameBindings.Fix(bindings);
        Assert.Empty(GameBindings.Fix(bindings));
    }

    [Fact]
    public void WhereTheNewKeyIsTakenTheBindingIsOnlyTakenOffTheOldOne()
    {
        var bindings = Defaults();
        bindings["QuickHeal"] = new() { "F" };
        GameBindings.Fix(bindings);
        Assert.Empty(bindings["SmartSelect"]);
        Assert.Equal(new[] { "F" }, bindings["QuickHeal"]);
    }

    [Fact]
    public void TheOldModsLettersComeOffTheMouseButtons()
    {
        var bindings = Defaults();
        bindings["MouseLeft"] = new() { "I" };
        bindings["MouseRight"] = new() { "P" };
        GameBindings.Fix(bindings);
        Assert.Equal(new[] { "Mouse1" }, bindings["MouseLeft"]);
        Assert.Equal(new[] { "Mouse2" }, bindings["MouseRight"]);
    }

    [Fact]
    public void APlayersOwnChoiceOfOtherKeysIsLeftAlone()
    {
        var bindings = Defaults();
        bindings["SmartSelect"] = new() { "Q" };
        bindings["QuickMana"] = new() { "Z" };
        GameBindings.Fix(bindings);
        Assert.Equal(new[] { "Q" }, bindings["SmartSelect"]);
        Assert.Equal(new[] { "Z" }, bindings["QuickMana"]);
    }
}
