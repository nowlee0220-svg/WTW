#!/usr/bin/env bash
# WTW 클라우드(우분투) 환경 준비: .NET 8 SDK · Godot 4.5.1 .NET(리눅스) · 윈도우 내보내기 템플릿.
# 이미 있으면 건너뛴다. 모든 것은 저장소의 .tools/ 아래에 놓는다(.NET SDK 는 패키지가 있으면 그것을 쓴다).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TOOLS="$ROOT/.tools"
GODOT_VER="4.5.1"
GODOT_TAG="${GODOT_VER}-stable"
GODOT_DIR="$TOOLS/godot"
GODOT_BIN="$GODOT_DIR/Godot_v${GODOT_TAG}_mono_linux_x86_64/Godot_v${GODOT_TAG}_mono_linux.x86_64"
TPL_DIR="$HOME/.local/share/godot/export_templates/${GODOT_VER}.stable.mono"
BASE_URL="https://github.com/godotengine/godot/releases/download/${GODOT_TAG}"

mkdir -p "$TOOLS"
log() { echo "[setup] $*"; }

dl() { # url dest — 실패하면 몇 번 다시 시도
  local url="$1" dest="$2" i
  for i in 1 2 3 4; do
    if curl -fsSL --retry 3 -o "$dest.part" "$url"; then mv "$dest.part" "$dest"; return 0; fi
    log "다운로드 실패($i): $url"; sleep $((i * 2))
  done
  return 1
}

# 1) .NET 8 SDK
if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^8\.'; then
  log ".NET 8 SDK 있음"
elif [ -x "$TOOLS/dotnet/dotnet" ]; then
  log ".NET 8 SDK 있음 (.tools/dotnet)"
else
  log ".NET 8 SDK 설치"
  if command -v apt-get >/dev/null 2>&1 && [ "$(id -u)" = "0" ] && \
     (apt-get install -y -qq dotnet-sdk-8.0 >/dev/null 2>&1 || (apt-get update -qq >/dev/null 2>&1 && apt-get install -y -qq dotnet-sdk-8.0 >/dev/null 2>&1)); then
    log "apt 로 설치함"
  else
    dl https://dot.net/v1/dotnet-install.sh "$TOOLS/dotnet-install.sh"
    bash "$TOOLS/dotnet-install.sh" --channel 8.0 --install-dir "$TOOLS/dotnet" >/dev/null
  fi
fi

# 2) Godot 4.5.1 .NET (리눅스, 헤드리스 실행·내보내기용)
if [ -x "$GODOT_BIN" ]; then
  log "Godot 있음"
else
  log "Godot ${GODOT_TAG} mono 설치"
  mkdir -p "$GODOT_DIR"
  dl "$BASE_URL/Godot_v${GODOT_TAG}_mono_linux_x86_64.zip" "$GODOT_DIR/godot.zip"
  unzip -q -o "$GODOT_DIR/godot.zip" -d "$GODOT_DIR"
  rm -f "$GODOT_DIR/godot.zip"
  chmod +x "$GODOT_BIN"
fi
ln -sf "$GODOT_BIN" "$TOOLS/godot-bin"

# 3) 내보내기 템플릿 (윈도우만 남긴다)
if [ -f "$TPL_DIR/windows_release_x86_64.exe" ]; then
  log "내보내기 템플릿 있음"
else
  log "내보내기 템플릿 설치 (크다, 잠시 걸림)"
  mkdir -p "$TPL_DIR"
  dl "$BASE_URL/Godot_v${GODOT_TAG}_mono_export_templates.tpz" "$TOOLS/templates.tpz"
  tmp="$TOOLS/tpl_tmp"; rm -rf "$tmp"; mkdir -p "$tmp"
  unzip -q -o "$TOOLS/templates.tpz" -d "$tmp"
  for f in version.txt windows_release_x86_64.exe windows_debug_x86_64.exe windows_release_x86_64_console.exe windows_debug_x86_64_console.exe; do
    [ -f "$tmp/templates/$f" ] && cp "$tmp/templates/$f" "$TPL_DIR/"
  done
  rm -rf "$tmp" "$TOOLS/templates.tpz"
fi

# 4) 가상 화면(스크린샷용) — 없으면 시도만
if ! command -v xvfb-run >/dev/null 2>&1 && [ "$(id -u)" = "0" ]; then
  apt-get install -y -qq xvfb >/dev/null 2>&1 || true
fi

log "끝. 환경 변수는 tools/env.sh 를 source 하면 된다."
