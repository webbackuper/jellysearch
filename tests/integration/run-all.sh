#!/usr/bin/env bash
# Runs every integration check on a throwaway Jellyfin instance and fresh test media, then removes them.
#
#   tests/integration/run-all.sh
#
# Uses the plugin build of Jellyfin.Plugin.JellySearch/bin/Release (dotnet build -c Release first).
# The IMDb ratings come from a small local copy served on 127.0.0.1 (no download from IMDb).
# Another Jellyfin can be tested before upgrading the server:
#   JELLYSEARCH_JF_BIN=/path/to/jellyfin/jellyfin JELLYSEARCH_JF_WEBDIR=/path/to/jellyfin/jellyfin-web tests/integration/run-all.sh
# With JELLYSEARCH_WITH_JELLYUSB=<folder of Jellyfin.Plugin.JellyUsb.dll>, JellyUSB is installed too.
# Exit status: 0 when every check passed.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
export JELLYSEARCH_TEST_PORT="${JELLYSEARCH_TEST_PORT:-18198}"
IMDB_PORT="${JELLYSEARCH_IMDB_PORT:-18199}"
WORK="$(mktemp -d -t jellysearch-it.XXXXXX)"
INSTANCE="$WORK/jf"
MEDIA="$WORK/media"
HTTP_PID=""
cleanup() {
    python3 -B "$HERE/test-server.py" stop "$INSTANCE" >/dev/null 2>&1 || true
    [ -n "$HTTP_PID" ] && kill "$HTTP_PID" 2>/dev/null || true
    [ "${JELLYSEARCH_KEEP:-0}" = 1 ] && echo "kept: $WORK" || rm -rf "$WORK"
}
trap cleanup EXIT

echo "==> Médiathèque de test"
"$HERE/make-media.sh" "$MEDIA" >/dev/null
(cd "$WORK/imdb" && exec python3 -m http.server "$IMDB_PORT" --bind 127.0.0.1 >/dev/null 2>&1) &
HTTP_PID=$!
export JELLYSEARCH_IMDB_URL="http://127.0.0.1:$IMDB_PORT/title.ratings.tsv.gz"
echo "==> Instance Jellyfin jetable (port $JELLYSEARCH_TEST_PORT)"
python3 -B "$HERE/test-server.py" start "$INSTANCE" "$MEDIA" | tail -1
python3 -B "$HERE/test-server.py" scan "$INSTANCE" | tail -1

status=0
for check in search-checks.py uninstall-checks.py; do
    echo "==> $check"
    if ! python3 -B "$HERE/$check" "$INSTANCE" "$MEDIA" > "$WORK/$check.log" 2>&1; then
        grep -E "FAIL|Error|Traceback" "$WORK/$check.log" | head -20 || true
        status=1
    fi
    tail -1 "$WORK/$check.log"
done

if [ "$status" -ne 0 ]; then
    echo "==> ÉCHEC des tests d'intégration" >&2
fi
exit "$status"
