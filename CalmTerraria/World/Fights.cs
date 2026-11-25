using CalmTerraria.Host;
using CalmTerraria.Screens;
using Srui;
using Terraria;
using Terraria.GameContent.Events;
using Terraria.ID;

namespace CalmTerraria.World;

/// <summary>What the game shows as bars across the screen: a boss's
/// health and how far an event has got. Shift+Backspace says both.
/// Without being asked, a boss's health is said as it falls past
/// three quarters, a half, a quarter and a tenth, and an invasion's
/// progress at each quarter; the game's own messages already say
/// when either starts and ends.</summary>
internal sealed class Fights
{
    private static readonly int[] BossMarks = { 75, 50, 25, 10 };

    private readonly SruiHost _host;
    /// <summary>For each boss in the fight, by name: the most health
    /// it has been seen to have in all, and the last mark said.</summary>
    private readonly Dictionary<string, (long Most, int Said)> _bosses = new();
    private int _eventSaid;
    private string _event = "";

    public Fights(SruiHost host, Shell shell)
    {
        _host = host;
        shell.WorldCommands[KeyCombo.WithShift(Key.Backspace)] = () => _host.App.Announce(Describe());
        shell.WorldFrame += Update;
        shell.EnteredWorld += () =>
        {
            _bosses.Clear();
            _event = "";
            _eventSaid = 0;
        };
    }

    /// <summary>The bosses about, each with the health of all its
    /// parts together: a worm is its segments, and a part that shares
    /// another's health is counted once.</summary>
    private static Dictionary<string, long> Bosses()
    {
        var bosses = new Dictionary<string, long>();
        foreach (var npc in Main.ActiveNPCs)
        {
            if (!npc.boss && !NPCID.Sets.ShouldBeCountedAsBoss[npc.type])
                continue;
            if (npc.realLife >= 0 && npc.realLife != npc.whoAmI)
                continue;
            if (npc.life <= 0)
                continue;
            bosses[npc.TypeName] = bosses.GetValueOrDefault(npc.TypeName) + npc.life;
        }
        return bosses;
    }

    /// <summary>The event under way, and how far through it is in
    /// hundredths where the game counts that, or -1.</summary>
    private static (string Name, int Percent)? Event()
    {
        if (Main.invasionType > 0 && Main.invasionSize > 0)
        {
            var name = Main.invasionType switch
            {
                InvasionID.GoblinArmy => "Goblin Army",
                InvasionID.SnowLegion => "Frost Legion",
                InvasionID.PirateInvasion => "Pirate Invasion",
                InvasionID.MartianMadness => "Martian Madness",
                _ => "Invasion",
            };
            var start = Math.Max(Main.invasionSizeStart, Main.invasionSize);
            return (name, start > 0 ? (int)(100L * (start - Main.invasionSize) / start) : -1);
        }
        // The moons and the Old One's Army go by waves, and the game
        // keeps the count of the wave only as it reports it.
        var wave = Main.invasionProgressMax > 0
            ? (int)(100L * Math.Clamp(Main.invasionProgress, 0, Main.invasionProgressMax) / Main.invasionProgressMax)
            : -1;
        if (Main.pumpkinMoon)
            return ($"Pumpkin Moon, wave {NPC.waveNumber}", wave);
        if (Main.snowMoon)
            return ($"Frost Moon, wave {NPC.waveNumber}", wave);
        if (DD2Event.Ongoing)
            return ($"Old One's Army, wave {NPC.waveNumber}", wave);
        if (Main.slimeRain)
            return ($"Slime Rain, {Main.slimeRainKillCount} killed", -1);
        if (Main.bloodMoon)
            return ("Blood Moon", -1);
        if (Main.eclipse)
            return ("Solar Eclipse", -1);
        return null;
    }

    private string Describe()
    {
        var parts = new List<string>();
        foreach (var (name, life) in Bosses())
        {
            var most = Math.Max(life, _bosses.GetValueOrDefault(name).Most);
            parts.Add($"{name}, {life} of {most}, {100 * life / most} percent.");
        }
        if (Event() is { } now)
            parts.Add(now.Percent >= 0 ? $"{now.Name}, {now.Percent} percent." : $"{now.Name}.");
        return parts.Count > 0 ? string.Join(" ", parts) : "No boss, no event.";
    }

    private void Update()
    {
        var bosses = Bosses();
        foreach (var gone in _bosses.Keys.Where(name => !bosses.ContainsKey(name)).ToList())
            _bosses.Remove(gone);
        foreach (var (name, life) in bosses)
        {
            var (most, said) = _bosses.TryGetValue(name, out var known) ? known : (life, 100);
            most = Math.Max(most, life);
            var percent = (int)(100 * life / most);
            // The lowest mark it is now under, said once.
            var mark = BossMarks.Where(at => percent <= at).DefaultIfEmpty(100).Min();
            if (mark < said)
            {
                _host.App.Announce($"{name}, {mark} percent.");
                said = mark;
            }
            _bosses[name] = (most, said);
        }

        if (Event() is not { Percent: >= 0 } now)
        {
            _event = "";
            _eventSaid = 0;
            return;
        }
        if (now.Name != _event)
        {
            _event = now.Name;
            _eventSaid = now.Percent / 25 * 25;
            return;
        }
        var quarter = now.Percent / 25 * 25;
        if (quarter > _eventSaid && quarter < 100)
        {
            _eventSaid = quarter;
            _host.App.Announce($"{now.Name}, {quarter} percent.");
        }
    }
}
