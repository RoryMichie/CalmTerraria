# Controls

Everything here is implemented.

# 1. Principle

The game keeps its own movement and action bindings; they work and are
rebindable. This document covers what the mod adds or moves.

# 2. The cursor

The cursor is a tile in the world, in place of the mouse. It is moved
from a three-by-three block under the right hand, anchored on the bump
of J:

    U I O
    J K L
    N M ,

J is the left of the middle row, so the hand finds the pad the way it
finds the home row. Columns follow the keyboard's stagger: U J N, I K M,
O L comma. The eight keys around K move the cursor one tile in their
direction, and what is under it is said as it lands.

- Shift with a direction skims: the cursor runs that way to the first
  tile that reads differently.
- K says what is under the cursor and where that is from the player.
- Ctrl+K brings the cursor back to the player.

While the cursor is within reach it travels with the player and keeps
its place relative to them: put it one tile ahead and it stays one tile
ahead. Beyond reach - after Home in the scanner, or after moving it
there - it stays on that spot in the world until it is moved back
within reach or Ctrl+K brings it home.

# 3. Clicking

Left bracket and right bracket are the two mouse buttons, as in
Factorio Access: left bracket is the left button (use the held item at
the cursor), right bracket is the right button (interact). They work
whatever the game's own mouse buttons are bound to.

Using a tool or placing something beyond the character's reach says
"Out of reach."

# 4. The scanner

Factorio Access's keys.

- Ctrl+Page Up and Ctrl+Page Down: category.
- Page Up and Page Down: kind within the category.
- Shift+Page Up and Shift+Page Down: instance of that kind, nearest
  first.
- End: refresh the scan. Shift+End: refresh, facing direction only.
- Home: move the cursor to the selected instance.

# 5. Screens

Tab moves between the parts of a screen. Escape goes back one step.

# 6. The pack screen

Escape, in a world. Five tab stops: the inventory, the equipment,
crafting, Powers, and Save and exit.

- Inventory: Enter wears what can be worn and holds anything else.
  Space always holds. Delete trashes one stack. Typing letters jumps
  to an item by name.
- Equipment: Enter takes the piece off.
- Crafting: what can be made now, from what is carried and what the
  character stands beside. Enter makes it once. Space says what it
  uses.

Opening a chest in the world (right bracket on it), or a piggy bank or
safe, brings the same screen with two tab stops in front:

- The chest's contents, as a list named for the chest. Enter takes
  all of the item.
- Chest actions: Loot all, Deposit all, Quick stack, Restock, and
  Rename for chests in the world.

While a chest is open, Enter in the inventory puts all of the item in
the chest; Space still holds. Escape closes the chest.

Holding swaps the item into the hand; the number keys still switch
between the last ten things held.

# 7. Powers

The Powers button on the pack screen: what a journey-mode character
has and more, for any character.

- Omniscience: the scanner and the cursor know the whole world, not
  only what the character has explored. Remembered between sessions.
  The scanner takes a few seconds to catch up after it is switched.
- God mode: no damage. Off again at the next launch.
- Give item: a list of every item; type the start of a name, Enter
  gives a full stack. The list stays open for more.
- Spawn creature: the same for creatures; one appears twelve tiles
  off on the side the character faces. Single player only.
- Make it day, Make it night. Single player only.

# 8. Keys taken from the game

A key the mod uses in a world is hidden from the game, so J (quick
mana) and M (the full-screen map) no longer reach it. Shift and Control
are not hidden, and the game's defaults put two things on them that
fight the mod's own Shift and Control commands. The game's bindings
should be changed to:

- Quick mana: G, since J is the cursor's.
- Auto select: F, not Left Shift. Otherwise skimming with Shift swaps
  the held item for as long as Shift is down.
- Smart cursor: V, not Left Control. Otherwise every Control command
  toggles it, and a tool stops aiming at the cursor's tile.
- The mouse buttons: the real mouse buttons. The old accessibility mod
  put them on I and P.

They live in the Custom profile of `input profiles.json` in the
tModLoader save folder, or under Controls in the game's settings. A
binding removed from that file comes back as the game's default, so
these are moved, not removed.
