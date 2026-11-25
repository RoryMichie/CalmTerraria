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
    /// <summary>What has been picked up and not yet said: a tree's
    /// worth of wood arrives as a dozen pickups in a second, and is
    /// one thing to hear.</summary>
    private readonly Dictionary<string, int> _picked = new();
    private readonly List<string> _pickedOrder = new();
    private ulong _pickedAt;
    /// <summary>How long pickups must stop for before they are said.</summary>
    private const ulong PickupQuietMs = 350;

    private const string FootstepPath = "TerrariaAmbience/Sounds/Custom/steps/";
    private const string ClothingPath = FootstepPath + "armor/";
    /// <summary>About ten decibels up, and never below a level that
    /// can be heard under the game.</summary>
    private const float FootstepGain = 3f;
    private const float FootstepFloor = 0.35f;

    /// <summary>For autopilot scripts: how many footsteps have been
    /// brought up, and the last of them.</summary>
    public static int FootstepsRaised { get; private set; }
    public static string LastFootstep { get; private set; } = "";

    private int _selected = -1;
    private bool? _smartCursor;
    private bool _dead;

    private readonly CalmTerraria.Pack.ChatLog _log;

    public Feedback(SruiHost host, Shell shell)
    {
        _host = host;
        _log = shell.ChatLog;
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
        On_PopupText.NewText_PopupTextContext_Item_int_bool_bool += (orig, context, item, stack, noStack, longText) =>
        {
            // Not "is it air": what goes onto a stack already carried
            // arrives here with nothing left in it, having all gone
            // into the stack, and is a pickup all the same.
            if (context is PopupTextContext.RegularItemPickup or PopupTextContext.ItemPickupToVoidContainer
                && item != null && item.type > 0 && stack > 0)
            {
                lock (_picked)
                {
                    var name = item.AffixName();
                    _picked[name] = _picked.GetValueOrDefault(name) + stack;
                    if (!_pickedOrder.Contains(name))
                        _pickedOrder.Add(name);
                    _pickedAt = _host.Now;
                }
            }
            return orig(context, item, stack, noStack, longText);
        };
        // Terraria Ambience's footsteps are made for a player who is
        // also watching; brought up to where they can be walked by.
        // Hooked where the sound is played and not at the game's
        // public entry, which is small enough to be compiled into its
        // callers, where no hook reaches it.
        Terraria.Audio.On_SoundPlayer.Play +=
            (Terraria.Audio.On_SoundPlayer.orig_Play orig, Terraria.Audio.SoundPlayer self,
                ref Terraria.Audio.SoundStyle style, Vector2? position, Terraria.Audio.SoundUpdateCallback callback) =>
            {
                if (style.Volume <= 0f || style.SoundPath?.StartsWith(FootstepPath, StringComparison.Ordinal) != true)
                    return orig(self, ref style, position, callback);
                // The floor is for the ground underfoot. Armour and
                // clothing are a layer over it, meant to stay under it.
                var floor = style.SoundPath.StartsWith(ClothingPath, StringComparison.Ordinal) ? 0f : FootstepFloor;
                var louder = style with { Volume = Math.Clamp(style.Volume * FootstepGain, floor, 1f) };
                FootstepsRaised++;
                LastFootstep = $"{style.SoundPath} {style.Volume:0.00} to {louder.Volume:0.00}";
                return orig(self, ref louder, position, callback);
            };
        shell.EnteredWorld += () =>
        {
            lock (_picked)
            {
                _picked.Clear();
                _pickedOrder.Clear();
            }
            _selected = -1;
            _smartCursor = null;
            _dead = false;
            _messages.Clear();
            _log.Clear();
        };
        shell.WorldFrame += Update;
        shell.WorldCommands[KeyCombo.Plain(Key.Backspace)] = () => Say(Status());
        shell.WorldCommands[KeyCombo.Plain(Key.Char('\''))] = () => Say(Instruments());
    }

    private void Say(string text) => _host.App.Announce(text);

    private void Update()
    {
        while (_messages.TryDequeue(out var message))
        {
            var text = ItemText.Clean(message ?? "");
            if (text.Length > 0)
            {
                _log.Add(text);
                Say(text);
            }
        }

        string? pickups = null;
        lock (_picked)
        {
            if (_pickedOrder.Count > 0 && _host.Now - _pickedAt >= PickupQuietMs)
            {
                pickups = string.Join(". ", _pickedOrder.Select(name =>
                    _picked[name] > 1 ? $"{name}, {_picked[name]}" : name));
                _picked.Clear();
                _pickedOrder.Clear();
            }
        }
        if (pickups != null)
            Say(pickups);

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
        if (_selected >= 0 && selected != _selected && !_host.OwnsKeyboard && !player.dead && !BuildTool.Working)
            Say(ItemText.Stack(player.inventory[selected]));
        _selected = selected;
    }

    /// <summary>What the character's informational accessories show -
    /// the watch's time, the depth meter's depth, the compass's
    /// position and the rest - in the words the game has just drawn
    /// for each. Those switched off in the game's own list are not
    /// drawn, and so are not read.</summary>
    private static string Instruments()
    {
        var readings = InfoCapture.Recent();
        if (readings.Count == 0)
            return "No informational accessories.";
        // Each as its own sentence; some come with their own ending.
        return string.Join(" ", readings.Select(text => text[^1] is '.' or '!' or '?' ? text : text + "."));
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
        // Some effects are named with their own punctuation: "Happy!"
        var said = string.Join(", ", parts);
        return said[^1] is '!' or '?' or '.' ? said : said + ".";
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

/// <summary>The game works out what each informational accessory
/// says as it draws it, and nowhere else. This is handed each one as
/// that happens, and keeps the latest.</summary>
internal sealed class InfoCapture : Terraria.ModLoader.GlobalInfoDisplay
{
    private static readonly SortedDictionary<int, (string Text, uint Frame)> Latest = new();

    public override void ModifyDisplayParameters(Terraria.ModLoader.InfoDisplay currentDisplay, ref string displayValue,
        ref string displayName, ref Color displayColor, ref Color displayShadowColor)
    {
        lock (Latest)
            Latest[currentDisplay.Type] = (displayValue ?? "", Main.GameUpdateCount);
    }

    /// <summary>What was drawn within the last half second, in the
    /// game's own order.</summary>
    public static List<string> Recent()
    {
        lock (Latest)
        {
            return Latest.Values
                .Where(entry => Main.GameUpdateCount - entry.Frame <= 30)
                .Select(entry => ItemText.Clean(entry.Text))
                // A meter with nothing to measure says so with these.
                .Where(text => text.Length > 0 && text != "N/A")
                .ToList();
        }
    }
}
