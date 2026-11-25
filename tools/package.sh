#!/usr/bin/env bash
# Makes dist/CalmTerraria.zip: what someone needs to install the mod
# without building it. Build first; this takes the installed .tmod.
set -eu
root="$(cd "$(dirname "$0")/.." && pwd)"
tmod="$USERPROFILE/Documents/My Games/Terraria/tModLoader/Mods/CalmTerraria.tmod"
out="$root/dist"
rm -rf "$out/CalmTerraria" "$out/CalmTerraria.zip"
mkdir -p "$out/CalmTerraria"
cp "$tmod" "$root/install/install.ps1" "$root/install/install.cmd" "$root/install/README.txt" "$root/docs/controls.md" "$out/CalmTerraria/"
(cd "$out" && powershell -NoProfile -Command "Compress-Archive -Path CalmTerraria -DestinationPath CalmTerraria.zip -Force")
ls -la "$out/CalmTerraria.zip"
