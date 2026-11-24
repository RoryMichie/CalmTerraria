using Srui;

namespace CalmTerraria.Powers;

/// <summary>What the game can be made to do outside its rules, as the
/// powers screen needs it.</summary>
public interface IPowers
{
    /// <summary>The scanner and the cursor know the whole world, not
    /// only what the character has explored.</summary>
    bool Omniscience { get; set; }

    /// <summary>The character takes no damage.</summary>
    bool GodMode { get; set; }

    /// <summary>Every item's name, alphabetical.</summary>
    IReadOnlyList<string> Items();

    /// <summary>That many of the item, into the inventory.</summary>
    void Give(string item, int count);

    /// <summary>How many of the item make a full stack.</summary>
    int FullStack(string item);

    /// <summary>Every creature's name, alphabetical.</summary>
    IReadOnlyList<string> Creatures();

    /// <summary>One of the creature, a little way off on the side the
    /// character faces. False where the game does not allow it.</summary>
    bool Spawn(string creature);

    /// <summary>The other players in the world, by name.</summary>
    IReadOnlyList<string> Players();

    /// <summary>Go to where the player is. False where it could not
    /// be done.</summary>
    bool TeleportTo(string player);

    /// <summary>The player's waypoints in this world, by name.</summary>
    IReadOnlyList<string> Waypoints();

    bool TeleportToWaypoint(string waypoint);

    /// <summary>Dawn or dusk, now. False where the game does not allow it.</summary>
    bool SetTime(bool day);
}

/// <summary>The powers: what a journey-mode character has and more,
/// for any character. Two switches and six actions; the two long
/// lists are found in by typing the start of a name.</summary>
public sealed class PowersScreen
{
    private const string Refused = "Not on a server.";

    private readonly SruiApp _app;
    private readonly IPowers _powers;

    public PowersScreen(SruiApp app, IPowers powers)
    {
        _app = app;
        _powers = powers;
    }

    public void Open()
    {
        var dialog = _app.OpenDialog();
        var omniscience = new CheckBox(dialog, "Omniscience", _powers.Omniscience);
        omniscience.Toggled += on => _powers.Omniscience = on;
        var god = new CheckBox(dialog, "God mode", _powers.GodMode);
        god.Toggled += on => _powers.GodMode = on;
        new Button(dialog, "Give item").Activated += () =>
            Pick("Items", _powers.Items(), item =>
            {
                var full = _powers.FullStack(item);
                CalmTerraria.Pack.Quantity.Ask(_app, full, full, count =>
                {
                    _powers.Give(item, count);
                    _app.Announce("Given.");
                });
                return null;
            });
        new Button(dialog, "Spawn creature").Activated += () =>
            Pick("Creatures", _powers.Creatures(), creature => _powers.Spawn(creature) ? "Spawned." : Refused);
        new Button(dialog, "Teleport to player").Activated += () =>
        {
            var players = _powers.Players();
            if (players.Count == 0)
                _app.Announce("No other players.");
            else
                Pick("Players", players, player => _powers.TeleportTo(player) ? "Teleported." : "Couldn't teleport.");
        };
        new Button(dialog, "Teleport to waypoint").Activated += () =>
        {
            var waypoints = _powers.Waypoints();
            if (waypoints.Count == 0)
                _app.Announce("No waypoints.");
            else
                Pick("Waypoints", waypoints, waypoint => _powers.TeleportToWaypoint(waypoint) ? "Teleported." : "Couldn't teleport.");
        };
        new Button(dialog, "Make it day").Activated += () => _app.Announce(_powers.SetTime(day: true) ? "Day." : Refused);
        new Button(dialog, "Make it night").Activated += () => _app.Announce(_powers.SetTime(day: false) ? "Night." : Refused);
        dialog.AnnounceOpened();
    }

    /// <summary>A long list to choose from, again and again: Enter
    /// acts and the list stays, Escape goes back.</summary>
    private void Pick(string title, IReadOnlyList<string> names, Func<string, string?> act)
    {
        var dialog = _app.OpenDialog();
        var list = new ListBox(dialog, title, names, activateItems: true);
        list.Activated += () =>
        {
            // Null when the action speaks for itself, later.
            if (names.Count > 0 && act(names[list.SelectedIndex]) is { } said)
                _app.Announce(said);
        };
        dialog.AnnounceOpened();
    }
}
