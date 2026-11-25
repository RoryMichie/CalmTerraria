using CalmTerraria.Settings;
using Terraria;

namespace CalmTerraria.Host;

/// <summary>The settings, done to the mod and to the game. The mod's
/// own are each a file that is there or is not, beside its other
/// files, so that they are the same on the next launch; the game's are
/// the game's, kept in its own settings file.</summary>
internal sealed class ModSettings : ISettings
{
    private static string File(string name) => Path.Combine(Main.SavePath, "CalmTerraria", name);

    private static bool? _classic, _noTick;
    /// <summary>Whether the file that silences shortcut keys is there.</summary>
    private static bool? _shortcuts;

    /// <summary>The classic key layout is in use.</summary>
    public static bool Classic => _classic ??= System.IO.File.Exists(File("classic-keys.txt"));

    /// <summary>Lists tick as they are moved through.</summary>
    public static bool Ticks => !(_noTick ??= System.IO.File.Exists(File("no-menu-tick.txt")));

    private static void Mark(string name, bool there, string says)
    {
        if (there)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File(name))!);
            System.IO.File.WriteAllText(File(name), says);
        }
        else
            System.IO.File.Delete(File(name));
    }

    /// <summary>Shortcut keys are said with the stops that have them.</summary>
    public static bool Shortcuts => !(_shortcuts ??= System.IO.File.Exists(File("no-shortcut-keys.txt")));

    /// <summary>Called when what is spoken has changed.</summary>
    public Action? SpeechChanged { get; set; }

    public bool SayShortcuts
    {
        get => Shortcuts;
        set
        {
            _shortcuts = !value;
            Mark("no-shortcut-keys.txt", !value, "Shortcut keys are not said.");
            SpeechChanged?.Invoke();
        }
    }

    /// <summary>Called when the key layout has changed, with whether
    /// it is now the classic one: the game's bindings follow.</summary>
    public Action<bool>? KeysChanged { get; set; }

    public bool ClassicKeys
    {
        get => Classic;
        set
        {
            if (value == Classic)
                return;
            _classic = value;
            Mark("classic-keys.txt", value, "The classic key layout is in use.");
            KeysChanged?.Invoke(value);
        }
    }

    public bool MenuTick
    {
        get => Ticks;
        set
        {
            _noTick = !value;
            Mark("no-menu-tick.txt", !value, "Lists do not tick.");
        }
    }

    private static int Percent(float volume) => (int)MathF.Round(volume * 100f);

    public int SoundVolume
    {
        get => Percent(Main.soundVolume);
        set => Main.soundVolume = value / 100f;
    }

    public int MusicVolume
    {
        get => Percent(Main.musicVolume);
        set => Main.musicVolume = value / 100f;
    }

    public int AmbientVolume
    {
        get => Percent(Main.ambientVolume);
        set => Main.ambientVolume = value / 100f;
    }

    public bool Autosave
    {
        get => Main.autoSave;
        set => Main.autoSave = value;
    }

    public void Save() => Main.SaveSettings();
}
