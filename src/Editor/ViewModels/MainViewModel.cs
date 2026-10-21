
using System.Collections.ObjectModel;
using Core.Levels;

namespace Editor.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly LevelArchiveImporter _importer = new();
    private readonly LevelPreviewReader _reader = new();

    private string? _selectedSource;
    private string? _selectedPackage;
    private string _sceneTitle = "Пустая сцена";
    private string _statusText = "Готово. Сцена не загружена.";
    private LevelPreview? _preview;
    private bool _isBusy;

    public MainViewModel()
    {
        Refresh();
    }

    public ObservableCollection<string> SourceLevels { get; } = new();

    public ObservableCollection<string> SavedLevels { get; } = new();

    public LevelPreview? Preview => _preview;

    public string? SelectedSource
    {
        get => _selectedSource;
        set
        {
            _selectedSource = value;
            OnPropertyChanged();
        }
    }

    public string? SelectedPackage
    {
        get => _selectedPackage;
        set
        {
            _selectedPackage = value;
            OnPropertyChanged();
        }
    }

    public string SceneTitle
    {
        get => _sceneTitle;
        private set
        {
            _sceneTitle = value;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public void Refresh()
    {
        string? oldSource = SelectedSource;
        string? oldPackage = SelectedPackage;

        SourceLevels.Clear();
        SavedLevels.Clear();

        foreach (string name in LevelPaths.FindImports())
        {
            SourceLevels.Add(name);
        }

        foreach (string name in LevelPaths.FindPackages())
        {
            SavedLevels.Add(name);
        }

        SelectedSource = SourceLevels.Contains(oldSource)
            ? oldSource
            : null;

        SelectedPackage = SavedLevels.Contains(oldPackage)
            ? oldPackage
            : null;
    }

    public void NewScene()
    {
        _preview = null;

        OnPropertyChanged(nameof(Preview));

        SceneTitle = "Пустая сцена";
        StatusText = "Создана пустая сцена.";
    }

    public async Task ImportSelectedAsync()
    {
        if (_isBusy || string.IsNullOrWhiteSpace(SelectedSource))
        {
            StatusText = "Выберите исходный уровень слева.";
            return;
        }

        _isBusy = true;

        string name = SelectedSource;

        StatusText =
            $"Импорт {name}: обработка исходных данных...";

        try
        {
            LevelImportResult result = await Task.Run(
                () => _importer.Import(name));

            Refresh();

            SelectedPackage = name;

            await OpenSelectedAsync();

            StatusText =
                $"Импортировано: {result.FileCount} файлов, " +
                $"{result.Vertices:N0} вершин, " +
                $"{result.Triangles:N0} граней коллизии.";
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                ArgumentException)
        {
            StatusText =
                "Ошибка импорта: " + exception.Message;
        }
        finally
        {
            _isBusy = false;
        }
    }

    public async Task OpenSelectedAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedPackage))
        {
            StatusText =
                "Выберите сохранённый уровень справа.";
            return;
        }

        string name = SelectedPackage;

        StatusText = $"Загрузка {name}...";

        try
        {
            LevelPreview preview = await Task.Run(
                () => _reader.Read(
                    LevelPaths.PackagePath(name)));

            _preview = preview;

            OnPropertyChanged(nameof(Preview));

            SceneTitle = preview.Name;

            StatusText =
                $"Открыто: {name}. " +
                "Отображается каркас коллизий (вид сверху).";
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                ArgumentException or
                System.Text.Json.JsonException or
                OverflowException)
        {
            StatusText =
                "Ошибка открытия: " + exception.Message;
        }
    }
}
