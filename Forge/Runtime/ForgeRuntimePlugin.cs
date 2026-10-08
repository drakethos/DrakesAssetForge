using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;

namespace DrakesForge.Runtime;

/// <summary>
/// The shared Forge Runtime: loads every data pack in BepInEx\plugins (and the Forge app's push folder)
/// that isn't embedded in its own mod. All the work is in <see cref="ForgeHost"/>.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
[BepInDependency("com.jotunn.jotunn")]
public sealed class ForgeRuntimePlugin : BaseUnityPlugin
{
    public const string Guid = "com.drakesworkshop.forgeruntime";
    public const string Name = "DrakesForgeRuntime";
    public const string Version = "0.4.0";

    private void Awake()
    {
        var pushFolders = Config.Bind(
            "Development",
            "PushFolders",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DrakesAssetForge", "Push"),
            "Folders (separated by ;) that Drakes Asset Forge pushes packs to. Packs here load like installed ones and hot-reload.");
        var hotReload = Config.Bind("Development", "HotReload", true, "Re-apply look, fields, snap points and recipes when a pack's files change.");

        var options = new ForgeOptions { HotReload = hotReload.Value, SkipEmbedded = true, IsShared = true };
        options.PackFolders.Add(Paths.PluginPath);
        foreach (var raw in pushFolders.Value.Split(';'))
        {
            var folder = raw.Trim();
            if (folder.Length == 0)
                continue;
            try
            {
                Directory.CreateDirectory(folder);
                options.DevFolders.Add(folder);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Push folder '{folder}' unusable: {ex.Message}");
            }
        }

        ForgeHost.Start(this, options, Logger);
    }
}
