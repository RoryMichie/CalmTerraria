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
