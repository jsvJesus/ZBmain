using Core.Storage;

namespace Game.Runtime;

public static class ClientBootstrap
{
    public static void Run()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        ProjectPaths.EnsurePacksDirectory();

        Console.WriteLine("Game — базовая точка входа игрового клиента.");
        Console.WriteLine("Графическая подсистема будет выбрана позже.");
        Console.WriteLine($"Каталог ресурсов: {ProjectPaths.PacksDirectory}");
        Console.WriteLine($"Объектов верхнего уровня: {new PackCatalog().GetTopLevelEntries().Count}");
        Console.WriteLine();
        Console.WriteLine("Нажмите Enter для выхода.");
        Console.ReadLine();
    }
}
