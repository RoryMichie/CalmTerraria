using CalmTerraria.Host;
using CalmTerraria.Screens;
using CalmTerraria.World;
using Terraria;
using Terraria.ModLoader;

namespace CalmTerraria;

public sealed class CalmTerrariaMod : Mod
{
    private SruiHost? _host;
    private Shell? _shell;
    private Autopilot? _autopilot;
    private Cursor? _cursor;
    private bool _failed;

    public override void Load()
    {
        if (Main.dedServ)
            return;
        NativeLibraries.Install(this);
        // The one hook the host needs: it runs every frame, in menus
        // and in a world alike, on the main thread, and the game's
        // keyboard read is the last thing the original does.
        On_Main.DoUpdate_HandleInput += AfterGameReadsInput;
        // The cursor is the mouse: its position and buttons go in
        // where the game reads the real one, so everything downstream
        // of that - aiming, reach, the held item - agrees.
        Terraria.GameInput.On_PlayerInput.MouseInput += orig =>
        {
            orig();
            if (!_failed)
                _cursor?.InjectMouse();
        };
        Terraria.GameInput.On_PlayerInput.KeyboardInput += orig =>
        {
            var any = orig();
            if (!_failed)
                _cursor?.InjectButtons();
            return any;
        };
    }

    public override void Unload()
    {
        On_Main.DoUpdate_HandleInput -= AfterGameReadsInput;
        var host = _host;
        _host = null;
        _shell = null;
        _autopilot = null;
        _cursor = null;
        if (host == null)
            return;
        // Mods unload off the main thread. SDL must stop calling into
        // this assembly now; speech and audio are torn down where they
        // were made.
        host.Detach();
        Main.QueueMainThreadAction(host.Dispose);
    }

    private void AfterGameReadsInput(On_Main.orig_DoUpdate_HandleInput orig, Main self)
    {
        orig(self);
        if (_failed)
            return;
        try
        {
            if (_host == null)
            {
                // Built here, not in Load: mods load off the main
                // thread, and speech belongs to the thread that speaks.
                _host = new SruiHost(Logger);
                _shell = new Shell(_host);
                _autopilot = Autopilot.TryLoad(_host, _shell, Logger);
                if (_autopilot != null)
                    DebugCommands.Register(_autopilot, Logger);
                var scanner = new WorldScanner(_host, _shell, Logger);
                var cursor = _cursor = new Cursor(_host, _shell);
                scanner.JumpRequested += target => cursor.MoveTo(target.X, target.Y);
                _autopilot?.Register("state", _ => Logger.Info($"State: {cursor.DebugState()}"));
                _autopilot?.Register("cursor-last", _ => cursor.MoveTo(DebugCommands.LastPlaced.X, DebugCommands.LastPlaced.Y));
                // cursor-npc Merchant - the cursor onto the nearest of them.
                _autopilot?.Register("cursor-npc", name =>
                {
                    var type = Terraria.ID.NPCID.Search.GetId(name.Trim());
                    foreach (var npc in Main.ActiveNPCs)
                    {
                        if (npc.type != type)
                            continue;
                        // Beside them first: people wander.
                        Main.LocalPlayer.Bottom = npc.Bottom - new Microsoft.Xna.Framework.Vector2(24f, 0f);
                        Main.LocalPlayer.velocity = Microsoft.Xna.Framework.Vector2.Zero;
                        cursor.MoveTo((int)(npc.Center.X / 16f), (int)(npc.Center.Y / 16f));
                        return;
                    }
                    throw new InvalidOperationException($"no {name} in the world");
                });
                _ = new Waypoints(_host, _shell, scanner, cursor);
                _ = new BuildTool(_host, _shell, cursor, scanner);
                _ = new MoveSounds(_host, _shell, Logger);
                _autopilot?.Register("moves", _ => Logger.Info($"Moves: {MoveSounds.Jumps} jumps, {MoveSounds.Landings} landings, {MoveSounds.Beats} heartbeats"));
                _ = new FlightSounds(_host, _shell, Logger);
                _autopilot?.Register("flight", _ => Logger.Info($"Flight: {FlightSounds.Report}"));
                _ = new Spy(_host, _shell);
                _ = new HazardWatch(_host, _shell);
                _ = new Feedback(_host, _shell);
                _autopilot?.Register("footsteps", _ => Logger.Info($"Footsteps: {Feedback.FootstepsRaised} raised, last {Feedback.LastFootstep}"));
                var sonar = new EnemySonar(_host, _shell, Logger);
                _autopilot?.Register("sonar-log", _ => sonar.LogPulses = !sonar.LogPulses);
                if (_autopilot != null)
                    scanner.RegisterDebug(_autopilot);
            }
            _autopilot?.Update();
            _shell!.Update();
            _host.Frame();
        }
        catch (Exception e)
        {
            // A failure here would otherwise repeat sixty times a
            // second; the game must stay playable without the mod.
            _failed = true;
            _host?.ReleaseKeyboard();
            Logger.Error("CalmTerraria stopped after an error.", e);
        }
    }
}
