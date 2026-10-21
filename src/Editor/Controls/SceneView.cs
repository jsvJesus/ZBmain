
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Core.Levels;

namespace Editor.Controls;

public sealed class SceneView : Control
{
    private readonly record struct ScreenTriangle(
        Point A,
        Point B,
        Point C);

    private static readonly IBrush BackgroundBrush =
        new SolidColorBrush(Color.FromRgb(12, 19, 30));

    private static readonly Pen GridPen = new(
        new SolidColorBrush(Color.FromRgb(37, 52, 69)),
        1);

    private static readonly Pen AxisPen = new(
        new SolidColorBrush(Color.FromRgb(78, 107, 137)),
        1.3);

    private static readonly IBrush[] FaceBrushes =
        CreateFaceBrushes();

    private LevelPreview? _scene;
    private Vector3 _target;

    private float _radius = 100f;
    private float _distance = 240f;
    private float _yaw = 0.7f;
    private float _pitch = 0.65f;

    private Point? _lastPointer;
    private bool _orbiting;
    private bool _panning;

    public SceneView()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public LevelPreview? Scene
    {
        get => _scene;
        set
        {
            _scene = value;
            ResetCamera();
            InvalidateVisual();
        }
    }

    public void ResetCamera()
    {
        _yaw = 0.7f;
        _pitch = 0.65f;

        if (_scene is null)
        {
            _target = Vector3.Zero;
            _radius = 100f;
        }
        else
        {
            _target = (_scene.Min + _scene.Max) / 2f;

            _radius = MathF.Max(
                50f,
                (_scene.Max - _scene.Min).Length() * 0.5f);
        }

        _distance = _radius * 2.8f;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(
        PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        PointerPointProperties properties =
            e.GetCurrentPoint(this).Properties;

        _orbiting = properties.IsLeftButtonPressed;

        _panning = properties.IsRightButtonPressed ||
                   properties.IsMiddleButtonPressed;

        if (!_orbiting && !_panning)
        {
            return;
        }

        _lastPointer = e.GetPosition(this);

        e.Pointer.Capture(this);
        Focus();

        e.Handled = true;
    }

    protected override void OnPointerReleased(
        PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        _lastPointer = null;
        _orbiting = false;
        _panning = false;

        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(
        PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        _lastPointer = null;
        _orbiting = false;
        _panning = false;
    }

    protected override void OnPointerMoved(
        PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_lastPointer is null ||
            (!_orbiting && !_panning))
        {
            return;
        }

        Point current = e.GetPosition(this);
        Avalonia.Vector delta = current - _lastPointer.Value;

        _lastPointer = current;

        if (_orbiting)
        {
            _yaw += (float)delta.X * 0.007f;

            _pitch = Math.Clamp(
                _pitch + (float)delta.Y * 0.007f,
                -1.45f,
                1.45f);
        }
        else if (_panning)
        {
            GetAxes(
                out _,
                out Vector3 right,
                out Vector3 up);

            float scale = _distance /
                          MathF.Max(1f, (float)Bounds.Height);

            _target += (float)-delta.X * scale * right;
            _target += (float)delta.Y * scale * up;
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(
        PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        _distance = Math.Clamp(
            _distance * MathF.Pow(
                0.84f,
                (float)e.Delta.Y),
            0.5f,
            _radius * 50f);

        InvalidateVisual();
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        context.FillRectangle(
            BackgroundBrush,
            new Rect(Bounds.Size));

        if (Bounds.Width < 2 || Bounds.Height < 2)
        {
            return;
        }

        GetAxes(
            out Vector3 forward,
            out Vector3 right,
            out Vector3 up);

        DrawGrid(
            context,
            forward,
            right,
            up);

        if (_scene is null ||
            _scene.Triangles.Count == 0)
        {
            return;
        }

        const int depthBands = 40;
        const int materialBands = 6;
        const int shadeBands = 4;

        int colorsPerBand = materialBands * shadeBands;

        float near = MathF.Max(
            0.02f,
            _distance / 10000f);

        float far = _distance + _radius * 3f;

        var batches = new Dictionary<
            int,
            List<ScreenTriangle>>();

        foreach (PreviewTriangle triangle in _scene.Triangles)
        {
            if (!Project(
                    triangle.A,
                    forward,
                    right,
                    up,
                    near,
                    out Point a,
                    out float za) ||
                !Project(
                    triangle.B,
                    forward,
                    right,
                    up,
                    near,
                    out Point b,
                    out float zb) ||
                !Project(
                    triangle.C,
                    forward,
                    right,
                    up,
                    near,
                    out Point c,
                    out float zc))
            {
                continue;
            }

            double signedArea =
                (b.X - a.X) * (c.Y - a.Y) -
                (b.Y - a.Y) * (c.X - a.X);

            if (Math.Abs(signedArea) < 0.5)
            {
                continue;
            }

            if ((a.X < -Bounds.Width &&
                 b.X < -Bounds.Width &&
                 c.X < -Bounds.Width) ||
                (a.X > Bounds.Width * 2 &&
                 b.X > Bounds.Width * 2 &&
                 c.X > Bounds.Width * 2) ||
                (a.Y < -Bounds.Height &&
                 b.Y < -Bounds.Height &&
                 c.Y < -Bounds.Height) ||
                (a.Y > Bounds.Height * 2 &&
                 b.Y > Bounds.Height * 2 &&
                 c.Y > Bounds.Height * 2))
            {
                continue;
            }

            Vector3 normal = Vector3.Cross(
                triangle.B - triangle.A,
                triangle.C - triangle.A);

            float brightness =
                normal.LengthSquared() > 0.0001f
                    ? MathF.Abs(Vector3.Dot(
                        Vector3.Normalize(normal),
                        Vector3.Normalize(
                            new Vector3(0.35f, 0.8f, 0.45f))))
                    : 0.5f;

            int shade = Math.Clamp(
                (int)(brightness * shadeBands),
                0,
                shadeBands - 1);

            int material = triangle.Material % materialBands;

            float depth = (za + zb + zc) / 3f;

            int band = Math.Clamp(
                (int)(depth / far * depthBands),
                0,
                depthBands - 1);

            int key =
                band * colorsPerBand +
                material * shadeBands +
                shade;

            if (!batches.TryGetValue(
                    key,
                    out List<ScreenTriangle>? faces))
            {
                faces = new List<ScreenTriangle>();
                batches.Add(key, faces);
            }

            faces.Add(new ScreenTriangle(a, b, c));
        }

        foreach (KeyValuePair<int, List<ScreenTriangle>> batch
                 in batches.OrderByDescending(item => item.Key))
        {
            var geometry = new StreamGeometry();

            using (StreamGeometryContext path = geometry.Open())
            {
                foreach (ScreenTriangle triangle in batch.Value)
                {
                    path.BeginFigure(triangle.A, true);
                    path.LineTo(triangle.B);
                    path.LineTo(triangle.C);
                    path.EndFigure(true);
                }
            }

            context.DrawGeometry(
                FaceBrushes[batch.Key % colorsPerBand],
                null,
                geometry);
        }
    }

    private void DrawGrid(
        DrawingContext context,
        Vector3 forward,
        Vector3 right,
        Vector3 up)
    {
        float floor = _scene?.Min.Y ?? 0f;

        float gridRange = MathF.Max(
            50f,
            _radius * 1.3f);

        float step = MathF.Pow(
            10f,
            MathF.Floor(
                MathF.Log10(gridRange / 12f)));

        step = MathF.Max(1f, step);

        int count = Math.Clamp(
            (int)MathF.Ceiling(gridRange / step),
            2,
            25);

        float centerX =
            MathF.Round(_target.X / step) * step;

        float centerZ =
            MathF.Round(_target.Z / step) * step;

        for (int i = -count; i <= count; i++)
        {
            float x = centerX + i * step;
            float z = centerZ + i * step;

            DrawWorldLine(
                context,
                new Vector3(
                    x,
                    floor,
                    centerZ - count * step),
                new Vector3(
                    x,
                    floor,
                    centerZ + count * step),
                x == 0 ? AxisPen : GridPen,
                forward,
                right,
                up);

            DrawWorldLine(
                context,
                new Vector3(
                    centerX - count * step,
                    floor,
                    z),
                new Vector3(
                    centerX + count * step,
                    floor,
                    z),
                z == 0 ? AxisPen : GridPen,
                forward,
                right,
                up);
        }
    }

    private void DrawWorldLine(
        DrawingContext context,
        Vector3 a,
        Vector3 b,
        Pen pen,
        Vector3 forward,
        Vector3 right,
        Vector3 up)
    {
        if (Project(
                a,
                forward,
                right,
                up,
                0.01f,
                out Point start,
                out _) &&
            Project(
                b,
                forward,
                right,
                up,
                0.01f,
                out Point end,
                out _))
        {
            context.DrawLine(
                pen,
                start,
                end);
        }
    }

    private bool Project(
        Vector3 point,
        Vector3 forward,
        Vector3 right,
        Vector3 up,
        float near,
        out Point screen,
        out float depth)
    {
        Vector3 delta = point - _target;

        depth = _distance - Vector3.Dot(
            delta,
            forward);

        if (depth <= near ||
            !float.IsFinite(depth))
        {
            screen = default;
            return false;
        }

        float focal =
            (float)Bounds.Height * 0.9f;

        screen = new Point(
            Bounds.Width / 2 +
            Vector3.Dot(delta, right) * focal / depth,
            Bounds.Height / 2 -
            Vector3.Dot(delta, up) * focal / depth);

        return double.IsFinite(screen.X) &&
               double.IsFinite(screen.Y);
    }

    private void GetAxes(
        out Vector3 forward,
        out Vector3 right,
        out Vector3 up)
    {
        forward = new Vector3(
            MathF.Cos(_pitch) * MathF.Sin(_yaw),
            MathF.Sin(_pitch),
            MathF.Cos(_pitch) * MathF.Cos(_yaw));

        right = Vector3.Normalize(
            Vector3.Cross(
                Vector3.UnitY,
                forward));

        up = Vector3.Normalize(
            Vector3.Cross(
                forward,
                right));
    }

    private static IBrush[] CreateFaceBrushes()
    {
        Color[] baseColors =
        [
            Color.FromRgb(95, 119, 111),
            Color.FromRgb(113, 111, 99),
            Color.FromRgb(91, 111, 127),
            Color.FromRgb(116, 114, 132),
            Color.FromRgb(99, 124, 106),
            Color.FromRgb(130, 120, 100)
        ];

        var brushes = new IBrush[baseColors.Length * 4];

        for (int material = 0;
             material < baseColors.Length;
             material++)
        {
            for (int shade = 0; shade < 4; shade++)
            {
                float factor =
                    0.48f + shade * 0.18f;

                Color color = baseColors[material];

                brushes[material * 4 + shade] =
                    new SolidColorBrush(
                        Color.FromRgb(
                            (byte)Math.Clamp(
                                (int)(color.R * factor),
                                0,
                                255),
                            (byte)Math.Clamp(
                                (int)(color.G * factor),
                                0,
                                255),
                            (byte)Math.Clamp(
                                (int)(color.B * factor),
                                0,
                                255)));
            }
        }

        return brushes;
    }
}
