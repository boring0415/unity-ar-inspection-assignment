using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace MinimalAR
{
    [DisallowMultipleComponent]
    public sealed class InspectionSubmission : MonoBehaviour
    {
        private static readonly string[] Priorities = { "low", "medium", "high" };
        private InspectionForm form;
        private IssueApiConfig config;
        private InspectionMarker marker;
        private bool busy;
        private bool retryChecked;
        private UnityWebRequest activeRequest;

        // 填写期间锁定实际标记，避免第二次触摸意外改掉所提交的坐标。
        public bool BlockPlacement => busy || (marker != null && !marker.IsSubmitted);

        private void Awake()
        {
            try
            {
                form = new InspectionForm();
                form.Submit.onClick.AddListener(SubmitOrVerify);
                form.Next.onClick.AddListener(StartNext);
                form.Retry.onClick.AddListener(() =>
                {
                    if (busy || marker == null || !retryChecked) return;
                    marker.AllowManualRetry();
                    retryChecked = false;
                    Notice("已允许手动重试，请确认后再次点击提交。原输入已保留。", false);
                    RefreshControls();
                });
                config = IssueApiConfig.Load();
            }
            catch (Exception exception)
            {
                Debug.LogError("巡检提交初始化失败：" + exception.Message, this);
                if (form != null) Notice("配置错误：" + exception.Message, true);
            }
            RefreshControls();
        }

        public void SelectMarker(Transform actualMarker)
        {
            if (form == null || config == null || actualMarker == null || BlockPlacement) return;
            marker = actualMarker.GetComponent<InspectionMarker>();
            if (marker == null) marker = actualMarker.gameObject.AddComponent<InspectionMarker>();
            form.Title.text = "";
            form.Description.text = "";
            form.Priority.value = 1;
            Notice("标记已锁定。请填写标题，确认坐标后提交；需要换点可点击“开始下一条”。", false);
            RefreshControls();
        }

        private void Update()
        {
            if (form == null) return;
            form.UpdateSafeArea();
            IssuePosition position = marker != null
                ? marker.SavedIssue?.position ?? marker.PendingPayload?.position ?? new IssuePosition(marker.transform.position)
                : null;
            form.Coordinates.text = position == null ? "尚未放置标记" : string.Format(CultureInfo.InvariantCulture,
                "标记中心 · AR 世界坐标（米）\nX {0:G7}    Y {1:G7}    Z {2:G7}", position.x, position.y, position.z);
        }

        private void StartNext()
        {
            if (busy || (marker != null && marker.IsUncertain)) return;
            marker = null; // 保留场景中已放置的立方体和锚点。
            retryChecked = false;
            form.Title.text = "";
            form.Description.text = "";
            Notice("请点击上方蓝色水平面放置新标记。未提交的旧输入将不再使用。", false);
            RefreshControls();
        }

        private void SubmitOrVerify()
        {
            if (busy || config == null || marker == null || marker.IsSubmitted) return;
            if (marker.IsUncertain) { busy = true; RefreshControls(); StartCoroutine(VerifyPending()); return; }
            CreateIssuePayload payload;
            try { payload = CreateIssuePayload.FromMarker(form.Title.text, form.Description.text, Priorities[form.Priority.value], marker.transform); }
            catch (ArgumentException exception) { Notice(exception.Message, true); return; }
            busy = true; // 同帧多次点击也只能启动一次协程。
            retryChecked = false;
            RefreshControls();
            StartCoroutine(Submit(payload));
        }

        private IEnumerator Submit(CreateIssuePayload payload)
        {
            try
            {
                Notice("正在检查已有记录，请稍候…", false);
                // 保存提交前的 ID 集合，连接中断后可识别本次新记录，避免直接重发。
                using (var query = IssueApiClient.GetIssues(config))
                {
                    activeRequest = query;
                    if (!IssueApiClient.TrySend(query, out var operation, out var sendError))
                    { Notice("未开始提交：" + sendError, true); yield break; }
                    yield return operation;
                    if (query.result != UnityWebRequest.Result.Success || !IssueApiClient.TryReadList(query.downloadHandler.text, out var existing))
                    { Notice("未开始提交：无法读取已有记录。请检查后端地址和网络，输入已保留。", true); yield break; }
                    marker.BeginAttempt(payload, existing);
                }

                Notice("正在提交，请勿重复点击…", false);
                using (var request = IssueApiClient.CreateIssue(config, payload))
                {
                    activeRequest = request;
                    if (!IssueApiClient.TrySend(request, out var operation, out var sendError))
                    { marker.AllowManualRetry(); Notice("发送失败：" + sendError, true); yield break; }
                    yield return operation;
                    string body = request.downloadHandler.text;
                    var result = IssueApiClient.ReadCreated(body);
                    if (request.result == UnityWebRequest.Result.Success && request.responseCode == 201 &&
                        result != null && result.IsValid && result.status == "open" && payload.Matches(result))
                    { Saved(result); yield break; }

                    var failure = IssueApiClient.ReadError(body);
                    if (request.responseCode >= 400 && failure != null && !string.IsNullOrEmpty(failure.code))
                    {
                        marker.AllowManualRetry();
                        Notice("提交失败（HTTP " + request.responseCode + "）：" + failure.message + "\n输入已保留，可修改后重试。", true);
                    }
                    else
                        Notice("提交结果尚未确认（网络中断、超时或响应异常）。输入已保留，请先点击“核对提交结果”，不要直接重复发送。", true);
                }
            }
            finally { activeRequest = null; busy = false; RefreshControls(); }
        }

        private IEnumerator VerifyPending()
        {
            try
            {
                Notice("正在核对后端是否已保存本次问题…", false);
                using (var request = IssueApiClient.GetIssues(config))
                {
                    activeRequest = request;
                    if (!IssueApiClient.TrySend(request, out var operation, out var error))
                    { Notice("核对失败：" + error, true); yield break; }
                    yield return operation;
                    if (request.result != UnityWebRequest.Result.Success || !IssueApiClient.TryReadList(request.downloadHandler.text, out var issues))
                    { Notice("核对失败，请恢复网络后重试核对。尚未再次发送问题。", true); yield break; }
                    var matches = issues.Where(issue => !marker.PreviousIDs.Contains(issue.id) && marker.PendingPayload.Matches(issue)).ToArray();
                    if (matches.Length == 1) Saved(matches[0]);
                    else if (matches.Length > 1)
                        Notice("发现多条匹配记录，请先在管理端核对；为避免重复，仍暂停再次提交。", true);
                    else
                    {
                        retryChecked = true;
                        Notice("暂未发现新增匹配记录。请在管理端确认无记录后，点击下方“允许手动重试”；本次核对没有发送新问题。", true);
                    }
                }
            }
            finally { activeRequest = null; busy = false; RefreshControls(); }
        }

        private void Saved(IssueRecord issue)
        {
            marker.MarkSaved(issue);
            retryChecked = false;
            Notice("提交成功，状态：" + issue.status + "\nID：" + issue.id + "\n可在电脑管理端刷新查看。", false, true);
            Debug.Log("巡检提交成功：" + issue.id, this);
        }

        private void Notice(string message, bool error, bool success = false)
        {
            if (form == null) return;
            form.Message.text = message;
            form.Message.color = error ? new Color(.73f, .18f, .12f) : success ? new Color(.08f, .45f, .2f) : new Color(.15f, .24f, .2f);
        }

        private void RefreshControls()
        {
            if (form == null) return;
            bool editable = !busy && config != null && marker != null && !marker.IsSubmitted && !marker.IsUncertain;
            form.Title.interactable = form.Description.interactable = form.Priority.interactable = editable;
            form.Submit.interactable = !busy && config != null && marker != null && !marker.IsSubmitted;
            form.Next.interactable = !busy && (marker == null || !marker.IsUncertain);
            form.Retry.gameObject.SetActive(!busy && retryChecked && marker != null && marker.IsUncertain);
            form.SubmitLabel.text = busy ? "正在处理…" : marker != null && marker.IsSubmitted ? "已提交，勿重复发送" :
                marker != null && marker.IsUncertain ? "核对提交结果" : "提交问题";
        }

        private void OnDestroy()
        {
            activeRequest?.Abort();
            StopAllCoroutines();
            form?.Dispose();
        }
    }
}
