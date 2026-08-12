using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PlayMuse;

/// <summary>
/// プレイリストの並べ替えドラッグ中に、挿入先の位置を示す水平線を描画するAdorner。
/// ヒットテストの対象にはならず、あくまで視覚的なガイドとして表示される。
/// </summary>
internal sealed class DropIndicatorAdorner : Adorner
{
    private readonly Pen pen = new(new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x1E)), 2)
    {
        DashCap = PenLineCap.Round,
    };

    public DropIndicatorAdorner(UIElement adornedElement) : base(adornedElement)
    {
        IsHitTestVisible = false;
    }

    public double LineY { get; set; }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = AdornedElement.RenderSize.Width;
        drawingContext.DrawLine(pen, new Point(0, LineY), new Point(width, LineY));

        const double markerRadius = 3.5;
        drawingContext.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x1E)), null, new Point(markerRadius, LineY), markerRadius, markerRadius);
    }
}
