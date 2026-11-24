using System.Collections.Concurrent;
using CalmTerraria.Host;
using CalmTerraria.Screens;
using Microsoft.Xna.Framework;
using Srui;
using Terraria;

namespace CalmTerraria.World;

/// <summary>What the game shows a sighted player without being asked,
/// spoken: its messages (deaths, events, chat), the item the hotbar
/// lands on, the smart cursor going on and off, dying and coming
/// back. And one thing that is asked for: Backspace reads health,
/// mana and what is affecting the character.</summary>
internal sealed class Feedback
{
    private readonly SruiHost _host;
    /// <summary>The game writes messages from wherever it is; they
    /// are spoken from the frame.</summary>
    private readonly ConcurrentQueue<string> _messages = new();
    private int _selected = -1;
    private bool? _smartCursor;
    private bool _dead;

    public Feedback(SruiHost host, Shell shell)
    {
        _host = host;
        On_Main.NewText_string_byte_byte_byte += (orig, text, r, g, b) =>
        {
            orig(text, r, g, b);
            _messages.Enqueue(text);
        };
        On_Main.NewTextMultiline += (orig, text, force, color, width) =>
        {
            orig(text, force, color, width);
            _messages.Enqueue(text);
        };
        shell.EnteredWorld += () =>
        {
            _selected = -1;
            _smartCursor = null;
            _dead = false;
            _messages.Clear();
        };
        shell.WorldFrame += Update;
        shell.WorldCommands[KeyCombo.Plain(Key.Backspace)] = () => Say(Status());
    }

    private void Say(string text) => _host.App.Announce(text);

    private void Update()
    {
        while (_messages.TryDequeue(out var message))
        {
            var text = ItemText.Clean(message ?? "");
            if (text.Length > 0)
                Say(text);
        }

        var player = Main.LocalPlayer;
        if (player.dead != _dead)
        {
            _dead = player.dead;
            // The death message itself is one of the game's messages.
            Say(_dead ? $"Respawning in {(player.respawnTimer + 59) / 60} seconds." : "Respawned.");
        }

        var smart = Main.SmartCursorWanted;
        if (_smartCursor is { } was && was != smart)
            Say(smart ? "Smart cursor on." : "Smart cursor off.");
        _smartCursor = smart;

        // A change made from the pack screen has been said there.
        var selected = player.selectedItem;
        if (_selected >= 0 && selected != _selected && !_host.OwnsKeyboard && !player.dead)
            Say(ItemText.Stack(player.inventory[selected]));
        _selected = selected;
    }

    /// <summary>Health, mana, and each thing affecting the character
    /// with the time it has left.</summary>
    private static string Status()
    {
        var player = Main.LocalPlayer;
        var parts = new List<string> { $"Health {player.statLife} of {player.statLifeMax2}" };
        if (player.statManaMax2 > 0)
            parts.Add($"mana {player.statMana} of {player.statManaMax2}");
        if (player.statDefense > 0)
            parts.Add($"defense {(int)player.statDefense}");
        for (var i = 0; i < player.buffType.Length; i++)
        {
            var type = player.buffType[i];
            if (type <= 0 || player.buffTime[i] <= 0)
                continue;
            var name = Lang.GetBuffName(type);
            // Under a second is an effect renewed every frame while
            // its cause is near: it has no time left worth saying.
            parts.Add(Main.buffNoTimeDisplay[type] || player.buffTime[i] < 60
                ? name
                : $"{name} {Duration(player.buffTime[i])}");
        }
        return string.Join(", ", parts) + ".";
    }

    private static string Duration(int frames)
    {
        var seconds = frames / 60;
        if (seconds >= 120)
            return $"{seconds / 60} minutes";
        if (seconds >= 60)
            return "1 minute";
        return seconds == 1 ? "1 second" : $"{seconds} seconds";
    }
}
