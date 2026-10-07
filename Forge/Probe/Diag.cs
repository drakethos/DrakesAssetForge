using AssetsTools.NET;
using AssetsTools.NET.Extra;
using DrakesForge.Valheim;

/// <summary>Raw dump of a prefab's root components, for debugging the reader.</summary>
internal static class Diag
{
    public static void Run(VanillaCatalog catalog, string name)
    {
        var entry = catalog.ByName[name];
        var am = new AssetsManager { MonoTempGenerator = new MonoCecilTempGenerator(catalog.Install.ManagedDirectory) };
        var bun = am.LoadBundleFile(catalog.BundlePath(entry.BundleId), true);
        Console.WriteLine("files in bundle: " + string.Join(", ", bun.file.GetAllFileNames()));
        var file = am.LoadAssetsFileFromBundle(bun, 0, false);
        Console.WriteLine($"typetrees: {file.file.Metadata.TypeTreeEnabled}; externals: {string.Join(", ", file.file.Metadata.Externals.Select(e => e.PathName))}");

        long pathId = 0;
        foreach (var ab in file.file.GetAssetsOfType(AssetClassID.AssetBundle))
            foreach (var item in am.GetBaseField(file, ab)["m_Container.Array"].Children)
                if (item[0].AsString == entry.PathInBundle)
                    pathId = item[1]["asset.m_PathID"].AsLong;

        var go = am.GetBaseField(file, file.file.GetAssetInfo(pathId));
        foreach (var c in go["m_Component.Array"].Children)
        {
            var t = am.GetExtAsset(file, c["component"]);
            if (t.info == null || (AssetClassID)t.info.TypeId != AssetClassID.Transform)
                continue;
            foreach (var child in t.baseField["m_Children.Array"].Children)
            {
                var ct = am.GetExtAsset(file, child);
                var cg = am.GetExtAsset(ct.file, ct.baseField["m_GameObject"]).baseField;
                var p = ct.baseField["m_LocalPosition"];
                Console.WriteLine($"  child '{cg["m_Name"].AsString}' tag={cg["m_Tag"].AsInt} active={cg["m_IsActive"].AsBool} pos=({p["x"].AsFloat:0.##},{p["y"].AsFloat:0.##},{p["z"].AsFloat:0.##})");
            }
        }

        foreach (var c in go["m_Component.Array"].Children)
        {
            var ptr = c["component"];
            Console.WriteLine($"  comp fileId={ptr["m_FileID"].AsInt} pathId={ptr["m_PathID"].AsLong}");
            try
            {
                var ext = am.GetExtAsset(file, ptr);
                Console.WriteLine($"    type={(AssetClassID?)ext.info?.TypeId} base={(ext.baseField == null ? "null" : "ok")}");
                if (ext.info == null || (AssetClassID)ext.info.TypeId != AssetClassID.MonoBehaviour)
                    continue;
                var bf = ext.baseField ?? am.GetBaseField(ext.file, ext.info);
                var sp = bf["m_Script"];
                Console.WriteLine($"    script ptr fileId={sp["m_FileID"].AsInt} pathId={sp["m_PathID"].AsLong} fields={string.Join(",", bf.Children.Take(8).Select(f => f.FieldName))}");
                var s = am.GetExtAsset(ext.file, sp);
                Console.WriteLine($"    script info={(s.info == null ? "null" : "ok")} class={s.baseField?["m_ClassName"].AsString}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("    ERR " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
