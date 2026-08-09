using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared rig housekeeping for the player object, used by both controllers.
///
/// A player built from a Unity Capsule primitive arrives with a CapsuleCollider
/// on the body mesh, coincident with the CharacterController on the root. The
/// controller's overlap recovery fights that capsule on every Move — and the
/// third-person camera probe hits it too. Rather than make the setup
/// instructions longer, both problems are handled here in code.
/// </summary>
public static class PlayerRig
{
    /// <summary>
    /// Disables solid colliders on the player's own body so they cannot fight
    /// the CharacterController.
    ///
    /// This must be DISABLING, not Physics.IgnoreCollision: the controller's
    /// PhysX character module runs its own sweeps and overlap recovery and does
    /// not consult ignore pairs. With a body capsule perfectly overlapping the
    /// controller, that recovery shoves the player upward on every Move while
    /// gravity pulls back down — a permanent stand-still vibration that ignore
    /// pairs cannot cure. Triggers are left alone; they never collide anyway.
    /// </summary>
    public static void SuppressSelfColliders(CharacterController controller)
    {
        if (controller == null) return;

        var colliders = controller.GetComponentsInChildren<Collider>(true);
        foreach (Collider collider in colliders)
        {
            if (collider == null || collider == controller) continue;
            if (collider.isTrigger) continue;
            collider.enabled = false;
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

        // Skip no-op moves; toggling the controller for nothing costs a physics
        // re-registration.
        if ((controller.transform.position - position).sqrMagnitude < 1e-8f)
        {
            return;
        }

        bool wasEnabled = controller.enabled;
        controller.enabled = false;
        controller.transform.position = position;
        controller.enabled = wasEnabled;
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
