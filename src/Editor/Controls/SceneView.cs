
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Core.Levels;

namespace Editor.Controls;

public sealed class SceneView : Control
{
    private static readonly Pen GridPen = new(
        new SolidColorBrush(Color.FromRgb(33, 46, 65)),
        1);

    private static readonly Pen AxisPen = new(
        new SolidColorBrush(Color.FromRgb(70, 98, 138)),
        1);

    private static readonly Pen MeshPen = new(
        new SolidColorBrush(Color.FromRgb(77, 171, 210)),
        0.65);

    private static readonly IBrush BackgroundBrush =
        new SolidColorBrush(Color.FromRgb(12, 19, 30));

    private LevelPreview? _scene;

    public LevelPreview? Scene
    {
        get => _scene;
        set
        {
            _scene = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        Rect rect = new(Bounds.Size);

        context.FillRectangle(
            BackgroundBrush,
            rect);

        for (int i = 1; i < 20; i++)
        {
            double x = rect.Width * i / 20;
            double y = rect.Height * i / 20;

            context.DrawLine(
                GridPen,
                new Point(x, 0),
                new Point(x, rect.Height));

            context.DrawLine(
                GridPen,
                new Point(0, y),
                new Point(rect.Width, y));
        }

        context.DrawLine(
            AxisPen,
            new Point(rect.Width / 2, 0),
            new Point(rect.Width / 2, rect.Height));

        context.DrawLine(
            AxisPen,
            new Point(0, rect.Height / 2),
            new Point(rect.Width, rect.Height / 2));

        if (_scene is null ||
            _scene.Triangles.Count == 0)
        {
            return;
        }

        double worldWidth = Math.Max(
            1,
            _scene.Max.X - _scene.Min.X);

        double worldHeight = Math.Max(
            1,
            _scene.Max.Y - _scene.Min.Y);

        double scale = Math.Min(
            Math.Max(1, rect.Width - 48) / worldWidth,
            Math.Max(1, rect.Height - 48) / worldHeight);

        double offsetX =
            rect.Width / 2 -
            (_scene.Min.X + _scene.Max.X) * scale / 2;

        double offsetY =
            rect.Height / 2 +
            (_scene.Min.Y + _scene.Max.Y) * scale / 2;

        Point Map(System.Numerics.Vector2 point) =>
            new(
                point.X * scale + offsetX,
                -point.Y * scale + offsetY);

        foreach (PreviewTriangle triangle in _scene.Triangles)
        {
            Point a = Map(triangle.A);
            Point b = Map(triangle.B);
            Point c = Map(triangle.C);

            context.DrawLine(MeshPen, a, b);
            context.DrawLine(MeshPen, b, c);
            context.DrawLine(MeshPen, c, a);
        }
    }
}
