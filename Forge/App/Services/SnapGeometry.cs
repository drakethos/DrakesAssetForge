using System.Numerics;
using DrakesForge.Valheim;

namespace DrakesForge.App.Services;

/// <summary>The model's bounding box in Unity space (metres, local to the piece's origin).</summary>
public readonly record struct Box(Vector3 Min, Vector3 Max)
{
    public Vector3 Size => Max - Min;
    public Vector3 Center => (Min + Max) / 2f;
    public bool IsEmpty => Min.X > Max.X;
}

/// <summary>
/// Snap point helpers: where a point sits on the model in words, points found from the model's shape, and
/// "magnetic" edges/grid while dragging. Directions as seen in the viewport's default view, which looks at the
/// front: up = +Y, front = +Z, right = -X (the viewport draws these as a small compass).
/// </summary>
public static class SnapGeometry
{
    /// <summary>Bounds of the drawn parts (viewport space, X mirrored) converted back to Unity space.</summary>
    public static Box Bounds(IEnumerable<ModelPart> parts)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var part in parts)
            for (var i = 0; i + 2 < part.Positions.Length; i += 3)
            {
                var p = new Vector3(-part.Positions[i], part.Positions[i + 1], part.Positions[i + 2]);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

        return new Box(min, max);
    }

    /// <summary>
    /// "bottom-right-front corner", "top-left edge", "top centre"… Thin directions (a floor's thickness, a sheet's depth)
    /// don't count, so a flat piece has corners and edges like a rectangle, and a pole has ends.
    /// </summary>
    public static string Label(Vector3 p, Box box)
    {
        if (box.IsEmpty)
            return "point";
        var biggest = Math.Max(box.Size.X, Math.Max(box.Size.Y, box.Size.Z));
        var axes = 0;
        string? Side(float v, float min, float max, string low, string high)
        {
            if (max - min < Math.Max(biggest * 0.2f, 0.04f))
                return null; // too thin to have sides
            axes++;
            var tol = Math.Max((max - min) * 0.15f, 0.02f);
            if (v <= min + tol)
                return low;
            if (v >= max - tol)
                return high;
            return null;
        }

        var y = Side(p.Y, box.Min.Y, box.Max.Y, "bottom", "top");
        var x = Side(-p.X, -box.Max.X, -box.Min.X, "left", "right");
        var z = Side(p.Z, box.Min.Z, box.Max.Z, "back", "front");
        var words = new[] { y, x, z }.Where(w => w != null).ToList();
        var name = string.Join("-", words);
        var kind = (axes, words.Count) switch
        {
            (_, 0) => "centre",
            (1, _) => name + " end",
            (2, 2) or (3, 3) => name + " corner",
            (2, 1) => name + " edge middle",
            (3, 2) => name + " edge",
            _ => name + " centre"
        };
        var outside = p.X < box.Min.X - 0.05f || p.X > box.Max.X + 0.05f || p.Y < box.Min.Y - 0.05f || p.Y > box.Max.Y + 0.05f ||
                      p.Z < box.Min.Z - 0.05f || p.Z > box.Max.Z + 0.05f;
        return outside ? kind + " (outside)" : kind;
    }

    public enum Preset { Detect, BottomCorners, TopCorners, EdgeMiddles, Centre }

    /// <summary>Points from the model's box. Detect: floors and walls get the 4 corners of their big face, beams their 2 ends, anything else 8 corners.</summary>
    public static List<Vector3> Points(Box box, Preset preset)
    {
        var result = new List<Vector3>();
        if (box.IsEmpty)
            return result;
        var (min, max, size) = (box.Min, box.Max, box.Size);
        var biggest = Math.Max(size.X, Math.Max(size.Y, size.Z));
        var thin = new[] { size.X, size.Y, size.Z }.Select(s => s < biggest * 0.2f).ToArray();
        // A thin piece's points go on its centre plane, or on 0 if the plane passes through the origin (most vanilla pieces).
        float Plane(int axis) => axis switch
        {
            0 => min.X <= 0 && max.X >= 0 ? 0 : box.Center.X,
            1 => min.Y <= 0 && max.Y >= 0 ? 0 : box.Center.Y,
            _ => min.Z <= 0 && max.Z >= 0 ? 0 : box.Center.Z
        };

        switch (preset)
        {
            case Preset.Detect when thin.Count(t => t) == 2:
            {
                // Beam or pole: both ends of the long axis.
                var axis = Array.IndexOf(thin, false);
                var c = new Vector3(Plane(0), Plane(1), Plane(2));
                for (var end = 0; end < 2; end++)
                {
                    var p = c;
                    var v = end == 0 ? Get(min, axis) : Get(max, axis);
                    result.Add(Set(p, axis, v));
                }

                return result;
            }
            case Preset.Detect when thin.Count(t => t) == 1:
            {
                var flat = Array.IndexOf(thin, true);
                foreach (var corner in FaceCorners(box, flat))
                    result.Add(Set(corner, flat, Plane(flat)));
                return result;
            }
            case Preset.Detect:
                foreach (var y in new[] { min.Y, max.Y })
                    result.AddRange(FaceCorners(box, 1).Select(c => c with { Y = y }));
                return result;
            case Preset.BottomCorners:
                return FaceCorners(box, 1).Select(c => c with { Y = min.Y }).ToList();
            case Preset.TopCorners:
                return FaceCorners(box, 1).Select(c => c with { Y = max.Y }).ToList();
            case Preset.EdgeMiddles:
            {
                // Middles of the big face's edges for flat pieces; of the bottom face otherwise.
                var flat = thin.Count(t => t) == 1 ? Array.IndexOf(thin, true) : 1;
                var level = flat == 1 && thin.Count(t => t) != 1 ? min.Y : Plane(flat);
                var corners = FaceCorners(box, flat).Select(c => Set(c, flat, level)).ToList();
                for (var i = 0; i < 4; i++)
                    result.Add((corners[i] + corners[(i + 1) % 4]) / 2f);
                return result;
            }
            default:
                return new List<Vector3> { box.Center with { Y = min.Y }, box.Center, box.Center with { Y = max.Y } };
        }
    }

    /// <summary>The 4 corners of the face perpendicular to <paramref name="axis"/>, in order around it (that axis left at centre).</summary>
    private static List<Vector3> FaceCorners(Box box, int axis)
    {
        var (min, max, c) = (box.Min, box.Max, box.Center);
        return axis switch
        {
            0 => new List<Vector3> { new(c.X, min.Y, min.Z), new(c.X, max.Y, min.Z), new(c.X, max.Y, max.Z), new(c.X, min.Y, max.Z) },
            1 => new List<Vector3> { new(min.X, c.Y, min.Z), new(max.X, c.Y, min.Z), new(max.X, c.Y, max.Z), new(min.X, c.Y, max.Z) },
            _ => new List<Vector3> { new(min.X, min.Y, c.Z), new(max.X, min.Y, c.Z), new(max.X, max.Y, c.Z), new(min.X, max.Y, c.Z) }
        };
    }

    /// <summary>
    /// Magnet for dragging: each moving coordinate sticks to the model's min/centre/max or 0 when close,
    /// otherwise rounds to the grid (0 = no grid).
    /// </summary>
    public static Vector3 Snap(Vector3 p, Box box, bool edges, float grid, bool moveX = true, bool moveY = true, bool moveZ = true)
    {
        var reach = box.IsEmpty ? 0.03f : Math.Max(box.Size.Length() * 0.03f, 0.02f);
        float One(float v, float min, float max)
        {
            if (edges && !box.IsEmpty)
                foreach (var target in new[] { min, (min + max) / 2f, max, 0f })
                    if (Math.Abs(v - target) <= reach)
                        return target;
            return grid > 0 ? MathF.Round(v / grid) * grid : v;
        }

        return new Vector3(
            moveX ? One(p.X, box.Min.X, box.Max.X) : p.X,
            moveY ? One(p.Y, box.Min.Y, box.Max.Y) : p.Y,
            moveZ ? One(p.Z, box.Min.Z, box.Max.Z) : p.Z);
    }

    private static float Get(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
    private static Vector3 Set(Vector3 v, int axis, float value) => axis == 0 ? v with { X = value } : axis == 1 ? v with { Y = value } : v with { Z = value };
}
