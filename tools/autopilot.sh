#!/usr/bin/env bash
# Runs the game under an autopilot script and prints what happened.
#
#   tools/autopilot.sh scripts/smoke.txt [timeout-seconds]
#
# The script is named to the game in an environment variable of the
# one process started here, so no game the player starts themselves
# can ever pick it up. The game is left to run until the script quits
# it (or the timeout kills it). What the mod spoke, what the autopilot
# did, and any error are then printed from the log.
#
# It refuses to start while a game is already running: a second game
# on top of someone's own is an interruption, and the two would share
# the saves.
set -u
script="$1"
timeout="${2:-300}"
tml="${TML_DIR:-/c/Program Files (x86)/Steam/steamapps/common/tModLoader}"

running() {
    powershell -NoProfile -Command "if (Get-CimInstance Win32_Process -Filter \"Name='dotnet.exe'\" | Where-Object { \$_.CommandLine -like '*tModLoader.dll*' -and \$_.CommandLine -notlike '*-build*' -and \$_.CommandLine -notlike '*-server*' }) { exit 0 } else { exit 1 }"
}
if [ ! -f "$script" ]; then
    echo "REFUSED: no script at $script. Nothing was started."
    exit 2
fi
named="$(cygpath -w "$(realpath "$script")")"
if running; then
    echo "REFUSED: a game is already running. Nothing was started."
    exit 3
fi

# The game's own launcher detaches, so the client is started the way
# that launcher ends up starting it: its bundled runtime, directly.
(cd "$tml" && CALMTERRARIA_AUTOPILOT="$named" ./dotnet/dotnet.exe tModLoader.dll >/dev/null 2>&1) &
launcher=$!
waited=0
# Which process is this run's: the only game there is, a moment after
# starting it. A game the player starts later is never touched.
sleep 3
ours="$(powershell -NoProfile -Command "(Get-CimInstance Win32_Process -Filter \"Name='dotnet.exe'\" | Where-Object { \$_.CommandLine -like '*tModLoader.dll*' -and \$_.CommandLine -notlike '*-server*' -and \$_.CommandLine -notlike '*-build*' } | Sort-Object CreationDate | Select-Object -First 1).ProcessId" | tr -d '\r')"
while kill -0 "$launcher" 2>/dev/null; do
    sleep 2
    waited=$((waited + 2))
    if [ "$waited" -ge "$timeout" ]; then
        echo "TIMEOUT after ${timeout}s; stopping the game this started."
        [ -n "$ours" ] && powershell -NoProfile -Command "Stop-Process -Id $ours -Force"
        break
    fi
done
wait "$launcher" 2>/dev/null
echo "--- ran ${waited}s"
# The newest client log: the one this run wrote.
log="$(ls -t "$tml/tModLoader-Logs"/client*.log | head -1)"
grep -E '\[CalmTerraria\]|CalmTerraria.*(rror|xception)|^\s+at CalmTerraria|^\s+at Srui' "$log" \
    | grep -vE 'icon_small|Speech backend' \
    | sed -E 's/^\[([0-9:.]+)\] \[[^]]*\] \[CalmTerraria\]: /\1 /'
