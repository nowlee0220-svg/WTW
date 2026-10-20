using System.Linq.Expressions;
using System.Reflection;

namespace Wtw.Sim.People;

/// <summary>
/// (S1) 지방마다 CPU: 한 스레드가 지방 하나를 계산할 때 쓰는 것들(그 스레드 전용).
/// 시각(Now) · 측정 셈 · 길 찾기 · 생각 · 판단 후보 · 낱말 기억 같은 임시 버퍼를 지방끼리 나눠 쓰지 않게 한다.
/// 지방 계산 밖(하루 경계 · 넘겨주기 · 개입 · 화면)에서는 PeopleSim 의 "한 줄기" 레인을 쓴다.
/// </summary>
internal sealed class SimLane
{
    public readonly PeopleSim Sim;
    public readonly bool Serial;
    public SimLane(PeopleSim sim, bool serial) { Sim = sim; Serial = serial; }

    /// <summary>지금 계산 중인 지방과 그 시각(지방 계산 중에만).</summary>
    public ProvinceLife? Life;
    public long Now;
    public ProvinceLife? LastLife;

    public PeopleStats Stats = new();
    public bool StatsDirty;
    /// <summary>지방 계산 중 죽은 사람 수 · 처리한 예약 수(맞추는 시각에 세계에 더한다).</summary>
    public int AliveDelta, PregnantDelta;
    public long EventsDone;

    public readonly List<(int X, int Y)> Path = new();
    public readonly List<int> TmpThoughts = new();
    public readonly List<int> RotTiles = new();
    public readonly List<Person> Hunters = new();
    public readonly List<(int Score, int Code)> Opts = new();
    public readonly Dictionary<int, int> Claims = new();
    public readonly Dictionary<(int, int, ulong, int, int), string> WordCache = new();
}

/// <summary>
/// (S1) 지방 계산 중에 생긴 "세계 전체에 남는 일"(알림 · 발견 목록 · 말의 낱말 · 그림 · 지식의 역사 · 부분 성공).
/// 지방끼리 나눠 쓰는 것이라 지방 계산 중에는 바로 바꾸지 않고 모아 두었다가, 맞추는 시각(넘겨주기 시각)에 (시각, 칸, 차례) 순으로 한꺼번에 적용한다
/// → CPU 수와 상관없이 같은 결과. 저장 파일에도 그대로 남는다(맞추는 시각 사이에 저장해도 끊김 없이 이어지게).
/// </summary>
public sealed class PendingOp
{
    public enum Kinds : byte { Notify = 1, Learned = 2, Partial = 3, Painting = 4, History = 5, Hunt = 6 }
    public Kinds Kind;
    public long Tick;
    public int Cell;
    public int A, B, C, D, E, F;
    public long L;
    public string S = "";
}

/// <summary>(S1) 측정 셈(PeopleStats)을 레인에서 세계로 더하고 비우는 함수(필드를 읽어 한 번 만든다 — 셈을 더해도 고칠 곳이 없다).</summary>
internal static class StatsMerge
{
    public static readonly Action<PeopleStats, PeopleStats> AddInto = Build(clear: false);
    public static readonly Action<PeopleStats, PeopleStats> ClearSecond = Build(clear: true);

    private static Action<PeopleStats, PeopleStats> Build(bool clear)
    {
        var to = Expression.Parameter(typeof(PeopleStats), "to");
        var from = Expression.Parameter(typeof(PeopleStats), "from");
        var body = new List<Expression>();
        foreach (var f in typeof(PeopleStats).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (f.FieldType == typeof(long))
            {
                var src = Expression.Field(from, f);
                body.Add(clear ? Expression.Assign(src, Expression.Constant(0L)) : Expression.AddAssign(Expression.Field(to, f), src));
            }
            else if (f.FieldType == typeof(long[]))
            {
                var i = Expression.Variable(typeof(int), "i");
                var a = Expression.Field(from, f);
                var b = Expression.Field(to, f);
                var brk = Expression.Label();
                var loop = Expression.Block(new[] { i },
                    Expression.Assign(i, Expression.Constant(0)),
                    Expression.Loop(Expression.IfThenElse(Expression.LessThan(i, Expression.ArrayLength(a)),
                        Expression.Block(
                            clear ? Expression.Assign(Expression.ArrayAccess(a, i), Expression.Constant(0L))
                                  : Expression.AddAssign(Expression.ArrayAccess(b, i), Expression.ArrayAccess(a, i)),
                            Expression.PostIncrementAssign(i)),
                        Expression.Break(brk)), brk));
                body.Add(loop);
            }
        }
        body.Add(Expression.Empty());
        var fn = Expression.Lambda<Action<PeopleStats, PeopleStats>>(Expression.Block(body), to, from).Compile();
        if (clear) return (_, b) => { fn(b, b); b.FailKeys.Clear(); b.RefoundDelays.Clear(); };
        return (a, b) =>
        {
            fn(a, b);
            foreach (var (k, v) in b.FailKeys) a.FailKeys[k] = a.FailKeys.GetValueOrDefault(k) + v;
            a.RefoundDelays.AddRange(b.RefoundDelays);
        };
    }
}
