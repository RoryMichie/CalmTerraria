#!/usr/bin/env bash
# Runs the game under an autopilot script and prints what happened.
#
#   tools/autopilot.sh scripts/smoke.txt [timeout-seconds]
#
# The script is copied to where the mod looks for it, the game is
# launched and left to run until the script quits it (or the timeout
# kills it), and the script is removed again so the next ordinary
# launch is an ordinary one. What the mod spoke, what the autopilot
# did, and any error are then printed from client.log.
set -u
script="$1"
timeout="${2:-300}"
tml="${TML_DIR:-/c/Program Files (x86)/Steam/steamapps/common/tModLoader}"
save="$USERPROFILE/Documents/My Games/Terraria/tModLoader"
log="$tml/tModLoader-Logs/client.log"
target="$save/CalmTerraria/autopilot.txt"

mkdir -p "$save/CalmTerraria"
cp "$script" "$target"
trap 'rm -f "$target"' EXIT

# The game's own launcher detaches, so the client is started the way
# that launcher ends up starting it: its bundled runtime, directly.
(cd "$tml" && ./dotnet/dotnet.exe tModLoader.dll >/dev/null 2>&1) &
launcher=$!
waited=0
while kill -0 "$launcher" 2>/dev/null; do
    sleep 2
    waited=$((waited + 2))
    if [ "$waited" -ge "$timeout" ]; then
        echo "TIMEOUT after ${timeout}s; killing the game."
        powershell -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name='dotnet.exe'\" | Where-Object { \$_.CommandLine -like '*tModLoader.dll*' } | ForEach-Object { Stop-Process -Id \$_.ProcessId -Force }"
        break
    fi
done
wait "$launcher" 2>/dev/null
echo "--- ran ${waited}s"
grep -E '\[CalmTerraria\]|CalmTerraria.*(rror|xception)|^\s+at CalmTerraria|^\s+at Srui' "$log" \
    | grep -vE 'icon_small|Speech backend' \
    | sed -E 's/^\[([0-9:.]+)\] \[[^]]*\] \[CalmTerraria\]: /\1 /'
