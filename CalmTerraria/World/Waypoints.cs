using CalmTerraria.Host;
using CalmTerraria.Scan;
using CalmTerraria.Screens;
using Srui;
using Terraria;

namespace CalmTerraria.World;

/// <summary>Places the player has named. They are the scanner's first
/// category, so everything the scanner does with a chest - how far,
/// which way, put the cursor on it - it does with a waypoint. Kept
/// per world, in a file beside the mod's other files.
///
/// Backslash names the tile under the cursor. Delete removes the
/// waypoint the scanner is on.</summary>
internal sealed class Waypoints
{
    public const string Category = "Waypoints";

    public static Waypoints? Instance { get; private set; }

    private readonly SruiHost _host;
    private readonly Shell _shell;
    private readonly WorldScanner _scanner;
    private readonly Cursor _cursor;
    private readonly List<(string Name, int X, int Y)> _points = new();
    private readonly Dictionary<Feature, int> _shown = new();
    private string? _file;

    public Waypoints(SruiHost host, Shell shell, WorldScanner scanner, Cursor cursor)
    {
        Instance = this;
        _host = host;
        _shell = shell;
        _scanner = scanner;
        _cursor = cursor;
        scanner.Extra = Features;
        shell.EnteredWorld += Load;
        shell.WorldCommands[KeyCombo.Plain(Key.Char('\\'))] = Ask;
        shell.WorldCommands[KeyCombo.Plain(Key.Delete)] = DeleteSelected;
    }

    public IReadOnlyList<(string Name, int X, int Y)> Points => _points;

    private IEnumerable<Feature> Features()
    {
        _shown.Clear();
        for (var i = 0; i < _points.Count; i++)
        {
            var feature = new Feature(new ScanKind(_points[i].Name, Category), _points[i].X, _points[i].Y);
            _shown[feature] = i;
            yield return feature;
        }
    }

    private void Load()
    {
        _points.Clear();
        // The world's name and number: the same on a server as alone.
        var name = string.Concat($"{Main.worldName}-{Main.worldID}".Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' or ' ' ? c : '_'));
        _file = Path.Combine(Main.SavePath, "CalmTerraria", "waypoints", name + ".txt");
        if (!File.Exists(_file))
            return;
        foreach (var line in File.ReadAllLines(_file))
        {
            // x, y, then the name, which may hold anything.
            var parts = line.Split('\t', 3);
            if (parts.Length == 3 && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y))
                _points.Add((parts[2], x, y));
        }
    }

    private void Save()
    {
        if (_file == null)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllLines(_file, _points.Select(point => $"{point.X}\t{point.Y}\t{point.Name}"));
    }

    /// <summary>Name the tile under the cursor.</summary>
    private void Ask()
    {
        var (x, y) = _cursor.Tile;
        var dialog = _shell.OpenOverWorld();
        var name = new EditBox(dialog, "Waypoint name", $"Waypoint {_points.Count + 1}") { SelectAllOnFocus = true };
        var save = new Button(dialog, "Save");
        _host.App.SetPrimary(save);
        save.Activated += () =>
        {
            var text = name.Text.Trim().Replace('\t', ' ');
            if (text.Length == 0)
            {
                _host.App.Announce("A name is needed.");
                return;
            }
            _points.Add((text, x, y));
            Save();
            dialog.Close();
            _host.App.Announce("Saved.");
            _scanner.Retake();
        };
        dialog.AnnounceOpened();
    }

    private void DeleteSelected()
    {
        if (_scanner.Selected is not { } feature || !_shown.TryGetValue(feature, out var index) || index >= _points.Count)
        {
            _host.App.Announce("The scanner is not on a waypoint.");
            return;
        }
        _points.RemoveAt(index);
        Save();
        _host.App.Announce("Deleted.");
        _scanner.Retake();
    }

    /// <summary>Stand the player on a waypoint, as the game's own
    /// teleports do, telling a server.</summary>
    public bool TeleportTo(string name)
    {
        var index = _points.FindIndex(point => point.Name == name);
        if (index < 0)
            return false;
        var player = Main.LocalPlayer;
        var to = new Microsoft.Xna.Framework.Vector2(
            _points[index].X * 16f + 8f - player.width / 2f, _points[index].Y * 16f + 16f - player.height);
        player.Teleport(to, 1);
        player.velocity = Microsoft.Xna.Framework.Vector2.Zero;
        if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient)
            NetMessage.SendData(Terraria.ID.MessageID.TeleportEntity, -1, -1, null, 0, player.whoAmI, to.X, to.Y, 1);
        return true;
    }
}
