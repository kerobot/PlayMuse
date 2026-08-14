using NAudio.Dsp;
using NAudio.Wave;
using PlayMuse.Core.Models;

namespace PlayMuse.Core.Services;

/// <summary>
/// 再生波形をリングバッファへキャプチャし、サンプルレートに応じた可変長FFTで
/// 16バンドのスペクトラムレベル（0〜10、減衰付きピークホールド込み）を算出するサービス。
/// </summary>
public sealed class SpectrumAnalyzerService : ISpectrumAnalyzerService
{
    private const int BandCount = 16;
    private const int MaxFftLength = 16384;
    private const double MinDecibel = -60.0;
    private const double MaxDecibel = 0.0;
    private const int LevelSteps = 10;

    /// <summary>ピークホールドが1フレーム（約33ms想定）あたりに減衰する段数。</summary>
    private const double PeakDecayPerUpdate = 0.3;

    /// <summary>レベル上昇時の追従係数（0〜1）。大きいほど素早く目標値に近づく。</summary>
    private const double LevelAttackFactor = 0.6;

    /// <summary>レベル下降時の追従係数（0〜1）。小さいほどゆっくり減衰し、なめらかに見える。</summary>
    private const double LevelReleaseFactor = 0.25;

    /// <summary>再生停止とみなすまでの、最終サンプル受信からの猶予時間。オーディオコールバックと
    /// UI側のポーリング間隔のずれによる誤検出（無音判定のちらつき）を防ぐためのバッファ。</summary>
    private static readonly TimeSpan SilenceTimeout = TimeSpan.FromMilliseconds(150);

    /// <summary>最終サンプル受信時刻が未設定であることを示す番兵値。</summary>
    private const long NoSampleReceivedTicks = -1;

    /// <summary>デフォルトのサンプルレート（Hz）。フォーマット未確定時の初期値。</summary>
    private const int DefaultSampleRate = 44100;

    /// <summary>FFT長を切り替えるサンプルレートの閾値（Hz）と、対応するFFT長。</summary>
    private const int SampleRateThreshold44100 = 44100;
    private const int SampleRateThreshold88200 = 88200;
    private const int SampleRateThreshold176400 = 176400;
    private const int FftLength2048 = 2048;
    private const int FftLength4096 = 4096;
    private const int FftLength8192 = 8192;
    private const int FftLength16384 = 16384;

    /// <summary>各ビット深度のPCMサンプルを-1.0〜1.0へ正規化するためのフルスケール値。</summary>
    private const double FullScale8Bit = 128.0;
    private const double FullScale16Bit = 32768.0;
    private const double FullScale24Bit = 8388608.0;
    private const double FullScale32Bit = 2147483648.0;

    /// <summary>8bit PCMの中心値（無音相当）のオフセット。</summary>
    private const int Offset8Bit = 128;

    /// <summary>24bit符号拡張時に立てる符号ビットと、上位バイトを1で埋めるマスク。</summary>
    private const int SignBit24Bit = 0x800000;
    private const int SignExtensionMask24Bit = unchecked((int)0xFF000000);

    /// <summary>Hamming窓の係数。</summary>
    private const double HammingCoefficientA = 0.54;
    private const double HammingCoefficientB = 0.46;

    /// <summary>デシベル変換時、振幅がゼロ以下の場合に用いる係数（20 * log10）。</summary>
    private const double DecibelScale = 20.0;

    private readonly float[] captureBuffer = new float[MaxFftLength];
    private readonly Lock sync = new();

    private int writeIndex;
    private int sampleCount;
    private int sampleRate = DefaultSampleRate;
    private long lastSampleTicks = NoSampleReceivedTicks;

    private readonly double[] peakLevels = new double[BandCount];
    private readonly double[] smoothedLevels = new double[BandCount];

    /// <summary>
    /// 再生波形のバイト列を受け取り、モノラル化したうえでリングバッファ（<see cref="captureBuffer"/>）に
    /// 蓄積する。オーディオ出力スレッド（<see cref="SpectrumTapProvider"/>経由）から高頻度で呼ばれる想定。
    /// </summary>
    public void PushSamples(ReadOnlySpan<byte> buffer, WaveFormat format)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        var channels = Math.Max(1, format.Channels);
        // BlockAlignはNAudioが報告する1フレーム（全チャンネル分）のバイト数。
        // 一部フォーマットでBlockAlignが取得できない場合はBitsPerSampleから1サンプルのバイト数を逆算する。
        var bytesPerSample = format.BlockAlign > 0 && channels > 0
            ? format.BlockAlign / channels
            : format.BitsPerSample / 8;
        if (bytesPerSample <= 0)
        {
            return;
        }

        var frameSize = bytesPerSample * channels;
        if (frameSize <= 0)
        {
            return;
        }

        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat;
        var containerBits = bytesPerSample * 8;
        var frameCount = buffer.Length / frameSize;

        lock (sync)
        {
            sampleRate = format.SampleRate;

            for (var frame = 0; frame < frameCount; frame++)
            {
                var frameOffset = frame * frameSize;
                double sum = 0;

                // 全チャンネルの値を加算し、後で平均をとることでモノラルダウンミックスする。
                // FFT解析は1系統の波形があれば十分なため、チャンネルごとの解析は行わない。
                for (var ch = 0; ch < channels; ch++)
                {
                    var sampleOffset = frameOffset + ch * bytesPerSample;
                    sum += ReadSample(buffer.Slice(sampleOffset, bytesPerSample), isFloat, containerBits);
                }

                // リングバッファに書き込み、書き込み位置を1つ進める（末尾に達したら先頭へ戻る）。
                captureBuffer[writeIndex] = (float)(sum / channels);
                writeIndex = (writeIndex + 1) % MaxFftLength;
                if (sampleCount < MaxFftLength)
                {
                    sampleCount++;
                }
            }

            // 「音声データが実際に流れてきた時刻」を記録する。GetBandLevels側で
            // この時刻からの経過時間を見て、再生が止まっているかどうかを判定する。
            lastSampleTicks = Environment.TickCount64;
        }
    }

    /// <summary>
    /// 直近のキャプチャ済み波形からFFTを実行し、16バンド分のスペクトルレベル（0〜10、ピークホールド込み）を
    /// 取得する。UI側のタイマー（約33ms間隔）から定期的に呼び出される想定。
    /// </summary>
    public IReadOnlyList<SpectrumBandLevel> GetBandLevels()
    {
        float[] windowSamples;
        int fftLength;
        int currentSampleRate;
        bool isSilentTimeout;

        lock (sync)
        {
            currentSampleRate = sampleRate;
            fftLength = GetFftLength(currentSampleRate);

            // 最終サンプル受信から一定時間（SilenceTimeout）が経過している場合は、
            // 一時停止・停止・デバイス切断などにより新しい再生データが供給されていないとみなす。
            // オーディオコールバックとUI側ポーリングの間隔のずれによる瞬間的な誤検出を避けるため、
            // 単純な「前回呼び出し以降にデータが来たか」ではなく、経過時間ベースで判定する。
            isSilentTimeout = lastSampleTicks == NoSampleReceivedTicks
                || Environment.TickCount64 - lastSampleTicks > SilenceTimeout.TotalMilliseconds;

            if (isSilentTimeout)
            {
                // 無音として扱う場合も、キャプチャバッファの古い波形を再利用してFFTにかけるのではなく、
                // 「全バンドが最小デシベル」という入力をBuildLevelsの平滑化処理に流し込む。
                // これにより、表示値は瞬時にゼロになるのではなく、既存のリリース（減衰）係数に従って
                // なめらかにゼロへ近づいていく。
                var silence = new double[BandCount];
                Array.Fill(silence, MinDecibel);
                return BuildLevels(silence);
            }

            if (sampleCount < fftLength)
            {
                // 再生開始直後などでリングバッファにFFTに必要な分のサンプルがまだ蓄積されていない場合。
                var silence = new double[BandCount];
                Array.Fill(silence, MinDecibel);
                return BuildLevels(silence);
            }

            // リングバッファの書き込み位置（writeIndex）を終端として、直近fftLength件分の
            // サンプルを時系列順に並べ直す。MaxFftLength * 2を足してから剰余を取ることで、
            // writeIndex - fftLengthが負になる場合でも正しく巻き戻したインデックスを得られる。
            windowSamples = new float[fftLength];
            var start = (writeIndex - fftLength + MaxFftLength * 2) % MaxFftLength;
            for (var i = 0; i < fftLength; i++)
            {
                windowSamples[i] = captureBuffer[(start + i) % MaxFftLength];
            }
        }

        // FFTで周波数ごとの振幅（マグニチュード）を求め、16バンドの帯域ごとにデシベル値へ変換し、
        // 最後に平滑化・ピークホールドを適用して表示用レベルを算出する。
        var magnitudes = ComputeMagnitudes(windowSamples, fftLength);
        var bandDecibels = MapToBandDecibels(magnitudes, fftLength, currentSampleRate);
        return BuildLevels(bandDecibels);
    }

    /// <summary>
    /// キャプチャバッファと平滑化・ピークホールドの状態をすべて初期化する。
    /// 再生停止時やフォーマット変更時など、古い波形やレベルを次回の解析に持ち越したくない場合に呼ぶ。
    /// </summary>
    public void Reset()
    {
        lock (sync)
        {
            Array.Clear(captureBuffer);
            writeIndex = 0;
            sampleCount = 0;
            lastSampleTicks = NoSampleReceivedTicks;
        }

        Array.Clear(peakLevels);
        Array.Clear(smoothedLevels);
    }

    /// <summary>
    /// サンプルレートに応じてFFT長を決定する。基準（44.1kHz付近）で2048、
    /// サンプルレートが高くなるほど周波数分解能を維持するためFFT長を大きくする。
    /// </summary>
    private static int GetFftLength(int sampleRate) => sampleRate switch
    {
        <= SampleRateThreshold44100 => FftLength2048,
        <= SampleRateThreshold88200 => FftLength4096,
        <= SampleRateThreshold176400 => FftLength8192,
        _ => FftLength16384,
    };

    /// <summary>
    /// 1サンプル分のバイト列を、フォーマット（float/PCMおよびビット深度）に応じて
    /// -1.0〜1.0の範囲に正規化した実数値へ変換する。
    /// </summary>
    private static double ReadSample(ReadOnlySpan<byte> sampleBytes, bool isFloat, int bitsPerSample)
    {
        if (isFloat && bitsPerSample == 32)
        {
            return BitConverter.ToSingle(sampleBytes);
        }

        // 整数PCMは各ビット深度のフルスケール値で割ることで-1.0〜1.0に正規化する。
        return bitsPerSample switch
        {
            16 => BitConverter.ToInt16(sampleBytes) / FullScale16Bit,
            24 => Read24BitSample(sampleBytes) / FullScale24Bit,
            32 => BitConverter.ToInt32(sampleBytes) / FullScale32Bit,
            8 => (sampleBytes[0] - Offset8Bit) / FullScale8Bit,
            _ => 0.0,
        };
    }

    /// <summary>
    /// .NETに標準の24bit整数型が存在しないため、3バイトのリトルエンディアン値を組み立てたうえで
    /// 符号ビット（最上位ビット）が立っている場合は上位バイトを1で埋め、負の値として正しく符号拡張する。
    /// </summary>
    private static int Read24BitSample(ReadOnlySpan<byte> sampleBytes)
    {
        var value = sampleBytes[0] | (sampleBytes[1] << 8) | (sampleBytes[2] << 16);
        if ((value & SignBit24Bit) != 0)
        {
            value |= SignExtensionMask24Bit;
        }

        return value;
    }

    /// <summary>
    /// 時系列の波形サンプルにHamming窓を適用したうえでFFT（高速フーリエ変換）を実行し、
    /// 各周波数ビンの振幅（マグニチュード）を求める。
    /// </summary>
    private static double[] ComputeMagnitudes(float[] samples, int fftLength)
    {
        var complex = new Complex[fftLength];
        var windowSum = 0.0;
        for (var i = 0; i < fftLength; i++)
        {
            // Hamming窓（0.54 - 0.46*cos）を適用する。窓関数を掛けずにFFTすると、
            // 波形の切り出し端で不連続が生じ、本来存在しない周波数成分（スペクトル漏れ）が
            // 発生してしまうため、窓の中心を1、両端をなだらかに0へ近づけて抑制する。
            var window = HammingCoefficientA - HammingCoefficientB * Math.Cos(2.0 * Math.PI * i / (fftLength - 1));
            windowSum += window;
            complex[i].X = (float)(samples[i] * window);
            complex[i].Y = 0f;
        }

        // FFTのバタフライ演算の段数（fftLength = 2^m）を求めて変換を実行する。
        var m = (int)Math.Log2(fftLength);
        FastFourierTransform.FFT(true, m, complex);

        // NAudioのFFT(forward=true)は内部でデータを1/fftLengthにスケーリングするため、
        // 窓関数のゲイン低下（windowSum分だけ振幅が小さくなる）とあわせて
        // 2*fftLength/windowSumで正規化し、振幅基準(0dBFS = フルスケール正弦波)に揃える。
        // 係数の2倍は、実数波形のFFT結果が正負の周波数に対称に分散する分を、
        // 片側（正の周波数側）のみを使う際に補うためのもの。
        var normalization = windowSum > 0 ? 2.0 * fftLength / windowSum : 1.0;

        // 実数波形のFFT結果は正負の周波数に対して対称になるため、半分（binCount = fftLength/2）だけを使う。
        var binCount = fftLength / 2;
        var magnitudes = new double[binCount];
        for (var i = 0; i < binCount; i++)
        {
            // 複素数の絶対値（√(実部^2 + 虚部^2)）が、その周波数ビンの振幅の大きさを表す。
            magnitudes[i] = Math.Sqrt(complex[i].X * complex[i].X + complex[i].Y * complex[i].Y) * normalization;
        }

        return magnitudes;
    }

    /// <summary>
    /// FFTで得られた周波数ビン単位のマグニチュードを、<see cref="SpectrumBandDefinitions"/>で
    /// 定義された16の周波数帯域（低音〜高音）ごとに集約し、デシベル値に変換する。
    /// </summary>
    private static double[] MapToBandDecibels(double[] magnitudes, int fftLength, int sampleRate)
    {
        var bands = SpectrumBandDefinitions.Bands;
        var decibels = new double[BandCount];
        // 1つの周波数ビンが占める帯域幅（Hz）。サンプルレートをFFT長で割ることで求まる。
        var binHz = (double)sampleRate / fftLength;

        for (var b = 0; b < BandCount; b++)
        {
            var band = bands[b];
            // 帯域の下限・上限周波数を、対応する周波数ビンの範囲（インデックス）に変換する。
            var lowBin = Math.Max(0, (int)Math.Floor(band.LowFrequencyHz / binHz));
            var highBin = Math.Min(magnitudes.Length - 1, (int)Math.Ceiling(band.HighFrequencyHz / binHz));

            // 帯域内で最も大きいマグニチュードをその帯域の代表値として採用する（平均ではなく最大値）。
            // ピーク成分を捉えやすくし、視覚的な反応をはっきりさせるため。
            var maxMagnitude = 0.0;
            for (var bin = lowBin; bin <= highBin; bin++)
            {
                if (magnitudes[bin] > maxMagnitude)
                {
                    maxMagnitude = magnitudes[bin];
                }
            }

            // 振幅をデシベル（20*log10）に変換する。振幅が0以下（＝無音）の場合はlog10が定義できないため、
            // 下限のMinDecibelを代わりに使う。
            decibels[b] = maxMagnitude <= 0 ? MinDecibel : DecibelScale * Math.Log10(maxMagnitude);
        }

        return decibels;
    }

    /// <summary>
    /// 帯域ごとのデシベル値を0〜<see cref="LevelSteps"/>の表示用レベルへ変換し、
    /// アタック/リリース方式の平滑化とピークホールドを適用して、UI表示に適した
    /// なめらかな値を生成する。
    /// </summary>
    private SpectrumBandLevel[] BuildLevels(double[] bandDecibels)
    {
        var result = new SpectrumBandLevel[BandCount];

        for (var b = 0; b < BandCount; b++)
        {
            // デシベル値をMinDecibel〜MaxDecibelの範囲にクランプしたうえで0〜1に正規化し、
            // 表示段数（LevelSteps）に引き伸ばして目標レベル（target）とする。
            var clamped = Math.Clamp(bandDecibels[b], MinDecibel, MaxDecibel);
            var normalized = (clamped - MinDecibel) / (MaxDecibel - MinDecibel);
            var target = normalized * LevelSteps;

            // 現在の表示レベルを目標値に少しずつ近づける（線形補間による一次遅れフィルタ）。
            // 音量が大きくなる方向（アタック）は素早く反応させ、小さくなる方向（リリース）は
            // ゆっくり追従させることで、フレームごとの瞬間的な変動を平滑化し、
            // バーの上下動をなめらかに見せる（アナログのVUメーター等と同様の考え方）。
            var factor = target > smoothedLevels[b] ? LevelAttackFactor : LevelReleaseFactor;
            smoothedLevels[b] += (target - smoothedLevels[b]) * factor;

            var level = smoothedLevels[b];

            // ピークホールド処理：現在レベルがこれまでのピークを超えたら即座にピークを更新し、
            // 超えていない場合は一定量（PeakDecayPerUpdate）ずつゆっくり減衰させる。
            // これにより、瞬間的な最大値を少し遅れて追従する「ピークインジケーター」を表現する。
            if (level > peakLevels[b])
            {
                peakLevels[b] = level;
            }
            else
            {
                peakLevels[b] = Math.Max(level, peakLevels[b] - PeakDecayPerUpdate);
            }

            result[b] = new SpectrumBandLevel(
                Math.Clamp(level, 0, LevelSteps),
                Math.Clamp(peakLevels[b], 0, LevelSteps));
        }

        return result;
    }
}
