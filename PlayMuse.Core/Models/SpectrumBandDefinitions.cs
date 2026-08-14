namespace PlayMuse.Core.Models;

/// <summary>
/// SPECTRUM ANALYZER表示欄の16バンド分の周波数定義（ラベル・下限・上限Hz）。
/// ラベル値を各バンドの代表（中心）周波数とみなし、隣接ラベルの幾何平均を境界として算出する。
/// </summary>
public sealed record SpectrumBandDefinition(string Label, double LowFrequencyHz, double HighFrequencyHz);

/// <summary>
/// 横軸に表示する16バンドの固定定義。
/// </summary>
public static class SpectrumBandDefinitions
{
    /// <summary>
    /// 各バンドの代表（中心）周波数。SPECTRUM ANALYZER欄の下部に列ごとに表示するラベルと対応する。
    /// </summary>
    private static readonly (string Label, double Hz)[] CenterFrequencies =
    [
        ("20", 20),
        ("30", 30),
        ("40", 40),
        ("70", 70),
        ("120", 120),
        ("190", 190),
        ("310", 310),
        ("490", 490),
        ("780", 780),
        ("1.2k", 1200),
        ("1.9k", 1900),
        ("3.1k", 3100),
        ("5.0k", 5000),
        ("7.9k", 7900),
        ("12k", 12000),
        ("20k", 20000),
    ];

    /// <summary>
    /// 16バンド分の境界定義。隣接する代表周波数の幾何平均を境界とし、
    /// 両端（最低域の下限・最高域の上限）は同じ比率で外挿する。
    /// </summary>
    public static IReadOnlyList<SpectrumBandDefinition> Bands { get; } = BuildBands();

    private static IReadOnlyList<SpectrumBandDefinition> BuildBands()
    {
        var count = CenterFrequencies.Length;
        var innerEdges = new double[count - 1];
        for (var i = 0; i < innerEdges.Length; i++)
        {
            innerEdges[i] = Math.Sqrt(CenterFrequencies[i].Hz * CenterFrequencies[i + 1].Hz);
        }

        var lowEdge0 = (CenterFrequencies[0].Hz * CenterFrequencies[0].Hz) / innerEdges[0];
        var highEdgeLast = (CenterFrequencies[^1].Hz * CenterFrequencies[^1].Hz) / innerEdges[^1];

        var bands = new SpectrumBandDefinition[count];
        for (var i = 0; i < count; i++)
        {
            var low = i == 0 ? lowEdge0 : innerEdges[i - 1];
            var high = i == count - 1 ? highEdgeLast : innerEdges[i];
            bands[i] = new SpectrumBandDefinition(CenterFrequencies[i].Label, low, high);
        }

        return bands;
    }
}
