#!/usr/bin/env bash
# Headless internet check through the Photon Cloud on this PC: a host and clients (Godot console,
# --headless, scene res://scenes/session/internet_dev.tscn) play scripted orders in one Photon room,
# then every instance's match hashes are compared tick by tick. Build the client first
# (dotnet build client/Tribes.Client.csproj); the build must have the Photon AppId (secrets/photon.json).
# Environment:
#   TICKS (2000), CLIENTS (2), REGION (asia; "best" for the nearest), MODE (versus | endless), SPEED (200)
#   HOST_ARGS, CLIENT_ARGS (every client), LAST_ARGS (the last client only, e.g. --net-rejoin-at=800)
#   LATE_AT=TICK   one more client joins once the host passes this tick (MODE=endless)
#   KILL_AT=TICK   the host's process is killed once it passes this tick (a multiple of 250): host migration
#   OUT (work/session-internet)
# Every instance keeps its user data (settings, saves) in OUT/appdata-<name>, never in the game's own.
# Prints one line per checked tick (instances and whether their hashes agree) and the NET_EVENT lines
# of host changes. Exit code 0 when every instance that should finish did and all hashes agree.
cd "$(dirname "$0")/.."
G=work/godot-4.7.2/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe
OUT=${OUT:-work/session-internet}
TICKS=${TICKS:-2000}
CLIENTS=${CLIENTS:-2}
REGION=${REGION:-asia}
MODE=${MODE:-versus}
SPEED=${SPEED:-200}
SCENE=res://scenes/session/internet_dev.tscn
mkdir -p "$OUT"
rm -f "$OUT"/*.log "$OUT/room.txt" "$OUT/hashes.txt"
ROOM_FILE="$(cygpath -wa "$OUT")\\room.txt"
# run NAME ARGS...: one headless instance with its own user data folder.
run() {
  local name=$1
  shift
  mkdir -p "$OUT/appdata-$name"
  APPDATA="$(cygpath -wa "$OUT/appdata-$name")" "$G" --headless --path client "$SCENE" -- "$@" > "$OUT/$name.log" 2>&1
}
COMMON="--net-ticks=$TICKS --net-region=$REGION --net-mode=$MODE --net-speed=$SPEED --player-id=new"

# shellcheck disable=SC2086
run host --net-host="$CLIENTS" --net-room-file="$ROOM_FILE" $COMMON --player-name=host ${HOST_ARGS:-} &
H=$!
pids=()
for i in $(seq 1 "$CLIENTS"); do
  extra="${CLIENT_ARGS:-}"
  [ "$i" = "$CLIENTS" ] && extra="$extra ${LAST_ARGS:-}"
  # shellcheck disable=SC2086
  run "p$i" --net-join-file="$ROOM_FILE" $COMMON --player-name="p$i" $extra &
  pids+=("$!")
done

tick_of() { grep -h "SESSION_HASH name=$1 " "$OUT/$1.log" 2> /dev/null | tail -1 | sed 's/.*tick=\([0-9]*\).*/\1/'; }

if [ -n "${LATE_AT:-}" ] || [ -n "${KILL_AT:-}" ]; then
  late_started=""
  killed=""
  for _ in $(seq 1 1800); do
    t=$(tick_of host)
    t=${t:-0}
    if [ -n "${LATE_AT:-}" ] && [ -z "$late_started" ] && [ "$t" -ge "$LATE_AT" ]; then
      # shellcheck disable=SC2086
      run late --net-join-file="$ROOM_FILE" $COMMON --player-name=late ${CLIENT_ARGS:-} &
      pids+=("$!")
      late_started=1
      echo "late joiner started at host tick $t"
    fi
    if [ -n "${KILL_AT:-}" ] && [ -z "$killed" ] && [ "$t" -ge "$KILL_AT" ]; then
      echo "killing the host at tick $t, $(date -u +%H:%M:%S.%3N) UTC"
      # The instance runs in a subshell: end the Godot processes of the host instance.
      wmic process where "name like 'Godot%' and CommandLine like '%internet_dev%--player-name=host%'" call terminate > /dev/null 2>&1 || true
      killed=1
    fi
    { [ -z "${LATE_AT:-}" ] || [ -n "$late_started" ]; } && { [ -z "${KILL_AT:-}" ] || [ -n "$killed" ]; } && break
    kill -0 "$H" 2> /dev/null || break
    sleep 1
  done
fi

status=0
if [ -z "${KILL_AT:-}" ]; then
  wait "$H" || status=1
else
  wait "$H" 2> /dev/null
fi
for p in "${pids[@]}"; do wait "$p" || status=1; done
grep -h "SESSION_FAIL" "$OUT"/*.log && status=1
grep -h "NET_EVENT.*\(HostChanged\|became\|quit\|leave-to-rejoin\|found-in-list\|in-match\)" "$OUT"/*.log | sort -t= -k3 | sed 's/^/  /'
# One line per checked tick: the tick, each instance's hash, and whether they all agree.
grep -h "SESSION_HASH" "$OUT"/*.log |
  awk '{for(i=1;i<=NF;i++){split($i,a,"="); v[a[1]]=a[2]} print v["tick"], v["name"], v["hash"]}' | sort -u | sort -n -k1 |
  awk '{ if ($1 != t) { if (t != "") print t, n, line, (same ? "same" : "DIFF"); t = $1; h = $3; line = ""; n = 0; same = 1 }
         line = line " " $2 "=" $3; n++; if ($3 != h) same = 0 }
       END { if (t != "") print t, n, line, (same ? "same" : "DIFF") }' | tee "$OUT/hashes.txt"
grep -q DIFF "$OUT/hashes.txt" && status=1
grep -q "^$TICKS " "$OUT/hashes.txt" || status=1
echo "internet check: $([ $status -eq 0 ] && echo OK || echo FAILED)"
exit $status
