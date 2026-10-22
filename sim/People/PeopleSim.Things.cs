using Wtw.Sim.Core;
using Wtw.Sim.Data;
using Wtw.Sim.Time;

namespace Wtw.Sim.People;

/// <summary>(E6) 사연 있는 물건의 일 한 가지(내력). 값은 저장 파일에 남는다.</summary>
public enum NamedEventKind : byte { Named = 0, Inherited, LeftBehind, Found, Gone, Broke, UsedAgain }

/// <summary>(E6) 사연 있는 물건의 내력 한 줄: 언제 · 무슨 일 · 누가(−1 = 없음) · 어디(칸) · 덧붙임(짐승 · 규칙 번호).</summary>
public readonly record struct NamedEvent(long Tick, NamedEventKind Kind, int Person, int Cell, int Arg);

/// <summary>
/// (E6) 사연 있는 물건(기획서 10절 · 2절 "사연 있는 물건"): 큰 일에 쓰인 물건(곰을 잡은 돌창 · 솜씨 좋은 사람이 빚은 훌륭한 토기 …)에
/// 그 무리의 말로 이름이 붙고 내력이 쌓인다. 물려받고 · 잃고 · 다시 찾는다. 들고 다니는 아이템 하나(같은 종류의 물건 가운데 하나)와 짝이 된다.
/// </summary>
public sealed class NamedItem
{
    public int Id, Item;
    public string Name = "";
    public int[] Concepts = Array.Empty<int>();
    /// <summary>이름이 붙은 까닭: 1 큰 짐승을 잡음(Arg 짐승) · 2 훌륭한 솜씨(Arg 규칙).</summary>
    public int Reason, Arg = -1;
    public int Lang = -1, Maker = -1;
    public long Born;
    /// <summary>지닌 사람(−1 = 땅에 놓임 · 없어짐) · 놓인 곳(칸 · 타일).</summary>
    public int Holder = -1, Cell = -1, Tile = -1;
    public bool Gone;
    public byte Quality = 1;
    public readonly List<NamedEvent> History = new();
}

public sealed partial class PeopleSim
{
    /// <summary>(E6) 사연 있는 물건(번호 순). 새로 생기는 것은 맞추는 시각에만(세계 쪽). 저장한다.</summary>
    public readonly SortedDictionary<int, NamedItem> Named = new();
    public int NextNamedId = 1;

    // ───────── 품질 ─────────

    /// <summary>든 것의 품질(없으면 보통).</summary>
    public int QualityOf(Person p, int item)
    {
        foreach (var (it, q) in p.Quality) if (it == item) return q;
        return 1;
    }

    /// <summary>새로 만든 물건의 품질: 같은 것을 이미 들었으면 더 좋은 쪽(가장 좋은 것을 쓴다). 보통이면 적지 않는다.</summary>
    private void SetQuality(Person p, int item, int q)
    {
        int k = p.Quality.FindIndex(e => e.Item == item);
        if (k >= 0) { if (q > p.Quality[k].Q || p.CarryCount(item) <= 1) p.Quality[k] = (item, (byte)q); }
        else if (q != 1) p.Quality.Add((item, (byte)q));
        p.Quality.RemoveAll(e => e.Q == 1 || (p.CarryCount(e.Item) == 0 && p.Worn != e.Item && p.WornFeet != e.Item));
    }

    /// <summary>
    /// 만든 물건의 품질: 그 일의 솜씨(기술) × 4 + 손재주 + 작업대 + 운 — 나쁨 · 보통 · 좋음 · 훌륭함. 손재주꾼 +, 서툰 손 −.
    /// </summary>
    private int MakeQuality(Person p, CraftRule rule, bool bench, Pcg32 rng)
    {
        int skill = rule.Skill >= 0 ? p.SkillLevel(rule.Skill) : 0;
        int score = skill * 5 + (Ability(p, PeopleData.Dex) - 5) * 4 + (p.HasTrait(T_handy) ? 10 : 0) - (p.HasTrait(T_clumsy) ? 10 : 0) + (bench ? 15 : 0) + rng.NextInt(60);
        return score < 15 ? 0 : score < 55 ? 1 : score < 80 ? 2 : 3;
    }

    /// <summary>품질에 따른 닳는 빠르기(×100): 나쁨 160 · 보통 100 · 좋음 60 · 훌륭함 35.</summary>
    public static int WearPct(int q) => q <= 0 ? 160 : q == 1 ? 100 : q == 2 ? 60 : 35;

    // ───────── 사연 있는 물건 ─────────

    public bool IsNamedHeld(Person p, int item)
    {
        if (p.NamedHeld.Count == 0) return false;
        foreach (int id in p.NamedHeld) if (Named.TryGetValue(id, out var n) && n.Item == item && !n.Gone) return true;
        return false;
    }

    /// <summary>(지방 계산 중) 큰 일에 쓰인 물건에 이름을 붙이려 한다 — 이름은 맞추는 시각에(세계의 목록).</summary>
    private void WantName(Person p, int item, int reason, int arg)
    {
        if (item < 0 || IsNamedHeld(p, item)) return;
        if (InProvince) Defer(new PendingOp { Kind = PendingOp.Kinds.Build, A = 4, B = p.Id, C = item, D = reason, E = arg });
        else NameThing(p, item, reason, arg);
    }

    internal void ApplyNamedOp(PendingOp op)
    {
        if (Get(op.B) is { Dead: false } p) NameThing(p, op.C, op.D, op.E);
    }

    /// <summary>이름을 붙인다: 그 무리의 말로(짐승 + 물건 · 큰 + 물건). 그 사람이 아직 그 물건을 들고 있어야.</summary>
    private void NameThing(Person p, int item, int reason, int arg)
    {
        if (p.CarryCount(item) == 0 || IsNamedHeld(p, item) || !Languages.TryGetValue(p.LangId, out var lang)) return;
        string? cid = ConceptForItem(Craft.Catalog.Items[item].Id);
        int c = cid is null ? C("stone") : C(cid);
        int first = reason == 1 && (uint)arg < (uint)Fauna.Animals.Count ? C(Fauna.Animals[arg].Id) : C("strong");
        if (c < 0 || first < 0) return;
        foreach (int k in new[] { first, c }) if (!lang.Known.Contains(k)) { lang.Known.Add(k); lang.Origins[k] = (-1, Now); lang.Version++; }
        var concepts = new[] { first, c };
        var n = new NamedItem
        {
            Id = NextNamedId++, Item = item, Name = Words.Name(lang, p.Accent, concepts.Select(k => Lang.Concepts[k]).ToList(), Lang), Concepts = concepts,
            Reason = reason, Arg = arg, Lang = p.LangId, Maker = reason == 2 ? p.Id : -1, Born = Now, Holder = p.Id, Cell = p.Cell, Quality = (byte)QualityOf(p, item),
        };
        n.History.Add(new NamedEvent(Now, NamedEventKind.Named, p.Id, p.Cell, arg));
        Named[n.Id] = n;
        p.NamedHeld.Add(n.Id);
        St.NamedMade++;
        AddBio(p, BioKind.NamedThing, n.Id, item);
        Notify(WorldEventKind.NamedThing, p, n.Id, $"{Hangul.I(DisplayName(p))} 지닌 {Craft.ItemName(item)}에 이름이 붙었습니다 — '{n.Name}'({NamedMeaning(n)})");
    }

    /// <summary>"곰을 잡은 돌창" · "훌륭한 토기".</summary>
    public string NamedMeaning(NamedItem n)
    {
        string item = Craft.ItemName(n.Item);
        if (n.Reason == 1 && (uint)n.Arg < (uint)Fauna.Animals.Count) return $"{Hangul.Eul(Fauna.Animals[n.Arg].Name)} 잡은 {item}";
        return $"훌륭한 {item}";
    }

    /// <summary>
    /// (죽을 때) 지닌 사연 있는 물건: 곁의 가족(짝 → 아이 → 부모, 같은 지방에 산 사람)이 물려받는다 — 없으면 그 자리에 남는다(잃음).
    /// 들고 있던 것이 땅에 떨어지기 전에 부른다.
    /// </summary>
    private void PassOnNamed(Person p, ProvinceLife life, int deathTile)
    {
        if (p.NamedHeld.Count == 0) return;
        Person? heir = Here(p.Partner, p) is { Dead: false } m && m.Stage(Now) >= LifeStage.Child ? m : null;
        if (heir is null) foreach (int c in p.Children) if (Here(c, p) is { Dead: false } k && k.Stage(Now) >= LifeStage.Child) { heir = k; break; }
        if (heir is null) foreach (int id in new[] { p.Mother, p.Father }) if (Here(id, p) is { Dead: false } par) { heir = par; break; }
        foreach (int id in p.NamedHeld.ToArray())
        {
            if (!Named.TryGetValue(id, out var n) || n.Gone) continue;
            if (p.CarryCount(n.Item) == 0) continue; // 들고 있지 않았다(하루 경계에 맞춘다)
            if (heir is not null)
            {
                p.RemoveCarry(n.Item, 1);
                heir.AddCarry(n.Item, 1);
                SetQuality(heir, n.Item, n.Quality);
                n.Holder = heir.Id; n.Cell = heir.Cell;
                heir.NamedHeld.Add(id);
                n.History.Add(new NamedEvent(Now, NamedEventKind.Inherited, heir.Id, p.Cell, p.Id));
                AddBio(heir, BioKind.Inherited, id, p.Id);
                St.NamedInherited++;
            }
            else
            {
                n.Holder = -1; n.Cell = p.Cell; n.Tile = deathTile;
                life.NamedGround.Add(id);
                n.History.Add(new NamedEvent(Now, NamedEventKind.LeftBehind, p.Id, p.Cell, -1));
                St.NamedLost++;
            }
        }
        p.NamedHeld.Clear();
    }

    /// <summary>땅에서 물건을 주웠다: 그 자리에 놓인 사연 있는 물건이면 다시 찾았다.</summary>
    /// (S1) 땅에 놓인 것은 그 지방의 목록(NamedGround)에서만 찾는다 — 지방 계산 중에 다른 지방의 물건을 읽지 않게.
    private void PickedUp(Person p, ProvinceLife life, int tile, int item)
    {
        if (life.NamedGround.Count == 0 || !IsMade(item)) return;
        foreach (int id in life.NamedGround)
        {
            var n = Named[id];
            if (n.Gone || n.Holder >= 0 || n.Item != item || n.Tile != tile) continue;
            life.NamedGround.Remove(id);
            n.Holder = p.Id; n.Tile = -1;
            p.NamedHeld.Add(id);
            SetQuality(p, item, n.Quality);
            n.History.Add(new NamedEvent(Now, NamedEventKind.Found, p.Id, p.Cell, -1));
            AddBio(p, BioKind.FoundThing, id, item);
            St.NamedFound++;
            return;
        }
    }

    /// <summary>
    /// 하루 경계(깨어 있는 지방만): 사연 있는 물건이 아직 있나 맞춘다 — 지닌 사람이 더는 들고 있지 않으면(닳아 부러짐 · 다른 것의 재료가 됨 · 내려놓음)
    /// 그 자리에 놓였으면 땅에, 아니면 없어진 것으로.
    /// </summary>
    internal void NamedDaily()
    {
        foreach (var (id, n) in Named)
        {
            if (n.Gone) continue;
            if (n.Holder >= 0)
            {
                var h = Get(n.Holder);
                if (h is null || h.Dead) { n.Holder = -1; continue; }
                n.Cell = h.Cell;
                if (h.CarryCount(n.Item) > 0 || h.Worn == n.Item || h.WornFeet == n.Item) continue;
                h.NamedHeld.Remove(id);
                int t = h.Tile(Now);
                if (Provinces.TryGetValue(h.Cell, out var lh) && lh.Items.TryGetValue(t, out var l) && l.Exists(c => c.Item == n.Item))
                { n.Holder = -1; n.Tile = t; lh.NamedGround.Add(id); n.History.Add(new NamedEvent(Now, NamedEventKind.LeftBehind, h.Id, h.Cell, -1)); St.NamedLost++; }
                else { n.Gone = true; n.Holder = -1; n.History.Add(new NamedEvent(Now, NamedEventKind.Broke, h.Id, h.Cell, -1)); St.NamedGone++; }
            }
            else if (n.Tile >= 0 && Provinces.TryGetValue(n.Cell, out var life))
            {
                if (!(life.Items.TryGetValue(n.Tile, out var l) && l.Exists(c => c.Item == n.Item)))
                { n.Gone = true; life.NamedGround.Remove(id); n.History.Add(new NamedEvent(Now, NamedEventKind.Gone, -1, n.Cell, -1)); St.NamedGone++; }
            }
        }
    }

    /// <summary>지방이 열리거나 깨어날 때: 그 땅에 놓인 사연 있는 물건 목록을 다시 만든다(세계 쪽 — 한 줄기에서).</summary>
    private void FillNamedGround(ProvinceLife life)
    {
        life.NamedGround.Clear();
        foreach (var (id, n) in Named) if (!n.Gone && n.Holder < 0 && n.Cell == life.Cell && n.Tile >= 0) life.NamedGround.Add(id);
    }

    // ───────── 열정 (기획서 7-1 — E3 에서 꺼 둠, E6 에서 다시 시험) ─────────

    /// <summary>
    /// 열정 점수가 오르내리는 일(좋은 일 +, 큰 실패 · 다침 −): 첫 발견 · 훌륭한 물건 · 큰 짐승 · 처음 거둠 · 다 지음 … 쌓이면 그 기술에 열정 🔥(두 번째는 🔥🔥).
    /// 꺼져 있으면(설정) 아무것도 하지 않는다.
    /// </summary>
    internal void PassionEvent(Person p, int skill, int points)
    {
        if (!_w.Config.Passion || skill < 0 || skill >= PeopleData.SkillCount) return;
        p.PassionPoints[skill] += points;
        if (p.Passion[skill] < 2 && p.PassionPoints[skill] >= 120 * (p.Passion[skill] + 1) && Rng(p.Cell).NextInt(100) < 25)
        {
            p.Passion[skill]++;
            p.PassionPoints[skill] = 0;
            AddBio(p, BioKind.Passion, -1, skill);
        }
        else if (p.Passion[skill] > 0 && p.PassionPoints[skill] < -60) { p.Passion[skill]--; p.PassionPoints[skill] = 0; }
    }
}
