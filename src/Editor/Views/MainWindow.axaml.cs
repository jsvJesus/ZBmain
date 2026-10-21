
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Editor.ViewModels;

namespace Editor.Views;

public sealed partial class MainWindow : Window
{
    private enum CameraAction
    {
        None,
        Orbit,
        Pan
    }

    private CameraAction _cameraAction;
    private Point _lastPointerPosition;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void NewSceneClicked(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
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
            await viewModel.OpenSelectedAsync();
            SceneView.Scene = viewModel.Scene;
        }
    }

    private void ScenePointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        PointerPointProperties properties =
            e.GetCurrentPoint(SceneInputOverlay).Properties;

        if (properties.IsRightButtonPressed ||
            properties.IsMiddleButtonPressed)
        {
            _cameraAction = CameraAction.Pan;
        }
        else if (properties.IsLeftButtonPressed)
        {
            _cameraAction = CameraAction.Orbit;
        }
        else
        {
            return;
        }

        _lastPointerPosition =
            e.GetPosition(SceneInputOverlay);

        SceneInputOverlay.Focus();
        e.Pointer.Capture(SceneInputOverlay);

        e.Handled = true;
    }

    private void ScenePointerMoved(
        object? sender,
        PointerEventArgs e)
    {
        if (_cameraAction == CameraAction.None)
        {
            return;
        }

        Point current = e.GetPosition(SceneInputOverlay);
        Vector delta = current - _lastPointerPosition;

        _lastPointerPosition = current;

        switch (_cameraAction)
        {
            case CameraAction.Orbit:
                SceneView.Orbit(
                    (float)delta.X,
                    (float)delta.Y);
                break;

            case CameraAction.Pan:
                SceneView.Pan(
                    (float)delta.X,
                    (float)delta.Y);
                break;
        }

        e.Handled = true;
    }

    private void ScenePointerReleased(
        object? sender,
        PointerReleasedEventArgs e)
    {
        _cameraAction = CameraAction.None;

        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void ScenePointerCaptureLost(
        object? sender,
        PointerCaptureLostEventArgs e)
    {
        _cameraAction = CameraAction.None;
    }

    private void ScenePointerWheel(
        object? sender,
        PointerWheelEventArgs e)
    {
        SceneView.Zoom((float)e.Delta.Y);
        e.Handled = true;
    }

    private void SceneKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        float speed = e.KeyModifiers.HasFlag(
            KeyModifiers.Shift)
            ? 5f
            : 1f;

        switch (e.Key)
        {
            case Key.W:
                SceneView.MoveCamera(speed, 0, 0);
                break;

            case Key.S:
                SceneView.MoveCamera(-speed, 0, 0);
                break;

            case Key.A:
                SceneView.MoveCamera(0, -speed, 0);
                break;

            case Key.D:
                SceneView.MoveCamera(0, speed, 0);
                break;

            case Key.Q:
                SceneView.MoveCamera(0, 0, -speed);
                break;

            case Key.E:
                SceneView.MoveCamera(0, 0, speed);
                break;

            case Key.R:
                SceneView.ResetCamera();
                break;

            default:
                return;
        }

        e.Handled = true;
    }
}
