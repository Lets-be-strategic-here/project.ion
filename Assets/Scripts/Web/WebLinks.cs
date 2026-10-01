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
#endif

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
