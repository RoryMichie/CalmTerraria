using CalmTerraria.Host;
using Srui;
using Terraria;

namespace CalmTerraria.Screens;

/// <summary>Escape, in a world. The world keeps running underneath.</summary>
internal sealed class GameMenu
{
    private readonly SruiHost _host;
    private readonly Shell _shell;
    private Dialog? _dialog;

    public GameMenu(SruiHost host, Shell shell)
    {
        _host = host;
        _shell = shell;
    }

    public void Open()
    {
        if (_dialog != null)
            return;
        _host.TakeKeyboard();
        var dialog = _dialog = _host.App.OpenDialog();
        var group = new Group(dialog, "Game menu");
        var exit = new Button(group, "Save and exit");
        exit.Activated += () =>
        {
            _shell.BeginBusy("Saving.");
            dialog.Close();
            WorldGen.SaveAndQuit();
        };
        dialog.Closed += () =>
        {
            _dialog = null;
            // Mid-save the keys stay ours: the character must not
            // walk off while the world is being written.
            if (!_shell.Busy)
                _host.ReleaseKeyboard();
        };
        dialog.AnnounceOpened();
    }

    public void Close()
    {
        _dialog?.Close(nested: true);
    }
}
