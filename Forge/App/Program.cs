using Avalonia;
using Avalonia.Headless;

namespace DrakesForge.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // `DrakesAssetForge screenshot <folder>` renders each screen to PNG without opening a window.
        if (args.Length > 0 && args[0] == "screenshot")
            return Screenshots.Run(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "forge-screens"));

        // Headless commands (inspect, validate, render, new-pack, export-code, help): see Cli.Usage.
        if (Cli.Run(args) is { } code)
            return code;

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

/// <summary>Entry point Avalonia.Headless uses for the screenshot command.</summary>
public static class HeadlessEntry
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, FrameBufferFormat = Avalonia.Platform.PixelFormat.Bgra8888 });
}
