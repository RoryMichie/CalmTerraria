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
        // godmode - the test character cannot be hurt.
        autopilot.Register("godmode", _ =>
        {
            RequireTestWorld();
            Main.LocalPlayer.creativeGodMode = true;
        });
    }

    private static void RequireTestWorld()
    {
        if (!Main.worldName.StartsWith("calmtest", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("this command is for test worlds");
    }
}
