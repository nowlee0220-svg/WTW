using Wtw.Sim.Data;
using Wtw.Sim.Provinces;
using Wtw.Sim.Time;

namespace Wtw.Sim.People;

/// <summary>
/// (T1) 먹을 것 장부: 지방 하나가 하루에 "다시 내는" 먹을 것(배부름, 1000 = 1 — 한 끼 ≈ 33)을 갈래마다.
/// 데이터와 지도로만 셈한다(시뮬을 돌리지 않는다 · 결과에 영향 없음). 사람이 닿지 못하는 곳도 모두 센다(이론상 최대).
///   Fruit: 따 먹는 것(foods — 열매 · 버섯 · 이끼): 한 몫의 배부름 ÷ 다시 열리는 날
///   Wild: 주워 드는 먹을 것(carry 가운데 먹을 수 있는 것 — 도토리 · 낟알 · 콩 · 덩이뿌리): 날로 먹을 때의 배부름(먹을 수 있음 × 5) ÷ 다시 나는 날
///   Game: 짐승(포식자 빼고): 한 해에 늘 수 있는 가장 큰 수(로지스틱 r·K/4 — 사람이 절반으로 줄여 놓고 잡을 때) × 고기 × 날고기 배부름 ÷ 40일
///   Fish: 물고기도 같은 셈
///   FieldPerTile: 일군 밭 한 칸이 하루에 내는 것(자라는 땅 평균, 볶은 낟알로 먹을 때) · FieldTiles: 밭이 될 수 있는 타일 수
/// </summary>
public sealed record FoodLedger(double Fruit, double Wild, double Game, double Fish, double FieldPerTile, int FieldTiles)
{
    /// <summary>들(밭 빼고)이 하루에 내는 것.</summary>
    public double WildTotal => Fruit + Wild + Game + Fish;
}

public sealed partial class PeopleSim
{
    /// <summary>사람 하나가 하루에 먹는 양(배부름, 1000 = 1): 깨어 16시간 + 자는 8시간 어림.</summary>
    public const int DailyNeed = FoodAwake * 16 + FoodAsleep * 8;

    /// <summary>들고 다니는 먹을 것 하나를 날로 먹을 때의 배부름(FinishEatCarried 와 같은 셈).</summary>
    public int CarriedNutrition(int item) => Math.Max(0, Prop(item, Craft.P_edible)) * 5000;

    /// <summary>
    /// 지방의 걸을 수 있는 타일마다 놓인 오브젝트를 데이터(people.json 의 foods · carry)로 셈한다: (따 먹는 것, 주워 드는 먹을 것) 하루 몫(1000 = 1).
    /// 데이터를 바꾸면 바뀐 값으로 나온다(시험: 다시 나는 날을 두 배로 하면 절반).
    /// </summary>
    public static (double Fruit, double Wild) PlantFood(ProvinceLife life, PeopleData data, Func<int, int> carriedNutrition)
    {
        double fruit = 0, wild = 0;
        for (int i = 0; i < HexFrame.TileCount; i++)
        {
            if (!life.Walk[i]) continue;
            int o = life.Map.ObjectAt(i);
            if (o < 0 || o >= data.Harvest.Length || data.Harvest[o] is not { } h || h.RegrowTicks <= 0) continue;
            double perDay = (double)GameTime.TicksPerDay / h.RegrowTicks;
            if (h.Food) fruit += h.Nutrition * perDay;
            else wild += carriedNutrition(h.ItemIndex) * perDay;
        }
        return (fruit, wild);
    }

    public FoodLedger Ledger(ProvinceLife life)
    {
        if (life.AnimalCap.Length == 0) ComputeCaps(life);
        var (fruit, wild) = PlantFood(life, Data, CarriedNutrition);
        double game = 0;
        int meat = Craft.ItemIndex("raw_meat");
        for (int s = 0; s < Fauna.Animals.Count; s++)
        {
            var a = Fauna.Animals[s];
            if (a.Predator) continue;
            int k = life.AnimalCap[s];
            double perYear = a.Growth1000 / 1000.0 * k / 4.0;
            int m = 0; foreach (var (it, n) in a.Yields) if (it == meat) m += n;
            game += perYear * m * CarriedNutrition(meat) / GameTime.DaysPerYear;
        }
        double fish = Fauna.FishItem < 0 ? 0 : Fauna.FishGrowth1000 / 1000.0 * life.FishCap / 4.0 * CarriedNutrition(Fauna.FishItem) / GameTime.DaysPerYear;
        var g = GrowMap(life);
        int roast = Craft.ItemIndex("roasted_grain");
        int per = CarriedNutrition(roast >= 0 ? roast : I_grain);
        double field = 0; int tiles = 0;
        for (int i = 0; i < g.Length; i++)
        {
            int days = GrowDays(g[i]);
            if (days == 0) continue;
            tiles++;
            field += (double)SeedYield * g[i] / 100 * per / days;
        }
        return new FoodLedger(fruit / 1000, wild / 1000, game / 1000, fish / 1000, tiles == 0 ? 0 : field / tiles / 1000, tiles);
    }

    private byte[]? _foodKind;
    /// <summary>(T1 측정) 들고 먹은 것의 갈래(Stats.Eaten 의 번호).</summary>
    internal int FoodKind(int item)
    {
        if (_foodKind is null)
        {
            var k = new byte[Craft.Catalog.Items.Count];
            var seeds = new HashSet<string> { "grain", "beans", "tuber", "acorn", "roasted_grain", "cooked_beans", "roasted_tuber", "roasted_acorn", "flour", "flatbread" };
            var meat = new HashSet<string> { "raw_meat", "raw_fish", "fat", "cooked_meat", "cooked_fish", "dried_meat", "smoked_meat" };
            for (int i = 0; i < k.Length; i++)
            {
                string id = Craft.Catalog.Items[i].Id;
                k[i] = (byte)(seeds.Contains(id) ? 2 : meat.Contains(id) ? 3 : id == "stew" ? 4 : 1);
            }
            _foodKind = k;
        }
        return (uint)item < _foodKind.Length ? _foodKind[item] : 4;
    }
}
