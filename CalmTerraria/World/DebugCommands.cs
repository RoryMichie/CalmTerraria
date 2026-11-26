using CalmTerraria.Host;
using log4net;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace CalmTerraria.World;

/// <summary>Things an autopilot script can do to a test world to set
/// up what it wants to check. They refuse to run anywhere else.</summary>
internal static class DebugCommands
{
    /// <summary>The tile of the last thing a command placed.</summary>
    public static (int X, int Y) LastPlaced { get; private set; }

    public static void Register(Autopilot autopilot, ILog log)
    {
        // Under the autopilot only: how long each step of making a
        // world takes, for when the question is where the time goes.
        Terraria.WorldBuilding.On_GenPass.Apply += (orig, self, progress, configuration) =>
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            orig(self, progress, configuration);
            log.Info($"GenPass: {clock.ElapsedMilliseconds} ms {self.Name}");
        };

        // spawn Zombie 12 0 - an NPC by its internal name, that many
        // tiles right of and below the player.
        autopilot.Register("spawn", args =>
        {
            RequireTestWorld();
            var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var type = NPCID.Search.GetId(parts[0]);
            var player = Main.LocalPlayer;
            var x = (int)player.Center.X + int.Parse(parts[1]) * 16;
            var y = (int)player.Bottom.Y + int.Parse(parts[2]) * 16;
            var index = NPC.NewNPC(new EntitySource_DebugCommand("CalmTerraria autopilot"), x, y, type);
            log.Info($"Debug: spawned {Main.npc[index].TypeName} as {index}.");
        });
        // time night | time day
        autopilot.Register("time", args =>
        {
            RequireTestWorld();
            Main.dayTime = args.Trim() == "day";
            Main.time = 0;
        });
        // clear - remove every hostile NPC.
        autopilot.Register("clear", _ =>
        {
            RequireTestWorld();
            foreach (var npc in Main.ActiveNPCs)
            {
                if (!npc.friendly && !npc.townNPC)
                    npc.active = false;
            }
        });
        // placechest - a chest standing on the nearest clear ground to
        // the player's right, and the player moved beside it. Where it
        // went is kept in LastPlaced.
        autopilot.Register("placechest", _ =>
        {
            RequireTestWorld();
            var (px, py) = WorldScanner.PlayerTile();
            for (var dx = 2; dx < 60; dx++)
            {
                for (var dy = -6; dy <= 6; dy++)
                {
                    var index = WorldGen.PlaceChest(px + dx, py + dy);
                    if (index < 0)
                        continue;
                    LastPlaced = (px + dx, py + dy);
                    // Stand the character beside it, within reach.
                    var player = Main.LocalPlayer;
                    player.Bottom = new Microsoft.Xna.Framework.Vector2((px + dx - 1) * 16f, (py + dy + 1) * 16f);
                    player.velocity = Microsoft.Xna.Framework.Vector2.Zero;
                    log.Info($"Debug: chest {index} at {LastPlaced}, player {px},{py}.");
                    return;
                }
            }
            throw new InvalidOperationException("nowhere to stand a chest");
        });
        // ambience - what Terraria Ambience's footsteps think is going on.
        autopilot.Register("ambience", _ =>
        {
            if (!Terraria.ModLoader.ModLoader.TryGetMod("TerrariaAmbienceAPI", out var api))
            {
                log.Info("Ambience: the API mod is not loaded.");
                return;
            }
            var code = api.Code;
            var checker = code.GetTypes().First(t => t.Name == "PlayerTileChecker");
            var sound = code.GetTypes().First(t => t.Name == "FootstepSound");
            var sounds = (System.Collections.IEnumerable)sound.GetField("FootstepSounds")!.GetValue(null)!;
            var lines = new List<string>();
            foreach (var one in sounds)
            {
                object? Get(string name) => sound.GetProperty(name)?.GetValue(one) ?? sound.GetField(name)?.GetValue(one);
                var tiles = Get("TileIds") as System.Collections.ICollection;
                var styles = Get("SoundStyles") as Array;
                lines.Add($"{Get("Name")} handle {Get("HandleByDefault")} tiles {tiles?.Count} styles {styles?.Length} step {Get("StepVolume")} mult {Get("VolumeMultiplier")} any {Get("UseAnyTile")}");
            }
            log.Info($"Ambience: tile {checker.GetProperty("TileId")!.GetValue(null)} should {checker.GetProperty("ShouldStep")!.GetValue(null)}; " + string.Join("; ", lines));
        });
        // clock - the world's time, to see whether it is running.
        autopilot.Register("clock", _ => log.Info($"Clock: {Main.time:0} paused {Main.gamePaused}"));
        // talk - whether the game thinks a conversation is open.
        autopilot.Register("talk", _ => log.Info($"Talk: npc {Main.LocalPlayer.talkNPC} sign {Main.LocalPlayer.sign} text '{Main.npcChatText}' shop {Main.npcShop} "
            + $"mouseRight {Main.mouseRight} release {Main.mouseRightRelease} chatRelease {Main.npcChatRelease} inventory {Main.playerInventory} mouse {Main.mouseX},{Main.mouseY}"));
        // clearing - nothing but air around the player, down to the feet.
        autopilot.Register("clearing", _ =>
        {
            RequireTestWorld();
            var (px, py) = WorldScanner.PlayerTile();
            for (var x = px - 6; x <= px + 6; x++)
                for (var y = py - 8; y <= py + 1; y++)
                    WorldGen.KillTile(x, y, noItem: true);
        });
        // hold LavaBucket 5 - that many of an item, by internal name, in the hand.
        autopilot.Register("hold", args =>
        {
            RequireTestWorld();
            var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var player = Main.LocalPlayer;
            if (player.selectedItem >= 10)
                player.selectedItem = 0;
            var item = new Item();
            item.SetDefaults(ItemID.Search.GetId(parts[0]));
            item.stack = parts.Length > 1 ? int.Parse(parts[1]) : 1;
            player.inventory[player.selectedItem] = item;
            log.Info($"Debug: holding {item.Name}, {item.stack}.");
        });
        // lava - how much lava is near the player.
        autopilot.Register("lava", _ =>
        {
            var (px, py) = WorldScanner.PlayerTile();
            int lava = 0, water = 0, obsidian = 0;
            for (var x = px - 20; x <= px + 20; x++)
                for (var y = py - 12; y <= py + 12; y++)
                {
                    var tile = Main.tile[x, y];
                    if (tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Lava) lava++;
                    if (tile.LiquidAmount > 0 && tile.LiquidType == LiquidID.Water) water++;
                    if (tile.HasTile && tile.TileType == TileID.Obsidian) obsidian++;
                }
            log.Info($"Lava: {lava} lava, {water} water, {obsidian} obsidian near the player.");
        });
        // health 20 - the test character's health, set.
        autopilot.Register("health", args =>
        {
            RequireTestWorld();
            Main.LocalPlayer.statLife = int.Parse(args.Trim());
        });
        // recall - the test character back at the world's spawn point.
        autopilot.Register("recall", _ =>
        {
            RequireTestWorld();
            Main.LocalPlayer.Spawn(PlayerSpawnContext.RecallFromItem);
        });
        // arena - a clean place to test: high above the spawn point, a
        // flat stone floor sixty tiles wide with nothing but air over
        // it, and the character stood in the middle of it. The ground
        // near spawn is dug to pieces by earlier tests.
        autopilot.Register("arena", _ =>
        {
            RequireTestWorld();
            var cx = Main.spawnTileX;
            var floor = Math.Max(60, Main.spawnTileY - 120);
            for (var x = cx - 30; x <= cx + 30; x++)
            {
                for (var y = floor - 30; y < floor; y++)
                {
                    var tile = Main.tile[x, y];
                    tile.ClearEverything();
                }
                for (var y = floor; y <= floor + 1; y++)
                {
                    Main.tile[x, y].ClearEverything();
                    WorldGen.PlaceTile(x, y, TileID.Stone, mute: true, forced: true);
                }
            }
            var player = Main.LocalPlayer;
            player.Bottom = new Microsoft.Xna.Framework.Vector2(cx * 16f + 8f, floor * 16f);
            player.velocity = Microsoft.Xna.Framework.Vector2.Zero;
            player.fallStart = floor;
            log.Info($"Debug: arena floor at {floor}, centre {cx}.");
        });
        // pool - on the arena, a stone basin of water to the right of
        // the character, big enough to fish in.
        autopilot.Register("pool", _ =>
        {
            RequireTestWorld();
            var (px, py) = WorldScanner.PlayerTile();
            var floor = py + 2;
            for (var x = px + 3; x <= px + 26; x++)
            {
                for (var y = floor; y <= floor + 8; y++)
                {
                    var tile = Main.tile[x, y];
                    tile.ClearEverything();
                    var wall = x == px + 3 || x == px + 26 || y == floor + 8;
                    if (wall)
                        WorldGen.PlaceTile(x, y, TileID.Stone, mute: true, forced: true);
                    else
                    {
                        tile.LiquidAmount = 255;
                        tile.LiquidType = LiquidID.Water;
                    }
                }
            }
            log.Info($"Debug: pool from {px + 4} to {px + 25}, surface at {floor}.");
        });
        // drop Wood 5 8 - that many of an item lying that many tiles to the right.
        autopilot.Register("drop", args =>
        {
            RequireTestWorld();
            var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var player = Main.LocalPlayer;
            Item.NewItem(new EntitySource_DebugCommand("CalmTerraria autopilot"),
                (int)player.Center.X + int.Parse(parts[2]) * 16, (int)player.Center.Y, 16, 16,
                ItemID.Search.GetId(parts[0]), int.Parse(parts[1]));
        });
        // items - everything lying about near the player, and whether it could be picked up.
        autopilot.Register("items", _ =>
        {
            var player = Main.LocalPlayer;
            var lines = new List<string>();
            foreach (var item in Main.ActiveItems)
            {
                if (Microsoft.Xna.Framework.Vector2.Distance(item.Center, player.Center) > 60 * 16)
                    continue;
                lines.Add($"{item.Name} x{item.stack} at {(int)(item.Center.X / 16f)},{(int)(item.Center.Y / 16f)} delay {item.noGrabDelay} "
                    + $"for {item.playerIndexTheItemIsReservedFor} space {player.ItemSpace(item).CanTakeItem} touch {player.Hitbox.Intersects(item.Hitbox)} can {Terraria.ModLoader.ItemLoader.CanPickup(item, player)} accept {player.CanAcceptItemIntoInventory(item)} shimmer {item.shimmerTime}/{item.shimmered} grabbed {item.beingGrabbed} vel {item.velocity.X:0.0},{item.velocity.Y:0.0} box {item.Hitbox}");
            }
            var (px, py) = WorldScanner.PlayerTile();
            log.Info($"Items: player {px},{py} me {Main.myPlayer} selected {player.selectedItem} '{player.HeldItem.Name}' anim {player.itemAnimation}: " + string.Join("; ", lines));
        });
        // hooks - the player's grappling hooks in flight, and which way they go.
        autopilot.Register("hooks", _ =>
        {
            var lines = new List<string>();
            foreach (var projectile in Main.ActiveProjectiles)
                if (projectile.owner == Main.myPlayer && projectile.aiStyle == 7)
                    lines.Add($"{projectile.Name} velocity {projectile.velocity.X:0.0},{projectile.velocity.Y:0.0}");
            log.Info("Hooks: " + (lines.Count == 0 ? "none" : string.Join("; ", lines)));
        });
        // pockmark - the arena's floor to the right of the character, dug
        // into holes with lumps left standing on it.
        autopilot.Register("pockmark", _ =>
        {
            RequireTestWorld();
            var (px, py) = WorldScanner.PlayerTile();
            var floor = py + 2;
            for (var x = px + 3; x <= px + 18; x++)
            {
                if (x % 3 == 0)
                    for (var y = floor; y <= floor + 1; y++)
                        Main.tile[x, y].ClearEverything();
                if (x % 4 == 0)
                    for (var y = floor - 2; y < floor; y++)
                        WorldGen.PlaceTile(x, y, TileID.Dirt, mute: true, forced: true);
            }
            log.Info($"Debug: pockmarked {px + 3}..{px + 18}, floor at {floor}.");
        });
        // ledge - a step three tiles high just left of the character,
        // and a wall too tall to jump a little further right.
        autopilot.Register("ledge", _ =>
        {
            RequireTestWorld();
            var (px, py) = WorldScanner.PlayerTile();
            var floor = py + 2;
            for (var x = px - 6; x <= px - 2; x++)
                for (var y = floor - 3; y < floor; y++)
                    WorldGen.PlaceTile(x, y, TileID.Stone, mute: true, forced: true);
            for (var y = floor - 12; y < floor; y++)
                WorldGen.PlaceTile(px + 8, y, TileID.Stone, mute: true, forced: true);
            log.Info($"Debug: ledge at {px - 6}..{px - 2}, wall at {px + 8}, floor at {floor}.");
        });
        // where - the character's tile, and whether they are on the ground.
        autopilot.Register("where", _ =>
        {
            var (px, py) = WorldScanner.PlayerTile();
            log.Info($"Where: {px}, {py}, {(Main.LocalPlayer.velocity.Y == 0f ? "standing" : "in the air")}.");
        });
        // ground - how flat that stretch is now: open tiles in the floor, solid ones above it.
        autopilot.Register("ground", _ =>
        {
            var cx = Main.spawnTileX;
            var floor = Math.Max(60, Main.spawnTileY - 120);
            int holes = 0, lumps = 0;
            for (var x = cx + 3; x <= cx + 18; x++)
            {
                if (!Main.tile[x, floor].HasTile)
                    holes++;
                for (var y = floor - 4; y < floor; y++)
                    if (Main.tile[x, y].HasTile)
                        lumps++;
            }
            log.Info($"Ground: {holes} holes in the floor, {lumps} tiles standing on it.");
        });
        // inv - where things are: the hand, the mouse, and the first slots.
        autopilot.Register("inv", _ =>
        {
            var player = Main.LocalPlayer;
            var slots = new List<string>();
            for (var i = 0; i < 58; i++)
                if (!player.inventory[i].IsAir)
                    slots.Add($"{i}:{player.inventory[i].Name}x{player.inventory[i].stack}");
            var (px, py) = WorldScanner.PlayerTile();
            var near = new List<string>();
            for (var x = px - 4; x <= px + 4; x++)
                for (var y = py - 3; y <= py + 2; y++)
                    if (Main.tile[x, y].HasTile && Main.tileFrameImportant[Main.tile[x, y].TileType])
                        near.Add($"{TileNames.At(x, y, Main.tile[x, y])}@{x - px},{y - py}");
            log.Info($"Inv: selected {player.selectedItem}, mouse '{Main.mouseItem.Name}'x{Main.mouseItem.stack}, objects near [{string.Join(" ", near.Distinct())}], slots {string.Join(" ", slots)}");
        });
        // fill - every empty inventory slot of the test character filled
        // with one of something different, so there is no room for more.
        autopilot.Register("fill", _ =>
        {
            RequireTestWorld();
            var inventory = Main.LocalPlayer.inventory;
            var type = ItemID.IronPickaxe;
            for (var slot = 0; slot < 50; slot++)
            {
                if (!inventory[slot].IsAir)
                    continue;
                inventory[slot] = new Item();
                inventory[slot].SetDefaults(type++);
            }
        });
        // die - the test character is killed.
        autopilot.Register("die", _ =>
        {
            RequireTestWorld();
            TerrariaPowers.GodModeOn = false;
            Main.LocalPlayer.KillMe(PlayerDeathReason.ByCustomReason("calmtest was slain by a test."), 9999, 0);
        });
        // godmode - the test character cannot be hurt.
        autopilot.Register("godmode", _ =>
        {
            RequireTestWorld();
            TerrariaPowers.GodModeOn = true;
        });
    }

    private static void RequireTestWorld()
    {
        if (!Main.worldName.StartsWith("calmtest", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("this command is for test worlds");
    }
}
