using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace DrakeAssetForge.Services;

/// <summary>
/// One-for-one AssetBundle subset: copies selected named assets and their PPtr dependency
/// closure from a fat UnityFS bundle into a new bundle. Bytes of kept assets are unchanged —
/// no mesh re-export, no material strip, no import guess.
/// </summary>
public static class BundleLiteralRepack
{
    public sealed class Result
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "";
        public string? OutputPath { get; init; }
        public int KeptAssets { get; init; }
        public int RemovedAssets { get; init; }
        public int ContainerEntries { get; init; }
        public IReadOnlyList<string> ContainerNames { get; init; } = Array.Empty<string>();
    }

    /// <param name="sourceBundle">Fat UnityFS (e.g. Imports/drake.bundle or Assets/drake).</param>
    /// <param name="outputBundle">Destination path (e.g. …/Assets/Items/keys/keys.bundle).</param>
    /// <param name="nameFilters">Substring filters matched against container paths (default: key, locksmit, keymaker).</param>
    /// <param name="fixMasterKey">Retarget NurbsPath → embedded key backup, strip NurbsPath, expose keyskull.</param>
    /// <remarks>Always strips MonoBehaviour/MonoScript — art packs are visual-only.</remarks>
    public static Result Repack(
        string sourceBundle,
        string outputBundle,
        IReadOnlyList<string>? nameFilters = null,
        bool compress = true,
        bool fixMasterKey = false)
    {
        if (string.IsNullOrWhiteSpace(sourceBundle) || !File.Exists(sourceBundle))
            return Fail("Source bundle not found: " + sourceBundle);
        if (string.IsNullOrWhiteSpace(outputBundle))
            return Fail("Output path is empty.");

        var filters = (nameFilters == null || nameFilters.Count == 0)
            ? new[] { "key", "locksmit", "keymaker" }
            : nameFilters.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).ToArray();
        if (filters.Length == 0)
            return Fail("No name filters.");

        var am = new AssetsManager();
        try
        {
            var bun = am.LoadBundleFile(sourceBundle, unpackIfPacked: true);
            if (bun.file.GetAllFileNames().Count == 0)
                return Fail("Source bundle has no files.");

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
            var abInfos = afile.file.GetAssetsOfType(AssetClassID.AssetBundle);
            if (abInfos.Count == 0)
                return Fail("Source has no AssetBundle manifest object.");

            var abInfo = abInfos[0];
            var manifest = am.GetBaseField(afile, abInfo);
            var container = manifest["m_Container.Array"];
            if (container.IsDummy)
                return Fail("AssetBundle manifest has no m_Container.");

            var keep = new HashSet<long> { abInfo.PathId };
            var seed = new HashSet<long>();
            var keepContainer = new List<AssetTypeValueField>();
            var containerNames = new List<string>();

            foreach (var item in container.Children)
            {
                var path = item[0].AsString ?? "";
                if (!Matches(path, filters))
                    continue;
                var pathId = item[1]["asset.m_PathID"].AsLong;
                keep.Add(pathId);
                seed.Add(pathId);
                keepContainer.Add(item);
                containerNames.Add(path.Replace('\\', '/'));
            }

            if (keepContainer.Count == 0)
            {
                return Fail(
                    "No container entries matched filters [" + string.Join(", ", filters) + "]. " +
                    "Source container has " + container.Children.Count + " entr(y/ies).");
            }

            // Walk deps from named assets only — never from the AssetBundle preload table
            // (that table references every asset in the fat pack).
            CollectDependencyClosure(am, afile, keep, seed);

            // MasterKey fix needs embedded 'key backup' + keyskull in the keep set BEFORE SetRemoved.
            // Strip NurbsPath before SetRemoved so we never resurrect a GO without its Transform.
            string? masterKeyFixNote = null;
            if (fixMasterKey)
            {
                SeedMasterKeyFixAssets(am, afile, keep);
                masterKeyFixNote = ApplyMasterKeyFixInPlace(am, afile, manifest, abInfo, keep);
            }

            // Art packs must not ship Valheim gameplay scripts (ItemDrop/ZNetView/…).
            // Unity cannot resolve those MonoScripts from a bundle → missing-script spam → "file 'none'".
            var scriptStripNote = StripGameplayScripts(am, afile, keep);

            var removed = 0;
            foreach (var info in afile.file.AssetInfos)
            {
                if (keep.Contains(info.PathId))
                    continue;
                info.SetRemoved();
                removed++;
            }

            container.Children = keepContainer.Select(c => c.Clone()).ToList();
            try
            {
                var arr = container.AsArray;
                arr.size = container.Children.Count;
                container.AsArray = arr;
            }
            catch
            {
                // Some Unity versions store array size only in Children.Count.
            }

            // Re-apply keyskull container entry after keepContainer trim (fix may have added it).
            if (fixMasterKey)
                EnsureKeyskullContainer(am, afile, manifest, keep);

            // Drop preload entries that pointed at pruned assets (avoids missing-ref noise at load).
            var preload = manifest["m_PreloadTable.Array"];
            if (!preload.IsDummy)
            {
                var keptPreload = new List<AssetTypeValueField>();
                foreach (var entry in preload.Children)
                {
                    var pid = entry["m_PathID"];
                    if (pid.IsDummy)
                        continue;
                    if (keep.Contains(pid.AsLong))
                        keptPreload.Add(entry.Clone());
                }

                preload.Children = keptPreload;
                try
                {
                    var arr = preload.AsArray;
                    arr.size = keptPreload.Count;
                    preload.AsArray = arr;
                }
                catch
                {
                    // ignore
                }
            }

            // Rebuild .resS / .resource first (patches stream offsets on kept assets), then write CAB.
            var streamNote = SlimExternalStreamFiles(am, bun, afile, keep, names);

            abInfo.SetNewData(manifest);
            bun.file.BlockAndDirInfo.DirectoryInfos[assetsIndex].SetNewData(afile.file);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputBundle))!);
            if (File.Exists(outputBundle))
                File.Delete(outputBundle);

            // Pack() ignores directory replacers on some AssetsTools builds — Write first, then
            // optionally re-Pack the written UnityFS for LZ4.
            var tempWrite = outputBundle + ".tmp";
            if (File.Exists(tempWrite))
                File.Delete(tempWrite);

            using (var fs = File.Create(tempWrite))
            {
                var writer = new AssetsFileWriter(fs);
                bun.file.Write(writer, 0);
            }

            if (compress)
            {
                var amPack = new AssetsManager();
                try
                {
                    var packedBun = amPack.LoadBundleFile(tempWrite, unpackIfPacked: false);
                    using var fs = File.Create(outputBundle);
                    var writer = new AssetsFileWriter(fs);
                    packedBun.file.Pack(writer, AssetBundleCompressionType.LZ4);
                }
                finally
                {
                    amPack.UnloadAll();
                    TryDelete(tempWrite);
                }
            }
            else
            {
                File.Move(tempWrite, outputBundle);
            }

            var size = new FileInfo(outputBundle).Length;
            var msg =
                $"Literal repack OK → {outputBundle} ({size:N0} bytes). " +
                $"container={keepContainer.Count} keptAssets={keep.Count} removed={removed}.";
            if (!string.IsNullOrWhiteSpace(masterKeyFixNote))
                msg += " " + masterKeyFixNote;
            if (!string.IsNullOrWhiteSpace(scriptStripNote))
                msg += " " + scriptStripNote;
            if (!string.IsNullOrWhiteSpace(streamNote))
                msg += " " + streamNote;

            // Refresh container names after optional keyskull inject.
            var finalNames = keepContainer.Select(c => (c[0].AsString ?? "").Replace('\\', '/')).ToList();
            try
            {
                foreach (var c in manifest["m_Container.Array"].Children)
                {
                    var p = (c[0].AsString ?? "").Replace('\\', '/');
                    if (!finalNames.Contains(p, StringComparer.OrdinalIgnoreCase))
                        finalNames.Add(p);
                }
            }
            catch
            {
                // ignore
            }

            return new Result
            {
                Success = true,
                OutputPath = outputBundle,
                KeptAssets = keep.Count,
                RemovedAssets = removed,
                ContainerEntries = finalNames.Count,
                ContainerNames = finalNames,
                Message = msg,
            };
        }
        catch (Exception ex)
        {
            return Fail("Literal repack failed: " + ex.Message);
        }
        finally
        {
            am.UnloadAll();
        }
    }

    private static Result Fail(string message) => new() { Success = false, Message = message };

    private static void SeedMasterKeyFixAssets(
        AssetsManager am,
        AssetsFileInstance afile,
        HashSet<long> keep)
    {
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

        var backup = FindNamed(AssetClassID.Mesh, "key backup");
        if (backup != 0)
            keep.Add(backup);

        var keyskull = FindNamed(AssetClassID.GameObject, "keyskull");
        if (keyskull != 0)
        {
            keep.Add(keyskull);
            CollectDependencyClosure(am, afile, keep, new HashSet<long> { keyskull });
        }
    }

    /// <summary>
    /// Remove every MonoBehaviour from kept GameObjects and drop MonoBehaviour / MonoScript
    /// assets from the keep set. Art bundles are visual-only — LockSmith (etc.) adds a clean
    /// ZNetView at runtime.
    /// </summary>
    private static string StripGameplayScripts(
        AssetsManager am,
        AssetsFileInstance afile,
        HashSet<long> keep)
    {
        static long ComponentPathId(AssetTypeValueField c)
        {
            try
            {
                if (!c["component"].IsDummy)
                    return c["component"]["m_PathID"].AsLong;
                if (!c["m_PathID"].IsDummy)
                    return c["m_PathID"].AsLong;
                if (c.Children.Count > 0 && !c.Children[0]["m_PathID"].IsDummy)
                    return c.Children[0]["m_PathID"].AsLong;
            }
            catch
            {
                // ignore
            }

            return 0;
        }

        var strippedBehaviours = 0;
        var patchedGos = 0;
        var drop = new HashSet<long>();

        foreach (var info in afile.file.GetAssetsOfType(AssetClassID.GameObject))
        {
            if (!keep.Contains(info.PathId))
                continue;

            try
            {
                var bf = am.GetBaseField(afile, info);
                var comps = bf["m_Component.Array"];
                if (comps.IsDummy)
                    comps = bf["m_Component"];
                if (comps.IsDummy)
                    continue;

                var kept = new List<AssetTypeValueField>();
                var changed = false;
                foreach (var c in comps.Children)
                {
                    var pid = ComponentPathId(c);
                    var ci = pid == 0 ? null : afile.file.GetAssetInfo(pid);
                    if (ci != null && ci.TypeId == (int)AssetClassID.MonoBehaviour)
                    {
                        drop.Add(pid);
                        strippedBehaviours++;
                        changed = true;
                        continue;
                    }

                    kept.Add(c.Clone());
                }

                if (!changed)
                    continue;

                comps.Children = kept;
                try
                {
                    var arr = comps.AsArray;
                    arr.size = kept.Count;
                    comps.AsArray = arr;
                }
                catch
                {
                    // ignore
                }

                info.SetNewData(bf);
                patchedGos++;
            }
            catch
            {
                // skip GO
            }
        }

        foreach (var info in afile.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
            drop.Add(info.PathId);

        foreach (var info in afile.file.GetAssetsOfType(AssetClassID.MonoScript))
            drop.Add(info.PathId);

        foreach (var id in drop)
            keep.Remove(id);

        if (strippedBehaviours == 0 && drop.Count == 0)
            return "scripts: none to strip.";

        return $"scripts: strippedBehaviours={strippedBehaviours}, patchedGos={patchedGos}, droppedAssets={drop.Count}.";
    }

    /// <summary>
    /// Retarget NurbsPath mesh → embedded key backup, strip the broken NurbsPath subtree
    /// (do not deactivate-in-place — that resurrects a GO after SetRemoved without its Transform),
    /// and expose keyskull in the container.
    /// </summary>
    private static string ApplyMasterKeyFixInPlace(
        AssetsManager am,
        AssetsFileInstance afile,
        AssetTypeValueField manifest,
        AssetFileInfo abInfo,
        HashSet<long> keep)
    {
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

        static long ComponentPathId(AssetTypeValueField c)
        {
            try
            {
                if (!c["component"].IsDummy)
                    return c["component"]["m_PathID"].AsLong;
                if (!c["m_PathID"].IsDummy)
                    return c["m_PathID"].AsLong;
                if (c.Children.Count > 0 && !c.Children[0]["m_PathID"].IsDummy)
                    return c.Children[0]["m_PathID"].AsLong;
            }
            catch
            {
                // ignore
            }

            return 0;
        }

        var nurbsMesh = FindNamed(AssetClassID.Mesh, "NurbsPath");
        var backupMesh = FindNamed(AssetClassID.Mesh, "key backup");
        var keyskullGo = FindNamed(AssetClassID.GameObject, "keyskull");
        var nurbsGo = FindNamed(AssetClassID.GameObject, "NurbsPath");

        if (backupMesh == 0)
            return "MasterKey fix skipped (no embedded 'key backup' mesh).";

        keep.Add(backupMesh);
        if (keyskullGo != 0)
            keep.Add(keyskullGo);

        var rewired = 0;
        if (nurbsMesh != 0)
        {
            foreach (var info in afile.file.GetAssetsOfType(AssetClassID.MeshFilter))
            {
                try
                {
                    var bf = am.GetBaseField(afile, info);
                    if (bf["m_Mesh"]["m_PathID"].AsLong != nurbsMesh)
                        continue;

                    // Only rewire filters that will remain (not on the NurbsPath GO we strip).
                    var owner = bf["m_GameObject"]["m_PathID"].AsLong;
                    if (nurbsGo != 0 && owner == nurbsGo)
                        continue;

                    bf["m_Mesh"]["m_FileID"].AsInt = 0;
                    bf["m_Mesh"]["m_PathID"].AsLong = backupMesh;
                    info.SetNewData(bf);
                    rewired++;
                }
                catch
                {
                    // skip
                }
            }
        }

        var stripped = 0;
        if (nurbsGo != 0)
        {
            try
            {
                var goBf = am.GetBaseField(afile, afile.file.GetAssetInfo(nurbsGo)!);
                var comps = goBf["m_Component.Array"];
                if (comps.IsDummy)
                    comps = goBf["m_Component"];

                long nurbsTransform = 0;
                var stripIds = new HashSet<long> { nurbsGo };
                if (!comps.IsDummy)
                {
                    foreach (var c in comps.Children)
                    {
                        var pid = ComponentPathId(c);
                        if (pid == 0)
                            continue;
                        stripIds.Add(pid);
                        var ci = afile.file.GetAssetInfo(pid);
                        if (ci != null && ci.TypeId == (int)AssetClassID.Transform)
                            nurbsTransform = pid;
                    }
                }

                if (nurbsTransform != 0)
                {
                    var tBf = am.GetBaseField(afile, afile.file.GetAssetInfo(nurbsTransform)!);
                    var fatherId = tBf["m_Father"]["m_PathID"].AsLong;
                    if (fatherId != 0)
                    {
                        var fatherInfo = afile.file.GetAssetInfo(fatherId);
                        if (fatherInfo != null)
                        {
                            var fatherBf = am.GetBaseField(afile, fatherInfo);
                            var children = fatherBf["m_Children.Array"];
                            if (children.IsDummy)
                                children = fatherBf["m_Children"];
                            if (!children.IsDummy)
                            {
                                var keptChildren = children.Children
                                    .Where(ch =>
                                    {
                                        var cpid = ch["m_PathID"].IsDummy ? ComponentPathId(ch) : ch["m_PathID"].AsLong;
                                        return cpid != nurbsTransform;
                                    })
                                    .Select(ch => ch.Clone())
                                    .ToList();
                                children.Children = keptChildren;
                                try
                                {
                                    var arr = children.AsArray;
                                    arr.size = keptChildren.Count;
                                    children.AsArray = arr;
                                }
                                catch
                                {
                                    // ignore
                                }

                                fatherInfo.SetNewData(fatherBf);
                            }
                        }
                    }
                }

                if (nurbsMesh != 0)
                    stripIds.Add(nurbsMesh);

                foreach (var id in stripIds)
                {
                    keep.Remove(id);
                    stripped++;
                }
            }
            catch
            {
                // fall through — SetRemoved will still drop anything not in keep
            }
        }
        else if (nurbsMesh != 0)
        {
            keep.Remove(nurbsMesh);
            stripped++;
        }

        var addedContainer = EnsureKeyskullContainer(am, afile, manifest, keep);

        return
            $"MasterKey fix: rewiredMeshFilters={rewired}, strippedNurbsAssets={stripped}, " +
            $"keyskullContainer={addedContainer}.";
    }

    private static bool EnsureKeyskullContainer(
        AssetsManager am,
        AssetsFileInstance afile,
        AssetTypeValueField manifest,
        HashSet<long> keep)
    {
        long keyskullGo = 0;
        foreach (var info in afile.file.GetAssetsOfType(AssetClassID.GameObject))
        {
            if (!keep.Contains(info.PathId))
                continue;
            try
            {
                var bf = am.GetBaseField(afile, info);
                var n = bf["m_Name"].IsDummy ? "" : bf["m_Name"].AsString;
                if (n.Equals("keyskull", StringComparison.OrdinalIgnoreCase))
                {
                    keyskullGo = info.PathId;
                    break;
                }
            }
            catch
            {
                // skip
            }
        }

        if (keyskullGo == 0)
            return false;

        var container = manifest["m_Container.Array"];
        if (container.IsDummy || container.Children.Count == 0)
            return false;

        var hasKeyskull = container.Children.Any(c =>
            (c[0].AsString ?? "").IndexOf("keyskull", StringComparison.OrdinalIgnoreCase) >= 0);
        if (hasKeyskull)
            return false;

        var template = container.Children[0].Clone();
        template[0].AsString = "assets/drake/locksmit/keyskull.prefab";
        template[1]["asset"]["m_FileID"].AsInt = 0;
        template[1]["asset"]["m_PathID"].AsLong = keyskullGo;
        var list = container.Children.ToList();
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

        return true;
    }

    /// <summary>
    /// Rebuilds .resS / .resource directory blobs so only byte ranges still referenced by
    /// kept Mesh / Texture2D / AudioClip assets remain. This is what actually shrinks a
    /// literal subset — pruning CAB assets alone leaves the fat stream files intact.
    /// </summary>
    private static string SlimExternalStreamFiles(
        AssetsManager am,
        BundleFileInstance bun,
        AssetsFileInstance afile,
        HashSet<long> keep,
        IList<string> names)
    {
        var notes = new List<string>();
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            var isResS = name.EndsWith(".resS", StringComparison.OrdinalIgnoreCase);
            var isResource = name.EndsWith(".resource", StringComparison.OrdinalIgnoreCase);
            if (!isResS && !isResource)
                continue;

            byte[] original;
            try
            {
                original = BundleHelper.LoadAssetDataFromBundle(bun.file, i);
            }
            catch (Exception ex)
            {
                notes.Add($"{Path.GetExtension(name)} readFail={ex.Message}");
                continue;
            }

            if (original == null || original.Length == 0)
            {
                bun.file.BlockAndDirInfo.DirectoryInfos[i].SetNewData(Array.Empty<byte>());
                notes.Add($"{Path.GetExtension(name)}→0");
                continue;
            }

            var ranges = CollectStreamRanges(am, afile, keep, name, isResS);
            if (ranges.Count == 0)
            {
                // Remove unused external file entirely — a 0-byte entry can load as Unity "file 'none'".
                bun.file.BlockAndDirInfo.DirectoryInfos[i].SetRemoved();
                notes.Add($"{Path.GetExtension(name)} {original.Length:N0}→removed (unused)");
                continue;
            }

            // Stable order by original offset so we can rewrite assets with new offsets.
            ranges.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            using var ms = new MemoryStream();
            foreach (var r in ranges)
            {
                if (r.Offset < 0 || r.Size <= 0 || r.Offset + r.Size > original.Length)
                    continue;
                var newOffset = (uint)ms.Position;
                ms.Write(original, (int)r.Offset, (int)r.Size);
                foreach (var patch in r.Patches)
                    patch(newOffset);
            }

            var slim = ms.ToArray();
            bun.file.BlockAndDirInfo.DirectoryInfos[i].SetNewData(slim);
            notes.Add($"{Path.GetExtension(name)} {original.Length:N0}→{slim.Length:N0} (slices={ranges.Count})");
        }

        // Persist any stream offset patches written into asset fields.
        // CollectStreamRanges mutates fields via patch callbacks; ensure infos are saved.
        // (Patches call info.SetNewData already.)

        return notes.Count == 0 ? "" : "streams: " + string.Join(", ", notes);
    }

    private sealed class StreamRange
    {
        public long Offset;
        public long Size;
        public List<Action<uint>> Patches = new();
    }

    private static List<StreamRange> CollectStreamRanges(
        AssetsManager am,
        AssetsFileInstance afile,
        HashSet<long> keep,
        string streamFileName,
        bool isResS)
    {
        // Deduplicate identical (offset,size) so shared slices are copied once.
        var map = new Dictionary<(long Offset, long Size), StreamRange>();

        void Consider(long offset, long size, AssetFileInfo info, AssetTypeValueField root, Action<uint> applyOffset)
        {
            if (size <= 0)
                return;
            var key = (offset, size);
            if (!map.TryGetValue(key, out var range))
            {
                range = new StreamRange { Offset = offset, Size = size };
                map[key] = range;
            }

            range.Patches.Add(newOffset =>
            {
                applyOffset(newOffset);
                info.SetNewData(root);
            });
        }

        var leaf = Path.GetFileName(streamFileName);

        if (isResS)
        {
            foreach (var type in new[] { AssetClassID.Mesh, AssetClassID.Texture2D })
            {
                foreach (var info in afile.file.GetAssetsOfType(type))
                {
                    if (!keep.Contains(info.PathId))
                        continue;
                    try
                    {
                        var bf = am.GetBaseField(afile, info);
                        var stream = bf["m_StreamData"];
                        if (stream.IsDummy)
                            continue;
                        var path = stream["path"].IsDummy ? "" : stream["path"].AsString ?? "";
                        if (path.IndexOf(leaf, StringComparison.OrdinalIgnoreCase) < 0 &&
                            path.IndexOf(".resS", StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        var offset = stream["offset"].AsUInt;
                        var size = stream["size"].AsUInt;
                        Consider(offset, size, info, bf, newOffset =>
                        {
                            stream["offset"].AsUInt = newOffset;
                        });
                    }
                    catch
                    {
                        // skip
                    }
                }
            }
        }
        else
        {
            foreach (var info in afile.file.GetAssetsOfType(AssetClassID.AudioClip))
            {
                if (!keep.Contains(info.PathId))
                    continue;
                try
                {
                    var bf = am.GetBaseField(afile, info);
                    var res = bf["m_Resource"];
                    if (res.IsDummy)
                        continue;
                    var src = res["m_Source"].IsDummy ? "" : res["m_Source"].AsString ?? "";
                    if (src.IndexOf(leaf, StringComparison.OrdinalIgnoreCase) < 0 &&
                        src.IndexOf(".resource", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    var offsetField = res["m_Offset"];
                    var sizeField = res["m_Size"];
                    if (offsetField.IsDummy || sizeField.IsDummy)
                        continue;
                    var offset = (long)offsetField.AsULong;
                    var size = (long)sizeField.AsUInt;
                    Consider(offset, size, info, bf, newOffset =>
                    {
                        offsetField.AsULong = newOffset;
                    });
                }
                catch
                {
                    // skip
                }
            }
        }

        return map.Values.ToList();
    }

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

    private static bool Matches(string name, IReadOnlyList<string> filters)
    {
        var n = name.Replace('\\', '/');
        foreach (var f in filters)
        {
            if (n.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static void CollectDependencyClosure(
        AssetsManager am,
        AssetsFileInstance afile,
        HashSet<long> keep,
        HashSet<long> seeds)
    {
        var queue = new Queue<long>(seeds);
        var visited = new HashSet<long>();

        while (queue.Count > 0)
        {
            var pathId = queue.Dequeue();
            if (!visited.Add(pathId))
                continue;

            keep.Add(pathId);
            var info = afile.file.GetAssetInfo(pathId);
            if (info == null)
                continue;

            try
            {
                var field = am.GetBaseField(afile, info);
                CollectPPtrs(field, keep, queue, visited);
            }
            catch
            {
                // Some types fail template read; asset bytes still kept if already seeded.
            }
        }
    }

    private static void CollectPPtrs(
        AssetTypeValueField field,
        HashSet<long> keep,
        Queue<long> queue,
        HashSet<long> visited)
    {
        if (field == null || field.IsDummy)
            return;

        // Only real PPtr_* fields — scanning every m_PathID child false-positives across the file.
        var typeName = field.TypeName ?? "";
        if (typeName.StartsWith("PPtr<", StringComparison.OrdinalIgnoreCase) ||
            typeName.Equals("PPtr", StringComparison.OrdinalIgnoreCase))
        {
            var fileId = field["m_FileID"];
            var pathIdField = field["m_PathID"];
            if (!fileId.IsDummy && !pathIdField.IsDummy && fileId.AsInt == 0)
            {
                var pathId = pathIdField.AsLong;
                if (pathId != 0 && keep.Add(pathId) && !visited.Contains(pathId))
                    queue.Enqueue(pathId);
            }
        }

        foreach (var child in field.Children)
            CollectPPtrs(child, keep, queue, visited);
    }
}
