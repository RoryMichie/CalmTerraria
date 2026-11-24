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

The cursor stays where it is put while the player walks.

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

Escape, in a world. Three tab stops: the inventory, the equipment, and
Save and exit.

- Inventory: Enter wears what can be worn and holds anything else.
  Space always holds. Delete trashes one stack. Typing letters jumps
  to an item by name.
- Equipment: Enter takes the piece off.

Holding swaps the item into the hand; the number keys still switch
between the last ten things held.

# 7. Keys taken from the game

A key the mod uses in a world is hidden from the game. With the game's
default bindings that costs two things: J was quick mana, and M was the
full-screen map. Quick mana needs a new home.

The old accessibility mod left its own bindings in the game's input
profile: the game's left and right mouse buttons are on I and P there.
CalmTerraria does not depend on that, and I is the cursor's.
