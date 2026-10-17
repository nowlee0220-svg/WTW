using Wtw.Sim.Core;
using Wtw.Sim.People;

/// <summary>
/// (E3b, 개발용) 소리 무게가 조금씩 바뀌면 알아듣기가 얼마나 떨어지나: 같은 말 두 벌을 따로 흘려 보낸다(무리마다 해마다 k 번, 소리 하나 ±1).
///   dotnet run -c Release --project tools/bench -- people drift [--langs 200] [--k 1,2,3,4]
/// </summary>
public static class DriftBench
{
    public static void Run(int langs, string ks)
    {
        var d = LanguageData.Default;
        int[] years = { 50, 100, 150, 200, 300, 400, 500, 600 };
        foreach (var kstr in ks.Split(','))
        {
            double k = double.Parse(kstr, System.Globalization.CultureInfo.InvariantCulture);
            var sums = new double[years.Length];
            for (int li = 0; li < langs; li++)
            {
                var a = Language.Create(li + 1, (ulong)(li + 1) * 7919, 0, d);
                var b = Language.Create(li + 1, (ulong)(li + 1) * 7919, 0, d);
                var rng = new Pcg32((ulong)li, 3);
                int yi = 0;
                double acc = 0;
                for (int y = 1; y <= years[^1]; y++)
                {
                    acc += k;
                    while (acc >= 1)
                    {
                        acc -= 1;
                        foreach (var l in new[] { a, b })
                        {
                            int i = rng.NextInt(l.Sounds.Length);
                            int step = rng.NextInt(2) == 0 ? -1 : 1;
                            l.Sounds[i] = Math.Max(0, l.Sounds[i] + step);
                        }
                    }
                    if (y == years[yi])
                    {
                        int s = 0, n = 0;
                        foreach (var c in d.Concepts) if (c.Base) { s += Words.Similarity(Words.Word(a, null, c, d), Words.Word(b, null, c, d)); n++; }
                        sums[yi] += s / (double)n;
                        yi++;
                    }
                }
            }
            Console.WriteLine($"k={k}/년: " + string.Join(" · ", years.Select((y, i) => $"{y}년 {sums[i] / langs:0}%")));
        }
    }
}

/// <summary>
/// (E3b, 개발용) 실제 규칙과 같은 모형: 떨어져 사는 두 무리. 무리마다 해마다 확률 k 로 소리 하나 ±1(무리 어긋남에 쌓임),
/// 어긋남 합이 T 이상이면 새 말로 갈라짐(어긋남을 소리 무게에 굳힘), 말마다 해마다 확률 r 로 낱말 하나를 새로(정해진 순서대로).
/// </summary>
public static class DriftModel
{
    public static void Run(int langs, string ks, string rs, int threshold)
    {
        var d = LanguageData.Default;
        int[] years = { 100, 200, 300, 400, 500, 600 };
        var concepts = d.Concepts.Where(c => c.Base).ToList();
        foreach (var kstr in ks.Split(','))
            foreach (var rstr in rs.Split(','))
            {
                double k = double.Parse(kstr, System.Globalization.CultureInfo.InvariantCulture), r = double.Parse(rstr, System.Globalization.CultureInfo.InvariantCulture);
                var sums = new double[years.Length];
                for (int li = 0; li < langs; li++)
                {
                    var rng = new Pcg32((ulong)li * 31 + 7, 5);
                    var parent = Language.Create(li + 1, (ulong)(li + 1) * 7919, 0, d);
                    // 두 무리: 처음엔 같은 말(같은 객체를 나눠 쓰는 것처럼 — 낱말 바뀜도 같이)
                    var langOf = new[] { parent, parent };
                    var off = new[] { new int[parent.Sounds.Length], new int[parent.Sounds.Length] };
                    int nextId = 1000;
                    int yi = 0;
                    for (int y = 1; y <= years[^1]; y++)
                    {
                        for (int g = 0; g < 2; g++)
                            if (rng.NextDouble() < k) { int i = rng.NextInt(parent.Sounds.Length); off[g][i] += rng.NextInt(2) == 0 ? -1 : 1; }
                        bool shared = langOf[0] == langOf[1];
                        for (int g = 0; g < 2; g++)
                        {
                            int l1 = off[g].Sum(Math.Abs);
                            var L = langOf[g];
                            if (!shared || l1 >= threshold)
                            {
                                if (shared) { var c = Clone(L, nextId++); langOf[g] = c; L = c; shared = false; }
                                for (int i = 0; i < L.Sounds.Length; i++) { L.Sounds[i] = Math.Max(0, L.Sounds[i] + off[g][i]); off[g][i] = 0; }
                            }
                        }
                        foreach (var L in langOf.Distinct())
                            if (rng.NextDouble() < r) Replace(L, concepts);
                        if (y == years[yi])
                        {
                            int s = 0;
                            foreach (var c in concepts) s += Words.Similarity(Words.Word(langOf[0], Acc(off[0]), c, d), Words.Word(langOf[1], Acc(off[1]), c, d));
                            sums[yi] += s / (double)concepts.Count;
                            yi++;
                        }
                    }
                }
                Console.WriteLine($"k={k} r={r} T={threshold}: " + string.Join(" · ", years.Select((y, i) => $"{y}년 {sums[i] / langs:0}%")));
            }
    }

    static sbyte[] Acc(int[] o) => o.Select(v => (sbyte)Math.Clamp(v, -6, 6)).ToArray();

    static Language Clone(Language L, int id)
    {
        var c = new Language { Id = id, Seed = L.Seed, Parent = L.Id, Length = L.Length, Sounds = (int[])L.Sounds.Clone(), Changes = L.Changes };
        foreach (var k in L.Known) c.Known.Add(k);
        foreach (var (k, v) in L.Overrides) c.Overrides[k] = v;
        return c;
    }

    static void Replace(Language L, List<ConceptDef> concepts) => L.ReplaceNextWord(concepts);
}
