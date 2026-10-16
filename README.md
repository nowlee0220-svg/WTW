# WTW — Watch The World

빈 행성에 사람을 한 명씩 뿌리면, 사람들이 스스로 재료를 실험해 기술을 찾고, 무리·마을·나라를 이루고, 다투고, 잊고, 되찾습니다. 플레이어는 신의 자리에서 지켜보고 가끔 개입합니다.

- 엔진: Godot 4 (.NET) + C# · 대상: 윈도우 PC
- 기획서: [`docs/PLAN.md`](docs/PLAN.md)
- 단계별 프롬프트: [`docs/PROMPTS.md`](docs/PROMPTS.md)
- 지금까지의 기록: [`docs/HISTORY.md`](docs/HISTORY.md)
- 단계 보고서: [`docs/reports/`](docs/reports/)

## 개발 (클라우드/리눅스)
```bash
tools/setup.sh                 # .NET 8 · Godot 4.5.1 .NET · 윈도우 내보내기 템플릿 (.tools/)
tools/build.sh                 # 전체 빌드
tools/test.sh                  # 빠른 테스트 (--slow 느린 테스트, --all 전부)
dotnet run -c Release --project tools/bench -- --dots 500 --days 100 --seeds 20   # 측정 (기준: tools/bench/baseline.json, 같은 컴퓨터일 때만 비교)
dotnet run -c Release --project tools/bench -- --planet --seeds 20               # 행성 만들기 측정(시간 · 땅 · 대륙 · 섬 · 오각형)
dotnet run -c Release --project tools/bench -- --dump build/p --seed 1 && python3 tools/planet_map.py build/p build/p.png  # 행성 펼친 지도
tools/screenshot.sh build/shot.png 5 1          # 가상 화면 스크린샷 (5초, 1배속)
tools/screenshot.sh build/t.png 0 4 --selftest --dots=500  # 게임 안 저장/불러오기 자체 시험
tools/screenshot.sh build/b.png 3 1 --mode=biome --select=auto  # 지도 모드 · 칸 정보 (AutoPilot.cs 참고)
tools/export-windows.sh        # dist/WTW-<단계>-windows.zip (단계 = game/project.godot 의 config/version)
tools/test-windows-exe.sh      # 내보낸 exe 를 Wine 으로 띄워 확인
```
