using CalmTerraria.Keys;
using CalmTerraria.Settings;
using Srui;
using Srui.Testing;

namespace CalmTerraria.Tests;

internal sealed class FakeSettings : ISettings
{
    public bool ClassicKeys { get; set; }
    public bool MenuTick { get; set; } = true;
    public int SoundVolume { get; set; } = 50;
    public int MusicVolume { get; set; } = 50;
    public int AmbientVolume { get; set; } = 50;
    public bool Autosave { get; set; } = true;
    public int Saves { get; private set; }
    public void Save() => Saves++;
}

public class SettingsScreenTests
{
    private static (TestApp Ui, FakeSettings Settings) Open()
    {
        var ui = new TestApp();
        var settings = new FakeSettings();
        new SettingsScreen(ui.App, settings).Open();
        return (ui, settings);
    }

    [Fact]
    public void OpensOnTheModTabAndClassicKeysSaysWhatChanged()
    {
        var (ui, settings) = Open();
        ui.Expect("Settings tab control Mod");
        ui.Press("tab");
        ui.Expect("Classic keys check box not checked");
        ui.Press("space");
        Assert.True(settings.ClassicKeys);
        Assert.Contains(ui.Spoken(), said => said.Contains("Cursor on O, K, L and semicolon."));
        ui.Press("tab");
        ui.Expect("Menu tick check box checked");
        ui.Press("space");
        Assert.False(settings.MenuTick);
    }

    [Fact]
    public void TheGameTabHasTheVolumesAndAutosave()
    {
        var (ui, settings) = Open();
        ui.Drain();
        ui.Press("right");
        ui.Expect("Game");
        ui.Press("tab");
        ui.Expect("Sound volume slider 50%");
        ui.Press("right");
        Assert.Equal(55, settings.SoundVolume);
        ui.Press("tab");
        ui.Press("tab");
        ui.Press("tab");
        ui.Expect("Autosave check box checked");
        ui.Press("space");
        Assert.False(settings.Autosave);
    }

    [Fact]
    public void ClosingKeepsWhatWasSet()
    {
        var (ui, settings) = Open();
        ui.Press("escape");
        Assert.Equal(1, settings.Saves);
    }
}

public class KeymapTests
{
    private static KeyCombo Plain(char c) => KeyCombo.Plain(Key.Char(c));

    [Fact]
    public void TheClassicCursorKeysAreTheModsDirections()
    {
        Assert.Equal(Plain('i'), Keymap.FromClassic(Plain('o')));
        Assert.Equal(Plain('j'), Keymap.FromClassic(Plain('k')));
        Assert.Equal(Plain('m'), Keymap.FromClassic(Plain('l')));
        Assert.Equal(Plain('l'), Keymap.FromClassic(Plain(';')));
    }

    [Fact]
    public void ShiftAndControlComeThrough()
    {
        Assert.Equal(KeyCombo.WithShift(Key.Char('i')), Keymap.FromClassic(KeyCombo.WithShift(Key.Char('o'))));
        Assert.Equal(KeyCombo.WithCtrl(Key.Char('k')), Keymap.FromClassic(KeyCombo.WithCtrl(Key.Char(','))));
    }

    [Fact]
    public void IAndPAreTheMouseButtons()
    {
        Assert.Equal(Plain('['), Keymap.FromClassic(Plain('i')));
        Assert.Equal(Plain(']'), Keymap.FromClassic(Plain('p')));
        Assert.Equal(Key.Char('['), Keymap.FromClassic(Key.Char('i')));
    }

    [Fact]
    public void TheScannerIsOnTheBracketsAndThePageKeys()
    {
        // Brackets: category.
        Assert.Equal(KeyCombo.WithCtrl(Key.PageUp), Keymap.FromClassic(Plain('[')));
        Assert.Equal(KeyCombo.WithCtrl(Key.PageDown), Keymap.FromClassic(Plain(']')));
        // Page keys alone: the next thing. With Shift: the next kind.
        Assert.Equal(KeyCombo.WithShift(Key.PageDown), Keymap.FromClassic(KeyCombo.Plain(Key.PageDown)));
        Assert.Equal(KeyCombo.Plain(Key.PageUp), Keymap.FromClassic(KeyCombo.WithShift(Key.PageUp)));
        Assert.Equal(KeyCombo.WithCtrl(Key.PageDown), Keymap.FromClassic(KeyCombo.WithCtrl(Key.PageDown)));
    }

    [Fact]
    public void HomeGoesThereAndControlHomeSendsTheCursor()
    {
        Assert.Equal(KeyCombo.WithCtrl(Key.Home), Keymap.FromClassic(KeyCombo.Plain(Key.Home)));
        Assert.Equal(KeyCombo.Plain(Key.Home), Keymap.FromClassic(KeyCombo.WithCtrl(Key.Home)));
    }

    [Fact]
    public void TheRestOfThePadGoesToTheGame()
    {
        foreach (var c in "ujnm")
            Assert.Null(Keymap.FromClassic(Plain(c)));
    }

    [Fact]
    public void EverythingElseIsAsItWas()
    {
        Assert.Equal(Plain('x'), Keymap.FromClassic(Plain('x')));
        Assert.Equal(KeyCombo.Plain(Key.Tab), Keymap.FromClassic(KeyCombo.Plain(Key.Tab)));
        Assert.Equal(KeyCombo.Plain(Key.End), Keymap.FromClassic(KeyCombo.Plain(Key.End)));
    }
}

public class ClassicGameBindingsTests
{
    private static Dictionary<string, List<string>> Bindings(string mana) => new()
    {
        ["MouseLeft"] = new() { "Mouse1" },
        ["MouseRight"] = new() { "Mouse2" },
        ["SmartSelect"] = new() { "LeftShift" },
        ["QuickMana"] = new() { mana },
    };

    [Fact]
    public void WithClassicKeysQuickManaStaysOnJ()
    {
        var bindings = Bindings("J");
        GameBindings.Fix(bindings, classic: true);
        Assert.Equal(new[] { "J" }, bindings["QuickMana"]);
        Assert.Equal(new[] { "F" }, bindings["SmartSelect"]);
    }

    [Fact]
    public void ChoosingClassicPutsQuickManaBackOnJ()
    {
        var bindings = Bindings("G");
        Assert.Single(GameBindings.ToClassic(bindings));
        Assert.Equal(new[] { "J" }, bindings["QuickMana"]);
        Assert.Empty(GameBindings.ToClassic(bindings));
    }

    [Fact]
    public void NotWhereSomethingElseHasJOrThePlayerChoseAnotherKey()
    {
        var taken = Bindings("G");
        taken["QuickBuff"] = new() { "J" };
        Assert.Empty(GameBindings.ToClassic(taken));
        var own = Bindings("Z");
        Assert.Empty(GameBindings.ToClassic(own));
        Assert.Equal(new[] { "Z" }, own["QuickMana"]);
    }
}
