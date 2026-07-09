using PlayMuse.Core.Models;

namespace PlayMuse.Core.Services;

/// <summary>
/// 音楽ファイルのタグ情報（ID3/Vorbis Comment等）を読み取り、Trackへ反映するサービス。
/// タグが存在しない、または読み取りに失敗した場合はファイル名ベースの表示を維持する。
/// </summary>
public interface IMetadataService
{
    Task ApplyMetadataAsync(Track track, CancellationToken cancellationToken = default);
}
