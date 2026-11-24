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
    private enum Place { Unknown, Title, Busy, Password, World, Elsewhere }

    private const int TitleMenuMode = 0;
    /// <summary>The game's status-text screen: loading, saving.</summary>
    private const int StatusMenuMode = 10;
    /// <summary>The game's status screen while a connection is made.</summary>
    public const int ConnectingMenuMode = 14;
    /// <summary>The game's screen for a connection that ended: its
    /// reason is the status text.</summary>
    private const int NetErrorMenuMode = 15;
    /// <summary>The game's screen asking for a server's password.</summary>
    private const int PasswordMenuMode = 31;

    private readonly SruiHost _host;
    private readonly TitleScreen _title;
    private readonly PackScreen _pack;
    private Place _place = Place.Unknown;
    private bool _busy;
    private bool _connecting;

    public Shell(SruiHost host)
    {
        _host = host;
        _title = new TitleScreen(host.App, this);
        _pack = new PackScreen(host.App, new World.TerrariaPack(), () =>
        {
            BeginBusy("Saving.");
            WorldGen.SaveAndQuit();
        }, new World.TerrariaPowers(), new World.TerrariaCrafting(), ChatLog, new World.TerrariaChat());
        // The world's screens are stops beside the world: Tab goes
        // round to them and the game runs on. Enter is the message box.
        WorldCommands[KeyCombo.Plain(Key.Tab)] = () => OpenPack(focus: PackFocus.First);
        WorldCommands[KeyCombo.WithShift(Key.Tab)] = () => OpenPack(focus: PackFocus.Last);
        WorldCommands[KeyCombo.Plain(Key.Enter)] = () => OpenPack(focus: PackFocus.Chat);
        _pack.Closed += () =>
        {
            if (_paused)
            {
                _paused = false;
                Main.autoPause = _autoPauseWas;
            }
            // Put away only to be put back for what was said next:
            // the conversation goes on.
            if (_following)
                return;
            _context = "";
            World.TerrariaTalk.End();
            Main.playerInventory = false;
            if (World.TerrariaChest.IsOpen)
            {
                // As the game closes a chest: the lid shuts on the
                // next frame, and a server is told.
                Main.LocalPlayer.chest = -1;
                Recipe.FindRecipes();
            }
            // Mid-save the keys stay ours: the character must not
            // walk off while the world is being written.
            if (!_busy)
                _host.ReleaseKeyboard();
        };
        // Loading and saving take the keys nowhere.
        host.App.InputFilter = input =>
        {
            if (!_busy)
                return false;
            // A connection can wait forever on a wrong address:
            // Escape gives up on it.
            if (_connecting && input.Kind == InputKind.Dismiss)
            {
                Disconnect();
                _host.App.Announce("Cancelled.");
            }
            return true;
        };
        host.KeyReleasedWhileGameOwns = key => WorldKeyReleased?.Invoke(key);
        host.KeyWhileGameOwns = combo =>
        {
            if (_place != Place.World)
                return false;
            if (combo == KeyCombo.Plain(Key.Escape))
            {
                OpenPack(paused: true);
                return true;
            }
            if (!WorldCommands.TryGetValue(combo, out var command))
                return false;
            command();
            return true;
        };
    }

    /// <summary>The game's messages, for the chat list.</summary>
    public ChatLog ChatLog { get; } = new();

    private bool _paused;
    private bool _autoPauseWas;

    /// <summary>The inventory and the rest. From Tab or Enter the game
    /// runs on underneath. From Escape, in single player, it is paused
    /// for as long as the screen is up, whatever the game's own
    /// autopause setting says: the game is told its inventory is open
    /// and to pause for that. A server cannot be paused.</summary>
    private void OpenPack(string? notice = null,
        PackFocus focus = PackFocus.First, bool paused = false)
    {
        if (_pack.IsOpen)
            return;
        _host.TakeKeyboard();
        if (paused && Main.netMode == Terraria.ID.NetmodeID.SinglePlayer)
        {
            _paused = true;
            _autoPauseWas = Main.autoPause;
            Main.autoPause = true;
            Main.playerInventory = true;
            _host.App.Announce("Paused.");
        }
        Recipe.FindRecipes();
        _pack.Open(null, notice, focus);
    }

    /// <summary>The pack screen, opened on the reason a chest did not
    /// open.</summary>
    public void OpenPackOnNotice(string notice) => OpenPack(notice: notice);

    private readonly World.TerrariaTalk _talk = new();
    /// <summary>What in the world the pack screen was opened for and
    /// is showing: a chest, someone's words, a shop. Empty when the
    /// player opened it themselves.</summary>
    private string _context = "";
    private bool _following;

    /// <summary>What the player has in front of them in the world
    /// brings the pack screen with it - a chest they opened, someone
    /// they spoke to, a shop - and when that changes, because a reply
    /// was chosen or the game closed the chest, the screen follows.</summary>
    private void FollowWorld()
    {
        if (_busy)
            return;
        World.TerrariaChest? chest = null;
        World.TerrariaShop? shop = null;
        Talk? talk = null;
        var buttons = new List<int>();
        var context = "";
        if (World.TerrariaShop.IsOpen)
        {
            shop = new World.TerrariaShop();
            context = $"shop {Main.LocalPlayer.talkNPC}";
        }
        else if (World.TerrariaTalk.Talking)
        {
            talk = _talk.Current(out buttons);
            // Not drawn yet: its buttons are not known. Next frame.
            if (talk == null)
                return;
            context = $"talk {talk.Speaker} {talk.Text} {string.Join("|", talk.Options)}";
        }
        else if (World.TerrariaChest.IsOpen)
        {
            chest = new World.TerrariaChest();
            context = $"chest {Main.LocalPlayer.chest}";
        }
        if (context == _context)
            return;
        if (_pack.IsOpen)
        {
            // Opened by the player, with nothing in front of them:
            // theirs to close.
            if (_context == "")
                return;
            _following = true;
            _pack.Close();
            _following = false;
            if (context == "")
            {
                _context = "";
                Main.playerInventory = false;
                if (!_busy)
                    _host.ReleaseKeyboard();
                return;
            }
        }
        _context = context;
        _host.TakeKeyboard();
        Recipe.FindRecipes();
        _pack.Open(chest, talk: talk, choose: index => _talk.Press(buttons[index]), shop: shop);
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

    /// <summary>Joins a server with this character.</summary>
    public void Join(Terraria.IO.PlayerFileData player, string host, int port) => _title.Join(player, host, port);

    /// <summary>Gives up on a connection, as the game's own Cancel does.</summary>
    public static void Disconnect()
    {
        Netplay.Disconnect = true;
        Netplay.ServerPassword = "";
        Main.menuMode = TitleMenuMode;
    }

    private string? _renameWorldTo;

    /// <summary>True from the moment the mod starts a load or a save
    /// until the game arrives where that leads.</summary>
    public bool Busy => _busy;

    /// <summary>The mod has asked the game for something slow. Says
    /// so once, and stops taking input until the game arrives.</summary>
    public void BeginBusy(string announcement, bool connecting = false)
    {
        _busy = true;
        _connecting = connecting;
        _title.Hide();
        _host.App.Announce(announcement);
        _host.TakeKeyboard();
    }

    /// <summary>Once a frame, before the host's.</summary>
    public void Update()
    {
        var place = !Main.gameMenu ? Place.World
            : Main.menuMode == TitleMenuMode ? Place.Title
            : _busy && (Main.menuMode == StatusMenuMode || Main.menuMode == ConnectingMenuMode) ? Place.Busy
            : Main.menuMode == PasswordMenuMode ? Place.Password
            : Place.Elsewhere;
        // A connection that was refused, lost or ended by the server:
        // say why, and go back to the title rather than leave the
        // game's own notice up. The game shows the reason on its
        // connecting screen, with the connection already closed.
        var ended = Main.gameMenu && (Main.menuMode == NetErrorMenuMode
            || (Main.menuMode == ConnectingMenuMode && Netplay.Disconnect && Main.netMode == Terraria.ID.NetmodeID.SinglePlayer));
        // Not while this mod's own Save and exit is on its way out.
        if (ended && (!_busy || _connecting))
        {
            _busy = _connecting = false;
            _host.App.Announce(string.IsNullOrWhiteSpace(Main.statusText) ? "Disconnected." : Main.statusText);
            Main.menuMode = TitleMenuMode;
            return;
        }
        if (place == _place)
        {
            if (place == Place.World)
            {
                FollowWorld();
                WorldFrame?.Invoke();
            }
            return;
        }
        var from = _place;
        _place = place;
        if (from == Place.World)
            LeftWorld?.Invoke();
        switch (place)
        {
            case Place.Password:
                _busy = _connecting = false;
                _host.TakeKeyboard();
                _title.AskPassword();
                break;
            case Place.Title:
                _busy = _connecting = false;
                _pack.Close();
                _host.TakeKeyboard();
                _title.Show();
                break;
            case Place.World:
                _busy = _connecting = false;
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
