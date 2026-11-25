using Srui;
using Terraria.Audio;
using Terraria.ID;

namespace CalmTerraria.Host;

/// <summary>The game's own menu tick, as its menus make it when the
/// mouse crosses an entry: once for each move to another item of a
/// list, and for each move the player makes to another stop.</summary>
internal sealed class ClickReader : IReader
{
    public void OnTick(IReadOnlyList<AccessibilityEvent> events)
    {
        foreach (var e in events)
        {
            if (e is AccessibilityEvent.ItemArrived
                or AccessibilityEvent.FocusArrived { Cause: FocusCause.UserNavigation })
            {
                SoundEngine.PlaySound(SoundID.MenuTick);
                return;
            }
        }
    }
}
