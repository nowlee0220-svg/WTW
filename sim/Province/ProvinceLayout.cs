namespace Wtw.Sim.Provinces;

/// <summary>
/// 여러 지방을 한 평면에 이어 붙이는 배치(기획서 5-4). 육각형 지방은 빈틈없이 맞물린다.
/// 가운데 지방은 제자리, 이웃 지방은 맞댄 변이 가운데를 보도록 60° 단위로 돌려 놓는다
/// (행성의 이웃 순서가 모두 바깥에서 볼 때 시계 방향이라, 뒤집지 않고 돌리기만 하면 된다).
/// 좌표 단위는 타일, 가운데 지방의 육각형 중심 = (0, 0). 화면처럼 y 는 아래쪽, 돌림은 시계 방향.
/// </summary>
public static class ProvinceLayout
{
    public readonly record struct Placed(int Cell, int CenterSlot, double OffsetX, double OffsetY, int Steps)
    {
        /// <summary>지방 지도의 타일 좌표(0~150) → 배치 좌표.</summary>
        public (double X, double Y) ToLayout(double tx, double ty)
        {
            double x = tx - HexFrame.Center, y = ty - HexFrame.Center;
            var (c, s) = CosSin(Steps);
            return (OffsetX + x * c - y * s, OffsetY + x * s + y * c);
        }

        /// <summary>배치 좌표 → 지방 지도의 타일 좌표.</summary>
        public (double X, double Y) ToLocal(double lx, double ly)
        {
            double x = lx - OffsetX, y = ly - OffsetY;
            var (c, s) = CosSin(Steps);
            return (HexFrame.Center + x * c + y * s, HexFrame.Center - x * s + y * c);
        }
    }

    /// <summary>cos, sin(60° × steps) — 표에서.</summary>
    public static (double Cos, double Sin) CosSin(int steps)
    {
        double h = Math.Sqrt(3) / 2;
        return (((steps % 6) + 6) % 6) switch { 0 => (1, 0), 1 => (0.5, h), 2 => (-0.5, h), 3 => (-1, 0), 4 => (-0.5, -h), _ => (0.5, -h) };
    }

    /// <summary>가운데 칸 center 의 자리(변) slot 너머 이웃의 배치(이웃이 없으면 null). Around 와 같은 셈.</summary>
    public static Placed? Neighbor(Planet.Planet p, int center, int slot)
    {
        var (_, _, slotCell, _) = ProvinceGenerator.Slots(p, center);
        int nb = slotCell[slot];
        if (nb < 0) return null;
        var (_, _, nbSlots, _) = ProvinceGenerator.Slots(p, nb);
        int m = Array.IndexOf(nbSlots, center);
        int steps = ((slot - m + 3) % 6 + 6) % 6;
        double d = 2 * HexFrame.A;
        return new Placed(nb, slot, HexFrame.NX[slot] * d, HexFrame.NY[slot] * d, steps);
    }

    /// <summary>가운데 칸과 (넣을 것을 고른) 이웃들의 배치. 이웃은 가운데 지방의 자리 순서대로.</summary>
    public static List<Placed> Around(Planet.Planet p, int center, Func<int, bool> include)
    {
        var list = new List<Placed> { new(center, -1, 0, 0, 0) };
        var (_, _, slotCell, _) = ProvinceGenerator.Slots(p, center);
        for (int s = 0; s < 6; s++)
        {
            int nb = slotCell[s];
            if (nb < 0 || !include(nb)) continue;
            var (_, _, nbSlots, _) = ProvinceGenerator.Slots(p, nb);
            int m = Array.IndexOf(nbSlots, center);
            int steps = ((s - m + 3) % 6 + 6) % 6;
            double d = 2 * HexFrame.A;
            list.Add(new Placed(nb, s, HexFrame.NX[s] * d, HexFrame.NY[s] * d, steps));
        }
        return list;
    }
}
