using Ion.Presentation;
using UnityEngine;

namespace Ion.Gameplay
{
    /// <summary>
    /// Null-safe, cached access to the presentation UI (Hud, PhotoOverlayUI). The UI may be built after
    /// the player, so lookups are retried at most once per second while missing.
    /// </summary>
    internal static class GameplayUI
    {
        static Hud s_Hud;
        static float s_NextHudFind;
        static PhotoOverlayUI s_Overlay;
        static float s_NextOverlayFind;

        static Hud s_PromptHud;
        static string s_LastPrompt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Hud = null;
            s_Overlay = null;
            s_PromptHud = null;
            s_LastPrompt = null;
            s_NextHudFind = 0f;
            s_NextOverlayFind = 0f;
        }

        public static Hud Hud
        {
            get
            {
                if (s_Hud == null && Time.unscaledTime >= s_NextHudFind)
                {
                    s_Hud = Object.FindFirstObjectByType<Hud>();
                    if (s_Hud == null) s_NextHudFind = Time.unscaledTime + 1f;
                }
                return s_Hud;
            }
        }

        public static PhotoOverlayUI Overlay
        {
            get
            {
                if (s_Overlay == null && Time.unscaledTime >= s_NextOverlayFind)
                {
                    s_Overlay = Object.FindFirstObjectByType<PhotoOverlayUI>();
                    if (s_Overlay == null) s_NextOverlayFind = Time.unscaledTime + 1f;
                }
                return s_Overlay;
            }
        }

        static bool Quiet => Ion.Levels.GameBootstrap.Restarting;

        /// <summary>
        /// A rewound photo flies from the middle of the screen back into the inventory strip. During a rewind
        /// glide it is timed to land as the glide settles.
        /// </summary>
        public static void PhotoReturned(Ion.Projection.PhotoData photo)
        {
            var hud = Hud;
            if (hud == null || Quiet) return;
            var rewind = Ion.Gameplay.State.RewindController.Instance;
            float seconds = rewind != null ? rewind.GlideRemaining : 0f;
            hud.FlyInFromCenter(photo, seconds > 0.05f ? Mathf.Max(seconds, Ion.Presentation.Motion.Feel.PickupFlySeconds) : 0f);
        }

        /// <summary>A collected photo pops from its spot in the world into the inventory strip.</summary>
        public static void PhotoCollected(Ion.Projection.PhotoData photo, Vector3 worldPosition)
        {
            var hud = Hud;
            if (hud != null && !Quiet) hud.FlyInFromWorld(photo, worldPosition);
        }

        /// <summary>A fresh instant-camera print slides out from the bottom, then joins the inventory strip.</summary>
        public static void PhotoPrinted(Ion.Projection.PhotoData photo)
        {
            var hud = Hud;
            if (hud != null && !Quiet) hud.PrintOut(photo);
        }

        public static void Toast(string text)
        {
            var hud = Hud;
            if (hud != null) hud.Toast(text);
        }

        public static void Toast(string text, float seconds)
        {
            var hud = Hud;
            if (hud != null) hud.Toast(text, seconds);
        }

        /// <summary>Sets the HUD prompt, only forwarding when the text (or the Hud instance) changed. "" hides it.</summary>
        public static void SetPrompt(string text)
        {
            if (text == null) text = string.Empty;
            var hud = Hud;
            if (hud == null) return;
            if (hud == s_PromptHud && string.Equals(text, s_LastPrompt)) return;
            s_PromptHud = hud;
            s_LastPrompt = text;
            hud.Prompt(text);
        }
    }
}
