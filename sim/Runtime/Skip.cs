using Wtw.Sim.People;
using Wtw.Sim.Time;
using Wtw.Sim.World;

namespace Wtw.Sim.Runtime;

// ───────── 일의 크기 · 팝업 ─────────

/// <summary>
/// (E4) 세계의 일이 얼마나 큰가. ★★★ 세계 수준(세계에서 처음 발견 · 발견이 사라짐 · 말이 갈라짐)은 멈추고 팝업,
/// ★★ 무리 수준(무리에서 처음 · 다시 찾아냄 · 새 땅 · 땅 이름)은 알림만, ★ 한 사람의 일(태어남 · 죽음 · 짝 · 무너짐 …)은 그 지방을 볼 때만.
/// 화면과 측정 도구가 같은 규칙을 쓴다.
/// </summary>
public static class EventLevels
{
    public const int Person = 1, Group = 2, World = 3;

    public static int Of(WorldEventKind k) => k switch
    {
        WorldEventKind.Discovery or WorldEventKind.DiscoveryLost or WorldEventKind.Language => World,
        WorldEventKind.DiscoveryGroup or WorldEventKind.Rediscovered or WorldEventKind.Moved or WorldEventKind.LandNamed => Group,
        _ => Person,
    };

    public static string Stars(int level) => level >= World ? "★★★" : level == Group ? "★★" : "★";

    /// <summary>팝업으로 멈출 수 있는 종류(켜고 끄기 목록).</summary>
    public static readonly WorldEventKind[] PopupKinds = { WorldEventKind.Discovery, WorldEventKind.DiscoveryLost, WorldEventKind.Language };

    public static string KindName(WorldEventKind k) => k switch
    {
        WorldEventKind.Discovery => "세계에서 처음 발견",
        WorldEventKind.DiscoveryLost => "발견이 사라짐",
        WorldEventKind.Language => "말이 갈라짐",
        WorldEventKind.DiscoveryGroup => "무리에서 처음",
        WorldEventKind.Rediscovered => "다시 찾아냄",
        WorldEventKind.Moved => "새 땅으로",
        WorldEventKind.LandNamed => "땅 이름",
        _ => k.ToString(),
    };
}

/// <summary>
/// (E4) 큰 일로 멈출지 정한다(너무 자주 뜨지 않게): ★★★ 만, 꺼 둔 종류는 빼고, 같은 종류는 게임 시간 간격(배속이 빠를수록 길게:
/// 1~3배속 1년 · 4배속 2년 · 5배속 5년) 그리고 실제 시간 30초 간격. 실제 시간을 모르면(측정) realSeconds 에 음수.
/// </summary>
public sealed class PopupGate
{
    public const double RealGapSeconds = 30;
    private readonly long[] _lastTick = new long[32];
    private readonly double[] _lastReal = new double[32];
    public readonly bool[] Muted = new bool[32];

    public PopupGate() { Array.Fill(_lastTick, long.MinValue / 2); Array.Fill(_lastReal, double.NegativeInfinity); }

    public static long GapTicks(int speed) => (speed >= 5 ? 5 : speed == 4 ? 2 : 1) * GameTime.TicksPerYear;

    public bool ShouldPause(WorldEvent e, long now, double realSeconds, int speed)
    {
        if (EventLevels.Of(e.Kind) < EventLevels.World) return false;
        int k = (int)e.Kind;
        if (Muted[k]) return false;
        if (now - _lastTick[k] < GapTicks(speed)) return false;
        if (realSeconds >= 0 && realSeconds - _lastReal[k] < RealGapSeconds) return false;
        _lastTick[k] = now;
        if (realSeconds >= 0) _lastReal[k] = realSeconds;
        return true;
    }
}

// ───────── 넘기기 ─────────

/// <summary>
/// (E4) [넘기기]의 목표. 새 목표는 이 클래스를 이어받아 Reached(그리고 원하면 Progress)만 쓰면 된다 — 나중 단계가 조건을 쉽게 더할 수 있게.
/// 시뮬 스레드에서만 불린다.
/// </summary>
public abstract class SkipGoal
{
    /// <summary>화면에 보일 목표("다음 발견까지" …).</summary>
    public abstract string Name { get; }
    /// <summary>시작할 때(시작 시각 · 그때의 값을 기억).</summary>
    public virtual void Begin(GameWorld w) { }
    /// <summary>목표에 닿았나. newEvents = 지난 확인 뒤에 생긴 세계의 일.</summary>
    public abstract bool Reached(GameWorld w, IReadOnlyList<WorldEvent> newEvents);
    /// <summary>0~1(모르면 음수 — 화면은 흐른 해만 보인다).</summary>
    public virtual double Progress(GameWorld w) => -1;
}

public static class SkipGoals
{
    /// <summary>시간만큼(년).</summary>
    public static SkipGoal Years(int years) => new TimeGoal(years);
    /// <summary>다음 발견(itemId = null 이면 아무거나 — 세계에서 처음 · 다시 찾아냄).</summary>
    public static SkipGoal Discovery(string? itemId) => new DiscoveryGoal(itemId);
    /// <summary>고른 물건(만드는 법)을 아는 사람이 n명 · 서로 다른 말의 무리 g개가 될 때까지.</summary>
    public static SkipGoal Spread(string itemId, int people, int groups) => new SpreadGoal(itemId, people, groups);
    /// <summary>아무도 살지 않던 새 땅으로 옮겨 갈 때까지.</summary>
    public static SkipGoal NewLand() => new EventGoal(WorldEventKind.Moved, "새 땅으로 옮겨 갈 때까지");
    /// <summary>땅에 이름이 붙을 때까지.</summary>
    public static SkipGoal LandNamed() => new EventGoal(WorldEventKind.LandNamed, "땅에 이름이 붙을 때까지");
    /// <summary>말이 갈라질 때까지.</summary>
    public static SkipGoal LanguageSplit() => new EventGoal(WorldEventKind.Language, "말이 갈라질 때까지");
    /// <summary>인구가 n명이 될 때까지.</summary>
    public static SkipGoal Population(int n) => new PopulationGoal(n);

    private sealed class TimeGoal(int years) : SkipGoal
    {
        private long _start;
        public override string Name => $"{years}년";
        public override void Begin(GameWorld w) => _start = w.Now;
        public override bool Reached(GameWorld w, IReadOnlyList<WorldEvent> e) => w.Now - _start >= years * GameTime.TicksPerYear;
        public override double Progress(GameWorld w) => Math.Clamp((w.Now - _start) / (double)(years * GameTime.TicksPerYear), 0, 1);
    }

    private sealed class DiscoveryGoal(string? itemId) : SkipGoal
    {
        public override string Name => itemId is null ? "다음 발견까지" : $"{Data.CraftData.Default.ItemName(Data.CraftData.Default.ItemIndex(itemId))}을(를) 알아낼 때까지";
        public override bool Reached(GameWorld w, IReadOnlyList<WorldEvent> events)
        {
            var c = w.People.Craft;
            foreach (var e in events)
            {
                if (e.Kind is not (WorldEventKind.Discovery or WorldEventKind.Rediscovered)) continue;
                if (itemId is null) return true;
                if ((uint)e.Other < c.Rules.Count && c.Catalog.Items[c.Rules[e.Other].Result].Id == itemId) return true;
            }
            return false;
        }
    }

    private sealed class SpreadGoal(string itemId, int people, int groups) : SkipGoal
    {
        public override string Name => $"{Data.CraftData.Default.ItemName(Data.CraftData.Default.ItemIndex(itemId))}을(를) {people}명 · {groups}무리가 알 때까지";
        private long _lastCheck = long.MinValue;
        public override bool Reached(GameWorld w, IReadOnlyList<WorldEvent> e)
        {
            if (w.Now - _lastCheck < GameTime.TicksPerDay) return false; // 하루에 한 번만 센다
            _lastCheck = w.Now;
            var (n, g) = Count(w);
            return n >= people && g >= groups;
        }
        public override double Progress(GameWorld w) { var (n, g) = Count(w); return Math.Min(n / (double)Math.Max(1, people), g / (double)Math.Max(1, groups)); }
        private (int, int) Count(GameWorld w)
        {
            var c = w.People.Craft;
            int item = c.ItemIndex(itemId);
            var rules = c.Rules.Where(r => r.Result == item).Select(r => r.Index).ToArray();
            int n = 0; var langs = new SortedSet<int>();
            foreach (var p in w.People.All)
            {
                if (p.Dead) continue;
                foreach (int r in rules) if (p.Knows(r)) { n++; langs.Add(p.LangId); break; }
            }
            return (n, langs.Count);
        }
    }

    private sealed class EventGoal(WorldEventKind kind, string name) : SkipGoal
    {
        public override string Name => name;
        public override bool Reached(GameWorld w, IReadOnlyList<WorldEvent> events) => events.Any(e => e.Kind == kind);
    }

    private sealed class PopulationGoal(int n) : SkipGoal
    {
        public override string Name => $"인구 {n}명까지";
        public override bool Reached(GameWorld w, IReadOnlyList<WorldEvent> e) => w.People.AliveCount >= n;
        public override double Progress(GameWorld w) => Math.Clamp(w.People.AliveCount / (double)n, 0, 1);
    }
}

/// <summary>
/// (E4) 넘기기를 돌린다(시뮬 스레드). 하루씩 최대 속도로 나아가며 목표 · 위급 · 그만을 본다. 끝나면 "그동안 일어난 일".
/// 위급 = 사람이 모두 죽음 / 시작 때의 절반 아래로 줄고 5명 이하.
/// </summary>
public sealed class SkipRun
{
    public const int MaxYears = 1000;
    public readonly SkipGoal Goal;
    private readonly long _startTick;
    private readonly int _startPop;
    private long _seenSeq;
    private readonly List<WorldEvent> _all = new();
    private readonly List<WorldEvent> _fresh = new();
    public bool Done { get; private set; }
    public string EndReason { get; private set; } = "";

    public SkipRun(GameWorld w, SkipGoal goal)
    {
        Goal = goal;
        _startTick = w.Now;
        _startPop = w.People.AliveCount;
        _seenSeq = w.People.Events.Count > 0 ? w.People.Events[^1].Seq : 0;
        goal.Begin(w);
    }

    public double YearsPassed(GameWorld w) => (w.Now - _startTick) / (double)GameTime.TicksPerYear;

    /// <summary>하루 나아간다. 끝났으면 true.</summary>
    public bool Step(GameWorld w)
    {
        if (Done) return true;
        w.AdvanceTo(w.Now + GameTime.TicksPerDay);
        _fresh.Clear();
        foreach (var e in w.People.Events) if (e.Seq > _seenSeq) _fresh.Add(e);
        if (_fresh.Count > 0) { _seenSeq = _fresh[^1].Seq; _all.AddRange(_fresh); }
        int alive = w.People.AliveCount;
        if (Goal.Reached(w, _fresh)) Finish("목표에 닿았습니다");
        else if (_startPop > 0 && alive == 0) Finish("위급: 사람이 모두 죽었습니다");
        else if (_startPop > 10 && alive <= 5 && alive * 2 < _startPop) Finish($"위급: 사람이 {_startPop}명에서 {alive}명으로 줄었습니다");
        else if (YearsPassed(w) >= MaxYears) Finish($"{MaxYears}년이 지나도 닿지 못했습니다");
        return Done;
    }

    public void Cancel() => Finish("그만두었습니다");

    private void Finish(string why) { if (!Done) { Done = true; EndReason = why; } }

    /// <summary>그동안 일어난 일(첫 줄 = 끝난 까닭).</summary>
    public List<string> Summary(GameWorld w)
    {
        var lines = new List<string> { $"{EndReason} — {YearsPassed(w):0.#}년이 흘렀습니다 ({GameTime.Format(_startTick)} → {GameTime.Format(w.Now)})" };
        int births = _all.Count(e => e.Kind == WorldEventKind.Birth), deaths = _all.Count(e => e.Kind == WorldEventKind.Death);
        lines.Add($"인구 {_startPop}명 → {w.People.AliveCount}명 (태어남 {births} · 죽음 {deaths})");
        foreach (var k in new[] { WorldEventKind.Discovery, WorldEventKind.Rediscovered, WorldEventKind.DiscoveryLost, WorldEventKind.Language, WorldEventKind.Moved, WorldEventKind.LandNamed, WorldEventKind.DiscoveryGroup })
        {
            var es = _all.Where(e => e.Kind == k).ToList();
            if (es.Count == 0) continue;
            lines.Add($"{EventLevels.Stars(EventLevels.Of(k))} {EventLevels.KindName(k)} {es.Count}번");
            if (EventLevels.Of(k) >= EventLevels.Group)
                foreach (var e in es.Take(k == WorldEventKind.DiscoveryGroup ? 3 : 6)) lines.Add($"   · {GameTime.Format(e.Tick)} — {e.Text}");
        }
        return lines;
    }

    /// <summary>그동안의 일(화면이 "그곳으로 가기"에 쓴다).</summary>
    public IReadOnlyList<WorldEvent> Events => _all;
}
