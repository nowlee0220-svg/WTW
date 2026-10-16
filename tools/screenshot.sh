#!/usr/bin/env bash
# 가상 화면(xvfb)에서 게임을 몇 초 돌리고 스크린샷을 찍는다.
# 사용: tools/screenshot.sh [출력.png] [초] [속도 1~5] [추가 인자...]
#   예) tools/screenshot.sh build/shot.png 5 1
#   자체 시험) tools/screenshot.sh build/selftest.png 0 1 --selftest
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT/tools/env.sh"
OUT="${1:-$ROOT/build/screenshot.png}"
SECS="${2:-5}"
SPEED="${3:-1}"
shift $(( $# > 3 ? 3 : $# )) || true
mkdir -p "$(dirname "$OUT")"
OUT="$(cd "$(dirname "$OUT")" && pwd)/$(basename "$OUT")"
cd "$ROOT"
[ -d game/.godot ] || "$GODOT" --headless --path game --import >/dev/null 2>&1 || true
xvfb-run -a -s "-screen 0 1920x1080x24" \
  "$GODOT" --path game --audio-driver Dummy --rendering-driver opengl3 --resolution 1920x1080 --position 0,0 \
  -- --screenshot="$OUT" --shot-after="$SECS" --speed="$SPEED" --seed=20261016 "$@" 2>&1 \
  | grep -E "SCREENSHOT|FRAMES|SELFTEST|PROVINCE|ENTEREXIT|ERROR|Error|error" | grep -v "^\s*at " || true
[ -f "$OUT" ] && echo "[screenshot] $OUT"
