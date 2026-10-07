using System.Collections.ObjectModel;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrakesForge.Format;
using DrakesForge.Valheim;

namespace DrakesForge.App.ViewModels;

/// <summary>
/// One kitbash part: another vanilla prefab's meshes placed on the model, with its own materials.
/// The preview uses the same rules as the runtime (positions in the prefab's space, Unity's Z-X-Y rotation).
/// </summary>
public sealed partial class PartRow : ObservableObject
{
    /// <summary>Viewport material slots for part <c>i</c> start at <c>SlotBase + i * SlotStride</c>.</summary>
    public const int SlotBase = 200_000;
    public const int SlotStride = 1_000;

    public PartRow(string prefab) => Prefab = prefab;

    public string Prefab { get; }
    /// <summary>Loaded vanilla preview (null until loaded, or when the prefab has no model).</summary>
    public VanillaModel? Model { get; set; }
    public ObservableCollection<MaterialEditor> Materials { get; } = new();

    [ObservableProperty] private int _number;
    [ObservableProperty] private string _child = "";
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _z;
    [ObservableProperty] private double _rotX;
    [ObservableProperty] private double _rotY;
    [ObservableProperty] private double _rotZ;
    [ObservableProperty] private double _scale = 1;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _meshNames = "";

    public IRelayCommand? RemoveCommand { get; set; }
    public IRelayCommand? SelectCommand { get; set; }

    /// <summary>Non-uniform scale from a hand-written recipe; kept until the Scale field is edited.</summary>
    private Vec3? _scaleVector;

    partial void OnScaleChanged(double value) => _scaleVector = null;

    public Vector3 Position => new((float)X, (float)Y, (float)Z);

    public void MoveTo(Vector3 p)
    {
        X = Math.Round(p.X, 3);
        Y = Math.Round(p.Y, 3);
        Z = Math.Round(p.Z, 3);
    }

    public static PartRow From(PartRecipe recipe)
    {
        var row = new PartRow(recipe.Prefab)
        {
            Child = recipe.Child ?? "",
            X = R(recipe.Position.X), Y = R(recipe.Position.Y), Z = R(recipe.Position.Z),
            RotX = R(recipe.Rotation.X), RotY = R(recipe.Rotation.Y), RotZ = R(recipe.Rotation.Z),
            Scale = R(recipe.Scale.X)
        };
        if (recipe.Scale.X != recipe.Scale.Y || recipe.Scale.Y != recipe.Scale.Z)
            row._scaleVector = recipe.Scale;
        return row;
    }

    private static double R(float v) => Math.Round(v, 4);

    public PartRecipe ToRecipe() => new()
    {
        Prefab = Prefab,
        Child = Child.Trim().Length > 0 ? Child.Trim() : null,
        Position = new Vec3((float)X, (float)Y, (float)Z),
        Rotation = new Vec3((float)RotX, (float)RotY, (float)RotZ),
        Scale = _scaleVector ?? new Vec3((float)Math.Max(Scale, 0.001), (float)Math.Max(Scale, 0.001), (float)Math.Max(Scale, 0.001)),
        Materials = Materials.Select(m => m.ToOverride()).Where(o => o != null).Select(o => o!).ToList()
    };

    /// <summary>The part's meshes in the viewport's space (X mirrored), filtered by <see cref="Child"/>.</summary>
    public IEnumerable<ModelPart> ViewParts(int index)
    {
        if (Model == null)
            yield break;
        var child = Child.Trim();
        var scale = _scaleVector is { } v ? new Vector3(v.X, v.Y, v.Z) : new Vector3((float)Scale);
        const float deg = MathF.PI / 180f;
        var rotation = Matrix4x4.CreateRotationZ((float)RotZ * deg) * Matrix4x4.CreateRotationX((float)RotX * deg) *
                       Matrix4x4.CreateRotationY((float)RotY * deg);
        var transform = Matrix4x4.CreateScale(scale) * rotation * Matrix4x4.CreateTranslation(Position);
        foreach (var part in Model.Parts)
        {
            if (child.Length > 0 && part.Name.Split('#')[0].IndexOf(child, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            yield return Transform(part, transform, rotation, SlotBase + index * SlotStride + part.MaterialSlot);
        }
    }

    /// <summary>Applies a Unity-space transform to a viewport-space part (X is mirrored on the way in and out).</summary>
    public static ModelPart Transform(ModelPart part, Matrix4x4 transform, Matrix4x4 rotation, int slot)
    {
        var positions = new float[part.Positions.Length];
        var normals = new float[part.Normals.Length];
        for (var i = 0; i + 2 < part.Positions.Length; i += 3)
        {
            var p = Vector3.Transform(new Vector3(-part.Positions[i], part.Positions[i + 1], part.Positions[i + 2]), transform);
            positions[i] = -p.X;
            positions[i + 1] = p.Y;
            positions[i + 2] = p.Z;
        }

        for (var i = 0; i + 2 < part.Normals.Length; i += 3)
        {
            var n = Vector3.TransformNormal(new Vector3(-part.Normals[i], part.Normals[i + 1], part.Normals[i + 2]), rotation);
            normals[i] = -n.X;
            normals[i + 1] = n.Y;
            normals[i + 2] = n.Z;
        }

        return new ModelPart
        {
            Name = part.Name,
            Positions = positions,
            Normals = normals,
            Uvs = part.Uvs,
            Indices = part.Indices,
            MaterialSlot = slot
        };
    }
}
