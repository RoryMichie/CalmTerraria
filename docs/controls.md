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
direction, and what is under it is said as it lands, with where that
is from the character: "Dirt, 3 right 1 down".

- Shift with a direction skims: the cursor runs that way to the first
  tile that reads differently.
- K says what is under the cursor and where that is from the player.
- Shift+K asks the game whether the room at the cursor is fit to
  live in, and reads its answer: what is missing, if anything.
- Ctrl+K brings the cursor back to the player.

While the cursor is within reach it travels with the player and keeps
its place relative to them: put it one tile ahead and it stays one tile
ahead. Beyond reach - after Home in the scanner, or after moving it
there - it stays on that spot in the world until it is moved back
within reach or Ctrl+K brings it home.

Looking along a line: Shift with W, A, S or D reads what lies that way
from the character, as runs of the same thing with how many tiles of
each - "Air, 5. Grass, 1. Dirt, 12." - out to fifty tiles. Left and
right look along the middle of the body; up starts above the head and
down below the feet. The character does not move for that press.

# 3. Clicking

Left bracket and right bracket are the two mouse buttons, as in
Factorio Access: left bracket is the left button (use the held item at
the cursor), right bracket is the right button (interact). They work
whatever the game's own mouse buttons are bound to.

Using a tool or placing something beyond the character's reach says
"Out of reach."

Building is the left bracket with a block or a wall held: it goes on
the cursor's tile. Holding the bracket and moving the cursor builds on
each tile it reaches. A placement that works makes the game's sound;
one that does not says why - "Occupied.", "Nothing to attach to.",
"Out of reach", "None left." A block cannot go where the character is
standing.

Work on an area:

- X marks one corner at the cursor, X again the other, and says the
  size. X a third time lets the area go.
- Shift+X marks the area of whatever the scanner is on: the whole
  vein of ore, the whole pool of lava, the whole run of spikes.
- With an area marked, the Tab ring leads with a Build list of what
  can be done to it with what is held:
  - a block: Fill, and Outline (the edge only: the shell of a room);
  - a wall: Wall;
  - always: Clear, using the best pickaxe and axe carried, trees
    first and from the foot; and, with a hammer carried, Remove walls;
  - an empty bucket or a sponge: Soak up the liquid;
  - a water bucket, where there is lava: Pour water on lava.
- Enter on a job reads its plan first - how many tiles, how much is
  carried, what clearing would give, what is in the way (tiles too
  hard for the pickaxe, a chest, lava) - with a Start button after.
- The work is done by using the held item on one tile after another,
  as a hand on a mouse would: it costs the blocks, takes the time and
  breaks only what the tool can. While it runs the character reaches
  anything on screen. Tiles further than that are said - "Walk
  closer: 12 left, nearest 48 right" - and it carries on as the
  character comes nearer.
- The work is called out as it goes, a run of one thing at a time:
  "There goes a tree." "Off go 13 Dirt Block." "In go 15 Stone Block."
- X stops a job. The area stays marked for the next one.
- With the scanner on something, the Build list also has Clear path
  to it, area or no area: a stair cut through the ground from the
  character to the thing, one tile along for each tile up or down so
  it can be walked without jumping, doubling back where the thing is
  straight above or below. Its plan says how long it is, what would
  come out, and what is in the way; it is cut in the order it is
  walked, and carries on as the character follows it.

# 4. The scanner

Factorio Access's keys.

- Ctrl+Page Up and Ctrl+Page Down: category.
- Page Up and Page Down: kind within the category.
- Shift+Page Up and Shift+Page Down: instance of that kind, nearest
  first.
- End: refresh the scan. Shift+End: refresh, facing direction only.
- The scan also refreshes by itself: whenever the category is
  changed, and at the first scanner key after five seconds without
  one. It never changes in the middle of a run of key presses. Home
  never refreshes; it goes to what was last read.
- Home: move the cursor to the selected instance. The character
  stays where it is.
- Ctrl+Home: move the character there - a teleport, to the nearest
  footing beside the thing - and the cursor onto it.

A chest that has been given a name is listed under Containers by that
name, as its own entry: "Ores, Chest".

Players are the scanner's second category, after waypoints: everyone
else in the world, at any distance, each under their own name.

Waypoints are the scanner's first category: places the player has
named, kept per world.

- Backslash: name the tile under the cursor. Type the name, Enter.
- Delete: remove the waypoint the scanner is on.
- Everything else is the scanner's: it says how far and which way,
  and Home puts the cursor there. Powers has Teleport to waypoint.

# 5. Screens

Tab moves between the parts of a screen. Escape goes back one step.

In a world, the world is one stop in a ring with the rest: Tab from
the world goes to the inventory and on round, Shift+Tab goes round the
other way, and tabbing past either end is back in the world. The game
runs on while this is done. Enter goes straight to the message box.
Escape opens the same ring with the game paused for as long as it is
up, in single player; a server cannot be paused.

The title menu has Single player, Join by address, New character, New
world, and Exit. A new character takes a name and a difficulty; its
looks are rolled at random. A new world takes a name (one is
suggested), a size, a difficulty, which evil, and a seed if wanted;
making it says every tenth of the way and then that it is done. Join by
address asks for a character, then an address and a port (7777 unless
changed; "host:port" typed into the address works too), starting from
the last server joined. Escape gives up on a connection that is taking
too long. A server's password is asked for when the server asks, and a
refusal is read out before the title menu returns.

# 6. The pack screen

Tab or Escape, in a world. The stops: the inventory, the equipment,
crafting, Powers, Save and exit, then the chat list and the message
box - chat is last, so from the world it is Shift+Tab away and the
things done with the hands are forwards.

- Carried (the inventory): Enter wears what can be worn and holds anything else.
  Space always holds. Delete trashes one stack. Typing letters jumps
  to an item by name. A digit, 1 to 0, puts the item in that hotbar
  slot and what was there takes its place. F1 reads the item's
  description.
- Equipment: Enter takes the piece off.
- Chat: the game's messages and what players have said, newest last.
- Message: type and press Enter to send; the box empties and stays.
  A line starting with a slash is a command, as in the game's chat.
- Crafting: what can be made now, from what is carried and what the
  character stands beside. Enter makes it once. Space says what it
  uses. F1 reads the description of what is made.

Opening a chest in the world (right bracket on it), or a piggy bank or
safe, brings the same screen with two tab stops in front:

- The chest's contents, as a list named for the chest. Enter takes
  all of the item. A digit takes it straight to that hotbar slot,
  and what was there goes to the inventory; with no room for it,
  nothing moves and that is said. F1 reads the description.
- Chest actions: Loot all, Deposit all, Quick stack, Restock, and
  Rename for chests in the world.

A flying piggy bank or a void lens, once summoned, is a container that
hangs in the air: the scanner lists it under Containers, the cursor
reads it, and right bracket within a couple of tiles of it opens it.

While a chest is open, Enter in the inventory puts all of the item in
the chest; Space still holds. Escape closes the chest.

Talking to someone (right bracket on them, from near enough) brings the
screen with what they said in front, as a stop that does nothing, and
after it a Replies list of the conversation's buttons: Shop, Quest,
Heal, Happiness and so on. Enter chooses one, and what they say next
is read. Escape ends the conversation. A sign reads the same way.

A shop puts its list in front: each item with its price, Enter buys
one and says what money is left, F1 describes. While a shop is open,
Enter in the inventory sells all of the item; the shop keeps it, to
be bought back.

Holding swaps the item into the hand; the number keys still switch
between the last ten things held.

How many: wherever more than one of something could be moved - taking
from or storing in a chest, selling, buying something that stacks,
giving from Powers, crafting something that could be made more than
once - Enter brings up a box asking how many. It opens
on the usual answer, selected: everything for a chest or a sale, one
for a purchase or a crafting, a full stack for a gift. Enter alone accepts that;
typing a number replaces it; Escape moves nothing. One of something is
moved without asking.

# 7. Status and what the game shows

- Backspace: health, mana, defense, and each effect on the character
  with the time it has left.
- The hotbar: the item is said when the selected slot changes.
- Smart cursor: said when it goes on or off.
- Picking things up: said once the pickups stop for a moment, with
  the total of each item - "Wood, 14. Acorn, 2." - and never one
  message per item.
- Footsteps: with the Terraria Ambience mod enabled, its footsteps,
  which differ by what is walked on, are brought up about ten
  decibels. Without that mod there are none.
- A chest that will not open says why: "Locked.", "Out of reach", or,
  for one another player has open, the pack screen with "Chest, in
  use by" and their name where the chest's list would be.
- Lava or spikes close by are said once as they come within a few
  tiles - "Lava, 3 right 1 down." - and again only after being clear
  of them. The scanner has a Hazards category: lava in any amount,
  spikes, traps, boulders, explosives, pressure plates.
- Low health is a heartbeat: under three tenths of full health, two
  soft thumps, quicker the less is left.
- Jumping and landing each have a sound; the landing is louder the
  faster the fall.
- The game's own messages - deaths, events, chat - are spoken as they
  appear. Dying says how long until the respawn; coming back says so.

# 8. Powers

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
- Teleport to player: a list of the other players; Enter goes to
  that one, the way a wormhole potion does.
- Teleport to waypoint: the same for the world's waypoints.
- Make it day, Make it night. Single player only.

# 9. Keys taken from the game

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
