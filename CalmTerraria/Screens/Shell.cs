using CalmTerraria.Host;
using CalmTerraria.Pack;
using Srui;
using Terraria;

namespace CalmTerraria.Screens;

/// <summary>Decides who has the keyboard, by where the game is. On the
/// title screen and in the screens opened from it, SRUI has it and the
/// game's own menu is never used. In a world the game has it, and
/// Escape opens the pack screen. Anywhere this mod has no screen for,
/// the keyboard is the game's, so a mouse or a sighted helper can
/// still get through.
///
/// The game's menus are not driven: the mod calls what their buttons
/// call. The game stays on its title screen underneath until a world
/// starts loading.</summary>
internal sealed class Shell
{
    private enum Place { Unknown, Title, Busy, World, Elsewhere }

    private const int TitleMenuMode = 0;
    /// <summary>The game's status-text screen: loading, saving.</summary>
    private const int StatusMenuMode = 10;

    private readonly SruiHost _host;
    private readonly TitleScreen _title;
    private readonly PackScreen _pack;
    private Place _place = Place.Unknown;
    private bool _busy;

    public Shell(SruiHost host)
    {
        _host = host;
        _title = new TitleScreen(host.App, this);
        _pack = new PackScreen(host.App, new World.TerrariaPack(), () =>
        {
            BeginBusy("Saving.");
            WorldGen.SaveAndQuit();
        });
        _pack.Closed += () =>
        {
            Main.playerInventory = false;
            // Mid-save the keys stay ours: the character must not
            // walk off while the world is being written.
            if (!_busy)
                _host.ReleaseKeyboard();
        };
        // Loading and saving take the keys nowhere.
        host.App.InputFilter = _ => _busy;
        host.KeyReleasedWhileGameOwns = key => WorldKeyReleased?.Invoke(key);
        host.KeyWhileGameOwns = combo =>
        {
            if (_place != Place.World)
                return false;
            if (combo == KeyCombo.Plain(Key.Escape))
            {
                OpenPack();
                return true;
            }
            if (!WorldCommands.TryGetValue(combo, out var command))
                return false;
            command();
            return true;
        };
    }

    /// <summary>Escape, in a world: the inventory, what is worn, and
    /// the way out. The game is told its own inventory is open, so a
    /// sighted onlooker sees it and the game's autopause setting
    /// applies as it would for anyone.</summary>
    private void OpenPack()
    {
        if (_pack.IsOpen)
            return;
        _host.TakeKeyboard();
        Main.playerInventory = true;
        _pack.Open();
    }

    /// <summary>A key came up while the player is in a world.</summary>
    public event Action<Key>? WorldKeyReleased;

    /// <summary>The mod's keys in a world, where the game has the
    /// keyboard. A key bound here is taken from the game.</summary>
    public Dictionary<KeyCombo, Action> WorldCommands { get; } = new();

    public bool InWorld => _place == Place.World;

    /// <summary>Raised when a world has loaded and the player is in it.</summary>
    public event Action? EnteredWorld;

    /// <summary>Raised every frame the player is in a world.</summary>
    public event Action? WorldFrame;

    /// <summary>Raised when the player has left a world.</summary>
    public event Action? LeftWorld;

    /// <summary>Starts single player with this character in this world,
    /// as the title screen's own path does.</summary>
    public void Play(Terraria.IO.PlayerFileData player, Terraria.IO.WorldFileData world, string? renameWorldTo = null)
    {
        _renameWorldTo = renameWorldTo;
        _title.Start(player, world);
    }

    public void Quit() => _title.Quit();

    private string? _renameWorldTo;

    /// <summary>True from the moment the mod starts a load or a save
    /// until the game arrives where that leads.</summary>
    public bool Busy => _busy;

    /// <summary>The mod has asked the game for something slow. Says
    /// so once, and stops taking input until the game arrives.</summary>
    public void BeginBusy(string announcement)
    {
        _busy = true;
        _title.Hide();
        _host.App.Announce(announcement);
        _host.TakeKeyboard();
    }

    /// <summary>Once a frame, before the host's.</summary>
    public void Update()
    {
        var place = !Main.gameMenu ? Place.World
            : Main.menuMode == TitleMenuMode ? Place.Title
            : _busy && Main.menuMode == StatusMenuMode ? Place.Busy
            : Place.Elsewhere;
        if (place == _place)
        {
            if (place == Place.World)
                WorldFrame?.Invoke();
            return;
        }
        var from = _place;
        _place = place;
        if (from == Place.World)
            LeftWorld?.Invoke();
        switch (place)
        {
            case Place.Title:
                _busy = false;
                _pack.Close();
                _host.TakeKeyboard();
                _title.Show();
                break;
            case Place.World:
                _busy = false;
                _title.Hide();
                _host.ReleaseKeyboard();
                if (_renameWorldTo != null)
                    Main.worldName = _renameWorldTo;
                _renameWorldTo = null;
                _host.App.Announce("Ready.");
                EnteredWorld?.Invoke();
                break;
            case Place.Elsewhere:
                // Mods are still loading at startup, which is nothing
                // to report. Later it is a screen this mod cannot
                // read, and the reason is usually in the status text.
                _host.ReleaseKeyboard();
                if (from == Place.Unknown)
                    break;
                _busy = false;
                _title.Hide();
                _pack.Close();
                _host.App.Announce(string.IsNullOrWhiteSpace(Main.statusText)
                    ? "A screen CalmTerraria cannot read yet."
                    : Main.statusText);
                break;
        }
    }
}
