using Avalonia;
using System;
using System.Threading.Tasks;
using DrakeAssetForge.Services;

namespace DrakeAssetForge;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (IsCliCommand(args))
        {
            // Ensure console output for WinExe hosts (double-click still opens GUI).
            AllocConsoleSafe();
            Environment.ExitCode = RunCli(args).GetAwaiter().GetResult();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static bool IsCliCommand(string[] args) =>
        args.Length > 0 &&
        (args[0].Equals("repack-keys", StringComparison.OrdinalIgnoreCase) ||
         args[0].Equals("repack-literal", StringComparison.OrdinalIgnoreCase) ||
         args[0].Equals("--help", StringComparison.OrdinalIgnoreCase) ||
         args[0].Equals("-h", StringComparison.OrdinalIgnoreCase) ||
         args[0].Equals("/?", StringComparison.OrdinalIgnoreCase));

    private static async Task<int> RunCli(string[] args)
    {
        if (args[0].Equals("repack-literal", StringComparison.OrdinalIgnoreCase))
            return BundleLiteralRepackCli.Run(args);

        if (args[0].Equals("repack-keys", StringComparison.OrdinalIgnoreCase))
            return await KeysFolderRepackCli.RunAsync(args);

        Console.WriteLine("Commands: repack-literal | repack-keys");
        Console.WriteLine();
        BundleLiteralRepackCli.Run(["--help"]);
        Console.WriteLine();
        KeysFolderRepackCli.RunAsync(["--help"]).GetAwaiter().GetResult();
        return 0;
    }

    private static void AllocConsoleSafe()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                AllocConsole();
        }
        catch
        {
            // already attached / not Windows
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
