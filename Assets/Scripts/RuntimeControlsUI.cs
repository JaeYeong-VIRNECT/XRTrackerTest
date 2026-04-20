using UnityEngine;
using UnityEngine.UI;
using IV.FormulaTracker;

public class RuntimeControlsUI : MonoBehaviour
{
    const string VisualPivotName = "MeshVisualPivot";

    enum AdaptiveTrackingMode
    {
        AnchorHold,
        FastRelock
    }

    [SerializeField] private TrackedBody trackedBody;
    [SerializeField] private Transform scaleTarget;
    [SerializeField] private Transform rotationTarget;
    [SerializeField] private bool preferAnchorRetention = true;
    [SerializeField] private float anchorSmoothTime = 0.35f;
    [SerializeField] private float anchorRotationStability = 15000f;
    [SerializeField] private float anchorPositionStability = 60000f;
    [SerializeField] private float anchorCorrectionInterval = 0.75f;
    [SerializeField] private float anchorNiceQualityThreshold = 0.9f;
    [SerializeField] private bool enableAdaptiveRelock = true;
    [SerializeField] private float detectionRateTrackingThreshold = 0.1f;
    [SerializeField] private float relockQualityThreshold = 0.94f;
    [SerializeField] private int relockStableFrames = 6;
    [SerializeField] private float relockBlendDuration = 0.8f;
    [SerializeField] private float relockSmoothTime = 0.08f;
    [SerializeField] private float relockRotationStability = 4500f;
    [SerializeField] private float relockPositionStability = 22000f;
    [SerializeField] private float relockCorrectionInterval = 0.12f;
    [SerializeField] private float relockNiceQualityThreshold = 0.72f;
    [SerializeField] private float scaleStep = 1.15f;
    [SerializeField] private float rotationStep = 15f;
    [SerializeField] private float minScale = 0.01f;
    [SerializeField] private float maxScale = 50f;
    [SerializeField] private float uiRefreshInterval = 0.1f;
    [SerializeField] private float outlineRefreshInterval = 0.05f;

    float _startThr = 0.5f;
    float _stopThr = 0.5f;
    bool _suppressCallbacks;
    RectTransform _panelRoot;
    Text _scaleValueText;
    Text _rotationXValueText;
    Text _rotationYValueText;
    Text _startValueText;
    Text _stopValueText;
    Slider _scaleSlider;
    Slider _rotationXSlider;
    Slider _rotationYSlider;
    Slider _startSlider;
    Slider _stopSlider;
    TouchManipulator _touchManipulator;
    Canvas _canvas;
    EdgeOutlineRenderer[] _outlineRenderers;
    bool _anchorProfileLogged;
    int _stableRelockFrameCount;
    float _relockTimer;
    float _uiRefreshTimer;
    float _outlineRefreshTimer;
    float _lastSafeZoneWidth = -1f;
    float _lastCanvasScaleFactor = -1f;
    float _lastScaleValue = float.NaN;
    Vector3 _lastRotationValue = new Vector3(float.NaN, float.NaN, float.NaN);
    float _lastStartThreshold = float.NaN;
    float _lastStopThreshold = float.NaN;
    bool _outlineRefreshPending;
    AdaptiveTrackingMode _adaptiveTrackingMode;

    void OnEnable()
    {
        if (trackedBody == null) trackedBody = GetComponent<TrackedBody>();
        if (scaleTarget == null) scaleTarget = ResolveVisualTarget();
        if (rotationTarget == null) rotationTarget = ResolveVisualTarget();
        if (_touchManipulator == null) _touchManipulator = GetComponent<TouchManipulator>();
        if (_outlineRenderers == null || _outlineRenderers.Length == 0) _outlineRenderers = GetComponents<EdgeOutlineRenderer>();
        if (trackedBody != null)
        {
            ApplyAnchorRetentionProfile();
            _startThr = trackedBody.CustomQualityToStart;
            _stopThr = trackedBody.CustomQualityToStop;
        }

        BuildUI();
        RefreshUI();
    }

    void OnDisable()
    {
        if (_panelRoot != null)
            _panelRoot.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (_panelRoot != null)
            Destroy(_panelRoot.gameObject);
    }

    void Update()
    {
        UpdateAdaptiveTrackingProfile();
        UpdateManipulationSafeZone();

        _uiRefreshTimer += Time.unscaledDeltaTime;
        if (_uiRefreshTimer >= uiRefreshInterval)
        {
            _uiRefreshTimer = 0f;
            RefreshUIIfNeeded();
        }

        if (_outlineRefreshPending)
        {
            _outlineRefreshTimer += Time.unscaledDeltaTime;
            if (_outlineRefreshTimer >= outlineRefreshInterval)
            {
                _outlineRefreshTimer = 0f;
                _outlineRefreshPending = false;
                RefreshOutline();
            }
        }
    }

    void BuildUI()
    {
        if (_panelRoot != null)
        {
            _panelRoot.gameObject.SetActive(true);
            return;
        }

        _canvas = RuntimeUIFactory.EnsureCanvas();
        _panelRoot = RuntimeUIFactory.CreatePanel(
            _canvas.transform,
            "Runtime Controls Panel",
            new Vector2(1f, 0.5f),
            new Vector2(1f, 0.5f),
            new Vector2(1f, 0.5f),
            new Vector2(-24f, 0f),
            new Vector2(360f, 760f),
            new Color(0.05f, 0.08f, 0.12f, 0.86f));

        var layout = _panelRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(22, 22, 22, 22);
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        RuntimeUIFactory.CreateText(_panelRoot, "Title", "MODEL SCALE", 30, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white, 42f);
        _scaleValueText = RuntimeUIFactory.CreateText(_panelRoot, "Scale Value", "Scale 1.00x", 24, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(0.82f, 0.91f, 1f), 34f);

        _scaleSlider = RuntimeUIFactory.CreateSlider(_panelRoot, "Scale Slider", new Color(1f, 1f, 1f, 0.18f), new Color(0.18f, 0.72f, 1f, 0.95f), Color.white, 30f);
        _scaleSlider.minValue = minScale;
        _scaleSlider.maxValue = maxScale;
        _scaleSlider.onValueChanged.AddListener(HandleScaleSliderChanged);

        var scaleButtons = RuntimeUIFactory.CreateRect(_panelRoot, "Scale Buttons");
        var scaleButtonsLayout = scaleButtons.gameObject.AddComponent<HorizontalLayoutGroup>();
        scaleButtonsLayout.spacing = 12f;
        scaleButtonsLayout.childAlignment = TextAnchor.MiddleCenter;
        scaleButtonsLayout.childControlHeight = true;
        scaleButtonsLayout.childControlWidth = true;
        scaleButtonsLayout.childForceExpandHeight = false;
        scaleButtonsLayout.childForceExpandWidth = true;
        var scaleButtonsElement = scaleButtons.gameObject.AddComponent<LayoutElement>();
        scaleButtonsElement.preferredHeight = 58f;

        var minusButton = RuntimeUIFactory.CreateButton(scaleButtons, "Scale Down", "-", new Color(0.15f, 0.2f, 0.28f, 0.95f), Color.white);
        minusButton.onClick.AddListener(() => ScaleBy(1f / scaleStep));
        var plusButton = RuntimeUIFactory.CreateButton(scaleButtons, "Scale Up", "+", new Color(0.15f, 0.2f, 0.28f, 0.95f), Color.white);
        plusButton.onClick.AddListener(() => ScaleBy(scaleStep));

        RuntimeUIFactory.CreateText(_panelRoot, "Rotation Title", "MODEL ROTATION", 24, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white, 34f);

        _rotationXValueText = CreateLabeledSlider(_panelRoot, "Rotate X", out _rotationXSlider, HandleRotationXChanged);
        _rotationXSlider.minValue = 0f;
        _rotationXSlider.maxValue = 360f;

        var rotateXButtons = CreateStepButtons(_panelRoot, "Rotate X Buttons", () => RotateBy(-rotationStep, 0f), () => RotateBy(rotationStep, 0f));
        var rotateXLeftText = rotateXButtons.leftButton.GetComponentInChildren<Text>();
        if (rotateXLeftText != null) rotateXLeftText.text = "X-";
        var rotateXRightText = rotateXButtons.rightButton.GetComponentInChildren<Text>();
        if (rotateXRightText != null) rotateXRightText.text = "X+";

        _rotationYValueText = CreateLabeledSlider(_panelRoot, "Rotate Y", out _rotationYSlider, HandleRotationYChanged);
        _rotationYSlider.minValue = 0f;
        _rotationYSlider.maxValue = 360f;

        var rotateYButtons = CreateStepButtons(_panelRoot, "Rotate Y Buttons", () => RotateBy(0f, -rotationStep), () => RotateBy(0f, rotationStep));
        var rotateYLeftText = rotateYButtons.leftButton.GetComponentInChildren<Text>();
        if (rotateYLeftText != null) rotateYLeftText.text = "Y-";
        var rotateYRightText = rotateYButtons.rightButton.GetComponentInChildren<Text>();
        if (rotateYRightText != null) rotateYRightText.text = "Y+";

        RuntimeUIFactory.CreateText(_panelRoot, "Threshold Title", "TRACKING THRESHOLDS", 24, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white, 34f);

        _startValueText = CreateLabeledSlider(_panelRoot, "Start Threshold", out _startSlider, HandleStartThresholdChanged);
        _startSlider.minValue = 0f;
        _startSlider.maxValue = 1f;

        _stopValueText = CreateLabeledSlider(_panelRoot, "Stop Threshold", out _stopSlider, HandleStopThresholdChanged);
        _stopSlider.minValue = 0f;
        _stopSlider.maxValue = 1f;

        var resetButton = RuntimeUIFactory.CreateButton(_panelRoot, "Reset Tracking", "RESET TRACKING", new Color(0.89f, 0.39f, 0.24f, 0.95f), Color.white, 60f);
        resetButton.onClick.AddListener(ResetTracking);
    }

    Text CreateLabeledSlider(Transform parent, string label, out Slider slider, UnityEngine.Events.UnityAction<float> listener)
    {
        var block = RuntimeUIFactory.CreateRect(parent, label + " Block");
        var blockLayout = block.gameObject.AddComponent<VerticalLayoutGroup>();
        blockLayout.spacing = 8f;
        blockLayout.childAlignment = TextAnchor.MiddleCenter;
        blockLayout.childControlHeight = true;
        blockLayout.childControlWidth = true;
        blockLayout.childForceExpandHeight = false;
        blockLayout.childForceExpandWidth = true;

        var blockElement = block.gameObject.AddComponent<LayoutElement>();
        blockElement.preferredHeight = 90f;

        var valueText = RuntimeUIFactory.CreateText(block, label + " Value", label, 20, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.86f, 0.93f, 1f), 26f);
        slider = RuntimeUIFactory.CreateSlider(block, label + " Slider", new Color(1f, 1f, 1f, 0.14f), new Color(0.22f, 0.9f, 0.66f, 0.95f), Color.white, 28f);
        slider.onValueChanged.AddListener(listener);
        return valueText;
    }

    (Button leftButton, Button rightButton) CreateStepButtons(Transform parent, string name, UnityEngine.Events.UnityAction leftAction, UnityEngine.Events.UnityAction rightAction)
    {
        var buttonsRoot = RuntimeUIFactory.CreateRect(parent, name);
        var layout = buttonsRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        var element = buttonsRoot.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = 52f;

        var leftButton = RuntimeUIFactory.CreateButton(buttonsRoot, name + " Left", "-", new Color(0.15f, 0.2f, 0.28f, 0.95f), Color.white, 50f);
        leftButton.onClick.AddListener(leftAction);

        var rightButton = RuntimeUIFactory.CreateButton(buttonsRoot, name + " Right", "+", new Color(0.15f, 0.2f, 0.28f, 0.95f), Color.white, 50f);
        rightButton.onClick.AddListener(rightAction);

        return (leftButton, rightButton);
    }

    void RefreshUI()
    {
        if (scaleTarget == null)
            scaleTarget = ResolveVisualTarget();

        if (scaleTarget == null || _scaleSlider == null)
            return;

        if (rotationTarget == null)
            rotationTarget = ResolveVisualTarget();

        _suppressCallbacks = true;

        float currentScale = Mathf.Clamp(scaleTarget.localScale.x, minScale, maxScale);
        _scaleSlider.value = currentScale;
        _scaleValueText.text = $"Scale {currentScale:F2}x";

        Vector3 currentRotation = rotationTarget != null ? rotationTarget.localEulerAngles : Vector3.zero;
        if (_rotationXSlider != null)
            _rotationXSlider.value = NormalizeAngle(currentRotation.x);
        if (_rotationYSlider != null)
            _rotationYSlider.value = NormalizeAngle(currentRotation.y);
        if (_rotationXValueText != null)
            _rotationXValueText.text = $"Rotate X  {NormalizeAngle(currentRotation.x):F0} deg";
        if (_rotationYValueText != null)
            _rotationYValueText.text = $"Rotate Y  {NormalizeAngle(currentRotation.y):F0} deg";

        if (trackedBody != null)
        {
            _startThr = trackedBody.CustomQualityToStart;
            _stopThr = trackedBody.CustomQualityToStop;

            if (_startSlider != null)
                _startSlider.value = _startThr;
            if (_stopSlider != null)
                _stopSlider.value = _stopThr;

            if (_startValueText != null)
                _startValueText.text = $"Start Threshold  {_startThr:P0}";
            if (_stopValueText != null)
                _stopValueText.text = $"Stop Threshold  {_stopThr:P0}";
        }

        _suppressCallbacks = false;

        _lastScaleValue = currentScale;
        _lastRotationValue = currentRotation;
        _lastStartThreshold = _startThr;
        _lastStopThreshold = _stopThr;
    }

    void RefreshUIIfNeeded()
    {
        if (_scaleSlider == null)
            return;

        if (scaleTarget == null)
            scaleTarget = ResolveVisualTarget();

        if (rotationTarget == null)
            rotationTarget = ResolveVisualTarget();

        float currentScale = scaleTarget != null ? Mathf.Clamp(scaleTarget.localScale.x, minScale, maxScale) : float.NaN;
        Vector3 currentRotation = rotationTarget != null ? rotationTarget.localEulerAngles : Vector3.zero;
        float currentStart = trackedBody != null ? trackedBody.CustomQualityToStart : _startThr;
        float currentStop = trackedBody != null ? trackedBody.CustomQualityToStop : _stopThr;

        bool needsRefresh =
            !Mathf.Approximately(_lastScaleValue, currentScale) ||
            !Approximately(_lastRotationValue, currentRotation) ||
            !Mathf.Approximately(_lastStartThreshold, currentStart) ||
            !Mathf.Approximately(_lastStopThreshold, currentStop);

        if (needsRefresh)
            RefreshUI();
    }

    void HandleScaleSliderChanged(float value)
    {
        if (_suppressCallbacks)
            return;

        SetScale(value);
    }

    void HandleRotationXChanged(float value)
    {
        if (_suppressCallbacks)
            return;

        SetRotation(value, null);
    }

    void HandleRotationYChanged(float value)
    {
        if (_suppressCallbacks)
            return;

        SetRotation(null, value);
    }

    void HandleStartThresholdChanged(float value)
    {
        if (_suppressCallbacks || trackedBody == null)
            return;

        _startThr = value;
        trackedBody.UseCustomStartThreshold = true;
        trackedBody.CustomQualityToStart = value;
        if (_startValueText != null)
            _startValueText.text = $"Start Threshold  {value:P0}";
    }

    void HandleStopThresholdChanged(float value)
    {
        if (_suppressCallbacks || trackedBody == null)
            return;

        _stopThr = value;
        trackedBody.UseCustomStopThreshold = true;
        trackedBody.CustomQualityToStop = value;
        if (_stopValueText != null)
            _stopValueText.text = $"Stop Threshold  {value:P0}";
    }

    void ScaleBy(float factor)
    {
        if (scaleTarget == null)
            return;

        SetScale(scaleTarget.localScale.x * factor);
    }

    void SetScale(float value)
    {
        if (scaleTarget == null)
            return;

        float target = Mathf.Clamp(value, minScale, maxScale);
        scaleTarget.localScale = Vector3.one * target;
        QueueOutlineRefresh();
        if (_scaleValueText != null)
            _scaleValueText.text = $"Scale {target:F2}x";
    }

    void RotateBy(float deltaX, float deltaY)
    {
        if (rotationTarget == null)
            rotationTarget = ResolveVisualTarget();

        if (rotationTarget == null)
            return;

        Vector3 currentRotation = rotationTarget.localEulerAngles;
        SetRotation(currentRotation.x + deltaX, currentRotation.y + deltaY);
    }

    void SetRotation(float? xDegrees, float? yDegrees)
    {
        if (rotationTarget == null)
            rotationTarget = ResolveVisualTarget();

        if (rotationTarget == null)
            return;

        Vector3 currentRotation = rotationTarget.localEulerAngles;
        float nextX = NormalizeAngle(xDegrees ?? currentRotation.x);
        float nextY = NormalizeAngle(yDegrees ?? currentRotation.y);
        rotationTarget.localRotation = Quaternion.Euler(nextX, nextY, currentRotation.z);
        QueueOutlineRefresh();

        if (_rotationXValueText != null)
            _rotationXValueText.text = $"Rotate X  {nextX:F0} deg";
        if (_rotationYValueText != null)
            _rotationYValueText.text = $"Rotate Y  {nextY:F0} deg";
    }

    Transform ResolveVisualTarget()
    {
        Transform pivot = transform.Find(VisualPivotName);
        if (pivot != null)
            return pivot;

        pivot = CreateVisualPivot();
        if (pivot != null)
            return pivot;

        Transform preferredChild = null;
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Transform candidate = renderers[i].transform;
            while (candidate.parent != null && candidate.parent != transform)
                candidate = candidate.parent;

            if (candidate == transform)
                continue;

            if (candidate.name == "Viewpoint")
                continue;

            preferredChild = candidate;
            break;
        }

        if (preferredChild != null)
            return preferredChild;

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child.name != "Viewpoint")
                return child;
        }

        return transform;
    }

    Transform CreateVisualPivot()
    {
        Bounds bounds;
        if (!TryGetVisualBounds(out bounds))
            return null;

        var visualChildren = new System.Collections.Generic.List<Transform>();
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (!IsVisualRootCandidate(child))
                continue;

            visualChildren.Add(child);
        }

        if (visualChildren.Count == 0)
            return null;

        var pivotObject = new GameObject(VisualPivotName);
        Transform pivot = pivotObject.transform;
        pivot.SetParent(transform, false);
        pivot.position = bounds.center;
        pivot.rotation = transform.rotation;
        pivot.localScale = Vector3.one;

        for (int i = 0; i < visualChildren.Count; i++)
            visualChildren[i].SetParent(pivot, true);

        return pivot;
    }

    bool TryGetVisualBounds(out Bounds bounds)
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        bounds = default;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            Transform rootChild = GetRootChild(renderer.transform);
            if (!IsVisualRootCandidate(rootChild))
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
                continue;
            }

            bounds.Encapsulate(renderer.bounds);
        }

        return hasBounds;
    }

    Transform GetRootChild(Transform candidate)
    {
        while (candidate != null && candidate.parent != null && candidate.parent != transform)
            candidate = candidate.parent;

        return candidate;
    }

    bool IsVisualRootCandidate(Transform candidate)
    {
        if (candidate == null || candidate == transform)
            return false;

        if (candidate.name == "Viewpoint" || candidate.name == VisualPivotName)
            return false;

        return candidate.GetComponentInChildren<Renderer>(true) != null;
    }

    static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle < 0f)
            angle += 360f;
        return angle;
    }

    void ResetTracking()
    {
        if (trackedBody != null)
            trackedBody.ResetTracking();
    }

    void UpdateManipulationSafeZone()
    {
        if (_touchManipulator == null || _panelRoot == null || _canvas == null)
            return;

        float panelWidth = _panelRoot.rect.width;
        float scaleFactor = _canvas.scaleFactor;
        if (Mathf.Approximately(panelWidth, _lastSafeZoneWidth) && Mathf.Approximately(scaleFactor, _lastCanvasScaleFactor))
            return;

        _lastSafeZoneWidth = panelWidth;
        _lastCanvasScaleFactor = scaleFactor;

        int ignoreWidth = Mathf.RoundToInt((panelWidth + 56f) * scaleFactor);
        _touchManipulator.SetRightEdgeIgnorePixels(ignoreWidth);
    }

    static bool Approximately(Vector3 a, Vector3 b)
    {
        return Mathf.Approximately(a.x, b.x) && Mathf.Approximately(a.y, b.y) && Mathf.Approximately(a.z, b.z);
    }

    void QueueOutlineRefresh()
    {
        _outlineRefreshPending = true;
    }

    void RefreshOutline()
    {
        if (_outlineRenderers == null || _outlineRenderers.Length == 0)
            _outlineRenderers = GetComponents<EdgeOutlineRenderer>();

        for (int i = 0; i < _outlineRenderers.Length; i++)
        {
            if (_outlineRenderers[i] != null)
                _outlineRenderers[i].SetDirty();
        }
    }

    void ApplyAnchorRetentionProfile()
    {
        if (trackedBody == null || !preferAnchorRetention || !trackedBody.IsStationary)
            return;

        ApplyTrackingProfile(
            AdaptiveTrackingMode.AnchorHold,
            anchorSmoothTime,
            anchorRotationStability,
            anchorPositionStability,
            anchorCorrectionInterval,
            anchorNiceQualityThreshold);

        if (_anchorProfileLogged)
            return;

        Debug.Log($"[RuntimeControlsUI] Anchor retention profile applied: smooth={trackedBody.SmoothTime:F2}s rot={trackedBody.RotationStability:F0} pos={trackedBody.PositionStability:F0} nice={trackedBody.CustomNiceQualityThreshold:F2} correction={trackedBody.QualityGateCorrectionInterval:F2}s");
        _anchorProfileLogged = true;
    }

    void UpdateAdaptiveTrackingProfile()
    {
        if (trackedBody == null || !preferAnchorRetention || !trackedBody.IsStationary)
            return;

        if (!enableAdaptiveRelock)
        {
            ApplyAnchorRetentionProfile();
            return;
        }

        float detectionRate = CalculateDetectionRate();
        bool objectVisible = detectionRate >= detectionRateTrackingThreshold;

        if (objectVisible)
        {
            ApplyTrackingProfile(
                AdaptiveTrackingMode.FastRelock,
                relockSmoothTime,
                relockRotationStability,
                relockPositionStability,
                relockCorrectionInterval,
                relockNiceQualityThreshold);
            return;
        }

        _stableRelockFrameCount = 0;
        _relockTimer = 0f;
        ApplyAnchorRetentionProfile();
    }

    void ApplyTrackingProfile(
        AdaptiveTrackingMode mode,
        float smoothTime,
        float rotationStability,
        float positionStability,
        float correctionInterval,
        float niceQualityThreshold)
    {
        if (trackedBody == null)
            return;

        bool stabilityChanged = false;

        if (!Mathf.Approximately(trackedBody.SmoothTime, smoothTime))
            trackedBody.SmoothTime = smoothTime;

        if (!Mathf.Approximately(trackedBody.RotationStability, rotationStability))
        {
            trackedBody.RotationStability = rotationStability;
            stabilityChanged = true;
        }

        if (!Mathf.Approximately(trackedBody.PositionStability, positionStability))
        {
            trackedBody.PositionStability = positionStability;
            stabilityChanged = true;
        }

        trackedBody.UseCustomNiceQuality = true;
        trackedBody.CustomNiceQualityThreshold = Mathf.Clamp01(niceQualityThreshold);
        trackedBody.QualityGateCorrectionInterval = Mathf.Max(0.05f, correctionInterval);

        if (Application.isPlaying && stabilityChanged && trackedBody.IsRegistered)
            trackedBody.UpdateStabilityParameters();

        if (_adaptiveTrackingMode == mode)
            return;

        _adaptiveTrackingMode = mode;
        Debug.Log($"[RuntimeControlsUI] Tracking profile -> {mode} (quality={trackedBody.InstantQuality:F2}, status={trackedBody.TrackingStatus})");
    }

    float CalculateDetectionRate()
    {
        if (trackedBody == null)
            return 0f;

        float projectionError = trackedBody.ProjectionErrorAverage;
        float edgeCoverage = trackedBody.EdgeCoverageAverage;
        float visibility = trackedBody.Visibility;

        float projectionQuality = projectionError <= 0f
            ? 0f
            : 1f - Mathf.InverseLerp(0.35f, 4f, projectionError);

        float edgeQuality = Mathf.Clamp01(edgeCoverage);
        float visibilityQuality = Mathf.Clamp01(visibility);
        float trackingFactor = trackedBody.IsTracking ? 1f : 0.35f;

        float rawScore = projectionQuality * 0.4f + edgeQuality * 0.35f + visibilityQuality * 0.25f;
        return Mathf.Clamp01(rawScore * trackingFactor);
    }
}
