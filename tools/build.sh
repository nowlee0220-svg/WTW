#!/usr/bin/env bash
# 전체 빌드: 시뮬·테스트·측정 도구(dotnet) + Godot 프로젝트(자원 가져오기 + C# 빌드).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT/tools/env.sh"
cd "$ROOT"
dotnet build Wtw.sln -c "${CONFIG:-Debug}" -nologo -v q
if [ -x "$GODOT" ]; then
  # 처음이면 .godot/ 를 만들고 자원(글꼴 등)을 가져온다
  "$GODOT" --headless --path game --import >/dev/null 2>&1 || true
  "$GODOT" --headless --path game --build-solutions --quit 2>&1 | grep -Ei "error|fail" || true
  dotnet build game/WTW.csproj -c "${CONFIG:-Debug}" -nologo -v q
else
  echo "[build] Godot 가 없어 game/ 은 dotnet 으로만 빌드한다 (tools/setup.sh 를 먼저)"
  dotnet build game/WTW.csproj -c "${CONFIG:-Debug}" -nologo -v q
fi
echo "[build] 끝"
