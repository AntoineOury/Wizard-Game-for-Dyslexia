# Player Controls: Laptop / Touch

One toggle switches the whole input scheme:

| | **Laptop (Desktop)** | **Touch** |
| --- | --- | --- |
| Move | WASD / arrows, Shift = sprint | Virtual joystick (bottom-left); push to the rim = sprint |
| Look | Mouse (cursor locked) | Drag anywhere on the right of the screen |
| Cursor | Locked; **Escape** toggles UI mode for clicking buttons | Always free |
| First-launch default | Desktop on PC/Mac | Touch on phones/tablets (`Application.isMobilePlatform`) |

The choice is saved in PlayerPrefs, so it survives scene switches and restarts.
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

**3. Nothing else.** The touch joystick/look surface is built at runtime by
`TouchControls` the moment the Touch scheme is active — no prefabs, no canvas
authoring, and it hides itself again in Desktop mode.

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
- No jump, matching the old controls. Add `jumpHeight` + a touch button later
  if the design ever wants it.
