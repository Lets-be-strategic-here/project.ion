using System;
using UnityEngine;

namespace Ion.Presentation
{
    /// <summary>
    /// Global quality tier every effect keys its cost off: 0 = Low, 1 = Medium, 2 = High, 3 = Ultra (strong machines:
    /// local lights, 2-cascade soft shadows, bloom, AO, extra detail; see AdaptiveQuality and UltraFx).
    /// <see cref="Current"/> is the effective tier. The player's choice (<see cref="Preference"/>) is
    /// persisted in PlayerPrefs "ion.quality": -1 = Auto (AdaptiveQuality picks the tier from frame
    /// time), 0..3 = a fixed tier. <see cref="Changed"/> fires on the main thread whenever
    /// <see cref="Current"/> changes. Handlers should be cheap; tier changes are rare (seconds apart).
    /// </summary>
    public static class QualityTier
    {
        public const int Auto = -1;
        public const int Low = 0;
        public const int Medium = 1;
        public const int High = 2;
        public const int Ultra = 3;
        /// <summary>Highest tier.</summary>
        public const int Max = Ultra;
        public const string PrefKey = "ion.quality";

        const int NotLoaded = -2;

        static int s_Preference = NotLoaded;
        static int s_Current = High;

        /// <summary>Fires after <see cref="Current"/> changes.</summary>
        public static event Action Changed;

        /// <summary>Fires after <see cref="Preference"/> changes (Auto vs a fixed tier).</summary>
        public static event Action PreferenceChanged;

        /// <summary>Effective tier: 0 Low, 1 Medium, 2 High, 3 Ultra.</summary>
        public static int Current
        {
            get
            {
                EnsureLoaded();
                return s_Current;
            }
        }

        /// <summary>Persisted choice: -1 Auto, else 0..3.</summary>
        public static int Preference
        {
            get
            {
                EnsureLoaded();
                return s_Preference;
            }
        }

        public static bool IsAuto => Preference == Auto;

        /// <summary>True on the Ultra tier.</summary>
        public static bool IsUltra => Current >= Ultra;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Preference = NotLoaded;
            s_Current = High;
            Changed = null;
            PreferenceChanged = null;
        }

        /// <summary>
        /// Sets the player's preference: -1 = Auto, 0..3 = fixed tier. Persisted.
        /// A fixed tier applies immediately; Auto keeps the current tier and lets AdaptiveQuality adjust it.
        /// </summary>
        public static void Set(int tier)
        {
            EnsureLoaded();
            tier = Mathf.Clamp(tier, Auto, Max);
            bool prefChanged = tier != s_Preference;
            s_Preference = tier;
            if (prefChanged)
            {
                PlayerPrefs.SetInt(PrefKey, tier);
                PlayerPrefs.Save();
            }
            if (tier != Auto) SetCurrent(tier);
            if (prefChanged) Raise(PreferenceChanged);
        }

        /// <summary>Used by AdaptiveQuality to move the effective tier while in Auto. Ignored otherwise.</summary>
        internal static void SetAutoTier(int tier)
        {
            EnsureLoaded();
            if (s_Preference != Auto) return;
            SetCurrent(Mathf.Clamp(tier, Low, Max));
        }

        public static string Name(int tier)
        {
            switch (tier)
            {
                case Auto: return "Auto";
                case Low: return "Low";
                case Medium: return "Med";
                case High: return "High";
                default: return "Ultra";
            }
        }

        static void SetCurrent(int tier)
        {
            if (tier == s_Current) return;
            s_Current = tier;
            Raise(Changed);
        }

        static void EnsureLoaded()
        {
            if (s_Preference != NotLoaded) return;
            int pref = PlayerPrefs.GetInt(PrefKey, Auto);
            if (pref < Auto || pref > Max) pref = Auto;
            s_Preference = pref;
            s_Current = pref == Auto ? High : pref;
        }

        static void Raise(Action evt)
        {
            if (evt == null) return;
            // One bad subscriber must not stop the others (tier changes are rare, so the list alloc is fine).
            var list = evt.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try
                {
                    ((Action)list[i])();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
    }
}
