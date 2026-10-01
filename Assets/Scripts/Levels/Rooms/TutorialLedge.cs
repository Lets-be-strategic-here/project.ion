using Ion.Gameplay;
using Ion.Gameplay.State;
using Ion.Levels.Arch;
using Ion.Levels.Props;
using Ion.Presentation;
using UnityEngine;

namespace Ion.Levels.Rooms
{
    using Arch = Ion.Levels.Arch.Arch;

    /// <summary>
    /// Zone 0, T1 "Ledge" (art bible §7.1), mood Dawn. Tutorial steps 1–4, taught through play:
    ///  1. The exit teleporter sits on a 3 m ledge (x[−4, 4] z[16, 24]) that cannot be climbed.
    ///  2. The only other way is a plank bridge west to island E, which holds the photo. Its middle
    ///     (x[−9.5, −6]) is a <see cref="StoryCollapse"/>: it gives way and the player falls → [R] rewind.
    ///  3. The rewind wakes a button on the court (channel <see cref="SpanChannel"/>): it rises and lights up,
    ///     and pressing it slides an oak deck out of island E across the 3.5 m gap.
    ///  4. On island E the photo shows this ledge with a 12-step stair. Standing on the brass marker, holding the
    ///     photo up level and placing it pastes the stair (and a copy of the exit teleporter).
    ///
    /// Tuning vs. the bible (Lead C, proven by the tests): the marker is at z = 5.5 (not 6.5) with a level view
    /// (pitch 0, which the holder's level-snap makes exact) so the whole stair, from its foot at z = 10, is inside
    /// the photo even 0.3 m off the marker; the button faces +X so it is pressed from the court.
    /// </summary>
    public sealed class TutorialLedge : Room
    {
        public override string Key => "t1";
        public override string Title => "Ledge";
        public override string Intro => "The teleporter is up on the ledge.";
        public override ZoneMood Mood => ZoneMood.Dawn;
        public override ArchStyle Style => ArchStyle.LightTable;

        public const string SpanChannel = "t1.span";
        public const float LedgeTop = 3f, LedgeFront = 16f, StairFoot = 10f;
        public const float CollapseX0 = -9.5f, CollapseX1 = -6f;
        public const float BridgeZ0 = 11f, BridgeZ1 = 13f;

        public static readonly Vector3 SpawnFeet = new Vector3(0f, 0f, 1.5f);
        public static readonly Vector3 MarkerFeet = new Vector3(0f, 0f, 5.5f);
        public static readonly Vector3 ExitPad = new Vector3(0f, LedgeTop, 21.5f);
        public static readonly Vector3 ButtonPos = new Vector3(-3f, 0f, 9f);
        public static readonly Vector3 DisplayPos = new Vector3(-14f, 0f, 12f);
        /// <summary>Where a fall from the collapse is recovered to (the last safe pose on the bridge head), roughly.</summary>
        public static readonly Vector3 BridgeHead = new Vector3(-5f, 0f, 12f);

        /// <summary>The stair photo (t1.stair). Set by Build.</summary>
        public DioramaShot StairShot { get; private set; }
        public Switch Button { get; private set; }
        public StoryCollapse Collapse { get; private set; }
        public Mover Span { get; private set; }
        LedgeDirector _director;

        public override void Build(Transform root, RoomContext ctx)
        {
            BuildLedge(root, false);
            ctx.SetSpawn(SpawnFeet, 0f);

            // The photo: this very ledge with a stair, from the marker, level.
            StairShot = ctx.RegisterDioramaShot(MarkerFeet, 0f, 0f, "Ledge stair", id: "t1.stair");
            BuildLedge(ctx.DioramaRoot, true);
            System.Action exit = ctx.NextZone();
            // The exit teleporter lies inside the placement frustum: the diorama carries a working copy (§7.5.3).
            ctx.DioramaTeleporter(ExitPad, Dir.NegZ, exit);

            // ---- devices (world only) ----
            ctx.Teleporter(ExitPad, Dir.NegZ, exit);
            Collapse = PropKit.CollapseSlab(root, new Vector3(CollapseX0, -0.25f, BridgeZ0), new Vector3(CollapseX1, 0f, BridgeZ1),
                                            new Surf(Mat.Oak, Pat.Boards));
            // Stowed inside the abutment / island body (kerb tops 0.0625 below the floor), it slides +X and up to
            // fill the gap flush with the deck.
            Span = PropKit.SlidingBridge(root, new Vector3(-13.0625f, -0.4375f, 11.25f), new Vector3(-9.5625f, -0.1875f, 12.75f),
                                         new Vector3(3.5625f, 0.1875f, 0f), SpanChannel);
            Button = PropKit.Button(root, ButtonPos, Dir.PosX, SpanChannel, ButtonStyle.Pedestal, false);
            ctx.PhotoDisplay(DisplayPos, Dir.PosX, StairShot);
            ctx.Marker(MarkerFeet, 0f);

            _director = root.gameObject.AddComponent<LedgeDirector>();
            _director.Init(ctx, Button, Collapse);

            // ---- hints (bracket key glyphs, §9.2) ----
            DioramaShot shot = StairShot;
            ctx.Hint(new Vector3(0f, 0f, 14.5f), 2.5f, "Three metres of sheer wall.  The teleporter is up there.", 4f);
            ctx.Hint(new Vector3(-1.75f, 0f, 9f), 2.5f, "[E] press", 3f,
                     () => Button != null && Button.Powered && !SwitchBoard.Get(SpanChannel), false);
            ctx.Hint(DisplayPos + new Vector3(1.5f, 0f, 0f), 2f, "A photo of the ledge.  With a stair.", 3.5f);
            ctx.Hint(MarkerFeet, 1.25f, "[SHIFT] hold up the photo  -  line it up with the ledge", 5f,
                     () => RoomContext.PlayerHas(shot) && !RoomContext.PlayerRaised, false);
            ctx.Hint(MarkerFeet, 1.25f, "[LMB] place", 3f,
                     () => RoomContext.PlayerRaised, false);

            // ---- the tutorial script (§7.5.4): every step, in order ----
            AddSolution("wall", RoomSolution.Kind.Walk, new Vector3(0f, 0f, 14.5f));
            AddSolution("unreachable", RoomSolution.Kind.Goal, new Vector3(0f, LedgeTop, 19f)).Hazard = RoomSolution.Hazards.Blocked;
            var bridge = AddSolution("bridge", RoomSolution.Kind.Walk, new Vector3(-12f, 0f, 12f), 270f);
            bridge.Via = new[] { new Vector3(-2f, 0f, 12f), new Vector3(-4.5f, 0f, 12f) };
            bridge.Hazard = RoomSolution.Hazards.Fall;
            var fall = AddSolution("rewind.fall", RoomSolution.Kind.Rewind, BridgeHead, 270f);
            fall.Expect = RoomSolution.RewindExpect.RecoveredFall;
            fall.Tolerance = 1.5f;
            var press = AddSolution("press", RoomSolution.Kind.Press, new Vector3(-1.75f, 0f, 9f), 270f);
            press.Via = new[] { new Vector3(-3f, 0f, 12f) };
            press.Channel = SpanChannel;
            AddSolution("cross", RoomSolution.Kind.Walk, new Vector3(-12f, 0f, 12f), 270f).Via =
                new[] { new Vector3(-3f, 0f, 12f), new Vector3(-5f, 0f, 12f) };
            AddSolution("pickup", RoomSolution.Kind.Pickup, new Vector3(-12.75f, 0f, 12f), 270f);
            AddSolution("place", RoomSolution.Kind.Place, MarkerFeet, 0f, 0f, 0).Via =
                new[] { new Vector3(-5f, 0f, 12f), new Vector3(-3f, 0f, 12f), new Vector3(-1f, 0f, 8f) };
            AddSolution("far", RoomSolution.Kind.Goal, new Vector3(0f, LedgeTop, 18.5f));
            AddSolution("exit", RoomSolution.Kind.Teleport, ExitPad).Destination = "t2";
        }

        protected internal override void OnRestart(RoomContext ctx)
        {
            if (_director != null) _director.ResetStory();
        }

        /// <summary>
        /// The ledge zone's architecture (world and diorama share it, so a paste lines up exactly). Devices are
        /// added by <see cref="Build"/> to the world only.
        /// </summary>
        public static void BuildLedge(Transform p, bool withStair)
        {
            ArchStyle st = Arch.Style ?? ArchStyle.LightTable;

            // ---- court (x[−4, 4] z[0, 16]) and the ground under the ledge: one floating terrace ----
            Arch.Terrace(p, new RectXZ(-4f, 0f, 4f, 24f), 0f, 3f);

            // ---- the ledge: a sheer formwork mass with a slab on top, articulated by a plinth and pilasters ----
            Arch.Box(p, new Vector3(-4f, 0f, LedgeFront), new Vector3(4f, LedgeTop - 0.5f, 24f), st.Wall);
            Arch.Floor(p, new RectXZ(-4f, LedgeFront, 4f, 24f), LedgeTop, 0.5f);
            Arch.Trim(p, new Vector3(-4f, 0f, LedgeFront), new Vector3(4f, 0f, LedgeFront), Dir.NegZ, TrimProfile.Plinth, 0f);
            Arch.Pilaster(p, new Vector3(-2f, 0f, LedgeFront), LedgeTop - 0.5f, Dir.NegZ);
            Arch.Pilaster(p, new Vector3(2f, 0f, LedgeFront), LedgeTop - 0.5f, Dir.NegZ);
            Arch.Pilaster(p, new Vector3(-3.5f, 0f, LedgeFront), LedgeTop - 0.5f, Dir.NegZ);
            Arch.Pilaster(p, new Vector3(3.5f, 0f, LedgeFront), LedgeTop - 0.5f, Dir.NegZ);

            // Ledge top: parapets, the bracket frame round the exit, planters.
            Arch.Parapet(p, new Vector3(3.875f, LedgeTop, LedgeFront), new Vector3(3.875f, LedgeTop, 24f));
            Arch.Parapet(p, new Vector3(-3.875f, LedgeTop, LedgeFront), new Vector3(-3.875f, LedgeTop, 24f));
            Arch.Parapet(p, new Vector3(-4f, LedgeTop, 23.875f), new Vector3(4f, LedgeTop, 23.875f));
            Arch.BracketFrame(p, ExitPad, Dir.NegZ, 2f, 3f, 0.5f);
            PropKit.Planter(p, new Vector3(-2.75f, LedgeTop, 23f), Dir.NegZ, PlanterStyle.Square, default, 0.625f, 101);
            PropKit.Planter(p, new Vector3(2.75f, LedgeTop, 23f), Dir.NegZ, PlanterStyle.Square, default, 0.625f, 102);
            PropKit.Lamp(p, new Vector3(-3.25f, LedgeTop, 17f), 0f, LampStyle.Bollard);
            PropKit.Lamp(p, new Vector3(3.25f, LedgeTop, 17f), 0f, LampStyle.Bollard);

            // Court parapets: east, south, and west with a gap for the bridge (z[10.5, 13.5]).
            Arch.Parapet(p, new Vector3(3.875f, 0f, 0f), new Vector3(3.875f, 0f, LedgeFront));
            Arch.Parapet(p, new Vector3(-4f, 0f, 0.125f), new Vector3(4f, 0f, 0.125f));
            Arch.Parapet(p, new Vector3(-3.875f, 0f, 0f), new Vector3(-3.875f, 0f, 10.5f));
            Arch.Parapet(p, new Vector3(-3.875f, 0f, 13.5f), new Vector3(-3.875f, 0f, LedgeFront));
            // The bridge threshold: a freestanding bracket frame (the Viewfinder "standing frame").
            Arch.BracketFrame(p, new Vector3(-3.75f, 0f, 12f), Dir.NegX, 2f, 3f, 0.5f);
            // Lanterns on the parapet ends either side of the bridge head (and at the island's landing).
            PropKit.Lamp(p, new Vector3(-3.875f, Arch.RailHeight, 10.25f), 0f, LampStyle.Lantern);
            PropKit.Lamp(p, new Vector3(-3.875f, Arch.RailHeight, 13.75f), 0f, LampStyle.Lantern);

            // Court furniture (scale, by the spawn; outside the photo's view).
            PropKit.Bench(p, new Vector3(-2.75f, 0f, 2.5f), Dir.PosX, 2f);
            PropKit.Planter(p, new Vector3(3f, 0f, 1.25f), Dir.NegX, PlanterStyle.Square, default, 0.625f, 103);
            PropKit.Planter(p, new Vector3(3f, 0f, 4f), Dir.NegX, PlanterStyle.Square, default, 0.625f, 104);
            PropKit.Lamp(p, new Vector3(-3.25f, 0f, 6f), 0f, LampStyle.Bollard);
            PropKit.Lamp(p, new Vector3(3.25f, 0f, 9f), 0f, LampStyle.Bollard);

            // ---- a house on an east annex beside the court (rule 4: a building mass with datum windows, a cornice,
            //      a roof with scuppers; behind the marker, so outside the stair photo's cone) ----
            Arch.Terrace(p, new RectXZ(4f, -1f, 12.5f, 9f), 0f, 3f);
            Arch.House(p, new RectXZ(6.25f, -0.25f, 11.75f, 7.75f), 0f, 1, Dir.NegX);

            // ---- the plank bridge: fixed east deck (railed on its north side), the gap, the west abutment ----
            Surf oak = new Surf(Mat.Oak, Pat.Boards);
            Arch.Box(p, new Vector3(CollapseX1, -0.25f, BridgeZ0), new Vector3(-4f, 0f, BridgeZ1), oak);
            Arch.Box(p, new Vector3(CollapseX1, -0.5f, 11.25f), new Vector3(-4f, -0.25f, 11.5f), Mat.Graphite);
            Arch.Box(p, new Vector3(CollapseX1, -0.5f, 12.5f), new Vector3(-4f, -0.25f, 12.75f), Mat.Graphite);
            Arch.Railing(p, new Vector3(CollapseX1, 0f, 12.875f), new Vector3(-4f, 0f, 12.875f));
            Arch.Floor(p, new RectXZ(-11f, 10.5f, CollapseX0, 13.5f), 0f, 1f);
            Arch.Parapet(p, new Vector3(-11f, 0f, 13.375f), new Vector3(CollapseX0, 0f, 13.375f));
            Arch.Parapet(p, new Vector3(-11f, 0f, 10.625f), new Vector3(CollapseX0, 0f, 10.625f));

            // ---- island E (x[−17, −11] z[8, 16]): the photo's home ----
            Arch.Terrace(p, new RectXZ(-17f, 8f, -11f, 16f), 0f, 3f);
            Arch.Parapet(p, new Vector3(-16.875f, 0f, 8f), new Vector3(-16.875f, 0f, 16f));
            Arch.Parapet(p, new Vector3(-17f, 0f, 15.875f), new Vector3(-11f, 0f, 15.875f));
            Arch.Parapet(p, new Vector3(-17f, 0f, 8.125f), new Vector3(-11f, 0f, 8.125f));
            Arch.Parapet(p, new Vector3(-11.125f, 0f, 8f), new Vector3(-11.125f, 0f, 10.5f));
            Arch.Parapet(p, new Vector3(-11.125f, 0f, 13.5f), new Vector3(-11.125f, 0f, 16f));
            PropKit.Lamp(p, new Vector3(-11.125f, Arch.RailHeight, 10.25f), 0f, LampStyle.Lantern);
            PropKit.Lamp(p, new Vector3(-11.125f, Arch.RailHeight, 13.75f), 0f, LampStyle.Lantern);
            PropKit.Planter(p, new Vector3(-15.75f, 0f, 9.5f), Dir.PosX, PlanterStyle.Square, default, 0.625f, 105);
            PropKit.Planter(p, new Vector3(-15.75f, 0f, 14.5f), Dir.PosX, PlanterStyle.Square, default, 0.625f, 106);
            PropKit.Plant(p, new Vector3(-16.25f, 0f, 12f), PlantKind.Cypress, 1f, 0f, 107);
            PropKit.Lamp(p, new Vector3(-12f, 0f, 9f), 0f, LampStyle.Bollard);

            // ---- the photo's delta: a 12-step stair, foot at z = 10, top flush with the ledge at z = 16 ----
            if (withStair) Arch.Stair(p, new Vector3(0f, 0f, StairFoot), Dir.PosZ, LedgeTop, 2f, true);
        }
    }

    /// <summary>
    /// T1's story beat (art bible §7.1 step 3): once the bridge has collapsed and the player has been rewound out
    /// of the fall (WorldHistory.FallRecovered in this zone), the court button powers up (it rises and lights Ion).
    /// One-way (never in the rewind history or a checkpoint). Fallback so nobody is ever stuck: if the player
    /// dodged the fall, the button wakes after 3 s on the court once the bridge is gone.
    /// </summary>
    public sealed class LedgeDirector : MonoBehaviour
    {
        RoomContext _ctx;
        Switch _button;
        StoryCollapse _collapse;
        WorldHistory _history;
        bool _recovered;
        float _courtSeconds;

        /// <summary>True once the button has woken (tests).</summary>
        public bool Woken => _button != null && _button.Powered;

        internal void Init(RoomContext ctx, Switch button, StoryCollapse collapse)
        {
            _ctx = ctx;
            _button = button;
            _collapse = collapse;
        }

        void OnDestroy()
        {
            if (_history != null) _history.FallRecovered -= OnFallRecovered;
        }

        void OnFallRecovered()
        {
            var game = GameBootstrap.Instance;
            if (game == null || _ctx == null || game.CurrentRoom != _ctx.Index) return;
            if (_collapse != null && _collapse.Fired) _recovered = true;
        }

        void Update()
        {
            if (_history == null)
            {
                _history = WorldHistory.Instance;
                if (_history != null) _history.FallRecovered += OnFallRecovered;
            }
            if (_button == null || _button.Powered || _collapse == null || !_collapse.Fired) return;
            var game = GameBootstrap.Instance;
            if (game == null || game.CurrentRoom != _ctx.Index) return;

            if (_recovered)
            {
                WakeUp();
                return;
            }

            // Fallback: back on the court, safe and grounded, with the bridge gone.
            var player = FirstPersonController.Current;
            if (player == null || !player.IsGrounded) { _courtSeconds = 0f; return; }
            Vector3 local = _ctx.WorldRoot.InverseTransformPoint(player.transform.position);
            bool onCourt = local.x > -4f && local.x < 4f && local.z > 0f && local.z < TutorialLedge.LedgeFront &&
                           Mathf.Abs(local.y) < 0.3f;
            _courtSeconds = onCourt ? _courtSeconds + Time.deltaTime : 0f;
            if (_courtSeconds >= 3f) WakeUp();
        }

        void WakeUp()
        {
            _button.SetPowered(true, true);
            RoomContext.Toast("Rewound.  Something woke up on the court.", 4f);
        }

        /// <summary>"Play again": the button sleeps again if its bridge can still give way.</summary>
        public void ResetStory()
        {
            _recovered = false;
            _courtSeconds = 0f;
            if (_button != null && _collapse != null && !_collapse.Fired) _button.SetPowered(false, false);
        }
    }
}
