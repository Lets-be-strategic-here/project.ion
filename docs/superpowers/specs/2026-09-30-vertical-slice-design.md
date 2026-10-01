# |project|ion — Vertical Slice Design

Date: 2026-09-30 · Status: approved for build (user: "playable/presentable thing first")

## Goal

A playable, presentable first-person puzzle game in the browser that recreates Viewfinder's
photo-projection mechanic. The portfolio content (real projects) comes later; this slice uses
simple low-poly rooms. Desktop browsers only (no mobile).

Success = from a fresh load of the GitHub Pages build, a player can click to play, walk
through 5 short rooms, pick up photos, hold them up, rotate, place them (world is cut and the
photo's contents become real, walkable geometry), rewind placements, use an instant camera to
capture-and-paste, and reach the final teleporter. 60 fps on a mid laptop in Chrome.

## Fixed decisions

- Unity **6000.3.25f1** (6.3 LTS), **URP** with the **Forward** path (not Forward+), linear colour, Input System package.
- Target: **Web** (WebGL2). Compression **Disabled** (GitHub Pages gzips on the fly), Strip Engine Code, Managed Stripping High, IL2CPP "Optimize for code size", Data Caching on, Debug Symbols off, Exceptions None, texture compression DXT, MSAA 4x.
- Hosting: `https://vasiniks.github.io/-project-ion/`. Local build to `Build/Web`; CI via GameCI later (needs the user's license secret).
- **Everything is built from code.** There are no hand-authored scenes or prefabs. An editor script (`-executeMethod`) creates the URP assets, Player settings and a single `Main.unity`, which contains one `GameBootstrap` object. At runtime `GameBootstrap` builds the rooms, the player, the UI and the photo dioramas. Agents can therefore author and verify everything in batch mode.
- **Low poly:** levels are composed only of **convex closed primitives** (boxes, wedges, prisms) with flat palette colours (one shared material per colour). Since there are no textures, cut faces need no UVs.
- Single-threaded throughout (WebGL). No Jobs/Burst threading.

## Folder / ownership map

| Area | Paths (exclusive owner) | Namespace |
|---|---|---|
| Projection core | `Assets/Scripts/Projection/**`, `Assets/Tests/EditMode/**` | `Ion.Projection` |
| Gameplay | `Assets/Scripts/Gameplay/**` | `Ion.Gameplay` |
| Presentation | `Assets/Shaders/**`, `Assets/Scripts/Presentation/**` | `Ion.Presentation` |
| Levels | `Assets/Scripts/Levels/**` | `Ion.Levels` |
| Build & setup | `Packages/manifest.json`, `ProjectSettings/ProjectVersion.txt`, `Assets/Scripts/Ion.Runtime.asmdef`, `Assets/Editor/**`, `Assets/WebGLTemplates/**`, `Assets/Plugins/WebGL/**`, `.github/workflows/**` | `Ion.EditorTools` |

Assemblies:
- `Ion.Runtime` covers all of `Assets/Scripts`. It references Unity.InputSystem, Unity.RenderPipelines.Universal.Runtime, Unity.RenderPipelines.Core.Runtime and Unity.ugui (UnityEngine.UI).
- `Ion.Editor` covers `Assets/Editor`.
- `Ion.Tests.EditMode` covers `Assets/Tests/EditMode` and references Ion.Runtime and the test framework.

## Cross-module contracts (all agents code against these exact signatures)

```csharp
namespace Ion.Projection {
  // Marker: world geometry that may be cut and captured. Requires MeshFilter+MeshRenderer.
  public sealed class Sliceable : MonoBehaviour { }
  // Marker: gameplay objects (pickups, teleporter, batteries) — never cut; captured/removed whole
  // depending on whether their transform.position is inside the frustum.
  public sealed class Interactable : MonoBehaviour { }

  public struct PhotoFrustum {            // world-space view volume of a photo
    public Pose Pose; public float FovY; public float Aspect; public float Near; public float Far;
    public Plane[] Planes();                          // 6 planes, normals point INTO the volume
    public bool Contains(Vector3 worldPoint);
    public static PhotoFrustum FromCamera(Camera cam, float fovY, float aspect, float rollDegrees, float near, float far);
  }

  public sealed class PhotoData {
    public float FovY, Aspect;                        // capture frustum shape
    public Texture2D Preview;                         // what the photo looks like (Polaroid image)
    public string Label;                              // short caption
    // internal: captured pieces (mesh + materials + pose relative to capture pose)
  }

  public sealed class ProjectionSystem : MonoBehaviour {
    public static ProjectionSystem Instance { get; }
    public const float HoldNear = 0.6f, MaxFar = 250f;
    public PhotoData Capture(Pose pose, float fovY, float aspect, string label); // clip-INSIDE copy + render preview
    public void Place(PhotoData photo, Camera viewer, float rollDegrees);         // cut-OUTSIDE world, then paste
    public bool CanRewind { get; }
    public void Rewind();                                                          // undo last Place
    public event System.Action Placed, Rewound;
  }

  public static class MeshClipper {   // pure geometry, unit-tested
    public static Mesh ClipInside(Mesh mesh, Matrix4x4 localToWorld, Plane[] worldPlanes);        // null if empty
    public static List<Mesh> ClipOutside(Mesh mesh, Matrix4x4 localToWorld, Plane[] worldPlanes); // pieces outside the convex volume
  }
}
```

Placement semantics:
- The photo frustum's apex is the viewer camera position. Its orientation is the viewer camera rotation rolled by `rollDegrees` about the forward axis. Its shape is the photo's FovY and Aspect, with Near = `HoldNear` and Far = `MaxFar`.
- `Place` runs in this order:
  1. Every `Sliceable` whose bounds intersect the frustum is replaced by its `ClipOutside` pieces, which become new Sliceable objects with non-convex MeshColliders.
  2. Every `Interactable` whose position is inside the frustum is deactivated.
  3. The photo's pieces are instantiated with `worldPose = viewerPose_rolled * capturePose⁻¹ * pieceWorldPose`.
  4. The result is pushed onto the undo stack.
- `Rewind` destroys everything spawned by the last placement and reactivates what that placement disabled.
- `Capture` clips every Sliceable against the frustum with `ClipInside` and clones Interactables whose position is inside. It renders the preview with a temporary camera (or a reused one) at the same pose and frustum. The player and UI are excluded via a layer: layer 8 = `Player`, layer 9 = `PhotoUI`.
- Caps:
  - Each plane split of a closed convex piece is capped with a fan-triangulated polygon, sorted by angle around the centroid on the plane.
  - Cap triangles use the piece's first submesh material.
  - Cap normals equal the plane normal facing out of the kept piece.
- `ClipOutside` decomposition, for planes p1..pn: piece_k = mesh ∩ inside(p1..p(k-1)) ∩ outside(pk). These pieces are disjoint and convex if the input is convex.

```csharp
namespace Ion.Gameplay {
  public sealed class FirstPersonController : MonoBehaviour { public Camera Camera { get; } public bool InputEnabled { get; set; } }
  public sealed class PhotoInventory : MonoBehaviour {
    public IReadOnlyList<PhotoData> Photos { get; }
    public void Add(PhotoData p); public event System.Action Changed;
    public int SelectedIndex { get; set; }
  }
  public sealed class PhotoHolder : MonoBehaviour { public bool IsRaised { get; } public float RollDegrees { get; } }
  public sealed class InstantCamera : MonoBehaviour { public bool Unlocked { get; set; } public int Film { get; set; } }
  public sealed class PhotoPickup : MonoBehaviour { public PhotoData Photo; }        // + Interactable; walk/E to collect
  public sealed class Teleporter : MonoBehaviour { public System.Action OnEnter; }    // + Interactable
  public sealed class PlayerSpawn { }
}
```

Controls:
- WASD to move, mouse to look, Space to jump, E to interact or pick up.
- 1–5 or the scroll wheel selects a photo. Hold RMB to raise it, then LMB to place it.
- Q/E rotate a raised photo in 90° steps (E means "interact" only when nothing is raised).
- R rewinds. C toggles the instant camera (raise it with RMB, take the shot with LMB).
- Esc releases the cursor. Clicking the canvas locks it again, with a "Click to play" overlay.
- Mouse sensitivity is a setting; mouse delta is never multiplied by deltaTime.

```csharp
namespace Ion.Presentation {
  public static class Palette { public static Material Get(Color c); /* cached Ion/FlatToon materials */
    public static readonly Color Sky, Mint, Coral, Cream, Sand, Slate, Ink; }
  public sealed class PhotoOverlayUI : MonoBehaviour { public void Show(PhotoData p, float roll); public void Hide(); } // Polaroid frame sized to the photo frustum footprint
  public sealed class Hud : MonoBehaviour { public void Prompt(string text); public void Toast(string text); }
  public sealed class ClickToPlayOverlay : MonoBehaviour { }
}
```

Shaders:
- `Ion/FlatToon`: URP lit-ish banded N·L from the main light, gradient ambient, main-light shadows, fog, `_BaseColor`.
- `Ion/GradientSky`: the skybox.
- `Ion/PhotoDisplay`: Polaroid look with slight desaturation, warm tint and vignette.

```csharp
namespace Ion.Levels {
  public static class Geo { public static GameObject Box(Transform parent, Vector3 center, Vector3 size, Color c, float yRot = 0);
                            public static GameObject Wedge(Transform parent, Vector3 center, Vector3 size, Color c, float yRot = 0); }
  public abstract class Room { public abstract void Build(Transform root, RoomContext ctx); } // ctx gives spawn point, capture helper, teleporter factory
  public sealed class GameBootstrap : MonoBehaviour { } // builds everything in Awake
}
```

## Content: 5 rooms (the critical path is about 5 minutes)

1. **Bridge.** A chasm with a photo on a pedestal showing a wooden bridge. Placing it so the bridge spans the gap lets you cross. Teaches raise, place and rewind.
2. **Doorway.** A solid wall with a photo of open sky. Placing it cuts a hole through the wall.
3. **Stairs.** A high ledge and a photo of a staircase taken sideways. You must rotate it (Q/E) before placing.
4. **Camera.** The instant camera unlocks with 3 film. Photograph a ramp from a side area and paste it at the cliff.
5. **Gallery.** A calm room with empty "Coming soon" frames (placeholders for portfolio projects) and the final teleporter, which shows a "Thanks for playing" card.

Photo dioramas live far below the world at y = −1000, spaced 200 m apart. At startup `GameBootstrap` captures each one with `ProjectionSystem.Capture` from a fixed pose.

## Web page

`Assets/WebGLTemplates/Ion/index.html` contains:
- the name |project|ion and a loading bar
- a "View projects (no game)" link (placeholder anchor) and a controls cheat-sheet
- a canvas that fills the window

`Assets/Plugins/WebGL/OpenUrl.jslib` opens links in a new tab on the next pointerup/keyup, so popup blockers allow it.

## Testing

- **EditMode tests:**
  - `MeshClipper`: a cube clipped by a single plane, a cube clipped by a frustum, the volume of the inside plus outside pieces equals the cube's volume, caps are watertight, and empty results are handled.
  - `PhotoFrustum.Contains`.
- **Batch-mode compile and test run:**
  `Unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -logFile -`
- **Batch-mode Web build:** `-executeMethod Ion.EditorTools.WebBuild.Build`.
- **Manual playtest** in the browser from a local static server.

## Out of scope (this slice)

Real portfolio projects, audio beyond basic SFX, batteries, photocopiers, mobile, save games, CI license setup.
