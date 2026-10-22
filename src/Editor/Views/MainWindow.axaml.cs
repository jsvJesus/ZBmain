using System.Diagnostics;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Editor.ViewModels;
using Vector = Avalonia.Vector;
using Editor.Controls;

namespace Editor.Views;

public sealed partial class MainWindow : Window
{
    private readonly HashSet<Key> _pressedKeys = new();
    private readonly DispatcherTimer _flightTimer;
    private readonly Stopwatch _clock = new();

    private Point _lastPointerPosition;
    private bool _isMouseLooking;
    private double _lastFrameTime;

    public MainWindow()
    {
        InitializeComponent();

        _flightTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        _flightTimer.Tick += FlightTimerTick;

        Opened += WindowOpened;
        Closed += WindowClosed;
        Deactivated += WindowDeactivated;
    }

    private void OpenVegetationPreviewClicked(
        object? sender,
        RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.Vegetation is null)
        {
            var informationWindow = new Window
            {
                Title = "Растительность",
                Width = 570,
                Height = 170,
                Content = new TextBlock
                {
                    Text = "Открой уровень с импортированным " +
                           "vegetation/details.zdetail.",
                    Margin = new Thickness(24),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                }
            };

            informationWindow.Show(this);
            return;
        }

        var status = new TextBlock
        {
            Text =
                "Direct3D 11 | Предпросмотр растительности | " +
                $"Ячеек: {viewModel.Vegetation.Cells.Length:N0}",
            Margin = new Thickness(12, 8),
            Foreground = Avalonia.Media.Brushes.White
        };

        var preview = new Direct3D11PreviewControl
        {
            Vegetation = viewModel.Vegetation
        };

        preview.RenderFailed += message =>
        {
            status.Text = message;
        };

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Background = Avalonia.Media.Brushes.Black
        };

        layout.Children.Add(status);

        Grid.SetRow(preview, 1);
        layout.Children.Add(preview);

        var window = new Window
        {
            Title = "DX11 — Растительность",
            Width = 1200,
            Height = 800,
            MinWidth = 640,
            MinHeight = 440,
            Content = layout
        };

        window.Show(this);
    }

    private void WindowOpened(object? sender, EventArgs e)
    {
        _clock.Restart();
        _lastFrameTime = 0;
        _flightTimer.Start();
    }

    private void WindowClosed(object? sender, EventArgs e)
    {
        _flightTimer.Stop();
        _clock.Stop();
        ResetInput();
    }

    private void WindowDeactivated(object? sender, EventArgs e)
    {
        ResetInput();
    }

    private void ResetInput()
    {
        _pressedKeys.Clear();
        _isMouseLooking = false;
    }

    private void FlightTimerTick(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;

        float deltaTime = (float)Math.Clamp(
            now - _lastFrameTime,
            0.0,
            0.05);

        _lastFrameTime = now;

        if (deltaTime <= 0f || _pressedKeys.Count == 0)
        {
            return;
        }

        float forward = 0f;
        float right = 0f;
        float vertical = 0f;

        if (_pressedKeys.Contains(Key.W))
        {
            forward += 1f;
        }

        if (_pressedKeys.Contains(Key.S))
        {
            forward -= 1f;
        }

        if (_pressedKeys.Contains(Key.D))
        {
            right += 1f;
        }

        if (_pressedKeys.Contains(Key.A))
        {
            right -= 1f;
        }

        if (_pressedKeys.Contains(Key.Space))
        {
            vertical += 1f;
        }

        if (_pressedKeys.Contains(Key.LeftShift) ||
            _pressedKeys.Contains(Key.RightShift))
        {
            vertical -= 1f;
        }

        Vector3 movement = new(
            right,
            vertical,
            forward);

        if (movement.LengthSquared() > 0f)
        {
            SceneView.Fly(
                movement,
                deltaTime);
        }
    }

    private void ScenePointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        SceneInputOverlay.Focus();

        PointerPointProperties properties =
            e.GetCurrentPoint(SceneInputOverlay).Properties;

        if (!properties.IsRightButtonPressed)
        {
            return;
        }

        _isMouseLooking = true;
        _lastPointerPosition = e.GetPosition(SceneInputOverlay);

        e.Pointer.Capture(SceneInputOverlay);
        e.Handled = true;
    }

    private void ScenePointerMoved(
        object? sender,
        PointerEventArgs e)
    {
        if (!_isMouseLooking)
        {
            return;
        }

        if (!e.GetCurrentPoint(
                SceneInputOverlay).Properties.IsRightButtonPressed)
        {
            _isMouseLooking = false;
            return;
        }

        Point current = e.GetPosition(SceneInputOverlay);
        Vector delta = current - _lastPointerPosition;

        _lastPointerPosition = current;

        if (delta.X != 0 || delta.Y != 0)
        {
            SceneView.Look(
                (float)delta.X,
                (float)delta.Y);
        }

        e.Handled = true;
    }

    private void ScenePointerReleased(
        object? sender,
        PointerReleasedEventArgs e)
    {
        if (!_isMouseLooking)
        {
            return;
        }

        _isMouseLooking = false;

        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void ScenePointerCaptureLost(
        object? sender,
        PointerCaptureLostEventArgs e)
    {
        _isMouseLooking = false;
    }

    private void ScenePointerWheel(
        object? sender,
        PointerWheelEventArgs e)
    {
        SceneView.AdjustSpeed((float)e.Delta.Y);
        e.Handled = true;
    }

    private void SceneKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Home)
        {
            SceneView.ShowMapTopDown();
            e.Handled = true;
            return;
        }

        if (!IsMovementKey(e.Key))
        {
            return;
        }

        _pressedKeys.Add(e.Key);
        e.Handled = true;
    }

    private void SceneKeyUp(
        object? sender,
        KeyEventArgs e)
    {
        if (!IsMovementKey(e.Key))
        {
            return;
        }

        _pressedKeys.Remove(e.Key);
        e.Handled = true;
    }

    private void SceneLostFocus(
        object? sender,
        RoutedEventArgs e)
    {
        ResetInput();
    }

    private static bool IsMovementKey(Key key)
    {
        return key is
            Key.W or
            Key.A or
            Key.S or
            Key.D or
            Key.Space or
            Key.LeftShift or
            Key.RightShift;
    }

    private void NewSceneClicked(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            ResetInput();
            viewModel.NewScene();
            SceneView.Scene = null;
        }
    }

    private void RefreshClicked(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Refresh();
        }
    }

    private async void ImportClicked(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            ResetInput();
            await viewModel.ImportSelectedAsync();
            SceneView.Scene = viewModel.Scene;
        }
    }

    private async void OpenClicked(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            ResetInput();
            await viewModel.OpenSelectedAsync();
            SceneView.Scene = viewModel.Scene;
        }
    }
}
