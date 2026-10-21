using NAudio.Wave;

namespace PlayMuse.Core.Services;

/// <summary>
/// NAudio 3.3.0 の <c>WasapiPlayer.FillBuffer</c> は、ソースの <see cref="IWaveProvider.Read"/> が
/// 0（=曲の終端）を返すと無条件に render buffer を 0 フレームで解放する。しかし排他・イベント同期モード
/// （<c>WasapiPlayerBuilder.WithExclusiveMode().WithEventSync()</c>）では WASAPI が 0 フレームでの
/// 解放を許容せず、<c>AUDCLNT_E_BUFFER_SIZE_ERROR</c>（"A buffer size error occurred."）が発生して
/// 再生スレッドが異常終了してしまう（NAudio 3.3.0 側の既知の回帰動作）。
/// このラッパーはソースが終端に達しても 0 を返さず、残りを無音で埋めてフルバッファ長を返すことで
/// この不正な解放呼び出しを回避する。曲の自然な終了は <see cref="NaturalEndReached"/> で一度だけ通知するため、
/// 呼び出し側は適切なタイミングで明示的に再生を停止する必要がある（さもないと無音が鳴り続ける）。
/// </summary>
/// <remarks>
/// これは NAudio 3.3.0 側の不具合に対するアプリケーション層での暫定対応（ワークアラウンド）である。
/// NAudio 側で <c>WasapiPlayer.FillBuffer</c> の排他・イベント同期モード時の終端処理が修正された場合、
/// 本クラスおよび <see cref="AudioPlaybackService"/> 側の適用・購読箇所（<c>InitializeOutputCore</c> /
/// <c>OnExclusiveModeNaturalEndReached</c>）は不要になる可能性が高いため、NAudio のバージョンアップ時には
/// 本ワークアラウンドの要否を見直すこと。
/// </remarks>
internal sealed class ExclusiveModeEndOfStreamWorkaroundProvider(IWaveProvider source) : IWaveProvider
{
    private bool naturalEndRaised;

    public WaveFormat WaveFormat => source.WaveFormat;

    /// <summary>ソースが終端（Read が 0 を返す状態）に達した最初のタイミングで一度だけ発火する。</summary>
    public event EventHandler? NaturalEndReached;

    public int Read(Span<byte> buffer)
    {
        var bytesRead = source.Read(buffer);

        if (bytesRead > 0)
        {
            return bytesRead;
        }

        // ソース終端: AUDCLNT_E_BUFFER_SIZE_ERROR を避けるため 0 を返さず、無音でフルバッファを埋めて返す。
        buffer.Clear();

        if (!naturalEndRaised)
        {
            naturalEndRaised = true;
            NaturalEndReached?.Invoke(this, EventArgs.Empty);
        }

        return buffer.Length;
    }
}
