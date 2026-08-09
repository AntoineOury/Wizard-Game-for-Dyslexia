using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// First-person movement and look in one component, replacing the
/// PlayerMovement + FirstPersonCam pair.
///
/// Setup: on the player root, remove PlayerMovement, Rigidbody and any capsule
/// collider, then add this (a CharacterController is added automatically).
/// Remove FirstPersonCam from the camera child — this script drives the pitch.
///
/// Improvements over the old pair:
/// - CharacterController instead of Rigidbody forces: no sliding on slopes, a
///   real slope limit and step offset, and no ground-layer mask to configure —
///   which also means it just works on streamed terrain chunks.
/// - Mouse look is no longer multiplied by Time.deltaTime. Mouse deltas are
///   already per-frame, so the old code made look speed depend on framerate.
/// - Desktop/Touch aware: reads keyboard + mouse or the on-screen controls
///   depending on PlayerControlScheme, and owns the cursor policy that
///   previously made the UI unclickable.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[AddComponentMenu("Otherwise Labs/First Person Controller")]
public class FirstPersonController : MonoBehaviour
{
    const float CoyoteTime = 0.12f;

    [Header("References")]
    [Tooltip("Camera to pitch up and down. Auto-finds a child Camera when empty.")]
    public Transform cameraTransform;

    [Header("Movement")]
    [Min(0f)] public float walkSpeed = 4.5f;
    [Min(0f)] public float sprintSpeed = 7.5f;
    [Tooltip("How quickly horizontal velocity reaches its target, in m/s². Higher = snappier.")]
    [Min(1f)] public float acceleration = 40f;
    [Tooltip("Fraction of acceleration available while airborne.")]
    [Range(0f, 1f)] public float airControl = 0.4f;
    [Min(0f)] public float gravity = 25f;
    [Tooltip("Apex height of a jump in world units. 0 disables jumping.")]
    [Min(0f)] public float jumpHeight = 1.5f;

    [Header("Look")]
    [Tooltip("Degrees per mouse delta unit.")]
    [Min(0.01f)] public float mouseSensitivity = 2f;
    [Tooltip("Degrees per percent of screen height dragged on the look area.")]
    [Min(0.01f)] public float touchSensitivity = 2f;
    public float minPitch = -75f;
    public float maxPitch = 60f;

    readonly List<Renderer> _bodyRenderers = new List<Renderer>();
    CharacterController _controller;
    Vector3 _horizontalVelocity;
    Vector3 _cameraLocalPosition;
    float _verticalVelocity;
    float _pitch;
    float _lastGroundedTime = float.NegativeInfinity;
    bool _uiMode;

    /// <summary>True while gameplay input is suspended so the cursor can use UI (desktop Escape toggle).</summary>
    public bool InUiMode => _uiMode;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        if (cameraTransform == null)
        {
            Camera childCamera = GetComponentInChildren<Camera>();
            if (childCamera != null) cameraTransform = childCamera.transform;
        }
        if (cameraTransform != null)
        {
            _pitch = NormalizePitch(cameraTransform.localEulerAngles.x);
            _cameraLocalPosition = cameraTransform.localPosition;
        }

        // A body built from a Capsule primitive brings its own CapsuleCollider,
        // which fights the CharacterController every frame if left connected.
        PlayerRig.IgnoreSelfColliders(_controller);
    }

    void Update()
    {
        // ThirdPersonController drives while its view mode is active; both
        // components stay enabled so a UI toggle can switch on the fly.
        if (PlayerViewMode.Current != ViewModeKind.FirstPerson) return;

        // The camera sits inside the body mesh in first person, so looking down
        // would show the inside of the player's own model.
        PlayerRig.SetBodyVisible(transform, false, _bodyRenderers);

        ControlSchemeKind scheme = PlayerControlScheme.Current;
        if (scheme == ControlSchemeKind.Touch) TouchControls.EnsureExists();

        ApplyCursorPolicy(scheme);
        HandleLook(scheme);

        // The spawner owns the transform until the player is standing on real
        // ground; applying gravity now would drag them through unbuilt terrain.
        if (PlayerTerrainSpawner.SpawnPending)
        {
            _verticalVelocity = 0f;
            _horizontalVelocity = Vector3.zero;
            return;
        }

        HandleMove(scheme);
    }

    void OnDisable()
    {
        // Whatever disables the player (scene switch, death, cutscene) should
        // leave the person with a usable cursor, and their body back on show.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        PlayerRig.SetBodyVisible(transform, true, _bodyRenderers);
    }

    void ApplyCursorPolicy(ControlSchemeKind scheme)
    {
        if (scheme == ControlSchemeKind.Touch)
        {
            // Touch has no pointer to trap; on a laptop this leaves the mouse
            // free to act as the finger.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        // Desktop: Escape flips between look mode (cursor locked, camera live)
        // and UI mode (cursor free for clicking buttons).
        if (Input.GetKeyDown(KeyCode.Escape)) _uiMode = !_uiMode;
        Cursor.lockState = _uiMode ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = _uiMode;

        // Belt and braces for every button, present and future: while in look
        // mode nothing may stay "selected", or Space (the UI Submit key) would
        // re-press it instead of only jumping.
        if (!_uiMode && EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    void HandleLook(ControlSchemeKind scheme)
    {
        Vector2 look = Vector2.zero;
        if (scheme == ControlSchemeKind.Desktop)
        {
            if (!_uiMode)
                look = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * mouseSensitivity;
        }
        else
        {
            look = TouchControls.ConsumeLookDelta() * touchSensitivity;
        }

        transform.Rotate(0f, look.x, 0f);

        _pitch = Mathf.Clamp(_pitch - look.y, minPitch, maxPitch);
        if (cameraTransform != null)
        {
            // Re-assert the authored local pose every frame: third person moves
            // the camera in world space, so returning to first person must snap
            // it back onto the head.
            cameraTransform.localPosition = _cameraLocalPosition;
            cameraTransform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }
    }

    void HandleMove(ControlSchemeKind scheme)
    {
        Vector2 moveInput;
        bool sprint;
        bool jumpPressed;
        if (scheme == ControlSchemeKind.Desktop)
        {
            moveInput = _uiMode
                ? Vector2.zero
                : new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            sprint = Input.GetKey(KeyCode.LeftShift);
            jumpPressed = !_uiMode && Input.GetKeyDown(KeyCode.Space);
        }
        else
        {
            moveInput = TouchControls.Move;
            // Pushing the stick to its rim reads as "as fast as possible".
            sprint = moveInput.magnitude > 0.92f;
            jumpPressed = TouchControls.ConsumeJump();
        }

        if (moveInput.sqrMagnitude > 1f) moveInput.Normalize();

        Vector3 wishDirection = transform.right * moveInput.x + transform.forward * moveInput.y;
        Vector3 targetVelocity = wishDirection * (sprint ? sprintSpeed : walkSpeed);

        float control = _controller.isGrounded ? 1f : airControl;
        _horizontalVelocity = Vector3.MoveTowards(
            _horizontalVelocity, targetVelocity, acceleration * control * Time.deltaTime);

        bool grounded = _controller.isGrounded;
        if (grounded) _lastGroundedTime = Time.time;

        // Coyote window: a jump pressed a hair after stepping off a ledge still
        // fires, which reads as fair rather than floaty.
        if (jumpPressed && jumpHeight > 0f && Time.time - _lastGroundedTime <= CoyoteTime)
        {
            _verticalVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);
            _lastGroundedTime = float.NegativeInfinity;
        }
        else if (grounded && _verticalVelocity < 0f)
        {
            // Small downward bias keeps the controller glued to slopes instead
            // of hopping down them.
            _verticalVelocity = -3f;
        }
        _verticalVelocity -= gravity * Time.deltaTime;

        Vector3 motion = _horizontalVelocity + Vector3.up * _verticalVelocity;
        _controller.Move(motion * Time.deltaTime);
    }

    static float NormalizePitch(float eulerX)
    {
        // localEulerAngles reports 0..360; pitch math wants -180..180.
        return eulerX > 180f ? eulerX - 360f : eulerX;
    }
}
