#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace Ion.Web
{
    /// <summary>
    /// Opens external links. On the web the tab opens on the player's next click/key release
    /// (see Assets/Plugins/WebGL/OpenUrl.jslib) so popup blockers allow it.
    /// </summary>
    public static class WebLinks
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void IonOpenUrl(string url);
        [DllImport("__Internal")]
        static extern int IonIsLoaderVisible();
        [DllImport("__Internal")]
        static extern void IonSetEndCardOpen(int open);
#endif

        /// <summary>True while the web page's loading card still covers the game (always false outside the Web player).</summary>
        public static bool LoaderVisible
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                try { return IonIsLoaderVisible() != 0; }
                catch { return false; }
#else
                return false;
#endif
            }
        }

        /// <summary>Tells the page whether the end card is open (it hides its own corner link meanwhile).</summary>
        public static void SetEndCardOpen(bool open)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try { IonSetEndCardOpen(open ? 1 : 0); }
            catch { }
#endif
        }

        public static void Open(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            IonOpenUrl(url);
#else
            Application.OpenURL(url);
#endif
        }
    }
}
