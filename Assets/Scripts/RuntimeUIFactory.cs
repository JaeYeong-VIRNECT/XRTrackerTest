using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class RuntimeUIFactory
{
    const string CanvasName = "XRTracker Runtime Canvas";

    static Font _font;

    public static Canvas EnsureCanvas()
    {
        var existing = GameObject.Find(CanvasName);
        if (existing != null)
            return existing.GetComponent<Canvas>();

        var canvasGo = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        EnsureEventSystem();
        return canvas;
    }

    public static Font GetFont()
    {
        if (_font == null)
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        return _font;
    }

    public static RectTransform CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size, Color background)
    {
        var rect = CreateRect(parent, name);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        var image = rect.gameObject.AddComponent<Image>();
        image.color = background;
        return rect;
    }

    public static RectTransform CreateRect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    public static Text CreateText(Transform parent, string name, string value, int fontSize, FontStyle fontStyle, TextAnchor alignment, Color color, float preferredHeight = -1f)
    {
        var rect = CreateRect(parent, name);
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        var text = rect.gameObject.AddComponent<Text>();
        text.font = GetFont();
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        if (preferredHeight > 0f)
        {
            var layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = preferredHeight;
        }

        return text;
    }

    public static Button CreateButton(Transform parent, string name, string label, Color background, Color foreground, float preferredHeight = 56f)
    {
        var rect = CreateRect(parent, name);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = background;

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;

        var colors = button.colors;
        colors.normalColor = background;
        colors.highlightedColor = background * 1.1f;
        colors.pressedColor = background * 0.9f;
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(background.r, background.g, background.b, background.a * 0.5f);
        button.colors = colors;

        var layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = preferredHeight;

        var labelText = CreateText(rect, "Label", label, 28, FontStyle.Bold, TextAnchor.MiddleCenter, foreground);
        labelText.raycastTarget = false;
        var labelRect = labelText.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        return button;
    }

    public static Slider CreateSlider(Transform parent, string name, Color trackColor, Color fillColor, Color handleColor, float preferredHeight = 28f)
    {
        var root = CreateRect(parent, name);
        var layout = root.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = preferredHeight;

        var slider = root.gameObject.AddComponent<Slider>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.transition = Selectable.Transition.None;

        var background = CreateRect(root, "Background");
        background.anchorMin = new Vector2(0f, 0.25f);
        background.anchorMax = new Vector2(1f, 0.75f);
        background.offsetMin = new Vector2(0f, 0f);
        background.offsetMax = new Vector2(0f, 0f);
        var backgroundImage = background.gameObject.AddComponent<Image>();
        backgroundImage.color = trackColor;

        var fillArea = CreateRect(root, "Fill Area");
        fillArea.anchorMin = new Vector2(0f, 0.25f);
        fillArea.anchorMax = new Vector2(1f, 0.75f);
        fillArea.offsetMin = new Vector2(10f, 0f);
        fillArea.offsetMax = new Vector2(-10f, 0f);

        var fill = CreateRect(fillArea, "Fill");
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(1f, 1f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
        var fillImage = fill.gameObject.AddComponent<Image>();
        fillImage.color = fillColor;

        var handleArea = CreateRect(root, "Handle Slide Area");
        handleArea.anchorMin = Vector2.zero;
        handleArea.anchorMax = Vector2.one;
        handleArea.offsetMin = new Vector2(10f, 0f);
        handleArea.offsetMax = new Vector2(-10f, 0f);

        var handle = CreateRect(handleArea, "Handle");
        handle.sizeDelta = new Vector2(24f, 24f);
        handle.anchorMin = new Vector2(0f, 0.5f);
        handle.anchorMax = new Vector2(0f, 0.5f);
        handle.pivot = new Vector2(0.5f, 0.5f);
        var handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = handleColor;

        slider.targetGraphic = handleImage;
        slider.fillRect = fill;
        slider.handleRect = handle;

        return slider;
    }

    public static RectTransform CreateFillBar(Transform parent, string name, Color backgroundColor, Color fillColor, out Image fillImage, float preferredHeight = 18f)
    {
        var root = CreateRect(parent, name);
        var layout = root.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = preferredHeight;

        var background = root.gameObject.AddComponent<Image>();
        background.color = backgroundColor;

        var fill = CreateRect(root, "Fill");
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(0f, 1f);
        fill.pivot = new Vector2(0f, 0.5f);
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
        fillImage = fill.gameObject.AddComponent<Image>();
        fillImage.color = fillColor;

        return root;
    }

    static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null)
            return;

        var go = new GameObject("XRTracker EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        Object.DontDestroyOnLoad(go);
    }
}