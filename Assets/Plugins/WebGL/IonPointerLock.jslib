// Pointer lock bridge (art bible §9.3). The page owns the lock: the controller lives in the WebGL
// template (window.ionPointerLock, Assets/WebGLTemplates/Ion/index.html) so it can request the lock
// synchronously inside the browser's own mousedown handler (a user gesture), which Unity's next-frame
// Cursor.lockState request cannot do reliably. Unity only arms it, polls its state and asks for releases.
// Every function degrades to a harmless default when the page has no controller (another template).
mergeInto(LibraryManager.library, {
  // 1 = the game wants a click on the canvas to lock the pointer (title / resume overlay up).
  IonPL_SetArmed: function (armed) {
    var c = window.ionPointerLock;
    if (c) c.setArmed(!!armed);
  },

  // Click regions in Unity screen pixels (origin bottom-left). kind 0 = block (a click there never locks:
  // the settings card), kind 1 = allow (when any allow rect exists, only clicks inside one lock: the end
  // card's "Play again" button). slot < 0 clears every rect.
  IonPL_SetRect: function (slot, kind, x0, y0, x1, y1) {
    var c = window.ionPointerLock;
    if (c) c.setRect(slot, kind, x0, y0, x1, y1);
  },

  // 1 while the browser's pointer lock is on the game canvas.
  IonPL_IsLocked: function () {
    var c = window.ionPointerLock;
    if (c) return c.isLocked() ? 1 : 0;
    var canvas = Module['canvas'];
    return document.pointerLockElement && document.pointerLockElement === canvas ? 1 : 0;
  },

  // Monotonic counters (Unity compares them with the last values it saw, so nothing is missed between
  // frames). which: 0 = locks acquired, 1 = locks lost, 2 = focus lost (blur / tab hidden), 3 = lock errors.
  IonPL_Serial: function (which) {
    var c = window.ionPointerLock;
    return c ? c.serial(which) : 0;
  },

  // Milliseconds since the lock was last lost (-1 if it never was). Chrome refuses a re-lock for ~1 s.
  IonPL_MsSinceUnlock: function () {
    var c = window.ionPointerLock;
    return c ? c.msSinceUnlock() : -1;
  },

  // 1 while the page's loading card still covers the canvas.
  IonPL_LoaderVisible: function () {
    var l = document.getElementById('ion-loader');
    return (l && !l.hidden && !l.classList.contains('done')) ? 1 : 0;
  },

  // Deliberate release (settings, end card). Clicking re-locks only when armed again.
  IonPL_Release: function () {
    var c = window.ionPointerLock;
    if (c) { c.release(); return; }
    if (document.exitPointerLock && document.pointerLockElement) document.exitPointerLock();
  },

  // The game has rendered its first frame: the loading card may offer "Click to play".
  IonPL_SetReady: function () {
    var c = window.ionPointerLock;
    if (c) c.setReady();
  }
});
