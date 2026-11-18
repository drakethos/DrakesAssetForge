using System.Numerics;
using DrakesForge.Valheim;

namespace DrakesForge.App.Services;

/// <summary>Outlines of collision shapes for the viewport, in Unity space.</summary>
public static class CollisionGeometry
{
    /// <summary>The 12 edges of a box given in some local space; <paramref name="toView"/> takes that space to the viewport's.</summary>
    public static void AddBox(List<Wire> wires, Vector3 center, Vector3 size, Matrix4x4 toView, uint argb)
    {
        var half = size / 2f;
        var corners = new Vector3[8];
        for (var i = 0; i < 8; i++)
            corners[i] = Vector3.Transform(center + new Vector3(
                (i & 1) != 0 ? half.X : -half.X,
                (i & 2) != 0 ? half.Y : -half.Y,
                (i & 4) != 0 ? half.Z : -half.Z), toView);
        for (var i = 0; i < 8; i++)
            foreach (var bit in new[] { 1, 2, 4 })
                if ((i & bit) == 0)
                    wires.Add(new Wire(corners[i], corners[i | bit], argb));
    }

    /// <summary>A collider read from a prefab; <paramref name="rootToView"/> takes the prefab root's space to the viewport's.</summary>
    public static void AddCollider(List<Wire> wires, ColliderInfo c, Matrix4x4 rootToView, uint argb)
    {
        var toView = c.ToRoot * rootToView;
        switch (c.Shape)
        {
            case ColliderShape.Sphere:
                Circle(wires, c.Center, Vector3.UnitX, Vector3.UnitY, c.Radius, toView, argb);
                Circle(wires, c.Center, Vector3.UnitY, Vector3.UnitZ, c.Radius, toView, argb);
                Circle(wires, c.Center, Vector3.UnitX, Vector3.UnitZ, c.Radius, toView, argb);
                break;
            case ColliderShape.Capsule:
            {
                var axis = c.Direction == 0 ? Vector3.UnitX : c.Direction == 2 ? Vector3.UnitZ : Vector3.UnitY;
                var u = c.Direction == 0 ? Vector3.UnitY : Vector3.UnitX;
                var v = Vector3.Cross(axis, u);
                var reach = Math.Max(c.Height / 2f - c.Radius, 0f);
                foreach (var end in new[] { c.Center - axis * reach, c.Center + axis * reach })
                {
                    Circle(wires, end, u, v, c.Radius, toView, argb);
                    Circle(wires, end, u, axis, c.Radius, toView, argb);
                    Circle(wires, end, v, axis, c.Radius, toView, argb);
                }
                foreach (var side in new[] { u, -u, v, -v })
                    wires.Add(new Wire(
                        Vector3.Transform(c.Center - axis * reach + side * c.Radius, toView),
                        Vector3.Transform(c.Center + axis * reach + side * c.Radius, toView), argb));
                break;
            }
            default:
                // Box, or a mesh collider's bounding box.
                AddBox(wires, c.Center, c.Size, toView, argb);
                break;
        }
    }

    private static void Circle(List<Wire> wires, Vector3 center, Vector3 u, Vector3 v, float radius, Matrix4x4 toView, uint argb)
    {
        const int segments = 24;
        var last = Vector3.Transform(center + u * radius, toView);
        for (var i = 1; i <= segments; i++)
        {
            var a = i * MathF.Tau / segments;
            var next = Vector3.Transform(center + (u * MathF.Cos(a) + v * MathF.Sin(a)) * radius, toView);
            wires.Add(new Wire(last, next, argb));
            last = next;
        }
    }
}
