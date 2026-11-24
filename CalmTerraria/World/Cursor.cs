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
        // Shift+K: the game's own housing query, on the cursor's tile.
        // What it finds is one of the game's messages, spoken as those are.
        commands[KeyCombo.WithShift(Key.Char('k'))] = () =>
        {
            var (x, y) = Tile;
            if (WorldGen.MoveTownNPC(x, y, -1))
                Main.NewText(Lang.inter[39].Value, 255, 240, 20);
        };
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
    /// <param name="where">Say also where that is from the player:
    /// for moves made from the pad, where it is the only way to keep
    /// track. Not for a jump from the scanner, which has just said.</param>
    public void MoveTo(int x, int y, bool where = false)
    {
        Place(x, y);
        if (where)
        {
            var (px, py) = WorldScanner.PlayerTile();
            Say($"{TileReader.Describe(_x, _y)}, {Directions.Offset(_x - px, _y - py)}");
        }
        else
            Say(TileReader.Describe(_x, _y));
        // Dragged with the button down: the next tile is built on too.
        if (_left)
            WatchPlacing();
    }

    private static readonly string[] LiquidWords = { "water", "lava", "honey", "shimmer" };

    /// <summary>A bucket or a sponge used where the game will do
    /// nothing with it, which the game does in silence: why not, or
    /// null when it will work. The rules are the game's own.</summary>
    private static string? BucketRefusal(Item item, int x, int y)
    {
        int pours = item.type switch
        {
            Terraria.ID.ItemID.WaterBucket or Terraria.ID.ItemID.BottomlessBucket => 0,
            Terraria.ID.ItemID.LavaBucket or Terraria.ID.ItemID.BottomlessLavaBucket => 1,
            Terraria.ID.ItemID.HoneyBucket or Terraria.ID.ItemID.BottomlessHoneyBucket => 2,
            _ => -1,
        };
        var scoops = item.type is Terraria.ID.ItemID.EmptyBucket or Terraria.ID.ItemID.SuperAbsorbantSponge
            or Terraria.ID.ItemID.LavaAbsorbantSponge;
        if (pours < 0 && !scoops)
            return null;
        var tile = Main.tile[x, y];
        var there = LiquidWords[Math.Clamp(tile.LiquidType, 0, LiquidWords.Length - 1)];
        if (scoops)
        {
            if (tile.LiquidAmount == 0)
                return "Nothing to take up here.";
            if (item.type == Terraria.ID.ItemID.SuperAbsorbantSponge && tile.LiquidType != Terraria.ID.LiquidID.Water)
                return $"This sponge takes water; that is {there}.";
            if (item.type == Terraria.ID.ItemID.LavaAbsorbantSponge && tile.LiquidType != Terraria.ID.LiquidID.Lava)
                return $"This sponge takes lava; that is {there}.";
            return null;
        }
        if (tile.HasTile && !tile.IsActuated && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType])
            return "Solid here: it pours into an open tile.";
        if (tile.LiquidAmount > 0 && tile.LiquidType != pours)
            return $"There is {there} in this tile: it pours into an empty one beside it.";
        if (tile.LiquidAmount >= 200)
            return "Already full.";
        return null;
    }

    /// <summary>A tile something is being placed on, as it was, and
    /// how many frames it has to change before the failure is said.</summary>
    private (int X, int Y, bool HadTile, ushort Wall, int Item, int Frames)? _placeWatch;

    /// <summary>Placing makes the game's own sound when it works and
    /// nothing at all when it does not; this is where the nothing
    /// gets words.</summary>
    private void WatchPlacing()
    {
        var item = Main.LocalPlayer.HeldItem;
        if (item.IsAir || (item.createTile < 0 && item.createWall <= 0) || !InReach(_x, _y))
        {
            _placeWatch = null;
            return;
        }
        var tile = Main.tile[_x, _y];
        _placeWatch = (_x, _y, tile.HasTile, tile.WallType, item.type, 20);
    }

    private void CheckPlacing()
    {
        if (_placeWatch is not { } watch)
            return;
        var tile = Main.tile[watch.X, watch.Y];
        var held = Main.LocalPlayer.HeldItem;
        if (tile.HasTile != watch.HadTile || tile.WallType != watch.Wall)
        {
            _placeWatch = null;
            // The last of the stack went into it.
            if (held.IsAir || held.type != watch.Item)
                Say("None left.");
            return;
        }
        if (watch.Frames > 0 && _left)
        {
            _placeWatch = watch with { Frames = watch.Frames - 1 };
            return;
        }
        _placeWatch = null;
        if (watch.Frames > 0)
            return;
        var wall = Terraria.ID.ContentSamples.ItemsByType[watch.Item].createWall > 0;
        if (wall)
            Say(watch.Wall != 0 ? "Already walled." : "Nothing to attach the wall to.");
        else
            Say(watch.HadTile ? "Occupied." : "Nothing to attach to.");
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
        CheckPlacing();
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
        MoveTo(x + dx, y + dy, where: true);
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
        MoveTo(x, y, where: true);
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

    /// <summary>A floating container the right button is being held
    /// on: the game opens it only with the mouse exactly on it, and it
    /// bobs about, so the mouse rides on it for as long as the button
    /// is down.</summary>
    private Projectile? _riding;

    /// <summary>The right button near a flying piggy bank or a void
    /// lens. Within a couple of tiles counts as on it. True when it
    /// is too far to open, which is said.</summary>
    private bool FloatingTooFar(int x, int y)
    {
        _riding = null;
        var near = new Rectangle(x * 16 - 32, y * 16 - 32, 80, 80);
        if (TileReader.Floating(near) is not { } floating)
            return false;
        var player = Main.LocalPlayer;
        var from = player.Center;
        if (!player.IsProjectileInteractibleAndInInteractionRange(floating, ref from))
        {
            var (px, py) = WorldScanner.PlayerTile();
            Say($"Out of reach, {Directions.Offset((int)(floating.Center.X / 16f) - px, (int)(floating.Center.Y / 16f) - py)}.");
            return true;
        }
        _riding = floating;
        return false;
    }

    private void Press(ref bool button)
    {
        if (button)
            return;
        var (x, y) = Tile;
        if (System.Runtime.CompilerServices.Unsafe.AreSame(ref button, ref _right) && (ChestRefused(x, y) || PersonTooFar(x, y) || FloatingTooFar(x, y)))
            return;
        if (!InReach(x, y) && UsesReach(Main.LocalPlayer.HeldItem))
        {
            var (px, py) = WorldScanner.PlayerTile();
            Say($"Out of reach, {Directions.Offset(x - px, y - py)}.");
        }
        button = true;
        if (System.Runtime.CompilerServices.Unsafe.AreSame(ref button, ref _left))
        {
            WatchPlacing();
            if (InReach(x, y) && BucketRefusal(Main.LocalPlayer.HeldItem, x, y) is { } why)
                Say(why);
        }
    }

    /// <summary>Whether using the item acts on the tile under the
    /// cursor, so that distance matters: tools and things that are
    /// placed. A sword or a bow is aimed, not reached with.</summary>
    private static bool UsesReach(Item item) =>
        item.pick > 0 || item.axe > 0 || item.hammer > 0 || item.createTile >= 0 || item.createWall > 0;

    private void Say(string text) => _host.App.Announce(text);

    /// <summary>A job has the cursor: it is on this tile with the left
    /// button as given, saying nothing. Null gives it back, button up.</summary>
    public void Drive((int X, int Y)? tile, bool hold)
    {
        if (tile is { } at)
        {
            _x = at.X;
            _y = at.Y;
            _placed = true;
            _carried = false;
        }
        _left = tile != null && hold;
        _placeWatch = null;
    }

    /// <summary>Put the cursor on a tile without reading it.</summary>
    public void MoveSilently(int x, int y) => Place(x, y);

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
        if (_right && _riding is { active: true } riding)
            world = riding.Center;
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
