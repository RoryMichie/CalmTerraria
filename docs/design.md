# Design

What CalmTerraria is building and why. Decisions, not a schedule.

# 1. The aim

Information parity. A sighted player learns the world from the screen,
the minimap, and the map; the old accessibility mod narrated the
game's menus well enough and gave almost none of that. Players went
months without knowing chests existed on the map. Everything the
screen and the map tell a sighted player gets a deliberate answer for
how it reaches a blind one.

# 2. Shape

Four layers.

1. **Host.** SRUI inside the game. Knows nothing about Terraria.
2. **Game facade.** A thin typed surface over the game, written from
   the decompiled game. The only place that touches game internals.
3. **Screens.** SRUI widgets over the facade. The game's own interface
   is not driven or narrated: the mod has its own, and calls what the
   game's buttons call.
4. **World sense.** The scanner, the cursor, and positional audio.

The game keeps its own movement and action bindings.

# 3. The scanner

The model is Factorio Access's, taken whole; its machinery is not,
being mostly answers to Lua and to Factorio.

- Three levels: category, kind, instance. Ores, iron, this vein.
  Instances are sorted by distance.
- The list is a snapshot. It changes when the player refreshes it, so
  nothing moves under the cursor.
- It reports only what the game's map has revealed, which is exactly
  what a sighted player's map shows. No light or screen-size rules.
- Names come from the map's own legend.
- Things that are many tiles are one entry: a vein, a pool, an ocean
  with its shore, an island.
- One small backend per kind of thing: how it groups, how it reads,
  whether it is still there.

Whatever the map can tell is a candidate: containers, ores, gems, life
and mana pickups, stations and altars, plants, liquids and their
shores, islands, structures. Live things - NPCs, enemies, dropped
items - come after.

# 4. Inventory and crafting

Lists, not grids. The inventory is one alphabetically sorted list, and
so is the crafting list; slots are the game's business. Other sort
orders and manual reordering may come later.

The inventory is one screen whose tab stops are whatever is present:
the inventory, coins and ammo, equipment, and a chest, shop, or
crafting list when one is open. A shop beside the inventory is two tab
stops, not a mode.

# 5. Order

1. Scanner.
2. Cursor and clicking.
3. Inventory, chests, shops, crafting.
4. Positional audio for enemies, drops, hazards, edges.
5. Build mode, on the cursor.
6. The remaining menus: creating characters and worlds, settings,
   multiplayer, mods.
7. The long tail: NPC dialogue, bestiary, journey mode, events, bosses.
