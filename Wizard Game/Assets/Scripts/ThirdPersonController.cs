using UnityEngine;

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
    [Min(0f)] public float jumpHeight = 1.2f;
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

    [Header("Look")]
    [Tooltip("Degrees per mouse delta unit.")]
    [Min(0.01f)] public float mouseSensitivity = 2f;
    [Tooltip("Degrees per percent of screen height dragged on the look area.")]
    [Min(0.01f)] public float touchSensitivity = 2f;

    CharacterController _controller;
    Vector3 _horizontalVelocity;
    float _verticalVelocity;
    float _yaw;
    float _pitch = 15f;
    float _lastGroundedTime = float.NegativeInfinity;
    bool _uiMode;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        if (cameraTransform == null)
        {
            Camera childCamera = GetComponentInChildren<Camera>();
            if (childCamera != null) cameraTransform = childCamera.transform;
        }
        _yaw = transform.eulerAngles.y;
    }

    void Update()
    {
        if (PlayerViewMode.Current != ViewModeKind.ThirdPerson) return;

        ControlSchemeKind scheme = PlayerControlScheme.Current;
        if (scheme == ControlSchemeKind.Touch) TouchControls.EnsureExists();

        ApplyCursorPolicy(scheme);
        HandleOrbitInput(scheme);
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
    }

    void ApplyCursorPolicy(ControlSchemeKind scheme)
    {
        if (scheme == ControlSchemeKind.Touch)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape)) _uiMode = !_uiMode;
        Cursor.lockState = _uiMode ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = _uiMode;
    }

    void HandleOrbitInput(ControlSchemeKind scheme)
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
            moveInput = _uiMode
                ? Vector2.zero
                : new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            sprint = Input.GetKey(KeyCode.LeftShift);
            jumpPressed = !_uiMode && Input.GetKeyDown(KeyCode.Space);
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
        // player. The cast starts inside the player's own capsule, and a
        // spherecast ignores colliders it starts inside, so the player never
        // blocks their own camera.
        float distance = cameraDistance;
        if (Physics.SphereCast(pivot, cameraCollisionRadius, back, out RaycastHit hit,
                cameraDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            distance = Mathf.Max(0.5f, hit.distance);
        }

        // World-space assignment deliberately overrides the parent hierarchy:
        // the camera stays a child of the player, but the orbit math decides
        // its final pose every frame. FirstPersonController re-asserts its own
        // local pose the same way, so switching views never leaves stale state.
        cameraTransform.position = pivot + back * distance;
        cameraTransform.rotation = orbit;
    }
}
