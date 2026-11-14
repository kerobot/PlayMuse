namespace MuseWiser.Services.Audio;

/// <summary>
/// SPECTRUM ANALYZER 表示欄の16バンド分の周波数定義（下限・上限 Hz）。
/// </summary>
public sealed record SpectrumBandRange(double LowFrequencyHz, double HighFrequencyHz);

/// <summary>
/// 横軸に表示する16バンドの固定定義。
/// 各値は各バンドの代表（中心）周波数とみなし、隣接バンドの幾何平均を境界として算出する。
/// 両端（最低バンドの下限・最高バンドの上限）は、隣接する内側境界との比率を用いて外挿する。
/// </summary>
public static class SpectrumBandDefinitions
{
    /// <summary>
    /// 各バンドの代表（中心）周波数。SPAN 系アナライザーの一般的な目盛りに合わせた固定値。
    /// </summary>
    private static readonly double[] CenterFrequencies =
    [
        20, 30, 40, 70, 120, 190, 310, 490, 780, 1200, 1900, 3100, 5000, 7900, 12000, 20000
    ];

    /// <summary>
    /// 16バンド分の境界定義。
    /// </summary>
    public static IReadOnlyList<SpectrumBandRange> Bands { get; } = BuildBands();

    /// <summary>
    /// <see cref="CenterFrequencies"/> の各中心周波数から、隣接バンドとの幾何平均を境界として
    /// 16 バンド分の周波数範囲（<see cref="SpectrumBandRange"/>）を構築する。
    /// </summary>
    /// <returns>各バンドの下限・上限周波数（Hz）の一覧。</returns>
    private static IReadOnlyList<SpectrumBandRange> BuildBands()
    {
        var count = CenterFrequencies.Length;
        var innerEdges = new double[count - 1];
        for (var i = 0; i < innerEdges.Length; i++)
        {
            innerEdges[i] = Math.Sqrt(CenterFrequencies[i] * CenterFrequencies[i + 1]);
        }

        var lowEdge0 = (CenterFrequencies[0] * CenterFrequencies[0]) / innerEdges[0];
        var highEdgeLast = (CenterFrequencies[^1] * CenterFrequencies[^1]) / innerEdges[^1];

        var bands = new SpectrumBandRange[count];
        for (var i = 0; i < count; i++)
        {
            var low = i == 0 ? lowEdge0 : innerEdges[i - 1];
            var high = i == count - 1 ? highEdgeLast : innerEdges[i];
            bands[i] = new SpectrumBandRange(low, high);
        }

        return bands;
    }
}
