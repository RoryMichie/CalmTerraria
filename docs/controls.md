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
  On the character it says so - "Your head", "Your body", "Your
  legs" - and then what they are standing in, if it is not air.
  Another player reads by name.
- Shift+K asks the game whether the room at the cursor is fit to
  live in, and reads its answer: what is missing, if anything. Where
  the game says only that it is not valid housing, the reason is
  said: how many tiles it has when it is too small; where the gap is
  when it is not closed in, with the cursor put on the gap; and, for
  a room that is whole and furnished, why nobody can stand in it - a
  floor of platforms, a door within two tiles of the only place to
  stand, furniture on that place.- Ctrl+K brings the cursor back to the player.

While the cursor is within reach it goes with the player. With the
smart cursor off it clings: a step brings it back onto the character.
Holding a bracket keeps it where it is, so a run of blocks can be laid
while walking.

Digging in a direction: with the smart cursor on (V), the keys the
character is moved with point the cursor as well: S is down, A and S
together down and left, and so on, with nothing said. A direction key
of the cursor's points too, and does not step. The cursor goes two tiles from the character
that way, whatever it was on before, and stays that way from them as
they move; the direction is said, with the tile the game has picked to
work on - "Left: Dirt, 1 left". Holding the left bracket with a
pickaxe then digs that way, and the game chooses the tiles: level, a
tunnel the character can walk through as they go; up or down, a
shaft; the diagonals, a stair. An axe, a hammer and blocks to place
are pointed the same way. K still reads the cursor's own tile and
Ctrl+K brings it back onto the character. A cursor sent out of reach,
by the scanner or by skimming, steps tile by tile as ever.

The grappling hook, on the game's own key (E), goes to the cursor: put
the cursor where the hook should catch. With the cursor on the
character it goes straight up. Jump lets go. Beyond reach - after Home in the scanner, or after moving it
there - it stays on that spot in the world until it is moved back
within reach or Ctrl+K brings it home.

Looking along a line: Shift with W, A, S or D reads what lies that way
from the character, as runs of the same thing with how many tiles of
each - "Air, 5. Grass, 1. Dirt, 12." - out to fifty tiles. Left and
right look along the middle of the body; up starts above the head and
down below the feet. The character does not move for that press.

What the cursor and the scanner know is what is on the game's map,
and the game maps what has been lit on screen. So that the dark is
not a thing to be managed unseen, the character is taken to carry a
light: what it would show goes on the map as they move, and stays
there. It reaches about twelve tiles through open space and about
four into solid ground, as light does, so ore is known when it is
near open space the character has come close to, and not through a
thickness of rock. Anything else unlit reads "Unknown". The
Omniscience power does away with all of it.

# 3. Clicking

Left bracket and right bracket are the two mouse buttons, as in
Factorio Access: left bracket is the left button (use the held item at
the cursor), right bracket is the right button (interact). They work
whatever the game's own mouse buttons are bound to.

Using a tool or placing something beyond the character's reach says
"Out of reach."

Fighting is the left bracket with a weapon held. While it is held and
an enemy is on screen, the weapon is aimed at that enemy and not at
the cursor: the nearest in plain sight, said by name as it is picked,
and the same one until it is gone. A yoyo flies to it and stays on it,
arrows and spells leave towards it, a sword swings to its side. With
no enemy about, the weapon goes to the cursor as before.

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
  - a block: also Level - flat ground along the bottom of the area.
    Everything inside it is cleared and every hole under it, down to
    eight tiles, is filled, for ground that has been dug to pieces;
  - a wall: Wall;
  - always: Clear, using the best pickaxe and axe carried, trees
    first and from the foot; and, with a hammer carried, Remove walls;
  - an empty bucket or a sponge: Soak up the liquid;
  - a water bucket, where there is lava: Pour water on lava.
- Each job in the list reads what it takes after its name - how much
  is carried and how much short, what clearing gives, what is in the
  way (tiles too hard for the pickaxe, a chest, lava). Enter starts
  it. The list follows what is held and what the world has become
  for as long as it is open.
- The work is done by using the held item on one tile after another,
  as a hand on a mouse would: it costs the blocks, takes the time and
  breaks only what the tool can. While it runs the character reaches
  anything on screen. Tiles further than that are said - "Walk
  closer: 12 left, nearest 48 right" - and it carries on as the
  character comes nearer.
- The work is called out as it goes, a run of one thing at a time:
  "Tree felled." "Cleared 13 Dirt Block." "Placed 15 Stone Block."
- The job walks the character itself: aside, when a block is to go
  where they stand, and closer, when what is left is out of reach. It
  walks as a player would, jumping a step, and will not walk into lava
  or off a drop. It says nothing while it can do this itself; where it
  cannot get to, it says what it wanted and
  leaves the walking to the player.
- Filling and outlining clear their own way first: trees, plants and
  loose things where the blocks are to go are cut down or dug out,
  then the blocks go in. Solid ground already there is left, being as
  good as a wall, and chests are never touched.
- After clearing or mining, what was dug or cut down is brought to
  the character from wherever the work was, and picked up. What the
  inventory has no room for is counted and left.
- The reach of a job is what is on screen, so the view is zoomed
  right out for as long as it runs. Walking is only ever sideways:
  what is too high or too far down is said as that, and the job
  carries on once the character is higher or lower. A job that
  leaves the character away from the work says where the work is.
- X stops a job. The area stays marked for the next one.
- With something from the scanner marked by Shift+X that is made of
  one kind of tile - ore, gems, spikes - the Build list leads with
  Mine: the vein miner. Every tile of it that touches, and nothing
  round it, with the character's own pickaxe. The line says how many
  tiles, how far, and how many are too hard.
- With anything marked - one tile after a first X, an area after a
  second, or something from the scanner after Shift+X - the Build
  list also has Clear path to it: a stair cut through the ground from
  the character to the mark, one tile along for each tile up or down so
  it can be walked without jumping, doubling back where the thing is
  straight above or below. Its line says how long it is, what would
  come out, and what is in the way; it is cut in the order it is
  walked, and carries on as the character follows it.

# 4. The scanner

Factorio Access's keys.

- Ctrl+Page Up and Ctrl+Page Down: category.
- Page Up and Page Down: kind within the category.
- Shift+Page Up and Shift+Page Down: instance of that kind, nearest
  first.
- End: refresh the scan. Shift+End: refresh, facing direction only.
- Whenever the scan is taken, by End or by itself, the world for
  160 tiles to either side and 100 above and below is looked at
  afresh on the spot: what has just been dug, poured on or built is
  already in it. Further off, it is as the background survey last
  found it, which can be a few seconds old.
- The scan also refreshes by itself: whenever the category is
  changed, and at the first scanner key after five seconds without
  one, or when something in it has gone - an item picked up, an enemy
  killed. It never otherwise changes in the middle of a run of key
  presses. Home
  never refreshes; it goes to what was last read.
- Home: move the cursor to the selected instance. The character
  stays where it is.
- Ctrl+Home: move the character there - a teleport - and the cursor
  onto it. The character is stood on the thing where there is room,
  so an item on the ground is picked up; where there is not - ore
  inside rock - on the nearest footing, with how far the thing is
  from there said. Never inside the ground. Something that has gone
  since the scan is said to be gone.

Critters - birds, bunnies, worms, fireflies, whatever a bug net
catches - have their own category, after Enemies, for those near
enough to be on screen.

Buildings are under Places: anything made of worked wood or brick, or
standing in front of a wall somebody put up. That is the player's own
building, and the cabins and ruins the world came with.

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
Moving to another item in a list, or to another part of a screen,
makes the game's own menu tick.

In a world, the world is one stop in a ring with the rest: Tab from
the world goes to the inventory and on round, Shift+Tab goes round the
other way, and tabbing past either end is back in the world. The game
runs on while this is done. Enter goes straight to the message box.
Escape opens the same ring with the game paused for as long as it is
up, in single player; a server cannot be paused.

The title menu has Single player, Join by address, New character, New
world, Settings, and Exit. A new character takes a name and a difficulty; its
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
crafting, Powers, Settings, Save and exit, then the chat list and the message
box - chat is last, so from the world it is Shift+Tab away and the
things done with the hands are forwards.

Alt with a letter goes straight to a stop, from the world - which
brings the screen up on it - and from anywhere on the screen:

- Alt+G: the world (the game).
- Alt+I: the inventory.
- Alt+S: storage. Alt+B: the build list. Each says so when there is
  none.
- Alt+E: equipment. Alt+C: crafting.
- Alt+P: Powers. Alt+O: Settings (options).
- Alt+H: the chat list (history). Alt+M: the message box.

A stop that has one says its key after it is read - "Crafting list
Torch, 3. Provides light alt c" - unless that is switched off in
Settings. These are the same in the classic key layout.

- Carried (the inventory): alphabetical, with what is in the game's
  coin and ammunition slots kept apart at the end and marked -
  "Silver Coin, 40, coin slot", "Wooden Arrow, 99, ammo slot". Arrows
  in the main inventory are their own line, in their place in the
  alphabet. Enter wears what can be worn and holds anything else.
  Space always holds. Delete trashes one stack. Typing letters jumps
  to an item by name. A digit, 1 to 0, puts the item in that hotbar
  slot and what was there takes its place. F1 reads the item's
  description. Left bracket uses one - drinks it, eats it, throws it
  - without its being held: it is in the hand only for as long as
  the use takes. Right bracket opens what opens: a bag, a crate.
  Using needs the game to be running, so it works from Tab and not
  from the paused screen behind Escape.
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
  nothing moves and that is said. F1 reads the description. The
  brackets work here too: one of the item comes out to be used or
  opened.
- Chest actions: Loot all, Deposit all, Quick stack, Restock, and
  Rename for chests in the world.

Storage: with chests near the character - within 50 tiles to either
side and 30 above or below, about what is on screen - and nothing
else open, the screen has two more stops after Carried, and every one
of those chests is treated as a single container. Locked chests are
left out.

- Storage, named with how many chests it is: everything in all of
  them as one alphabetical list, the same item in several chests as
  one line with its total. The keys are a chest's: Enter takes the
  item, asking how many; a digit takes it to that hotbar slot; F1
  describes; the brackets take one out to use or open. Typing letters
  jumps to an item by name.
- Storage actions: Quick stack (puts away only what storage already
  has some of), Deposit all, and Restock. Deposit all and Quick stack
  leave the hotbar and favourites alone.
- In Carried, Shift+Enter puts the item away, asking how many. It
  goes onto a stack of the same thing where there is one, then into
  a chest that already holds that thing, then into the nearest chest
  with room.
- Crafting counts what is in storage as it counts what is carried,
  and takes what it uses from the chests.

On a server, storage works the same, with these differences:

- A server says what is in a chest only when it is opened, so as the
  screen comes up each chest in reach is opened and shut again at
  once, from where the character stands. The list fills as the
  answers arrive, which is a moment. Nothing of that is heard here;
  other players see the lids move.
- A chest another player has open is left out until they shut it.
- Chests rigged to a trap are left out: asking about one would set
  the trap off.
- Crafting counts storage only while this screen is up without a
  chest, a shop or a conversation in front.
- Two players with this mod can both use the same chests: each is
  told when the other changes one, and looks again.

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
be bought back. Each line of the inventory then says what the shop
gives for it, before its description - "Wood, 300. Sells for 1
copper each. Material" - or "Worth nothing."

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

Descriptions: in every list of items - carried, a chest, a shop,
crafting - landing on an item reads it and then the game's own
description of it: "Torch, 286. Can be placed. Material. Provides
light". F1 reads the description again.

# 7. Status and what the game shows

- Backspace: health, mana, defense, and each effect on the character
  with the time it has left. With three or fewer inventory slots
  free it says how many, or that the inventory is full.
- Shift+Backspace: the fight. Each boss about, with its health as a
  number and in hundredths - a boss in several parts is all its parts
  together - and the event under way: an invasion with how far
  through it is, a moon or the Old One's Army with its wave, a slime
  rain with how many are killed, a blood moon or an eclipse by name.
  "No boss, no event." otherwise.
- Unasked, a boss's health is said as it falls past three quarters,
  a half, a quarter and a tenth - "Eye of Cthulhu, 50 percent." -
  and an invasion's progress at each quarter.
- A full inventory: the Carried list is named "Carried, full";
  walking over something there is no room for says "No room for"
  it, once; and nothing is crafted that would have nowhere to go.
  Under the cursor, something lying loose reads with "dropped"
  after it, so it is not taken for the thing standing there.
- Z: where the character is, in a few words. The biome and the layer
  of the world; how far the biome runs to either side, where the
  scanner has it as a place; then a room with its size and what it is built of, or
  what is overhead, or "Open sky."; then, outside a room, what the
  ground does to each side as it would be walked - "Left: flat 4,
  then up 2. Right: flat 12, then a drop." A side that is level for
  thirty tiles is "flat". A room says how many of its tiles have no
  back wall only when some have none.
- C: the character's coordinates, in tiles - "2140, 312." The
  first counts from the world's left edge and the second from its
  top, so the second grows going down.- Apostrophe: what the character's informational accessories show -
  the time, the depth, the position and so on - in the game's own
  words, for those the character has.
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
- The character's own yoyo wavers softly from where it is, for as
  long as it is out; so does a flail on its chain, and a chained
  blade or harpoon, so the arc of the swing is heard. Things flying at the character - arrows,
  fireballs - make a higher, quicker waver from where they are, the
  nearest four of them. Both are to the side they are on, higher in
  pitch above the character and lower below, louder the nearer.
- Walking into a wall is a short knock from the side the wall is on,
  once for each time it is walked into.
- A fish on the line is two quick rising notes, on top of the game's
  own splash as the float dips. Left bracket then reels it in.
- Falling is a whistle for as long as it lasts, sinking in pitch as
  the fall goes on. Once the fall is long enough to hurt at the
  bottom, the whistle flutters.
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
- Omnipresent storage: storage is every unlocked chest in the world,
  wherever the character is, not only the chests near them. Taking,
  putting away and crafting all reach it. Remembered between
  sessions. On a server it is every chest in the parts of the world
  this computer has been sent, which is where the character has been.
- Give item: a list of every item; type the start of a name, Enter
  gives a full stack. The list stays open for more.
- Spawn creature: the same for creatures; one appears twelve tiles
  off on the side the character faces. Single player only.
- Teleport to player: a list of the other players; Enter goes to
  that one, the way a wormhole potion does.
- Teleport to waypoint: the same for the world's waypoints.
- Make it day, Make it night. Single player only.

# 9. Settings

The Settings button on the pack screen, after Powers, and Settings on
the title menu. Two tabs; left and right arrow change tab, Tab goes
into it. Everything takes effect as it is changed, and is kept.

Mod:

- Classic keys: the key layout of the earlier accessibility mod, for
  hands that know it. See section 10.
- Menu tick: the game's tick on moving through a list. On unless
  switched off.
- Say shortcut keys: a stop with an Alt key says it after it is read.
  On unless switched off.
- Say roles: what kind of thing a stop is - "list", "button", "check
  box" - is said with it. On unless switched off.

Game:

- Sound volume, Music volume, Ambient volume: arrows move by 5, Page
  Up and Page Down by 20, Home and End go to the ends.
- Autosave.

# 10. Classic keys

With Classic keys on, in a world:

- O, K, L and semicolon move the cursor up, left, down and right.
  There are no diagonals. Shift with one of them skims.
- Comma says what is under the cursor; Shift+comma asks about housing;
  Ctrl+comma brings the cursor back to the player.
- I uses the held item at the cursor and P interacts.
- The scanner: left and right bracket change category. Page Up and
  Page Down go from one thing to the next, nearest first. Shift+Page
  Up and Shift+Page Down go from one kind of thing to the next. Home
  teleports the character to the selected thing, and Ctrl+Home only
  moves the cursor there: the other way round from section 4.
- J is the game's quick mana again, and H its quick heal as always.
  Switching the layout moves the game's quick mana binding between G
  and J by itself, where the player has not put it somewhere else.
- U, N and M are the game's: M is its full-screen map.

Everything else is as in the rest of this document: X,
Backspace, Z, apostrophe, backslash, Tab. Inside the mod's screens
nothing changes, since letters there find things by name: using an
item from a list is still the brackets.

# 11. Keys taken from the game

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
