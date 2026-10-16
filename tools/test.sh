#!/usr/bin/env bash
# 테스트. 기본 = 빠른 테스트만(수 초). --slow = 느린 테스트만, --all = 전부.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT/tools/env.sh"
cd "$ROOT"
case "${1:-}" in
  --slow) FILTER="Category=Slow" ;;
  --all)  FILTER="" ;;
  *)      FILTER="Category=Fast" ;;
esac
if [ -n "$FILTER" ]; then
  dotnet test sim.tests -c "${CONFIG:-Debug}" -nologo --filter "$FILTER"
else
  dotnet test sim.tests -c "${CONFIG:-Debug}" -nologo
fi
