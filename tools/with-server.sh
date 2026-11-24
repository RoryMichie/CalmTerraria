#!/usr/bin/env bash
# Runs an autopilot script against a server on this machine.
#
#   tools/with-server.sh scripts/join.txt [server arguments...]
#
# The server hosts the calmtest world on port 7778. It is started
# without Steam, so it cannot see workshop mods, and tModLoader then
# drops them from the list of enabled mods - for the client as well.
# That list is saved first and put back afterwards, whatever happens.
set -u
script="$1"; shift
tml="${TML_DIR:-/c/Program Files (x86)/Steam/steamapps/common/tModLoader}"
save="$USERPROFILE/Documents/My Games/Terraria/tModLoader"
enabled="$save/Mods/enabled.json"
kept="$(mktemp)"
out="$(mktemp)"
cp "$enabled" "$kept"
stop() {
    powershell -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name='dotnet.exe'\" | Where-Object { \$_.CommandLine -like '*tModLoader.dll*-server*' } | ForEach-Object { Stop-Process -Id \$_.ProcessId -Force }"
    cp "$kept" "$enabled"
    rm -f "$kept" "$out"
}
trap stop EXIT
(cd "$tml" && ./dotnet/dotnet.exe tModLoader.dll -server -world "$(cygpath -w "$save/Worlds/calmtest.wld")" \
    -port 7778 -nosteam -players 4 "$@" > "$out" 2>&1 < /dev/null) &
for _ in $(seq 1 60); do
    sleep 2
    grep -q "Listening on port" "$out" && break
done
"$(dirname "$0")/autopilot.sh" "$script" 150
