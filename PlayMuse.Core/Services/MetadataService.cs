using PlayMuse.Core.Models;

namespace PlayMuse.Core.Services;

/// <summary>
/// TagLibSharpを用いてID3(mp3)/Vorbis Comment(flac)等のタグを読み取り、Trackへ反映するサービス。
/// タグの欠落や読み取り失敗時は、コンストラクタで設定済みのファイル名ベースの表示を維持する。
/// </summary>
public sealed class MetadataService(IDispatcherService dispatcherService) : IMetadataService
{
    public Task ApplyMetadataAsync(Track track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? title = null;
            string? artist = null;
            string? album = null;
            TimeSpan? duration = null;
            var sampleRate = 0;
            var bitsPerSample = 0;
            byte[]? albumArtData = null;

            try
            {
                using var file = TagLib.File.Create(track.FilePath);
                var tag = file.Tag;

                title = !string.IsNullOrWhiteSpace(tag.Title) ? tag.Title : null;
                artist = !string.IsNullOrWhiteSpace(tag.FirstPerformer) ? tag.FirstPerformer : null;
                album = !string.IsNullOrWhiteSpace(tag.Album) ? tag.Album : null;

                if (file.Properties?.Duration is { } fileDuration && fileDuration > TimeSpan.Zero)
                {
                    duration = fileDuration;
                }

                if (file.Properties?.AudioSampleRate is { } fileSampleRate && fileSampleRate > 0)
                {
                    sampleRate = fileSampleRate;
                }

                if (file.Properties?.BitsPerSample is { } fileBitsPerSample && fileBitsPerSample > 0)
                {
                    bitsPerSample = fileBitsPerSample;
                }

                if (tag.Pictures is { Length: > 0 } pictures)
                {
                    albumArtData = pictures[0].Data.Data;
                }
            }
            catch (Exception)
            {
                // タグ読み取り失敗時は、Trackコンストラクタで設定済みのファイル名ベース表示を維持する。
            }

            // Trackのプロパティ変更通知はUIスレッドで発生させる必要があるため、Dispatcherへマーシャリングする。
            dispatcherService.Invoke(() =>
            {
                if (title is not null)
                {
                    track.Title = title;
                }

                track.Artist = artist;
                track.Album = album;

                if (duration is not null)
                {
                    track.Duration = duration.Value;
                }

                track.SampleRate = sampleRate;
                track.BitsPerSample = bitsPerSample;
                track.AlbumArtData = albumArtData;
            });
        }, cancellationToken);
    }
}
