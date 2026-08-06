using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
    private const string TrackDragFormat = "PlayMuse.Track";

    private readonly MainViewModel viewModel;
    private readonly DispatcherTimer positionTimer;
    private Point? dragStartPoint;
    private Track? draggingTrack;
    private DropIndicatorAdorner? dropIndicatorAdorner;

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

    private void TrackList_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(TrackDragFormat) && sender is ListBox listBox)
        {
            e.Effects = DragDropEffects.Move;
            var (_, lineY) = GetDropTarget(listBox, e);
            ShowDropIndicator(listBox, lineY);
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            RemoveDropIndicator();
        }
        else
        {
            e.Effects = DragDropEffects.None;
            RemoveDropIndicator();
        }

        e.Handled = true;
    }

    private void TrackList_DragLeave(object sender, DragEventArgs e)
        => RemoveDropIndicator();

    private void TrackList_Drop(object sender, DragEventArgs e)
    {
        RemoveDropIndicator();
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
        }
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
