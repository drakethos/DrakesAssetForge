using System.Diagnostics;
using DrakesForge.Valheim;

var install = ValheimInstall.Find(Environment.CurrentDirectory) ?? throw new InvalidOperationException("Valheim not found.");
var sw = Stopwatch.StartNew();
var catalog = VanillaCatalog.Load(install);
Console.WriteLine($"Catalog: {catalog.Entries.Count} prefabs ({catalog.Entries.Count(e => e.Kind == VanillaKind.Item)} items, {catalog.Entries.Count(e => e.Kind == VanillaKind.Piece)} pieces) in {sw.ElapsedMilliseconds} ms");
foreach (var g in catalog.Entries.GroupBy(e => (e.Kind, e.Category)).OrderBy(g => g.Key.Kind).ThenByDescending(g => g.Count()).Take(14))
    Console.WriteLine($"  {g.Key.Kind,-5} {(g.Key.Category.Length == 0 ? "(top)" : g.Key.Category),-16} {g.Count()}");

if (args.Length > 1 && args[0] == "diag")
{
    Diag.Run(catalog, args[1]);
    return;
}

using var session = new AssetSession(catalog);
foreach (var name in args.Length > 0 ? args : new[] { "iron_grate", "SwordBronze" })
{
    if (!catalog.ByName.TryGetValue(name, out var entry))
    {
        Console.WriteLine($"\n{name}: not in catalog");
        continue;
    }

    sw.Restart();
    var info = PrefabReader.Inspect(session, entry);
    if (args.Contains("--fields"))
    {
        void Dump(IEnumerable<FieldNode> nodes, string indent)
        {
            foreach (var n in nodes)
            {
                var v = n.Value switch { float[] f => "[" + string.Join(", ", f.Select(x => x.ToString("0.##"))) + "]", null => "", var o => o.ToString() };
                var extra = n.Kind == FieldKind.Enum ? $" ({string.Join("/", n.EnumOptions!.Take(6).Select(o => o.Name))}{(n.IsFlags ? ", flags" : "")})" : n.Kind == FieldKind.List ? $" x{n.Count}" : "";
                Console.WriteLine($"{indent}{n.Label} [{n.Kind}:{n.TypeName}] = {v}{extra}");
                if (n.Kind == FieldKind.Group && indent.Length < 8)
                    Dump(n.Children, indent + "  ");
            }
        }

        foreach (var c in info.Components)
        {
            Console.WriteLine($"  == {c.Name} ({c.Fields.Count} fields)");
            Dump(c.Fields, "    ");
        }
    }

    if (args.Contains("--icon"))
    {
        sw.Restart();
        var icon = PreviewLoader.LoadIcon(session, entry);
        Console.WriteLine($"  fast icon: {(icon == null ? "NONE" : $"{icon.Width}x{icon.Height}")} in {sw.ElapsedMilliseconds} ms");
    }

    if (args.Contains("--render"))
        Render.Save(session, entry);
    Console.WriteLine($"\n{info.Name} [{entry.Kind}/{entry.Category}] bundle {entry.BundleId} (+{catalog.DependenciesOf(entry.BundleId).Count} deps) read in {sw.ElapsedMilliseconds} ms");
    Console.WriteLine($"  scripts: {string.Join(", ", info.Scripts)}");
    Console.WriteLine($"  icon: {(info.HasIcon ? "yes" : "no")}");
    if (info.ArmorMaterial is { } am)
    {
        Console.WriteLine($"  ARMOR MATERIAL {am.Name} shader={am.Shader} textures: {string.Join(", ", am.Textures.Select(t => $"{t.Key}={t.Value}"))}");
        Console.WriteLine($"    floats: {string.Join(" ", am.Floats.Select(f => $"{f.Key}={f.Value:0.##}"))}");
    }
    if (info.PieceCost is { } cost)
        Console.WriteLine($"  cost: {cost.Category} @ {cost.Station ?? "no station"}: {string.Join(", ", cost.Resources.Select(r => $"{r.Item} x{r.Amount}{(r.Recover ? "" : " (no refund)")}"))}");
    if (info.SnapPoints.Count > 0)
        Console.WriteLine($"  snap points: {string.Join(" ", info.SnapPoints.Select(p => $"({p.X:0.##},{p.Y:0.##},{p.Z:0.##})"))}");
    var slot = 0;
    foreach (var r in info.Renderers)
    {
        Console.WriteLine($"  {(r.Visible ? "show" : "hide")} {(r.Skinned ? "skinned " : "")}{(r.Path.Length == 0 ? "(root)" : r.Path)}  mesh={r.MeshName}");
        foreach (var m in r.Materials)
        {
            Console.WriteLine($"     slot {slot++}: {m.Name}  shader={m.Shader}  tex={m.MainTextureName ?? "-"}  color={string.Join(",", m.Color.Select(c => c.ToString("0.##")))}");
            if (args.Contains("--props"))
                Console.WriteLine($"        textures: {string.Join(" ", m.TextureSlots)}\n        floats: {string.Join(" ", m.Floats.Select(f => $"{f.Key}={f.Value:0.##}"))}");
        }
    }
}
