#!/usr/bin/env bash
# Headless LAN check on this PC: a host and two clients (Godot console, 127.0.0.1) play scripted orders,
# then every instance's match hashes are compared. Build the client first (dotnet build client/Tribes.Client.csproj).
# Environment: PORT (47811), TICKS (2000), BOB_ARGS (extra options for the second client, e.g. --session-rejoin-at=800),
# HOST_ARGS (extra options for the host, e.g. --session-discovery for BOB_ARGS="... --session-rejoin-via=list"),
# OUT (log folder, work/session-lan). Exit code 0 when every instance finished and all hashes agree.
cd "$(dirname "$0")/.."
G=work/godot-4.7.2/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe
OUT=${OUT:-work/session-lan}
PORT=${PORT:-47811}
TICKS=${TICKS:-2000}
SCENE=res://scenes/session/session_dev.tscn
mkdir -p "$OUT"
"$G" --headless --path client "$SCENE" -- --session-host=2 --session-port="$PORT" --session-ticks="$TICKS" --session-name=host ${HOST_ARGS:-} > "$OUT/host.log" 2>&1 &
H=$!
sleep 4
"$G" --headless --path client "$SCENE" -- --session-join=127.0.0.1:"$PORT" --session-ticks="$TICKS" --session-name=alice > "$OUT/alice.log" 2>&1 &
A=$!
# shellcheck disable=SC2086
"$G" --headless --path client "$SCENE" -- --session-join=127.0.0.1:"$PORT" --session-ticks="$TICKS" --session-name=bob ${BOB_ARGS:-} > "$OUT/bob.log" 2>&1 &
B=$!
status=0
for p in $H $A $B; do wait "$p" || status=1; done
grep -h "SESSION_FAIL" "$OUT"/*.log && status=1
# One line per checked tick: the tick, each instance's hash, and whether they agree.
grep -h "SESSION_HASH" "$OUT"/host.log "$OUT"/alice.log "$OUT"/bob.log |
  awk '{for(i=1;i<=NF;i++){split($i,a,"="); v[a[1]]=a[2]} print v["tick"], v["name"], v["hash"]}' | sort -u | sort -n -k1 |
  awk '{ if ($1 != t) { if (t != "") print t, line, (n == 3 && same ? "same" : "DIFF"); t = $1; h = $3; line = ""; n = 0; same = 1 }
         line = line " " $2 "=" $3; n++; if ($3 != h) same = 0 }
       END { if (t != "") print t, line, (n == 3 && same ? "same" : "DIFF") }' | tee "$OUT/hashes.txt"
grep -q DIFF "$OUT/hashes.txt" && status=1
[ -s "$OUT/hashes.txt" ] || status=1
echo "lan check: $([ $status -eq 0 ] && echo OK || echo FAILED)"
exit $status
