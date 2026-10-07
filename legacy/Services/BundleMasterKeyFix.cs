using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace DrakeAssetForge.Services;

/// <summary>
/// Post-pass on a literal keys.bundle: MasterKey already has keyskull + shaft, but the fat pack
/// also carries a broken NurbsPath (.resS) mesh. Retarget any MeshFilter using that mesh to the
/// embedded <c>key backup</c> mesh, deactivate NurbsPath nodes, and add a container entry so
/// <c>LoadAsset("keyskull")</c> works at runtime.
/// </summary>
public static class BundleMasterKeyFix
{
    public sealed class Result
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "";
        public int RewiredMeshFilters { get; init; }
        public int DeactivatedNurbs { get; init; }
        public bool AddedKeyskullContainer { get; init; }
    }

    public static Result Fix(string bundlePath)
    {
        if (string.IsNullOrWhiteSpace(bundlePath) || !File.Exists(bundlePath))
            return Fail("Bundle not found: " + bundlePath);

        var am = new AssetsManager();
        try
        {
            var bun = am.LoadBundleFile(bundlePath, unpackIfPacked: true);
            var assetsIndex = 0;
            var names = bun.file.GetAllFileNames();
            for (var i = 0; i < names.Count; i++)
            {
                if (!names[i].EndsWith(".resS", StringComparison.OrdinalIgnoreCase) &&
                    !names[i].EndsWith(".resource", StringComparison.OrdinalIgnoreCase))
                {
                    assetsIndex = i;
                    break;
                }
            }

            var afile = am.LoadAssetsFileFromBundle(bun, assetsIndex, false);

            long FindNamed(AssetClassID type, string name)
            {
                foreach (var info in afile.file.GetAssetsOfType(type))
                {
                    try
                    {
                        var bf = am.GetBaseField(afile, info);
                        var n = bf["m_Name"].IsDummy ? "" : bf["m_Name"].AsString;
                        if (n.Equals(name, StringComparison.OrdinalIgnoreCase))
                            return info.PathId;
                    }
                    catch
                    {
                        // skip
                    }
                }

                return 0;
            }

            var nurbsMesh = FindNamed(AssetClassID.Mesh, "NurbsPath");
            var backupMesh = FindNamed(AssetClassID.Mesh, "key backup");
            var keyskullGo = FindNamed(AssetClassID.GameObject, "keyskull");
            var masterKeyGo = FindNamed(AssetClassID.GameObject, "MasterKey");

            if (backupMesh == 0)
                return Fail("No embedded 'key backup' mesh in bundle — cannot replace NurbsPath.");

            var rewired = 0;
            var deactivated = 0;

            // Retarget every MeshFilter still pointing at the streamed NurbsPath mesh.
            if (nurbsMesh != 0)
            {
                foreach (var info in afile.file.GetAssetsOfType(AssetClassID.MeshFilter))
                {
                    try
                    {
                        var bf = am.GetBaseField(afile, info);
                        var meshPid = bf["m_Mesh"]["m_PathID"].AsLong;
                        if (meshPid != nurbsMesh)
                            continue;

                        bf["m_Mesh"]["m_FileID"].AsInt = 0;
                        bf["m_Mesh"]["m_PathID"].AsLong = backupMesh;
                        info.SetNewData(bf);
                        rewired++;
                    }
                    catch
                    {
                        // skip broken templates
                    }
                }
            }

            // Deactivate NurbsPath GameObjects so they never draw even if somehow parented.
            foreach (var info in afile.file.GetAssetsOfType(AssetClassID.GameObject))
            {
                try
                {
                    var bf = am.GetBaseField(afile, info);
                    var n = bf["m_Name"].IsDummy ? "" : bf["m_Name"].AsString;
                    if (!n.Equals("NurbsPath", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!bf["m_IsActive"].IsDummy)
                        bf["m_IsActive"].AsBool = false;
                    info.SetNewData(bf);
                    deactivated++;
                }
                catch
                {
                    // skip
                }
            }

            var addedContainer = false;
            var abInfos = afile.file.GetAssetsOfType(AssetClassID.AssetBundle);
            if (abInfos.Count > 0 && keyskullGo != 0)
            {
                var abInfo = abInfos[0];
                var manifest = am.GetBaseField(afile, abInfo);
                var container = manifest["m_Container.Array"];
                var hasKeyskull = container.Children.Any(c =>
                {
                    var p = c[0].AsString ?? "";
                    return p.IndexOf("keyskull", StringComparison.OrdinalIgnoreCase) >= 0;
                });

                if (!hasKeyskull && container.Children.Count > 0)
                {
                    // Clone an existing entry (same struct) and point it at keyskull.
                    var template = container.Children[0].Clone();
                    template[0].AsString = "assets/drake/locksmit/keyskull.prefab";
                    template[1]["asset"]["m_FileID"].AsInt = 0;
                    template[1]["asset"]["m_PathID"].AsLong = keyskullGo;
                    var list = container.Children.Select(c => c.Clone()).ToList();
                    list.Add(template);
                    container.Children = list;
                    try
                    {
                        var arr = container.AsArray;
                        arr.size = list.Count;
                        container.AsArray = arr;
                    }
                    catch
                    {
                        // ignore
                    }

                    abInfo.SetNewData(manifest);
                    addedContainer = true;
                }
            }

            if (rewired == 0 && deactivated == 0 && !addedContainer && masterKeyGo != 0)
            {
                // Still rewrite the file so callers get a consistent pass; message notes no-op-ish.
            }

            bun.file.BlockAndDirInfo.DirectoryInfos[assetsIndex].SetNewData(afile.file);

            var temp = bundlePath + ".fix.tmp";
            if (File.Exists(temp))
                File.Delete(temp);

            using (var fs = File.Create(temp))
            {
                var writer = new AssetsFileWriter(fs);
                bun.file.Write(writer, 0);
            }

            var final = bundlePath + ".fix.out";
            if (File.Exists(final))
                File.Delete(final);

            var amPack = new AssetsManager();
            try
            {
                var packed = amPack.LoadBundleFile(temp, unpackIfPacked: false);
                using var fs = File.Create(final);
                var writer = new AssetsFileWriter(fs);
                packed.file.Pack(writer, AssetBundleCompressionType.LZ4);
            }
            finally
            {
                amPack.UnloadAll();
                TryDelete(temp);
            }

            File.Copy(final, bundlePath, overwrite: true);
            TryDelete(final);

            return new Result
            {
                Success = true,
                RewiredMeshFilters = rewired,
                DeactivatedNurbs = deactivated,
                AddedKeyskullContainer = addedContainer,
                Message =
                    $"MasterKey pack fix: rewiredMeshFilters={rewired}, " +
                    $"deactivatedNurbs={deactivated}, keyskullContainer={addedContainer}, " +
                    $"backupMesh={backupMesh}, masterKeyGo={masterKeyGo != 0}.",
            };
        }
        catch (Exception ex)
        {
            return Fail("MasterKey pack fix failed: " + ex.Message);
        }
        finally
        {
            am.UnloadAll();
        }
    }

    private static Result Fail(string message) => new() { Success = false, Message = message };

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }
}
