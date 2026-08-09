using OtherwiseLabs.TerrainTools;
using UnityEngine;

/// <summary>
/// Puts the player on the ground at startup instead of letting them fall onto it.
///
/// Streamed terrain does not exist on frame 1 — chunks are built a few per frame,
/// and only chunks near the viewer get colliders at all. A player authored at any
/// Y therefore free-falls through empty space for as long as the world takes to
/// build, then lands hard once a collider finally appears. That is the drop, the
/// jitter and the "spawns high" symptom, all from the same cause.
///
/// The terrain's height is a pure function of position, so it can be sampled
/// analytically before any chunk exists. This component snaps the player onto
/// that surface immediately and holds them there — gravity suspended — until real
/// collision geometry is confirmed underneath.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[AddComponentMenu("Otherwise Labs/Player Terrain Spawner")]
public class PlayerTerrainSpawner : MonoBehaviour
{
    [Tooltip("Streamer to sample. Auto-found in the scene when empty.")]
    public InfiniteTerrainStreamer streamer;

    [Tooltip("Clearance above the sampled surface, in world units.")]
    [Min(0f)] public float groundClearance = 0.05f;

    [Tooltip("Give up waiting for real terrain colliders after this long, and let gravity take over.")]
    [Min(0.1f)] public float readyTimeout = 8f;

    [Tooltip("Log where the player was placed. Useful when a spawn looks wrong.")]
    public bool logSpawn = true;

    /// <summary>
    /// True while the player is being held on the analytic surface. Both
    /// controllers skip movement and gravity while this is set, so nothing
    /// fights the spawner for control of the transform.
    /// </summary>
    public static bool SpawnPending { get; private set; }

    CharacterController _controller;
    float _startTime;
    bool _done;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        if (streamer == null) streamer = FindObjectOfType<InfiniteTerrainStreamer>();

        // Do this before the first physics step, not after the first collision.
        PlayerRig.IgnoreSelfColliders(_controller);

        SpawnPending = true;
        _startTime = Time.time;
        SnapToSampledGround();
    }

    void OnDisable()
    {
        // Never leave the controllers gated by a component that is gone.
        SpawnPending = false;
    }

    void Update()
    {
        if (_done) return;

        if (HasRealGroundBelow())
        {
            Finish("terrain collider ready");
            return;
        }

        if (Time.time - _startTime > readyTimeout)
        {
            Finish("timed out waiting for terrain colliders");
            return;
        }

        // Hold position on the analytic surface. Re-sampling each frame keeps the
        // player level even while chunks are still streaming in around them.
        SnapToSampledGround();
    }

    void SnapToSampledGround()
    {
        if (streamer == null) { Finish("no InfiniteTerrainStreamer in scene"); return; }

        // Chunks are children of the streamer, so sample in its space and convert
        // back. Keeps working if the streamer is ever moved off the origin.
        Vector3 local = streamer.transform.InverseTransformPoint(transform.position);
        float surface = streamer.SampleWorldHeight(local.x, local.z);
        Vector3 groundWorld = streamer.transform.TransformPoint(new Vector3(local.x, surface, local.z));

        Vector3 target = transform.position;
        target.y = PlayerRig.FeetToCenter(_controller, groundWorld.y) + groundClearance;
        PlayerRig.Teleport(_controller, target);
    }

    bool HasRealGroundBelow()
    {
        // Cast from inside the capsule down past the feet. Anything solid means
        // a chunk collider has arrived and physics can take over.
        float reach = _controller.height * 0.5f + 0.5f;
        Vector3 origin = transform.position + Vector3.up * 0.1f;

        int count = Physics.RaycastNonAlloc(origin, Vector3.down, _hitBuffer, reach, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (!PlayerRig.IsSelfCollider(_hitBuffer[i].collider, transform)) return true;
        }
        return false;
    }

    readonly RaycastHit[] _hitBuffer = new RaycastHit[8];

    void Finish(string reason)
    {
        if (_done) return;
        _done = true;
        SpawnPending = false;
        if (logSpawn)
            Debug.Log($"[{name}] Spawned at {transform.position} ({reason}).", this);
    }
}
