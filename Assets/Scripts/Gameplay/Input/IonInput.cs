using Ion.Presentation.Motion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ion.Gameplay
{
    /// <summary>
    /// The game's gated view of the mouse and the raise key (art bible §9.2, §9.3):
    ///  * Raise = Shift (left or right) held, or toggled when Settings says "Raise: Toggle"; RMB hold stays as
    ///    an unadvertised alternative. Only while the pointer is locked.
    ///  * The click that acquired the lock never acts: after a lock, mouse buttons are ignored until every
    ///    button is up and at least 2 frames have passed.
    ///  * Mouse look is dropped for 2 frames after a lock and clamped to 300 px per frame (Chrome sometimes
    ///    reports huge spurious deltas around lock changes).
    ///  * Focus loss (alt-tab, hidden tab) forgets everything held: a Shift held across an alt-tab must not
    ///    leave the photo raised. Keys must be released before they count again.
    /// Evaluated lazily once per frame on first use. Tests drive it through the Simulated* fields.
    /// </summary>
    public static class IonInput
    {
        public const int GateFrames = 2;
        public const float MaxDeltaPx = 300f;

        static int s_Frame = -1;
        static bool s_Hooked;

        static bool s_MouseGated;      // after a lock: wait for all buttons up + GateFrames
        static int s_GateFrame;
        static bool s_ShiftNeedsRelease, s_RmbNeedsRelease;
        static bool s_Toggled;
        static bool s_Raise, s_RaisePressed, s_Primary, s_PrimaryHeld;
        static Vector2 s_Look;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Frame = -1;
            s_Hooked = false;
            s_MouseGated = false;
            s_GateFrame = 0;
            s_ShiftNeedsRelease = s_RmbNeedsRelease = false;
            s_Toggled = false;
            s_Raise = s_RaisePressed = s_Primary = s_PrimaryHeld = false;
            s_Look = Vector2.zero;
            SimulatedRaise = SimulatedPrimaryDown = false;
            SimulatedLook = Vector2.zero;
            SimulatedRotate = 0f;
            UseSimulatedDevices = false;
            _simPrimaryWas = false;
        }

        // ---------------------------------------------------------------- simulation (tests)

        /// <summary>Tests: read the Simulated* fields instead of the keyboard and mouse.</summary>
        public static bool UseSimulatedDevices;
        /// <summary>Tests: "Shift is held".</summary>
        public static bool SimulatedRaise;
        /// <summary>Tests: "the left mouse button is down".</summary>
        public static bool SimulatedPrimaryDown;
        /// <summary>Tests: mouse delta (pixels) this frame.</summary>
        public static Vector2 SimulatedLook;
        /// <summary>Tests: rotate keys held (+1 = Q, −1 = E, 0 = none).</summary>
        public static float SimulatedRotate;
        static bool _simPrimaryWas;

        // ---------------------------------------------------------------- queries

        /// <summary>Gameplay input is live: the pointer is locked to the game (or simulated).</summary>
        public static bool Active { get { Tick(); return PointerLock.IsLocked; } }

        /// <summary>The player wants the photo / viewfinder up this frame.</summary>
        public static bool RaiseHeld { get { Tick(); return s_Raise; } }

        /// <summary>Raise went from off to on this frame.</summary>
        public static bool RaisePressedThisFrame { get { Tick(); return s_RaisePressed; } }

        /// <summary>A left click that may act (place / shoot) this frame. Never the click that locked.</summary>
        public static bool PrimaryPressedThisFrame { get { Tick(); return s_Primary; } }

        /// <summary>Left button held (and not gated).</summary>
        public static bool PrimaryHeld { get { Tick(); return s_PrimaryHeld; } }

        /// <summary>Rotate keys held this frame: +1 = Q, −1 = E, 0 = none or both (only while the lock is live).</summary>
        public static float RotateAxis
        {
            get
            {
                Tick();
                if (!PointerLock.IsLocked) return 0f;
                if (UseSimulatedDevices) return Mathf.Clamp(SimulatedRotate, -1f, 1f);
                var kb = Keyboard.current;
                if (kb == null) return 0f;
                float a = 0f;
                if (kb.qKey.isPressed) a += 1f;
                if (kb.eKey.isPressed) a -= 1f;
                return a;
            }
        }

        /// <summary>Mouse movement in pixels this frame (0 while unlocked / just locked; clamped).</summary>
        public static Vector2 LookDelta { get { Tick(); return s_Look; } }

        /// <summary>True while mouse buttons are being ignored after a lock.</summary>
        public static bool MouseGated { get { Tick(); return s_MouseGated; } }

        /// <summary>
        /// After a place / shot: the raise must be released before it counts again (hold), or the toggle
        /// switches off (toggle), so the next photo is not raised by the same key press.
        /// </summary>
        public static void ConsumeRaise()
        {
            Tick();
            s_Toggled = false;
            if (ShiftDown()) s_ShiftNeedsRelease = true;
            if (RmbDown()) s_RmbNeedsRelease = true;
            s_Raise = false;
        }

        /// <summary>Forgets every held input until it is released (focus loss, deliberate releases).</summary>
        public static void ResetHeld()
        {
            s_Toggled = false;
            s_ShiftNeedsRelease = true;
            s_RmbNeedsRelease = true;
            s_MouseGated = true;
            s_GateFrame = Time.frameCount;
            s_Raise = s_RaisePressed = s_Primary = s_PrimaryHeld = false;
            s_Look = Vector2.zero;
        }

        // ---------------------------------------------------------------- evaluation

        static void Hook()
        {
            if (s_Hooked) return;
            s_Hooked = true;
            PointerLock.Changed += OnLockChanged;
            PointerLock.FocusLost += OnFocusLost;
        }

        static void OnLockChanged(bool locked)
        {
            // Every acquisition starts a gate: the acquiring click (and its release) never act.
            s_MouseGated = true;
            s_GateFrame = Time.frameCount;
            if (!locked) s_Toggled = false;
        }

        static void OnFocusLost()
        {
            ResetHeld();
            // Stale "pressed" states would otherwise survive the missing key-up events.
            if (!UseSimulatedDevices)
            {
                try
                {
                    if (Keyboard.current != null) InputSystem.ResetDevice(Keyboard.current);
                    if (Mouse.current != null) InputSystem.ResetDevice(Mouse.current);
                }
                catch (System.Exception e) { Debug.LogWarning("[IonInput] device reset failed: " + e.Message); }
            }
        }

        static bool ShiftDown()
        {
            if (UseSimulatedDevices) return SimulatedRaise;
            var kb = Keyboard.current;
            return kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
        }

        static bool RmbDown()
        {
            if (UseSimulatedDevices) return false;
            var m = Mouse.current;
            return m != null && m.rightButton.isPressed;
        }

        static bool LmbDown()
        {
            if (UseSimulatedDevices) return SimulatedPrimaryDown;
            var m = Mouse.current;
            return m != null && m.leftButton.isPressed;
        }

        static bool LmbPressedThisFrame()
        {
            if (UseSimulatedDevices) return SimulatedPrimaryDown && !_simPrimaryWas;
            var m = Mouse.current;
            return m != null && m.leftButton.wasPressedThisFrame;
        }

        static bool ShiftPressedThisFrame()
        {
            if (UseSimulatedDevices) return false; // toggle mode is not simulated
            var kb = Keyboard.current;
            return kb != null && (kb.leftShiftKey.wasPressedThisFrame || kb.rightShiftKey.wasPressedThisFrame);
        }

        static void Tick()
        {
            int frame = Time.frameCount;
            if (frame == s_Frame) return;
            s_Frame = frame;
            Hook();
            PointerLock.Tick();

            bool locked = PointerLock.IsLocked;
            bool shift = ShiftDown();
            bool rmb = RmbDown();
            bool lmb = LmbDown();
            bool lmbPressed = LmbPressedThisFrame();
            bool shiftPressed = ShiftPressedThisFrame();
            if (UseSimulatedDevices) _simPrimaryWas = SimulatedPrimaryDown;

            if (!shift) s_ShiftNeedsRelease = false;
            if (!rmb) s_RmbNeedsRelease = false;

            // Mouse gate after a lock: all buttons up, and at least GateFrames frames.
            if (s_MouseGated && !lmb && !rmb && frame - s_GateFrame >= GateFrames)
                s_MouseGated = false;

            bool wasRaise = s_Raise;
            if (!locked)
            {
                s_Raise = false;
                s_Toggled = false;
                s_Primary = false;
                s_PrimaryHeld = false;
                s_Look = Vector2.zero;
                s_RaisePressed = false;
                return;
            }

            bool shiftLive = shift && !s_ShiftNeedsRelease;
            bool rmbLive = rmb && !s_RmbNeedsRelease && !s_MouseGated;
            if (Feel.RaiseToggle && !UseSimulatedDevices)
            {
                if (shiftPressed && !s_ShiftNeedsRelease) s_Toggled = !s_Toggled;
                s_Raise = s_Toggled || rmbLive;
            }
            else
            {
                s_Raise = shiftLive || rmbLive;
            }
            s_RaisePressed = s_Raise && !wasRaise;

            s_Primary = lmbPressed && !s_MouseGated;
            s_PrimaryHeld = lmb && !s_MouseGated;

            // Look: dropped for GateFrames frames after the lock, clamped against spurious spikes.
            Vector2 d;
            if (UseSimulatedDevices) d = SimulatedLook;
            else
            {
                var m = Mouse.current;
                d = m != null ? m.delta.ReadValue() : Vector2.zero;
            }
            if (PointerLock.FramesSinceLock <= GateFrames) d = Vector2.zero;
            d.x = Mathf.Clamp(d.x, -MaxDeltaPx, MaxDeltaPx);
            d.y = Mathf.Clamp(d.y, -MaxDeltaPx, MaxDeltaPx);
            s_Look = d;
        }
    }
}
