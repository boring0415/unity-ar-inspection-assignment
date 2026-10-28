using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using MinimalAR;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Android;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;

/// <summary>仅菜单或显式请求触发；不自动改场景、不自动提交问题。</summary>
[InitializeOnLoad]
public static class InspectionSubmissionTools
{
    private const string Reports = "ARInspectionReports";
    private const string Request = "Library/InspectionSubmission.request";
    private static double nextCheck;
    private static UnityWebRequest smokeRequest;
    private static CreateIssuePayload smokePayload;
    private static int previewFrames;
    private static readonly StringBuilder SmokeLog = new StringBuilder();

    static InspectionSubmissionTools()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("InspectionPreview", false))
            {
                SessionState.SetBool("InspectionPreview", false);
                previewFrames = 12;
                EditorApplication.update += WaitForPreviewLayout;
            }
        };
    }

    private static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (smokeRequest != null) { PollSmoke(); return; }
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + .5;
        if (!File.Exists(Request)) return;
        string action = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try
        {
            switch (action)
            {
                case "configure": Configure(); Verify(); break;
                case "verify": Verify(); break;
                case "smoke": StartSmoke(); break;
                case "build": Build(); break;
                case "preview": Preview(); break;
                default: throw new InvalidOperationException("Unknown inspection request: " + action);
            }
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(Reports);
            File.WriteAllText(Reports + "/" + action + "-error.txt", exception.ToString());
            Debug.LogException(exception);
        }
    }

    [MenuItem("Tools/AR Inspection/1 Configure Development HTTP")]
    public static void Configure()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play Mode first.");
        Directory.CreateDirectory(Reports + "/before");
        string backup = Reports + "/before/ProjectSettings.asset";
        if (!File.Exists(backup)) File.Copy("ProjectSettings/ProjectSettings.asset", backup);
        PlayerSettings.Android.forceInternetPermission = true;
        PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
        AssetDatabase.SaveAssets();
        Debug.Log("Inspection: Internet permission enabled; HTTP permitted in Development builds only.");
    }

    [MenuItem("Tools/AR Inspection/2 Verify Configuration And Payload")]
    public static void Verify()
    {
        var config = IssueApiConfig.Load();
        Require(SceneManager.GetActiveScene().path == "Assets/Scenes/SampleScene.unity", "Open the existing SampleScene.");
        Require(UnityEngine.Object.FindObjectsByType<ARSession>(FindObjectsSortMode.None).Length == 1, "Expected one existing AR Session.");
        Require(UnityEngine.Object.FindObjectsByType<TapToPlaceCube>(FindObjectsSortMode.None).Length == 1, "Expected existing placement component.");
        var placement = UnityEngine.Object.FindFirstObjectByType<TapToPlaceCube>();
        Require(placement.GetComponent<ARPlaneManager>()?.planePrefab != null && placement.GetComponent<ARAnchorManager>() != null,
            "Existing AR references must be preserved.");
        Require(new SerializedObject(placement).FindProperty("cubePrefab").objectReferenceValue != null, "Existing cube prefab is missing.");
        Require(Resources.Load<Font>("InspectionFont") != null, "Chinese font missing.");
        Require(PlayerSettings.Android.forceInternetPermission, "Android Internet permission missing.");
        Require(PlayerSettings.insecureHttpOption == InsecureHttpOption.DevelopmentOnly, "Use DevelopmentOnly for local HTTP.");

        var testMarker = new GameObject("Inspection payload verification") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            testMarker.transform.position = new Vector3(.42f, .08f, -1.26f);
            var payload = CreateIssuePayload.FromMarker("  编译检查标题  ", "引号\"与换行\n中文", "high", testMarker.transform);
            string json = JsonUtility.ToJson(payload);
            var roundTrip = JsonUtility.FromJson<CreateIssuePayload>(json);
            Require(roundTrip.title == "编译检查标题" && roundTrip.description == payload.description, "JSON string escaping failed.");
            Require(roundTrip.position.x == testMarker.transform.position.x && roundTrip.position.y == testMarker.transform.position.y &&
                    roundTrip.position.z == testMarker.transform.position.z, "Coordinates must come from actual marker Transform.");
            Require(typeof(CreateIssuePayload).GetFields().Select(field => field.Name).OrderBy(name => name)
                    .SequenceEqual(new[] { "description", "position", "priority", "title" }), "Unexpected POST fields.");
            bool blankRejected = false;
            try { CreateIssuePayload.FromMarker("  ", "", "high", testMarker.transform); }
            catch (ArgumentException) { blankRejected = true; }
            Require(blankRejected, "Blank title was accepted.");
            bool priorityRejected = false;
            try { CreateIssuePayload.FromMarker("a", "", "urgent", testMarker.transform); }
            catch (ArgumentException) { priorityRejected = true; }
            Require(priorityRejected, "Invalid priority was accepted.");
            Require(IssueApiClient.TryReadList("[]", out var empty) && empty.Length == 0, "Top-level array wrapper failed.");
            Require(!IssueApiClient.TryReadList("{}", out _) && !IssueApiClient.TryReadList("[{}]", out _), "Invalid list was accepted.");
            using (var request = IssueApiClient.CreateIssue(config, payload))
            {
                Require(request.method == "POST" && request.url == config.baseUrl + "/api/issues", "Wrong POST route.");
                Require(Encoding.UTF8.GetString(request.uploadHandler.data) == json, "Wrong UTF-8 body.");
                Require(request.GetRequestHeader("Content-Type").StartsWith("application/json"), "Missing JSON header.");
            }
            var markerState = testMarker.AddComponent<InspectionMarker>();
            markerState.BeginAttempt(payload, Array.Empty<IssueRecord>());
            Require(markerState.IsUncertain && !markerState.IsSubmitted, "Pending state failed.");
            markerState.MarkSaved(new IssueRecord { id = "verification-only" });
            Require(markerState.IsSubmitted && !markerState.IsUncertain, "Duplicate submission guard state failed.");
            Directory.CreateDirectory(Reports);
            File.WriteAllText(Reports + "/configuration.txt", "UTC: " + DateTime.UtcNow.ToString("O") + "\nUnity: " + Application.unityVersion +
                "\nPASS: existing scene AR references retained; scripts compiled; Chinese font imported\n" +
                "PASS: POST route, exact request fields, UTF-8 escaping, marker coordinates, blank title and priority validation\n" +
                "PASS: top-level array parsing; per-marker pending/submitted state\n" +
                "Android Internet: true; HTTP: DevelopmentOnly\nAPI: " + config.baseUrl + "\n" +
                "No physical AR tracking, touch/keyboard, UI raycast or Android network test is implied.\n");
        }
        finally { UnityEngine.Object.DestroyImmediate(testMarker); }
        Debug.Log("Inspection configuration and payload verification passed.");
    }

    [MenuItem("Tools/AR Inspection/3 Editor HTTP Smoke Test (Creates One Record)")]
    public static void StartSmoke()
    {
        Verify();
        Require(smokeRequest == null, "A smoke request is already active.");
        var point = new GameObject("Editor HTTP test point") { hideFlags = HideFlags.HideAndDontSave };
        point.transform.position = new Vector3(.42f, .08f, 1.26f);
        try
        {
            smokePayload = CreateIssuePayload.FromMarker("Unity 编辑器联调验证（非真机） " + DateTime.UtcNow.ToString("HHmmss"),
                "由 UnityWebRequest 发送；固定编辑器测试 Transform，不能作为手机 AR 坐标或真机验收证据。", "low", point.transform);
        }
        finally { UnityEngine.Object.DestroyImmediate(point); }
        SmokeLog.Clear();
        SmokeLog.AppendLine("UTC: " + DateTime.UtcNow.ToString("O"));
        SmokeLog.AppendLine("EDITOR ONLY. One explicit test record; no phone AR test.");
        SmokeLog.AppendLine("Request: " + JsonUtility.ToJson(smokePayload));
        smokeRequest = IssueApiClient.CreateIssue(IssueApiConfig.Load(), smokePayload);
        if (!IssueApiClient.TrySend(smokeRequest, out _, out var error)) { FinishSmoke("FAIL: " + error); }
    }

    private static void PollSmoke()
    {
        if (!smokeRequest.isDone) return;
        SmokeLog.AppendLine("HTTP: " + smokeRequest.responseCode);
        SmokeLog.AppendLine("Body: " + smokeRequest.downloadHandler.text);
        if (smokeRequest.method == "POST")
        {
            var issue = IssueApiClient.ReadCreated(smokeRequest.downloadHandler.text);
            if (smokeRequest.result != UnityWebRequest.Result.Success || smokeRequest.responseCode != 201 ||
                issue == null || !issue.IsValid || !smokePayload.Matches(issue) || issue.status != "open")
            { FinishSmoke("FAIL: POST result invalid; no automatic retry"); return; }
            File.WriteAllText(Reports + "/editor-created-issue.json", smokeRequest.downloadHandler.text);
            smokeRequest.Dispose();
            smokeRequest = IssueApiClient.GetIssues(IssueApiConfig.Load());
            if (!IssueApiClient.TrySend(smokeRequest, out _, out var error)) FinishSmoke("FAIL: " + error);
            return;
        }
        bool matched = smokeRequest.result == UnityWebRequest.Result.Success &&
            IssueApiClient.TryReadList(smokeRequest.downloadHandler.text, out var list) && list.Any(smokePayload.Matches);
        FinishSmoke(matched ? "PASS: UnityWebRequest POST 201 and GET verified" : "FAIL: created issue not confirmed by GET");
    }

    private static void FinishSmoke(string result)
    {
        SmokeLog.AppendLine(result);
        File.WriteAllText(Reports + "/editor-http.txt", SmokeLog.ToString());
        smokeRequest?.Dispose();
        smokeRequest = null;
        Debug.Log(result);
    }

    [MenuItem("Tools/AR Inspection/4 Build Development APK")]
    public static void Build()
    {
        Verify();
        Require(!SceneManager.GetActiveScene().isDirty, "Save the existing scene before building.");
        Require(EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android, "Switch to Android first.");
        Directory.CreateDirectory("Builds/Android");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
            target = BuildTarget.Android,
            locationPathName = "Builds/Android/MinimalARInspection.apk",
            options = BuildOptions.Development
        });
        File.WriteAllText(Reports + "/android-build.txt", "UTC: " + DateTime.UtcNow.ToString("O") + "\nResult: " + report.summary.result +
            "\nErrors: " + report.summary.totalErrors + "\nWarnings: " + report.summary.totalWarnings + "\nDuration: " + report.summary.totalTime +
            "\nOutput: " + report.summary.outputPath + "\n" + string.Join("\n", report.steps.SelectMany(step => step.messages)
                .Where(message => message.type == LogType.Error || message.type == LogType.Exception || message.type == LogType.Warning)
                .Select(message => message.type + ": " + message.content)));
        Require(report.summary.result == BuildResult.Succeeded, "Inspection APK build failed; see ARInspectionReports/android-build.txt.");
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    [MenuItem("Tools/AR Inspection/5 Preview UI In Play Mode (No POST)")]
    public static void Preview()
    {
        Verify();
        SessionState.SetBool("InspectionPreview", true);
        EditorApplication.isPlaying = true;
    }

    private static void PreviewChecks()
    {
        try
        {
            var controller = UnityEngine.Object.FindFirstObjectByType<InspectionSubmission>();
            Require(controller != null, "Runtime form controller was not created.");
            var prefab = new SerializedObject(UnityEngine.Object.FindFirstObjectByType<TapToPlaceCube>()).FindProperty("cubePrefab").objectReferenceValue as GameObject;
            var marker = UnityEngine.Object.Instantiate(prefab, new Vector3(.42f, .08f, 1.26f), Quaternion.identity);
            marker.name = "Editor Preview Marker - NOT physical AR";
            controller.SelectMarker(marker.transform);
            var form = (InspectionForm)typeof(InspectionSubmission).GetField("form", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(controller);
            Canvas.ForceUpdateCanvases();
            var log = new StringBuilder("EDITOR PLAY MODE ONLY; fixed preview Transform; no POST.\n");
            log.AppendLine("Screen: " + Screen.width + "x" + Screen.height + "; canvas scale=" + form.Root.GetComponent<Canvas>().scaleFactor);
            foreach (var text in form.Root.GetComponentsInChildren<UnityEngine.UI.Text>())
                log.AppendLine(text.transform.parent.name + ": " + text.text + "; rect=" + text.rectTransform.rect + "; world=" + text.transform.position + "; vertices=" + text.cachedTextGenerator.vertexCount + "; culled=" + text.canvasRenderer.cull + "; shader=" + text.materialForRendering.shader.name);
            File.WriteAllText(Reports + "/ui-layout-diagnostics.txt", log.ToString());
            Require(controller.BlockPlacement, "Draft must lock placement.");
            Require(form.Title.interactable && form.Submit.interactable, "Draft controls should be enabled.");
            foreach (var control in new[] { form.Title.gameObject, form.Priority.gameObject })
            {
                if (control == form.Priority.gameObject)
                {
                    form.Root.GetComponentInChildren<UnityEngine.UI.ScrollRect>().verticalNormalizedPosition = .45f;
                    Canvas.ForceUpdateCanvases();
                }
                var rect = control.GetComponent<RectTransform>();
                var point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
                Require(InspectionInputGuard.IsOverUI(point), "UI raycast did not block " + control.name);
                log.AppendLine("PASS: current-position UI raycast blocks " + control.name);
            }
            var scroll = form.Root.GetComponentInChildren<UnityEngine.UI.ScrollRect>();
            scroll.verticalNormalizedPosition = 0;
            Canvas.ForceUpdateCanvases();
            var submitRect = form.Submit.GetComponent<RectTransform>();
            Require(InspectionInputGuard.IsOverUI(RectTransformUtility.WorldToScreenPoint(null, submitRect.TransformPoint(submitRect.rect.center))), "Submit raycast did not block.");
            log.AppendLine("PASS: current-position UI raycast blocks Submit button; draft locks placement");
            scroll.verticalNormalizedPosition = 1;
            form.Message.text = "编辑器预览：固定测试坐标，并非真实 AR。此预览未发送问题。";
            File.WriteAllText(Reports + "/ui-preview.txt", log.ToString());
            Debug.Log("Inspection runtime UI preview checks passed. Stop Play Mode after inspection.");
        }
        catch (Exception exception)
        {
            File.WriteAllText(Reports + "/ui-preview-error.txt", exception.ToString());
            Debug.LogException(exception);
        }
    }

    private static void WaitForPreviewLayout()
    {
        if (--previewFrames > 0) return;
        EditorApplication.update -= WaitForPreviewLayout;
        if (EditorApplication.isPlaying) PreviewChecks();
    }
}

public sealed class InspectionHttpBuildCheck : IPreprocessBuildWithReport
{
    public static bool DevelopmentHttp { get; private set; }
    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report)
    {
        DevelopmentHttp = false;
        if (report.summary.platform != BuildTarget.Android) return;
        var config = IssueApiConfig.Load();
        if (config.baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            (report.summary.options & BuildOptions.Development) == 0)
            throw new BuildFailedException("本地 HTTP API 请使用 Development Build；正式包应先配置 HTTPS 地址。");
        if (!PlayerSettings.Android.forceInternetPermission)
            throw new BuildFailedException("请先执行 Tools > AR Inspection > 1 Configure Development HTTP。");
        DevelopmentHttp = config.baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            (report.summary.options & BuildOptions.Development) != 0;
    }
}

// 修改 Unity 生成的开发包清单，不提供覆盖整份 AR 清单的自定义模板。
public sealed class InspectionDevelopmentManifest : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 1000;
    public void OnPostGenerateGradleAndroidProject(string path)
    {
        if (!InspectionHttpBuildCheck.DevelopmentHttp) return;
        string manifest = Path.Combine(path, "src/main/AndroidManifest.xml");
        var document = XDocument.Load(manifest);
        var application = document.Root?.Element("application");
        if (application == null) throw new BuildFailedException("Generated Android manifest has no application element.");
        XNamespace android = "http://schemas.android.com/apk/res/android";
        application.SetAttributeValue(android + "usesCleartextTraffic", "true");
        document.Save(manifest);
        Debug.Log("Inspection: development APK manifest explicitly allows local HTTP.");
    }
}
