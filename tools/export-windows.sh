#!/usr/bin/env bash
# 윈도우 x86_64 실행 파일을 내보내 dist/WTW-<단계>-windows.zip 으로 묶는다.
# .NET 실행에 필요한 파일이 모두 들어가므로 사용자 PC 에 따로 설치할 것이 없다.
# 사용: tools/export-windows.sh [단계 이름, 기본 = game/project.godot 의 config/version]
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT/tools/env.sh"
STAGE="${1:-$(sed -n 's/^config\/version="\(.*\)"/\1/p' "$ROOT/game/project.godot")}"
[ -n "$STAGE" ] || { echo "[export] 단계 이름을 찾지 못했다 (game/project.godot 의 config/version)"; exit 1; }
OUTDIR="$ROOT/build/WTW"
ZIP="$ROOT/dist/WTW-${STAGE}-windows.zip"
cd "$ROOT"
rm -rf "$OUTDIR"; mkdir -p "$OUTDIR" "$ROOT/dist"
"$GODOT" --headless --path game --import >/dev/null 2>&1 || true
"$GODOT" --headless --path game --export-release "Windows" "$OUTDIR/WTW.exe" 2>&1 | grep -Ei "error|fail|warn" | grep -v "^\s*at " || true
if [ ! -f "$OUTDIR/WTW.exe" ]; then echo "[export] 실패: WTW.exe 가 없다"; exit 1; fi
cp game/fonts/OFL.txt "$OUTDIR/FONT-LICENSE-NanumGothic-OFL.txt"
cat > "$OUTDIR/읽어보기.txt" <<TXT
WTW (Watch The World) — ${STAGE}

1. 이 폴더를 통째로 둔 채 WTW.exe 를 두 번 누르면 실행됩니다.
2. 윈도우가 "Windows의 PC 보호" 경고를 띄우면 "추가 정보" → "실행" 을 누르세요.
   (서명하지 않은 실행 파일이라 처음 한 번 나오는 경고입니다.)
3. 켜면 "새 세계" 화면이 나옵니다. [만들기] 를 누르면 행성이 만들어집니다.
4. 지구본: 왼쪽 끌기 = 돌리기, 휠 = 가까이·멀리, 칸 누르기 = 칸 정보, 화살표 키 = 돌리기.
   지도 모드: Q 지형 · W 생물군계 · E 기온 · R 강수 · T 자원 (아래 막대 단추로도).
   스페이스 = 멈춤, 1~5 = 1배속~5배속, F5 = 저장(멈춘 다음), F9 = 불러오기, Esc = 창 닫기.
TXT
rm -f "$ZIP"
(cd "$OUTDIR/.." && zip -qr "$ZIP" "$(basename "$OUTDIR")")
echo "[export] $ZIP ($(du -h "$ZIP" | cut -f1))"
