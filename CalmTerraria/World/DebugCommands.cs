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
