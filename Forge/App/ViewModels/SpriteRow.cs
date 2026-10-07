using System.Numerics;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrakesForge.App.Services;
using DrakesForge.Format;
using DrakesForge.Valheim;

namespace DrakesForge.App.ViewModels;

/// <summary>One flat image on the model: file, size, placement, and its quad for the viewport.</summary>
public sealed partial class SpriteRow : ObservableObject
{
    /// <summary>Viewport material slots for sprites start here, after any real material slot.</summary>
    public const int SlotBase = 100_000;

    private bool _syncing;

    public SpriteRow(string file, RgbaImage? image)
    {
        File = file;
        Image = image;
        Thumbnail = image != null ? Images.ToBitmap(Images.Resize(image, 64, Math.Max(1, (int)(64.0 * image.Height / image.Width)))) : null;
        Aspect = image is { Width: > 0, Height: > 0 } ? image.Width / (double)image.Height : 1;
    }

    /// <summary>Pack-relative PNG.</summary>
    public string File { get; }
    public string FileName => Path.GetFileName(File);
    public RgbaImage? Image { get; }
    public Bitmap? Thumbnail { get; }
    /// <summary>The image's width / height.</summary>
    public double Aspect { get; }
    public bool IsMissing => Image == null;

    [ObservableProperty] private double _width = 1;
    [ObservableProperty] private double _height = 1;
    [ObservableProperty] private bool _keepAspect = true;
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _z;
    [ObservableProperty] private double _rotX;
    [ObservableProperty] private double _rotY;
    [ObservableProperty] private double _rotZ;
    [ObservableProperty] private bool _doubleSided = true;

    public IRelayCommand? RemoveCommand { get; set; }

    partial void OnWidthChanged(double value)
    {
        if (_syncing || !KeepAspect || value <= 0)
            return;
        _syncing = true;
        Height = Math.Round(value / Aspect, 3);
        _syncing = false;
    }

    partial void OnHeightChanged(double value)
    {
        if (_syncing || !KeepAspect || value <= 0)
            return;
        _syncing = true;
        Width = Math.Round(value * Aspect, 3);
        _syncing = false;
    }

    /// <summary>Upright, standing on the origin (signs, banners, cut-outs).</summary>
    [RelayCommand]
    private void StandUp()
    {
        RotX = 0;
        Y = 0;
        Z = 0;
    }

    /// <summary>Flat on the ground, face up, centred on the origin (paper, rugs, decals).</summary>
    [RelayCommand]
    private void LieFlat()
    {
        RotX = -90;
        Y = 0.01;
        Z = Math.Round(Height / 2, 3);
    }

    public static SpriteRow From(SpriteRecipe s, RgbaImage? image)
    {
        var row = new SpriteRow(s.File, image) { _keepAspect = false };
        row.Width = R(s.Width);
        row.Height = R(s.Height);
        row.X = R(s.Position.X);
        row.Y = R(s.Position.Y);
        row.Z = R(s.Position.Z);
        row.RotX = R(s.Rotation.X);
        row.RotY = R(s.Rotation.Y);
        row.RotZ = R(s.Rotation.Z);
        row.DoubleSided = s.DoubleSided;
        // Keep the lock only if the saved size already matches the image.
        row.KeepAspect = Math.Abs(s.Width / Math.Max(s.Height, 1e-4) - row.Aspect) < 0.01;
        return row;
    }

    // Saved values are floats: 0.6f reads back as 0.6000000238 without this.
    private static double R(float v) => Math.Round(v, 4);

    public SpriteRecipe ToRecipe() => new()
    {
        File = File,
        Width = (float)Math.Round(Math.Max(Width, 0.001), 3),
        Height = (float)Math.Round(Math.Max(Height, 0.001), 3),
        Position = new Vec3((float)X, (float)Y, (float)Z),
        Rotation = new Vec3((float)RotX, (float)RotY, (float)RotZ),
        DoubleSided = DoubleSided
    };

    /// <summary>The quad as the runtime builds it, in the viewport's axes.</summary>
    public ModelPart ToPart(int slot) =>
        SpriteGeometry.Part(FileName, Width, Height, new Vector3((float)X, (float)Y, (float)Z), new Vector3((float)RotX, (float)RotY, (float)RotZ), slot);
}
