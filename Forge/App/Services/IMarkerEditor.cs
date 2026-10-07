using System.Numerics;

namespace DrakesForge.App.Services;

/// <summary>What the viewport tells its editor while points are picked and dragged.</summary>
public interface IMarkerEditor
{
    void SelectMarker(int id);
    /// <param name="constraint">The lock in effect ("free", "x", "y", "z", "floor"), so the editor only snaps coordinates that move.</param>
    void MoveMarker(int id, Vector3 unityPosition, string constraint);
    void EndMarkerDrag(int id);
    void DeleteMarker(int id);
}
