// Opens a URL in a new tab. Browsers only allow window.open inside a user gesture, and Unity
// polls input a frame late, so we defer the open to the next pointerup/keyup (a real gesture).
mergeInto(LibraryManager.library, {
  IonOpenUrl: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    if (!url) return;

    if (window.__ionPendingOpen) {
      // Replace an earlier, not-yet-fired request instead of opening two tabs.
      window.__ionPendingOpen.url = url;
      return;
    }

    var pending = { url: url };
    var fire = function () {
      document.removeEventListener('pointerup', fire, true);
      document.removeEventListener('keyup', fire, true);
      window.__ionPendingOpen = null;
      if (document.exitPointerLock && document.pointerLockElement) document.exitPointerLock();
      window.open(pending.url, '_blank', 'noopener');
    };
    window.__ionPendingOpen = pending;
    document.addEventListener('pointerup', fire, true);
    document.addEventListener('keyup', fire, true);
  }
});
