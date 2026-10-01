using System.Collections.Generic;
using Ion.Projection;
using UnityEngine;

namespace Ion.Gameplay.State
{
    /// <summary>Where the player stands and looks (feet position, degrees) and in which zone.</summary>
    public struct PlayerPose
    {
        public Vector3 Feet;
        public float Yaw, Pitch;
        public int Zone;

        public PlayerPose(Vector3 feet, float yaw, float pitch, int zone)
        {
            Feet = feet;
            Yaw = yaw;
            Pitch = pitch;
            Zone = zone;
        }

        /// <summary>The current pose of <paramref name="fpc"/> (zone from <see cref="ZoneInfo.ZoneOf"/>).</summary>
        public static PlayerPose Of(FirstPersonController fpc)
        {
            if (fpc == null) return default;
            Vector3 p = fpc.transform.position;
            return new PlayerPose(p, fpc.Yaw, fpc.Pitch, ZoneInfo.ZoneOf(p));
        }

        public override string ToString() => $"({Feet.x:0.00}, {Feet.y:0.00}, {Feet.z:0.00}) yaw {Yaw:0} pitch {Pitch:0} zone {Zone}";
    }

    public enum ChangeKind { Placement, Capture, Switch, Pickup, CameraPickup }

    /// <summary>One undoable world change. Pushed by the code that makes the change (art bible §11).</summary>
    public abstract class WorldChange
    {
        /// <summary>Safe pose at the moment of the change (valid in the pre-change world).</summary>
        public PlayerPose SafePose;
        /// <summary>Game time when the change was made.</summary>
        public float Time;
        public abstract ChangeKind Kind { get; }
        /// <summary>Restores the world as it was before the change. <paramref name="instant"/>: no animations (checkpoint restore).</summary>
        internal abstract void Undo(bool instant);

        public override string ToString() => Kind + " @" + Time.ToString("0.00");
    }

    /// <summary>A photo placement. Undo: ProjectionSystem.Rewind() back to the depth before it; the photo returns to the hand.</summary>
    internal sealed class PlacementChange : WorldChange
    {
        public int ProjectionDepthBefore;
        public PhotoData Photo;
        public int Index;
        public PhotoInventory Inventory;
        public override ChangeKind Kind => ChangeKind.Placement;

        internal override void Undo(bool instant)
        {
            var ps = ProjectionSystem.Instance;
            if (ps != null)
            {
                if (ps.PlacementCount != ProjectionDepthBefore + 1)
                    Debug.LogWarning("[WorldHistory] placement depth " + ps.PlacementCount + ", expected " + (ProjectionDepthBefore + 1) + " (a placement bypassed the history)");
                int guard = 64;
                while (ps.PlacementCount > ProjectionDepthBefore && guard-- > 0) ps.Rewind();
            }
            if (Inventory != null && Photo != null && !Inventory.Contains(Photo))
            {
                Inventory.Insert(Index, Photo);
                if (!instant) GameplayUI.PhotoReturned(Photo);
            }
        }
    }

    /// <summary>An instant-camera shot. Undo: the photo leaves the inventory and the film comes back.</summary>
    internal sealed class CaptureChange : WorldChange
    {
        public PhotoData Photo;
        public int InventoryIndex;
        public int FilmBefore;
        public PhotoInventory Inventory;
        public InstantCamera Camera;
        public override ChangeKind Kind => ChangeKind.Capture;

        internal override void Undo(bool instant)
        {
            if (Inventory != null && Photo != null) Inventory.Remove(Photo);
            if (Camera != null) Camera.Film = FilmBefore;
        }
    }

    /// <summary>A switch press. Undo: the channel goes back (movers run fast while rewinding).</summary>
    internal sealed class SwitchChange : WorldChange
    {
        public string Channel;
        public bool Before;
        public override ChangeKind Kind => ChangeKind.Switch;

        internal override void Undo(bool instant) => SwitchBoard.Set(Channel, Before, instant);
    }

    /// <summary>A photo picked up. Undo: it leaves the inventory and lies where it was.</summary>
    internal sealed class PickupChange : WorldChange
    {
        public PhotoPickup Pickup;
        public PhotoData Photo;
        public int Index;
        public PhotoInventory Inventory;
        public override ChangeKind Kind => ChangeKind.Pickup;

        internal override void Undo(bool instant)
        {
            if (Inventory != null && Photo != null) Inventory.Remove(Photo);
            if (Pickup != null) Pickup.ResetPickup();
        }
    }

    /// <summary>The instant camera picked up. Undo: unlock state and film as before; the camera is back on its stand.</summary>
    internal sealed class CameraPickupChange : WorldChange
    {
        public CameraPickup Pickup;
        public bool UnlockedBefore;
        public int FilmBefore;
        public InstantCamera Camera;
        public override ChangeKind Kind => ChangeKind.CameraPickup;

        internal override void Undo(bool instant)
        {
            if (Camera != null)
            {
                Camera.SetUnlockedSilently(UnlockedBefore);
                Camera.Film = FilmBefore;
            }
            if (Pickup != null) Pickup.ResetPickup();
        }
    }

    /// <summary>Snapshot taken at zone entry, hub entry and explicit CheckpointMarkers.</summary>
    public sealed class Checkpoint
    {
        public string Id;
        public int Zone;
        /// <summary>History depth when set: the floor a single R cannot cross.</summary>
        public int HistoryDepth;
        public PlayerPose Pose;
        public List<PhotoData> Inventory;
        public int SelectedIndex;
        public int Film;
        public bool CameraUnlocked;
        /// <summary>SwitchBoard snapshot.</summary>
        public Dictionary<string, bool> Channels;
        /// <summary>Game time when it was set.</summary>
        public float Time;

        public override string ToString() => $"checkpoint '{Id}' zone {Zone} depth {HistoryDepth} photos {(Inventory != null ? Inventory.Count : 0)}";
    }

    public enum RewindResult { Undid, RecoveredFall, Nothing, ToCheckpoint }

    /// <summary>
    /// Zone layout hooks (Lead C may replace the defaults): zones sit on the X line every
    /// 50 m (GameBootstrap.RoomSpacing), and a zone's void (limbo) starts 6 m below its lowest floor.
    /// </summary>
    public static class ZoneInfo
    {
        public const float DefaultSpacing = 50f;
        public const float DefaultVoidY = -6f;
        /// <summary>Last-resort reset height (always recovers, whatever the zone).</summary>
        public const float ResetY = -50f;

        /// <summary>Zone index of a world position. Default: round(x / 50), never negative.</summary>
        public static System.Func<Vector3, int> ZoneOfProvider;
        /// <summary>Void height of a zone (lowest floor − 6). Default: −6.</summary>
        public static System.Func<int, float> VoidYProvider;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ZoneOfProvider = null;
            VoidYProvider = null;
        }

        public static int ZoneOf(Vector3 worldPosition)
        {
            if (ZoneOfProvider != null) return ZoneOfProvider(worldPosition);
            return Mathf.Max(0, Mathf.RoundToInt(worldPosition.x / DefaultSpacing));
        }

        public static float VoidY(int zone) => VoidYProvider != null ? VoidYProvider(zone) : DefaultVoidY;
    }
}
