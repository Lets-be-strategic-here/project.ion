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

        public static void Toast(string text)
        {
            var hud = Hud;
            if (hud != null) hud.Toast(text);
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
