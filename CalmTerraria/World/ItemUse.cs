using Terraria;
using Terraria.GameInput;

namespace CalmTerraria.World;

/// <summary>Using one of something from the inventory without holding
/// it. The game has no such thing for most items - it uses what is in
/// the hand, when the mouse is clicked - so that is what is done, as
/// briefly as the game allows: the item changes places with what is
/// held, the mouse is clicked until the use begins, and when the use
/// has finished the two change back.</summary>
internal static class ItemUse
{
    private enum Phase { Idle, Starting, Using }

    private static Phase _phase;
    private static int _slot, _hand, _frames;
    private static Action<string>? _say;

    public static bool Active => _phase != Phase.Idle;

    public static bool Begin(int slot, Action<string> say)
    {
        if (Active)
            return false;
        var player = Main.LocalPlayer;
        if (player.selectedItem >= 10)
            player.selectedItem = 0;
        _slot = slot;
        _hand = player.selectedItem;
        _say = say;
        if (_slot != _hand)
            Utils.Swap(ref player.inventory[_slot], ref player.inventory[_hand]);
        _phase = Phase.Starting;
        _frames = 40;
        return true;
    }

    /// <summary>As the game turns keys into controls: the click.</summary>
    public static void Inject()
    {
        if (_phase == Phase.Starting)
            PlayerInput.Triggers.Current.MouseLeft = true;
    }

    /// <summary>Once a frame in a world.</summary>
    public static void Update()
    {
        if (_phase == Phase.Idle)
            return;
        var player = Main.LocalPlayer;
        if (_phase == Phase.Starting)
        {
            if (player.itemAnimation > 0)
                _phase = Phase.Using;
            else if (--_frames <= 0 || player.dead)
            {
                // The game would not: on cooldown, nothing to use it
                // on, or not a thing that is used this way.
                Restore();
                _say?.Invoke("Couldn't use it.");
            }
            return;
        }
        if (player.itemAnimation == 0 || player.dead)
            Restore();
    }

    private static void Restore()
    {
        var inventory = Main.LocalPlayer.inventory;
        if (_slot != _hand)
            Utils.Swap(ref inventory[_slot], ref inventory[_hand]);
        _phase = Phase.Idle;
        Recipe.FindRecipes();
    }
}
