using PlayMuse.Core.Models;

namespace PlayMuse.Tests.Utilities;

/// <summary>
/// 複数のテストクラスで共通して利用する、テスト用データ生成のヘルパーメソッドを提供する。
/// </summary>
internal static class TestHelper
{
    /// <summary>
    /// 一時ディレクトリ配下の <paramref name="fileName"/> を指すファイルパスで <see cref="Track"/> を生成する。
    /// 実際のファイルは作成しない（存在チェックを伴わないテスト向け）。
    /// </summary>
    public static Track CreateTrack(string fileName) => new(Path.Combine(Path.GetTempPath(), fileName));
}
