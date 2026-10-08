using UnityEngine;
using DrakesForge.Format;

namespace DrakesForge.Runtime;

/// <summary>
/// Items: how a held item sits in the hand (look.hold). Offsets on top of the vanilla attach pose, so the
/// mount logic stays Valheim's own. Applies to the attach child, which is what both held and dropped items show.
/// </summary>
internal static class HoldPose
{
    public static void Apply(GameObject prefab, LookRecipe look)
    {
        if (!look.HasHold || prefab.GetComponent<ItemDrop>() == null || prefab.transform.Find(GameNames.AttachChild) is not { } attach)
            return;
        attach.localPosition += new Vector3(look.HoldPosition.X, look.HoldPosition.Y, look.HoldPosition.Z);
        attach.localRotation *= Quaternion.Euler(look.HoldRotation.X, look.HoldRotation.Y, look.HoldRotation.Z);
        attach.localScale = Vector3.Scale(attach.localScale, new Vector3(look.HoldScale.X, look.HoldScale.Y, look.HoldScale.Z));
    }

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
    }
}
