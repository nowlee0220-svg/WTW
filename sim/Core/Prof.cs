using System.Diagnostics;

namespace Wtw.Sim.Core;

/// <summary>
/// (S1) 구간별 시간 재기 — 환경 변수 WTW_PROF=1 일 때만 켠다(꺼져 있으면 거의 비용이 없다). 구간 표시는 저장소 코드에 넣지 않고
/// 측정용 사본에 tools/profile/instrument.py 로 넣는다(고치기 전 · 뒤를 같은 자리로 비교). 끝날 때 표를 찍는다.
/// 구간에 들어가고 나올 때 시각을 재어 "그 구간에서만 쓴 시간"(안쪽 구간은 뺀다)을 스레드마다 모은다. 결과에 영향이 없다.
/// </summary>
public static class Prof
{
    public enum S : byte
    {
        Other = 0, Decide, Path, Near, Experiment, Material, Fire, Teach, Paint, Group, Language, Talk, Needs, Mood,
        Cross, Wake, MapGen, Record, Day, Sync, Schedule, Sim, Count,
    }

    public static readonly string[] Names =
    {
        "그 밖(시뮬 밖 · 나머지)", "판단(그 밖)", "길 찾기", "주변 찾기(물 · 먹을 것 · 그늘 · 둘러보기)", "실험 · 만들기(계획)", "재료 구하기", "불 돌보기",
        "가르치기 · 배우기", "그림(그리기 · 보기)", "무리 셈 · 잊기(하루 경계)", "말(닮아 감 · 갈라짐 · 이름 · 지명)", "이야기 · 짝", "욕구 · 기온 셈", "기분 · 생각",
        "지방 넘기(옮겨 가기)", "지방 깨우기 · 경계 만들기", "지방 지도 만들기", "저장용 기록 · 알림", "하루 경계(그 밖)", "맞추는 시각 · CPU 나눔", "예약(스케줄러)", "시뮬 그 밖(예약 처리 · 판단 밖)",
    };

    public static readonly bool On = Environment.GetEnvironmentVariable("WTW_PROF") == "1";

    static Prof()
    {
        if (!On) return;
        var gc0 = GC.GetTotalPauseDuration();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Console.Error.Write(Table(GC.GetTotalPauseDuration() - gc0));
    }

    /// <summary>구간별 표(마크다운): 시뮬 안(그 밖 = 시뮬 밖 빼고)의 몫 · 초 · 불린 수, 메모리 정리(GC) 멈춤.</summary>
    public static string Table(TimeSpan gcPause)
    {
        var r = Snapshot();
        double sim = 0; for (int i = 1; i < r.Length; i++) sim += r[i].Seconds;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"\n## 구간별 시간 (시뮬 안 {sim:0.0}초, 모든 스레드 합)\n");
        sb.AppendLine("| 일 종류 | 몫 | 초 | 불린 수 |");
        sb.AppendLine("|---|---|---|---|");
        foreach (int i in Enumerable.Range(1, r.Length - 1).OrderByDescending(i => r[i].Seconds))
            sb.AppendLine($"| {Names[i]} | {100 * r[i].Seconds / Math.Max(1e-9, sim):0.0}% | {r[i].Seconds:0.0} | {r[i].Calls:N0} |");
        sb.AppendLine($"| (따로) 메모리 정리(GC) 멈춤 | {100 * gcPause.TotalSeconds / Math.Max(1e-9, sim):0.0}% | {gcPause.TotalSeconds:0.0} | |");
        return sb.ToString();
    }

    private sealed class Acc { public readonly long[] Ticks = new long[(int)S.Count]; public readonly long[] Calls = new long[(int)S.Count]; public readonly S[] Stack = new S[256]; public int Depth; public long Last; }
    [ThreadStatic] private static Acc? t_acc;
    private static readonly List<Acc> All = new();

    private static Acc Get()
    {
        var a = t_acc;
        if (a is not null) return a;
        a = new Acc { Last = Stopwatch.GetTimestamp() };
        lock (All) All.Add(a);
        return t_acc = a;
    }

    public readonly struct Scope : IDisposable
    {
        private readonly bool _on;
        internal Scope(bool on) { _on = on; }
        public void Dispose()
        {
            if (!_on) return;
            var a = t_acc!;
            long now = Stopwatch.GetTimestamp();
            a.Ticks[(int)a.Stack[a.Depth]] += now - a.Last;
            a.Last = now;
            if (a.Depth > 0) a.Depth--;
        }
    }

    public static Scope Enter(S s)
    {
        if (!On) return default;
        var a = Get();
        long now = Stopwatch.GetTimestamp();
        a.Ticks[(int)a.Stack[a.Depth]] += now - a.Last;
        a.Last = now;
        if (a.Depth < a.Stack.Length - 1) a.Stack[++a.Depth] = s;
        a.Calls[(int)s]++;
        return new Scope(true);
    }

    /// <summary>모든 스레드의 구간별 (초, 불린 수). 시작 이후 "그 밖"(구간 밖)도 포함.</summary>
    public static (double Seconds, long Calls)[] Snapshot()
    {
        var r = new (double, long)[(int)S.Count];
        lock (All)
            foreach (var a in All)
                for (int i = 0; i < r.Length; i++) r[i] = (r[i].Item1 + a.Ticks[i] / (double)Stopwatch.Frequency, r[i].Item2 + a.Calls[i]);
        return r;
    }

    public static void Reset()
    {
        lock (All) foreach (var a in All) { Array.Clear(a.Ticks); Array.Clear(a.Calls); a.Last = Stopwatch.GetTimestamp(); }
    }
}
