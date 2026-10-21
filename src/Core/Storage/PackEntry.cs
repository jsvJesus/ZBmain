namespace Core.Storage;

public sealed record PackEntry(string Name, string FullPath, bool IsDirectory)
{
    public string TypeName => IsDirectory ? "Папка" : "Файл";
}
