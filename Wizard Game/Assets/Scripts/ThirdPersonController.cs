using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Third-person movement with an orbit camera, for both control schemes:
/// desktop (WASD moves relative to the camera, mouse orbits, Space jumps) and
/// touch (the same joystick / look surface / jump button as first person).
///
/// Lives on the same player object as FirstPersonController — they share the
/// CharacterController and the camera, and each drives only while its view
/// mode (PlayerViewMode) is active, so a UI toggle switches views on the fly.
///
/// Classic third-person feel: the character turns to face where it is going
/// rather than where the camera looks, and the camera is pulled in by a
/// spherecast so hills, trees and buildings never hide the player.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[AddComponentMenu("Otherwise Labs/Third Person Controller")]
public class ThirdPersonController : MonoBehaviour
{
    const float CoyoteTime = 0.12f;

    [Header("References")]
    [Tooltip("Camera to orbit. Auto-finds a child Camera when empty. Shared with FirstPersonController.")]
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
    [Tooltip("Degrees per second the character turns to face its movement direction.")]
    [Min(30f)] public float turnSpeed = 540f;

    [Header("Camera Orbit")]
    [Min(1f)] public float cameraDistance = 5f;
    [Tooltip("Height above the player's pivot that the camera looks at (roughly the head).")]
    public float pivotHeight = 1.6f;
    public float minPitch = -30f;
    public float maxPitch = 65f;
    [Tooltip("Radius of the collision probe that keeps the camera out of terrain and props.")]
    [Min(0.05f)] public float cameraCollisionRadius = 0.3f;

    [Tooltip("Closest the camera may ever get to the player. Stops it entering the body when something crowds it from behind.")]
    [Min(0.2f)] public float minCameraDistance = 1.4f;

    [Tooltip("How fast the camera eases back out after an obstruction clears, in units per second. Pulling in is always instant.")]
    [Min(0.1f)] public float cameraReturnSpeed = 6f;

    [Header("Look")]
    [Tooltip("Degrees per mouse delta unit.")]
    [Min(0.01f)] public float mouseSensitivity = 2f;
    [Tooltip("Degrees per percent of screen height dragged on the look area.")]
    [Min(0.01f)] public float touchSensitivity = 2f;

    readonly List<Renderer> _bodyRenderers = new List<Renderer>();
    readonly RaycastHit[] _cameraHits = new RaycastHit[16];
    CharacterController _controller;
    float _currentCameraDistance;
    Vector3 _horizontalVelocity;
    float _verticalVelocity;
    float _yaw;
    float _pitch = 15f;
    float _lastGroundedTime = float.NegativeInfinity;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        if (cameraTransform == null)
        {
            Camera childCamera = GetComponentInChildren<Camera>();
            if (childCamera != null) cameraTransform = childCamera.transform;
        }
        _yaw = transform.eulerAngles.y;
        _currentCameraDistance = cameraDistance;

        PlayerRig.IgnoreSelfColliders(_controller);
    }

    void Update()
    {
        if (PlayerViewMode.Current != ViewModeKind.ThirdPerson) return;

        // The body is the whole point of third person.
        PlayerRig.SetBodyVisible(transform, true, _bodyRenderers);

        ControlSchemeKind scheme = PlayerControlScheme.Current;
        if (scheme == ControlSchemeKind.Touch) TouchControls.EnsureExists();

        ApplyCursorPolicy(scheme);
        HandleOrbitInput(scheme);

        if (PlayerTerrainSpawner.SpawnPending)
        {
            _verticalVelocity = 0f;
            _horizontalVelocity = Vector3.zero;
            return;
        }

        HandleMove(scheme);
    }

    // Camera follows in LateUpdate so it always sees this frame's final
    // character position, not last frame's.
    void LateUpdate()
    {
        if (PlayerViewMode.Current != ViewModeKind.ThirdPerson) return;
        PositionCamera();
    }

    void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        PlayerRig.SetBodyVisible(transform, true, _bodyRenderers);
    }

    void ApplyCursorPolicy(ControlSchemeKind scheme)
    {
        if (scheme == ControlSchemeKind.Touch)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape)) PlayerControlScheme.UiMode = !PlayerControlScheme.UiMode;
        Cursor.lockState = PlayerControlScheme.UiMode ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = PlayerControlScheme.UiMode;

        // Belt and braces for every button, present and future: while in look
        // mode nothing may stay "selected", or Space (the UI Submit key) would
        // re-press it instead of only jumping.
        if (!PlayerControlScheme.UiMode && EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    void HandleOrbitInput(ControlSchemeKind scheme)
    {
        Vector2 look = Vector2.zero;
        if (scheme == ControlSchemeKind.Desktop)
        {
            if (!PlayerControlScheme.UiMode)
                look = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * mouseSensitivity;
        }
        else
        {
            look = TouchControls.ConsumeLookDelta() * touchSensitivity;
        }

        _yaw += look.x;
        _pitch = Mathf.Clamp(_pitch - look.y, minPitch, maxPitch);
    }

    void HandleMove(ControlSchemeKind scheme)
    {
        Vector2 moveInput;
        bool sprint;
        bool jumpPressed;
        if (scheme == ControlSchemeKind.Desktop)
        {
            moveInput = PlayerControlScheme.UiMode
                ? Vector2.zero
                : new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            sprint = Input.GetKey(KeyCode.LeftShift);
            jumpPressed = !PlayerControlScheme.UiMode && Input.GetKeyDown(KeyCode.Space);
        }
        else
        {
            moveInput = TouchControls.Move;
            sprint = moveInput.magnitude > 0.92f;
            jumpPressed = TouchControls.ConsumeJump();
        }

        if (moveInput.sqrMagnitude > 1f) moveInput.Normalize();

        // Movement is relative to the camera's orbit yaw, and the character
        // turns to face travel — where the camera looks and where the body
        // points are independent, which is what makes it read as third person.
        Quaternion orbitYaw = Quaternion.Euler(0f, _yaw, 0f);
        Vector3 wishDirection = orbitYaw * new Vector3(moveInput.x, 0f, moveInput.y);

        if (wishDirection.sqrMagnitude > 0.0001f)
        {
            Quaternion facing = Quaternion.LookRotation(wishDirection.normalized);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, turnSpeed * Time.deltaTime);
        }

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
            // Small downward bias keeps the controller glued to slopes.
            _verticalVelocity = -3f;
        }
        _verticalVelocity -= gravity * Time.deltaTime;

        Vector3 motion = _horizontalVelocity + Vector3.up * _verticalVelocity;
        _controller.Move(motion * Time.deltaTime);
    }

    void PositionCamera()
    {
        if (cameraTransform == null) return;

        Vector3 pivot = transform.position + Vector3.up * pivotHeight;
        Quaternion orbit = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 back = orbit * Vector3.back;

        // Pull the camera in when something solid sits between it and the
        // player, ignoring the player's own body. PhysX reports distance 0 for
        // a cast that begins inside a collider, so a single SphereCast would
        // hit the player first and jam the camera at its minimum — the flicker
        // between hitting and not hitting is what causes the dizziness. Scan
        // every hit instead and discard our own.
        float target = cameraDistance;
        int count = Physics.SphereCastNonAlloc(pivot, cameraCollisionRadius, back, _cameraHits,
            cameraDistance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (_cameraHits[i].distance <= 0f) continue;                       // started inside: no usable normal
            if (PlayerRig.IsSelfCollider(_cameraHits[i].collider, transform)) continue;
            target = Mathf.Min(target, _cameraHits[i].distance);
        }
        target = Mathf.Max(minCameraDistance, target);

        // Snap inwards so nothing ever clips through the camera, ease back out
        // so a passing tree does not fling the view.
        _currentCameraDistance = target < _currentCameraDistance
            ? target
            : Mathf.MoveTowards(_currentCameraDistance, target, cameraReturnSpeed * Time.deltaTime);
        float distance = _currentCameraDistance;

        // World-space assignment deliberately overrides the parent hierarchy:
        // the camera stays a child of the player, but the orbit math decides
        // its final pose every frame. FirstPersonController re-asserts its own
        // local pose the same way, so switching views never leaves stale state.
        cameraTransform.position = pivot + back * distance;
        cameraTransform.rotation = orbit;
    }
}
