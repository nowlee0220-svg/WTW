using System.Text;
using Wtw.Sim.Core;
using Wtw.Sim.Movers;
using Wtw.Sim.People;
using Wtw.Sim.Planet;
using Wtw.Sim.Scheduling;
using Wtw.Sim.World;

namespace Wtw.Sim.Persistence;

/// <summary>
/// 저장 파일. 맨 앞 "WTWS" + 버전 번호. 순서가 정해진 것만 쓰므로 같은 상태 = 같은 바이트.
/// 행성은 칸 하나하나를 저장하지 않는다: 시드 + 설정(땅 칸 수 · 기후 경향)으로 다시 만든다.
/// 나중에 칸이 바뀌게 되면(운석 등) "바뀐 칸 목록"만 덧붙인다 — 자리는 버전 2 에 이미 있다(지금은 늘 0개).
/// 지방 지도도 행성 + 칸으로 다시 만들므로 저장하지 않는다. 버전 3 부터 "바뀐 지방 목록" 자리가 있다(지금은 늘 0개 —
/// 나무를 베는 것 같은 변화는 E3~ 에서 여기에 "바뀐 타일"만 넣는다).
/// 버전 4(E3): 맨 끝에 사람 묶음 — 사람 상한 · 열정 켜짐, 알림, 사람이 사는 지방의 바뀐 것(딴 몫 · 걷힌 숨김), 사람 하나하나의 전부.
/// 버전 5(E3 고침): 말(언어) · 이름의 뜻 · 말씨 · 기억해 둔 물가 · 열매 자리. 버전 4 파일은 지방마다 말 하나를 새로 만들어 읽는다.
/// 버전 6(E3b): 지방 넘기 — 잠든 지방도 같은 목록에(깨어 있든 잠들었든 칸 번호 순), 넘겨주기 대기, 땅 이름, 사람의 옮겨 가기 상태,
///   기억(칸 × 타일 수 + 타일). 버전 5 이하의 기억은 그 사람이 사는 지방의 타일로 읽는다.
/// 버전 7(E4): 지방의 불 · 땅에 놓인 물건 · 캐낸 바위, 발견 목록 · 부분 성공 · 무리마다 처음 안 때, 말의 낱말 출처, 사람의 바람 · 아는 법 · 단서 · 계획 · 불씨 · 그릇의 물 · 별명.
/// 버전 8(E5): 그림 · 무리마다 아는 법(끊김 · 되찾음) · 지식의 역사, 사람의 아는 정도 · 마지막으로 쓴 때 · 어떻게 알았나 · 배우는 중 · 가르치기/그림 시각 · 남기려는 법.
///   버전 7 이하의 아는 법은 처음 알 때의 정도(스스로 80 · 보고 50)로 읽는다.
/// 버전 1(E0) · 2(E1) · 3(E2) 파일도 읽는다(행성 설정은 기본값, 사람 없음).
/// </summary>
public static class SaveGame
{
    public const int Version = 8;
    private static readonly byte[] Magic = "WTWS"u8.ToArray();

    public static byte[] ToBytes(GameWorld w)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true)) Write(bw, w);
        return ms.ToArray();
    }

    public static GameWorld FromBytes(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var br = new BinaryReader(ms, Encoding.UTF8);
        return Read(br);
    }

    public static void SaveFile(GameWorld w, string path)
    {
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, ToBytes(w));
        File.Move(tmp, path, overwrite: true);
    }

    public static GameWorld LoadFile(string path) => FromBytes(File.ReadAllBytes(path));

    /// <summary>상태 지문(저장 바이트의 안정 해시).</summary>
    public static ulong Fingerprint(GameWorld w)
    {
        ulong h = 0;
        foreach (byte b in ToBytes(w)) h = StableHash.Combine(h, b);
        return h;
    }

    private static void Write(BinaryWriter bw, GameWorld w)
    {
        bw.Write(Magic);
        bw.Write(Version);
        bw.Write(w.Seed);
        bw.Write(w.Config.DotCount);
        bw.Write(w.Config.MapTiles);
        bw.Write(w.Config.LandCells);
        bw.Write((int)w.Config.Climate);
        bw.Write(w.Config.PeopleCap);
        bw.Write(w.Config.Passion);
        bw.Write(0); // 바뀐 행성 칸 수 (아직 없음)
        bw.Write(0); // (버전 3 의 자리 — 바뀐 지방은 버전 4 부터 맨 끝 사람 묶음에)
        bw.Write(w.Now);
        bw.Write(w.NextDayBoundary);
        bw.Write(w.DaysElapsed);
        bw.Write(w.EventsProcessed);
        bw.Write(w.Entities.NextId);

        bw.Write(w.Provinces.Count);
        foreach (var p in w.Provinces) { bw.Write(p.Id); bw.Write(p.Name); }

        var streams = w.Rng.Export();
        bw.Write(streams.Count);
        foreach (var s in streams) { bw.Write(s.Name); bw.Write(s.Province); bw.Write(s.State); bw.Write(s.Increment); }

        var events = w.Scheduler.ExportSorted();
        bw.Write(w.Scheduler.NextHandle);
        bw.Write(w.Scheduler.NextOrder);
        bw.Write(events.Length);
        foreach (var e in events) { bw.Write(e.Handle); bw.Write(e.Order); bw.Write(e.Tick); bw.Write(e.Target); bw.Write(e.Kind); }

        var dots = w.Dots.Dots;
        bw.Write(dots.Count);
        foreach (var d in dots)
        {
            bw.Write(d.Id); bw.Write(d.Province); bw.Write(d.TicksPerTile); bw.Write(d.ColorIndex);
            bw.Write(d.Walking); bw.Write(d.PendingHandle);
            bw.Write(d.LegCount);
            for (int k = d.LegCount - 1; k >= 0; k--) // 오래된 것부터
            {
                var leg = d.LegAgo(k);
                bw.Write(leg.Depart); bw.Write(leg.Arrive); bw.Write(leg.PointCount);
                for (int i = 0; i < leg.PointCount; i++) { bw.Write(leg.X[i]); bw.Write(leg.Y[i]); }
            }
        }

        bw.Write(w.Interventions.Count);
        foreach (var iv in w.Interventions)
        {
            bw.Write(iv.Tick); bw.Write(iv.Kind); bw.Write(iv.Values.Length);
            foreach (var v in iv.Values) bw.Write(v);
        }
        WritePeople(bw, w);
    }

    private static void WritePeople(BinaryWriter bw, GameWorld w)
    {
        var ps = w.People;
        bw.Write(ps.NextEventSeq);
        bw.Write(ps.Events.Count);
        foreach (var e in ps.Events) { bw.Write(e.Seq); bw.Write(e.Tick); bw.Write((byte)e.Kind); bw.Write(e.Person); bw.Write(e.Other); bw.Write(e.Cell); bw.Write(e.Text); }
        // 깨어 있든 잠들었든 같은 모양으로(칸 번호 순) — 잠든 상태는 결과에 영향이 없으므로 저장 바이트도 같게
        var cells = ps.OpenedProvinces().ToList();
        bw.Write(cells.Count);
        foreach (int cell in cells)
        {
            bw.Write(cell);
            if (ps.Provinces.TryGetValue(cell, out var life))
            {
                bw.Write(life.Stocks.Count);
                foreach (var (tile, st) in life.Stocks) { bw.Write(tile); bw.Write(st.Stock); bw.Write(st.Stamp); }
                bw.Write(life.Revealed.Count);
                foreach (int t in life.Revealed) bw.Write(t);
                bw.Write(life.Fires.Count);
                foreach (var (t, f) in life.Fires) WriteFire(bw, t, f);
                int ni = 0; foreach (var (_, l) in life.Items) ni += l.Count;
                bw.Write(ni);
                foreach (var (t, l) in life.Items) foreach (var (item, n) in l) { bw.Write(t); bw.Write(item); bw.Write(n); }
                bw.Write(life.Dug.Count);
                foreach (int t in life.Dug) bw.Write(t);
            }
            else
            {
                var d = ps.Dormant[cell];
                bw.Write(d.Stocks.Length);
                foreach (var (tile, st) in d.Stocks) { bw.Write(tile); bw.Write(st.Stock); bw.Write(st.Stamp); }
                bw.Write(d.Revealed.Length);
                foreach (int t in d.Revealed) bw.Write(t);
                bw.Write(d.Fires.Length);
                foreach (var (t, f) in d.Fires) WriteFire(bw, t, f);
                bw.Write(d.Ground.Length);
                foreach (var (t, item, n) in d.Ground) { bw.Write(t); bw.Write(item); bw.Write(n); }
                bw.Write(d.Dug.Length);
                foreach (int t in d.Dug) bw.Write(t);
            }
        }
        // (E4) 발견 목록 · 부분 성공 · 무리마다 처음
        bw.Write(ps.Discoveries.Count);
        foreach (var (rule, d) in ps.Discoveries)
        {
            bw.Write(rule); bw.Write(d.Tick); bw.Write(d.Person); bw.Write(d.Cell); bw.Write(d.Wish); bw.Write(d.LangId); bw.Write(d.PartialsBefore);
            bw.Write(d.PartialTexts.Count); foreach (var t in d.PartialTexts) bw.Write(t);
            bw.Write(d.Lost); bw.Write(d.LostTick); bw.Write(d.LostCount); bw.Write(d.RefoundCount);
        }
        bw.Write(ps.Partials.Count);
        foreach (var (rule, pr) in ps.Partials) { bw.Write(rule); bw.Write(pr.Count); bw.Write(pr.Texts.Count); foreach (var t in pr.Texts) bw.Write(t); }
        bw.Write(ps.GroupFirst.Count);
        foreach (var (k, v) in ps.GroupFirst) { bw.Write(k); bw.Write(v); }
        // (E5) 그림 · 무리마다 아는 법 · 지식의 역사
        bw.Write(ps.NextPaintingId);
        bw.Write(ps.Paintings.Count);
        foreach (var (_, g) in ps.Paintings)
        {
            bw.Write(g.Id); bw.Write(g.Cell); bw.Write(g.Tile); bw.Write(g.Stand); bw.Write(g.Rule); bw.Write(g.Painter); bw.Write(g.Medium);
            bw.Write(g.Tick); bw.Write(g.Until); bw.Write(g.Seen); bw.Write(g.Taught);
        }
        bw.Write(ps.GroupKnows.Count);
        foreach (var (k, g) in ps.GroupKnows) { bw.Write(k); bw.Write(g.Known); bw.Write(g.Knowers); bw.Write(g.Since); bw.Write(g.LostTick); bw.Write(g.LostCount); bw.Write(g.RefoundCount); bw.Write(g.EmptySince); }
        bw.Write(ps.KnowHistory.Count);
        foreach (var (rule, list) in ps.KnowHistory)
        {
            bw.Write(rule); bw.Write(list.Count);
            foreach (var e in list) { bw.Write(e.Tick); bw.Write(e.Kind); bw.Write(e.Cell); bw.Write(e.Text); }
        }
        var (queue, at) = ps.CrossQueueForSave();
        bw.Write(at); bw.Write(queue.Length); foreach (int id in queue) bw.Write(id);
        bw.Write(ps.LandNames.Count);
        foreach (var (cell, list) in ps.LandNames)
        {
            bw.Write(cell); bw.Write(list.Count);
            foreach (var n in list)
            {
                bw.Write(n.LangId); bw.Write(n.Word); bw.Write(n.Concepts.Length); foreach (int c in n.Concepts) bw.Write(c);
                bw.Write(n.Tick); bw.Write(n.Namer); bw.Write(n.Reason);
            }
        }
        bw.Write(ps.NextLanguageId);
        bw.Write(ps.Languages.Count);
        foreach (var (_, l) in ps.Languages)
        {
            bw.Write(l.Id); bw.Write(l.Seed); bw.Write(l.Parent); bw.Write(l.Born); bw.Write(l.Length);
            bw.Write(l.Sounds.Length); foreach (int v in l.Sounds) bw.Write(v);
            bw.Write(l.Known.Count); foreach (int k in l.Known) bw.Write(k);
            bw.Write(l.Overrides.Count); foreach (var (k, v) in l.Overrides) { bw.Write(k); bw.Write(v); }
            bw.Write(l.Changes);
            bw.Write(l.Origins.Count); foreach (var (k, v) in l.Origins) { bw.Write(k); bw.Write(v.FromLang); bw.Write(v.Tick); }
            bw.Write(l.Loans.Count); foreach (var (k, v) in l.Loans) { bw.Write(k); bw.Write(v); }
        }
        bw.Write(ps.All.Count);
        foreach (var p in ps.All) WritePerson(bw, p);
    }

    private static void WriteFire(BinaryWriter bw, int tile, Fire f) { bw.Write(tile); bw.Write(f.Kind); bw.Write(f.Stamp); bw.Write(f.Fuel); bw.Write(f.Charcoal); bw.Write(f.Lighter); }
    private static (int, Fire) ReadFire(BinaryReader br) => (br.ReadInt32(), new Fire { Kind = br.ReadByte(), Stamp = br.ReadInt64(), Fuel = br.ReadInt64(), Charcoal = br.ReadBoolean(), Lighter = br.ReadInt32() });

    private static void WritePerson(BinaryWriter bw, Person p)
    {
        bw.Write(p.Id); bw.Write(p.Name); bw.Write((byte)p.Sex); bw.Write(p.BirthTick); bw.Write(p.Cell);
        bw.Write(p.NameConcepts.Length); foreach (int c in p.NameConcepts) bw.Write(c);
        bw.Write(p.NameFrom); bw.Write(p.NamedBy); bw.Write(p.LangId);
        bw.Write(p.Accent.Length); foreach (sbyte a in p.Accent) bw.Write(a);
        bw.Write(p.TalkConcept); bw.Write(p.SearchDir); bw.Write(p.How); bw.Write(p.HowArg);
        bw.Write(p.KnownWater.Count); foreach (int t in p.KnownWater) bw.Write(t);
        bw.Write(p.KnownFood.Count); foreach (int t in p.KnownFood) bw.Write(t);
        bw.Write(p.FromCell); bw.Write(p.ArrivedTick); bw.Write(p.SearchMiss); bw.Write(p.Scarcity); bw.Write(p.LastMove); bw.Write((byte)p.MoveWhy); bw.Write(p.MoveTo);
        var l = p.Look;
        bw.Write(l.Skin); bw.Write(l.Hair); bw.Write(l.HairStyle); bw.Write(l.Build); bw.Write(l.Height); bw.Write(l.Face); bw.Write(l.Eyes); bw.Write(l.Beard);
        foreach (int v in p.BaseAbility) bw.Write(v);
        bw.Write(p.Traits.Count); foreach (int t in p.Traits) bw.Write(t);
        foreach (int v in p.SkillXp) bw.Write(v);
        foreach (byte v in p.Passion) bw.Write(v);
        foreach (int v in p.PassionPoints) bw.Write(v);
        bw.Write(p.Food); bw.Write(p.Water); bw.Write(p.Rest); bw.Write(p.Joy); bw.Write(p.Social);
        bw.Write(p.Malnutrition); bw.Write(p.Dehydration); bw.Write(p.Hypothermia); bw.Write(p.Heatstroke); bw.Write(p.Health);
        bw.Write(p.IllUntil); bw.Write(p.IllDamage); bw.Write(p.LastUpdate); bw.Write(p.LastTalkTick); bw.Write(p.LastGather);
        bw.Write((byte)p.Act); bw.Write(p.Walking); bw.Write(p.ActUntil); bw.Write(p.Target); bw.Write(p.TargetB); bw.Write((byte)p.Why); bw.Write(p.WhyArg);
        bw.Write(p.PendingHandle); bw.Write(p.Asleep); bw.Write(p.SleepShelter); bw.Write(p.SleptCold); bw.Write(p.Huddle);
        bw.Write(p.Partner); bw.Write(p.Mother); bw.Write(p.Father); bw.Write(p.Carrier);
        bw.Write(p.Children.Count); foreach (int c in p.Children) bw.Write(c);
        bw.Write(p.Conceived); bw.Write(p.ChildFather);
        bw.Write(p.Carry.Count); foreach (var (item, count) in p.Carry) { bw.Write(item); bw.Write(count); }
        bw.Write(p.Thoughts.Count); foreach (var t in p.Thoughts) { bw.Write(t.Def); bw.Write(t.Until); bw.Write(t.Other); }
        bw.Write(p.Relations.Count); foreach (var r in p.Relations) { bw.Write(r.Other); bw.Write(r.Opinion); bw.Write(r.Grudge); bw.Write(r.Favor); bw.Write(r.Talks); }
        bw.Write(p.Bio.Count); foreach (var b in p.Bio) { bw.Write(b.Tick); bw.Write((byte)b.Kind); bw.Write(b.Other); bw.Write(b.Param); }
        bw.Write(p.BreakUntil); bw.Write(p.Breaks); bw.Write(p.LastBreak); bw.Write(p.FoundCave);
        bw.Write(p.Dead); bw.Write(p.DeathTick); bw.Write((byte)p.Cause); bw.Write(p.Eaten); bw.Write(p.BedShift); bw.Write(p.WakeShift);
        bw.Write(p.MotionCount);
        for (int k = p.MotionCount - 1; k >= 0; k--)
        {
            var m = p.MotionAgo(k);
            bw.Write(m.Depart); bw.Write(m.Arrive); bw.Write(m.Count);
            for (int i = 0; i < m.Count; i++) { bw.Write(m.X[i]); bw.Write(m.Y[i]); }
        }
        // (E4)
        bw.Write(p.Wish.Length); for (int i = 0; i < p.Wish.Length; i++) { bw.Write(p.Wish[i]); bw.Write(p.WishSince[i]); }
        bw.Write(p.Known.Count); foreach (var k in p.Known) { bw.Write(k.Rule); bw.Write(k.Tick); bw.Write(k.Wish); bw.Write(k.Input); bw.Write(k.With); bw.Write(k.LearnedFrom); }
        bw.Write(p.Clues.Count); foreach (var c in p.Clues) { bw.Write(c.Rule); bw.Write(c.Key); bw.Write(c.Tick); bw.Write(c.Input); }
        bw.Write(p.PlanAction); bw.Write(p.PlanInput); bw.Write(p.PlanWith); bw.Write(p.PlanTool); bw.Write(p.PlanWish); bw.Write(p.PlanRule); bw.Write(p.PlanHeat); bw.Write(p.PlanItem);
        bw.Write(p.LastExperiment); bw.Write(p.LastCraftFail); bw.Write(p.MissKey); bw.Write(p.MissTick); bw.Write(p.Experiments); bw.Write(p.EmberTick); bw.Write(p.PotWater); bw.Write(p.Mined);
        bw.Write(p.Nickname); bw.Write(p.NicknameConcepts.Length); foreach (int c in p.NicknameConcepts) bw.Write(c); bw.Write(p.NicknameReason);
        // (E5)
        foreach (var k in p.Known) { bw.Write(k.Level); bw.Write(k.LastUse); bw.Write((byte)k.Via); }
        bw.Write(p.Learning.Count); foreach (var ln in p.Learning) { bw.Write(ln.Rule); bw.Write(ln.Progress); bw.Write(ln.From); bw.Write((byte)ln.Via); bw.Write(ln.Tick); }
        bw.Write(p.LastTeach); bw.Write(p.LastPaint); bw.Write(p.LastStudy); bw.Write(p.RecordRule); bw.Write((byte)p.RecordWhy);
    }

    private static void ReadPeople(BinaryReader br, GameWorld w, int version)
    {
        var ps = w.People;
        ps.NextEventSeq = br.ReadInt64();
        int ne = br.ReadInt32();
        for (int i = 0; i < ne; i++)
            ps.AddLoadedEvent(new WorldEvent(br.ReadInt64(), br.ReadInt64(), (WorldEventKind)br.ReadByte(), br.ReadInt32(), br.ReadInt32(), br.ReadInt32(), br.ReadString()));
        int np = br.ReadInt32();
        for (int i = 0; i < np; i++)
        {
            // 모두 잠든 지방으로 읽고, 산 사람이 들어오면(AddLoaded) 깨어난다
            int cell = br.ReadInt32();
            int ns = br.ReadInt32();
            var stocks = new (int, TileStock)[ns];
            for (int k = 0; k < ns; k++) stocks[k] = (br.ReadInt32(), new TileStock { Stock = br.ReadInt32(), Stamp = br.ReadInt64() });
            int nr = br.ReadInt32();
            var rev = new int[nr];
            for (int k = 0; k < nr; k++) rev[k] = br.ReadInt32();
            var dp = new DormantProvince(cell, stocks, rev, new List<Person>());
            if (version >= 7)
            {
                var fires = new (int, Fire)[br.ReadInt32()];
                for (int k = 0; k < fires.Length; k++) fires[k] = ReadFire(br);
                var ground = new (int, int, int)[br.ReadInt32()];
                for (int k = 0; k < ground.Length; k++) ground[k] = (br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
                var dug = new int[br.ReadInt32()];
                for (int k = 0; k < dug.Length; k++) dug[k] = br.ReadInt32();
                dp = dp with { Fires = fires, Ground = ground, Dug = dug };
            }
            ps.Dormant[cell] = dp;
        }
        if (version >= 7)
        {
            int nd = br.ReadInt32();
            for (int i = 0; i < nd; i++)
            {
                var d = new Discovery { Rule = br.ReadInt32(), Tick = br.ReadInt64(), Person = br.ReadInt32(), Cell = br.ReadInt32(), Wish = br.ReadInt32(), LangId = br.ReadInt32(), PartialsBefore = br.ReadInt32() };
                int nt = br.ReadInt32(); for (int k = 0; k < nt; k++) d.PartialTexts.Add(br.ReadString());
                d.Lost = br.ReadBoolean(); d.LostTick = br.ReadInt64(); d.LostCount = br.ReadInt32(); d.RefoundCount = br.ReadInt32();
                ps.Discoveries[d.Rule] = d;
            }
            int npa = br.ReadInt32();
            for (int i = 0; i < npa; i++)
            {
                int rule = br.ReadInt32(), cnt = br.ReadInt32(); var texts = new List<string>();
                int nt = br.ReadInt32(); for (int k = 0; k < nt; k++) texts.Add(br.ReadString());
                ps.Partials[rule] = (cnt, texts);
            }
            int ng = br.ReadInt32();
            for (int i = 0; i < ng; i++) ps.GroupFirst[br.ReadInt64()] = br.ReadInt64();
        }
        if (version >= 8)
        {
            ps.NextPaintingId = br.ReadInt32();
            int npt = br.ReadInt32();
            for (int i = 0; i < npt; i++)
            {
                var g = new Painting { Id = br.ReadInt32(), Cell = br.ReadInt32(), Tile = br.ReadInt32(), Stand = br.ReadInt32(), Rule = br.ReadInt32(), Painter = br.ReadInt32(), Medium = br.ReadByte(),
                    Tick = br.ReadInt64(), Until = br.ReadInt64(), Seen = br.ReadInt32(), Taught = br.ReadInt32() };
                ps.Paintings[g.Id] = g;
            }
            int ngk = br.ReadInt32();
            for (int i = 0; i < ngk; i++)
                ps.GroupKnows[br.ReadInt64()] = new GroupKnow { Known = br.ReadBoolean(), Knowers = br.ReadInt32(), Since = br.ReadInt64(), LostTick = br.ReadInt64(), LostCount = br.ReadInt32(), RefoundCount = br.ReadInt32(), EmptySince = br.ReadInt64() };
            int nh = br.ReadInt32();
            for (int i = 0; i < nh; i++)
            {
                int rule = br.ReadInt32(), cnt = br.ReadInt32();
                var list = new List<KnowEvent>(cnt);
                for (int k = 0; k < cnt; k++) list.Add(new KnowEvent(br.ReadInt64(), br.ReadByte(), br.ReadInt32(), br.ReadString()));
                ps.KnowHistory[rule] = list;
            }
        }
        if (version >= 6)
        {
            long at = br.ReadInt64();
            int nq = br.ReadInt32();
            var q = new int[nq];
            for (int k = 0; k < nq; k++) q[k] = br.ReadInt32();
            ps.LoadCrossQueue(q, at);
            int nn = br.ReadInt32();
            for (int i = 0; i < nn; i++)
            {
                int cell = br.ReadInt32(), cnt = br.ReadInt32();
                for (int k = 0; k < cnt; k++)
                {
                    int lid = br.ReadInt32(); string word = br.ReadString();
                    var cs = new int[br.ReadInt32()];
                    for (int j = 0; j < cs.Length; j++) cs[j] = br.ReadInt32();
                    ps.AddLoadedLandName(cell, new LandName(lid, word, cs, br.ReadInt64(), br.ReadInt32(), br.ReadString()));
                }
            }
        }
        if (version >= 5)
        {
            ps.NextLanguageId = br.ReadInt32();
            int nl = br.ReadInt32();
            for (int i = 0; i < nl; i++)
            {
                var l = new Language { Id = br.ReadInt32(), Seed = br.ReadUInt64(), Parent = br.ReadInt32(), Born = br.ReadInt64(), Length = br.ReadInt32() };
                l.Sounds = new int[br.ReadInt32()];
                if (l.Sounds.Length != ps.Lang.SoundCount) throw new InvalidDataException("저장 파일의 말(소리 수)이 지금 데이터와 다릅니다.");
                for (int k = 0; k < l.Sounds.Length; k++) l.Sounds[k] = br.ReadInt32();
                int nk = br.ReadInt32(); for (int k = 0; k < nk; k++) l.Known.Add(br.ReadInt32());
                int no = br.ReadInt32(); for (int k = 0; k < no; k++) l.Overrides[br.ReadInt32()] = br.ReadUInt64();
                if (version >= 6) l.Changes = br.ReadInt32();
                if (version >= 7)
                {
                    int nor = br.ReadInt32(); for (int k = 0; k < nor; k++) { int c = br.ReadInt32(); l.Origins[c] = (br.ReadInt32(), br.ReadInt64()); }
                    int nlo = br.ReadInt32(); for (int k = 0; k < nlo; k++) { int c = br.ReadInt32(); l.Loans[c] = br.ReadString(); }
                }
                ps.AddLoadedLanguage(l);
            }
        }
        int count = br.ReadInt32();
        var byCellLang = new SortedDictionary<int, int>();
        for (int i = 0; i < count; i++)
        {
            var p = ReadPerson(br, version);
            if (version < 5) // 버전 4: 말이 없던 때 — 지방마다 말 하나
            {
                if (!byCellLang.TryGetValue(p.Cell, out int lid)) { lid = ps.NewLanguageForLoad(); byCellLang[p.Cell] = lid; }
                p.LangId = lid;
            }
            ps.AddLoaded(p);
        }
    }

    private static Person ReadPerson(BinaryReader br, int version)
    {
        var p = new Person { Id = br.ReadInt32(), Name = br.ReadString(), Sex = (Sex)br.ReadByte(), BirthTick = br.ReadInt64(), Cell = br.ReadInt32() };
        if (version >= 5)
        {
            p.NameConcepts = new int[br.ReadInt32()];
            for (int k = 0; k < p.NameConcepts.Length; k++) p.NameConcepts[k] = br.ReadInt32();
            p.NameFrom = br.ReadInt32(); p.NamedBy = br.ReadInt32(); p.LangId = br.ReadInt32();
            int na = br.ReadInt32();
            if (na != p.Accent.Length) throw new InvalidDataException("저장 파일의 말씨(소리 수)가 지금 데이터와 다릅니다.");
            for (int k = 0; k < na; k++) p.Accent[k] = br.ReadSByte();
            p.TalkConcept = br.ReadInt32(); p.SearchDir = br.ReadInt32(); p.How = br.ReadByte(); p.HowArg = br.ReadInt32();
            int nw = br.ReadInt32(); for (int k = 0; k < nw; k++) p.KnownWater.Add(br.ReadInt32());
            int nf = br.ReadInt32(); for (int k = 0; k < nf; k++) p.KnownFood.Add(br.ReadInt32());
            if (version < 6)
            {
                for (int k = 0; k < p.KnownWater.Count; k++) p.KnownWater[k] = Person.Place(p.Cell, p.KnownWater[k]);
                for (int k = 0; k < p.KnownFood.Count; k++) p.KnownFood[k] = Person.Place(p.Cell, p.KnownFood[k]);
            }
            else
            {
                p.FromCell = br.ReadInt32(); p.ArrivedTick = br.ReadInt64(); p.SearchMiss = br.ReadInt32(); p.Scarcity = br.ReadInt32();
                p.LastMove = br.ReadInt64(); p.MoveWhy = (Why)br.ReadByte(); p.MoveTo = br.ReadInt32();
            }
        }
        p.Look = new Appearance(br.ReadByte(), br.ReadByte(), br.ReadByte(), br.ReadByte(), br.ReadByte(), br.ReadByte(), br.ReadByte(), br.ReadByte());
        for (int k = 0; k < p.BaseAbility.Length; k++) p.BaseAbility[k] = br.ReadInt32();
        int nt = br.ReadInt32(); for (int k = 0; k < nt; k++) p.Traits.Add(br.ReadInt32());
        for (int k = 0; k < p.SkillXp.Length; k++) p.SkillXp[k] = br.ReadInt32();
        for (int k = 0; k < p.Passion.Length; k++) p.Passion[k] = br.ReadByte();
        for (int k = 0; k < p.PassionPoints.Length; k++) p.PassionPoints[k] = br.ReadInt32();
        p.Food = br.ReadInt32(); p.Water = br.ReadInt32(); p.Rest = br.ReadInt32(); p.Joy = br.ReadInt32(); p.Social = br.ReadInt32();
        p.Malnutrition = br.ReadInt32(); p.Dehydration = br.ReadInt32(); p.Hypothermia = br.ReadInt32(); p.Heatstroke = br.ReadInt32(); p.Health = br.ReadInt32();
        p.IllUntil = br.ReadInt64(); p.IllDamage = br.ReadInt32(); p.LastUpdate = br.ReadInt64(); p.LastTalkTick = br.ReadInt64(); p.LastGather = br.ReadInt64();
        p.Act = (Act)br.ReadByte(); p.Walking = br.ReadBoolean(); p.ActUntil = br.ReadInt64(); p.Target = br.ReadInt32(); p.TargetB = br.ReadInt32(); p.Why = (Why)br.ReadByte(); p.WhyArg = br.ReadInt32();
        p.PendingHandle = br.ReadInt64(); p.Asleep = br.ReadBoolean(); p.SleepShelter = br.ReadInt32(); p.SleptCold = br.ReadBoolean(); p.Huddle = br.ReadBoolean();
        p.Partner = br.ReadInt32(); p.Mother = br.ReadInt32(); p.Father = br.ReadInt32(); p.Carrier = br.ReadInt32();
        int nc = br.ReadInt32(); for (int k = 0; k < nc; k++) p.Children.Add(br.ReadInt32());
        p.Conceived = br.ReadInt64(); p.ChildFather = br.ReadInt32();
        int ny = br.ReadInt32(); for (int k = 0; k < ny; k++) p.Carry.Add((br.ReadInt32(), br.ReadInt32()));
        int nth = br.ReadInt32(); for (int k = 0; k < nth; k++) p.Thoughts.Add(new ThoughtInst { Def = br.ReadInt32(), Until = br.ReadInt64(), Other = br.ReadInt32() });
        int nrel = br.ReadInt32(); for (int k = 0; k < nrel; k++) p.Relations.Add(new Relation { Other = br.ReadInt32(), Opinion = br.ReadInt32(), Grudge = br.ReadInt32(), Favor = br.ReadInt32(), Talks = br.ReadInt32() });
        int nb = br.ReadInt32(); for (int k = 0; k < nb; k++) p.Bio.Add(new BioEntry { Tick = br.ReadInt64(), Kind = (BioKind)br.ReadByte(), Other = br.ReadInt32(), Param = br.ReadInt32() });
        p.BreakUntil = br.ReadInt64(); p.Breaks = br.ReadInt32(); p.LastBreak = br.ReadInt64(); p.FoundCave = br.ReadBoolean();
        p.Dead = br.ReadBoolean(); p.DeathTick = br.ReadInt64(); p.Cause = (DeathCause)br.ReadByte(); p.Eaten = br.ReadBoolean(); p.BedShift = br.ReadInt32(); p.WakeShift = br.ReadInt32();
        int nm = br.ReadInt32();
        if (nm < 1 || nm > Person.History) throw new InvalidDataException("저장 파일이 손상되었습니다(사람 움직임).");
        for (int k = 0; k < nm; k++)
        {
            long dep = br.ReadInt64(), arr = br.ReadInt64();
            int n = br.ReadInt32();
            if (n < 1 || n > 100_000) throw new InvalidDataException("저장 파일이 손상되었습니다(사람 길).");
            var xs = new int[n]; var ys = new int[n];
            for (int j = 0; j < n; j++) { xs[j] = br.ReadInt32(); ys[j] = br.ReadInt32(); }
            p.NewMotion().Set(dep, arr, xs, ys, n);
        }
        if (version >= 7)
        {
            int nw = br.ReadInt32();
            if (nw != p.Wish.Length) throw new InvalidDataException("저장 파일의 바람 수가 지금 데이터와 다릅니다.");
            for (int i = 0; i < nw; i++) { p.Wish[i] = br.ReadInt32(); p.WishSince[i] = br.ReadInt64(); }
            int nk = br.ReadInt32(); for (int k = 0; k < nk; k++) p.Known.Add(new Knowledge { Rule = br.ReadInt32(), Tick = br.ReadInt64(), Wish = br.ReadInt32(), Input = br.ReadInt32(), With = br.ReadInt32(), LearnedFrom = br.ReadInt32() });
            int ncl = br.ReadInt32(); for (int k = 0; k < ncl; k++) p.Clues.Add(new Clue { Rule = br.ReadInt32(), Key = br.ReadString(), Tick = br.ReadInt64(), Input = br.ReadInt32() });
            p.PlanAction = br.ReadInt32(); p.PlanInput = br.ReadInt32(); p.PlanWith = br.ReadInt32(); p.PlanTool = br.ReadInt32(); p.PlanWish = br.ReadInt32(); p.PlanRule = br.ReadInt32(); p.PlanHeat = br.ReadInt32(); p.PlanItem = br.ReadInt32();
            p.LastExperiment = br.ReadInt64(); p.LastCraftFail = br.ReadInt64(); p.MissKey = br.ReadInt64(); p.MissTick = br.ReadInt64(); p.Experiments = br.ReadInt32(); p.EmberTick = br.ReadInt64(); p.PotWater = br.ReadInt32(); p.Mined = br.ReadInt32();
            p.Nickname = br.ReadString(); p.NicknameConcepts = new int[br.ReadInt32()]; for (int k = 0; k < p.NicknameConcepts.Length; k++) p.NicknameConcepts[k] = br.ReadInt32(); p.NicknameReason = br.ReadString();
        }
        if (version >= 8)
        {
            for (int i = 0; i < p.Known.Count; i++) { var k = p.Known[i]; k.Level = br.ReadInt32(); k.LastUse = br.ReadInt64(); k.Via = (Via)br.ReadByte(); p.Known[i] = k; }
            int nl = br.ReadInt32(); for (int k = 0; k < nl; k++) p.Learning.Add(new Learning { Rule = br.ReadInt32(), Progress = br.ReadInt32(), From = br.ReadInt32(), Via = (Via)br.ReadByte(), Tick = br.ReadInt64() });
            p.LastTeach = br.ReadInt64(); p.LastPaint = br.ReadInt64(); p.LastStudy = br.ReadInt64(); p.RecordRule = br.ReadInt32(); p.RecordWhy = (Why)br.ReadByte();
        }
        else
            for (int i = 0; i < p.Known.Count; i++) { var k = p.Known[i]; k.Via = k.LearnedFrom < 0 ? Via.Self : Via.Watch; k.Level = PeopleSim.StartLevel(k.Via); k.LastUse = k.Tick; p.Known[i] = k; }
        return p;
    }

    private static GameWorld Read(BinaryReader br)
    {
        var magic = br.ReadBytes(4);
        if (!magic.AsSpan().SequenceEqual(Magic)) throw new InvalidDataException("WTW 저장 파일이 아닙니다.");
        int version = br.ReadInt32();
        if (version < 1 || version > Version) throw new InvalidDataException($"저장 파일 버전({version})을 이 게임 버전({Version})에서 읽을 수 없습니다.");

        ulong seed = br.ReadUInt64();
        var config = new WorldConfig { DotCount = br.ReadInt32(), MapTiles = br.ReadInt32() };
        if (version >= 2)
        {
            config = config with { LandCells = br.ReadInt32(), Climate = (ClimateBias)br.ReadInt32() };
            if (config.LandCells < PlanetSettings.MinLand || config.LandCells > PlanetSettings.MaxLand || !Enum.IsDefined(config.Climate))
                throw new InvalidDataException("저장 파일이 손상되었습니다(행성 설정).");
            if (version >= 4)
            {
                config = config with { PeopleCap = br.ReadInt32(), Passion = br.ReadBoolean() };
                if (config.PeopleCap < WorldConfig.MinCap || config.PeopleCap > WorldConfig.MaxCap) throw new InvalidDataException("저장 파일이 손상되었습니다(사람 상한).");
            }
            if (br.ReadInt32() != 0) throw new InvalidDataException("저장 파일이 손상되었습니다(바뀐 행성 칸).");
        }
        if (version >= 3 && br.ReadInt32() != 0) throw new InvalidDataException("저장 파일이 손상되었습니다(바뀐 지방).");
        long now = br.ReadInt64(), nextDay = br.ReadInt64(), days = br.ReadInt64(), events = br.ReadInt64();
        var w = GameWorld.CreateEmpty(seed, config, now, nextDay, days, events);
        w.Entities.NextId = br.ReadInt32();

        int provinces = br.ReadInt32();
        for (int i = 0; i < provinces; i++) w.Provinces.Add(new Province(br.ReadInt32(), br.ReadString()));

        int streams = br.ReadInt32();
        for (int i = 0; i < streams; i++) w.Rng.Import(br.ReadString(), br.ReadInt32(), br.ReadUInt64(), br.ReadUInt64());

        long nextHandle = br.ReadInt64(), nextOrder = br.ReadInt64();
        int count = br.ReadInt32();
        var list = new List<ScheduledEvent>(count);
        for (int i = 0; i < count; i++)
            list.Add(new ScheduledEvent(br.ReadInt64(), br.ReadInt64(), br.ReadInt64(), br.ReadInt32(), br.ReadInt32()));
        w.Scheduler.Import(list, nextHandle, nextOrder);

        int dots = br.ReadInt32();
        Span<int> xs = stackalloc int[Leg.MaxPoints];
        Span<int> ys = stackalloc int[Leg.MaxPoints];
        for (int i = 0; i < dots; i++)
        {
            var d = new Dot
            {
                Id = br.ReadInt32(), Province = br.ReadInt32(), TicksPerTile = br.ReadInt32(), ColorIndex = br.ReadInt32(),
                Walking = br.ReadBoolean(), PendingHandle = br.ReadInt64(),
            };
            int legs = br.ReadInt32();
            for (int k = 0; k < legs; k++)
            {
                long dep = br.ReadInt64(), arr = br.ReadInt64();
                int n = br.ReadInt32();
                if (n < 1 || n > Leg.MaxPoints) throw new InvalidDataException("저장 파일이 손상되었습니다(경로 점 개수).");
                for (int j = 0; j < n; j++) { xs[j] = br.ReadInt32(); ys[j] = br.ReadInt32(); }
                d.NewLeg().Set(dep, arr, xs[..n], ys[..n]);
            }
            w.Dots.AddLoaded(d);
        }

        int ivs = br.ReadInt32();
        for (int i = 0; i < ivs; i++)
        {
            long tick = br.ReadInt64();
            string kind = br.ReadString();
            var vals = new long[br.ReadInt32()];
            for (int j = 0; j < vals.Length; j++) vals[j] = br.ReadInt64();
            w.AddLoadedIntervention(new Intervention(tick, kind, vals));
        }
        if (version >= 4) ReadPeople(br, w, version);
        return w;
    }
}
