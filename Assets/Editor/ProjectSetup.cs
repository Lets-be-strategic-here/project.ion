using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Ion.EditorTools
{
    /// <summary>
    /// Builds every project-level asset/setting from code (URP asset, quality, graphics, tags/layers,
    /// player settings, Main.unity). Idempotent: safe to run any number of times.
    ///
    /// Batch mode:
    ///   Unity -batchmode -nographics -quit -projectPath . -executeMethod Ion.EditorTools.ProjectSetup.Run -logFile -
    ///
    /// Changing "Active Input Handling" only takes effect for the editor after a restart. In batch mode
    /// simply run the setup as its own invocation, then run tests/builds in later invocations.
    /// </summary>
    public static class ProjectSetup
    {
        public const string SettingsFolder = "Assets/Settings";
        public const string ScenesFolder = "Assets/Scenes";
        public const string UrpAssetPath = SettingsFolder + "/URP-Ion.asset";
        public const string RendererDataPath = SettingsFolder + "/URP-Ion_Renderer.asset";
        public const string LightingSettingsPath = SettingsFolder + "/Lighting-Ion.lighting";
        public const string MainScenePath = ScenesFolder + "/Main.unity";
        public const string BootstrapTypeName = "Ion.Levels.GameBootstrap, Ion.Runtime";
        public const string WebTemplate = "PROJECT:Ion";

        public const int PlayerLayer = 8;
        public const int PhotoUILayer = 9;

        static readonly string[] RequiredShaders = { "Ion/FlatToon", "Ion/GradientSky", "Ion/PhotoDisplay", "Ion/AmbienceSoft", "Ion/Backdrop" };
        // Runtime fallbacks used by Presentation when an Ion shader is missing; cheap to include.
        static readonly string[] OptionalShaders = { "Universal Render Pipeline/Unlit" };

        // Palette (kept in sync with the web template): sky #BFE3F2, cream #FFF4E0, ink #2B2F36.
        static readonly Color Sky = new Color32(0xBF, 0xE3, 0xF2, 0xFF);
        static readonly Color Cream = new Color32(0xFF, 0xF4, 0xE0, 0xFF);
        static readonly Color Ink = new Color32(0x2B, 0x2F, 0x36, 0xFF);

        [MenuItem("Ion/Setup Project", priority = 0)]
        public static void RunFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Run();
        }

        /// <summary>Entry point for -executeMethod. In batch mode, exits with code 1 if any step failed.</summary>
        public static void Run()
        {
            bool ok = RunSetup();
            if (!ok && Application.isBatchMode)
                EditorApplication.Exit(1);
        }

        /// <summary>Runs every setup step. Never throws or exits; returns false if any step failed.</summary>
        public static bool RunSetup()
        {
            Debug.Log("[Ion] Project setup started.");
            int errors = 0;
            errors += Step("folders", EnsureFolders);
            errors += Step("render pipeline", () => SetupRenderPipeline());
            errors += Step("quality settings", SetupQuality);
            errors += Step("graphics settings", SetupGraphicsSettings);
            errors += Step("tags & layers", SetupLayers);
            errors += Step("player settings", SetupPlayerSettings);
            errors += Step("input handling", SetupInputHandling);
            errors += Step("main scene", CreateMainScene);
            errors += Step("build settings", SetupBuildSettings);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (errors == 0)
                Debug.Log("[Ion] Project setup finished OK. (If Active Input Handling changed, restart the editor.)");
            else
                Debug.LogError($"[Ion] Project setup finished with {errors} failed step(s). See errors above.");
            return errors == 0;
        }

        /// <summary>Runs <see cref="RunSetup"/> only if the essentials (URP default pipeline, Main scene) are missing.</summary>
        public static void EnsureConfigured()
        {
            bool urpOk = GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset;
            bool sceneOk = File.Exists(MainScenePath);
            if (!urpOk || !sceneOk)
            {
                Debug.Log($"[Ion] Project not fully configured (URP={urpOk}, scene={sceneOk}); running setup.");
                RunSetup();
                return;
            }
            // Cheap and idempotent: picks up shaders added under Assets/Shaders since the last setup.
            if (Step("graphics settings", SetupGraphicsSettings) == 0) AssetDatabase.SaveAssets();
        }

        static int Step(string name, Action action)
        {
            try
            {
                action();
                return 0;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Ion] Setup step '{name}' failed: {e}");
                return 1;
            }
        }

        // ------------------------------------------------------------------ folders

        static void EnsureFolders()
        {
            EnsureFolder(SettingsFolder);
            EnsureFolder(ScenesFolder);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ------------------------------------------------------------------ URP

        public static UniversalRenderPipelineAsset SetupRenderPipeline()
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererDataPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, RendererDataPath);
            }
            rendererData.renderingMode = UnityEngine.Rendering.Universal.RenderingMode.Forward; // NOT Forward+
            rendererData.depthPrimingMode = UnityEngine.Rendering.Universal.DepthPrimingMode.Disabled;
            // Post-processing data: only the Ultra tier turns post-processing on (bloom, per camera at runtime,
            // UltraFx); every other tier keeps it off, so this costs nothing there.
            if (rendererData.postProcessData == null)
                rendererData.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                    "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            EnsureSsaoFeature(rendererData);
            EditorUtility.SetDirty(rendererData);

            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (urp == null)
            {
                urp = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(urp, UrpAssetPath);
            }

            // Public setters where available...
            urp.msaaSampleCount = 4;
            urp.supportsHDR = false;
            urp.supportsCameraDepthTexture = false;
            urp.supportsCameraOpaqueTexture = false;
            urp.shadowDistance = 40f;
            urp.shadowCascadeCount = 1;
            urp.renderScale = 1f;
            urp.useSRPBatcher = true;

            // ...serialized fields for the ones with internal setters.
            var so = new SerializedObject(urp);
            var list = so.FindProperty("m_RendererDataList");
            if (list != null)
            {
                if (list.arraySize < 1) list.arraySize = 1;
                list.GetArrayElementAtIndex(0).objectReferenceValue = rendererData;
            }
            SetInt(so, "m_DefaultRendererIndex", 0);
            SetInt(so, "m_MainLightRenderingMode", (int)UnityEngine.Rendering.Universal.LightRenderingMode.PerPixel);
            SetBool(so, "m_MainLightShadowsSupported", true);
            SetInt(so, "m_MainLightShadowmapResolution", 2048);
            // Per-pixel local lights (lamps, lanterns, teleporters): UltraFx enables at most 8 Light components, and
            // only on the Ultra tier; with none enabled URP drops the additional-lights keyword (no cost elsewhere).
            SetInt(so, "m_AdditionalLightsRenderingMode", (int)UnityEngine.Rendering.Universal.LightRenderingMode.PerPixel);
            SetInt(so, "m_AdditionalLightsPerObjectLimit", 8);
            SetBool(so, "m_AdditionalLightShadowsSupported", false);
            SetBool(so, "m_SoftShadowsSupported", true); // filtered edges; AdaptiveQuality picks the quality per tier
            SetInt(so, "m_SoftShadowQuality", 2);
            SetBool(so, "m_SupportsDynamicBatching", false);
            SetBool(so, "m_RequireDepthTexture", false);
            SetBool(so, "m_RequireOpaqueTexture", false);
            SetBool(so, "m_SupportsHDR", false);
            SetInt(so, "m_MSAA", 4);
            SetFloat(so, "m_ShadowDistance", 40f);
            // Toon shadows are thresholded (FlatToon): a little more bias than the default keeps acne stripes off
            // grazing walls beside pilasters and trims without visible peter-panning at 4096 / 30 m.
            SetFloat(so, "m_ShadowDepthBias", 1.6f);
            SetFloat(so, "m_ShadowNormalBias", 1.3f);
            SetInt(so, "m_ShadowCascadeCount", 1);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);

            GraphicsSettings.defaultRenderPipeline = urp;
            AssetDatabase.SaveAssets();
            return urp;
        }

        /// <summary>
        /// Removes URP's SSAO feature if an earlier setup added it. Measured on WebGL2 (Ultra, Chrome/Metal): the
        /// depth prepass doubled the draw calls and the 4-sample, downsampled result was noisy and blotchy on the
        /// flat toon surfaces. Ultra uses a cheap substitute instead (deeper baked contact shade, Ambience).
        /// </summary>
        static void EnsureSsaoFeature(UniversalRendererData rendererData)
        {
            var features = rendererData.rendererFeatures;
            for (int i = features.Count - 1; i >= 0; i--)
            {
                if (!(features[i] is ScreenSpaceAmbientOcclusion ssao)) continue;
                features.RemoveAt(i);
                var rso = new SerializedObject(rendererData);
                var map = rso.FindProperty("m_RendererFeatureMap");
                if (map != null && i < map.arraySize)
                {
                    map.DeleteArrayElementAtIndex(i);
                    rso.ApplyModifiedPropertiesWithoutUndo();
                }
                AssetDatabase.RemoveObjectFromAsset(ssao);
                UnityEngine.Object.DestroyImmediate(ssao, true);
            }
        }

        // ------------------------------------------------------------------ quality

        static void SetupQuality()
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            var qualityAsset = LoadProjectSettingsAsset("ProjectSettings/QualitySettings.asset");
            if (qualityAsset != null)
            {
                var so = new SerializedObject(qualityAsset);
                var levels = so.FindProperty("m_QualitySettings");
                if (levels != null && levels.arraySize > 0)
                {
                    // Keep exactly one level. Prefer whatever the web platform used by default.
                    int keep = Mathf.Clamp(FindPlatformDefault(so, "WebGL", QualitySettings.GetQualityLevel()), 0, levels.arraySize - 1);
                    if (keep != 0) levels.MoveArrayElement(keep, 0);
                    levels.arraySize = 1;
                    var level = levels.GetArrayElementAtIndex(0);
                    SetString(level, "name", "Ion");
                    var rp = level.FindPropertyRelative("customRenderPipeline");
                    if (rp != null) rp.objectReferenceValue = urp;
                    SetInt(level, "vSyncCount", 1);
                    SetInt(level, "antiAliasing", 0); // URP asset owns MSAA
                    SetFloat(level, "lodBias", 2f);
                    SetInt(so, "m_CurrentQuality", 0);

                    var perPlatform = so.FindProperty("m_PerPlatformDefaultQuality");
                    if (perPlatform != null && perPlatform.isArray)
                    {
                        for (int i = 0; i < perPlatform.arraySize; i++)
                            SetInt(perPlatform.GetArrayElementAtIndex(i), "second", 0);
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            // Belt and braces through the public API for every remaining level.
            string[] names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
            QualitySettings.SetQualityLevel(0, false);
        }

        static int FindPlatformDefault(SerializedObject qualitySo, string platform, int fallback)
        {
            var perPlatform = qualitySo.FindProperty("m_PerPlatformDefaultQuality");
            if (perPlatform == null || !perPlatform.isArray) return fallback;
            for (int i = 0; i < perPlatform.arraySize; i++)
            {
                var e = perPlatform.GetArrayElementAtIndex(i);
                var k = e.FindPropertyRelative("first");
                var v = e.FindPropertyRelative("second");
                if (k != null && v != null && k.stringValue == platform) return v.intValue;
            }
            return fallback;
        }

        // ------------------------------------------------------------------ graphics settings

        static void SetupGraphicsSettings()
        {
            var gsAsset = LoadProjectSettingsAsset("ProjectSettings/GraphicsSettings.asset");
            if (gsAsset == null) throw new InvalidOperationException("GraphicsSettings asset not found.");
            var so = new SerializedObject(gsAsset);

            var shaders = new List<Shader>();
            foreach (var name in RequiredShaders)
            {
                var s = Shader.Find(name);
                if (s != null) shaders.Add(s);
                else Debug.LogError($"[Ion] Required shader '{name}' not found; it will be missing from the build.");
            }
            foreach (var name in OptionalShaders)
            {
                var s = Shader.Find(name);
                if (s != null) shaders.Add(s);
            }
            // Any other shader authored under Assets/Shaders (e.g. hidden helpers) is included too.
            if (AssetDatabase.IsValidFolder("Assets/Shaders"))
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Shader", new[] { "Assets/Shaders" }))
                {
                    var s = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));
                    if (s != null && !shaders.Contains(s)) shaders.Add(s);
                }
            }

            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            if (arr != null)
            {
                foreach (var shader in shaders)
                {
                    bool present = false;
                    for (int i = 0; i < arr.arraySize; i++)
                        if (arr.GetArrayElementAtIndex(i).objectReferenceValue == shader) { present = true; break; }
                    if (present) continue;
                    arr.InsertArrayElementAtIndex(arr.arraySize);
                    arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = shader;
                }
            }
            else Debug.LogError("[Ion] m_AlwaysIncludedShaders not found on GraphicsSettings.");

            // Fog is switched on at runtime by GameBootstrap, so automatic fog stripping (which
            // looks at the scenes in the build) must not strip the fog variants.
            SetInt(so, "m_FogStripping", 1); // 1 = Custom
            SetBool(so, "m_FogKeepLinear", true);
            SetBool(so, "m_FogKeepExp", true);
            SetBool(so, "m_FogKeepExp2", true);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ tags & layers

        static void SetupLayers()
        {
            var tagManager = LoadProjectSettingsAsset("ProjectSettings/TagManager.asset");
            if (tagManager == null) throw new InvalidOperationException("TagManager asset not found.");
            var so = new SerializedObject(tagManager);
            var layers = so.FindProperty("layers");
            if (layers == null || !layers.isArray) throw new InvalidOperationException("TagManager.layers not found.");
            SetLayer(layers, PlayerLayer, "Player");
            SetLayer(layers, PhotoUILayer, "PhotoUI");
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetLayer(SerializedProperty layers, int index, string name)
        {
            if (index >= layers.arraySize) layers.arraySize = index + 1;
            var p = layers.GetArrayElementAtIndex(index);
            if (!string.IsNullOrEmpty(p.stringValue) && p.stringValue != name)
                Debug.LogWarning($"[Ion] Overwriting layer {index} '{p.stringValue}' with '{name}'.");
            p.stringValue = name;
        }

        // ------------------------------------------------------------------ player settings

        static void SetupPlayerSettings()
        {
            PlayerSettings.companyName = "vasiniks";
            PlayerSettings.productName = "[project]ion";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SplashScreen.show = false; // the web template carries "Built with Unity"
            PlayerSettings.SplashScreen.showUnityLogo = false;

            var web = NamedBuildTarget.WebGL;
            PlayerSettings.SetManagedStrippingLevel(web, ManagedStrippingLevel.High);
            InvokeStaticWithEnum(typeof(PlayerSettings), "SetIl2CppCodeGeneration", web, "OptimizeSize");
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.OpenGLES3 }); // WebGL2 only

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled; // Pages gzips on the fly
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
            PlayerSettings.WebGL.nameFilesAsHashes = false;
            PlayerSettings.WebGL.template = WebTemplate;

            // Less certain / version-dependent members go through reflection so a rename can't
            // break compilation of the whole editor assembly.
            var webType = typeof(PlayerSettings.WebGL);
            TrySetStatic(webType, "linkerTarget", "Wasm");
            TrySetStatic(webType, "debugSymbolMode", "Off");
            TrySetStatic(webType, "showDiagnostics", false);
            TrySetStatic(webType, "powerPreference", "HighPerformance");
            TrySetStatic(webType, "memoryGrowthMode", "Geometric");
            TrySetStatic(webType, "initialMemorySize", 256);   // MB
            TrySetStatic(webType, "maximumMemorySize", 2048);  // MB
            TrySetStatic(webType, "geometricMemoryGrowthStep", 0.2f);
            TrySetStatic(webType, "geometricMemoryGrowthCap", 96);

            EditorUserBuildSettings.webGLBuildSubtarget = WebGLTextureSubtarget.DXT;
            SetWebCodeOptimization(WebCodeOptimization);
        }

        /// <summary>
        /// Web "Code Optimization" (Build Profiles → Web): smallest wasm with link-time optimisation. Slower
        /// to build, noticeably smaller download. Stored in the build settings (Library), so every build
        /// re-applies it (WebBuild calls this too) and fresh CI checkouts get it as well.
        /// </summary>
        public const string WebCodeOptimization = "DiskSizeLTO";

        public static void SetWebCodeOptimization(string value)
        {
            bool done = false;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = asm.GetName().Name;
                if (!name.StartsWith("UnityEditor.WebGL", StringComparison.Ordinal)) continue;
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                foreach (Type t in types)
                {
                    if (t == null) continue;
                    PropertyInfo prop = t.GetProperty("codeOptimization", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (prop == null || !prop.CanWrite || !prop.PropertyType.IsEnum) continue;
                    if (!Enum.IsDefined(prop.PropertyType, value)) continue;
                    try
                    {
                        prop.SetValue(null, Enum.Parse(prop.PropertyType, value));
                        Debug.Log($"[Ion] Web code optimization: {t.FullName}.codeOptimization = {prop.GetValue(null)}");
                        done = true;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Ion] Could not set {t.FullName}.codeOptimization: {e.Message}");
                    }
                }
            }
            if (!done)
            {
                EditorUserBuildSettings.SetPlatformSettings("WebGL", "CodeOptimization", value);
                Debug.Log("[Ion] Web code optimization (platform setting) = " +
                          EditorUserBuildSettings.GetPlatformSettings("WebGL", "CodeOptimization"));
            }
        }

        // ------------------------------------------------------------------ input handling

        /// <summary>Sets Active Input Handling to "Input System Package (New)" only.</summary>
        static void SetupInputHandling()
        {
            // Preferred: the Input System's own helper, which knows Unity 6's build-profile player settings.
            var helper = Type.GetType("UnityEngine.InputSystem.Editor.EditorPlayerSettingHelpers, Unity.InputSystem.Editor");
            if (helper != null)
            {
                var newProp = helper.GetProperty("newSystemBackendsEnabled", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var oldProp = helper.GetProperty("oldSystemBackendsEnabled", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (newProp != null && oldProp != null)
                {
                    // Order matters: enable new first, then disable old (see the helper's own comments).
                    newProp.SetValue(null, true);
                    oldProp.SetValue(null, false);
                    bool okNew = (bool)newProp.GetValue(null);
                    bool okOld = (bool)oldProp.GetValue(null);
                    if (okNew && !okOld)
                    {
                        Debug.Log("[Ion] Active Input Handling = Input System Package (New). Restart the editor if it changed.");
                        return;
                    }
                    Debug.LogWarning("[Ion] Input System helper did not apply the setting; falling back to PlayerSettings.");
                }
            }

            // Fallback: write the serialized field directly (0 = old, 1 = new, 2 = both).
            var ps = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
            if (ps == null) throw new InvalidOperationException("PlayerSettings asset not found.");
            var so = new SerializedObject(ps);
            var prop = so.FindProperty("activeInputHandler");
            if (prop == null) throw new InvalidOperationException("PlayerSettings.activeInputHandler not found.");
            if (prop.intValue != 1)
            {
                prop.intValue = 1;
                so.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log("[Ion] Active Input Handling set to Input System Package (New). Restart the editor.");
            }
        }

        // ------------------------------------------------------------------ scene & build settings

        static void CreateMainScene()
        {
            string openScenePath = SceneManager.GetActiveScene().path;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Scene-level render settings. GameBootstrap may override all of this at runtime; fog is
            // enabled here too so the build keeps fog shader variants.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Sky;
            RenderSettings.ambientEquatorColor = Cream;
            RenderSettings.ambientGroundColor = Color.Lerp(Ink, Cream, 0.55f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Sky;
            RenderSettings.fogStartDistance = 40f;
            RenderSettings.fogEndDistance = 180f;
            RenderSettings.skybox = null;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

            Lightmapping.lightingSettings = GetOrCreateLightingSettings();

            var go = new GameObject("GameBootstrap");
            var bootstrapType = Type.GetType(BootstrapTypeName);
            if (bootstrapType != null && typeof(Component).IsAssignableFrom(bootstrapType))
                go.AddComponent(bootstrapType);
            else
                Debug.LogError($"[Ion] Type '{BootstrapTypeName}' not found (does Ion.Runtime compile?). " +
                               "Main.unity was saved WITHOUT the bootstrap component; re-run setup once it compiles.");

            if (!EditorSceneManager.SaveScene(scene, MainScenePath))
                throw new InvalidOperationException("Could not save " + MainScenePath);

            if (!Application.isBatchMode && !string.IsNullOrEmpty(openScenePath) && openScenePath != MainScenePath
                && File.Exists(openScenePath))
                EditorSceneManager.OpenScene(openScenePath, OpenSceneMode.Single);
        }

        static LightingSettings GetOrCreateLightingSettings()
        {
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
            if (ls == null)
            {
                ls = new LightingSettings { name = "Lighting-Ion" };
                AssetDatabase.CreateAsset(ls, LightingSettingsPath);
            }
            ls.bakedGI = false;    // everything is generated at runtime; nothing to bake
            ls.realtimeGI = false;
            TrySetInstance(ls, "autoGenerate", false);
            EditorUtility.SetDirty(ls);
            return ls;
        }

        /// <summary>True if Main.unity exists and its GameBootstrap object carries the bootstrap component.</summary>
        public static bool MainSceneHasBootstrap()
        {
            if (!File.Exists(MainScenePath)) return false;
            var t = Type.GetType(BootstrapTypeName);
            if (t == null) return false;
            // Cheap text check: the scene YAML references the script by its MonoScript guid.
            var script = FindMonoScript(t);
            if (script == null) return false;
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(script));
            return !string.IsNullOrEmpty(guid) && File.ReadAllText(MainScenePath).Contains(guid);
        }

        static MonoScript FindMonoScript(Type t)
        {
            foreach (var guid in AssetDatabase.FindAssets(t.Name + " t:MonoScript"))
            {
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
                if (ms != null && ms.GetClass() == t) return ms;
            }
            return null;
        }

        static void SetupBuildSettings()
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScenePath, true) };
        }

        // ------------------------------------------------------------------ helpers

        static UnityEngine.Object LoadProjectSettingsAsset(string path)
        {
            var all = AssetDatabase.LoadAllAssetsAtPath(path);
            return all != null && all.Length > 0 ? all[0] : null;
        }

        static void SetInt(SerializedObject so, string name, int v)
        {
            var p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning($"[Ion] Serialized field '{name}' not found on {so.targetObject.GetType().Name}."); return; }
            if (p.propertyType == SerializedPropertyType.Enum) p.enumValueFlag = v; else p.intValue = v;
        }

        static void SetInt(SerializedProperty parent, string name, int v)
        {
            var p = parent.FindPropertyRelative(name);
            if (p == null) { Debug.LogWarning($"[Ion] Serialized field '{name}' not found."); return; }
            if (p.propertyType == SerializedPropertyType.Enum) p.enumValueFlag = v; else p.intValue = v;
        }

        static void SetBool(SerializedObject so, string name, bool v)
        {
            var p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning($"[Ion] Serialized field '{name}' not found on {so.targetObject.GetType().Name}."); return; }
            p.boolValue = v;
        }

        static void SetFloat(SerializedObject so, string name, float v)
        {
            var p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning($"[Ion] Serialized field '{name}' not found on {so.targetObject.GetType().Name}."); return; }
            p.floatValue = v;
        }

        static void SetFloat(SerializedProperty parent, string name, float v)
        {
            var p = parent.FindPropertyRelative(name);
            if (p == null) { Debug.LogWarning($"[Ion] Serialized field '{name}' not found."); return; }
            p.floatValue = v;
        }

        static void SetString(SerializedProperty parent, string name, string v)
        {
            var p = parent.FindPropertyRelative(name);
            if (p == null) { Debug.LogWarning($"[Ion] Serialized field '{name}' not found."); return; }
            p.stringValue = v;
        }

        static object ConvertTo(Type type, object value)
        {
            if (type.IsEnum && value is string s) return Enum.Parse(type, s);
            return Convert.ChangeType(value, type);
        }

        static void TrySetStatic(Type type, string property, object value)
        {
            var p = type.GetProperty(property, BindingFlags.Public | BindingFlags.Static);
            if (p == null || !p.CanWrite) { Debug.LogWarning($"[Ion] {type.Name}.{property} not available; skipped."); return; }
            try { p.SetValue(null, ConvertTo(p.PropertyType, value)); }
            catch (Exception e) { Debug.LogWarning($"[Ion] Could not set {type.Name}.{property}: {e.Message}"); }
        }

        static void TrySetInstance(object target, string property, object value)
        {
            var p = target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
            if (p == null || !p.CanWrite) return;
            try { p.SetValue(target, ConvertTo(p.PropertyType, value)); }
            catch (Exception e) { Debug.LogWarning($"[Ion] Could not set {target.GetType().Name}.{property}: {e.Message}"); }
        }

        /// <summary>Calls static Method(firstArg, Enum.Parse(secondParamType, enumName)).</summary>
        static void InvokeStaticWithEnum(Type type, string method, object firstArg, string enumName)
        {
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != method) continue;
                var ps = m.GetParameters();
                if (ps.Length != 2 || !ps[0].ParameterType.IsInstanceOfType(firstArg) || !ps[1].ParameterType.IsEnum) continue;
                try
                {
                    m.Invoke(null, new[] { firstArg, Enum.Parse(ps[1].ParameterType, enumName) });
                    return;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Ion] {type.Name}.{method}({enumName}) failed: {e.Message}");
                    return;
                }
            }
            Debug.LogWarning($"[Ion] {type.Name}.{method} not available; skipped.");
        }
    }
}
