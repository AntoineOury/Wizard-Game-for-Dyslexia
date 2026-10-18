// Ground-truth pointer position for WebGL builds, straight from the DOM.
//
// Why this exists: Unity's Input.mousePosition in browser builds has proven
// unreliable on scaled displays (ink landing shrunken and offset during
// letter tracing while the same build's UI worked) — the exact scaling it
// applies between CSS pixels and the canvas backing buffer varies with
// browser, display scaling and Unity version. The browser itself always
// knows the truth: pointer events carry client coordinates, and
// getBoundingClientRect() gives the canvas's CSS rect. Their ratio is a
// normalized 0..1 canvas position that is correct on every device, every
// display scale, every browser — which is all a world-space ray needs.
//
// One stream (pointer events) covers mouse, touch and pen alike.

mergeInto(LibraryManager.library, {

  OtherwisePointer_Start: function () {
    if (window.__otherwisePointer) return;
    var state = window.__otherwisePointer = { x: 0.5, y: 0.5, has: false };
    var canvas = document.querySelector("#unity-canvas") ||
                 document.getElementsByTagName("canvas")[0];
    if (!canvas) return;
    var update = function (e) {
      var rect = canvas.getBoundingClientRect();
      if (rect.width <= 0 || rect.height <= 0) return;
      state.x = (e.clientX - rect.left) / rect.width;
      state.y = 1 - (e.clientY - rect.top) / rect.height;   // GL-style: 0 at bottom
      state.has = true;
    };
    canvas.addEventListener("pointerdown", update, true);
    canvas.addEventListener("pointermove", update, true);
  },

  OtherwisePointer_HasData: function () {
    var state = window.__otherwisePointer;
    return (state && state.has) ? 1 : 0;
  },

  OtherwisePointer_X: function () {
    var state = window.__otherwisePointer;
    return state ? state.x : 0.5;
  },

  OtherwisePointer_Y: function () {
    var state = window.__otherwisePointer;
    return state ? state.y : 0.5;
  }
});
