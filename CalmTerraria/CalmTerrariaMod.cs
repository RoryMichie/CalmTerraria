using CalmTerraria.Host;
using CalmTerraria.Screens;
using Terraria;
using Terraria.ModLoader;

namespace CalmTerraria;

public sealed class CalmTerrariaMod : Mod
{
    private SruiHost? _host;
    private Shell? _shell;
    private Autopilot? _autopilot;
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
    }

    public override void Unload()
    {
        On_Main.DoUpdate_HandleInput -= AfterGameReadsInput;
        var host = _host;
        _host = null;
        _shell = null;
        _autopilot = null;
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
