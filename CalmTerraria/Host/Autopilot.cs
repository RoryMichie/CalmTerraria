using CalmTerraria.Screens;
using log4net;
using Srui;
using Terraria;
using XnaKeys = Microsoft.Xna.Framework.Input.Keys;

namespace CalmTerraria.Host;

/// <summary>Plays a script of input into the running game, so the mod
/// can be exercised in the real thing with nobody at the keyboard and
/// judged afterwards from the spoken transcript in the log. It runs
/// when <c>CalmTerraria/autopilot.txt</c> exists under the save path
/// at startup, and never otherwise.
///
/// One step per line; a step finishes before the next begins.
/// <list type="bullet">
/// <item><c>key ctrl+pagedown</c> - tap a combo, as typed.</item>
/// <item><c>down [</c>, <c>up [</c> - press and release separately,
/// for a key that is held.</item>
/// <item><c>type some text</c> - type characters.</item>
/// <item><c>hold D 800</c> - hold one of the game's keys for that many
/// milliseconds (the game's key names: A, Space, OemComma).</item>
/// <item><c>wait 500</c> - milliseconds.</item>
/// <item><c>heard Ready</c> - wait until something containing the text
/// has been spoken since the previous step began.</item>
/// <item><c>play calmtest calmtest</c> - start single player with a
/// character file and a world file, by file name. Only files whose
/// names start with "calmtest" are accepted, so a script can never
/// write to a real save.</item>
/// <item><c>do name args</c> - run a registered debug command.</item>
/// <item><c>log text</c> - a marker in the log.</item>
/// <item><c>quit</c> - close the game.</item>
/// </list>
/// A step that waits gives up after a minute, logs why, and the script
/// ends by closing the game.</summary>
internal sealed class Autopilot
{
    private const ulong TimeoutMs = 60_000;
    private const string TestPrefix = "calmtest";

    private readonly SruiHost _host;
    private readonly Shell _shell;
    private readonly ILog _log;
    private readonly Queue<string> _steps;
    private readonly List<string> _heardThisStep = new();
    private readonly Dictionary<string, Action<string>> _commands = new();
    private Func<bool>? _waiting;
    private string _waitingFor = "";
    private ulong _deadline;
    private XnaKeys? _held;

    public static Autopilot? TryLoad(SruiHost host, Shell shell, ILog log)
    {
        var path = Path.Combine(Main.SavePath, "CalmTerraria", "autopilot.txt");
        if (!File.Exists(path))
            return null;
        var steps = File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'));
        log.Info($"Autopilot: running {path}");
        return new Autopilot(host, shell, log, steps);
    }

    private Autopilot(SruiHost host, Shell shell, ILog log, IEnumerable<string> steps)
    {
        _host = host;
        _shell = shell;
        _log = log;
        _steps = new Queue<string>(steps);
        host.Spoke += text => _heardThisStep.Add(text);
    }

    /// <summary>A named action a script can run with <c>do</c>.</summary>
    public void Register(string name, Action<string> command) => _commands[name] = command;

    /// <summary>Once a frame, before the shell's.</summary>
    public void Update()
    {
        if (_waiting != null)
        {
            if (_waiting())
                _waiting = null;
            else if (_host.Now > _deadline)
            {
                _log.Error($"Autopilot: gave up waiting for {_waitingFor}. Heard: {string.Join(" | ", _heardThisStep)}");
                _steps.Clear();
                _steps.Enqueue("quit");
                _waiting = null;
            }
            else
                return;
        }
        if (_held is { } held)
        {
            _host.HeldForGame.Remove(held);
            _held = null;
        }
        if (_steps.Count == 0)
            return;
        var step = _steps.Dequeue();
        _log.Info($"Autopilot: {step}");
        try
        {
            Run(step);
        }
        catch (Exception e)
        {
            _log.Error($"Autopilot: '{step}' failed.", e);
            _steps.Clear();
            _steps.Enqueue("quit");
        }
    }

    private void Run(string step)
    {
        var space = step.IndexOf(' ');
        var verb = space < 0 ? step : step[..space];
        var rest = space < 0 ? "" : step[(space + 1)..].Trim();
        switch (verb)
        {
            case "key":
                _heardThisStep.Clear();
                Tap(rest);
                // The tap is dispatched by the host this frame; let it land.
                WaitFor("the tap to land", After(50));
                break;
            case "down":
            case "up":
            {
                if (!KeyCombo.TryParseConfig(rest, out var combo))
                    throw new FormatException($"'{rest}' is not a key combo");
                var (key, mods) = combo.ToFlat();
                _heardThisStep.Clear();
                _host.Inject(new KeyboardEvent(
                    new KeyInput(key, mods, verb == "down" ? KeyPhase.Press : KeyPhase.Release), null));
                WaitFor("the key to land", After(50));
                break;
            }
            case "type":
                _heardThisStep.Clear();
                foreach (var c in rest)
                    _host.Inject(new KeyboardEvent(null, InputEvent.TypeChar(c)));
                WaitFor("the typing to land", After(50));
                break;
            case "hold":
            {
                var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var key = Enum.Parse<XnaKeys>(parts[0], ignoreCase: true);
                _heardThisStep.Clear();
                _held = key;
                _host.HeldForGame.Add(key);
                WaitFor($"{key} to be held", After(ulong.Parse(parts[1])));
                break;
            }
            case "wait":
                WaitFor("time", After(ulong.Parse(rest)));
                break;
            case "heard":
                WaitFor($"'{rest}' to be spoken", () => _heardThisStep.Any(
                    text => text.Contains(rest, StringComparison.OrdinalIgnoreCase)));
                break;
            case "play":
            {
                var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                _heardThisStep.Clear();
                Play(parts[0], parts[1]);
                break;
            }
            case "join":
            {
                // join calmtest 127.0.0.1 7777
                var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                _heardThisStep.Clear();
                if (!parts[0].StartsWith(TestPrefix, StringComparison.OrdinalIgnoreCase)
                    || (parts[1] != "127.0.0.1" && parts[1] != "localhost"))
                    throw new InvalidOperationException("the autopilot only joins this machine, as a test character");
                Main.LoadPlayers();
                var player = Main.PlayerList.FirstOrDefault(p => FileName(p.Path) == parts[0])
                    ?? throw new FileNotFoundException($"no character file {parts[0]}");
                _shell.Join(player, parts[1], int.Parse(parts[2]));
                break;
            }
            case "do":
            {
                var nameEnd = rest.IndexOf(' ');
                var name = nameEnd < 0 ? rest : rest[..nameEnd];
                _heardThisStep.Clear();
                _commands[name](nameEnd < 0 ? "" : rest[(nameEnd + 1)..].Trim());
                break;
            }
            case "log":
                break;
            case "quit":
                _shell.Quit();
                break;
            default:
                throw new FormatException($"unknown step '{verb}'");
        }
    }

    private Func<bool> After(ulong ms)
    {
        var at = _host.Now + ms;
        return () => _host.Now >= at;
    }

    private void WaitFor(string what, Func<bool> done)
    {
        _waiting = done;
        _waitingFor = what;
        _deadline = _host.Now + TimeoutMs;
    }

    private void Tap(string config)
    {
        if (!KeyCombo.TryParseConfig(config, out var combo))
            throw new FormatException($"'{config}' is not a key combo");
        var (key, mods) = combo.ToFlat();
        var input = combo.ToInput();
        // A plain printable arrives as the character it types.
        if (input is null && !combo.Ctrl && !combo.Alt)
        {
            if (combo.Key == Key.Space)
                input = new InputEvent(InputKind.TypeChar, ' ', key, mods);
            else if (combo.Key.IsChar(out var c))
                input = new InputEvent(InputKind.TypeChar, combo.Shift ? char.ToUpperInvariant(c) : c, key, mods);
        }
        _host.Inject(new KeyboardEvent(new KeyInput(key, mods, KeyPhase.Press), input));
        _host.Inject(new KeyboardEvent(new KeyInput(key, mods, KeyPhase.Release), null));
    }

    private void Play(string playerFile, string worldFile)
    {
        if (!playerFile.StartsWith(TestPrefix, StringComparison.OrdinalIgnoreCase)
            || !worldFile.StartsWith(TestPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"the autopilot only plays saves named {TestPrefix}*");
        Main.LoadPlayers();
        var player = Main.PlayerList.FirstOrDefault(p => FileName(p.Path) == playerFile)
            ?? throw new FileNotFoundException($"no character file {playerFile}");
        // A copy of a real save carries the original's name; rename it
        // so the two can be told apart in the lists.
        if (player.Name != playerFile)
            player.Rename(playerFile);
        Main.LoadWorlds();
        var world = Main.WorldList.FirstOrDefault(w => FileName(w.Path) == worldFile)
            ?? throw new FileNotFoundException($"no world file {worldFile}");
        _shell.Play(player, world, renameWorldTo: worldFile);
    }

    private static string FileName(string path) =>
        Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
}
