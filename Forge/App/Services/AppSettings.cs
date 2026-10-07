using DrakesForge.Format.Json;
using DrakesForge.Valheim;

namespace DrakesForge.App.Services;

/// <summary>Remembers the last pack and a manually chosen Valheim folder.</summary>
public static class AppSettings
{
    private static string FilePath => Path.Combine(ForgePaths.DataDirectory, "forge-app.json");

    public static string? LastPack { get; set; }
    public static string? ValheimPath { get; set; }
    /// <summary>Where new packs are created (default Documents\DrakesAssetForge\Packs).</summary>
    public static string? PacksFolder { get; set; }
    /// <summary>Plugins folder "Push to game" installs into; null = Forge dev folder.</summary>
    public static string? PushFolder { get; set; }
    public static string? PushLabel { get; set; }

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return;
            var json = JsonValue.Parse(File.ReadAllText(FilePath));
            LastPack = json["lastPack"]?.AsString();
            ValheimPath = json["valheimPath"]?.AsString();
            PacksFolder = json["packsFolder"]?.AsString();
            PushFolder = json["pushFolder"]?.AsString();
            PushLabel = json["pushLabel"]?.AsString();
        }
        catch (Exception ex) when (ex is IOException or FormatException)
        {
            // start fresh
        }
    }

    /// <summary>Set by the headless screenshot run so it can never overwrite the user's real settings.</summary>
    public static bool ReadOnly { get; set; }

    public static void Save()
    {
        if (ReadOnly)
            return;
        try
        {
            Directory.CreateDirectory(ForgePaths.DataDirectory);
            var json = JsonValue.NewObject();
            if (LastPack != null)
                json.Set("lastPack", LastPack);
            if (ValheimPath != null)
                json.Set("valheimPath", ValheimPath);
            if (PacksFolder != null)
                json.Set("packsFolder", PacksFolder);
            if (PushFolder != null)
                json.Set("pushFolder", PushFolder);
            if (PushLabel != null)
                json.Set("pushLabel", PushLabel);
            File.WriteAllText(FilePath, json.ToJson());
        }
        catch (IOException)
        {
            // settings are a convenience
        }
    }
}
