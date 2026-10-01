using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Ion.Presentation
{
    /// <summary>
    /// Builds the whole uGUI layer in code: EventSystem (Input System module), a Screen Space
    /// Overlay canvas on layer 9 "PhotoUI", the photo overlay, crosshair, HUD, tutorial line, end card,
    /// screen effects and click-to-play overlay (in that draw order). Idempotent.
    /// </summary>
    public static class UIFactory
    {
        public static Canvas Canvas { get; private set; }

        public static void Create()
        {
            if (Canvas != null) return;
            if (Object.FindFirstObjectByType<Hud>() != null) return;

            EnsureEventSystem();

            var canvasGo = new GameObject("Ion UI", typeof(RectTransform));
            canvasGo.layer = UIUtil.UILayer;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            canvas.pixelPerfect = false;

            IonCanvasScaler.AddTo(canvasGo); // 1920x1080, match 0.5, with a minimum scale

            canvasGo.AddComponent<GraphicRaycaster>();
            Canvas = canvas;
            var root = canvasGo.transform;

            // Draw order: photo overlay < crosshair < HUD < tutorial line < end card < screen effects
            // (flash / rewind / fade-to-white) < click-to-play.
            var overlay = UIUtil.NewRect("PhotoOverlay", root);
            UIUtil.Stretch(overlay);
            overlay.gameObject.AddComponent<PhotoOverlayUI>();

            var cross = UIUtil.NewRect("Crosshair", root);
            cross.gameObject.AddComponent<Crosshair>();

            var hud = UIUtil.NewRect("Hud", root);
            UIUtil.Stretch(hud);
            hud.gameObject.AddComponent<Hud>();

            var tutorial = UIUtil.NewRect("Onboarding", root);
            UIUtil.Stretch(tutorial);
            tutorial.gameObject.AddComponent<Onboarding>();

            var end = UIUtil.NewRect("EndCard", root);
            UIUtil.Stretch(end);
            end.gameObject.AddComponent<EndCard>();

            var fx = UIUtil.NewRect("ScreenFx", root);
            UIUtil.Stretch(fx);
            fx.gameObject.AddComponent<ScreenFx>();
            fx.gameObject.AddComponent<FreshPulse>();

            var ctp = UIUtil.NewRect("ClickToPlay", root);
            UIUtil.Stretch(ctp);
            ctp.gameObject.AddComponent<ClickToPlayOverlay>();
        }

        static void EnsureEventSystem()
        {
            var es = Object.FindFirstObjectByType<EventSystem>();
            if (es == null)
            {
                var go = new GameObject("EventSystem");
                go.AddComponent<EventSystem>();
                go.AddComponent<InputSystemUIInputModule>(); // installs DefaultInputActions when added from code
                return;
            }
            if (es.GetComponent<BaseInputModule>() == null)
                es.gameObject.AddComponent<InputSystemUIInputModule>();
        }
    }
}
