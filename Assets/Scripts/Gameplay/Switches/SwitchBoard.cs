using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ion.Gameplay
{
    /// <summary>
    /// Global switch channels (art bible §11). A channel is on or off; switches toggle channels and
    /// <see cref="SwitchTarget"/>s follow them. Channel state is global, so a photo copy of a button (or of a
    /// gate) works exactly like the original. Unknown channels read as off.
    /// <see cref="Set"/> writes no history: <see cref="Switch.Press"/> records the change, and undo /
    /// checkpoint restore use Set / <see cref="Restore"/>.
    /// </summary>
    public static class SwitchBoard
    {
        static readonly Dictionary<string, bool> s_Channels = new Dictionary<string, bool>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Channels.Clear();
            ChannelChanged = null;
        }

        /// <summary>channel, on, instant (true for checkpoint restores: no animation).</summary>
        public static event Action<string, bool, bool> ChannelChanged;

        public static bool Get(string channel) =>
            !string.IsNullOrEmpty(channel) && s_Channels.TryGetValue(channel, out bool on) && on;

        public static void Set(string channel, bool on, bool instant = false)
        {
            if (string.IsNullOrEmpty(channel)) return;
            bool was = Get(channel);
            s_Channels[channel] = on;
            if (was == on && !instant) return;
            Notify(channel, on, instant);
        }

        /// <summary>A copy of every channel's state.</summary>
        public static Dictionary<string, bool> Snapshot() => new Dictionary<string, bool>(s_Channels);

        /// <summary>
        /// Puts every channel back to <paramref name="snapshot"/> instantly (null / empty = everything off).
        /// Channels missing from the snapshot turn off.
        /// </summary>
        public static void Restore(Dictionary<string, bool> snapshot)
        {
            var keys = new List<string>(s_Channels.Keys);
            if (snapshot != null)
                foreach (var kv in snapshot)
                    if (!s_Channels.ContainsKey(kv.Key)) keys.Add(kv.Key);

            for (int i = 0; i < keys.Count; i++)
            {
                string ch = keys[i];
                bool on = snapshot != null && snapshot.TryGetValue(ch, out bool v) && v;
                s_Channels[ch] = on;
                Notify(ch, on, true);
            }
        }

        static void Notify(string channel, bool on, bool instant)
        {
            var handler = ChannelChanged;
            if (handler == null) return;
            foreach (Action<string, bool, bool> d in handler.GetInvocationList())
            {
                try { d(channel, on, instant); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}
