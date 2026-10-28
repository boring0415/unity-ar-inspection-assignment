using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace MinimalAR
{
    /// <summary>运行时创建独立 uGUI 表单，保留原有场景与 AR 相机。</summary>
    public sealed class InspectionForm : IDisposable
    {
        public InputField Title { get; private set; }
        public InputField Description { get; private set; }
        public Dropdown Priority { get; private set; }
        public Button Submit { get; private set; }
        public Button Next { get; private set; }
        public Button Retry { get; private set; }
        public Text Coordinates { get; private set; }
        public Text Message { get; private set; }
        public Text SubmitLabel { get; private set; }
        public GameObject Root { get; private set; }
        private GameObject ownedEventSystem;
        private RectTransform safeArea;
        private RectTransform panel;
        private readonly Font font;
        private readonly Color ink = new Color(.1f, .19f, .17f);

        public InspectionForm()
        {
            font = Resources.Load<Font>("InspectionFont");
            if (font == null) throw new InvalidOperationException("缺少 Resources/InspectionFont.otf 中文字体。");
            Root = new GameObject("Inspection Submission UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = Root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = Root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(720, 1280);
            scaler.matchWidthOrHeight = .5f;
            if (EventSystem.current == null)
            {
                ownedEventSystem = new GameObject("Inspection EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                var module = ownedEventSystem.GetComponent<InputSystemUIInputModule>();
                if (module.actionsAsset == null) module.AssignDefaultActions();
            }

            safeArea = Rect("Safe Area", Root.transform);
            panel = Rect("Form Panel", safeArea);
            panel.anchorMin = new Vector2(.025f, 0);
            panel.anchorMax = new Vector2(.975f, 0);
            panel.pivot = new Vector2(.5f, 0);
            panel.anchoredPosition = new Vector2(0, 12);
            panel.gameObject.AddComponent<Image>().color = new Color(.95f, .98f, .96f, .97f);

            var viewport = Rect("Form Viewport", panel);
            Fill(viewport, 8);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Form Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 16, 16);
            layout.spacing = 8;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = panel.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28;

            Label("巡检问题提交", content, 30, 40);
            Label("标题 *", content, 22, 28);
            Title = Field("Title", "请输入问题标题", content, 56, false);
            Label("描述（选填）", content, 22, 28);
            Description = Field("Description", "补充问题情况", content, 88, true);
            Label("优先级", content, 22, 28);
            Priority = PriorityDropdown(content);
            Coordinates = Label("尚未放置标记", content, 21, 64);
            Message = Label("先点击上方蓝色水平面放置标记，再填写并提交。", content, 22, 112);
            Submit = Button("提交问题", content, 58, out var submitLabel);
            SubmitLabel = submitLabel;
            Retry = Button("已核对无记录，允许手动重试", content, 52, out _);
            Retry.gameObject.SetActive(false);
            Next = Button("开始下一条（保留已有标记）", content, 52, out _);
            UpdateSafeArea();
        }

        public void UpdateSafeArea()
        {
            var bounds = Screen.safeArea;
            if (Screen.width == 0 || Screen.height == 0) return;
            safeArea.anchorMin = new Vector2(bounds.xMin / Screen.width, bounds.yMin / Screen.height);
            safeArea.anchorMax = new Vector2(bounds.xMax / Screen.width, bounds.yMax / Screen.height);
            safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
            // 软键盘报告有效遮挡区域时，将表单移到键盘上方；内容可上下滚动。
            float keyboard = TouchScreenKeyboard.visible ? TouchScreenKeyboard.area.height : 0;
            var scale = Root.GetComponent<Canvas>().scaleFactor;
            float usable = Mathf.Max(250, safeArea.rect.height - keyboard / Mathf.Max(.01f, scale));
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Min(790, usable * .64f));
            panel.anchoredPosition = new Vector2(0, 12 + keyboard / Mathf.Max(.01f, scale));
        }

        public void Dispose()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root);
            if (ownedEventSystem != null) UnityEngine.Object.Destroy(ownedEventSystem);
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var item = new GameObject(name, typeof(RectTransform));
            item.transform.SetParent(parent, false);
            var rect = item.GetComponent<RectTransform>();
            rect.sizeDelta = Vector2.zero;
            return rect;
        }

        private static void Fill(RectTransform rect, float inset = 0)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.one * inset;
            rect.offsetMax = Vector2.one * -inset;
        }

        private static void Height(GameObject item, float height) => item.AddComponent<LayoutElement>().preferredHeight = height;

        private Text TextAt(string value, Transform parent, int size)
        {
            var text = Rect("Text", parent).gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.text = value;
            text.color = ink;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            // CJK 字体的行高高于字号；按几何对齐，避免中文整行被 Truncate 裁掉。
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.alignByGeometry = true;
            text.lineSpacing = .9f;
            return text;
        }

        private Text Label(string value, Transform parent, int size, float height)
        {
            var text = TextAt(value, parent, size);
            Height(text.gameObject, height);
            return text;
        }

        private InputField Field(string name, string placeholder, Transform parent, float height, bool multiline)
        {
            var rect = Rect(name, parent);
            rect.gameObject.AddComponent<Image>().color = Color.white;
            Height(rect.gameObject, height);
            var text = TextAt("", rect, 24);
            Fill(text.rectTransform, 10);
            text.alignment = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
            var hint = TextAt(placeholder, rect, 24);
            Fill(hint.rectTransform, 10);
            hint.color = new Color(.5f, .56f, .53f);
            var input = rect.gameObject.AddComponent<InputField>();
            input.textComponent = text;
            input.placeholder = hint;
            input.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            input.characterLimit = multiline ? 2000 : 200;
            return input;
        }

        private Button Button(string value, Transform parent, float height, out Text text)
        {
            var rect = Rect(value, parent);
            rect.gameObject.AddComponent<Image>().color = new Color(.17f, .43f, .33f);
            Height(rect.gameObject, height);
            text = TextAt(value, rect, 23);
            Fill(text.rectTransform, 6);
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            return button;
        }

        private Dropdown PriorityDropdown(Transform parent)
        {
            var rect = Rect("Priority", parent);
            rect.gameObject.AddComponent<Image>().color = Color.white;
            Height(rect.gameObject, 54);
            var caption = TextAt("", rect, 24);
            Fill(caption.rectTransform, 10);
            var dropdown = rect.gameObject.AddComponent<Dropdown>();
            dropdown.captionText = caption;
            dropdown.targetGraphic = rect.GetComponent<Image>();

            var template = Rect("Template", rect);
            template.anchorMin = Vector2.zero;
            template.anchorMax = new Vector2(1, 0);
            template.pivot = new Vector2(.5f, 1);
            template.anchoredPosition = Vector2.zero;
            template.sizeDelta = new Vector2(0, 180);
            template.gameObject.AddComponent<Image>().color = Color.white;
            var viewport = Rect("Viewport", template);
            Fill(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(.5f, 1);
            content.sizeDelta = new Vector2(0, 56);
            var item = Rect("Item", content);
            Fill(item);
            var background = item.gameObject.AddComponent<Image>();
            background.color = new Color(.9f, .95f, .92f);
            var label = TextAt("优先级", item, 24);
            Fill(label.rectTransform, 10);
            var toggle = item.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = background;
            var scroll = template.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            dropdown.template = template;
            dropdown.itemText = label;
            dropdown.AddOptions(new System.Collections.Generic.List<string> { "低 · low", "中 · medium", "高 · high" });
            dropdown.value = 1;
            template.gameObject.SetActive(false);
            return dropdown;
        }
    }
}
