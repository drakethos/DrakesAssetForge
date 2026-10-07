using Avalonia.Headless;
using Avalonia.Threading;
using DrakesForge.App.Services;
using DrakesForge.App.ViewModels;
using DrakesForge.App.Views;

namespace DrakesForge.App;

/// <summary>
/// Headless walk through the main flow on a scratch pack, saving a PNG per screen.
/// Used to check layouts without a desktop; never touches the user's own packs.
/// </summary>
internal static class Screenshots
{
    public static int Run(string outDir)
    {
        Directory.CreateDirectory(outDir);
        // Scratch run: read the user's settings for context, never write them.
        AppSettings.Load();
        AppSettings.ReadOnly = true;
        var scratch = Path.Combine(Path.GetTempPath(), "forge-screens-pack");
        if (Directory.Exists(scratch))
            Directory.Delete(scratch, true);

        using var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessEntry));
        return session.Dispatch(async () =>
        {
            var vm = new MainViewModel { LivePush = false };
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();

            vm.OpenValheim(null);
            vm.Pack = PackProject.Create(scratch, "ScreenshotPack", "Bronze Builds", "DrakeMods");
            vm.GoTo(Step.Browse);

            // Browse: shortlist three things (search falls back to everything), then preview the iron gate.
            vm.Browse.Search = "iron_grate";
            await Settle(window, 6000);
            foreach (var name in new[] { "iron_grate", "woodiron_pole", "SwordBronze" })
            {
                vm.Browse.Search = name;
                await Settle(window, 300);
                var tile = vm.Browse.Results.FirstOrDefault(t => t.Name == name);
                if (tile != null && !tile.InList)
                    vm.Browse.Toggle(tile);
            }

            vm.Browse.Search = "grate";
            await Settle(window, 300);
            vm.Browse.Selected = vm.Browse.Results.FirstOrDefault(t => t.Name == "iron_grate");
            await Settle(window, 5000);
            Save(window, outDir, "1-browse");

            vm.Browse.SetModeCommand.Execute("Materials");
            await Settle(window, 400);
            Save(window, outDir, "1b-browse-materials");
            vm.Browse.SetModeCommand.Execute("Model");

            // Import: pole becomes a source, sword a reskin.
            vm.GoTo(Step.Import);
            await Settle(window, 2000);
            var pole = vm.Import.Rows.FirstOrDefault(r => r.Entry.Name == "woodiron_pole");
            if (pole != null)
                pole.Mode = ImportMode.Source;
            var sword = vm.Import.Rows.FirstOrDefault(r => r.Entry.Name == "SwordBronze");
            if (sword != null)
                sword.Mode = ImportMode.Reskin;
            await Settle(window, 300);
            Save(window, outDir, "2-import");

            // Workspace: tint the gate's bars bronze.
            vm.Import.ImportAllCommand.Execute(null);
            await Settle(window, 2500);
            var editor = vm.Workspace.Editor;
            var bars = editor?.Materials.FirstOrDefault(m => m.Target == "metalwall");
            if (editor != null && bars != null)
            {
                editor.SelectedMaterial = bars;
                bars.SetTintCommand.Execute("#C48A48");
                bars.GlossOn = true;
                bars.Gloss = 0.65;
            }

            await Settle(window, 1200);
            Save(window, outDir, "3-workspace-look");

            if (editor != null)
            {
                editor.Tab = "Snap";
                editor.SnapMode = "add";
                editor.AddSnapPointCommand.Execute(null);
            }

            await Settle(window, 800);
            Save(window, outDir, "3b-workspace-snap");

            // Components: change WearNTear health and material type, then search.
            if (editor != null)
            {
                editor.Tab = "Components";
                await Settle(window, 600);
                Save(window, outDir, "3d-components");
                editor.Components.Search = "health";
                await Settle(window, 400);
                var health = editor.Components.Results.FirstOrDefault(e => e.Node.Path == "m_health" && e.Component == "WearNTear");
                if (health != null)
                    health.Text = "3500";
                await Settle(window, 400);
                Save(window, outDir, "3e-components-search");
                editor.Components.Search = "";
            }

            // Material picker over the workspace.
            if (editor != null && bars != null)
            {
                editor.Tab = "Look";
                editor.OpenMaterialPicker(bars);
                await Settle(window, 300);
                var picker = vm.Workspace.Picker;
                if (picker != null)
                {
                    picker.Scope = "All";
                    picker.Search = "wood_wall";
                    await Settle(window, 600);
                    picker.Selected = picker.Results.FirstOrDefault();
                    await Settle(window, 3000);
                    Save(window, outDir, "3f-material-picker");
                    picker.CancelCommand.Execute(null);
                }
            }

            if (editor != null)
                editor.Tab = "Recipe";
            await Settle(window, 400);
            Save(window, outDir, "3c-workspace-recipe");

            editor?.SaveNow();

            // Armour: worn view, body material textures, armour-look picker.
            var dress = new Format.ItemRecipe { Id = "screens_dress", Base = "ArmorDress7", Kind = Format.RecipeKind.Item, Name = "Wedding Dress" };
            vm.Pack!.SaveRecipe(dress);
            vm.Workspace.Reload(dress);
            await Settle(window, 4000);
            if (vm.Workspace.Editor is { } dressEditor && dressEditor.Materials.FirstOrDefault(m => m.IsArmor) is { } body)
            {
                Save(window, outDir, "5-dress-worn");
                dressEditor.SelectedMaterial = body;
                await Settle(window, 3000);
                Save(window, outDir, "5b-dress-body");
                dressEditor.OpenMaterialPicker(body);
                await Settle(window, 300);
                if (vm.Workspace.Picker is { } armorPicker)
                {
                    armorPicker.Search = "ArmorIronChest";
                    await Settle(window, 500);
                    armorPicker.Selected = armorPicker.Results.FirstOrDefault();
                    await Settle(window, 3500);
                    Save(window, outDir, "5c-armor-picker");
                    armorPicker.CancelCommand.Execute(null);
                }
            }

            // Ward clone: blue fire and runes, no warding, plus a Rigidbody.
            var ward = new Format.ItemRecipe { Id = "screens_ward", Base = "guard_stone", Kind = Format.RecipeKind.Piece, Name = "Blue Ward" };
            vm.Pack!.SaveRecipe(ward);
            vm.Workspace.Reload(ward);
            await Settle(window, 4000);
            if (vm.Workspace.Editor is { } wardEditor)
            {
                wardEditor.LightColorOn = true;
                wardEditor.LightColor = "#66AAFF";
                wardEditor.LightIntensity = 1.5;
                wardEditor.FlameTintOn = true;
                wardEditor.FlameTint = "#4C8CFF";
                if (wardEditor.Materials.FirstOrDefault(m => m.HasEmission && !m.IsAll) is { } runes)
                {
                    wardEditor.SelectedMaterial = runes;
                    runes.EmissionOn = true;
                    runes.EmissionColor = "#66AAFF";
                    runes.EmissionStrength = 2.5;
                }

                await Settle(window, 1500);
                Save(window, outDir, "7-ward-fire");

                wardEditor.Tab = "Components";
                await Settle(window, 1500);
                if (wardEditor.Components.Components.OfType<ComponentCard>().FirstOrDefault(c => c.Label == "PrivateArea") is { } privateArea)
                {
                    foreach (var card in wardEditor.Components.Components)
                        card.IsExpanded = false;
                    privateArea.AskRemoveCommand.Execute(null);
                    await Settle(window, 400);
                    Save(window, outDir, "7b-ward-remove-confirm");
                    privateArea.ConfirmRemoveCommand.Execute(null);
                }

                await wardEditor.Components.OpenAddCommand.ExecuteAsync(null);
                await Settle(window, 1500);
                Save(window, outDir, "7c-ward-add");
                wardEditor.Components.AddSearch = "Rigidbody";
                await Settle(window, 200);
                if (wardEditor.Components.AddResults.FirstOrDefault() is { } rigid)
                    await rigid.AddCommand.ExecuteAsync(null);
                await Settle(window, 1000);
                foreach (var card in wardEditor.Components.Components.OfType<ComponentCard>().Where(c => c.Label != "Rigidbody"))
                    card.IsExpanded = false;
                await Settle(window, 600);
                Save(window, outDir, "7d-ward-components");
                wardEditor.SaveNow();
            }

            vm.GoTo(Step.Publish);
            await Settle(window, 600);
            vm.Publish.MakeIconCommand.Execute(null);
            vm.Publish.GenerateReadmeCommand.Execute(null);
            await Settle(window, 500);
            Save(window, outDir, "4-publish");

            vm.Publish.SetOutputCommand.Execute("code");
            vm.Publish.CodeFolder = Path.Combine(Path.GetTempPath(), "forge-screens-code");
            vm.Publish.GenerateCodeCommand.Execute(null);
            await Settle(window, 500);
            Save(window, outDir, "4b-publish-code");

            vm.Publish.SetOutputCommand.Execute("plain");
            vm.Publish.CodeFolder = Path.Combine(Path.GetTempPath(), "forge-screens-plain");
            await vm.Publish.GenerateCodeCommand.ExecuteAsync(null);
            await Settle(window, 500);
            Save(window, outDir, "4c-publish-plain");

            vm.OpenSettingsCommand.Execute(null);
            await Settle(window, 500);
            Save(window, outDir, "6-settings");
            vm.Settings = null;

            Console.WriteLine($"Screens in {outDir}; scratch pack in {scratch}");
            foreach (var file in Directory.GetFiles(Path.Combine(scratch, "items")))
                Console.WriteLine($"--- {Path.GetFileName(file)}\n{File.ReadAllText(file)}");
            vm.Dispose();
            return 0;
        }, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>Lets background loads finish and the UI catch up.</summary>
    private static async Task Settle(MainWindow window, int ms)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(50);
        }
    }

    private static void Save(MainWindow window, string dir, string name)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        if (window.CaptureRenderedFrame() is { } frame)
            Images.SavePng(Images.FromBitmap(frame), Path.Combine(dir, name + ".png"));
    }
}
