using System.Collections.Generic;
using UnityEngine;

namespace MinimalAR
{
    /// <summary>只记录当前会话内这个实际立方体的提交结果，不改变其锚点或姿态。</summary>
    public sealed class InspectionMarker : MonoBehaviour
    {
        public IssueRecord SavedIssue { get; private set; }
        public CreateIssuePayload PendingPayload { get; private set; }
        public readonly HashSet<string> PreviousIDs = new HashSet<string>();
        public bool IsSubmitted => SavedIssue != null;
        public bool IsUncertain => PendingPayload != null;

        public void BeginAttempt(CreateIssuePayload payload, IssueRecord[] existing)
        {
            PendingPayload = payload;
            PreviousIDs.Clear();
            foreach (var issue in existing) PreviousIDs.Add(issue.id);
        }

        public void MarkSaved(IssueRecord issue) { SavedIssue = issue; PendingPayload = null; }
        public void AllowManualRetry() { PendingPayload = null; }
    }
}
