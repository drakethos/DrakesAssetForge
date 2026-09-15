using System.Text.Json;
using System.Text.Json.Nodes;

namespace DrakeAssetForge.Services;

/// <summary>Reads/writes %LocalAppData%/DrakeAssetForge/settings.json without clobbering unrelated keys.</summary>
public static class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string SettingsPath => UnityArtCompiler.SettingsPath;

    public static string? TryLoadUnityExe() => TryGetString("unityExe");

    public static string? TryLoadLastProjectPath() => TryGetString("lastProjectPath");

    public static void SaveUnityExe(string unityExe) => SetString("unityExe", unityExe);

    public static void SaveLastProjectPath(string projectPath) => SetString("lastProjectPath", projectPath);

    public sealed class WindowStateSettings
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; } = 1400;
        public double Height { get; set; } = 860;
        public bool Maximized { get; set; }
    }

    public static WindowStateSettings? TryLoadWindowState()
    {
        var root = TryReadRoot();
        if (root == null || !root.TryGetPropertyValue("window", out var node) || node is not JsonObject win)
            return null;

        try
        {
            return new WindowStateSettings
            {
                X = win["x"]?.GetValue<double>() ?? 0,
                Y = win["y"]?.GetValue<double>() ?? 0,
                Width = win["width"]?.GetValue<double>() ?? 1400,
                Height = win["height"]?.GetValue<double>() ?? 860,
                Maximized = win["maximized"]?.GetValue<bool>() ?? false,
            };
        }
        catch
        {
            return null;
        }
    }

    public static void SaveWindowState(WindowStateSettings state)
    {
        var root = TryReadRoot() ?? new JsonObject();
        root["window"] = new JsonObject
        {
            ["x"] = state.X,
            ["y"] = state.Y,
            ["width"] = state.Width,
            ["height"] = state.Height,
            ["maximized"] = state.Maximized,
        };
        WriteRoot(root);
    }

    private static string? TryGetString(string key)
    {
        var root = TryReadRoot();
        if (root == null || !root.TryGetPropertyValue(key, out var node) || node is null)
            return null;
        return node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;
    }

    private static void SetString(string key, string value)
    {
        var root = TryReadRoot() ?? new JsonObject();
        root[key] = value;
        WriteRoot(root);
    }

    private static JsonObject? TryReadRoot()
    {
        if (!File.Exists(SettingsPath))
            return null;
        try
        {
            return JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteRoot(JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, root.ToJsonString(JsonOptions));
    }
}
