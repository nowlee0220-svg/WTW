using System.Text;
using Wtw.Sim.Core;
using Wtw.Sim.Movers;
using Wtw.Sim.Planet;
using Wtw.Sim.Scheduling;
using Wtw.Sim.World;

namespace Wtw.Sim.Persistence;

/// <summary>
/// 저장 파일. 맨 앞 "WTWS" + 버전 번호. 순서가 정해진 것만 쓰므로 같은 상태 = 같은 바이트.
/// 행성은 칸 하나하나를 저장하지 않는다: 시드 + 설정(땅 칸 수 · 기후 경향)으로 다시 만든다.
/// 나중에 칸이 바뀌게 되면(운석 등) "바뀐 칸 목록"만 덧붙인다 — 자리는 버전 2 에 이미 있다(지금은 늘 0개).
/// 버전 1(E0) 파일도 읽는다(행성 설정은 기본값).
/// </summary>
public static class SaveGame
{
    public const int Version = 2;
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
        bw.Write(0); // 바뀐 행성 칸 수 (아직 없음)
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
            if (Array.IndexOf(PlanetSettings.LandChoices, config.LandCells) < 0 || !Enum.IsDefined(config.Climate))
                throw new InvalidDataException("저장 파일이 손상되었습니다(행성 설정).");
            if (br.ReadInt32() != 0) throw new InvalidDataException("저장 파일이 손상되었습니다(바뀐 행성 칸).");
        }
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
        return w;
    }
}
