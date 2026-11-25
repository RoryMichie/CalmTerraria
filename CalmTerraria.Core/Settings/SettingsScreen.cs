using Srui;

namespace CalmTerraria.Settings;

/// <summary>What can be set, as the settings screen needs it: the
/// mod's own choices, and the few of the game's that matter without a
/// picture. Volumes are whole numbers, 0 to 100.</summary>
public interface ISettings
{
    /// <summary>The classic key layout in a world, in place of the
    /// mod's own.</summary>
    bool ClassicKeys { get; set; }

    /// <summary>The game's menu tick on moving through a list.</summary>
    bool MenuTick { get; set; }

    int SoundVolume { get; set; }
    int MusicVolume { get; set; }
    int AmbientVolume { get; set; }

    /// <summary>The game saves the world and the character by itself
    /// every few minutes.</summary>
    bool Autosave { get; set; }

    /// <summary>Keep what was set. Called once, as the screen closes.</summary>
    void Save();
}

/// <summary>Settings, in two tabs: the mod's and the game's. Everything
/// takes effect as it is changed and is kept when the screen closes.</summary>
public sealed class SettingsScreen
{
    private readonly SruiApp _app;
    private readonly ISettings _settings;

    public SettingsScreen(SruiApp app, ISettings settings)
    {
        _app = app;
        _settings = settings;
    }

    public void Open()
    {
        var dialog = _app.OpenDialog();
        var tabs = new TabControl(dialog, "Settings");

        var mod = tabs.AddTab("Mod").Panel!;
        var classic = new CheckBox(mod, "Classic keys", _settings.ClassicKeys);
        classic.Toggled += on =>
        {
            _settings.ClassicKeys = on;
            _app.Announce(on
                ? "Cursor on O, K, L and semicolon. Comma reads it. I uses, P interacts. J is quick mana."
                : "Cursor on the pad around K. K reads it. Left bracket uses, right bracket interacts. G is quick mana.");
        };
        var tick = new CheckBox(mod, "Menu tick", _settings.MenuTick);
        tick.Toggled += on => _settings.MenuTick = on;

        var game = tabs.AddTab("Game").Panel!;
        Volume(game, "Sound volume", _settings.SoundVolume, value => _settings.SoundVolume = value);
        Volume(game, "Music volume", _settings.MusicVolume, value => _settings.MusicVolume = value);
        Volume(game, "Ambient volume", _settings.AmbientVolume, value => _settings.AmbientVolume = value);
        var autosave = new CheckBox(game, "Autosave", _settings.Autosave);
        autosave.Toggled += on => _settings.Autosave = on;

        dialog.Closed += _settings.Save;
        dialog.AnnounceOpened();
    }

    private static void Volume(Widget panel, string name, int value, Action<int> set)
    {
        var slider = new Slider(panel, name, value, 0, 100, smallStep: 5, largeStep: 20, unit: "%");
        slider.Changed += () => set((int)Math.Round(slider.Number));
    }
}
