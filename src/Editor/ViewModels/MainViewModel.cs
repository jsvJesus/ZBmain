
using System.Collections.ObjectModel;
using Core.Levels;

namespace Editor.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly LevelWorkspaceImporter _importer = new();
    private readonly RenderLevelReader _reader = new();
    private readonly DetailLevelReader _detailReader = new();

    private string? _selectedSource;
    private string? _selectedPackage;

    private string _sceneTitle = "Пустая сцена";
    private string _statusText = "Сцена не загружена.";

    private RenderLevel? _scene;
    private DetailLevel? _vegetation;
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

    public DetailLevel? Vegetation
    {
        get => _vegetation;
        private set
        {
            _vegetation = value;
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
        Vegetation = null;

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
            $"Импорт {levelName}: обработка геометрии и растительности...";

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
                $"Мешей: {result.VisualMeshes:N0}. " +
                $"Растительность: {result.DetailModels:N0} моделей, " +
                $"{result.DetailCells:N0} ячеек, " +
                $"{result.DetailLayers:N0} непустых слоёв.";
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

            var loaded = await Task.Run(() =>
            {
                RenderLevel level = _reader.Read(path);

                DetailLevel? vegetation = null;

                if (Directory.Exists(path))
                {
                    string detailsPath = Path.Combine(
                        path,
                        "vegetation",
                        "details.zdetail");

                    if (File.Exists(detailsPath))
                    {
                        vegetation = _detailReader.Read(detailsPath);
                    }
                }

                return (Level: level, Vegetation: vegetation);
            });

            Scene = loaded.Level;
            Vegetation = loaded.Vegetation;

            SceneTitle = loaded.Level.Name;

            long triangles = loaded.Level.Surfaces.Sum(
                surface => (long)surface.IndexCount / 3);

            string sourceType = Directory.Exists(path)
                ? "рабочая папка"
                : "архив";

            string vegetationStatus = Vegetation is null
                ? "Растительность: не импортирована."
                : $"Растительность: {Vegetation.Models.Length:N0} моделей, " +
                  $"{Vegetation.Cells.Length:N0} ячеек, " +
                  $"{Vegetation.ActiveLayers:N0} непустых слоёв.";

            StatusText =
                $"Открыто: {loaded.Level.Name} ({sourceType}). " +
                $"Мешей: {loaded.Level.Surfaces.Length:N0}, " +
                $"треугольников: {triangles:N0}. " +
                vegetationStatus;
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
