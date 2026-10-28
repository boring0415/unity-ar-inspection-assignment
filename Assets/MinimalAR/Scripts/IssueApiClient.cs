using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace MinimalAR
{
    [Serializable]
    public sealed class IssueApiConfig
    {
        public string baseUrl;
        public int timeoutSeconds;

        public static IssueApiConfig Load()
        {
            var asset = Resources.Load<TextAsset>("IssueApiConfig");
            if (asset == null) throw new InvalidOperationException("缺少 Resources/IssueApiConfig.json。");
            var config = JsonUtility.FromJson<IssueApiConfig>(asset.text);
            if (config == null || !Uri.TryCreate(config.baseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") || uri.AbsolutePath != "/" ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
                !string.IsNullOrEmpty(uri.UserInfo) || config.timeoutSeconds < 1 || config.timeoutSeconds > 120)
                throw new InvalidOperationException("API 配置无效：填写不含 /api 的 http(s) 基础地址，超时为 1–120 秒。");
            config.baseUrl = config.baseUrl.TrimEnd('/');
            return config;
        }
    }

    [Serializable]
    public sealed class IssuePosition
    {
        public float x;
        public float y;
        public float z;
        public IssuePosition(Vector3 value) { x = value.x; y = value.y; z = value.z; }
        public bool IsFinite => float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z);
    }

    // 仅这四个字段允许发送。id/status/createdAt/updatedAt 均由 Go 生成。
    [Serializable]
    public sealed class CreateIssuePayload
    {
        public string title;
        public string description;
        public string priority;
        public IssuePosition position;

        public static CreateIssuePayload FromMarker(string title, string description, string priority, Transform marker)
        {
            if (marker == null) throw new ArgumentException("请先放置实际标记。");
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("请填写问题标题。");
            if (priority != "low" && priority != "medium" && priority != "high")
                throw new ArgumentException("优先级必须为 low、medium 或 high。");
            var payload = new CreateIssuePayload
            {
                title = title.Trim(), description = description ?? "", priority = priority,
                position = new IssuePosition(marker.position)
            };
            if (!payload.position.IsFinite) throw new ArgumentException("标记坐标无效，请重新放置。");
            return payload;
        }

        public bool Matches(IssueRecord issue) => issue != null && issue.title == title &&
            issue.description == description && issue.priority == priority && issue.position != null &&
            Mathf.Abs(issue.position.x - position.x) < .00001f &&
            Mathf.Abs(issue.position.y - position.y) < .00001f &&
            Mathf.Abs(issue.position.z - position.z) < .00001f;
    }

    [Serializable]
    public sealed class IssueRecord
    {
        public string id;
        public string title;
        public string description;
        public string priority;
        public string status;
        public IssuePosition position;
        public string createdAt;
        public string updatedAt;
        public bool IsValid => !string.IsNullOrEmpty(id) && !string.IsNullOrWhiteSpace(title) &&
            (priority == "low" || priority == "medium" || priority == "high") &&
            (status == "open" || status == "in_progress" || status == "resolved") &&
            position != null && position.IsFinite && !string.IsNullOrEmpty(createdAt) && !string.IsNullOrEmpty(updatedAt);
    }

    [Serializable] public sealed class IssueList { public IssueRecord[] issues; }
    [Serializable] public sealed class IssueError { public string code; public string message; }
    [Serializable] public sealed class IssueErrorResponse { public IssueError error; }

    public static class IssueApiClient
    {
        public static UnityWebRequest GetIssues(IssueApiConfig config) => Configure(UnityWebRequest.Get(config.baseUrl + "/api/issues"), config);

        public static UnityWebRequest CreateIssue(IssueApiConfig config, CreateIssuePayload payload)
        {
            var request = new UnityWebRequest(config.baseUrl + "/api/issues", UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload))),
                downloadHandler = new DownloadHandlerBuffer()
            };
            request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            return Configure(request, config);
        }

        private static UnityWebRequest Configure(UnityWebRequest request, IssueApiConfig config)
        {
            request.timeout = config.timeoutSeconds;
            request.redirectLimit = 0; // 不向意外的重定向目的地重发问题。
            request.SetRequestHeader("Accept", "application/json");
            return request;
        }

        public static bool TryReadList(string json, out IssueRecord[] issues)
        {
            issues = null;
            if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("[")) return false;
            try
            {
                // JsonUtility 不支持顶层数组，包装后解析；后端接口保持不变。
                var list = JsonUtility.FromJson<IssueList>("{\"issues\":" + json + "}");
                if (list?.issues == null || Array.Exists(list.issues, issue => issue == null || !issue.IsValid)) return false;
                issues = list.issues;
                return true;
            }
            catch (ArgumentException) { return false; }
        }

        public static IssueRecord ReadCreated(string json)
        {
            try { return JsonUtility.FromJson<IssueRecord>(json); }
            catch (ArgumentException) { return null; }
        }

        public static IssueError ReadError(string json)
        {
            try { return JsonUtility.FromJson<IssueErrorResponse>(json)?.error; }
            catch (ArgumentException) { return null; }
        }

        public static bool TrySend(UnityWebRequest request, out UnityWebRequestAsyncOperation operation, out string error)
        {
            operation = null;
            error = null;
            try { operation = request.SendWebRequest(); return true; }
            catch (InvalidOperationException exception) { error = exception.Message; return false; }
        }
    }
}
