namespace Wtw.Sim.People;

/// <summary>(E5c) 밭 한 칸의 단계. 값은 저장 파일에 남으므로 뒤에만 더할 것.</summary>
public enum PlotStage : byte
{
    /// <summary>일군 빈 밭(심을 것을 기다림 — 오래 두면 잡풀이 덮는다).</summary>
    Empty = 0,
    /// <summary>씨를 묻음(아직 싹이 안 남).</summary>
    Sown,
    Sprout,
    Growing,
    Ripe,
}

/// <summary>
/// (E5c) 심은 자리 하나(타일 하나): 사람이 일군 밭 · 그냥 묻은 자리 · 흘린 씨에서 저절로 난 싹(Wild).
/// 조용한 밭은 날마다 훑지 않는다 — 다음 단계로 바뀌는 시각(Due)을 지방의 예약 목록(ProvinceLife.PlotDue)에 두고 그때만 본다.
/// 잡풀(Weeds)은 마지막으로 김을 맨 때(LastTend)부터 흐른 날로 그때그때 셈한다(저장하는 것은 시각뿐).
/// </summary>
public sealed class Plot
{
    public int Tile;
    public PlotStage Stage;
    /// <summary>일군 땅인가(아니면 그냥 묻은 자리 · 저절로 난 싹 — 잡풀이 많고 덜 거둔다).</summary>
    public bool Tilled;
    /// <summary>흘린 씨에서 저절로 났다(아무도 심지 않음).</summary>
    public bool Wild;
    /// <summary>자라는 것(씨 아이템 번호, 빈 밭이면 −1).</summary>
    public int Crop = -1;
    /// <summary>이 단계가 시작된 때 · 다음 단계로 바뀌는 때.</summary>
    public long Stamp, Due;
    /// <summary>마지막으로 김을 맨(일군 · 심은) 때.</summary>
    public long LastTend;
    /// <summary>짐승이 뜯어 먹은 정도(0~100, 100 이면 다 먹힘).</summary>
    public int Damage;
    /// <summary>심은 사람(−1 = 아무도).</summary>
    public int Planter = -1;
}
