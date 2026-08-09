using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class FirstPersonCam : MonoBehaviour
{

    public float sensX;
    public float sensY;

    public Transform orientation;

    float rotationX;
    float rotationY;


    // Start is called before the first frame update
    void Start()
    {
        SetUiMode(false);
    }

    // Update is called once per frame
    void Update()
    {
        // Escape flips between look mode and UI mode. Without this the locked,
        // hidden cursor can never reach the menu buttons, and clicks go nowhere.
        if (Input.GetKeyDown(KeyCode.Escape))
            SetUiMode(Cursor.lockState == CursorLockMode.Locked);
        if (Cursor.lockState != CursorLockMode.Locked) return;

        float mouseX = Input.GetAxisRaw("Mouse X") * Time.deltaTime * sensX;
        float mouseY = Input.GetAxisRaw("Mouse Y") * Time.deltaTime * sensY;

        rotationY += mouseX;
        rotationX -= mouseY;

        // Clamp the vertical rotation to prevent flipping
        rotationX = Mathf.Clamp(rotationX, -75f, 50f);

        // Apply the rotations
        transform.rotation = Quaternion.Euler(rotationX, rotationY, 0f);
        orientation.rotation = Quaternion.Euler(0f, rotationY, 0f);
    }

    static void SetUiMode(bool uiMode)
    {
        Cursor.lockState = uiMode ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = uiMode;
    }
}
