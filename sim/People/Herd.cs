namespace Wtw.Sim.People;

/// <summary>(E5b) 무리가 지금 하는 일. 값은 저장 파일에 남으므로 뒤에만 더할 것.</summary>
public enum HerdState : byte { Graze = 0, Rest, Flee, Stalk, Follow, Sleep }

/// <summary>
/// (E5b) 짐승 무리 하나: 같은 종 몇 마리가 함께 움직인다(한 점으로 계산하고, 화면은 그 둘레에 마릿수만큼 그린다).
/// 번호는 지방 안에서만(지방마다 다음 번호) — 지방 계산이 지방마다 따로라서. 예약은 지방 예약표(EventKinds.Herd).
/// 조용한 무리는 사람처럼 매 틱 계산하지 않는다: "어디로 가서 얼마나 풀을 뜯는다"를 정하고 다음 판단 시각을 예약한다.
/// </summary>
public sealed class Herd
{
    public int Id;
    public int Species;
    /// <summary>마릿수(다 자란 것).</summary>
    public int Count;
    /// <summary>사는 곳의 가운데 타일(멀리 가면 돌아온다).</summary>
    public int Home;
    public HerdState State;
    /// <summary>포식자가 노리는 것: TargetKind 1 = 사람 번호 · 2 = 무리 번호.</summary>
    public int Target = -1;
    public byte TargetKind;
    /// <summary>포식자: 마지막으로 먹은 때.</summary>
    public long LastMeal;
    /// <summary>길들이기의 첫걸음: 따르는 사람(−1 = 들짐승) · 친해진 정도(0~100) · 사람 곁에 남은 때.</summary>
    public int Owner = -1, Bond;
    public long Since;
    /// <summary>포식자: 이때까지는 사람을 노리지 않는다(사람에게 쫓겨난 뒤 · 불에 데인 뒤).</summary>
    public long Wary;
    public long PendingHandle;
    /// <summary>움직임(지금 · 바로 전 — 화면이 조금 지난 시각도 그릴 수 있게).</summary>
    public Motion Motion = new(), Prev = new();

    public bool Tame => Owner >= 0;

    public void NewMotion() { (Prev, Motion) = (Motion, Prev); }
    public void Position(long tick, out int x, out int y) => Motion.IntPositionAt(tick, out x, out y);
    public int Tile(long tick) { Position(tick, out int x, out int y); return (y / 1000) * Provinces.HexFrame.Size + x / 1000; }

    public Herd Clone()
    {
        var h = (Herd)MemberwiseClone();
        h.Motion = new Motion(); h.Motion.CopyFrom(Motion);
        h.Prev = new Motion(); h.Prev.CopyFrom(Prev);
        return h;
    }
}
