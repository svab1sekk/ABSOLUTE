using System.Windows;
using System.Windows.Media;

namespace Absolute.App.Controls;

internal sealed class Sparkline : FrameworkElement
{
    private readonly Queue<double> values = new();
    public void Add(double value)
    {
        values.Enqueue(Math.Clamp(value, 0, 100));
        while (values.Count > 50) values.Dequeue();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(40, 158, 120, 200)), 1);
        for (var y = 0; y < 4; y++) context.DrawLine(grid, new Point(0, y * ActualHeight / 3), new Point(ActualWidth, y * ActualHeight / 3));
        if (values.Count < 2) return;
        var points = values.Select((value, index) => new Point(ActualWidth * index / 49, ActualHeight - value / 100 * (ActualHeight - 4))).ToArray();
        var line = new StreamGeometry();
        using (var drawing = line.Open()) { drawing.BeginFigure(points[0], false, false); drawing.PolyLineTo(points.Skip(1).ToArray(), true, false); }
        line.Freeze();
        context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(176, 132, 222)), 2), line);
    }
}
