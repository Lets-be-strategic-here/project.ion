#if UNITY_WEBGL && !UNITY_EDITOR
#define ION_WEB_LOCK
using System.Runtime.InteropServices;
#endif
using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ion.Gameplay
{
    /// <summary>
    /// Pointer-lock state, owned by the browser (art bible §9.3). On the Web player the page's controller
    /// (WebGL template + IonPointerLock.jslib) locks synchronously inside the click's own mousedown handler;
    /// Unity polls the result once per frame and mirrors it into <see cref="Cursor.lockState"/>.
    /// Cursor.lockState is NOT the source of truth on the web. In the Editor / standalone the cursor lock is
    /// used directly. Tests drive <see cref="Simulate"/>.
    ///
    /// Everything is evaluated lazily, once per frame, on first use (no execution-order dependency).
    /// </summary>
    public static class PointerLock
    {
        /// <summary>Chrome refuses requestPointerLock for about a second after the user pressed Esc.</summary>
        public const float ReLockCooldown = 1.0f;

#if ION_WEB_LOCK
        [DllImport("__Internal")] static extern void IonPL_SetArmed(int armed);
        [DllImport("__Internal")] static extern void IonPL_SetRect(int slot, int kind, float x0, float y0, float x1, float y1);
        [DllImport("__Internal")] static extern int IonPL_IsLocked();
        [DllImport("__Internal")] static extern int IonPL_Serial(int which);
        [DllImport("__Internal")] static extern int IonPL_MsSinceUnlock();
        [DllImport("__Internal")] static extern int IonPL_LoaderVisible();
        [DllImport("__Internal")] static extern void IonPL_Release();
        [DllImport("__Internal")] static extern void IonPL_SetReady();
#endif

        static int s_Frame = -1;
        static bool s_Locked;
        static int s_LockSerial;
        static int s_SeenLock, s_SeenUnlock, s_SeenBlur, s_SeenError;
        static int s_LockFrame = -1000;
        static float s_UnlockTime = -1000f;
        static bool s_Armed, s_ArmedSent, s_ReadySent;
        static bool s_Errored;
        static bool s_LoaderVisible;

        // Simulation (tests / automation).
        static bool s_Simulated, s_SimLocked;
        static int s_SimBlur, s_SimError;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Frame = -1;
            s_Locked = false;
            s_LockSerial = 0;
            s_SeenLock = s_SeenUnlock = s_SeenBlur = s_SeenError = 0;
            s_LockFrame = -1000;
            s_UnlockTime = -1000f;
            s_Armed = s_ArmedSent = s_ReadySent = false;
            s_Errored = false;
            s_LoaderVisible = false;
            s_Simulated = s_SimLocked = false;
            s_SimBlur = s_SimError = 0;
            Changed = null;
            FocusLost = null;
        }

        /// <summary>Raised when the lock is acquired (true) or lost (false), on the frame Unity notices.</summary>
        public static event Action<bool> Changed;

        /// <summary>The page lost focus (alt-tab, tab hidden): held input must be forgotten.</summary>
        public static event Action FocusLost;

        /// <summary>True while the pointer is locked to the game.</summary>
        public static bool IsLocked { get { Tick(); return s_Locked; } }

        /// <summary>Frames since the lock was last acquired (large when never / not locked).</summary>
        public static int FramesSinceLock { get { Tick(); return s_Locked ? Time.frameCount - s_LockFrame : int.MaxValue; } }

        /// <summary>Real seconds since the lock was last lost (large if it never was).</summary>
        public static float SecondsSinceUnlock
        {
            get
            {
                Tick();
#if ION_WEB_LOCK
                if (!s_Simulated)
                {
                    int ms = SafeMsSinceUnlock();
                    return ms < 0 ? 1000f : ms / 1000f;
                }
#endif
                return Time.realtimeSinceStartup - s_UnlockTime;
            }
        }

        /// <summary>
        /// "Click to resume" may be offered: unlocked, and either the browser's re-lock cooldown has passed
        /// or the last request failed (then the next click simply retries).
        /// </summary>
        public static bool ResumeReady
        {
            get
            {
                if (IsLocked) return false;
                return s_Errored || SecondsSinceUnlock >= ReLockCooldown;
            }
        }

        /// <summary>0..1 progress of the re-lock cooldown (1 = ready). For the overlay's resume ring.</summary>
        public static float ResumeProgress => IsLocked ? 1f : (s_Errored ? 1f : Mathf.Clamp01(SecondsSinceUnlock / ReLockCooldown));

        /// <summary>True when the last lock request was refused by the browser (cleared on the next lock).</summary>
        public static bool LastRequestFailed { get { Tick(); return s_Errored; } }

        /// <summary>True while the web page's loading card is still on screen (false outside the Web player).</summary>
        public static bool LoaderVisible { get { Tick(); return s_LoaderVisible; } }

        /// <summary>Lock ever acquired this session (the title becomes "Click to resume").</summary>
        public static bool HasEverLocked { get { Tick(); return s_LockSerial > 0; } }

        /// <summary>
        /// Arms the page: while armed, a left click on the canvas (outside blocked rects) locks the pointer
        /// synchronously. The click-to-play overlay arms; the settings panel and end card shape it with rects.
        /// </summary>
        public static void SetArmed(bool armed)
        {
            s_Armed = armed;
#if ION_WEB_LOCK
            if (s_ArmedSent == armed && s_ReadySent) return;
            s_ArmedSent = armed;
            if (!s_ReadySent)
            {
                s_ReadySent = true;
                try { IonPL_SetReady(); } catch { }
            }
            try { IonPL_SetArmed(armed ? 1 : 0); } catch { }
#endif
        }

        public static bool Armed => s_Armed;

        public enum RectKind { Block = 0, Allow = 1 }

        /// <summary>
        /// Sets click region <paramref name="slot"/> (0..7) in screen pixels (origin bottom-left, like
        /// Input mouse positions). A null rect clears the slot.
        /// </summary>
        public static void SetRect(int slot, RectKind kind, Rect? screenRect)
        {
#if ION_WEB_LOCK
            try
            {
                if (screenRect.HasValue)
                {
                    Rect r = screenRect.Value;
                    IonPL_SetRect(slot, (int)kind, r.xMin, r.yMin, r.xMax, r.yMax);
                }
                else
                {
                    IonPL_SetRect(slot, (int)kind, 0f, 0f, -1f, -1f);
                }
            }
            catch { }
#endif
        }

        /// <summary>
        /// Locks now where the platform allows it from script (Editor / standalone). On the web this is a
        /// no-op: the page locks inside the click itself (arm with <see cref="SetArmed"/>).
        /// </summary>
        public static void Request()
        {
            if (s_Simulated) { Simulate(true); return; }
#if !ION_WEB_LOCK
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
#endif
        }

        /// <summary>Deliberate release (settings panel, end card). Re-locking needs a click.</summary>
        public static void Release()
        {
            if (s_Simulated) { Simulate(false); return; }
#if ION_WEB_LOCK
            try { IonPL_Release(); } catch { }
#endif
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // ---------------------------------------------------------------- simulation (tests)

        /// <summary>True while <see cref="Simulate"/> drives the state (tests).</summary>
        public static bool IsSimulated => s_Simulated;

        /// <summary>Tests: take over the lock state (true = locked). Takes effect on the next query.</summary>
        public static void Simulate(bool locked)
        {
            if (!s_Simulated)
            {
                // Start the simulated counters where the real ones are (no phantom blur / error).
                s_SimBlur = s_SeenBlur;
                s_SimError = s_SeenError;
            }
            s_Simulated = true;
            s_SimLocked = locked;
            s_Frame = -1; // re-evaluate now
        }

        /// <summary>Tests: a simulated window blur (also drops a simulated lock, like the page does).</summary>
        public static void SimulateBlur()
        {
            if (!s_Simulated) { s_SimBlur = s_SeenBlur; s_SimError = s_SeenError; s_SimLocked = s_Locked; }
            s_Simulated = true;
            s_SimLocked = false;
            s_SimBlur++;
            s_Frame = -1;
        }

        /// <summary>Tests: a simulated pointerlockerror.</summary>
        public static void SimulateError()
        {
            if (!s_Simulated) { s_SimBlur = s_SeenBlur; s_SimError = s_SeenError; s_SimLocked = s_Locked; }
            s_Simulated = true;
            s_SimError++;
            s_Frame = -1;
        }

        /// <summary>Tests: back to the real platform state.</summary>
        public static void EndSimulation()
        {
            s_Simulated = false;
            s_Frame = -1;
        }

        // ---------------------------------------------------------------- per-frame evaluation

        /// <summary>Evaluates the state once per frame (cheap; safe to call from anywhere).</summary>
        public static void Tick()
        {
            int frame = Time.frameCount;
            if (frame == s_Frame) return;
            s_Frame = frame;

            bool locked;
            int lockSerial, unlockSerial, blurSerial, errorSerial;
            if (s_Simulated)
            {
                locked = s_SimLocked;
                lockSerial = s_SeenLock + (locked && !s_Locked ? 1 : 0);
                unlockSerial = s_SeenUnlock + (!locked && s_Locked ? 1 : 0);
                blurSerial = s_SimBlur;
                errorSerial = s_SimError;
                s_LoaderVisible = false;
            }
            else
            {
#if ION_WEB_LOCK
                locked = SafeIsLocked();
                lockSerial = SafeSerial(0);
                unlockSerial = SafeSerial(1);
                blurSerial = SafeSerial(2);
                errorSerial = SafeSerial(3);
                s_LoaderVisible = SafeLoaderVisible();
                // Unity follows the browser (its own deferred lock requests are never used).
                if (locked && Cursor.lockState != CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
                else if (!locked && Cursor.lockState == CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
#else
                var kb = Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame && Cursor.lockState == CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                locked = Cursor.lockState == CursorLockMode.Locked;
                lockSerial = s_SeenLock + (locked && !s_Locked ? 1 : 0);
                unlockSerial = s_SeenUnlock + (!locked && s_Locked ? 1 : 0);
                blurSerial = s_SeenBlur + (s_FocusLostPending ? 1 : 0);
                s_FocusLostPending = false;
                errorSerial = s_SeenError;
                s_LoaderVisible = false;
#endif
            }

            bool was = s_Locked;
            bool gained = lockSerial != s_SeenLock || (locked && !was);
            bool lost = unlockSerial != s_SeenUnlock || (!locked && was);
            bool blurred = blurSerial != s_SeenBlur;
            bool errored = errorSerial != s_SeenError;
            s_SeenLock = lockSerial;
            s_SeenUnlock = unlockSerial;
            s_SeenBlur = blurSerial;
            s_SeenError = errorSerial;
            if (gained) s_LockSerial++;

            s_Locked = locked;
            if (errored) s_Errored = true;
            if (gained && locked)
            {
                s_LockFrame = frame;
                s_Errored = false;
            }
            if (lost) s_UnlockTime = Time.realtimeSinceStartup;

            // Several transitions between two frames are replayed in a consistent order, ending in the
            // current state (the click gate re-arms on every acquisition).
            if (locked)
            {
                if (lost && was) SafeInvoke(false);
                if (gained) SafeInvoke(true);
            }
            else
            {
                if (gained) SafeInvoke(true);
                if (lost) SafeInvoke(false);
            }
            if (blurred) SafeFocusLost();
        }

#if !ION_WEB_LOCK
        static bool s_FocusLostPending;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void HookFocus()
        {
            Application.focusChanged -= OnFocusChanged;
            Application.focusChanged += OnFocusChanged;
        }

        static void OnFocusChanged(bool focused)
        {
            if (!focused) s_FocusLostPending = true;
        }
#endif

        static void SafeInvoke(bool locked)
        {
            try { Changed?.Invoke(locked); }
            catch (Exception e) { Debug.LogException(e); }
        }

        static void SafeFocusLost()
        {
            try { FocusLost?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

#if ION_WEB_LOCK
        static bool SafeIsLocked()
        {
            try { return IonPL_IsLocked() != 0; }
            catch { return false; }
        }

        static bool SafeLoaderVisible()
        {
            try { return IonPL_LoaderVisible() != 0; }
            catch { return false; }
        }

        static int SafeSerial(int which)
        {
            try { return IonPL_Serial(which); }
            catch { return 0; }
        }

        static int SafeMsSinceUnlock()
        {
            try { return IonPL_MsSinceUnlock(); }
            catch { return -1; }
        }
#endif
    }
}
