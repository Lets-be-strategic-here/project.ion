using UnityEngine;

namespace Ion.Presentation.Motion
{
    /// <summary>
    /// Every timing / spring constant of the game's motion (art bible §9.1), in one place so tuning happens
    /// here. Durations are seconds (time to settle within 2% for springs); springs are (Hz, ζ) pairs for
    /// <see cref="Spring.Step(ref float, ref float, float, float, float, float)"/>.
    /// Also holds the two motion-related player preferences (reduced motion, raise hold / toggle).
    /// Owner: Lead D. Read by B (Mover timings), E (rewind timing for the tape dip) and everyone animating.
    /// </summary>
    public static class Feel
    {
        // ---------------------------------------------------------------- photo held up
        public const float RaiseFreq = 2.6f, RaiseZeta = 0.82f;          // ≈ 0.34 s, ≈ 2% overshoot
        public const float RaiseTiltDeg = -7f, RaiseTiltZeta = 0.9f;     // tilt −7° → 0
        public const float RaiseSeconds = 0.34f;
        /// <summary>Place is enabled once the raise has reached this fraction.</summary>
        public const float PlaceEnableProgress = 0.85f;
        public const float LowerSeconds = 0.26f;                         // easeInOutCubic
        public const float SwayFreq = 1.8f, SwayZeta = 0.75f;            // lag spring on mouse delta
        public const float SwayMaxPx = 12f;
        // Q/E rotation is continuous: hold to turn, tap to nudge (the overlay draws the exact logical roll).
        public const float RotateDegPerSec = 60f;                        // steady speed while held
        public const float RotateEaseIn = 0.14f;                         // velocity time constant on press
        public const float RotateEaseOut = 0.07f;                        // ... and on release (≈ 4° coast)
        public const float RotateTapDegrees = 3f;                        // a tap turns at least this far (+ coast)
        public const float RotateAssistDegrees = 2.5f;                   // magnetic assist window around 90° multiples
        public const float RotateAssistSeconds = 0.12f;                  // eased settle time constant
        public const float RotateAssistMaxSpeed = 6f;                    // assist waits until the coast is this slow
        public const float RotateTiltFreq = 3.4f;                        // in-hand tilt → upright spring (raise)

        // ---------------------------------------------------------------- place
        public const float PlaceSeconds = 0.70f;
        public const float PlacePressSeconds = 0.10f;                    // scale 1 → 0.985, easeOutQuad
        public const float PlacePressScale = 0.985f;
        public const float PlaceSwapAt = 0.10f;                          // world swap (earliest)
        public const float PlaceStageMaxSeconds = 0.40f;                 // ... latest, while staged cuts run (slow machines)
        public const float PlaceFlashAlpha = 0.30f, PlaceFlashSeconds = 0.35f;      // Frost, easeOutQuart
        public const float PlaceCardScale = 1.08f, PlaceCardSeconds = 0.30f;       // easeOutQuart + fade
        public const float DevelopSeconds = 0.6f;                        // FreshPulse "develop"
        public const float PlaceFovKickDeg = 2.5f, PlaceFovFreq = 2f, PlaceFovZeta = 0.8f;

        // ---------------------------------------------------------------- rewind
        public const float RewindSeconds = 0.60f;
        public const float RewindSwapAt = 0.22f;
        public const float RewindWashAlpha = 0.22f, RewindDesat = 0.5f;  // Cyanotype wash, sine in-out
        public const float RewindMoveFadeIn = 0.18f;                      // to Paper, easeInQuad
        public const float RewindMoveHold = 0.06f;
        public const float RewindMoveFadeOut = 0.30f;                     // easeOutCubic
        /// <summary>A second R within this window escalates to the checkpoint (RewindController.DoubleTapWindow).</summary>
        public const float DoubleTapWindow = 0.35f;

        // Single R that undoes a change: the player glides back to the pose they made it from (no fade, no
        // teleport). Position follows Ease.Smootherstep over RewindGlideSeconds(distance); the look slerps on the
        // same curve; every rewind effect (desaturation, tape bands, vignette, tape sound) follows its speed.
        public const float RewindGlideBaseSeconds = 0.80f;               // any glide, however short
        public const float RewindGlidePerMeter = 0.04f;                  // + this per metre of travel ...
        public const float RewindGlideMaxSeconds = 1.60f;                // ... capped here (20 m and beyond)
        public const float RewindGlideUndoAt = 0.24f;                    // world undo (after the un-develop)
        public const float RewindUndevelopSeconds = RewindGlideUndoAt;   // pasted pieces wash back to paper first
        public const float RewindUndevelopGlow = 0.62f;                  // FreshPulse warm glow at full un-develop
        public const float RewindGlideArcMax = 1.8f;                     // highest lift when the straight line is blocked
        public const float RewindGlideArcSpeed = 4f;                     // m/s the arc height may change mid-glide
        public const float RewindGlideCoverSeconds = 0.12f;              // Paper veil while the eye crosses a surface
        public const int RewindQueueMax = 1;                             // R presses buffered during a glide
        public const float RewindFxDesat = 0.62f;                        // world desaturation at full speed
        public const float RewindFxBands = 1f;                           // tape / scanline band strength at full speed
        public const float RewindFxBandCycles = 2.2f;                    // bands rolled through the frame per glide
        public const float RewindFxVignette = 0.30f;                     // vignette alpha at full speed
        public const float RewindTapePitchMin = 0.55f, RewindTapePitchMax = 1.55f;  // tape loop pitch: rest → full speed
        public const float RewindTapeVolume = 0.85f;
        public const float RewindMusicDip = 0.9f;                        // music tape-dip during a glide

        /// <summary>Glide duration for a travel distance: 0.8 s + 0.04 s/m, at most 1.6 s.</summary>
        public static float RewindGlideSeconds(float distance) =>
            Mathf.Min(RewindGlideMaxSeconds, RewindGlideBaseSeconds + RewindGlidePerMeter * Mathf.Max(0f, distance));

        /// <summary>Glide position along the path (0..1) at normalised time <paramref name="k"/>.</summary>
        public static float RewindGlideEase(float k) => Ease.Smootherstep(k);

        /// <summary>
        /// Rewind effect strength (0..1) at normalised glide time <paramref name="k"/>: the glide's speed, with a
        /// fuller shoulder (√ of the normalised smootherstep speed) so the effects read early and settle with it.
        /// </summary>
        public static float RewindFxEnvelope(float k) => Mathf.Sqrt(Ease.SmootherstepSpeed(k));

        public const float CheckpointSeconds = 1.00f;
        public const float CheckpointCloseSeconds = 0.35f;               // brackets close, easeInOutCubic
        public const float CheckpointSwapAt = 0.40f;
        public const float CheckpointOpenSeconds = 0.45f;                // easeOutCubic
        public const float CheckpointWashAlpha = 0.30f;

        public const float NothingSeconds = 0.30f;
        public const float NothingShakePx = 5f, NothingShakeCycles = 2f, NothingShakeZeta = 0.3f;
        public const float NothingToastSeconds = 1.4f;
        public const int NothingHintTimes = 3;                           // "R R — back to the checkpoint"

        // ---------------------------------------------------------------- fall / limbo
        public const float LimboInSeconds = 0.8f;                        // vignette + fog, easeOutCubic
        public const float LimboGravityScale = 0.15f;
        public const float LimboAutoRecoverSeconds = 4.0f;

        // ---------------------------------------------------------------- pickup
        public const float PickupSeconds = 0.70f;
        public const float PickupLift = 0.2f, PickupLiftSeconds = 0.30f, PickupLiftBack = 1.3f;   // easeOutBack
        public const float PickupFlySeconds = 0.40f, PickupFlyEndScale = 0.4f;                     // easeInOutCubic
        public const float CardFreq = 2.8f, CardZeta = 0.78f;           // HUD card spring

        // ---------------------------------------------------------------- teleport
        public const float TeleportSeconds = 1.10f;
        public const float TeleportFadeIn = 0.40f;                       // to Frost, easeInQuad, FOV +6°
        public const float TeleportHold = 0.15f;
        public const float TeleportFadeOut = 0.55f;                      // easeOutCubic
        public const float TeleportFovDeg = 6f, TeleportFovFreq = 1.8f, TeleportFovZeta = 0.9f;

        // ---------------------------------------------------------------- instant camera
        public const float ViewfinderTicksSeconds = 0.25f;               // corner ticks slide in, easeOutCubic
        public const float ShutterSeconds = 0.45f;
        public const float ShutterCloseSeconds = 0.05f;                  // linear
        public const float ShutterOpenSeconds = 0.12f;                   // easeOutQuad
        public const float ShutterFlashAlpha = 0.25f, ShutterFlashSeconds = 0.25f;
        public const float PrintEjectSeconds = 0.5f;                     // easeOutCubic

        // ---------------------------------------------------------------- devices
        public const float ButtonPressSeconds = 0.10f, ButtonTravel = 0.03f;   // easeOutQuad
        public const float ButtonReleaseFreq = 4f, ButtonReleaseZeta = 0.7f;
        public const float LeverAngleDeg = 35f;
        public const float MoverSecondsPer4m = 1.6f, MoverSettleSeconds = 0.06f;   // easeInOutCubic
        public const float MoverRewindSeconds = 0.35f;
        public const float CollapseWarnSeconds = 0.35f, CollapseWobbleDeg = 2f, CollapseWobbleHz = 9f;
        public const float CollapseGravity = 20f;
        public const float ExhibitSeconds = 0.8f, ExhibitLampFreq = 3f, ExhibitLampZeta = 0.7f;
        public const float HatchSeconds = 1.6f;
        public const float PowerUpSeconds = 0.6f, PowerUpRise = 0.25f;  // T1 button wakes up
        public const float CheckpointPulseSeconds = 0.6f;

        // ---------------------------------------------------------------- body
        public const float LandingFreq = 4.5f, LandingZeta = 0.65f;
        public const float LandingDepthPerMps = 0.03f, LandingMinSpeed = 4f, LandingMaxDepth = 0.09f;
        public const float HeadBobAmplitude = 0.015f, HeadBobStride = 2.3f;

        // ---------------------------------------------------------------- UI
        public const float UIHoverSeconds = 0.12f;                       // easeOutQuad
        public const float UIPressScale = 0.97f, UIPressSeconds = 0.08f;
        public const float PanelInSeconds = 0.22f, PanelOutSeconds = 0.16f, PanelRisePx = 8f;
        public const float ToastHoldSeconds = 2.4f, ToastOutSeconds = 0.25f;
        public const float ToastFreq = 3f, ToastZeta = 0.85f;
        public const float PromptCrossfadeSeconds = 0.15f;
        public const float ReducedMotionCrossfade = 0.3f;                // replaces the bracket iris

        // ---------------------------------------------------------------- preferences

        public const string ReducedMotionPrefKey = "ion.reducedMotion";
        public const string RaiseTogglePrefKey = "ion.raiseToggle";

        static int s_Reduced = -1, s_Toggle = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Reduced = -1;
            s_Toggle = -1;
        }

        /// <summary>
        /// Reduced motion (Settings): no photo sway, head bob, FOV kicks or shakes; the checkpoint bracket
        /// iris becomes a 0.3 s crossfade. Persisted.
        /// </summary>
        public static bool ReducedMotion
        {
            get
            {
                if (s_Reduced < 0) s_Reduced = PlayerPrefs.GetInt(ReducedMotionPrefKey, 0) != 0 ? 1 : 0;
                return s_Reduced == 1;
            }
            set
            {
                s_Reduced = value ? 1 : 0;
                PlayerPrefs.SetInt(ReducedMotionPrefKey, s_Reduced);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Raise mode (Settings "Raise: Hold / Toggle"). False = hold Shift (default). Persisted.</summary>
        public static bool RaiseToggle
        {
            get
            {
                if (s_Toggle < 0) s_Toggle = PlayerPrefs.GetInt(RaiseTogglePrefKey, 0) != 0 ? 1 : 0;
                return s_Toggle == 1;
            }
            set
            {
                s_Toggle = value ? 1 : 0;
                PlayerPrefs.SetInt(RaiseTogglePrefKey, s_Toggle);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Mover duration for a travel distance (1.6 s per ≤ 4 m).</summary>
        public static float MoverSeconds(float distance) =>
            MoverSecondsPer4m * Mathf.Max(1f, distance / 4f);
    }
}
