using CalmTerraria.Host;
using CalmTerraria.Screens;
using Srui;
using Terraria;

namespace CalmTerraria.World;

/// <summary>A look along a line. Shift with a movement key reads what
/// lies that way from the character, nearest first, as runs of the
/// same thing and how many tiles of each: "Air, 5. Grass, 1. Dirt,
/// 12." It reads what the cursor would read on each tile, so what is
/// not yet explored reads as unknown here too.</summary>
internal sealed class Spy
{
    /// <summary>How far it looks, and how many runs it reads: past a
    /// few, the far end of the line is not what was asked about.</summary>
    private const int Range = 50, MaxRuns = 6;

    private readonly SruiHost _host;

    public Spy(SruiHost host, Shell shell)
    {
        _host = host;
        var commands = shell.WorldCommands;
        commands[KeyCombo.WithShift(Key.Char('a'))] = () => Look(-1, 0);
        commands[KeyCombo.WithShift(Key.Char('d'))] = () => Look(1, 0);
        commands[KeyCombo.WithShift(Key.Char('w'))] = () => Look(0, -1);
        commands[KeyCombo.WithShift(Key.Char('s'))] = () => Look(0, 1);
    }

    private void Look(int dx, int dy)
    {
        var (x, y) = WorldScanner.PlayerTile();
        // Past the body: the character is three tiles tall around its
        // middle, and one wide.
        if (dy < 0)
            y -= 1;
        else if (dy > 0)
            y += 1;
        var runs = new List<(string Name, int Length)>();
        for (var step = 1; step <= Range; step++)
        {
            int tx = x + dx * step, ty = y + dy * step;
            if (!WorldGen.InWorld(tx, ty))
            {
                runs.Add(("Edge of the world", 0));
                break;
            }
            var name = TileReader.Describe(tx, ty);
            if (runs.Count > 0 && runs[^1].Name == name)
                runs[^1] = (name, runs[^1].Length + 1);
            else if (runs.Count == MaxRuns)
                break;
            else
                runs.Add((name, 1));
        }
        _host.App.Announce(string.Join(" ", runs.Select(run =>
            run.Length == 0 ? $"{run.Name}." : $"{run.Name}, {run.Length}.")));
    }
}
