namespace Ion.Presentation.Audio
{
    /// <summary>
    /// Every sound effect in the game (art bible §8). Gameplay code plays them with
    /// <see cref="IonAudio.Play(Sfx, UnityEngine.Vector3?)"/>; nobody else creates AudioSources.
    /// Append only: the numeric values are stable (tests and debug tools use them).
    /// Clips live in <c>Assets/Resources/Audio/Sfx/</c> (see <see cref="SfxLibrary"/> for file names).
    /// </summary>
    public enum Sfx
    {
        None = 0,

        // Photo and camera (own body: always 2D).
        PhotoRaise = 1,
        PhotoLower = 2,
        PhotoPlace = 3,
        PhotoRewind = 4,
        PhotoRotate = 5,
        CameraShutter = 6,
        CameraEmpty = 7,
        PhotoPickup = 8,
        CameraRaise = 9,

        // Teleporters.
        TeleporterHumLoop = 10,
        TeleportTravel = 11,

        // UI.
        UiHover = 20,
        UiClick = 21,

        // Ambience.
        AmbWindLoop = 30,
        AmbBird = 31,

        // Devices (positional when a position is given).
        SwitchPress = 40,
        SwitchRelease = 41,
        SwitchDenied = 42,
        MoverLoop = 43,
        MoverStop = 44,
        CollapseCrack = 45,
        CollapseFall = 46,
        HatchRise = 47,
        ExhibitWake = 48,
        /// <summary>A device powering up (the T1 button waking): the ion G# shimmer, shorter.</summary>
        DeviceWake = 49,

        // Rewind, falls and checkpoints.
        FallWhoosh = 50,
        LimboDroneLoop = 51,
        RewindNothing = 52,
        RewindCheckpoint = 53,
        CheckpointSet = 54,
        /// <summary>The rewind glide's tape (a loop; IonAudio drives its pitch and level from the glide's speed).</summary>
        RewindTapeLoop = 55,
        /// <summary>The rewind glide settling: the tape stops, a soft in-key landing.</summary>
        RewindSettle = 56,

        // Body.
        Land = 60,
        FootstepTile = 61,
        FootstepStone = 62,
        FootstepWood = 63,
        FootstepGrass = 64,
    }

    /// <summary>Which music the current zone wants (art bible §8: tutorial, hub and ending; puzzle wings).</summary>
    public enum MusicArea
    {
        None = 0,
        Tutorial = 1,
        Hub = 2,
        Puzzle = 3,
        Ending = 4,
    }

    /// <summary>Footstep set, picked from the ground's <see cref="Mat"/> (and its pattern when known).</summary>
    public enum FootSurface
    {
        Tile = 0,
        Stone = 1,
        Wood = 2,
        Grass = 3,
    }

    /// <summary>A running loop started with <see cref="IonAudio.StartLoop"/>. Default = invalid.</summary>
    public readonly struct LoopHandle
    {
        public readonly int Id;
        public LoopHandle(int id) { Id = id; }
        public bool IsValid => Id != 0;
    }
}
