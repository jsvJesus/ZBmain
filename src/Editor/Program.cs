using Avalonia;

namespace Editor;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildApp().StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
