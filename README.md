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
    the keyboard, the per-frame tick. Knows nothing about Terraria's
    rules.
  - `Screens/` - what the player uses: the shell that decides who has
    the keyboard, the title screen, the game menu.
- `docs/design.md` - what is being built and the decisions behind it.
- `docs/controls.md` - the control scheme as decided so far.

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

- **Title screen.** A list: Single player, Exit. The game's own menu
  is not used.
- **Single player.** Pick a character, then a world; Escape goes back
  one step. Creating either is not built yet.
- **In a world.** "Ready." when the world has loaded. Nothing else
  speaks yet. Escape opens the game menu, whose one command is Save
  and exit; Escape again closes it.

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
