using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Srui;
using Terraria;
using Terraria.IO;

namespace CalmTerraria.Screens;

/// <summary>The title screen, and the way from it into a world: pick a
/// character, pick a world, play. Each step is a dialog over the last,
/// so Escape goes back one.</summary>
internal sealed class TitleScreen
{
    private const int JourneyDifficulty = 3;

    private readonly SruiApp _app;
    private readonly Shell _shell;
    private ListBox? _menu;
    private Dialog? _characters;

    public TitleScreen(SruiApp app, Shell shell)
    {
        _app = app;
        _shell = shell;
    }

    public void Show()
    {
        if (_menu == null)
        {
            _menu = new ListBox(_app, "Terraria", new[] { "Single player", "Join by address", "Exit" }, activateItems: true);
            _menu.Activated += () =>
            {
                switch (_menu.SelectedIndex)
                {
                    case 0: ChooseCharacter(ChooseWorld); break;
                    case 1: ChooseCharacter(AskAddress); break;
                    default: QuitGame(Main.instance); break;
                }
            };
        }
        _menu.Hidden = false;
        _app.EnsureFocus();
        _app.ReannounceWithContext();
    }

    /// <summary>Out of the tab ring and out of every reading while the
    /// game is somewhere else: a dialog closing in a world must not
    /// restore focus to the title menu and read it.</summary>
    public void Hide()
    {
        // Hidden first, so the dialogs close over a ground with
        // nothing to restore to.
        if (_menu != null)
            _menu.Hidden = true;
        _password?.Close(nested: true);
        _password = null;
        _characters?.Close(nested: true);
        _characters = null;
    }

    private const int DefaultPort = 7777;
    private string? _address;
    private Dialog? _password;

    /// <summary>Where to join: an address and a port, starting from
    /// the last server joined. "host:port" in the address is taken
    /// apart.</summary>
    private void AskAddress(PlayerFileData player)
    {
        var recentPort = Main.recentPort is { Length: > 0 } ports && ports[0] > 0 ? ports[0] : DefaultPort;
        var recentAddress = Main.recentIP is { Length: > 0 } addresses ? addresses[0] : null;
        var dialog = _app.OpenDialog();
        var address = new EditBox(dialog, "Address", _address ?? recentAddress ?? "") { SelectAllOnFocus = true };
        var port = new EditBox(dialog, "Port", recentPort.ToString()) { SelectAllOnFocus = true };
        var join = new Button(dialog, "Join");
        _app.SetPrimary(join);
        join.Activated += () =>
        {
            var host = address.Text.Trim();
            var portText = port.Text.Trim();
            var colon = host.LastIndexOf(':');
            // One colon is host:port; more is an IPv6 address.
            if (colon > 0 && host.IndexOf(':') == colon)
            {
                portText = host[(colon + 1)..];
                host = host[..colon];
            }
            if (host.Length == 0)
            {
                _app.Announce("An address is needed.");
                return;
            }
            if (!int.TryParse(portText, out var number) || number < 1 || number > 65535)
            {
                _app.Announce("The port must be a number from 1 to 65535.");
                return;
            }
            _address = host;
            Join(player, host, number);
        };
        dialog.AnnounceOpened();
    }

    /// <summary>What the game's own menus do between picking a
    /// character for multiplayer and the connection starting.</summary>
    public void Join(PlayerFileData player, string host, int port)
    {
        Main.menuMultiplayer = true;
        Main.ServerSideCharacter = false;
        Main.myPlayer = 0;
        player.SetAsActive();
        Main.autoPass = false;
        // Left set by the last connection; it is how the end of this
        // one will be known.
        Netplay.Disconnect = false;
        Netplay.ListenPort = port;
        Main.getIP = host;
        Main.menuMode = Shell.ConnectingMenuMode;
        Main.statusText = Terraria.Localization.Language.GetTextValue("Net.ConnectingTo", host);
        _shell.BeginBusy($"Connecting to {host}.", connecting: true);
        Netplay.SetRemoteIPAsync(host, Main.StartClientGameplay);
    }

    /// <summary>The server wants its password.</summary>
    public void AskPassword()
    {
        if (_password != null)
            return;
        var dialog = _password = _app.OpenDialog();
        var sent = false;
        var password = new EditBox(dialog, "Server password") { Password = true };
        var join = new Button(dialog, "Join");
        _app.SetPrimary(join);
        join.Activated += () =>
        {
            sent = true;
            Netplay.ServerPassword = password.Text;
            NetMessage.SendData(Terraria.ID.MessageID.SendPassword);
            Main.menuMode = Shell.ConnectingMenuMode;
            _shell.BeginBusy("Connecting.", connecting: true);
        };
        dialog.Closed += () =>
        {
            _password = null;
            if (!sent)
                Shell.Disconnect();
        };
        dialog.AnnounceOpened();
    }

    private void ChooseCharacter(Action<PlayerFileData> then)
    {
        Main.LoadPlayers();
        var players = Main.PlayerList.ToList();
        if (players.Count == 0)
        {
            _app.Announce("No characters. Creating one is not built yet.");
            return;
        }
        var dialog = _characters = _app.OpenDialog();
        var names = players.Select(p =>
            p.Player.difficulty == JourneyDifficulty ? $"{p.Name}, journey" : p.Name).ToList();
        var list = new ListBox(dialog, "Characters", names, activateItems: true);
        list.Activated += () => then(players[list.SelectedIndex]);
        dialog.AnnounceOpened();
    }

    /// <summary>Straight into a world, without the lists.</summary>
    public void Start(PlayerFileData player, WorldFileData world)
    {
        Select(player);
        Play(player, world);
    }

    public void Quit() => QuitGame(Main.instance);

    /// <summary>What the game's own character list does on a
    /// single-player pick.</summary>
    private static void Select(PlayerFileData player)
    {
        Main.menuMultiplayer = false;
        Main.ServerSideCharacter = false;
        Main.myPlayer = 0;
        player.SetAsActive();
        Main.player[Main.myPlayer].position = Vector2.Zero;
        Main.LoadWorlds();
    }

    private void ChooseWorld(PlayerFileData player)
    {
        Select(player);

        var worlds = Main.WorldList.ToList();
        if (worlds.Count == 0)
        {
            _app.Announce("No worlds. Creating one is not built yet.");
            return;
        }
        var dialog = _app.OpenDialog();
        var names = worlds.Select(w => IsJourney(w) ? $"{w.Name}, journey" : w.Name).ToList();
        var list = new ListBox(dialog, "Worlds", names, activateItems: true);
        list.Activated += () => Play(player, worlds[list.SelectedIndex]);
        dialog.AnnounceOpened();
    }

    private void Play(PlayerFileData player, WorldFileData world)
    {
        if (!world.IsValid)
        {
            _app.Announce("This world's file is damaged; couldn't load.");
            return;
        }
        var journeyPlayer = player.Player.difficulty == JourneyDifficulty;
        if (journeyPlayer != IsJourney(world))
        {
            _app.Announce(journeyPlayer
                ? "A journey character needs a journey world."
                : "A journey world needs a journey character.");
            return;
        }
        // What the game's own world list does on Play.
        world.SetAsActive();
        Main.menuMode = 10;
        _shell.BeginBusy($"Loading {world.Name}.");
        WorldGen.playWorld();
    }

    private static bool IsJourney(WorldFileData world) =>
        Main.RegisteredGameModes.TryGetValue(world.GameMode, out var mode) && mode.IsJourneyMode;

    /// <summary>The title menu's own Exit, which is not public.</summary>
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "QuitGame")]
    private static extern void QuitGame(Main main);
}
