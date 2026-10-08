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
    /// <summary>Plugins folder "Push to game" installs into; null = Forge dev folder (only when <see cref="PushChosen"/>).</summary>
    public static string? PushFolder { get; set; }
    public static string? PushLabel { get; set; }
    /// <summary>True once the user picked a target in Settings. Until then Push uses the profile this app lives in.</summary>
    public static bool PushChosen { get; set; }
    /// <summary>Profiles the user pinned in Settings; always listed.</summary>
    public static List<string> PinnedPushFolders { get; } = new();
    /// <summary>How many profiles Settings lists before "Show more".</summary>
    public static int PushListSize { get; set; } = 5;

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
            // Files written before pushChosen existed only ever saved a folder when the user picked one.
            PushChosen = json["pushChosen"]?.AsBool() ?? PushFolder != null;
            PushListSize = (int)(json["pushListSize"]?.AsNumber() ?? PushListSize);
            PinnedPushFolders.Clear();
            foreach (var pin in json["pinnedPushFolders"]?.Items ?? Array.Empty<JsonValue>())
                if (pin.AsString() is { } folder)
                    PinnedPushFolders.Add(folder);
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
            json.Set("pushChosen", PushChosen);
            json.Set("pushListSize", PushListSize);
            var pins = JsonValue.NewArray();
            foreach (var pin in PinnedPushFolders)
                pins.Add(pin);
            json.Set("pinnedPushFolders", pins);
            File.WriteAllText(FilePath, json.ToJson());
        }
        catch (IOException)
        {
            // settings are a convenience
        }
    }
}
