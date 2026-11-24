# CalmTerraria

A tModLoader mod that makes Terraria playable without sight. It is
built on SRUI: the mod hosts a headless SRUI app inside the game, so
its screens are ordinary SRUI widgets spoken through Prism, and its
sound goes through Srui.Audio.

It is early. What exists is the foundation and the way from the title
screen into a world: see section 3.

# 1. Layout

- `CalmTerraria/` - the mod. The folder's name is the mod's name.
  - `Host/` - the SRUI app's life inside the game: native libraries,
    the keyboard, the per-frame tick, the autopilot. Knows nothing
    about Terraria's rules.
  - `Screens/` - the shell that decides who has the keyboard, and the
    title screen.
  - `World/` - the game, supplied to the core: the scan source, the
    cursor, the sonar's audio, the player's belongings.
- `CalmTerraria.Core/` - everything that does not need the game: the
  scanner, the sonar's timing, the pack screen. References no
  Terraria assembly.
- `CalmTerraria.Tests/` - headless tests of the core, on SRUI's
  harness. `dotnet test CalmTerraria.Tests`.
- `scripts/` and `tools/autopilot.sh` - tests in the real game; see
  section 5.
- `docs/design.md` - what is being built and the decisions behind it.
- `docs/controls.md` - every key.

# 2. Building

Needs the `srui` clone beside this one (`repos/srui`), on a branch
that targets net8.0, with its native libraries built
(`native/build-native.ps1` there, once).

    dotnet build CalmTerraria

That compiles the mod, stages SRUI's managed and native libraries into
it, and packages `CalmTerraria.tmod` into the tModLoader Mods folder,
enabled. Close the game first: a loaded mod's file is locked.

tModLoader is expected at the default Steam path; set `TML_DIR` where
it is not.

# 3. What works

The mod is meant to run without any other accessibility mod.

- **Title screen.** A list: Single player, Exit. Pick a character,
  then a world; Escape goes back one step. Creating either is not
  built yet.
- **Scanner.** What the map has revealed, as category, kind, and
  instance, nearest first: containers, treasure, ores, gems, crafting
  stations, plants, liquids and oceans, places such as the dungeon or
  a floating island, then people, enemies, and dropped items.
- **Cursor.** A tile cursor on the J pad that says what is under it;
  the brackets are the mouse buttons there.
- **Enemy sonar.** Each nearby enemy pulses from where it is, faster
  as it closes, muffled behind ground, brighter once a held melee
  weapon reaches it.
- **Pack screen.** Escape, in a world: the inventory as one
  alphabetical list, what is worn, and Save and exit.

Everything spoken is also written to tModLoader's `client.log`, on
lines starting `Spoke:`.

# 4. How the host works

- **Runtime.** tModLoader runs mods on .NET 8, so SRUI is built for
  net8.0 as well as net10.0.
- **Natives.** A mod is loaded from a stream and has no folder. The
  native DLLs travel in the mod file, are written under the save path
  on load, and are handed to SRUI's P/Invokes by an import resolver.
- **Keyboard in.** The game owns the window. An SDL event watch sees
  every key and text event the game pumps, which gives real
  auto-repeat and layout-correct text; they are translated into SRUI
  input and drained once a frame.
- **Keyboard ownership.** One detour, after the game reads the
  keyboard each frame. While SRUI owns the keyboard, the state the
  game read is replaced with an empty one.

# 5. Testing in the game

The autopilot plays a script of input into the running game, and the
transcript is read back from the log:

    tools/autopilot.sh scripts/smoke.txt

It launches the game, runs the script, quits, and prints what was
spoken. `Host/Autopilot.cs` documents the script's steps. A script can
only load saves whose file names start with `calmtest`, and the
commands that change a world (spawning an enemy, revealing the map)
refuse to run in any other.
