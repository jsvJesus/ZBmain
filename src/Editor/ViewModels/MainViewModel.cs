using System.Collections.ObjectModel;
using Core.Storage;

namespace Editor.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly PackCatalog _catalog;
    private PackEntry? _selectedEntry;
    private string _statusText = "Готово";

    public MainViewModel(PackCatalog catalog)
    {
        _catalog = catalog;
        Refresh();
    }

    public ObservableCollection<PackEntry> Entries { get; } = new();

    public string PacksPath => ProjectPaths.PacksDirectory;

    public string SelectedName => SelectedEntry?.Name ?? "Выберите элемент из списка слева";

    public string SelectedType => SelectedEntry?.TypeName ?? "—";

    public string SelectedPath => SelectedEntry?.FullPath ?? "—";

    public PackEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (ReferenceEquals(_selectedEntry, value))
            {
                return;
            }

            _selectedEntry = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedName));
            OnPropertyChanged(nameof(SelectedType));
            OnPropertyChanged(nameof(SelectedPath));
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
        try
        {
            IReadOnlyList<PackEntry> items = _catalog.GetTopLevelEntries();
            SelectedEntry = null;
            Entries.Clear();

            foreach (PackEntry item in items)
            {
                Entries.Add(item);
            }

            StatusText = $"Готово · объектов в packs: {Entries.Count}";
        }
        catch (IOException exception)
        {
            StatusText = $"Ошибка доступа к ресурсам: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            StatusText = $"Недостаточно прав: {exception.Message}";
        }
    }
}
