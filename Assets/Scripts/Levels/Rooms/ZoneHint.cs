using System;
using Ion.Gameplay;
using Ion.Presentation;
using UnityEngine;

namespace Ion.Levels.Rooms
{
    /// <summary>
    /// A tutorial / level hint: toasts <see cref="Text"/> when the player's feet are inside a horizontal radius
    /// around this object (and within <see cref="Height"/> vertically) while <see cref="When"/> holds. Fires at most
    /// once per entry, and only once ever when <see cref="Once"/>. A distance poll: no collider, survives being cut
    /// by a photo, never sliced or captured (neither Sliceable nor Interactable). Text uses bracket key glyphs,
    /// e.g. "[SHIFT] hold up photo" (art bible §9.2).
    /// </summary>
    public sealed class ZoneHint : MonoBehaviour
    {
        public string Text;
        public float Radius = 1.5f;
        public float Height = 2.5f;
        public float Seconds = 4f;
        public bool Once = true;
        /// <summary>Optional condition (null = always).</summary>
        public Func<bool> When;

        bool _inside;
        bool _firedThisEntry;
        bool _firedEver;

        /// <summary>How many times the hint has shown (tests).</summary>
        public int ShownCount { get; private set; }

        /// <summary>Re-arms the hint (game restart).</summary>
        public void ResetZone()
        {
            _inside = false;
            _firedThisEntry = false;
            _firedEver = false;
        }

        void Update()
        {
            var player = FirstPersonController.Current;
            if (player == null) return;
            Vector3 d = player.transform.position - transform.position;
            bool inside = d.y > -1f && d.y < Height && d.x * d.x + d.z * d.z <= Radius * Radius;
            if (!inside)
            {
                _inside = false;
                _firedThisEntry = false;
                return;
            }
            _inside = true;
            if (_firedThisEntry || (Once && _firedEver) || !player.InputEnabled) return;
            bool ok;
            try { ok = When == null || When(); }
            catch (Exception e) { Debug.LogException(e); ok = false; }
            if (!ok) return;
            _firedThisEntry = true;
            _firedEver = true;
            ShownCount++;
            RoomContext.Toast(Text, Seconds);
        }

        /// <summary>True while the player is inside the hint area.</summary>
        public bool PlayerInside => _inside;
    }

    /// <summary>Small easing helpers for level-owned animations (hatch, prints). Values from Feel where they exist.</summary>
    internal static class LevelEase
    {
        public static float InOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
        }

        public static float OutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            float u = 1f - t;
            return 1f - u * u * u;
        }
    }
}
