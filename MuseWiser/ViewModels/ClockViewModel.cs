using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MuseWiser.ViewModels;

/// <summary>
/// 現在日時（時分秒・年月日＋曜日）をリアルタイムに表示するための ViewModel。
/// </summary>
public sealed partial class ClockViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// 1秒ごとに現在時刻を更新するためのタイマー。
    /// </summary>
    private readonly DispatcherTimer _timer;

    /// <summary>
    /// 表示用の時刻文字列（HH:mm:ss）。
    /// </summary>
    [ObservableProperty]
    private string _timeText = string.Empty;

    /// <summary>
    /// 表示用の日付文字列（yyyy-MM-dd(曜日)）。
    /// </summary>
    [ObservableProperty]
    private string _dateText = string.Empty;

    /// <summary>
    /// <see cref="ClockViewModel"/> の新しいインスタンスを初期化し、時刻更新用タイマーを開始する。
    /// </summary>
    public ClockViewModel()
    {
        UpdateNow();

        _timer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _timer.Tick += (_, _) => UpdateNow();
        _timer.Start();
    }

    /// <summary>
    /// 現在日時を取得し、<see cref="TimeText"/> および <see cref="DateText"/> を更新する。
    /// </summary>
    private void UpdateNow()
    {
        var now = DateTime.Now;
        TimeText = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        DateText = now.ToString("yyyy-MM-dd(ddd)", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// タイマーを停止する。
    /// </summary>
    public void Dispose()
    {
        _timer.Stop();
    }
}
