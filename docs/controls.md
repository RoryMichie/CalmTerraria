# Controls

Decisions so far. Only section 6 is implemented.

# 1. Principle

The game keeps its own movement and action bindings; they work and are
rebindable. This document covers what the mod adds or moves.

# 2. The right-hand pad

A three-by-three block under the right hand, anchored on the bump of J:

    U I O
    J K L
    N M ,

J is the left of the middle row, so the hand finds the pad the way it
finds the home row. Columns follow the keyboard's stagger: U J N, I K M,
O L comma. That gives eight directions around K.

Open: what K itself does.

# 3. Clicking

Left bracket and right bracket are the two mouse buttons, as in
Factorio Access: left bracket is the left button (use), right bracket
is the right button (interact).

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

# 6. In a world

Escape opens the game menu.
