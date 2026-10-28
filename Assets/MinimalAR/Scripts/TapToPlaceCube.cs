using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace MinimalAR
{
    /// <summary>在已追踪的水平平面上点击一次，放置一个由本地 AR Anchor 追踪的立方体。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ARRaycastManager), typeof(ARPlaneManager), typeof(ARAnchorManager))]
    public sealed class TapToPlaceCube : MonoBehaviour
    {
        [SerializeField] private GameObject cubePrefab;
        private ARRaycastManager raycastManager;
        private ARPlaneManager planeManager;
        private ARAnchorManager anchorManager;
        private InspectionSubmission submission;
        private readonly List<ARRaycastHit> hits = new();

        private void Awake()
        {
            // 复用 XR Origin 上的现有管理器，不在运行时创建第二套 AR 场景。
            raycastManager = GetComponent<ARRaycastManager>();
            planeManager = GetComponent<ARPlaneManager>();
            anchorManager = GetComponent<ARAnchorManager>();
            // 在现有 XR Origin 上增量启用表单，不重新配置或改写原场景。
            submission = GetComponent<InspectionSubmission>();
            if (submission == null) submission = gameObject.AddComponent<InspectionSubmission>();
            if (cubePrefab == null)
            {
                Debug.LogError("请为 TapToPlaceCube 指定 Cube Prefab。", this);
                enabled = false;
            }
        }

        private void Update()
        {
            // 追踪尚未建立或已丢失时不放置，避免生成看似固定但没有可靠追踪的内容。
            if (ARSession.state != ARSessionState.SessionTracking ||
                !raycastManager.isActiveAndEnabled || !planeManager.isActiveAndEnabled ||
                !anchorManager.isActiveAndEnabled || anchorManager.subsystem == null ||
                !anchorManager.subsystem.running || !TryGetPressPosition(out var position))
                return;

            if ((submission != null && submission.BlockPlacement) || InspectionInputGuard.IsOverUI(position))
                return;

            // 这是 AR 平面射线检测，不是 Physics.Raycast，也不要求平面有 Collider。
            // PlaneWithinPolygon 只接受已经识别出的平面边界内部，不命中无限延伸平面。
            if (!raycastManager.Raycast(position, hits, TrackableType.PlaneWithinPolygon))
                return;

            // ARRaycastManager 按距离从近到远返回结果。
            foreach (var hit in hits)
            {
                var plane = planeManager.GetPlane(hit.trackableId);
                if (plane == null || plane.trackingState != TrackingState.Tracking ||
                    plane.subsumedBy != null || plane.alignment != PlaneAlignment.HorizontalUp)
                    continue;

                // 在命中姿态创建平面附着锚点。ARCore 持续更新锚点，内容不会跟随手机相机。
                // 锚点只在本次 AR 会话内有效；没有云锚点或跨次启动持久化。
                var anchor = anchorManager.AttachAnchor(plane, hit.pose);
                if (anchor == null)
                {
                    Debug.LogWarning("ARCore 暂时无法创建锚点，本次点击不生成立方体。", this);
                    return;
                }

                var cube = Instantiate(cubePrefab, anchor.transform);
                // Unity Cube 的原点在中心：沿命中平面局部向上抬升半个边长，使底面贴住平面。
                cube.transform.localPosition = Vector3.up * (cube.transform.localScale.y * 0.5f);
                cube.transform.localRotation = Quaternion.identity;
                submission?.SelectMarker(cube.transform);
                return;
            }
        }

        private static bool TryGetPressPosition(out Vector2 position)
        {
            // 项目 Active Input Handling = Input System Package (New)。仅按下当帧响应一次。
            var touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                var touch = touchscreen.primaryTouch;
                if (touch.press.wasPressedThisFrame)
                {
                    position = touch.position.ReadValue();
                    return true;
                }

                // 有触摸时不再处理可能由触摸模拟的鼠标，防止同一次点击重复放置。
                foreach (var activeTouch in touchscreen.touches)
                {
                    if (!activeTouch.press.isPressed)
                        continue;
                    position = default;
                    return false;
                }
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                position = mouse.position.ReadValue();
                return true;
            }

            position = default;
            return false;
        }
    }
}
