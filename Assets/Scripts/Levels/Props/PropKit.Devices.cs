using Ion.Gameplay;
using Ion.Presentation;
using Ion.Presentation.Motion;
using Ion.Projection;
using UnityEngine;

namespace Ion.Levels.Props
{
    using Ion.Levels.Arch;
    using B = PropBuild;

    public static partial class PropKit
    {
        // ====================================================================== teleporter

        /// <summary>
        /// Teleporter pod (Interactable, never sliced). Floor disc: a stepped 8-gon 1.5 Ø × 0.25 (Concrete, an Ion
        /// band, Limestone top) with two brass footprint plates. Pod back 1.0 × 2.25 × 0.5 in <paramref name="tint"/>
        /// behind the disc (−Z), with a 0.5 × 0.375 Ion porthole in a Graphite bracket surround. Two Graphite side
        /// rails shaped "[" and "]" frame the player. Pivot = disc centre on the floor; enter from the front (+Z).
        /// Existing <see cref="Ion.Gameplay.Teleporter"/> + <see cref="TeleporterFx"/>; photo copies share
        /// <paramref name="onEnter"/>.
        /// </summary>
        public static Ion.Gameplay.Teleporter Teleporter(Transform p, Vector3 basePos, Dir facing, System.Action onEnter,
                                                         Mat tint = Mat.Graphite)
        {
            // One device language: pods are off-white Paper (or a zone tint), never a graphite slab that reads as a hole.
            if (tint == Mat.Graphite) tint = Mat.Paper;
            GameObject go = B.DeviceRoot(p, "Teleporter", basePos, Quaternion.Euler(0f, B.Yaw(facing), 0f));
            Transform t = go.transform;
            Transform body = B.Frame(t, "Body", Vector3.zero, Quaternion.identity);
            const ArchFlags f = B.Device;

            // Disc: stepped like a plinth, with a thin Ion band in the step.
            B.Prism(body, Vector3.zero, 0.75f, 0.125f, 8, ConcreteSurf, f);
            B.Prism(body, new Vector3(0f, 0.125f, 0f), 0.71875f, 0.03125f, 8, Mat.Ion, f);
            B.Prism(body, new Vector3(0f, 0.15625f, 0f), 0.6875f, 0.09375f, 8, TrimSurf, f);
            // Brass footprint plates.
            B.Box(body, 0.0625f, 0.25f, -0.0625f, 0.1875f, 0.2578125f, 0.25f, Mat.Brass, f);
            B.Box(body, -0.1875f, 0.25f, -0.0625f, -0.0625f, 0.2578125f, 0.25f, Mat.Brass, f);

            // Pod back.
            Surf podSurf = IsStructure(tint) ? new Surf(tint, Pat.Formwork) : (Surf)tint;
            B.Box(body, -0.5625f, 0f, -1.3125f, 0.5625f, 0.125f, -0.6875f, TrimSurf, f);                     // plinth
            B.Chamfer(body, -0.5f, 0.125f, -1.25f, 0.5f, 2.25f, -0.75f, podSurf, f);
            B.Chamfer(body, -0.5625f, 2.25f, -1.3125f, 0.5625f, 2.375f, -0.6875f, TrimSurf, f, 0.03125f);   // cornice
            // Porthole screen + Graphite bracket surround ("[ ]" around the ion window).
            B.Box(body, -0.25f, 1.4375f, -0.75f, 0.25f, 1.8125f, -0.734375f, Mat.Ion, f);
            B.BracketClip(body, 0.3125f, 1.375f, 0.5f, -0.75f, 0.125f, false, f, Mat.Graphite, 0.0625f, 0.0625f);
            B.BracketClip(body, -0.3125f, 1.375f, 0.5f, -0.75f, 0.125f, true, f, Mat.Graphite, 0.0625f, 0.0625f);
            B.Rivet(body, new Vector3(0.34375f, 1.84375f, -0.671875f), f);
            B.Rivet(body, new Vector3(-0.34375f, 1.84375f, -0.671875f), f);

            // Side rails: "[" on the viewer's left (+X), "]" on the right, 3 boxes each, 0.0625 sections.
            for (int s = -1; s <= 1; s += 2)
            {
                float xo = s * 0.875f, xb = s * 0.8125f, xr = s * 0.625f;
                B.Box(body, xo, 0f, -0.03125f, xb, 2.25f, 0.03125f, Mat.Graphite, f);
                B.Box(body, xb, 2.1875f, -0.03125f, xr, 2.25f, 0.03125f, Mat.Graphite, f);
                B.Box(body, xb, 0.25f, -0.03125f, xr, 0.3125f, 0.03125f, Mat.Graphite, f);
                B.Rivet(body, new Vector3(s * 0.84375f, 2.21875f, 0.0625f), f);
                B.Rivet(body, new Vector3(s * 0.84375f, 0.28125f, 0.0625f), f);
            }
            B.BakeDevice(body.gameObject);
            // Ultra: the porthole and the ion band light the pod's surroundings.
            Ion.Presentation.Quality.LocalLights.Add(t, new Vector3(0f, 1.3f, 0.1f), LightColor(Mat.Ion), 4.5f, 1.5f);

            // Colliders: the disc (convex 8-gon) and the pod back; the trigger volume on the root.
            var disc = new GameObject("DiscCollider");
            disc.transform.SetParent(t, false);
            disc.transform.localPosition = new Vector3(0f, 0.125f, 0f);
            disc.transform.localScale = new Vector3(1.5f, 0.25f, 1.5f);
            var dc = disc.AddComponent<MeshCollider>();
            dc.sharedMesh = Geo.PrismMesh(8);
            dc.convex = true;
            B.Collider(t, new Vector3(-0.5f, 0f, -1.25f), new Vector3(0.5f, 2.375f, -0.75f), "PodCollider");
            var trigger = go.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(1.6f, 2.4f, 1.5f);
            trigger.center = new Vector3(0f, 1.2f, 0.05f);

            B.Deactivate(go);
            var tp = go.AddComponent<Ion.Gameplay.Teleporter>();
            tp.TriggerSize = trigger.size;
            tp.OnEnter = onEnter;
            go.AddComponent<TeleporterFx>();
            B.Activate(go);
            return tp;
        }

        /// <summary>Photo shape of the pre-made photos (4:3), used until a shot reports its own.</summary>
        const float DefaultAspect = 4f / 3f;

        static bool IsStructure(Mat m) =>
            m == Mat.Paper || m == Mat.Plaster || m == Mat.Rose || m == Mat.Mint || m == Mat.Concrete || m == Mat.Limestone;

        // ====================================================================== photo display

        /// <summary>
        /// A photo on display, ready to pick up: a Standard pedestal (sliceable) with a Graphite slide stand holding
        /// the print tilted 15°, an Ion edge breathing while it is available. The returned
        /// <see cref="PhotoPickup"/> is the print itself (Interactable); it appears once the shot is captured.
        /// </summary>
        public static PhotoPickup PhotoDisplay(Transform p, Vector3 basePos, Dir facing, DioramaShot shot)
        {
            return PhotoDisplay(p, basePos, facing, shot, DisplayMount.Pedestal);
        }

        public static PhotoPickup PhotoDisplay(Transform p, Vector3 basePos, Dir facing, PhotoData photo)
        {
            return PhotoDisplay(p, basePos, facing, photo, DisplayMount.Pedestal);
        }

        /// <summary>Photo display on a pedestal or on an easel (T2). Addition.</summary>
        public static PhotoPickup PhotoDisplay(Transform p, Vector3 basePos, Dir facing, DioramaShot shot, DisplayMount mount)
        {
            float aspect = shot != null && shot.Aspect > 0.1f ? shot.Aspect : DefaultAspect;
            PhotoPickup pickup = BuildDisplay(p, basePos, facing, mount, aspect, shot != null ? shot.Label : null);
            BindPickup(pickup, shot, null);
            return pickup;
        }

        /// <summary>Photo display (pedestal or easel) for a photo that already exists. Addition.</summary>
        public static PhotoPickup PhotoDisplay(Transform p, Vector3 basePos, Dir facing, PhotoData photo, DisplayMount mount)
        {
            float aspect = photo != null && photo.Aspect > 0.1f ? photo.Aspect : DefaultAspect;
            PhotoPickup pickup = BuildDisplay(p, basePos, facing, mount, aspect, photo != null ? photo.Label : null);
            Arm(pickup, photo, null);
            return pickup;
        }

        static void BindPickup(PhotoPickup pickup, DioramaShot shot, ExhibitStand stand)
        {
            if (pickup == null || shot == null) return;
            if (shot.Photo != null) { Arm(pickup, shot.Photo, stand); return; }
            shot.Captured += photo => Arm(pickup, photo, stand);
        }

        /// <summary>Gives the pickup its photo and shows it (unless an exhibit keeps it hidden).</summary>
        static void Arm(PhotoPickup pickup, PhotoData photo, ExhibitStand stand)
        {
            if (pickup == null) return;
            pickup.Photo = photo;
            PrintCard card = pickup.GetComponentInChildren<PrintCard>(true);
            if (card != null) card.Bind(photo);
            if (stand != null) stand.RefreshPickup();
            else B.Activate(pickup.gameObject);
        }

        static PhotoPickup BuildDisplay(Transform p, Vector3 basePos, Dir facing, DisplayMount mount, float aspect, string label)
        {
            Vector3 bottom;     // frame-local bottom-back edge of the print
            Quaternion lean;
            Vector2 image;
            float border;
            Transform g;
            if (mount == DisplayMount.Easel)
            {
                g = BuildEasel(p, basePos, facing, out bottom, out lean);
                const float outerW = 0.8f;
                border = 0.04f;
                float iw = outerW - 2f * border;
                image = new Vector2(iw, iw / aspect);
            }
            else
            {
                g = B.Frame(p, "PhotoDisplay", basePos, B.Yaw(facing));
                BuildPedestal(g, 1f, B.Solid);
                bottom = PrintStand(g, 1f);
                lean = Quaternion.Euler(-15f, 0f, 0f);
                border = 0.0625f;
                image = aspect >= 1f ? new Vector2(0.4f, 0.4f / aspect) : new Vector2(0.4f * aspect, 0.4f);
            }
            return BuildPickupPrint(p, g, bottom, lean, image, border, label);
        }

        /// <summary>Graphite slide stand on a top at <paramref name="topY"/>: a front lip and a back strut. Returns the print's bottom-back point.</summary>
        static Vector3 PrintStand(Transform g, float topY)
        {
            B.Box(g, -0.25f, topY, 0.125f, 0.25f, topY + 0.0625f, 0.1875f, Mat.Graphite, B.Soft);
            B.Box(g, -0.28125f, topY, 0.0625f, 0.28125f, topY + 0.015625f, 0.1875f, Mat.Graphite, B.Soft);
            B.Beam(g, new Vector3(0f, topY, -0.1875f), new Vector3(0f, topY + 0.25f, -0.0625f), 0.0625f, Mat.Graphite, B.Soft);
            B.Rivet(g, new Vector3(0.21875f, topY + 0.03125f, 0.203125f), B.Soft);
            B.Rivet(g, new Vector3(-0.21875f, topY + 0.03125f, 0.203125f), B.Soft);
            return new Vector3(0f, topY + 0.015625f, 0.0625f);
        }

        /// <summary>The pickup: an inactive Interactable at the print's bottom edge holding a slide-mounted print with an Ion edge.</summary>
        static PhotoPickup BuildPickupPrint(Transform p, Transform g, Vector3 bottom, Quaternion lean, Vector2 image, float border, string label)
        {
            B.PoseIn(p, g, bottom, Quaternion.identity, out Vector3 pos, out Quaternion rot);
            GameObject go = B.DeviceRoot(p, string.IsNullOrEmpty(label) ? "PhotoPickup" : "PhotoPickup " + label, pos, rot);
            float outerH = image.y + 2f * border;
            Vector3 center = lean * new Vector3(0f, outerH * 0.5f, PrintBuilder.MountThickness * 0.5f);
            PrintCard card = PrintBuilder.Build(go.transform, "Print", center, lean, image, border, true, true, false, true);
            Transform edge = card.transform.Find("IonEdge");
            if (edge != null) edge.gameObject.AddComponent<IonBreath>();
            B.Deactivate(go);
            var pickup = go.AddComponent<PhotoPickup>();
            pickup.Animate = false;
            pickup.VisualHeight = center.y;
            // Stays inactive until it has its photo (Arm).
            return pickup;
        }

        // ====================================================================== camera stand

        /// <summary>
        /// Lectern (Standard pedestal + a ≈15° Limestone wedge top, sliceable) holding the instant camera: Mustard body
        /// 0.25 × 0.15 × 0.18, 12-gon Graphite lens, Ion shutter button, Frost viewfinder, Paper flash. The
        /// <see cref="CameraPickup"/> (Interactable) sits at the lectern's foot; walking up to it takes the camera.
        /// </summary>
        public static CameraPickup CameraStand(Transform p, Vector3 basePos, Dir facing, int film = 3)
        {
            Transform g = B.Frame(p, "CameraStand", basePos, B.Yaw(facing));
            BuildPedestal(g, 1f, B.Solid);
            const float rise = 0.1875f, half = 0.3125f;
            B.Wedge(g, new Vector3(-half, 1f, -half), new Vector3(half, 1f + rise, half), Dir.NegZ, TrimSurf, B.Solid);
            B.Box(g, -0.25f, 1f, half, 0.25f, 1.0625f, half + 0.0625f, Mat.Graphite, B.Soft);   // lip

            B.PoseIn(p, g, Vector3.zero, Quaternion.identity, out Vector3 pos, out Quaternion rot);
            GameObject go = B.DeviceRoot(p, "CameraPickup", pos, rot);
            float a = Mathf.Atan2(rise, 2f * half) * Mathf.Rad2Deg;
            float surfaceY = 1f + rise * 0.5f;
            Vector3 normal = Quaternion.Euler(a, 0f, 0f) * Vector3.up;
            Transform cam = B.Frame(go.transform, "Camera", new Vector3(0f, surfaceY, 0f) + normal * 0.075f, Quaternion.Euler(a, 0f, 0f));
            BuildInstantCamera(cam);
            B.BakeDevice(cam.gameObject);

            B.Deactivate(go);
            var pickup = go.AddComponent<CameraPickup>();
            pickup.Film = film;
            pickup.Visual = cam.gameObject;
            B.Activate(go);
            return pickup;
        }

        /// <summary>The instant camera model, centred on its body, lens toward +Z.</summary>
        static void BuildInstantCamera(Transform c)
        {
            const ArchFlags f = B.Device;
            B.Chamfer(c, -0.125f, -0.075f, -0.09f, 0.125f, 0.075f, 0.09f, Mat.Mustard, f, 0.015f);
            B.Box(c, -0.13f, 0.055f, -0.095f, 0.13f, 0.08f, 0.095f, Mat.Graphite, f);                  // top deck
            Transform lens = B.Frame(c, "Lens", new Vector3(0f, -0.01f, 0.09f), Quaternion.Euler(90f, 0f, 0f));
            B.Prism(lens, Vector3.zero, 0.055f, 0.045f, 12, Mat.Graphite, f);
            B.Prism(lens, new Vector3(0f, 0.045f, 0f), 0.035f, 0.006f, 12, Mat.Cyanotype, f);
            B.Box(c, 0.055f, 0.03f, 0.09f, 0.1f, 0.055f, 0.096f, Mat.Frost, f);                          // viewfinder
            B.Box(c, -0.105f, 0.025f, 0.09f, -0.045f, 0.055f, 0.096f, Mat.Paper, f);                     // flash
            B.Box(c, 0.07f, 0.08f, -0.03f, 0.1f, 0.1f, 0f, Mat.Ion, f);                                  // shutter button
            B.Box(c, -0.09f, -0.068f, 0.09f, 0.09f, -0.052f, 0.095f, Mat.Graphite, f);                  // print slot
            for (int s = -1; s <= 1; s += 2)
                B.Box(c, s * 0.125f, 0.02f, -0.02f, s * 0.14f, 0.05f, 0.02f, Mat.Graphite, f);         // strap lugs
        }

        // ====================================================================== exhibit

        /// <summary>
        /// Hub exhibit (§5, §7.3): a Concrete bracket pier 1.0 × 0.5 (U in plan, open to the front) with a Limestone
        /// cap, a Paper/Sprocket slide-mount frame 1.75 × 1.375 (image 1.6 × 1.2, top at 2.5) with brass crop clips,
        /// the plaque in the pier's niche at 0.9, a 0.125 state lamp on the frame's top-right corner and, on the cap,
        /// the key photo's pickup in a slide stand. Body sliceable; the print, lamp and pickup are Interactable.
        /// Coming soon (no key): a Frost slide and the plaque "[ coming soon ]".
        /// </summary>
        public static ExhibitStand Exhibit(Transform p, Vector3 basePos, Dir facing, ExhibitSpec spec)
        {
            spec = spec ?? new ExhibitSpec();
            bool soon = spec.Key == null;
            Transform g = B.Frame(p, "Exhibit " + spec.Number, basePos, B.Yaw(facing));

            // Pier (U in plan) and cap.
            B.Box(g, -0.5f, 0f, -0.25f, 0.5f, 1.0625f, 0f, ConcreteSurf, B.Solid);
            B.Box(g, -0.5f, 0f, 0f, -0.25f, 1.0625f, 0.25f, ConcreteSurf, B.Solid);
            B.Box(g, 0.25f, 0f, 0f, 0.5f, 1.0625f, 0.25f, ConcreteSurf, B.Solid);
            B.Chamfer(g, -0.5625f, 1.0625f, -0.3125f, 0.5625f, 1.125f, 0.3125f, TrimSurf, B.Solid, 0.015625f);

            // Slide-mount frame standing on the cap's back.
            const float fw = 1.75f, fh = 1.375f, iw = 1.6f, ih = 1.2f, y0 = 1.125f, z0 = -0.25f, z1 = -0.125f;
            float hw = fw * 0.5f, hiw = iw * 0.5f, cy = y0 + fh * 0.5f, hih = ih * 0.5f;
            var sprocket = new Surf(Mat.Paper, Pat.Sprocket);
            B.Box(g, -hiw, cy - hih, z0, hiw, cy + hih, z0 + 0.0625f, new Surf(Mat.Paper, Pat.Formwork), B.Soft);   // print back (Paper: never the strongest shape in the room)
            B.Box(g, hiw, y0, z0, hw, y0 + fh, z1, sprocket, B.Solid);
            B.Box(g, -hw, y0, z0, -hiw, y0 + fh, z1, sprocket, B.Solid);
            B.Box(g, -hiw, cy + hih, z0, hiw, y0 + fh, z1, sprocket, B.Solid);
            B.Box(g, -hiw, y0, z0, hiw, cy - hih, z1, sprocket, B.Solid);
            const float arm = 0.125f, cw = 0.0625f, cz1 = z1 + 0.015625f;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            {
                float ox = sx * hw, oy = sy > 0 ? y0 + fh : y0;
                B.Box(g, new Vector3(ox, oy, z1), new Vector3(ox - sx * arm, oy - sy * cw, cz1), Mat.Brass, B.Soft);
                B.Box(g, new Vector3(ox, oy - sy * cw, z1), new Vector3(ox - sx * cw, oy - sy * arm, cz1), Mat.Brass, B.Soft);
            }

            // Plaque in the niche.
            Transform plaque = B.Frame(g, "Plaque", new Vector3(0f, 0.9f, 0f), Quaternion.identity);
            BuildPlaque(p, plaque, soon ? "[ coming soon ]" : PlaqueText(spec.Number, spec.Title), ArchFlags.Soft);

            // Display node (Interactable): the big print, its Ion border, the state lamp and the plaque tick.
            B.PoseIn(p, g, new Vector3(0f, cy, z0 + 0.0625f), Quaternion.identity, out Vector3 dpos, out Quaternion drot);
            GameObject display = B.DeviceRoot(p, "ExhibitDisplay " + spec.Number, dpos, drot);
            Transform dt = display.transform;
            PrintCard print = PrintBuilder.Build(dt, "Print", new Vector3(0f, 0f, 0.005f), Quaternion.identity, new Vector2(iw, ih),
                                                 0f, true, false, false);
            GameObject border = print.transform.Find("IonEdge") != null ? print.transform.Find("IonEdge").gameObject : null;
            // Lamp: a 0.125 cube on the frame's top-right corner (the viewer's right is −X).
            // (display-local = frame-local minus the node's origin (0, cy, z0 + 0.0625))
            Transform lamp = B.Frame(dt, "Lamp", new Vector3(-hw + 0.0625f, y0 + fh + 0.0625f - cy, (z0 + z1) * 0.5f - (z0 + 0.0625f)),
                                     Quaternion.identity);
            B.Box(lamp, -0.0625f, -0.0625f, -0.0625f, 0.0625f, 0.0625f, 0.0625f, Mat.Graphite, B.Device);
            B.BakeDevice(lamp.gameObject);
            // Tick on the right end of the plaque.
            Transform tick = B.Frame(dt, "Tick", new Vector3(-0.1875f, 0.9f - cy, 0.1f - (z0 + 0.0625f)), Quaternion.identity);
            B.Box(tick, -0.03125f, -0.03125f, -0.0078125f, 0.03125f, 0.03125f, 0.0078125f, Mat.Brass, B.Device);
            B.BakeDevice(tick.gameObject);
            PropBuild.SetMaterial(tick.gameObject, PropBuild.BrassGlow);
            B.Activate(display);

            // Key photo pickup on the cap, in a slide stand.
            PhotoPickup pickup = null;
            if (!soon)
            {
                Vector3 bottom = PrintStand(g, 1.125f);
                float aspect = spec.Key.Aspect > 0.1f ? spec.Key.Aspect : DefaultAspect;
                pickup = BuildPickupPrint(p, g, bottom, Quaternion.Euler(-15f, 0f, 0f), new Vector2(0.4f, 0.4f / aspect), 0.0625f, spec.Key.Label);
            }

            // State view on its own (non-Interactable) object.
            B.PoseIn(p, g, Vector3.zero, Quaternion.identity, out Vector3 spos, out Quaternion srot);
            Transform standT = B.Frame(p, "ExhibitStand " + spec.Number, spos, srot);
            var stand = standT.gameObject.AddComponent<ExhibitStand>();
            stand.Init(spec, print, border, lamp, tick.gameObject, pickup);
            if (!soon)
            {
                print.Bind(spec.Key);
                BindPickup(pickup, spec.Key, stand);
            }
            return stand;
        }

        // ====================================================================== switches

        /// <summary>
        /// Button (Interactable, never sliced). Pedestal: a Standard pedestal with a 0.25² × 0.0625 cap in a Graphite
        /// bracket bezel; while unpowered it sits 0.25 lower and rises (0.6 s) when powered. Wall: a Graphite plate
        /// at 1.25 on the wall face (<paramref name="basePos"/> = the face at floor level). The cap is Ion when
        /// powered and off, Brass when on, Graphite when unpowered; it travels 0.03 when pressed. A photo of the
        /// button pastes a working button (same channel).
        /// </summary>
        public static Switch Button(Transform p, Vector3 basePos, Dir facing, string channel,
                                    ButtonStyle style = ButtonStyle.Pedestal, bool powered = true)
        {
            GameObject go = B.DeviceRoot(p, "Button " + channel, basePos, Quaternion.Euler(0f, B.Yaw(facing), 0f));
            Transform t = go.transform;
            Transform cap;
            Transform riser = null;
            Vector3 pressDir;
            float riseDepth = 0f;
            const ArchFlags f = B.Device;

            if (style == ButtonStyle.Wall)
            {
                Transform body = B.Frame(t, "Body", Vector3.zero, Quaternion.identity);
                B.Chamfer(body, -0.28125f, 0.96875f, 0f, 0.28125f, 1.53125f, 0.0625f, Mat.Graphite, f, 0.015625f);
                B.Box(body, -0.25f, 1f, 0.0625f, 0.25f, 1.5f, 0.078125f, TrimSurf, f);
                B.BracketClip(body, 0.21875f, 1.0625f, 0.375f, 0.078125f, 0.125f, false, f, Mat.Graphite, 0.0625f, 0.03125f);
                B.BracketClip(body, -0.21875f, 1.0625f, 0.375f, 0.078125f, 0.125f, true, f, Mat.Graphite, 0.0625f, 0.03125f);
                for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    B.Rivet(body, new Vector3(sx * 0.21875f, 1.25f + sy * 0.21875f, 0.0703125f), f);
                B.BakeDevice(body.gameObject);
                cap = B.Frame(t, "Cap", new Vector3(0f, 1.25f, 0.078125f), Quaternion.identity);
                // A Paper face in a state ring (Ion / Brass / Graphite): reads as a button, never a dark blob.
                CapRing(cap, f, false);
                B.Chamfer(cap, -0.09375f, -0.09375f, 0f, 0.09375f, 0.09375f, 0.078125f, Mat.Paper, f, 0.015625f);
                pressDir = Vector3.back;
                B.Collider(t, new Vector3(-0.28125f, 0.96875f, 0f), new Vector3(0.28125f, 1.53125f, 0.140625f));
            }
            else
            {
                riser = B.Frame(t, "Riser", Vector3.zero, Quaternion.identity);
                Transform body = B.Frame(riser, "Body", Vector3.zero, Quaternion.identity);
                BuildPedestal(body, 1f, f);
                // Brass bracket bezel around the cap ("[ ]" in plan; the device bracket family).
                for (int s = -1; s <= 1; s += 2)
                {
                    float xo = s * 0.21875f, xi = s * 0.15625f, xr = s * 0.09375f;
                    B.Box(body, xo, 1f, -0.1875f, xi, 1.0625f, 0.1875f, Mat.Brass, f);
                    B.Box(body, xi, 1f, 0.125f, xr, 1.0625f, 0.1875f, Mat.Brass, f);
                    B.Box(body, xi, 1f, -0.1875f, xr, 1.0625f, -0.125f, Mat.Brass, f);
                }
                B.BakeDevice(body.gameObject);
                cap = B.Frame(riser, "Cap", new Vector3(0f, 1f, 0f), Quaternion.identity);
                CapRing(cap, f, true);
                B.Chamfer(cap, -0.09375f, 0f, -0.09375f, 0.09375f, 0.078125f, 0.09375f, Mat.Paper, f, 0.015625f);
                pressDir = Vector3.down;
                riseDepth = Feel.PowerUpRise;
                B.Collider(riser, new Vector3(-0.375f, 0f, -0.375f), new Vector3(0.375f, 1.0625f, 0.375f));
            }
            B.BakeDevice(cap.gameObject);

            B.Deactivate(go);
            var sw = go.AddComponent<Switch>();
            sw.Channel = channel;
            sw.Powered = powered;
            sw.Kind = Switch.SwitchKind.Button;
            sw.Cap = cap;
            sw.CapTravelLocal = pressDir * Feel.ButtonTravel;
            sw.Indicators = IndicatorRenderers(cap);
            SetSwitchMaterials(sw);
            sw.FocusOffset = style == ButtonStyle.Wall ? new Vector3(0f, 1.25f, 0.125f) : new Vector3(0f, 1.05f, 0f);
            if (riser != null)
            {
                sw.RiseRoot = riser;
                sw.RiseHeight = riseDepth;
            }
            B.Activate(go);
            return sw;
        }

        /// <summary>
        /// The state ring of a button cap (the Switch recolours it): four 1/32 bars round a 0.1875 Paper face, 0.25
        /// outside. <paramref name="up"/>: a pedestal cap (ring in XZ, 0 → 0.0625 up); else a wall cap (ring in XY, out +Z).
        /// </summary>
        static void CapRing(Transform cap, ArchFlags f, bool up)
        {
            const float o = 0.125f, i = 0.09375f, t = 0.0625f;
            if (up)
            {
                B.Box(cap, -o, 0f, -o, o, t, -i, Mat.Ion, f);
                B.Box(cap, -o, 0f, i, o, t, o, Mat.Ion, f);
                B.Box(cap, -o, 0f, -i, -i, t, i, Mat.Ion, f);
                B.Box(cap, i, 0f, -i, o, t, i, Mat.Ion, f);
            }
            else
            {
                B.Box(cap, -o, -o, 0f, o, -i, t, Mat.Ion, f);
                B.Box(cap, -o, i, 0f, o, o, t, Mat.Ion, f);
                B.Box(cap, -o, -i, 0f, -i, i, t, Mat.Ion, f);
                B.Box(cap, i, -i, 0f, o, i, t, Mat.Ion, f);
            }
        }

        /// <summary>The cap renderers that show the switch state (the ring; never the Paper face).</summary>
        static Renderer[] IndicatorRenderers(Transform cap)
        {
            var list = new System.Collections.Generic.List<Renderer>();
            Material ion = Palette.Get(Mat.Ion);
            foreach (Renderer r in cap.GetComponentsInChildren<Renderer>(true))
                if (r.sharedMaterial == ion) list.Add(r);
            return list.Count > 0 ? list.ToArray() : cap.GetComponentsInChildren<Renderer>(true);
        }

        static void SetSwitchMaterials(Switch sw)
        {
            sw.UnpoweredMaterial = Palette.Get(Mat.Graphite);
            sw.OffMaterial = Palette.Get(Mat.Ion);
            sw.OnMaterial = Palette.Get(Mat.Brass);
        }

        /// <summary>
        /// Floor lever (Interactable): TextileRed base 0.5 × 0.375 × 0.25 with a Graphite slot and brass rivets, a
        /// Graphite handle 0.05² × 0.5 that rotates ±35° (back = off, front = on) and a knob lit like a button cap.
        /// </summary>
        public static Switch Lever(Transform p, Vector3 basePos, Dir facing, string channel)
        {
            GameObject go = B.DeviceRoot(p, "Lever " + channel, basePos, Quaternion.Euler(0f, B.Yaw(facing), 0f));
            Transform t = go.transform;
            const ArchFlags f = B.Device;
            Transform body = B.Frame(t, "Body", Vector3.zero, Quaternion.identity);
            B.Chamfer(body, -0.25f, 0f, -0.1875f, 0.25f, 0.25f, 0.1875f, Mat.TextileRed, f);
            B.Box(body, -0.046875f, 0.25f, -0.15625f, 0.046875f, 0.265625f, 0.15625f, Mat.Graphite, f);
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                B.Rivet(body, new Vector3(sx * 0.1875f, 0.25f, sz * 0.125f), f);
            B.BakeDevice(body.gameObject);

            // The handle's rest rotation is upright; the Switch turns it ±35° about local X (back = off, front = on).
            Transform handle = B.Frame(t, "Handle", new Vector3(0f, 0.25f, 0f), Quaternion.identity);
            Transform stick = B.Frame(handle, "Stick", Vector3.zero, Quaternion.identity);
            B.Box(stick, -0.025f, -0.03125f, -0.025f, 0.025f, 0.5f, 0.025f, Mat.Graphite, f);
            B.BakeDevice(stick.gameObject);
            Transform knob = B.Frame(handle, "Knob", new Vector3(0f, 0.5f, 0f), Quaternion.identity);
            B.Prism(knob, Vector3.zero, 0.0625f, 0.09375f, 8, Mat.Ion, f);
            B.BakeDevice(knob.gameObject);
            B.Collider(t, new Vector3(-0.25f, 0f, -0.1875f), new Vector3(0.25f, 0.25f, 0.1875f));

            B.Deactivate(go);
            var sw = go.AddComponent<Switch>();
            sw.Channel = channel;
            sw.Kind = Switch.SwitchKind.Lever;
            sw.Handle = handle;
            sw.HandleAxisLocal = Vector3.right;
            sw.HandleAngle = Feel.LeverAngleDeg;
            sw.Indicators = knob.GetComponentsInChildren<Renderer>(true);
            SetSwitchMaterials(sw);
            sw.FocusOffset = new Vector3(0f, 0.6f, 0f);
            B.Activate(go);
            return sw;
        }

        // ====================================================================== movers

        /// <summary>
        /// Sliding bridge (Interactable Mover): an Oak/Boards deck filling the box [<paramref name="stowedMin"/>,
        /// <paramref name="stowedMax"/>] (p-local, where it stows inside a terrace body), Graphite kerbs 0.125 along the
        /// sides parallel to <paramref name="travel"/>, brass rivets and an Ion nosing on its leading end. When
        /// <paramref name="channel"/> is on it slides by <paramref name="travel"/> (1.6 s per 4 m).
        /// </summary>
        public static Mover SlidingBridge(Transform p, Vector3 stowedMin, Vector3 stowedMax, Vector3 travel, string channel)
        {
            Vector3 lo = Vector3.Min(stowedMin, stowedMax), hi = Vector3.Max(stowedMin, stowedMax);
            Vector3 pivot = new Vector3((lo.x + hi.x) * 0.5f, hi.y, (lo.z + hi.z) * 0.5f);
            GameObject go = B.DeviceRoot(p, "SlidingBridge " + channel, pivot, Quaternion.identity);
            Transform deck = B.Frame(go.transform, "Deck", Vector3.zero, Quaternion.identity);
            Transform vis = B.Frame(deck, "Visual", Vector3.zero, Quaternion.identity);
            const ArchFlags f = B.Device;
            Vector3 a = lo - pivot, b = hi - pivot;     // deck box, deck-local (top at y = 0)
            B.Box(vis, a, b, OakBoards, f);
            bool alongX = Mathf.Abs(travel.x) >= Mathf.Abs(travel.z);
            const float kerb = 0.125f;
            if (alongX)
            {
                B.Box(vis, a.x, 0f, a.z, b.x, kerb, a.z + kerb, Mat.Graphite, f);
                B.Box(vis, a.x, 0f, b.z - kerb, b.x, kerb, b.z, Mat.Graphite, f);
                float lead = travel.x >= 0f ? b.x : a.x;
                B.Box(vis, lead - (travel.x >= 0f ? 0.0625f : -0.0625f), 0f, a.z + kerb, lead, 0.015625f, b.z - kerb, Mat.Ion, f);
                for (float x = a.x + 0.5f; x < b.x - 0.25f; x += 1f)
                {
                    B.Rivet(vis, new Vector3(x, kerb + 0.015625f, a.z + kerb * 0.5f), f);
                    B.Rivet(vis, new Vector3(x, kerb + 0.015625f, b.z - kerb * 0.5f), f);
                }
            }
            else
            {
                B.Box(vis, a.x, 0f, a.z, a.x + kerb, kerb, b.z, Mat.Graphite, f);
                B.Box(vis, b.x - kerb, 0f, a.z, b.x, kerb, b.z, Mat.Graphite, f);
                float lead = travel.z >= 0f ? b.z : a.z;
                B.Box(vis, a.x + kerb, 0f, lead - (travel.z >= 0f ? 0.0625f : -0.0625f), b.x - kerb, 0.015625f, lead, Mat.Ion, f);
                for (float z = a.z + 0.5f; z < b.z - 0.25f; z += 1f)
                {
                    B.Rivet(vis, new Vector3(a.x + kerb * 0.5f, kerb + 0.015625f, z), f);
                    B.Rivet(vis, new Vector3(b.x - kerb * 0.5f, kerb + 0.015625f, z), f);
                }
            }
            B.BakeDevice(vis.gameObject);
            B.Collider(deck, a, b, "DeckCollider");

            B.Deactivate(go);
            var mover = deck.gameObject.AddComponent<Mover>();
            mover.Channel = channel;
            mover.OffLocal = Vector3.zero;
            mover.OnLocal = travel;
            mover.Duration = Feel.MoverSecondsPer4m;   // per ≤ 4 m; Mover scales longer travels
            B.Activate(go);
            return mover;
        }

        /// <summary>
        /// Gate (Interactable Mover): a Paper/Formwork slab 0.25 thick with a Limestone coping and a Graphite shoe,
        /// standing in an opening <paramref name="width"/> × <paramref name="height"/> whose sill centre is
        /// <paramref name="sillCenter"/>; it sinks <paramref name="height"/> into a floor slot when open, with a Graphite
        /// slot plate on the sill. The bracket surround on the fixed wall is the wall's own (Arch.Wall's
        /// Opening.Door has Bracket = true by default); for a gate without such a wall use the overload with
        /// <c>surround: true</c>.
        /// </summary>
        public static Mover Gate(Transform p, Vector3 sillCenter, Dir facing, float width, float height, string channel,
                                 bool openWhenOn = true)
        {
            return Gate(p, sillCenter, facing, width, height, channel, openWhenOn, false, 0.5f);
        }

        /// <summary>
        /// Gate that also builds the sliceable bracket surround on both faces of a <paramref name="wallThickness"/>
        /// wall (for openings whose wall has no surround of its own). Addition.
        /// </summary>
        public static Mover Gate(Transform p, Vector3 sillCenter, Dir facing, float width, float height, string channel,
                                 bool openWhenOn, bool surround, float wallThickness = 0.5f)
        {
            float yaw = B.Yaw(facing);
            float hw = width * 0.5f;
            Transform frame = B.Frame(p, "GateFrame", sillCenter, yaw);
            if (surround)
            {
                float half = Mathf.Max(0.25f, wallThickness) * 0.5f;
                B.BracketSurround(frame, width, height, half, 1f, true, TrimSurf, ArchFlags.Soft);
                B.BracketSurround(frame, width, height, -half, -1f, true, TrimSurf, ArchFlags.Soft);
            }
            B.Box(frame, -hw - 0.0625f, 0f, -0.1875f, hw + 0.0625f, 0.01f, 0.1875f, Mat.Graphite, ArchFlags.Soft);   // slot plate

            GameObject go = B.DeviceRoot(p, "Gate " + channel, sillCenter, Quaternion.Euler(0f, yaw, 0f));
            Transform slab = B.Frame(go.transform, "Slab", Vector3.zero, Quaternion.identity);
            Transform vis = B.Frame(slab, "Visual", Vector3.zero, Quaternion.identity);
            const ArchFlags f = B.Device;
            B.Box(vis, -hw, 0f, -0.140625f, hw, 0.0625f, 0.140625f, Mat.Graphite, f);                     // shoe
            B.Box(vis, -hw, 0.0625f, -0.125f, hw, height - 0.0625f, 0.125f, PaperForm, f);
            B.Chamfer(vis, -hw, height - 0.0625f, -0.15625f, hw, height, 0.15625f, TrimSurf, f, 0.015625f); // coping
            for (int s = -1; s <= 1; s += 2)
            {
                B.Rivet(vis, new Vector3(s * (hw - 0.1875f), height - 0.1875f, 0.140625f), f);
                B.Rivet(vis, new Vector3(s * (hw - 0.1875f), height - 0.1875f, -0.140625f), f);
            }
            B.BakeDevice(vis.gameObject);
            B.Collider(slab, new Vector3(-hw, 0f, -0.125f), new Vector3(hw, height, 0.125f), "SlabCollider");

            Vector3 closed = Vector3.zero, open = new Vector3(0f, -height, 0f);
            B.Deactivate(go);
            var mover = slab.gameObject.AddComponent<Mover>();
            mover.Channel = channel;
            mover.OffLocal = openWhenOn ? closed : open;
            mover.OnLocal = openWhenOn ? open : closed;
            mover.Duration = Feel.MoverSecondsPer4m;
            slab.localPosition = mover.OffLocal;
            B.Activate(go);
            return mover;
        }

        // ====================================================================== collapse slab

        /// <summary>
        /// A floor or deck piece that gives way (Interactable StoryCollapse, one-way story event): the box
        /// [<paramref name="min"/>, <paramref name="max"/>] (p-local) in <paramref name="s"/>, looking exactly like
        /// the surface it continues (its pattern space is p-local, so joints line up with the zone around it).
        /// Pivot = the centre of its top face; a box collider; the StoryCollapse wobbles and drops its own transform.
        /// </summary>
        public static StoryCollapse CollapseSlab(Transform p, Vector3 min, Vector3 max, Surf s)
        {
            Vector3 lo = Vector3.Min(min, max), hi = Vector3.Max(min, max);
            Vector3 c = new Vector3((lo.x + hi.x) * 0.5f, hi.y, (lo.z + hi.z) * 0.5f);
            GameObject go = B.DeviceRoot(p, "CollapseSlab", c, Quaternion.identity);
            // Visual frame at p's origin: pattern space = p-local, continuous with the baked zone.
            Transform vis = B.Frame(go.transform, "Visual", -c, Quaternion.identity);
            if (hi.y - lo.y >= 0.375f)
            {
                // SlabEdge profile (§2.2): top layer, inset drip groove, body — reads as the same slab.
                B.Box(vis, new Vector3(lo.x, hi.y - 0.125f, lo.z), hi, s, B.Device);
                B.Box(vis, new Vector3(lo.x + 0.0625f, hi.y - 0.1875f, lo.z + 0.0625f),
                      new Vector3(hi.x - 0.0625f, hi.y - 0.125f, hi.z - 0.0625f), s, B.Device);
                B.Box(vis, lo, new Vector3(hi.x, hi.y - 0.1875f, hi.z), s, B.Device);
            }
            else
            {
                B.Box(vis, lo, hi, s, B.Device);
            }
            B.BakeDevice(vis.gameObject);
            B.Collider(go.transform, lo - c, hi - c);
            B.Deactivate(go);
            var sc = go.AddComponent<StoryCollapse>();
            sc.Delay = Feel.CollapseWarnSeconds;
            B.Activate(go);
            return sc;
        }

        // ====================================================================== checkpoint marker

        /// <summary>
        /// Checkpoint marker (not Interactable, not Sliceable): a <see cref="CheckpointMarker"/> volume (distance
        /// test) at <paramref name="feet"/>, plus a sliceable brass "[ ]" floor inlay around a 0.125 Ion dot. When
        /// the checkpoint <paramref name="id"/> is reached, an Ion square pulses out of the dot once.
        /// </summary>
        public static CheckpointMarker Checkpoint(Transform p, Vector3 feet, float yaw, string id)
        {
            Transform g = B.Frame(p, "CheckpointInlay " + id, feet, yaw);
            BracketInlay(g, 0.3125f, 0.25f, 0.1875f, Mat.Brass, ArchFlags.Soft);
            B.Exempt(g);

            var go = new GameObject("Checkpoint " + id);
            go.SetActive(false);
            go.transform.SetParent(p, false);
            go.transform.localPosition = feet;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            // The Ion dot is its own small renderer (not baked): CheckpointMarker scales it when the checkpoint is set.
            var dot = new GameObject("Dot");
            dot.transform.SetParent(go.transform, false);
            dot.transform.localPosition = new Vector3(0f, 0.005f, 0f);
            dot.transform.localScale = new Vector3(0.125f, 0.01f, 0.125f);
            dot.AddComponent<MeshFilter>().sharedMesh = Geo.CubeMesh;
            var r = dot.AddComponent<MeshRenderer>();
            r.sharedMaterial = Palette.Get(Mat.Ion);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var marker = go.AddComponent<CheckpointMarker>();
            marker.Id = id;
            marker.Dot = dot.transform;
            go.AddComponent<CheckpointPulse>().Init(id);
            go.SetActive(true);
            return marker;
        }
    }
}
