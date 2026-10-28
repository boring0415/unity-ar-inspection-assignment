using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MinimalAR
{
    public static class InspectionInputGuard
    {
        private static readonly List<RaycastResult> Hits = new List<RaycastResult>();

        public static bool IsOverUI(Vector2 screenPosition)
        {
            var events = EventSystem.current;
            if (events == null) return false;
            // 直接按本次触点做 UI 射线，避免 Input System 的上帧 pointer 状态造成首帧穿透。
            Hits.Clear();
            events.RaycastAll(new PointerEventData(events) { position = screenPosition }, Hits);
            foreach (var hit in Hits)
                if (hit.module is GraphicRaycaster) return true;
            var selected = events.currentSelectedGameObject;
            var field = selected != null ? selected.GetComponent<InputField>() : null;
            return field != null && field.isFocused;
        }
    }
}
