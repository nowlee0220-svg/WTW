#!/usr/bin/env python3
"""
(S1) 프로파일 → "일 종류별 비용 표".
dotnet-trace 의 dotnet-sampled-thread-time 프로파일(speedscope JSON)을 읽어, 시뮬 스레드의 시간을 일 종류로 나눈다.
표본마다 스택에서 "가장 안쪽의" 분류된 함수 하나에 시간을 준다(겹쳐 세지 않음 — 합이 100%).
사용: python3 tools/profile/categories.py 파일.speedscope.json [--thread 이름 일부] [--top 30]
  (만들기: tools/profile/run.sh 참고)
"""
import json, re, sys, collections

# (분류, 함수 이름 정규식) — 위에서부터가 아니라 스택의 가장 안쪽 일치가 이긴다.
CATS = [
    ("지도 만들기 (지방 깨우기 · 경계 만들기)", r"ProvinceGenerator|ProvinceLife\.\.ctor|PeopleSim\.BuildBorder|ProvinceCache"),
    ("길 찾기", r"PathFinder\."),
    ("주변 찾기 (물 · 먹을 것 · 그늘 · 둘러보기)", r"ProvinceLife\.Nearest(?!Fire|Material|Stone)|ProvinceLife\.Ok|PeopleSim\.(Look|SeenAcross|NearestKnown|Followee|Search|TryEat|TryDrink|Remember|SeeAround|TryWarm|TryWander|TryGather|Beside|NearestGate)\b"),
    ("욕구 · 기온 셈", r"PeopleSim\.(Integrate|IntegrateBaby|PredictUrgent|Step|FeltAt|FeltNow|RatesOf)\b|ProvinceLife\.(FeltTemp10|FireWarmth|OutsideTemp10)"),
    ("실험 · 만들기 (계획)", r"PeopleSim\.(Plan|PlanInner|FinishWork|ExperimentScore|CraftScore|TryExperiment|TryCraftKnown|ApplyResult|PickAction|InputPrefs|WithPrefs|PickCarried|PickTool|HasInputFor|InputInWorld|AlreadyCovered|MeetsAll|MeetsInput|BeginWork|GoalProps|ServesWish|TryMine|FinishMine|TryCook|FinishCook)\b|CraftData|Craft\.Judge|CraftCatalog"),
    ("재료 구하기", r"PeopleSim\.(StartMaterialFor|FinishMaterial|FinishMaterialInner|ArriveMaterial)\b|ProvinceLife\.(NearestMaterial|NearestStone|NearestCarryWhere)|StartMaterialFor"),
    ("불 돌보기", r"PeopleSim\.(TryFireWork|TryTendFire|TendBeforeSleep|StartTend|FinishTend|FiresDaily|TryWarmByFire|TryLightKnownFire|ArriveTakeFire|TryLightWithEmber|HearthNear|NeedsFire|NearestFireAny|SpotAway)\b|ProvinceLife\.(NearestFire|HeatAt|LightFire)"),
    ("가르치기 · 배우기", r"PeopleSim\.(TeachScore|TryTeach|BeginLesson|FinishLesson|ShareClue|AddLearning|TeachWatchers|Understanding|Learn|UseKnowledge)\b"),
    ("그림 (그리기 · 보기)", r"PeopleSim\.(StudyScore|TryStudy|FinishStudy|MakePainting|FindSurface|PaintingAt|RecordWishes|HasPainting|PaintingsOf)\b"),
    ("무리 셈 · 잊기 (하루 경계)", r"PeopleSim\.(GroupKnowledgeDaily|CheckLostDiscoveries|KnowledgeDaily|GroupKnowers|Knowers|RegainHow)\b"),
    ("말 (닮아 감 · 갈라짐 · 이름 · 지명)", r"PeopleSim\.(LanguageYear|NameLands|NameLand|Converge|WordOf|Speakers|GiveName|NameTaken|DisplayName|Descriptor|LearnSpeech|NewWordFor|GiveNickname|LandWordTaken)\b|People\.Words\.|People\.Language\."),
    ("이야기 · 짝", r"PeopleSim\.(TryTalk|BeginTalk|EndTalk|TryPair|Argue|CanBeTalkedTo|TalkDelta)\b"),
    ("지방 넘기 (옮겨 가기)", r"PeopleSim\.(DoCrossings|Transfer|TryMove|PickNewLand|OpenBorders|CrossToKnown|StartCross|MoveIntent|BorderOf|FollowAcross|CrossToward|SleepEmptyProvinces)\b|<OpenBorders>"),
    ("지방 깨우기 (되살리기)", r"PeopleSim\.Life\b|RestoreDug|ProvinceLife\.Mine\b"),
    ("저장용 기록 · 알림", r"PeopleSim\.(AddBio|Notify|History|NotifyGrouped)\b"),
    ("기분 · 생각", r"PeopleSim\.(Mood|StateThoughts|ActiveThoughtCounts|AddThought|PurgeThoughts|StartBreak|BreakThreshold)\b"),
    ("하루 경계 (그 밖)", r"PeopleSim\.(OnDay|GiveBirth|Make|ReassignCarrier|Kill|TryConceive|CraftDaily)\b"),
    ("예약 (스케줄러)", r"Scheduling\.Scheduler|PeopleSim\.(Schedule|Unschedule)\b"),
    ("판단 (그 밖)", r"PeopleSim\.|Person\.|Motion\."),
    ("메모리 정리 (GC)", r"(?i)\bgc\b|GarbageCollect|gc_heap|WKS::|SVR::"),
]
COMPILED = [(n, re.compile(p)) for n, p in CATS]

def short(n):
    n = re.sub(r"\(.*?\)", "", n)
    return n.replace("Wtw.Sim!", "").replace("Wtw.Sim.People.", "People.").replace("Wtw.Sim.", "")

def main():
    path = sys.argv[1]
    args = sys.argv[2:]
    thread = args[args.index("--thread") + 1] if "--thread" in args else None
    top = int(args[args.index("--top") + 1]) if "--top" in args else 25
    d = json.load(open(path))
    frames = [short(f["name"]) for f in d["shared"]["frames"]]
    cat_of = []
    for f in frames:
        c = None
        for n, rx in COMPILED:
            if rx.search(f): c = n; break
        cat_of.append(c)
    profs = [p for p in d["profiles"] if p.get("events")]
    if thread: profs = [p for p in profs if thread in p["name"]]
    cats = collections.Counter(); excl = collections.Counter(); total = 0.0; sim = 0.0
    for p in profs:
        stack = []; last = None
        for e in p["events"]:
            t = e["at"]
            if last is not None and stack:
                dt = t - last; total += dt
                c = None
                for i in reversed(stack):
                    if cat_of[i]: c = cat_of[i]; break
                if any("AdvanceTo" in frames[i] for i in stack):
                    sim += dt
                    cats[c or "기타 (런타임 · 시뮬 밖)"] += dt
                excl[frames[stack[-1]]] += dt
            last = t
            if e["type"] == "O": stack.append(e["frame"])
            else: stack.pop()
    print(f"시뮬 시간 {sim/1000:.1f}초 (전체 표본 {total/1000:.1f}초)")
    print("| 일 종류 | 몫 |")
    print("|---|---|")
    for n, w in cats.most_common():
        print(f"| {n} | {100*w/max(sim,1e-9):.1f}% |")

if __name__ == "__main__":
    main()
