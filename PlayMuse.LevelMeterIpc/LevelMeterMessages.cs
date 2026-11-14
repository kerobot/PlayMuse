namespace PlayMuse.LevelMeterIpc;

/// <summary>
/// <see cref="LevelMeterMessageReader"/> が受信・復元する全メッセージ型に共通のマーカーインターフェイス。
/// 受信側は <c>is</c> によるパターンマッチでメッセージ種別を判別できる。
/// </summary>
public interface ILevelMeterMessage;

/// <summary>
/// パイプ接続確立時、および出力デバイス／フォーマット変更時に送信される、
/// PlayMuse 側の現在の出力デバイス情報。
/// </summary>
/// <param name="DeviceName">選択中の再生デバイスのフレンドリ名。不明な場合は null。</param>
/// <param name="SampleRate">出力サンプルレート（Hz）。</param>
/// <param name="Channels">出力チャンネル数。</param>
/// <param name="IsExclusiveMode">
/// この情報が WASAPI 排他モード再生中に送信されたものであるかどうか。
/// レベルメーター連携は排他モード再生中のみ行われるため、現時点では常に true になる想定だが、
/// 将来の拡張や受信側での明示的な排他モード表示のためにフィールドとして持たせている。
/// </param>
public sealed record LevelMeterDeviceInfoMessage(string? DeviceName, int SampleRate, int Channels, bool IsExclusiveMode) : ILevelMeterMessage;

/// <summary>
/// 再生中の音量レベル・スペクトラムレベルを表す、0.0〜1.0 に正規化された1フレーム分のデータ。
/// ピークホールド等の減衰演出は受信側（MuseWiser）で行うため、本メッセージには生の値のみを含む。
/// </summary>
/// <param name="Left">左チャンネルのレベル（0.0〜1.0）。</param>
/// <param name="Right">右チャンネルのレベル（0.0〜1.0）。</param>
/// <param name="Spectrum">
/// 周波数バンドごとの振幅（0.0〜1.0）。要素数は <see cref="LevelMeterIpcDefaults.SpectrumBandCount"/> と一致する。
/// </param>
public sealed record LevelMeterFrameMessage(float Left, float Right, float[] Spectrum) : ILevelMeterMessage;
