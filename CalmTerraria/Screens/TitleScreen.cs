using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Srui;
using Terraria;
using Terraria.IO;
using Terraria.ModLoader;

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
            _menu = new ListBox(_app, "Terraria",
                new[] { "Single player", "Join by address", "New character", "New world", "Exit" }, activateItems: true);
            _menu.Activated += () =>
            {
                switch (_menu.SelectedIndex)
                {
                    case 0: ChooseCharacter(ChooseWorld); break;
                    case 1: ChooseCharacter(AskAddress); break;
                    case 2: NewCharacter(); break;
                    case 3: NewWorld(); break;
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

    // Creating a character or a world is done by the game's own
    // creation screens, built but never shown: their choices are set
    // and their own Create is called, so what is made is exactly what
    // the game makes, starting inventory and all. None of that is
    // public, so it is reached by name.
    private const System.Reflection.BindingFlags Hidden =
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;

    private static void Call(object screen, string method, params object?[] arguments) =>
        (screen.GetType().GetMethod(method, Hidden)
            ?? throw new MissingMethodException(screen.GetType().Name, method)).Invoke(screen, arguments);

    private static System.Reflection.FieldInfo Field(object screen, string name) =>
        screen.GetType().GetField(name, Hidden) ?? throw new MissingFieldException(screen.GetType().Name, name);

    private static readonly string[] CharacterDifficulties =
    {
        "Classic, coins are dropped on death",
        "Mediumcore, everything carried is dropped on death",
        "Hardcore, death is the end of the character",
        "Journey, with its own powers, for journey worlds only",
    };

    private void NewCharacter()
    {
        var dialog = _characters = _app.OpenDialog();
        var name = new EditBox(dialog, "Name");
        var difficulty = new ListBox(dialog, "Difficulty", CharacterDifficulties);
        var create = new Button(dialog, "Create");
        _app.SetPrimary(create);
        create.Activated += () =>
        {
            var text = name.Text.Trim();
            if (text.Length == 0 || text.Length > 20)
            {
                _app.Announce("A name of 1 to 20 characters is needed.");
                return;
            }
            try
            {
                var player = Main.PendingPlayer = new Player();
                var screen = new Terraria.GameContent.UI.States.UICharacterCreation(player);
                // Looks are the game's own roll of the dice.
                Call(screen, "Click_RandomizePlayer", null, null);
                player.name = text;
                // The list is in the game's own order: 0 to 3.
                player.difficulty = (byte)difficulty.SelectedIndex;
                Call(screen, "FinishCreatingCharacter");
                // The game goes to its own character list from there.
                Main.menuMode = 0;
            }
            catch (Exception e)
            {
                ModContent.GetInstance<CalmTerrariaMod>().Logger.Error("Creating a character failed.", e);
                _app.Announce("The character could not be created; the reason is in the log.");
                return;
            }
            dialog.Close();
            _app.Announce($"{text} created.");
        };
        dialog.AnnounceOpened();
    }

    private static readonly string[] WorldSizes = { "Small", "Medium", "Large" };
    private static readonly string[] WorldDifficulties =
    {
        "Classic", "Expert, harder with better rewards", "Master, hardest", "Journey, for journey characters only",
    };
    private static readonly string[] WorldDifficultyIds = { "Normal", "Expert", "Master", "Creative" };
    private static readonly string[] WorldEvils = { "Random", "Corruption", "Crimson" };

    private void NewWorld()
    {
        object screen;
        string suggested;
        try
        {
            // The game's screen looks at the chosen character to
            // suggest a difficulty, and here none has been chosen.
            if (Main.ActivePlayerFileData?.Player == null)
                new PlayerFileData { Player = new Player() }.SetAsActive();
            screen = new Terraria.GameContent.UI.States.UIWorldCreation();
            Call(screen, "AssignRandomWorldName");
            suggested = Field(screen, "_optionwWorldName").GetValue(screen) as string ?? "";
        }
        catch (Exception e)
        {
            ModContent.GetInstance<CalmTerrariaMod>().Logger.Error("Opening world creation failed.", e);
            _app.Announce("Worlds cannot be created here; the reason is in the log.");
            return;
        }
        var dialog = _characters = _app.OpenDialog();
        var name = new EditBox(dialog, "Name", suggested) { SelectAllOnFocus = true };
        var size = new ListBox(dialog, "Size", WorldSizes);
        var difficulty = new ListBox(dialog, "Difficulty", WorldDifficulties);
        var evil = new ListBox(dialog, "Evil", WorldEvils);
        var seed = new EditBox(dialog, "Seed, blank for random");
        var create = new Button(dialog, "Create");
        _app.SetPrimary(create);
        create.Activated += () =>
        {
            var text = name.Text.Trim();
            if (text.Length == 0 || text.Length > 27)
            {
                _app.Announce("A name of 1 to 27 characters is needed.");
                return;
            }
            try
            {
                void Choose(string field, string value)
                {
                    var info = Field(screen, field);
                    info.SetValue(screen, Enum.Parse(info.FieldType, value));
                }
                Field(screen, "_optionwWorldName").SetValue(screen, text);
                Field(screen, "_optionSeed").SetValue(screen, seed.Text.Trim());
                Choose("_optionSize", WorldSizes[size.SelectedIndex]);
                Choose("_optionDifficulty", WorldDifficultyIds[difficulty.SelectedIndex]);
                Choose("_optionEvil", WorldEvils[evil.SelectedIndex]);
                _shell.BeginGenerating(text);
                Call(screen, "FinishCreatingWorld");
            }
            catch (Exception e)
            {
                ModContent.GetInstance<CalmTerrariaMod>().Logger.Error("Creating a world failed.", e);
                _shell.GeneratingFailed();
            }
        };
        dialog.AnnounceOpened();
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
            _app.Announce("No characters. Make one with New character.");
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
            _app.Announce("No worlds. Make one with New world.");
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
