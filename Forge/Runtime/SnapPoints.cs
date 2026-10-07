using System.Collections.Generic;
using DrakesForge.Format;
using UnityEngine;

namespace DrakesForge.Runtime;

/// <summary>Valheim snap points are direct children tagged "snappoint" (Piece.GetSnapPoints).</summary>
internal static class SnapPoints
{
    private const string Tag = "snappoint";
    private const string ChildName = "_snappoint";

    public sealed class Snapshot
    {
        public List<Vector3> Positions { get; } = new();
        public bool Active { get; set; }
    }

    public static Snapshot Capture(GameObject piece)
    {
        var snapshot = new Snapshot();
        foreach (var child in Children(piece))
        {
            snapshot.Positions.Add(child.localPosition);
            snapshot.Active = child.gameObject.activeSelf;
        }

        return snapshot;
    }

    public static void Apply(GameObject piece, SnapRecipe snap)
    {
        if (snap.Mode == SnapMode.Keep)
            return;

        // New points match the base's active state, so they behave like its own.
        var active = Capture(piece) is { Positions.Count: > 0 } existing && existing.Active;
        if (snap.Mode == SnapMode.Replace)
            RemoveAll(piece);
        foreach (var p in snap.Points)
            Create(piece, new Vector3(p.X, p.Y, p.Z), active);
    }

    public static void Restore(GameObject piece, Snapshot snapshot)
    {
        RemoveAll(piece);
        foreach (var position in snapshot.Positions)
            Create(piece, position, snapshot.Active);
    }

    private static List<Transform> Children(GameObject piece)
    {
        var result = new List<Transform>();
        foreach (Transform child in piece.transform)
            if (child.CompareTag(Tag))
                result.Add(child);
        return result;
    }

    private static void RemoveAll(GameObject piece)
    {
        foreach (var child in Children(piece))
            Object.DestroyImmediate(child.gameObject);
    }

    private static void Create(GameObject piece, Vector3 localPosition, bool active)
    {
        var point = new GameObject(ChildName) { tag = Tag };
        point.transform.SetParent(piece.transform, false);
        point.transform.localPosition = localPosition;
        point.SetActive(active);
    }
}
