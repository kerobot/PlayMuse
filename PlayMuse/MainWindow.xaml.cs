using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PlayMuse.Core.Models;
using PlayMuse.Core.ViewModels;

namespace PlayMuse;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const string TrackDragFormat = "PlayMuse.Track";
    private const double AutoScrollEdgeSize = 40;
    private const double AutoScrollStep = 16;
    private const double TrackInfoPanelExpandedHeight = 110;
    private const double SpectrumPanelExpandedHeight = 130;
    private static readonly TimeSpan PanelToggleDuration = TimeSpan.FromSeconds(0.2);

    private const int SpectrumColumnCount = 16;
    private const int SpectrumRowCount = 10;
    private const double SpectrumPartialCellMinOpacity = 0.35;
    private static readonly string[] SpectrumFrequencyLabels =
    [
        "20", "30", "40", "70", "120", "190", "310", "490",
        "780", "1.2k", "1.9k", "3.1k", "5.0k", "7.9k", "12k", "20k",
    ];

    private readonly MainViewModel viewModel;
    private readonly DispatcherTimer positionTimer;
    private readonly DispatcherTimer autoScrollTimer;
    private readonly DispatcherTimer spectrumTimer;
    private readonly System.Windows.Shapes.Rectangle[,] spectrumCells = new System.Windows.Shapes.Rectangle[SpectrumColumnCount, SpectrumRowCount];
    private readonly Brush?[,] spectrumCellFills = new Brush?[SpectrumColumnCount, SpectrumRowCount];
    private readonly double[,] spectrumCellOpacities = new double[SpectrumColumnCount, SpectrumRowCount];
    private readonly System.Windows.Shapes.Line[] spectrumVerticalGridLines = new System.Windows.Shapes.Line[SpectrumColumnCount + 1];
    private readonly System.Windows.Shapes.Line[] spectrumHorizontalGridLines = new System.Windows.Shapes.Line[SpectrumRowCount + 1];
    private Point? dragStartPoint;
    private Track? draggingTrack;
    private DropIndicatorAdorner? dropIndicatorAdorner;
    private ScrollViewer? autoScrollViewer;
    private double autoScrollDirection;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = this.viewModel = viewModel;

        // 再生位置の表示更新はViewが所有するタイマーから行う。
        // PlayMuse.CoreはUIフレームワーク非依存に保つため、DispatcherTimerはここに置く。
        positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        positionTimer.Tick += (_, _) => viewModel.RefreshPosition();

        autoScrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50),
        };
        autoScrollTimer.Tick += (_, _) => autoScrollViewer?.ScrollToVerticalOffset(autoScrollViewer.VerticalOffset + autoScrollDirection);

        spectrumTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        spectrumTimer.Tick += (_, _) => RenderSpectrum();

        BuildSpectrumGrid();

        Loaded += (_, _) => positionTimer.Start();
        Loaded += (_, _) =>
        {
            ToggleTrackInfoPanel(viewModel.IsTrackInfoVisible, animate: false);
            ToggleSpectrumPanel(viewModel.IsSpectrumVisible, animate: false);
        };
        Closed += (_, _) =>
        {
            positionTimer.Stop();
            autoScrollTimer.Stop();
            spectrumTimer.Stop();
            viewModel.Dispose();
        };

        viewModel.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.IsTrackInfoVisible):
                    ToggleTrackInfoPanel(viewModel.IsTrackInfoVisible, animate: true);
                    break;
                case nameof(MainViewModel.IsSpectrumVisible):
                    ToggleSpectrumPanel(viewModel.IsSpectrumVisible, animate: true);
                    break;
            }
        };
    }

    /// <summary>
    /// TRACK INFOMATIONパネルの開閉を高さアニメーションで反映する。展開先はSPECTRUM ANALYZERパネルと同じ固定高さとする。
    /// </summary>
    private void ToggleTrackInfoPanel(bool visible, bool animate)
    {
        AnimatePanelHeight(TrackInfoPanel, visible ? TrackInfoPanelExpandedHeight : 0, animate);
    }

    /// <summary>
    /// SPECTRUM ANALYZERパネル（プレースホルダー）の開閉を高さアニメーションで反映する。展開先は固定高さとする。
    /// </summary>
    private void ToggleSpectrumPanel(bool visible, bool animate)
    {
        AnimatePanelHeight(SpectrumPanel, visible ? SpectrumPanelExpandedHeight : 0, animate);

        if (visible)
        {
            spectrumTimer.Start();
        }
        else
        {
            spectrumTimer.Stop();
        }
    }

    private static void AnimatePanelHeight(Border panel, double targetHeight, bool animate)
    {
        if (!animate)
        {
            panel.BeginAnimation(FrameworkElement.HeightProperty, null);
            panel.Height = targetHeight;
            return;
        }

        var animation = new DoubleAnimation
        {
            From = double.IsNaN(panel.Height) ? panel.ActualHeight : panel.Height,
            To = targetHeight,
            Duration = PanelToggleDuration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
        };
        panel.BeginAnimation(FrameworkElement.HeightProperty, animation);
    }

    /// <summary>
    /// SPECTRUM ANALYZER欄に縦10マス×横16マスの罫線と、各マスに対応するLED風の矩形を配置する。
    /// ウィンドウサイズ変更時も比率を維持できるよう、実際の座標計算は <see cref="SpectrumCanvas_SizeChanged"/> で行う。
    /// </summary>
    private void BuildSpectrumGrid()
    {
        SpectrumLabelsGrid.ColumnDefinitions.Clear();
        SpectrumLabelsGrid.Children.Clear();

        for (var col = 0; col < SpectrumColumnCount; col++)
        {
            SpectrumLabelsGrid.ColumnDefinitions.Add(new ColumnDefinition());

            var label = new TextBlock
            {
                Text = SpectrumFrequencyLabels[col],
                FontSize = 9,
                Opacity = 0.6,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (Brush)FindResource("AccentGlowBrush"),
            };
            Grid.SetColumn(label, col);
            SpectrumLabelsGrid.Children.Add(label);
        }

        SpectrumCanvas.Children.Clear();

        for (var col = 0; col <= SpectrumColumnCount; col++)
        {
            var line = new System.Windows.Shapes.Line
            {
                Stroke = (Brush)FindResource("AccentDimBrush"),
                StrokeThickness = 0.5,
                Opacity = 0.4,
            };
            spectrumVerticalGridLines[col] = line;
            SpectrumCanvas.Children.Add(line);
        }

        for (var row = 0; row <= SpectrumRowCount; row++)
        {
            var line = new System.Windows.Shapes.Line
            {
                Stroke = (Brush)FindResource("AccentDimBrush"),
                StrokeThickness = 0.5,
                Opacity = 0.4,
            };
            spectrumHorizontalGridLines[row] = line;
            SpectrumCanvas.Children.Add(line);
        }

        for (var col = 0; col < SpectrumColumnCount; col++)
        {
            for (var row = 0; row < SpectrumRowCount; row++)
            {
                var cell = new System.Windows.Shapes.Rectangle
                {
                    Fill = Brushes.Transparent,
                };
                spectrumCells[col, row] = cell;
                spectrumCellFills[col, row] = cell.Fill;
                spectrumCellOpacities[col, row] = cell.Opacity;
                SpectrumCanvas.Children.Add(cell);
            }
        }

        LayoutSpectrumGrid();
    }

    /// <summary>
    /// Canvasのサイズ変更に応じて各マスの位置・サイズを再計算する。縦横のマス数は変更せず表示比率のみ追従する。
    /// </summary>
    private void SpectrumCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => LayoutSpectrumGrid();

    private void LayoutSpectrumGrid()
    {
        var width = SpectrumCanvas.ActualWidth;
        var height = SpectrumCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var cellWidth = width / SpectrumColumnCount;
        var cellHeight = height / SpectrumRowCount;
        const double cellMargin = 1.0;

        for (var col = 0; col <= SpectrumColumnCount; col++)
        {
            var line = spectrumVerticalGridLines[col];
            var x = col * cellWidth;
            line.X1 = x;
            line.Y1 = 0;
            line.X2 = x;
            line.Y2 = height;
        }

        for (var row = 0; row <= SpectrumRowCount; row++)
        {
            var line = spectrumHorizontalGridLines[row];
            var y = row * cellHeight;
            line.X1 = 0;
            line.Y1 = y;
            line.X2 = width;
            line.Y2 = y;
        }

        for (var col = 0; col < SpectrumColumnCount; col++)
        {
            for (var row = 0; row < SpectrumRowCount; row++)
            {
                var cell = spectrumCells[col, row];
                cell.Width = Math.Max(0, cellWidth - cellMargin * 2);
                cell.Height = Math.Max(0, cellHeight - cellMargin * 2);
                Canvas.SetLeft(cell, col * cellWidth + cellMargin);
                // 行0が最上段（最大レベル）になるよう上から配置する。
                Canvas.SetTop(cell, row * cellHeight + cellMargin);
            }
        }
    }

    /// <summary>
    /// 33ms間隔でViewModelから16バンド分のレベル・ピークホールド値（連続値）を取得し、LEDセルの
    /// 点灯状態を更新する。段の境界をまたぐセルは不透明度を連続値に応じて調整し、なめらかに見せる。
    /// </summary>
    private void RenderSpectrum()
    {
        var levels = viewModel.GetSpectrumLevels();
        var dimBrush = (Brush)FindResource("SpectrumBarBrush");
        var glowBrush = (Brush)FindResource("SpectrumPeakBrush");

        for (var col = 0; col < SpectrumColumnCount && col < levels.Count; col++)
        {
            var level = levels[col].Level;
            var peak = levels[col].PeakLevel;

            // ピークホールドは最も近い段に1マス分のグロー表示として点灯させる。
            var peakRow = SpectrumRowCount - (int)Math.Round(Math.Clamp(peak, 0, SpectrumRowCount));

            for (var row = 0; row < SpectrumRowCount; row++)
            {
                // row 0が最上段（最大レベル）、row 9が最下段（最小レベル）に対応する。
                var rowLevel = SpectrumRowCount - row;
                var cell = spectrumCells[col, row];

                Brush fill;
                double opacity;

                if (row == peakRow && peak > level)
                {
                    fill = glowBrush;
                    opacity = 1.0;
                }
                else if (rowLevel <= level)
                {
                    // 完全に点灯している段は不透明度を最大にする。
                    fill = dimBrush;
                    opacity = 1.0;
                }
                else if (rowLevel - 1 < level)
                {
                    // レベルが段の途中で終わる場合、端数分だけ不透明度を上げて滑らかに見せる。
                    // ただし下限を設け、暗すぎて消灯しているように見えないようにする。
                    fill = dimBrush;
                    opacity = Math.Clamp(level - (rowLevel - 1), SpectrumPartialCellMinOpacity, 1.0);
                }
                else
                {
                    fill = Brushes.Transparent;
                    opacity = 1.0;
                }

                // 値に変化がないセルはFill/Opacityの再設定を避け、無効な描画・レイアウトパスの誘発を抑える。
                if (!ReferenceEquals(spectrumCellFills[col, row], fill))
                {
                    cell.Fill = fill;
                    spectrumCellFills[col, row] = fill;
                }

                if (spectrumCellOpacities[col, row] != opacity)
                {
                    cell.Opacity = opacity;
                    spectrumCellOpacities[col, row] = opacity;
                }
            }
        }
    }

    private void TrackItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Track track })
        {
            viewModel.PlayTrackCommand.Execute(track);
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();

    private void PositionSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        => viewModel.IsSeeking = true;

    private void PositionSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        => viewModel.IsSeeking = false;

    private void TrackList_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(TrackDragFormat) && sender is ListBox listBox)
        {
            e.Effects = DragDropEffects.Move;
            var (_, lineY) = GetDropTarget(listBox, e);
            ShowDropIndicator(listBox, lineY);
            UpdateAutoScroll(listBox, e);
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            RemoveDropIndicator();
            StopAutoScroll();
        }
        else
        {
            e.Effects = DragDropEffects.None;
            RemoveDropIndicator();
            StopAutoScroll();
        }

        e.Handled = true;
    }

    private void TrackList_DragLeave(object sender, DragEventArgs e)
    {
        RemoveDropIndicator();
        StopAutoScroll();
    }

    private void TrackList_Drop(object sender, DragEventArgs e)
    {
        RemoveDropIndicator();
        StopAutoScroll();
        viewModel.DraggingTrack = null;

        if (e.Data.GetDataPresent(TrackDragFormat))
        {
            if (e.Data.GetData(TrackDragFormat) is Track draggedTrack && sender is ListBox listBox)
            {
                var (targetIndex, _) = GetDropTarget(listBox, e);
                viewModel.MoveTrack(draggedTrack, targetIndex);
            }

            e.Handled = true;
            return;
        }

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            viewModel.AddFiles(paths);
        }

        e.Handled = true;
    }

    private void DragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Track track })
        {
            dragStartPoint = e.GetPosition(null);
            draggingTrack = track;
        }
    }

    private void DragHandle_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || dragStartPoint is not Point startPoint || draggingTrack is null)
        {
            return;
        }

        var currentPosition = e.GetPosition(null);
        if (Math.Abs(currentPosition.X - startPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPosition.Y - startPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var trackToDrag = draggingTrack;
        dragStartPoint = null;
        draggingTrack = null;

        viewModel.DraggingTrack = trackToDrag;
        try
        {
            var data = new DataObject(TrackDragFormat, trackToDrag);
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
        }
        finally
        {
            viewModel.DraggingTrack = null;
            RemoveDropIndicator();
            StopAutoScroll();
        }
    }

    /// <summary>
    /// ドラッグ中のマウス位置がリスト上端・下端付近にある場合、自動スクロールを開始する。
    /// </summary>
    private void UpdateAutoScroll(ListBox listBox, DragEventArgs e)
    {
        var scrollViewer = FindScrollViewer(listBox);
        if (scrollViewer is null)
        {
            StopAutoScroll();
            return;
        }

        var position = e.GetPosition(scrollViewer);
        if (position.Y < AutoScrollEdgeSize)
        {
            autoScrollDirection = -AutoScrollStep;
        }
        else if (position.Y > scrollViewer.ActualHeight - AutoScrollEdgeSize)
        {
            autoScrollDirection = AutoScrollStep;
        }
        else
        {
            StopAutoScroll();
            return;
        }

        autoScrollViewer = scrollViewer;
        autoScrollTimer.Start();
    }

    private void StopAutoScroll()
    {
        autoScrollTimer.Stop();
        autoScrollViewer = null;
        autoScrollDirection = 0;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            var result = FindScrollViewer(child);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>
    /// ドロップ位置にあるListBoxItemを走査し、マウスY座標がアイテム中央より上か下かで挿入先インデックスと、
    /// インジケーターラインを描画すべきY座標（ListBox基準）を決定する。
    /// </summary>
    private static (int TargetIndex, double LineY) GetDropTarget(ListBox listBox, DragEventArgs e)
    {
        for (var i = 0; i < listBox.Items.Count; i++)
        {
            if (listBox.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem container)
            {
                continue;
            }

            var position = e.GetPosition(container);
            if (position.Y < 0 || position.Y > container.ActualHeight)
            {
                continue;
            }

            var isAfter = position.Y > container.ActualHeight / 2;
            var lineY = container.TranslatePoint(new Point(0, isAfter ? container.ActualHeight : 0), listBox).Y;
            return (isAfter ? i + 1 : i, lineY);
        }

        if (listBox.Items.Count > 0 &&
            listBox.ItemContainerGenerator.ContainerFromIndex(listBox.Items.Count - 1) is ListBoxItem lastContainer)
        {
            var lineY = lastContainer.TranslatePoint(new Point(0, lastContainer.ActualHeight), listBox).Y;
            return (listBox.Items.Count, lineY);
        }

        return (0, 0);
    }

    private void ShowDropIndicator(ListBox listBox, double lineY)
    {
        var layer = AdornerLayer.GetAdornerLayer(listBox);
        if (layer is null)
        {
            return;
        }

        if (dropIndicatorAdorner is null)
        {
            dropIndicatorAdorner = new DropIndicatorAdorner(listBox);
            layer.Add(dropIndicatorAdorner);
        }

        dropIndicatorAdorner.LineY = lineY;
        dropIndicatorAdorner.InvalidateVisual();
    }

    private void RemoveDropIndicator()
    {
        if (dropIndicatorAdorner is null)
        {
            return;
        }

        var layer = AdornerLayer.GetAdornerLayer(dropIndicatorAdorner.AdornedElement);
        layer?.Remove(dropIndicatorAdorner);
        dropIndicatorAdorner = null;
    }
}
