using UnityEngine;
using DrakesForge.Format;

namespace DrakesForge.Runtime;

/// <summary>
/// Items: how a held item sits in the hand (look.hold), as offsets on Valheim's own pose.
/// Valheim mounts a copy of the attach child on the hand bone and zeroes its position and rotation
/// (VisEquipment.AttachItem); only its scale survives. So scale goes on attach, while the move and turn go on
/// attach's children, and attach itself is turned back the other way so dropped items keep Valheim's look.
/// </summary>
internal static class HoldPose
{
    public static void Apply(GameObject prefab, LookRecipe look)
    {
        if (!look.HasHold || prefab.GetComponent<ItemDrop>() == null || prefab.transform.Find(GameNames.AttachChild) is not { } attach)
            return;
        var move = new Vector3(look.HoldPosition.X, look.HoldPosition.Y, look.HoldPosition.Z);
        var turn = Quaternion.Euler(look.HoldRotation.X, look.HoldRotation.Y, look.HoldRotation.Z);
        attach.localScale = Vector3.Scale(attach.localScale, new Vector3(look.HoldScale.X, look.HoldScale.Y, look.HoldScale.Z));

        // In hand: point = move + scale * (turn * child point), so the move is in metres whatever the scale.
        var scale = attach.localScale;
        var offset = new Vector3(Divide(move.x, scale.x), Divide(move.y, scale.y), Divide(move.z, scale.z));
        foreach (Transform child in attach)
        {
            child.localPosition = offset + turn * child.localPosition;
            child.localRotation = turn * child.localRotation;
        }

        // Dropped: undo the turn and move on attach's own pose (exact for uniform scale).
        var back = attach.localRotation * Quaternion.Inverse(turn);
        attach.localPosition -= back * move;
        attach.localRotation = back;
    }

    private static float Divide(float value, float by) => by == 0f ? 0f : value / by;

    /// <summary>Placed or dropped copies follow the prefab's attach pose (hot reload moves them with the prefab).</summary>
    public static void CopyToInstance(GameObject prefab, GameObject instance)
    {
        if (prefab.GetComponent<ItemDrop>() == null || prefab.transform.Find(GameNames.AttachChild) is not { } source)
            return;
        if (instance.transform.Find(GameNames.AttachChild) is not { } target)
            return;
        target.localPosition = source.localPosition;
        target.localRotation = source.localRotation;
        target.localScale = source.localScale;

        // The hold's move and turn live on attach's children: match them by position, else by name.
        for (var i = 0; i < source.childCount; i++)
        {
            var from = source.GetChild(i);
            var to = i < target.childCount && target.GetChild(i).name == from.name ? target.GetChild(i) : target.Find(from.name);
            if (to == null)
                continue;
            to.localPosition = from.localPosition;
            to.localRotation = from.localRotation;
        }
    }
}
