#!/usr/bin/env python3
"""
(S1) 구간별 시간 재기를 시뮬 코드에 넣는다(측정용 사본에만 — 저장소의 코드에는 넣지 않는다).
사용: python3 tools/profile/instrument.py <저장소 사본 경로>
      그 사본에서 측정 도구를 빌드해 `people cap ... --prof` 처럼 돌리면 구간별 시간이 나온다.
고치기 전(main) 코드와 뒤 코드에 같은 자리를 넣어 같은 표로 비교하려고 함수 머리(이름)로 찾는다. 없는 함수는 건너뛴다.
"""
import sys, os, re
root = sys.argv[1]
P = "Wtw.Sim.Core.Prof"
T = [
 ("sim/People/PeopleSim.Acts.cs", [
   ("private void Decide(Person p)", "Decide"), ("private bool TryTalk(Person p)", "Talk"), ("private void BeginTalk(Person p)", "Talk"),
   ("private void EndTalk(Person p)", "Talk"), ("private bool TryWarm(Person p, Why why)", "Near"), ("private bool TryWander(Person p, Why why)", "Near"),
   ("private bool TryGather(Person p)", "Near"), ("private void GoSleep(Person p, Why why)", "Near")]),
 ("sim/People/PeopleSim.Find.cs", [
   ("private void Look(Person p)", "Near"), ("private bool TryDrink(Person p, Why why)", "Near"), ("private bool TryEat(Person p, Why why, int skipTile = -1)", "Near"),
   ("private bool Search(Person p, Act act, Why why, int arg, bool water)", "Near")]),
 ("sim/People/PathFinder.cs", [("public bool Find(int sx, int sy, int goal, List<(int X, int Y)> points)", "Path"), ("public bool TryStraight(int sx, int sy, int goal", "Path")]),
 ("sim/People/ProvinceLife.cs", [
   ("private int Nearest(List<int>[] buckets,", "Near"), ("public int NearestMaterial(", "Material"), ("public int NearestCarryWhere(", "Material"),
   ("public int NearestStone(", "Material"), ("public int FireWarmth(int tile, long tick)", "Fire"), ("public int NearestFire(", "Fire"), ("public int HeatAt(", "Fire"),
   ("public ProvinceLife(Planet.Planet planet, ProvinceMap map, PeopleData data)", "Wake")]),
 ("sim/People/PeopleSim.Craft.cs", [
   ("private int ExperimentScore(Person p, ProvinceLife life)", "Experiment"), ("private int CraftScore(Person p)", "Experiment"),
   ("private bool PlanInner(Person p, int wish, bool known, Pcg32 rng)", "Experiment"), ("internal void FinishWork(Person p)", "Experiment"),
   ("private bool StartMaterialFor(Person p, Func<int, bool> itemOk, int prop, Why why)", "Material"), ("private void FinishMaterial(Person p)", "Material"),
   ("private bool TryFireWork(Person p, ProvinceLife life, int here, Pcg32 rng)", "Fire"), ("private bool TryTendFire(Person p)", "Fire"),
   ("private void FinishTend(Person p)", "Fire"), ("private void FiresDaily(int cell, ProvinceLife life)", "Fire"), ("private bool TryWarmByFire(Person p, Why why)", "Fire"),
   ("internal void CheckLostDiscoveries()", "Group"), ("private bool TryMine(Person p)", "Experiment"), ("private void TeachWatchers(", "Teach")]),
 ("sim/People/PeopleSim.Knowledge.cs", [
   ("private int TeachScore(Person p, ProvinceLife life)", "Teach"), ("private bool TryTeach(Person p)", "Teach"), ("internal void BeginLesson(Person p)", "Teach"),
   ("internal void FinishLesson(Person p, bool done)", "Teach"), ("internal void ShareClue(Person a, Person b, Pcg32 rng)", "Teach"),
   ("private int StudyScore(Person p, ProvinceLife life, out Painting? target)", "Paint"), ("internal void FinishStudy(Person p)", "Paint"),
   ("internal void MakePainting(Person p, int here, Pcg32 rng)", "Paint"), ("private void RecordWishes(", "Paint"),
   ("internal void GroupKnowledgeDaily()", "Group"), ("internal void KnowledgeDaily(Person p)", "Group")]),
 ("sim/People/PeopleSim.Acts.cs", [("private void Finish(Person p)", "X7"), ("private void Arrive(Person p)", "X7")]),
 ("sim/People/PeopleSim.Find.cs", [("private bool TryFetchStash(", "X8")]),
 ("sim/People/PeopleSim.Craft.cs", [("private bool TryCook(Person p, Why why)", "X8"), ("private int BestCarriedFood(Person p, bool desperate)", "X8")]),
 ("sim/People/PeopleSim.Drift.cs", [("internal void LanguageYear()", "Language")]),
 ("sim/People/PeopleSim.Places.cs", [("private void NameLands()", "Language")]),
 ("sim/People/PeopleSim.Speech.cs", [("internal void Converge(Person p, Person q, Pcg32 rng)", "Language"), ("public string WordOf(Person p, int concept, int minSyllables = 1)", "Language"),
   ("public int Understanding(Person a, Person b)", "Language")]),
 ("sim/People/PeopleSim.Needs.cs", [("private void Integrate(Person p, long until)", "Needs"), ("private long PredictUrgent(Person p, long from, long until, int tile)", "Needs")]),
 ("sim/People/PeopleSim.cs", [("public int Mood(Person p)", "Mood"), ("private void Schedule(Person p, long tick)", "Schedule"), ("private void Notify(WorldEventKind kind, Person p, int other, string text)", "Record"),
   ("internal void OnEvent(int id)", "Decide")]),
 ("sim/People/PeopleSim.Cross.cs", [("internal void DoCrossings()", "Cross"), ("private Border? BuildBorder(int cell, int slot)", "Wake")]),
 ("sim/People/PeopleSim.Move.cs", [("private bool TryMove(Person p)", "Cross"), ("private void MoveIntent(Person p, Core.Pcg32 rng)", "Cross")]),
 ("sim/People/PeopleSim.Life.cs", [("internal void OnDay()", "Day")]),
 ("sim/People/PeopleSim.Lanes.cs", [("internal void RunProvinces(long upTo, int threads)", "Sync"), ("internal void ApplyOps()", "Record")]),
 ("sim/Province/ProvinceGenerator.cs", [("public static ProvinceMap Generate(", "MapGen")]),
 ("sim/Scheduling/Scheduler.cs", [("public ScheduledEvent Pop()", "Schedule")]),
 ("sim/World/GameWorld.cs", [("public void AdvanceTo(long targetTick)", "Sim")]),
 # (E5b) 짐승 · 사냥 — X1 무리의 판단 · X2 사냥할까 · X3 둘레의 사람 · X4 늘고 줄기 · X5 사냥 · 물고기 · X6 옷 · 썩음
 ("sim/People/PeopleSim.Animals.cs", [("internal void OnHerdEvent(ProvinceLife life, int id)", "X1"), ("private Person? NearestPerson(", "X3"), ("private Person? Vulnerable(", "X3"),
   ("private void AnimalsDaily(ProvinceLife life)", "X4"), ("private bool TryStalk(", "X1")]),
 ("sim/People/PeopleSim.Hunt.cs", [("private int HuntScore(Person p, ProvinceLife life, out Herd? target", "X2"), ("public int WeaponPower(Person p)", "X2"), ("private void HuntHelpers(", "X2"), ("private bool TryHunt(Person p, Herd h)", "X5"),
   ("private void ArriveHunt(Person p)", "X5"), ("private void ResolveHunt(Person p)", "X5"), ("private void FinishButcher(Person p, bool done)", "X5"), ("private int FishScore(", "X5"),
   ("private void BodyDaily(Person p, Pcg32 rng)", "X6"), ("private void RotGround(", "X6")]),
]
here = os.path.dirname(os.path.abspath(__file__))
src = os.path.join(here, "..", "..", "sim", "Core", "Prof.cs")
dst = os.path.join(root, "sim", "Core", "Prof.cs")
if os.path.abspath(src) != os.path.abspath(dst):
    open(dst, "w", encoding="utf-8").write(open(src, encoding="utf-8").read())
done = 0; miss = []
for f, items in T:
    path = os.path.join(root, f)
    if not os.path.exists(path): miss += [f + " (파일 없음)"]; continue
    lines = open(path, encoding="utf-8").read().split("\n")
    for sig, sec in items:
        idx = next((i for i, l in enumerate(lines) if sig in l and "=>" not in l), None)
        if idx is None: miss.append(f"{f}: {sig}"); continue
        j = idx
        while lines[j].strip() != "{": j += 1
        indent = lines[j][:len(lines[j]) - len(lines[j].lstrip())] + "    "
        lines.insert(j + 1, f"{indent}using var __prof = {P}.Enter({P}.S.{sec});")
        done += 1
    open(path, "w", encoding="utf-8").write("\n".join(lines))
print(f"넣음 {done}곳 · 못 찾음 {len(miss)}곳")
for m in miss: print("  -", m)
