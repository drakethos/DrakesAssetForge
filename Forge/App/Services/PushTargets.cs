using DrakesForge.Format;
using DrakesForge.Valheim;

namespace DrakesForge.App.Services;

/// <summary>Somewhere "Push to game" can install a pack: a BepInEx plugins folder, or Forge's own dev folder.</summary>
public sealed class PushTarget
{
    public required string Manager { get; init; }
    public required string Profile { get; init; }
    /// <summary>BepInEx\plugins of the profile (or the Forge dev folder).</summary>
    public required string Folder { get; init; }
    /// <summary>Forge's own %LocalAppData% push folder: packs go in by id, no Author- prefix.</summary>
    public bool IsDevFolder { get; init; }

    /// <summary>Remembered label for a saved target (when it isn't re-detected).</summary>
    public string? LabelOverride { get; init; }
    public string Label => LabelOverride ?? (IsDevFolder ? "Forge dev folder" : $"{Manager} · {Profile}");
    public bool HasRuntime => IsDevFolder || ContainsFile(Folder, "DrakesForgeRuntime.dll");
    public bool HasJotunn => IsDevFolder || ContainsFile(Folder, "Jotunn.dll");

    /// <summary>Where a pack lands: Author-Id like a Thunderstore install, so it matches the published zip.</summary>
    public string PackFolder(ForgePack manifest)
    {
        if (IsDevFolder)
            return Path.Combine(Folder, manifest.Id);
        var author = Sanitize(manifest.Author.Length > 0 ? manifest.Author : "Unknown");
        return Path.Combine(Folder, $"{author}-{Sanitize(manifest.Id)}");
    }

    public static string Sanitize(string s) => new(s.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());

    private static bool ContainsFile(string folder, string file)
    {
        try
        {
            return Directory.Exists(folder) && Directory.EnumerateFiles(folder, file, new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true }).Any();
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>Finds mod-manager profiles and BepInEx installs on this machine.</summary>
public static class PushTargets
{
    public static List<PushTarget> Detect(string? valheimRoot)
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var targets = new List<PushTarget>();
        AddProfiles(targets, "Gale", Path.Combine(roaming, "com.kesomannen.gale", "valheim", "profiles"));
        AddProfiles(targets, "r2modman", Path.Combine(roaming, "r2modmanPlus-local", "Valheim", "profiles"));
        AddProfiles(targets, "Thunderstore", Path.Combine(roaming, "Thunderstore Mod Manager", "DataFolder", "Valheim", "profiles"));

        if (valheimRoot != null && Directory.Exists(Path.Combine(valheimRoot, "BepInEx", "plugins")))
            targets.Add(new PushTarget { Manager = "Valheim folder", Profile = "BepInEx", Folder = Path.Combine(valheimRoot, "BepInEx", "plugins") });

        targets.Add(Dev());
        return targets;
    }

    public static PushTarget Dev() => new() { Manager = "Forge", Profile = "dev", Folder = ForgePaths.PushDirectory, IsDevFolder = true };

    /// <summary>A folder the user picked: a profile root, its BepInEx folder, or the plugins folder itself.</summary>
    public static PushTarget Custom(string folder)
    {
        var plugins = folder;
        if (Directory.Exists(Path.Combine(folder, "BepInEx", "plugins")))
            plugins = Path.Combine(folder, "BepInEx", "plugins");
        else if (Directory.Exists(Path.Combine(folder, "plugins")))
            plugins = Path.Combine(folder, "plugins");
        return new PushTarget { Manager = "Custom", Profile = Path.GetFileName(folder.TrimEnd('\\', '/')), Folder = plugins };
    }

    private static void AddProfiles(List<PushTarget> targets, string manager, string profilesDir)
    {
        if (!Directory.Exists(profilesDir))
            return;
        foreach (var profile in Directory.GetDirectories(profilesDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var plugins = Path.Combine(profile, "BepInEx", "plugins");
            if (Directory.Exists(plugins))
                targets.Add(new PushTarget { Manager = manager, Profile = Path.GetFileName(profile), Folder = plugins });
        }
    }

    /// <summary>The Forge Runtime DLL shipped next to the app (copied in at build time), if present.</summary>
    public static string? BundledRuntime()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "runtime", "DrakesForgeRuntime.dll");
        return File.Exists(path) ? path : null;
    }

    /// <summary>Installs the runtime the way Thunderstore would: plugins\DrakeMods-DrakesForgeRuntime\.</summary>
    public static string InstallRuntime(PushTarget target)
    {
        var dll = BundledRuntime() ?? throw new FileNotFoundException("This build of the app doesn't include Forge Runtime.");
        var dir = Path.Combine(target.Folder, "DrakeMods-DrakesForgeRuntime");
        Directory.CreateDirectory(dir);
        File.Copy(dll, Path.Combine(dir, "DrakesForgeRuntime.dll"), true);
        return dir;
    }
}
