# Player Controls: Laptop / Touch

One toggle switches the whole input scheme:

| | **Laptop (Desktop)** | **Touch** |
| --- | --- | --- |
| Move | WASD / arrows, Shift = sprint | Virtual joystick (bottom-left); push to the rim = sprint |
| Look | Mouse (cursor locked) | Drag anywhere on the right of the screen |
| Jump | **Space** | **JUMP button** (bottom-right) |
| Cursor | Locked; **Escape** toggles UI mode for clicking buttons | Always free |
| First-launch default | Desktop on PC/Mac | Touch on phones/tablets (`Application.isMobilePlatform`) |

The choice is saved in PlayerPrefs, so it survives scene switches and restarts.

**UI focus is shared and stateless.** Escape suspends gameplay input so the
cursor can click buttons; pressing ANY toggle drops you straight back into
gameplay (cursor locked in Laptop mode). One flag serves both view
controllers, so switching views or schemes can never strand a controller in
UI mode with a dead keyboard — press Escape again whenever you want the
cursor back.
Touch mode is fully testable on a laptop: the on-screen controls run through
the EventSystem's pointer events, and the mouse drives those exactly like a
finger — click-drag the joystick, click-drag the right half to look.

## Scene setup (one-time)

**1. Player object** — replace the old movement pair:

- On the player root: remove **PlayerMovement**, the **Rigidbody**, and any
  capsule collider. Add **First Person Controller** (menu: Otherwise Labs).
  A CharacterController is added automatically — adjust its Height/Center to
  match the old capsule if needed.
- On the camera child: remove **FirstPersonCam**. The controller drives pitch;
  its Camera Transform field auto-finds a child camera when left empty.
- The `orientation` transform the old scripts needed is no longer used.

**2. Toggle button** — duplicate your main-menu Button, place it next to the
original, rename it, and add the **ControlSchemeToggleButton** component. That
is all: it wires its own onClick and keeps its label showing the active mode
("Controls: Laptop" / "Controls: Touch"). Works with TMP and legacy Text.

**3. View toggle + third person (optional)** — duplicate a button once more,
empty its On Click () list, and add **ViewModeToggleButton**. Then add
**Third Person Controller** to the player root — the *same object* as First
Person Controller. Both stay enabled; they share the CharacterController and
camera, and only the controller matching the active view mode drives. Camera
view and control scheme are independent toggles: all four combinations work.

**4. Nothing else.** The touch joystick, look surface and JUMP button are
built at runtime by `TouchControls` the moment the Touch scheme is active —
no prefabs, no canvas authoring, and they hide again in Desktop mode.

## Third person

Classic mainstream feel: movement is relative to the camera and the character
turns to face where it is going; the mouse or look-drag orbits the camera
around the player. A spherecast pulls the camera in when terrain or props sit
between it and the player, so the player is never hidden. Orbit distance,
pivot height, pitch limits and collision radius are Inspector fields on the
Third Person Controller.

Switching views is instant and stateless: first person re-asserts the camera's
authored head pose every frame, third person writes the orbit pose every
frame, so the toggle just changes which one wins.

## Why the button did nothing before

`FirstPersonCam` locked and hid the cursor on Start, so no pointer could ever
reach the button — no event, no error. Two fixes shipped together:

- `FirstPersonCam` (legacy, superseded by the controller) now toggles UI mode
  with **Escape** instead of locking forever.
- `SceneSwitchingManager.SceneSwitch` releases the cursor before loading, so
  the menu scene never arrives with an invisible cursor.

## Improvements in FirstPersonController over the old pair

- **CharacterController, not Rigidbody forces**: real slope limit and step
  offset, no sliding, no ground LayerMask to configure — which also means it
  works on streamed terrain chunks out of the box (the old ground raycast
  only matched layers listed in its mask).
- **Framerate-independent look**: the old script multiplied mouse deltas by
  `Time.deltaTime`, making look speed depend on FPS. Mouse deltas are already
  per-frame; the new controller uses them directly.
- **Single component** owning move + look + cursor policy, scheme-aware.
- **Jump** on Space / the touch JUMP button, with a short coyote window
  (jumps pressed a hair after stepping off a ledge still fire). Set
  `jumpHeight` to 0 on either controller to disable jumping.
