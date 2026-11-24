# CalmTerraria

A tModLoader mod that makes Terraria playable without sight. It is
built on SRUI: the mod hosts a headless SRUI app inside the game, so
its screens are ordinary SRUI widgets spoken through Prism, and its
sound goes through Srui.Audio.

It is at the spike stage. What exists proves the foundation and nothing
else: see section 3.

# 1. Layout

- `CalmTerraria/` - the mod. The folder's name is the mod's name.
  - `Host/` - the SRUI app's life inside the game: native libraries,
    the keyboard, the per-frame tick. Knows nothing about Terraria's
    rules.
  - `Spike/` - the test screen. Deleted when the first real screen
    exists.
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

# 3. The spike

With the mod enabled, the game says "CalmTerraria loaded" once it
reaches the title screen. F2 opens a test screen and Escape closes it.
While it is open the game receives no keys at all.

- Tab moves between the widgets.
- Speech backend: says which screen reader Prism found.
- Sound on the left, sound on the right: a ping through HRTF.
- Typing: an edit box, for checking that letters reach it and not the
  game, and that arrows review what was typed.

What it proves: SRUI loads on the game's .NET 8 runtime, the native
libraries load from inside a mod file, the keyboard has exactly one
owner, and the Escape that closes a screen does not also reach the
game.

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
