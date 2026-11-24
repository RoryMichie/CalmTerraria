using CalmTerraria.Host;
using CalmTerraria.Scan;
using CalmTerraria.Screens;
using Microsoft.Xna.Framework;
using Srui;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameInput;

namespace CalmTerraria.World;

/// <summary>The cursor: a tile in the world the player points at, in
/// place of the mouse. It is moved from the right-hand pad, says what
/// is under it as it lands, and is where the game believes the mouse
/// to be - so the held item is used there, and the two bracket keys
/// are the two mouse buttons.
///
/// While it is within the player's reach it travels with the player,
/// keeping its place relative to them: the tile ahead stays the tile
/// ahead. Sent beyond reach - by the scanner, or by walking it there -
/// it stays on that spot in the world, for looking around, until it is
/// moved back within reach or Ctrl+K brings it home.</summary>
internal sealed class Cursor
{
    /// <summary>How far a skim looks for something different.</summary>
    private const int SkimLimit = 200;

    private readonly SruiHost _host;
    private readonly Shell _shell;
    private int _x, _y;
    private bool _placed;
    /// <summary>Whether the cursor travels with the player, and where
    /// it sits relative to them when it does.</summary>
    private bool _carried;
    private int _offsetX, _offsetY;
    private bool _left, _right;

    public Cursor(SruiHost host, Shell shell)
    {
        _host = host;
        _shell = shell;
        shell.EnteredWorld += () =>
        {
            _placed = false;
            _left = _right = false;
        };
        shell.LeftWorld += () => _left = _right = false;
        shell.WorldFrame += Carry;
        shell.WorldKeyReleased += key =>
        {
            if (key == Key.Char('['))
                _left = false;
            if (key == Key.Char(']'))
                _right = false;
        };

        var commands = shell.WorldCommands;
        // The pad, as it lies under the hand: U I O, J K L, N M comma.
        Bind(commands, 'u', -1, -1);
        Bind(commands, 'i', 0, -1);
        Bind(commands, 'o', 1, -1);
        Bind(commands, 'j', -1, 0);
        Bind(commands, 'l', 1, 0);
        Bind(commands, 'n', -1, 1);
        Bind(commands, 'm', 0, 1);
        Bind(commands, ',', 1, 1);
        commands[KeyCombo.Plain(Key.Char('k'))] = ReadWhere;
        commands[KeyCombo.WithCtrl(Key.Char('k'))] = () =>
        {
            var (px, py) = WorldScanner.PlayerTile();
            MoveTo(px, py);
        };
        commands[KeyCombo.Plain(Key.Char('['))] = () => Press(ref _left);
        commands[KeyCombo.Plain(Key.Char(']'))] = () => Press(ref _right);
    }

    private void Bind(Dictionary<KeyCombo, Action> commands, char key, int dx, int dy)
    {
        commands[KeyCombo.Plain(Key.Char(key))] = () => Move(dx, dy);
        commands[KeyCombo.WithShift(Key.Char(key))] = () => Skim(dx, dy);
    }

    /// <summary>The cursor's tile. It starts on the player.</summary>
    public (int X, int Y) Tile
    {
        get
        {
            if (!_placed)
            {
                var (px, py) = WorldScanner.PlayerTile();
                Place(px, py);
            }
            return (_x, _y);
        }
    }

    /// <summary>Put the cursor on a tile and say what is there.</summary>
    public void MoveTo(int x, int y)
    {
        Place(x, y);
        Say(TileReader.Describe(_x, _y));
    }

    private void Place(int x, int y)
    {
        _x = Math.Clamp(x, 0, Main.maxTilesX - 1);
        _y = Math.Clamp(y, 0, Main.maxTilesY - 1);
        _placed = true;
        var (px, py) = WorldScanner.PlayerTile();
        _offsetX = _x - px;
        _offsetY = _y - py;
        _carried = InReach(_x, _y);
    }

    private static bool InReach(int x, int y) =>
        Main.LocalPlayer.IsInTileInteractionRange(x, y, TileReachCheckSettings.Simple);

    /// <summary>Each frame: a cursor within reach goes where the
    /// player goes. Silently - the player moved, not the cursor.</summary>
    private void Carry()
    {
        if (_lockWatch is { } watch)
        {
            // A key in the pack opens it on the click; without one
            // the game does nothing at all.
            if (!Chest.IsLocked(watch.X, watch.Y))
                _lockWatch = null;
            else if (watch.Frames <= 0)
            {
                _lockWatch = null;
                Say("Locked.");
            }
            else
                _lockWatch = (watch.X, watch.Y, watch.Frames - 1);
        }
        if (!_placed || !_carried)
            return;
        var (px, py) = WorldScanner.PlayerTile();
        _x = Math.Clamp(px + _offsetX, 0, Main.maxTilesX - 1);
        _y = Math.Clamp(py + _offsetY, 0, Main.maxTilesY - 1);
    }

    private void Move(int dx, int dy)
    {
        var (x, y) = Tile;
        MoveTo(x + dx, y + dy);
    }

    /// <summary>Run in a direction to the first tile that reads
    /// differently from this one: the end of the wall, the far side of
    /// the gap.</summary>
    private void Skim(int dx, int dy)
    {
        var (x, y) = Tile;
        var from = TileReader.Describe(x, y);
        for (var step = 0; step < SkimLimit; step++)
        {
            if (!WorldGen.InWorld(x + dx, y + dy))
                break;
            x += dx;
            y += dy;
            if (TileReader.Describe(x, y) != from)
                break;
        }
        MoveTo(x, y);
    }

    /// <summary>K: what is here, and where here is from the player.</summary>
    private void ReadWhere()
    {
        var (x, y) = Tile;
        var (px, py) = WorldScanner.PlayerTile();
        Say($"{TileReader.Describe(x, y)}, {Directions.Offset(x - px, y - py)}");
    }

    /// <summary>A locked chest that was just reached for, and how many
    /// frames it has to come unlocked before that is said.</summary>
    private (int X, int Y, int Frames)? _lockWatch;

    /// <summary>The right button on a chest: the cases where the game
    /// does nothing and shows nothing. Out of reach and locked are
    /// said. A chest another player has open brings the pack screen
    /// with that reason where the chest would be. True when the click
    /// should not be sent.</summary>
    private bool ChestRefused(int x, int y)
    {
        var tile = Main.tile[x, y];
        if (!tile.HasTile || !Terraria.ID.TileID.Sets.BasicChest[tile.TileType])
            return false;
        var player = Main.LocalPlayer;
        var (px, py) = WorldScanner.PlayerTile();
        if (!InReach(x, y))
        {
            Say($"Out of reach, {Directions.Offset(x - px, y - py)}.");
            return true;
        }
        var index = Chest.FindChestByGuessing(x, y);
        if (index >= 0)
        {
            var user = Chest.UsingChest(index);
            if (user >= 0 && user != Main.myPlayer)
            {
                _shell.OpenPackOnNotice($"{TileNames.At(x, y, tile)}, in use by {Main.player[user].name}");
                return true;
            }
            var chest = Main.chest[index];
            if (Chest.IsLocked(chest.x, chest.y))
                _lockWatch = (chest.x, chest.y, 30);
        }
        return false;
    }

    /// <summary>The right button on someone who can be talked to but
    /// is too far away to hear: the game does nothing, so it is said.</summary>
    private bool PersonTooFar(int x, int y)
    {
        var tile = new Rectangle(x * 16, y * 16, 16, 16);
        var player = Main.LocalPlayer;
        // The game's own test for who is near enough to talk to.
        var near = new Rectangle(
            (int)player.Center.X - Player.tileRangeX * 16, (int)player.Center.Y - Player.tileRangeY * 16,
            Player.tileRangeX * 32, Player.tileRangeY * 32);
        foreach (var npc in Main.ActiveNPCs)
        {
            if (!npc.townNPC && !npc.isLikeATownNPC || !npc.Hitbox.Intersects(tile))
                continue;
            if (near.Intersects(npc.Hitbox))
                return false;
            var (px, py) = WorldScanner.PlayerTile();
            Say($"Too far to talk, {Directions.Offset(x - px, y - py)}.");
            return true;
        }
        return false;
    }

    private void Press(ref bool button)
    {
        if (button)
            return;
        var (x, y) = Tile;
        if (System.Runtime.CompilerServices.Unsafe.AreSame(ref button, ref _right) && (ChestRefused(x, y) || PersonTooFar(x, y)))
            return;
        if (!InReach(x, y) && UsesReach(Main.LocalPlayer.HeldItem))
        {
            var (px, py) = WorldScanner.PlayerTile();
            Say($"Out of reach, {Directions.Offset(x - px, y - py)}.");
        }
        button = true;
    }

    /// <summary>Whether using the item acts on the tile under the
    /// cursor, so that distance matters: tools and things that are
    /// placed. A sword or a bow is aimed, not reached with.</summary>
    private static bool UsesReach(Item item) =>
        item.pick > 0 || item.axe > 0 || item.hammer > 0 || item.createTile >= 0 || item.createWall > 0;

    private void Say(string text) => _host.App.Announce(text);

    /// <summary>For autopilot scripts: what the game currently
    /// believes about the mouse and the held item.</summary>
    public string DebugState()
    {
        var player = Main.LocalPlayer;
        return $"cursor {Tile}{(_carried ? " carried" : " pinned")}, player {WorldScanner.PlayerTile()}, held '{player.HeldItem.Name}' slot {player.selectedItem}, "
            + $"focus {Main.hasFocus} active {Main.instance.IsActive}, mouse {Main.mouseX},{Main.mouseY} left {Main.mouseLeft} "
            + $"brackets {_left}/{_right}, target {Player.tileTargetX},{Player.tileTargetY}, "
            + $"useItem {player.controlUseItem} animation {player.itemAnimation} dead {player.dead}";
    }

    /// <summary>Called as the game reads the mouse: the mouse is
    /// where the cursor is.</summary>
    public void InjectMouse()
    {
        if (!_shell.InWorld || _host.OwnsKeyboard)
        {
            _left = _right = false;
            return;
        }
        if (!_placed)
            return;
        var world = new Vector2(_x * 16f + 8f, _y * 16f + 8f);
        var screen = Vector2.Transform(world - Main.screenPosition, Main.GameViewMatrix.ZoomMatrix);
        PlayerInput.MouseX = (int)Math.Clamp(screen.X, 0f, Main.screenWidth - 1f);
        PlayerInput.MouseY = (int)Math.Clamp(screen.Y, 0f, Main.screenHeight - 1f);
    }

    /// <summary>Called as the game turns keys into controls: the
    /// brackets are the mouse buttons. Set on the controls themselves,
    /// not as mouse presses for the game to map, because which control
    /// a mouse button is bound to is the player's to change.</summary>
    public void InjectButtons()
    {
        if (!_shell.InWorld || _host.OwnsKeyboard)
            return;
        if (_left)
            PlayerInput.Triggers.Current.MouseLeft = true;
        if (_right)
            PlayerInput.Triggers.Current.MouseRight = true;
    }
}
