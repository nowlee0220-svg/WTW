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
/// 버전 1(E0) · 2(E1) · 3(E2) 파일도 읽는다(행성 설정은 기본값, 사람 없음).
/// </summary>
public static class SaveGame
{
    public const int Version = 4;
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
        bw.Write(ps.Provinces.Count);
        foreach (var (cell, life) in ps.Provinces)
        {
            bw.Write(cell);
            bw.Write(life.Stocks.Count);
            foreach (var (tile, st) in life.Stocks) { bw.Write(tile); bw.Write(st.Stock); bw.Write(st.Stamp); }
            bw.Write(life.Revealed.Count);
            foreach (int t in life.Revealed) bw.Write(t);
        }
        bw.Write(ps.All.Count);
        foreach (var p in ps.All) WritePerson(bw, p);
    }

    private static void WritePerson(BinaryWriter bw, Person p)
    {
        bw.Write(p.Id); bw.Write(p.Name); bw.Write((byte)p.Sex); bw.Write(p.BirthTick); bw.Write(p.Cell);
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
    }

    private static void ReadPeople(BinaryReader br, GameWorld w)
    {
        var ps = w.People;
        ps.NextEventSeq = br.ReadInt64();
        int ne = br.ReadInt32();
        for (int i = 0; i < ne; i++)
            ps.AddLoadedEvent(new WorldEvent(br.ReadInt64(), br.ReadInt64(), (WorldEventKind)br.ReadByte(), br.ReadInt32(), br.ReadInt32(), br.ReadInt32(), br.ReadString()));
        int np = br.ReadInt32();
        for (int i = 0; i < np; i++)
        {
            var life = ps.Life(br.ReadInt32());
            int ns = br.ReadInt32();
            for (int k = 0; k < ns; k++) life.Stocks[br.ReadInt32()] = new TileStock { Stock = br.ReadInt32(), Stamp = br.ReadInt64() };
            int nr = br.ReadInt32();
            for (int k = 0; k < nr; k++) life.Revealed.Add(br.ReadInt32());
        }
        int count = br.ReadInt32();
        for (int i = 0; i < count; i++) ps.AddLoaded(ReadPerson(br));
    }

    private static Person ReadPerson(BinaryReader br)
    {
        var p = new Person { Id = br.ReadInt32(), Name = br.ReadString(), Sex = (Sex)br.ReadByte(), BirthTick = br.ReadInt64(), Cell = br.ReadInt32() };
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
        if (version >= 4) ReadPeople(br, w);
        return w;
    }
}
