using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared rig housekeeping for the player object, used by both controllers.
///
/// A player built from a Unity Capsule primitive arrives with a CapsuleCollider
/// on the body mesh, sitting in the same space as the CharacterController on the
/// root. Those two fight every frame — and the third-person camera's collision
/// probe hits the body and slams itself into the player. Rather than make the
/// setup instructions longer, both problems are handled here in code.
/// </summary>
public static class PlayerRig
{
    /// <summary>
    /// Stops the CharacterController colliding with colliders on its own body.
    /// A child CapsuleCollider overlapping the controller produces a constant
    /// depenetration fight, which reads as jitter while standing still.
    /// </summary>
    public static void IgnoreSelfColliders(CharacterController controller)
    {
        if (controller == null) return;

        var colliders = controller.GetComponentsInChildren<Collider>(true);
        foreach (Collider collider in colliders)
        {
            if (collider == null || collider == controller) continue;
            if (collider.isTrigger) continue;  // triggers are for gameplay, leave them alone
            Physics.IgnoreCollision(controller, collider, true);
        }
    }

    /// <summary>
    /// True when a collider belongs to the player's own hierarchy, so the
    /// camera probe can skip it. Without this the probe's very first hit is the
    /// player's own body: PhysX reports distance 0 for a cast that starts inside
    /// a collider, the camera jams to its minimum, and the flicker between
    /// hitting and not hitting is the jitter that causes the dizziness.
    /// </summary>
    public static bool IsSelfCollider(Collider collider, Transform playerRoot)
    {
        if (collider == null || playerRoot == null) return false;
        return collider.transform == playerRoot || collider.transform.IsChildOf(playerRoot);
    }

    /// <summary>
    /// Shows or hides the player's body renderers. In first person the camera
    /// sits inside the body mesh — near clip plane 0.3 against a capsule you are
    /// standing inside means looking down shows the inside of your own model.
    /// Hiding it is what every first-person game does.
    /// </summary>
    public static void SetBodyVisible(Transform playerRoot, bool visible, List<Renderer> cache)
    {
        if (playerRoot == null) return;

        if (cache.Count == 0)
        {
            playerRoot.GetComponentsInChildren(true, cache);
            // Canvas graphics are renderers too; never hide the UI.
            cache.RemoveAll(r => r == null || r is CanvasRenderer || r.GetComponentInParent<Canvas>() != null);
        }

        foreach (Renderer renderer in cache)
        {
            if (renderer != null && renderer.enabled != visible) renderer.enabled = visible;
        }
    }

    /// <summary>
    /// Teleports a CharacterController safely. The controller caches its own
    /// position internally, so moving the transform while it is enabled gets
    /// overwritten on the next Move call.
    /// </summary>
    public static void Teleport(CharacterController controller, Vector3 position)
    {
        if (controller == null)
        {
            return;
        }

        // A no-op teleport is not free: toggling a collider below wipes its
        // Physics.IgnoreCollision pairs, silently re-enabling self-collision.
        if ((controller.transform.position - position).sqrMagnitude < 1e-8f)
        {
            return;
        }

        bool wasEnabled = controller.enabled;
        controller.enabled = false;
        controller.transform.position = position;
        controller.enabled = wasEnabled;

        // Unity drops every IgnoreCollision pair involving a collider when it is
        // disabled, so the toggle above just undid IgnoreSelfColliders. Restore
        // it, or the controller resumes fighting its own body colliders — which
        // shows up as the player jittering up and down while standing still.
        if (wasEnabled)
        {
            IgnoreSelfColliders(controller);
        }
    }

    /// <summary>
    /// World Y that puts the controller's feet on <paramref name="groundY"/>.
    /// </summary>
    public static float FeetToCenter(CharacterController controller, float groundY)
    {
        if (controller == null) return groundY;
        // Bottom of the capsule sits at center.y - height/2 in local space.
        return groundY + controller.height * 0.5f - controller.center.y + controller.skinWidth;
    }
}
