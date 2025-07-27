using System.Collections;
using System.Collections.Generic;
// using System.Numerics;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed;

    public float groundDrag;

    [Header("Ground Check")]
    public LayerMask Ground;
    public float playerHeight;
    bool isGrounded;

    public Transform orientation;

    float horizontalInput;
    float verticalInput;

    Vector3 inputDirection;
    Rigidbody rb;


    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true; // Prevent the Rigidbody from rotating
    }


    private void Update()
    {
        isGrounded = Physics.Raycast(transform.position, Vector3.down, playerHeight * 0.5f + 0.3f, Ground);
        PlayerInput();
        SpeedControl();

        if (isGrounded)
        {
            rb.drag = groundDrag; // Apply ground drag when grounded
        }
        else
        {
            rb.drag = 0; // No drag when in the air
        }
    }

    private void FixedUpdate()
    {
        MovePlayer();
    }

    private void PlayerInput()
    {
        horizontalInput = Input.GetAxisRaw("Horizontal");
        verticalInput = Input.GetAxisRaw("Vertical");
    }
    private void MovePlayer()
    {
        // Calculate the input direction based on the orientation
        inputDirection = (orientation.forward * verticalInput + orientation.right * horizontalInput).normalized;

        rb.AddForce(inputDirection * moveSpeed * 10f, ForceMode.Force);
    }
    
    private void SpeedControl()
    {
        // Limit the speed of the player
        Vector3 flatVel = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
        if (flatVel.magnitude > moveSpeed)
        {
            Vector3 limitedVel = flatVel.normalized * moveSpeed;
            rb.velocity = new Vector3(limitedVel.x, rb.velocity.y, limitedVel.z);
        }
    }
}

