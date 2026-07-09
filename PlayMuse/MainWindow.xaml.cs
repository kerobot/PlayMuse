using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using PlayMuse.Core.Models;
using PlayMuse.Core.ViewModels;

namespace PlayMuse;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly DispatcherTimer positionTimer;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = this.viewModel = viewModel;

        // 再生位置の表示更新はViewが所有するタイマーから行う。
        // PlayMuse.CoreはUIフレームワーク非依存に保つため、DispatcherTimerはここに置く。
        positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        positionTimer.Tick += (_, _) => viewModel.RefreshPosition();

        Loaded += (_, _) => positionTimer.Start();
        Closed += (_, _) =>
        {
            positionTimer.Stop();
            viewModel.Dispose();
        };
    }

    private void TrackItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Track track })
        {
            viewModel.PlayTrackCommand.Execute(track);
        }
    }

    private void PositionSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        => viewModel.IsSeeking = true;

    private void PositionSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        => viewModel.IsSeeking = false;
}
