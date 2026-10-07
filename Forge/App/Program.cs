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

        // `DrakesAssetForge export-code <pack folder> <output folder>`: the C# mod export, without the UI.
        if (args.Length > 2 && args[0] == "export-code")
            return ExportCode(args[1], args[2]);

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static int ExportCode(string packFolder, string output)
    {
        Services.AppSettings.Load();
        var pack = Services.PackProject.Open(packFolder);
        var install = Valheim.ValheimInstall.Find();
        var target = Services.AppSettings.PushFolder is { } f ? Services.PushTargets.Custom(f) : Services.PushTargets.Detect(install?.Root).FirstOrDefault(t => t.HasRuntime && t.HasJotunn && !t.IsDevFolder);
        var result = Services.CodeProjectWriter.Write(pack, output, false, target, install?.Root);
        Console.WriteLine($"Wrote {result.Written.Count} file(s), kept {result.Kept.Count} of yours in {result.Folder}");
        Console.WriteLine(result.ProjectFile);
        return 0;
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
