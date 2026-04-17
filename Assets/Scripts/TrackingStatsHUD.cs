using UnityEngine;
using UnityEngine.UI;
using IV.FormulaTracker;

public class TrackingStatsHUD : MonoBehaviour
{
    [SerializeField] private TrackedBody trackedBody;
    [SerializeField] private Vector2 anchor = new Vector2(24, -24);

    RectTransform _panelRoot;
    Text _licenseText;
    Text _statusText;
    Text _scoreText;
    Text _projectionText;
    Text _edgeText;
    Text _visibilityText;
    Image _scoreFill;

    void OnEnable()
    {
        if (trackedBody == null) trackedBody = GetComponent<TrackedBody>();
        BuildUI();
        RefreshStats();
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
        RefreshStats();
    }

    void BuildUI()
    {
        if (_panelRoot != null)
        {
            _panelRoot.gameObject.SetActive(true);
            return;
        }

        var canvas = RuntimeUIFactory.EnsureCanvas();
        _panelRoot = RuntimeUIFactory.CreatePanel(
            canvas.transform,
            "Tracking Stats Panel",
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            anchor,
            new Vector2(400f, 280f),
            new Color(0.04f, 0.08f, 0.11f, 0.82f));

        var layout = _panelRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 20, 20);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        RuntimeUIFactory.CreateText(_panelRoot, "Title", "DETECTION RATE", 28, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white, 36f);
        _scoreText = RuntimeUIFactory.CreateText(_panelRoot, "Score", "0%", 40, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.23f, 0.92f, 0.7f), 48f);
        RuntimeUIFactory.CreateFillBar(_panelRoot, "Score Bar", new Color(1f, 1f, 1f, 0.14f), new Color(0.23f, 0.92f, 0.7f), out _scoreFill, 20f);

        _statusText = RuntimeUIFactory.CreateText(_panelRoot, "Status", "Status: waiting", 20, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white, 26f);
        _licenseText = RuntimeUIFactory.CreateText(_panelRoot, "License", "License: waiting", 18, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.78f, 0.86f, 0.94f), 24f);
        _projectionText = RuntimeUIFactory.CreateText(_panelRoot, "Projection", "Projection Error: -", 18, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white, 24f);
        _edgeText = RuntimeUIFactory.CreateText(_panelRoot, "Edge", "Edge Coverage: -", 18, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white, 24f);
        _visibilityText = RuntimeUIFactory.CreateText(_panelRoot, "Visibility", "Visibility: -", 18, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white, 24f);
    }

    void RefreshStats()
    {
        if (_panelRoot == null ||
            _licenseText == null ||
            _statusText == null ||
            _scoreText == null ||
            _projectionText == null ||
            _edgeText == null ||
            _visibilityText == null)
            return;

        if (trackedBody == null)
        {
            _statusText.text = "Status: no TrackedBody";
            _licenseText.text = "License: manager not found";
            _scoreText.text = "0%";
            SetScoreVisuals(0f);
            return;
        }

        var manager = XRTrackerManager.Instance;
        _licenseText.text = manager == null
            ? "License: manager not found"
            : $"License: {manager.LicenseTier} / {manager.LicenseStatus}";

        float projectionError = trackedBody.ProjectionErrorAverage;
        float edgeCoverage = trackedBody.EdgeCoverageAverage;
        float visibility = trackedBody.Visibility;
        float score = CalculateDetectionScore(projectionError, edgeCoverage, visibility, trackedBody.IsTracking);

        _statusText.text = $"Status: {trackedBody.TrackingStatus}   Tracking {(trackedBody.IsTracking ? "ON" : "OFF")}";
        _scoreText.text = $"{Mathf.RoundToInt(score * 100f)}%  {DescribeScore(score)}";
        _projectionText.text = $"Projection Error: {projectionError:F2} deg  {DescribeProjection(projectionError)}";
        _edgeText.text = $"Edge Coverage: {edgeCoverage:P0}  {DescribeHighMetric(edgeCoverage, 0.7f, 0.4f)}";
        _visibilityText.text = $"Visibility: {visibility:P0}  {DescribeHighMetric(visibility, 0.6f, 0.3f)}";

        SetScoreVisuals(score);
    }

    void SetScoreVisuals(float score)
    {
        score = Mathf.Clamp01(score);
        if (_scoreFill != null)
            _scoreFill.rectTransform.anchorMax = new Vector2(score, 1f);

        Color tint = score >= 0.75f
            ? new Color(0.23f, 0.92f, 0.7f)
            : score >= 0.45f
                ? new Color(1f, 0.77f, 0.25f)
                : new Color(0.96f, 0.37f, 0.34f);

        if (_scoreFill != null)
            _scoreFill.color = tint;
        if (_scoreText != null)
            _scoreText.color = tint;
    }

    static float CalculateDetectionScore(float projectionError, float edgeCoverage, float visibility, bool isTracking)
    {
        float projectionQuality = projectionError <= 0f
            ? 0f
            : 1f - Mathf.InverseLerp(0.35f, 4f, projectionError);

        float edgeQuality = Mathf.Clamp01(edgeCoverage);
        float visibilityQuality = Mathf.Clamp01(visibility);
        float trackingFactor = isTracking ? 1f : 0.35f;

        float rawScore = projectionQuality * 0.4f + edgeQuality * 0.35f + visibilityQuality * 0.25f;
        return Mathf.Clamp01(rawScore * trackingFactor);
    }

    static string DescribeProjection(float degrees)
    {
        if (degrees <= 0f) return "NO DATA";
        if (degrees < 0.5f) return "GOOD";
        if (degrees < 2f) return "OK";
        return "LOW";
    }

    static string DescribeHighMetric(float value, float good, float ok)
    {
        if (value >= good) return "GOOD";
        if (value >= ok) return "OK";
        return "LOW";
    }

    static string DescribeScore(float value)
    {
        if (value >= 0.75f) return "STABLE";
        if (value >= 0.45f) return "RECOVERING";
        return "SEARCHING";
    }
}
