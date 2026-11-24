using CalmTerraria.Pack;
using Terraria;
using Terraria.Chat;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI.Chat;

namespace CalmTerraria.World;

/// <summary>Sends a typed message as the game's own chat box does:
/// mod commands first, then the game's commands and plain chat, to
/// the server or, alone, straight back to this player.</summary>
internal sealed class TerrariaChat : IChat
{
    /// <summary>tModLoader's own step for a line starting with a
    /// slash. It is not public, so it is found by name; if a later
    /// tModLoader moves it, the line goes on as ordinary chat.</summary>
    private static bool HandledByAMod(string text)
    {
        try
        {
            var loader = typeof(CommandLoader);
            var caller = Activator.CreateInstance(
                loader.Assembly.GetType("Terraria.ModLoader.ChatCommandCaller", throwOnError: true)!, nonPublic: true);
            var handle = loader.GetMethod("HandleCommand",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            return handle?.Invoke(null, new[] { text, caller }) is true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Send(string text)
    {
        if (text.Length > 0 && text[0] == '/' && HandledByAMod(text))
            return;
        var message = ChatManager.Commands.CreateOutgoingMessage(text);
        if (Main.netMode == NetmodeID.MultiplayerClient)
            ChatHelper.SendChatMessageFromClient(message);
        else if (Main.netMode == NetmodeID.SinglePlayer)
            ChatManager.Commands.ProcessIncomingMessage(message, Main.myPlayer);
    }
}
