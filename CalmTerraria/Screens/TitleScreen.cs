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
            _menu = new ListBox(_app, "Terraria", new[] { "Single player", "Exit" }, activateItems: true);
            _menu.Activated += () =>
            {
                if (_menu.SelectedIndex == 0)
                    ChooseCharacter();
                else
                    QuitGame(Main.instance);
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
        _characters?.Close(nested: true);
        _characters = null;
    }

    private void ChooseCharacter()
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
        list.Activated += () => ChooseWorld(players[list.SelectedIndex]);
        dialog.AnnounceOpened();
    }

    private void ChooseWorld(PlayerFileData player)
    {
        // What the game's own character list does on a single-player pick.
        Main.menuMultiplayer = false;
        Main.ServerSideCharacter = false;
        Main.myPlayer = 0;
        player.SetAsActive();
        Main.player[Main.myPlayer].position = Vector2.Zero;
        Main.LoadWorlds();

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
