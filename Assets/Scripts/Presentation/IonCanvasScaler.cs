using UnityEngine;
using UnityEngine.UI;

namespace Ion.Presentation
{
    /// <summary>
    /// CanvasScaler for the HUD canvases: Scale With Screen Size against 1920x1080 (match 0.5), but never
    /// below <see cref="MinScale"/>, so text stays readable in small browser windows (at 800x500 the plain
    /// scaler would shrink 20 px text to ~9 px). Layouts that must fit the screen (the title and end
    /// cards) fit themselves.
    /// </summary>
    public sealed class IonCanvasScaler : CanvasScaler
    {
        public const float MinScale = 0.72f;

        /// <summary>Applies the shared settings to a scaler.</summary>
        public static IonCanvasScaler AddTo(GameObject go)
        {
            var scaler = go.AddComponent<IonCanvasScaler>();
            scaler.uiScaleMode = ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return scaler;
        }

        /// <summary>The scale factor for a screen size (same log-space match as CanvasScaler, with the floor).</summary>
        public static float ScaleFor(Vector2 screen, Vector2 reference, float match)
        {
            if (screen.x <= 0f || screen.y <= 0f) return 1f;
            float logW = Mathf.Log(screen.x / reference.x, 2f);
            float logH = Mathf.Log(screen.y / reference.y, 2f);
            float scale = Mathf.Pow(2f, Mathf.Lerp(logW, logH, match));
            return Mathf.Max(MinScale, scale);
        }

        protected override void HandleScaleWithScreenSize()
        {
            Vector2 screen = new Vector2(Screen.width, Screen.height);
            var canvas = GetComponent<Canvas>();
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                int display = canvas.targetDisplay;
                if (display > 0 && display < Display.displays.Length)
                    screen = new Vector2(Display.displays[display].renderingWidth, Display.displays[display].renderingHeight);
            }
            SetScaleFactor(ScaleFor(screen, m_ReferenceResolution, m_MatchWidthOrHeight));
            SetReferencePixelsPerUnit(m_ReferencePixelsPerUnit);
        }
    }
}
