using System.Collections.Generic;
using System.Numerics;
using DrakesForge.Valheim;

namespace DrakesForge.App.Services;

/// <summary>
/// A simple body for the hold preview, drawn from boxes. Its right hand is at the origin, which is where Valheim
/// mounts a held item's attach child, so the item in the viewport reads as it would in hand. Proportions are about a 1.8 m person.
/// </summary>
public static class Mannequin
{
    /// <summary>Material slot used to draw the figure: not a real slot, the viewport colours it.</summary>
    public const int Slot = -2;
    /// <summary>The parts that mark the front (nose, chest panel): drawn in a stronger colour so facing is obvious.</summary>
    public const int FrontSlot = -3;

    public static IReadOnlyList<ModelPart> Parts()
    {
        var parts = new List<ModelPart>();
        foreach (var (centre, size, slot) in Boxes())
            parts.Add(Box(centre, size, slot));
        return parts;
    }

    // Forward is +Z (Unity). Built standing on the floor, then shifted so the right hand (+X side) is at the origin.
    private static IEnumerable<(Vector3 Centre, Vector3 Size, int Slot)> Boxes()
    {
        var hand = new Vector3(-0.04f, 0.85f, 0f);
        var shoulder = new Vector3(-0.04f, 1.47f, 0f);
        const int body = Slot;
        var boxes = new (Vector3 Centre, Vector3 Size, int Slot)[]
        {
            (new Vector3(-0.35f, 1.25f, 0f), new Vector3(0.42f, 0.55f, 0.24f), body),  // torso
            (new Vector3(-0.35f, 1.66f, 0f), new Vector3(0.20f, 0.24f, 0.20f), body),  // head
            (new Vector3(-0.35f, 0.92f, 0f), new Vector3(0.40f, 0.14f, 0.22f), body),  // hips
            (new Vector3(-0.46f, 0.45f, 0f), new Vector3(0.15f, 0.90f, 0.15f), body),  // left leg
            (new Vector3(-0.24f, 0.45f, 0f), new Vector3(0.15f, 0.90f, 0.15f), body),  // right leg
            (new Vector3(-0.66f, 1.20f, 0f), new Vector3(0.10f, 0.60f, 0.10f), body),  // left arm
            ((shoulder + hand) / 2, new Vector3(0.10f, Vector3.Distance(shoulder, hand), 0.10f), body), // right arm
            (hand, new Vector3(0.12f, 0.12f, 0.12f), body),                             // hand
            // Front markers: a nose on the head and a chest panel, both on the +Z (forward) side.
            (new Vector3(-0.35f, 1.66f, 0.12f), new Vector3(0.07f, 0.07f, 0.06f), FrontSlot),  // nose
            (new Vector3(-0.35f, 1.28f, 0.125f), new Vector3(0.30f, 0.36f, 0.01f), FrontSlot), // chest panel
        };
        foreach (var (centre, size, slot) in boxes)
            yield return (centre - hand, size, slot);
    }

    private static ModelPart Box(Vector3 c, Vector3 s, int slot)
    {
        var h = s / 2;
        // Each face: four corners and its normal, two triangles.
        var faces = new (Vector3 Normal, Vector3[] Corners)[]
        {
            (Vector3.UnitZ,    new[] { c + new Vector3(-h.X, -h.Y,  h.Z), c + new Vector3( h.X, -h.Y,  h.Z), c + new Vector3( h.X,  h.Y,  h.Z), c + new Vector3(-h.X,  h.Y,  h.Z) }),
            (-Vector3.UnitZ,   new[] { c + new Vector3( h.X, -h.Y, -h.Z), c + new Vector3(-h.X, -h.Y, -h.Z), c + new Vector3(-h.X,  h.Y, -h.Z), c + new Vector3( h.X,  h.Y, -h.Z) }),
            (Vector3.UnitX,    new[] { c + new Vector3( h.X, -h.Y,  h.Z), c + new Vector3( h.X, -h.Y, -h.Z), c + new Vector3( h.X,  h.Y, -h.Z), c + new Vector3( h.X,  h.Y,  h.Z) }),
            (-Vector3.UnitX,   new[] { c + new Vector3(-h.X, -h.Y, -h.Z), c + new Vector3(-h.X, -h.Y,  h.Z), c + new Vector3(-h.X,  h.Y,  h.Z), c + new Vector3(-h.X,  h.Y, -h.Z) }),
            (Vector3.UnitY,    new[] { c + new Vector3(-h.X,  h.Y,  h.Z), c + new Vector3( h.X,  h.Y,  h.Z), c + new Vector3( h.X,  h.Y, -h.Z), c + new Vector3(-h.X,  h.Y, -h.Z) }),
            (-Vector3.UnitY,   new[] { c + new Vector3(-h.X, -h.Y, -h.Z), c + new Vector3( h.X, -h.Y, -h.Z), c + new Vector3( h.X, -h.Y,  h.Z), c + new Vector3(-h.X, -h.Y,  h.Z) }),
        };

        var positions = new List<float>();
        var normals = new List<float>();
        var indices = new List<int>();
        foreach (var (normal, corners) in faces)
        {
            var first = positions.Count / 3;
            foreach (var corner in corners)
            {
                positions.Add(corner.X);
                positions.Add(corner.Y);
                positions.Add(corner.Z);
                normals.Add(normal.X);
                normals.Add(normal.Y);
                normals.Add(normal.Z);
            }
            indices.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
        }

        return new ModelPart
        {
            Name = "mannequin",
            Positions = positions.ToArray(),
            Normals = normals.ToArray(),
            Uvs = new float[positions.Count / 3 * 2],
            Indices = indices.ToArray(),
            MaterialSlot = slot
        };
    }
}
