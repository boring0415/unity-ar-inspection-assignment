using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MinimalAR;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.ARCore;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARCore;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>通过 Unity API 小范围配置当前场景；不改写场景 YAML，不重建现有 AR 对象。</summary>
public static class MinimalARSetup
{
    private const string Root = "Assets/MinimalAR";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string Reports = "ARPrototypeReports";

    // 只消费本次任务显式写入的请求文件。正常打开项目时不会自动修改场景或构建。
    [InitializeOnLoadMethod]
    private static void RunRequestedSetup()
    {
        EditorApplication.delayCall += () =>
        {
            const string request = "Library/MinimalAR.setup-request";
            if (!File.Exists(request) || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            var action = File.ReadAllText(request).Trim();
            File.Delete(request);
            try
            {
                if (action == "report")
                {
                    ValidateCurrentScene();
                    ExportLastBuildReport();
                }
                else if (action == "configure") ConfigureCurrentScene();
                else throw new InvalidOperationException($"Unknown Minimal AR request: {action}");
            }
            catch (Exception exception)
            {
                Directory.CreateDirectory(Reports);
                File.WriteAllText($"{Reports}/setup-error.txt", exception.ToString());
                Debug.LogException(exception);
            }
        };
    }

    private static T[] Components<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [MenuItem("Tools/Minimal AR/1 Configure Current Scene")]
    public static void ConfigureCurrentScene()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play Mode first.");
        var scene = SceneManager.GetActiveScene();
        Require(scene.path == ScenePath && SceneManager.sceneCount == 1,
            "Open only Assets/Scenes/SampleScene.unity before configuring.");
        Require(Components<ARSession>(scene).Length == 1, "Expected exactly one existing AR Session.");
        Require(Components<XROrigin>(scene).Length == 1, "Expected exactly one existing XR Origin.");
        Require(Components<Camera>(scene).Length == 1, "Expected exactly one existing AR camera.");
        var origin = Components<XROrigin>(scene).Single();
        var planes = origin.GetComponent<ARPlaneManager>();
        var rays = origin.GetComponent<ARRaycastManager>();
        Require(planes != null && rays != null, "Existing XR Origin must contain both AR managers.");

        // 先备份编辑器中的完整场景（包括尚未保存的内容），再执行可撤销的组件修改。
        Directory.CreateDirectory($"{Root}/Editor/Backups");
        var backup = AssetDatabase.GenerateUniqueAssetPath($"{Root}/Editor/Backups/BeforeMinimalAR.unity");
        Require(EditorSceneManager.SaveScene(scene, backup, true), "Could not save scene backup.");
        var undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Configure Minimal AR");
        Undo.RecordObject(planes, "Assign horizontal plane visualization");
        planes.requestedDetectionMode = PlaneDetectionMode.Horizontal;
        if (planes.planePrefab == null) planes.planePrefab = CreatePlanePrefab();
        if (origin.GetComponent<ARAnchorManager>() == null)
            Undo.AddComponent<ARAnchorManager>(origin.gameObject);
        var placement = origin.GetComponent<TapToPlaceCube>();
        if (placement == null) placement = Undo.AddComponent<TapToPlaceCube>(origin.gameObject);
        var serializedPlacement = new SerializedObject(placement);
        if (serializedPlacement.FindProperty("cubePrefab").objectReferenceValue == null)
        {
            serializedPlacement.FindProperty("cubePrefab").objectReferenceValue = CreateCubePrefab();
            serializedPlacement.ApplyModifiedProperties();
        }

        // Raycast Prefab 用于持续追踪射线对象；本原型的单次 Raycast 无需它，保留原值。
        // 保留 API 25，并以 OpenGLES3 避免 Vulkan 在 AR Required 下要求 API 29。
        if (PlayerSettings.GetGraphicsAPIs(BuildTarget.Android).FirstOrDefault() == GraphicsDeviceType.Vulkan &&
            (int)PlayerSettings.Android.minSdkVersion < 29)
        {
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
        }
        var arcore = ARCoreSettings.currentSettings;
        Require(arcore != null, "Existing ARCore settings missing.");
        arcore.requirement = ARCoreSettings.Requirement.Required;
        // 平面检测不依赖 Depth API，不让深度支持成为安装此最小原型的额外条件。
        arcore.depth = ARCoreSettings.Requirement.Optional;
        var identifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        if (string.IsNullOrEmpty(identifier) ||
            !Regex.IsMatch(identifier, @"^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z][a-zA-Z0-9_]*)+$"))
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.example.unityarprototype");

        var scenes = EditorBuildSettings.scenes.ToList();
        var existing = scenes.FindIndex(entry => entry.path == scene.path);
        if (existing < 0) scenes.Add(new EditorBuildSettingsScene(scene.path, true));
        else scenes[existing].enabled = true;
        EditorBuildSettings.scenes = scenes.ToArray();
        EditorUtility.SetDirty(planes);
        EditorUtility.SetDirty(placement);
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        Require(EditorSceneManager.SaveScene(scene), "Failed to save configured scene.");
        AssetDatabase.SaveAssets();
        ValidateCurrentScene();
        Debug.Log($"Minimal AR configured. Backup: {backup}");
    }

    private static Material MaterialAsset(string name, string shaderName, Color color)
    {
        var path = $"{Root}/Materials/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        var shader = Shader.Find(shaderName);
        Require(shader != null, $"Missing shader: {shaderName}");
        var material = new Material(shader) { name = name, color = color };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static GameObject CreatePlanePrefab()
    {
        var path = $"{Root}/Prefabs/DetectedHorizontalPlane.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;
        var plane = new GameObject("Detected Horizontal Plane", typeof(MeshFilter), typeof(MeshRenderer),
            typeof(ARPlane), typeof(ARPlaneMeshVisualizer), typeof(LineRenderer));
        try
        {
            var renderer = plane.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = MaterialAsset("PlaneFill", "MinimalAR/PlaneOverlay", new Color(.1f, .8f, 1f, .25f));
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var line = plane.GetComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 0;
            line.widthMultiplier = .006f;
            line.sharedMaterial = MaterialAsset("PlaneEdge", "MinimalAR/PlaneOverlay", new Color(.1f, .8f, 1f, .95f));
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return PrefabUtility.SaveAsPrefabAsset(plane, path);
        }
        finally { UnityEngine.Object.DestroyImmediate(plane); }
    }

    private static GameObject CreateCubePrefab()
    {
        var path = $"{Root}/Prefabs/PlacementCube.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            cube.name = "Placed Cube (10 cm)";
            cube.transform.localScale = Vector3.one * .1f;
            cube.GetComponent<MeshRenderer>().sharedMaterial = MaterialAsset("CubeOrange", "Standard", new Color(1f, .35f, .08f));
            return PrefabUtility.SaveAsPrefabAsset(cube, path);
        }
        finally { UnityEngine.Object.DestroyImmediate(cube); }
    }

    [MenuItem("Tools/Minimal AR/2 Validate Current Scene")]
    public static void ValidateCurrentScene()
    {
        var scene = SceneManager.GetActiveScene();
        var origin = Components<XROrigin>(scene).Single();
        var planes = origin.GetComponent<ARPlaneManager>();
        var rays = origin.GetComponent<ARRaycastManager>();
        var anchors = origin.GetComponent<ARAnchorManager>();
        Require(Components<ARSession>(scene).Length == 1 && Components<Camera>(scene).Length == 1,
            "AR Session or camera count is not one.");
        Require(Components<ARPlaneManager>(scene).Length == 1 && Components<ARRaycastManager>(scene).Length == 1 &&
            Components<ARAnchorManager>(scene).Length == 1, "Duplicate or missing AR managers.");
        var camera = origin.Camera;
        Require(camera != null && camera.transform.IsChildOf(origin.transform) && camera.isActiveAndEnabled &&
            camera.GetComponent<ARCameraManager>() != null && camera.GetComponent<ARCameraBackground>() != null &&
            camera.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>() != null, "Incomplete AR camera.");
        Require(planes.isActiveAndEnabled && rays.isActiveAndEnabled && anchors.isActiveAndEnabled,
            "AR managers must be enabled.");
        Require(planes.requestedDetectionMode == PlaneDetectionMode.Horizontal && planes.planePrefab != null,
            "Horizontal plane detection or prefab is missing.");
        Require(planes.planePrefab.GetComponent<ARPlaneMeshVisualizer>() != null &&
            planes.planePrefab.GetComponent<ARPlane>() != null &&
            planes.planePrefab.GetComponent<MeshFilter>() != null &&
            planes.planePrefab.GetComponent<MeshRenderer>()?.sharedMaterial != null, "Incomplete plane prefab.");
        var placement = origin.GetComponent<TapToPlaceCube>();
        Require(placement != null && placement.enabled, "Placement component missing or disabled.");
        var cube = new SerializedObject(placement).FindProperty("cubePrefab").objectReferenceValue as GameObject;
        Require(cube != null && cube.GetComponent<MeshFilter>()?.sharedMesh != null &&
            cube.GetComponent<MeshRenderer>()?.sharedMaterial != null, "Cube prefab missing mesh or material.");
        var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
        Require(xr != null && xr.InitManagerOnStart && xr.Manager.activeLoaders.OfType<ARCoreLoader>().Any(),
            "ARCore loader is not enabled for Android startup.");
        Require(PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) == ScriptingImplementation.IL2CPP &&
            (PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARM64) != 0, "IL2CPP/ARM64 missing.");
        var api = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android).First();
        Require(api == GraphicsDeviceType.OpenGLES3 || api == GraphicsDeviceType.Vulkan, "Unsupported graphics API.");
        Require((int)PlayerSettings.Android.minSdkVersion >= (api == GraphicsDeviceType.Vulkan ? 29 : 25),
            "Minimum SDK does not meet installed Unity/ARCore requirements.");
        Require(ARCoreSettings.currentSettings.requirement == ARCoreSettings.Requirement.Required &&
            ARCoreSettings.currentSettings.depth == ARCoreSettings.Requirement.Optional, "Unexpected ARCore requirements.");
        Require(EditorBuildSettings.scenes.Any(entry => entry.enabled && entry.path == scene.path), "Scene is not enabled in build list.");
        var playerSettings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").First());
        Require(playerSettings.FindProperty("activeInputHandler").intValue == 1, "Expected existing New Input System configuration.");
        Directory.CreateDirectory(Reports);
        File.WriteAllText($"{Reports}/scene-validation.txt",
            $"UTC: {DateTime.UtcNow:O}\nScene: {scene.path}\nUnity: {Application.unityVersion}\n" +
            $"Active target: {EditorUserBuildSettings.activeBuildTarget}\nPASS: 1 ARSession, 1 XROrigin, 1 Camera, 1 PlaneManager, 1 RaycastManager, 1 AnchorManager\n" +
            $"PASS: AR camera background/manager/pose driver; horizontal plane mesh prefab; 10cm Cube prefab; placement references\n" +
            $"PASS: New Input System; ARCore enabled on startup; AR Required; Depth Optional; IL2CPP; ARM64\n" +
            $"Graphics: {api}; Minimum SDK: {(int)PlayerSettings.Android.minSdkVersion}; Target SDK: {PlayerSettings.Android.targetSdkVersion}\n" +
            $"Application ID: {PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)}\n" +
            $"Build scenes: {string.Join(", ", EditorBuildSettings.scenes.Where(entry => entry.enabled).Select(entry => entry.path))}\n" +
            "This validates editor configuration only. No physical tracking, camera-permission grant, or device test is implied.\n");
        Debug.Log("Minimal AR scene validation passed. See ARPrototypeReports/scene-validation.txt");
    }

    [MenuItem("Tools/Minimal AR/3 Build Android APK")]
    public static void BuildAndroidApk()
    {
        ValidateCurrentScene();
        Require(EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android, "Switch Build Profiles to Android first.");
        Require(!SceneManager.GetActiveScene().isDirty, "Save the scene before building.");
        Directory.CreateDirectory("Builds/Android");
        Directory.CreateDirectory(Reports);
        var originalBundle = EditorUserBuildSettings.buildAppBundle;
        var originalExport = EditorUserBuildSettings.exportAsGoogleAndroidProject;
        try
        {
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(entry => entry.enabled).Select(entry => entry.path).ToArray(),
                target = BuildTarget.Android,
                locationPathName = "Builds/Android/MinimalAR.apk",
                options = BuildOptions.Development
            });
            File.WriteAllText($"{Reports}/android-build.txt",
                $"UTC: {DateTime.UtcNow:O}\nResult: {report.summary.result}\nErrors: {report.summary.totalErrors}\n" +
                $"Warnings: {report.summary.totalWarnings}\nDuration: {report.summary.totalTime}\nOutput: {report.summary.outputPath}\n" +
                string.Join("\n", report.steps.SelectMany(step => step.messages)
                    .Where(message => message.type == LogType.Error || message.type == LogType.Exception)
                    .Select(message => message.content)));
            Require(report.summary.result == BuildResult.Succeeded, "Android build failed; see android-build.txt and Console.");
            Debug.Log("Minimal AR APK built: Builds/Android/MinimalAR.apk. Device testing still required.");
        }
        finally
        {
            EditorUserBuildSettings.buildAppBundle = originalBundle;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = originalExport;
        }
    }

    [MenuItem("Tools/Minimal AR/4 Export Last Build Details")]
    public static void ExportLastBuildReport()
    {
        var report = BuildReport.GetLatestReport();
        Require(report != null, "No Unity build report is available.");
        Directory.CreateDirectory(Reports);
        File.WriteAllText($"{Reports}/android-build-details.txt",
            $"Result: {report.summary.result}\nOutput: {report.summary.outputPath}\n" +
            $"Errors: {report.summary.totalErrors}; Warnings: {report.summary.totalWarnings}\n" +
            string.Join("\n", report.steps.SelectMany(step => step.messages)
                .Where(message => message.type == LogType.Warning || message.type == LogType.Error || message.type == LogType.Exception)
                .Select(message => $"[{message.type}] {message.content}")));
    }
}
