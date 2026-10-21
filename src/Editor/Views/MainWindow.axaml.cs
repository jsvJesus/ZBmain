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

    private void RefreshClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Refresh();
        }
    }
}
