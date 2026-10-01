# Building |project|ion

Unity **6000.3.25f1** (6.3 LTS) with the **Web Build Support** module. Everything below runs
from the repo root (which is also the Unity project root).

```bash
UNITY=/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity
```

Only one Unity process may have the project open at a time. Close the editor before running
batch-mode commands; each command is its own invocation.

## 1. Project setup (once, and after changing setup code)

Creates `Assets/Settings/URP-Ion.asset` (+ renderer, Forward path, MSAA 4x, HDR off, one shadow
cascade), assigns it to Graphics/Quality settings, includes the Ion shaders, sets layers
8 `Player` and 9 `PhotoUI`, sets the Player/Web settings, sets Active Input Handling to the Input
System package, and regenerates `Assets/Scenes/Main.unity` (one `GameBootstrap` object).

```bash
"$UNITY" -batchmode -nographics -quit -projectPath . \
  -executeMethod Ion.EditorTools.ProjectSetup.Run -logFile -
```

The first time, run it **twice**. The first run may happen before every package has
imported, and changing *Active Input Handling* only takes effect after the editor restarts.
Both runs should end with `[Ion] Project setup finished OK.` In the editor, the same action is
under the menu **Ion → Setup Project**.

## 2. Tests (EditMode)

```bash
"$UNITY" -batchmode -nographics -projectPath . \
  -runTests -testPlatform EditMode -testResults Build/test-results.xml -logFile -
```

(Do not pass `-quit` with `-runTests`.) The exit code is non-zero if a test fails. The results
are written to `Build/test-results.xml`.

## 3. Web build

```bash
"$UNITY" -batchmode -nographics -quit -projectPath . -buildTarget WebGL \
  -executeMethod Ion.EditorTools.WebBuild.Build -logFile -
```

- The output goes to `Build/Web` (`index.html`, `Build/`, `TemplateData/`). Pass `-ionOutput <dir>` to change it.
- `Ion.EditorTools.WebBuild.BuildDev` makes a development build (with a profiler connection and readable stack traces).
- The exit code is non-zero on failure. The build also refuses to run if `Ion.Levels.GameBootstrap` is missing.
- `-buildTarget WebGL` avoids a platform switch partway through the method. Without it, the build switches the target itself, which is slower.
- In the editor, the same builds are under **Ion → Build Web** and **Ion → Build Web (Development)**.

Settings that matter for the web build: Compression *Disabled* (GitHub Pages gzips on the fly),
Data Caching on, Exceptions *None*, Strip Engine Code, Managed Stripping *High* (with
`Assets/Plugins/WebGL/link.xml` preserving `Ion.Runtime`), IL2CPP *Optimize for size*, DXT
textures, WebGL2 only, and the custom template `PROJECT:Ion` (`Assets/WebGLTemplates/Ion`).

## 4. Serve locally

```bash
scripts/serve-web.sh            # http://localhost:8080/
PORT=9000 scripts/serve-web.sh  # another port
```

Open the page in desktop Chrome. Append `?dpr=2` to the URL to render at full Retina resolution.
By default the device pixel ratio is capped at 1.5 for frame rate.

## 5. CI / GitHub Pages

`.github/workflows/web.yml` uses GameCI (`game-ci/unity-builder@v6`) to run setup and then
`WebBuild.Build`. It uploads `Build/Web` with `actions/upload-pages-artifact` and deploys it
with `actions/deploy-pages`. The workflow runs **manually** (`workflow_dispatch`) until these
repo secrets are set:

- `UNITY_LICENSE`: the contents of the `.ulf` file, for Personal
- or `UNITY_SERIAL`: for Pro
- `UNITY_EMAIL` and `UNITY_PASSWORD`

Pages must use **GitHub Actions** as its source. The target URL is
`https://vasiniks.github.io/-project-ion/`. All asset paths in the template are relative, so
the sub-path works.

## Troubleshooting

- **Keyboard and mouse do nothing in Play mode.** Active Input Handling has not switched yet. Re-run the setup, then restart the editor.
- **The scene is pink or magenta.** The URP asset is not assigned, or the Ion shaders were not included. Re-run the setup and check the log for `Required shader ... not found`.
- **`Type 'Ion.Levels.GameBootstrap, Ion.Runtime' not found`.** `Ion.Runtime` has compile errors. Fix them, then re-run the setup.
