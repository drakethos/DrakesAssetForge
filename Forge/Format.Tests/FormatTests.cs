using DrakesForge.Format;
using DrakesForge.Format.Json;
using Xunit;

namespace DrakesForge.Format.Tests;

public class JsonValueTests
{
    [Fact]
    public void Parses_nested_values_and_comments()
    {
        var v = JsonValue.Parse("""
            {
              // a note
              "a": [1, 2.5, -3e2],
              "b": { "c": "x\"yA", "d": true, "e": null }
            }
            """);

        Assert.Equal(2.5, v["a"]!.Items[1].AsNumber());
        Assert.Equal(-300, v["a"]!.Items[2].AsNumber());
        Assert.Equal("x\"yA", v["b"]!["c"]!.AsString());
        Assert.True(v["b"]!["d"]!.AsBool());
        Assert.True(v["b"]!["e"]!.IsNull);
    }

    [Fact]
    public void Round_trips_and_keeps_key_order()
    {
        var obj = JsonValue.NewObject().Set("z", 1).Set("a", "two").Set("m", JsonValue.NewArray().Add(1.5).Add(2));
        var text = obj.ToJson();
        var back = JsonValue.Parse(text);

        Assert.Equal(new[] { "z", "a", "m" }, back.Properties.Select(p => p.Key));
        Assert.Contains("[1.5, 2]", text);
    }

    [Fact]
    public void Reports_line_and_column_on_error()
    {
        var ex = Assert.Throws<FormatException>(() => JsonValue.Parse("{\n  \"a\": ,\n}"));
        Assert.Contains("line 2", ex.Message);
    }
}

public class RecipeTests
{
    private static string SamplePack => Path.Combine(AppContext.BaseDirectory, "samples", "BronzeBuilds");

    [Fact]
    public void Sample_pack_loads_without_problems()
    {
        var pack = PackReader.Load(SamplePack);

        Assert.Empty(pack.Problems);
        Assert.Equal("BronzeBuilds", pack.Manifest.Id);
        Assert.Equal(2, pack.Recipes.Count);

        var gate = pack.Recipes.Single(r => r.Id == "drake_bronze_gate");
        Assert.Equal(RecipeKind.Piece, gate.Kind);
        Assert.Equal("iron_grate", gate.Base);
        Assert.Null(gate.Look.Materials[0].Target);
        Assert.Equal("#C48A48", gate.Look.Materials[0].Tint);
        Assert.Equal(0.65f, gate.Look.Materials[0].Floats["_Glossiness"]);
        Assert.Equal(1200, gate.Fields["WearNTear"]["m_health"].AsNumber());
        Assert.Equal("glow", gate.Behaviours[0].Type);
        Assert.Equal(2, gate.Craft!.Requirements.Count);
        Assert.Equal(SnapMode.Add, gate.Snap!.Mode);
        Assert.Equal(2f, gate.Snap.Points[0].Y);
    }

    [Fact]
    public void Write_then_read_is_lossless()
    {
        var gate = PackReader.Load(SamplePack).Recipes.Single(r => r.Id == "drake_bronze_gate");
        var text = RecipeSerializer.WriteRecipe(gate);
        var problems = new List<string>();
        var back = RecipeSerializer.ReadRecipe(text, problems);

        Assert.Empty(problems);
        Assert.Equal(text, RecipeSerializer.WriteRecipe(back));
    }

    [Fact]
    public void Material_from_object_form_keeps_material_name()
    {
        var problems = new List<string>();
        var r = RecipeSerializer.ReadRecipe("""
            { "id": "x", "base": "y", "look": { "materials": [ { "target": "old", "from": { "prefab": "P", "material": "M" } } ] } }
            """, problems);

        Assert.Empty(problems);
        Assert.Equal("P", r.Look.Materials[0].FromPrefab);
        Assert.Equal("M", r.Look.Materials[0].FromMaterial);
        Assert.Contains("\"material\": \"M\"", RecipeSerializer.WriteRecipe(r));
    }

    [Theory]
    [InlineData("""{ "base": "y" }""", "Missing \"id\"")]
    [InlineData("""{ "id": "a b", "base": "y" }""", "may not contain")]
    [InlineData("""{ "id": "x" }""", "Missing \"base\"")]
    [InlineData("""{ "id": "x", "base": "y", "kind": "creature" }""", "Unknown kind")]
    [InlineData("""{ "id": "x", "base": "y", "look": { "materials": [ { "tint": "orange" } ] } }""", "not #RRGGBB")]
    [InlineData("""{ "id": "x", "base": "y", "snap": { "mode": "replace", "points": [[1, 2]] } }""", "[x, y, z]")]
    [InlineData("""{ "id": "x", "base": "y", "look": { "mesh": {} } }""", "look.mesh needs")]
    [InlineData("""{ "id": "x", "base": "y" """, "Unexpected end")]
    public void Bad_recipes_explain_themselves(string json, string expected)
    {
        var problems = new List<string>();
        RecipeSerializer.ReadRecipe(json, problems);
        Assert.Contains(problems, p => p.Contains(expected));
    }

    [Theory]
    [InlineData("#FF8000", 1f, 128 / 255f, 0f, 1f)]
    [InlineData("#f80", 1f, 136 / 255f, 0f, 1f)]
    [InlineData("#00000080", 0f, 0f, 0f, 128 / 255f)]
    [InlineData("#FF8000*2", 2f, 256 / 255f, 0f, 1f)]
    public void Parses_colors(string raw, float r, float g, float b, float a)
    {
        Assert.True(RecipeSerializer.TryParseColor(raw, out var c));
        Assert.Equal(new[] { r, g, b, a }, c);
    }

    [Fact]
    public void Sprites_round_trip()
    {
        var problems = new List<string>();
        var r = RecipeSerializer.ReadRecipe("""
            { "id": "paper", "base": "Wood", "look": { "hideMesh": true, "sprites": [
              { "file": "textures/note.png", "size": [0.4, 0.6], "position": [0, 0.05, 0], "rotation": [90, 0, 0], "doubleSided": false },
              { "file": "textures/b.png", "size": [1, 2] } ] } }
            """, problems);

        Assert.Empty(problems);
        Assert.True(r.Look.HideMesh);
        Assert.Equal(2, r.Look.Sprites.Count);
        Assert.Equal(0.6f, r.Look.Sprites[0].Height);
        Assert.Equal(90f, r.Look.Sprites[0].Rotation.X);
        Assert.False(r.Look.Sprites[0].DoubleSided);
        Assert.True(r.Look.Sprites[1].DoubleSided);

        var text = RecipeSerializer.WriteRecipe(r);
        Assert.Equal(text, RecipeSerializer.WriteRecipe(RecipeSerializer.ReadRecipe(text, problems)));
        Assert.Empty(problems);
    }

    [Fact]
    public void Resolve_refuses_paths_outside_the_pack()
    {
        var pack = new LoadedPack(Path.GetTempPath(), new ForgePack());
        Assert.Null(pack.Resolve("../../windows/evil.png"));
        Assert.NotNull(pack.Resolve("textures/ok.png"));
    }
}
