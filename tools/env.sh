# source tools/env.sh — dotnet 과 godot 경로를 잡는다.
WTW_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [ -x "$WTW_ROOT/.tools/dotnet/dotnet" ] && ! (command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^8\.'); then
  export DOTNET_ROOT="$WTW_ROOT/.tools/dotnet"; export PATH="$DOTNET_ROOT:$PATH"
fi
export GODOT="$WTW_ROOT/.tools/godot-bin"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
