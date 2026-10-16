#!/usr/bin/env bash
# 내보낸 윈도우 실행 파일(dist/WTW-<단계>-windows.zip)을 Wine + 가상 화면에서 실제로 띄워 본다.
# 게임 안 자체 시험(저장 → 더 돌림 → 불러오기 → 같은 자리인지)과 스크린샷을 한다.
# 사용: tools/test-windows-exe.sh [단계, 기본 E0] [스크린샷 경로]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STAGE="${1:-E0}"
SHOT="${2:-$ROOT/build/windows-exe.png}"
ZIP="$ROOT/dist/WTW-${STAGE}-windows.zip"
[ -f "$ZIP" ] || { echo "[wine] $ZIP 가 없다. tools/export-windows.sh 먼저"; exit 1; }
if ! command -v wine >/dev/null 2>&1; then
  echo "[wine] Wine 설치 (처음 한 번, 몇 분)"
  apt-get install -y -qq wine64 >/dev/null 2>&1 || (apt-get update -qq && apt-get install -y -qq wine64 >/dev/null)
fi
WORK="$ROOT/build/wine-run"; rm -rf "$WORK"; mkdir -p "$WORK" "$(dirname "$SHOT")"
unzip -q "$ZIP" -d "$WORK"
export WINEDEBUG=-all WINEPREFIX="$ROOT/.tools/wineprefix"
run() { timeout 300 xvfb-run -a -s "-screen 0 1920x1080x24" wine "$WORK/WTW/WTW.exe" --rendering-driver opengl3 --audio-driver Dummy --resolution 1920x1080 -- "$@" 2>&1 | grep -aE "SELFTEST|FRAMES|SCREENSHOT" | sort -u; }
run --selftest --speed=4 --seed=7
WINSHOT="Z:$(echo "$SHOT" | tr '/' '\\')"
run --screenshot="$WINSHOT" --shot-after=6 --speed=4 --seed=20261016
[ -f "$SHOT" ] && echo "[wine] 스크린샷 $SHOT"
