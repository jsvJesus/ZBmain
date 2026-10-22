
using System.Collections.ObjectModel;
using Core.Levels;

namespace Editor.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly LevelWorkspaceImporter _importer = new();
    private readonly RenderLevelReader _reader = new();

    private string? _selectedSource;
    private string? _selectedPackage;

    private string _sceneTitle = "Пустая сцена";
    private string _statusText = "Сцена не загружена.";

    private RenderLevel? _scene;
    private bool _isBusy;

    public MainViewModel()
    {
        Refresh();
    }

    public ObservableCollection<string> SourceLevels { get; } = new();

    public ObservableCollection<string> SavedLevels { get; } = new();

    public RenderLevel? Scene
    {
        get => _scene;
        private set
        {
            _scene = value;
            OnPropertyChanged();
        }
    }

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
        LevelPaths.EnsureDirectories();

        string? previousSource = SelectedSource;
        string? previousPackage = SelectedPackage;

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

        SelectedSource = previousSource is not null &&
                         SourceLevels.Contains(previousSource)
            ? previousSource
            : null;

        SelectedPackage = previousPackage is not null &&
                          SavedLevels.Contains(previousPackage)
            ? previousPackage
            : null;
    }

    public void NewScene()
    {
        Scene = null;
        SceneTitle = "Пустая сцена";
        StatusText = "Создана пустая сцена.";
    }

    public async Task ImportSelectedAsync()
    {
        if (_isBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedSource))
        {
            StatusText = "Выберите исходный уровень.";
            return;
        }

        _isBusy = true;

        string levelName = SelectedSource;

        StatusText =
            $"Импорт {levelName}: создание рабочей папки...";

        try
        {
            LevelWorkspaceImportResult result =
                await Task.Run(
                    () => _importer.Import(levelName));

            Refresh();
            SelectedPackage = levelName;

            await OpenSelectedAsync();

            StatusText =
                $"Импортировано: {result.FileCount} файлов. " +
                $"Мешей: {result.VisualMeshes:N0}, " +
                $"треугольников: {result.VisualTriangles:N0}. " +
                $"Папка: {result.DirectoryPath}";
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                ArgumentException or
                NotSupportedException or
                OverflowException)
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
            StatusText = "Выберите уровень.";
            return;
        }

        string levelName = SelectedPackage;

        StatusText = $"Открытие {levelName}...";

        try
        {
            string path = LevelPaths.OpenPath(levelName);

            RenderLevel level = await Task.Run(
                () => _reader.Read(path));

            Scene = level;
            SceneTitle = level.Name;

            long triangles = level.Surfaces.Sum(
                surface => (long)surface.IndexCount / 3);

            string sourceType = Directory.Exists(path)
                ? "рабочая папка"
                : "архив";

            StatusText =
                $"Открыто: {level.Name} ({sourceType}). " +
                $"Мешей: {level.Surfaces.Length:N0}, " +
                $"треугольников: {triangles:N0}, " +
                $"материалов: {level.Materials.Length:N0}.";
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                ArgumentException or
                NotSupportedException or
                OverflowException)
        {
            StatusText =
                "Ошибка открытия: " + exception.Message;
        }
    }
}
