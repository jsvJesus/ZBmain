using Avalonia.Controls;
using Avalonia.Interactivity;
using Editor.ViewModels;

namespace Editor.Views;

public sealed partial class MainWindow : Window
{
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
            SceneView.Scene = viewModel.Preview;
        }
    }

    private async void OpenClicked(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.OpenSelectedAsync();
            SceneView.Scene = viewModel.Preview;
        }
    }
}
