using System.Globalization;
using System.Numerics;
using DrakesForge.App.Services;
using DrakesForge.Format;
using DrakesForge.Format.Json;
using DrakesForge.Valheim;

namespace DrakesForge.App;

/// <summary>
/// Headless commands, so packs can be made and checked without the UI (scripts, remote sessions):
/// inspect, validate, render, new-pack, export-code. Each reads Valheim from the usual install/settings.
/// </summary>
internal static class Cli
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public const string Usage = """
        DrakesAssetForge <command>
          inspect <prefab> [--json]                     scripts, materials, snap points, size, lights of a Valheim prefab
          validate <pack folder>                        recipe problems, missing files, bases not in this Valheim
          render <pack folder> <recipe id> <out.png> [--yaw deg] [--pitch deg] [--size px] [--worn]
                                                        preview image (approximate shading; sprites, hidden mesh, borrowed mesh)
          new-pack <folder> <id> <name> <author>        an empty pack
          export-code <pack folder> <out folder> [--plain] [--libs] [--embed] [--look-only] [--into]
                                                        [--namespace N] [--folder sub/dir]
                                                        C# export (see Forge/README.md)
          screenshot <folder>                           walk the UI headlessly, PNG per screen
        """;

    public static int? Run(string[] args)
    {
        if (args.Length == 0)
            return null;
        try
        {
            return args[0] switch
            {
                "inspect" when args.Length > 1 => Inspect(args[1], args.Contains("--json")),
                "validate" when args.Length > 1 => Validate(args[1]),
                "render" when args.Length > 3 => Render(args[1], args[2], args[3], Opt(args, "--yaw", 40), Opt(args, "--pitch", 17), (int)Opt(args, "--size", 512), args.Contains("--worn")),
                "new-pack" when args.Length > 4 => NewPack(args[1], args[2], args[3], args[4]),
                "export-code" when args.Length > 2 => ExportCode(args),
                "help" or "--help" or "-h" => Print(Usage),
                _ => null
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or FormatException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            return 1;
        }
    }

    private static int Print(string text)
    {
        Console.WriteLine(text);
        return 0;
    }

    private static double Opt(string[] args, string name, double fallback)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], NumberStyles.Float, Inv, out var v) ? v : fallback;
    }

    private static string? Str(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static VanillaService OpenVanilla()
    {
        AppSettings.Load();
        var install = ValheimInstall.Find(AppSettings.ValheimPath);
        return VanillaService.TryOpen(install?.Root, out var error) ?? throw new InvalidOperationException(error);
    }

    // ---- inspect ----

    private static int Inspect(string prefab, bool json)
    {
        using var vanilla = OpenVanilla();
        if (!vanilla.Catalog.ByName.TryGetValue(prefab, out var entry))
            throw new InvalidOperationException($"'{prefab}' isn't in this Valheim install.");
        var preview = vanilla.LoadPreviewAsync(entry).GetAwaiter().GetResult();
        var info = preview.Info;
        var box = SnapGeometry.Bounds(preview.Model?.Parts ?? new List<ModelPart>());
        if (json)
        {
            var o = JsonValue.NewObject()
                .Set("name", info.Name)
                .Set("kind", entry.Kind.ToString().ToLowerInvariant())
                .Set("scripts", JsonArray(info.Scripts.Select(s => (JsonValue)s)))
                .Set("materials", JsonArray(info.MaterialSlots.DistinctBy(m => m.Name).Select(m => JsonValue.NewObject()
                    .Set("name", m.Name).Set("shader", m.Shader).Set("texture", m.MainTextureName))))
                .Set("snapPoints", JsonArray(info.SnapPoints.Select(p => Vec(p))))
                .Set("lights", info.Lights.Count)
                .Set("particles", info.Particles.Count);
            if (!box.IsEmpty)
                o.Set("bounds", JsonValue.NewObject().Set("min", Vec(box.Min)).Set("max", Vec(box.Max)).Set("size", Vec(box.Size)));
            Console.WriteLine(o.ToJson());
            return 0;
        }

        Console.WriteLine($"{info.Name} [{entry.Kind}{(entry.Category.Length > 0 ? "/" + entry.Category : "")}]");
        Console.WriteLine($"  scripts:   {string.Join(", ", info.Scripts)}");
        if (!box.IsEmpty)
            Console.WriteLine($"  size:      {box.Size.X:0.###} wide x {box.Size.Y:0.###} tall x {box.Size.Z:0.###} deep (m), min ({F(box.Min)}) max ({F(box.Max)})");
        foreach (var m in info.MaterialSlots.DistinctBy(m => m.Name))
            Console.WriteLine($"  material:  {m.Name}  shader={m.Shader}  texture={m.MainTextureName ?? "-"}");
        if (info.SnapPoints.Count > 0)
            Console.WriteLine($"  snap:      {string.Join("  ", info.SnapPoints.Select(p => "(" + F(p) + ")"))}");
        if (info.Lights.Count + info.Particles.Count > 0)
            Console.WriteLine($"  effects:   {info.Lights.Count} light(s), {info.Particles.Count} particle effect(s)");
        return 0;
    }

    private static JsonValue JsonArray(IEnumerable<JsonValue> items)
    {
        var a = JsonValue.NewArray();
        foreach (var i in items)
            a.Add(i);
        return a;
    }

    private static JsonValue Vec(Vector3 v) => JsonValue.NewArray().Add(Math.Round(v.X, 3)).Add(Math.Round(v.Y, 3)).Add(Math.Round(v.Z, 3));
    private static string F(Vector3 v) => $"{v.X.ToString("0.###", Inv)}, {v.Y.ToString("0.###", Inv)}, {v.Z.ToString("0.###", Inv)}";

    // ---- validate ----

    private static int Validate(string packFolder)
    {
        var pack = PackReader.Load(packFolder);
        var problems = pack.Problems.Select(p => $"{Path.GetFileName(p.File)}: {p.Message}").ToList();
        using var vanilla = OpenVanilla();
        foreach (var r in pack.Recipes)
        {
            if (!vanilla.Catalog.ByName.ContainsKey(r.Base))
                problems.Add($"{r.Id}: base '{r.Base}' isn't in this Valheim install.");
            var files = new List<string?> { r.Look.Icon };
            files.AddRange(r.Look.Materials.SelectMany(m => m.Textures.Values));
            files.AddRange(r.Look.Sprites.Select(s => s.File));
            foreach (var f in files.Where(f => !string.IsNullOrEmpty(f)).Distinct())
                if (pack.Resolve(f!) is not { } full || !File.Exists(full))
                    problems.Add($"{r.Id}: file '{f}' is missing from the pack.");
            if (r.Look.Mesh?.Prefab is { } mesh && !vanilla.Catalog.ByName.ContainsKey(mesh))
                problems.Add($"{r.Id}: mesh prefab '{mesh}' isn't in this Valheim install.");
            foreach (var m in r.Look.Materials.Where(m => m.FromPrefab != null && !vanilla.Catalog.ByName.ContainsKey(m.FromPrefab!)))
                problems.Add($"{r.Id}: material source '{m.FromPrefab}' isn't in this Valheim install.");
        }

        Console.WriteLine($"{pack.Manifest.Id}: {pack.Recipes.Count} recipe(s), {problems.Count} problem(s)");
        foreach (var p in problems)
            Console.WriteLine("  - " + p);
        return problems.Count == 0 ? 0 : 2;
    }

    // ---- render ----

    private static int Render(string packFolder, string id, string output, double yaw, double pitch, int size, bool worn)
    {
        var project = PackProject.Open(packFolder);
        var recipe = project.Recipes.FirstOrDefault(r => r.Id == id) ?? throw new InvalidOperationException($"No recipe '{id}' in {packFolder}.");
        using var vanilla = OpenVanilla();
        var basePreview = vanilla.TryLoadPreviewAsync(recipe.Base)?.GetAwaiter().GetResult() ?? throw new InvalidOperationException($"Base '{recipe.Base}' not found.");
        var model = (recipe.Look.Mesh?.Prefab is { } mesh ? vanilla.TryLoadPreviewAsync(mesh)?.GetAwaiter().GetResult()?.Model : null) ?? basePreview.Model
                    ?? throw new InvalidOperationException($"'{recipe.Base}' has no model.");

        var parts = new List<ModelPart>();
        if (worn && model.WornParts.Count > 0)
            parts.AddRange(model.WornParts);
        else
        {
            if (!recipe.Look.HideMesh)
                parts.AddRange(model.Parts);
            var images = new List<RgbaImage?>();
            foreach (var s in recipe.Look.Sprites)
            {
                images.Add(Images.LoadFile(project.FullPath(s.File)));
                parts.Add(SpriteGeometry.Part(s.File, s.Width, s.Height, new Vector3(s.Position.X, s.Position.Y, s.Position.Z),
                    new Vector3(s.Rotation.X, s.Rotation.Y, s.Rotation.Z), ViewModels.SpriteRow.SlotBase + images.Count - 1));
            }

            if (parts.Count == 0)
                throw new InvalidOperationException("Nothing to draw (mesh hidden and no sprites).");
            var camera = new OrbitCamera { Yaw = (float)(yaw * Math.PI / 180), Pitch = (float)(pitch * Math.PI / 180) };
            var image = SoftwareRenderer.Render(parts, slot => slot >= ViewModels.SpriteRow.SlotBase
                ? new SlotLook(images[slot - ViewModels.SpriteRow.SlotBase], Vector4.One)
                : SoftwareRenderer.DefaultLook(model, slot), camera, size, size);
            Images.SavePng(image, Path.GetFullPath(output));
            Console.WriteLine($"Rendered {id} ({parts.Count} part(s)) to {Path.GetFullPath(output)}");
            return 0;
        }

        var wornImage = SoftwareRenderer.Render(parts, slot => SoftwareRenderer.DefaultLook(model, slot),
            new OrbitCamera { Yaw = (float)(yaw * Math.PI / 180), Pitch = (float)(pitch * Math.PI / 180) }, size, size);
        Images.SavePng(wornImage, Path.GetFullPath(output));
        Console.WriteLine($"Rendered {id} (worn) to {Path.GetFullPath(output)}");
        return 0;
    }

    // ---- new-pack ----

    private static int NewPack(string folder, string id, string name, string author)
    {
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            throw new InvalidOperationException($"{folder} isn't empty.");
        var pack = PackProject.Create(Path.GetFullPath(folder), id, name, author);
        Console.WriteLine($"Created pack {pack.Manifest.Id} in {pack.Root}. Add recipes as items/<id>.json.");
        return 0;
    }

    // ---- export-code ----

    private static int ExportCode(string[] args)
    {
        AppSettings.Load();
        var pack = PackProject.Open(args[1]);
        var output = args[2];
        var install = ValheimInstall.Find(AppSettings.ValheimPath);
        var target = AppSettings.PushFolder is { } f ? PushTargets.Custom(f) : PushTargets.Detect(install?.Root).FirstOrDefault(t => t.HasRuntime && t.HasJotunn && !t.IsDevFolder);
        var into = args.Contains("--into");
        CodeExportResult result;
        if (args.Contains("--plain") || args.Contains("--libs") || args.Contains("--look-only"))
        {
            using var vanilla = OpenVanilla();
            var types = LiteTypeInfo.LoadAsync(pack.Recipes, vanilla.InspectAsync, vanilla.ComponentDefaultsAsync).GetAwaiter().GetResult();
            var options = new LiteOptions
            {
                UseLibs = args.Contains("--libs"),
                Embed = args.Contains("--embed"),
                LookOnly = args.Contains("--look-only"),
                Namespace = Str(args, "--namespace"),
                Subfolder = Str(args, "--folder")
            };
            result = LiteCodeWriter.Write(pack, output, into, types, target, install?.Root, options);
        }
        else
        {
            result = CodeProjectWriter.Write(pack, output, into, target, install?.Root);
        }

        Console.WriteLine($"Wrote {result.Written.Count} file(s), kept {result.Kept.Count} of yours in {result.Folder}");
        if (result.ProjectFile != null)
            Console.WriteLine(result.ProjectFile);
        return 0;
    }
}
