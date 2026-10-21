using Core.Storage;

namespace Server.Hosting;

public sealed class ConsoleServer
{
    private readonly PackCatalog _catalog = new();

    public void Run()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        ProjectPaths.EnsurePacksDirectory();

        Console.WriteLine("Server — консольный каркас серверной части.");
        Console.WriteLine("Сетевой сервер и игровой мир ещё не реализованы.");
        Console.WriteLine("Команды: help, status, paths, stop");
        Console.WriteLine();

        while (true)
        {
            Console.Write("server> ");
            string? command = Console.ReadLine()?.Trim().ToLowerInvariant();

            switch (command)
            {
                case "help":
                    Console.WriteLine("help — команды; status — состояние; paths — каталоги; stop — выход.");
                    break;
                case "status":
                    Console.WriteLine($"Каркас активен. Элементов packs: {_catalog.GetTopLevelEntries().Count}.");
                    break;
                case "paths":
                    Console.WriteLine($"bin: {ProjectPaths.BinDirectory}");
                    Console.WriteLine($"packs: {ProjectPaths.PacksDirectory}");
                    break;
                case "stop":
                case "exit":
                case null:
                    Console.WriteLine("Сервер остановлен.");
                    return;
                default:
                    Console.WriteLine("Неизвестная команда. Введите help.");
                    break;
            }
        }
    }
}
