using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using IV.FormulaTracker;

/// <summary>
/// Loads a single large model from Resources, auto-fits it to the camera view on start,
/// and registers it for XR tracking. Use for single-model registration demos like buildings.
/// Attach to the AR_Tracker GameObject.
/// </summary>
public class LargeBuildingTracker : MonoBehaviour
{
    [Header("Model")]
    [Tooltip("Resources path to the model (e.g. 'Large/Virnect_Building').")]
    [SerializeField] private string _resourcePath = "Large/Virnect_Building";

    [Header("Auto-Fit")]
    [Tooltip("Padding around model in view. 1.0 = exact fit, 1.2 = 20% margin.")]
    [SerializeField] private float _fitPadding = 1.15f;

    [Header("Model Real-World Size (guarantees accurate lossyScale)")]
    [Tooltip("Real-world LONGEST dimension of the object in meters (e.g. width of the building). " +
             "When > 0, the script auto-scales the imported model so max(bounds.x,y,z) exactly equals this value. " +
             "This forces transform.lossyScale to the correct meters-per-mesh-unit, which the SDK uses as " +
             "'geometry_unit_in_meter'. " +
             "Leave 0 ONLY if you trust the FBX import scale completely.")]
    [SerializeField] private float _realWorldLongestDimensionM = 0f;

    [Header("Tracking")]
    [SerializeField] private TrackingMethod _trackingMethod = TrackingMethod.Edge;
    [Tooltip("Object is fixed in world. Enables AR pose fusion to maintain pose when tracking quality drops.")]
    [SerializeField] private bool _isStationary = true;
    [Tooltip("Time in seconds to smooth pose corrections. 0 = instant. SDK default 0.1.")]
    [SerializeField] private float _smoothTime = 0.35f;
    [Tooltip("Tikhonov regularization for rotation. Higher = smoother, slower. SDK default 5000.")]
    [Range(1f, 50000f)]
    [SerializeField] private float _rotationStability = 15000f;
    [Tooltip("Tikhonov regularization for position. Higher = smoother, slower. SDK default 30000.")]
    [Range(1f, 100000f)]
    [SerializeField] private float _positionStability = 60000f;

    [Header("Tracking Quality Thresholds (Edge defaults: 0.65 / 0.5)")]
    [Tooltip("Minimum quality (0-1) required to START tracking. Higher = harder to lock on, more confident detection.")]
    [Range(0f, 1f)]
    [SerializeField] private float _qualityToStart = 0.65f;
    [Tooltip("Quality (0-1) below which tracking is LOST. Lower = sticks longer when partial occlusion.")]
    [Range(0f, 1f)]
    [SerializeField] private float _qualityToStop = 0.5f;

    [Tooltip("Override the SDK 'nice quality' threshold. This is what triggers AR ANCHOR placement (separate from start/stop). SDK default 0.8 — usually higher than QualityToStart, so anchor may never fire if quality stays in between.")]
    [SerializeField] private bool _useCustomNiceQuality = true;
    [Tooltip("Custom nice-quality threshold. Lower = anchor fires earlier (less stable initial anchor but more likely to be placed). Recommended: match QualityToStart.")]
    [Range(0f, 1f)]
    [SerializeField] private float _customNiceQuality = 0.65f;

    [Header("Edge Tracking — Detection")]
    [Tooltip("Search radius for edge correspondence (relative to image width). Larger = tolerates faster motion. SDK default 0.03125.")]
    [Range(0.005f, 0.2f)]
    [SerializeField] private float _edgeSearchRadius = 0.03125f;
    [Tooltip("Minimum gradient (contrast) for edge matching. Higher = only strong edges. SDK default 15.")]
    [Range(1f, 765f)]
    [SerializeField] private float _edgeMinGradient = 15f;
    [Tooltip("Pixel spacing between edge sample points. Lower = denser/more accurate/slower. SDK default 6.")]
    [Range(2f, 20f)]
    [SerializeField] private float _edgeSampleStep = 6f;

    [Header("Edge Tracking — Keyframe (stationary jitter reduction)")]
    [Tooltip("Reuse edge sites across frames instead of re-rendering. Reduces jitter on stationary objects.")]
    [SerializeField] private bool _edgeUseKeyframe = true;
    [Tooltip("Rotation threshold (deg) to refresh keyframe. SDK default 3.")]
    [Range(0.1f, 10f)]
    [SerializeField] private float _edgeKeyframeRotationDeg = 3f;
    [Tooltip("Translation threshold (m) to refresh keyframe. SDK default 0.03.")]
    [Range(0.001f, 0.1f)]
    [SerializeField] private float _edgeKeyframeTranslation = 0.03f;

    [Header("Edge Tracking — Crease Edges (sharp surface seams)")]
    [Tooltip("Detect edges at sharp surface creases (window frames, building corners, holes). Recommended for buildings.")]
    [SerializeField] private bool _edgeEnableCreaseEdges = true;
    [Tooltip("Angle (deg) above which surface normal differences create crease edge features. SDK default 60.")]
    [Range(5f, 90f)]
    [SerializeField] private float _edgeCreaseAngle = 60f;

    [Header("Optional Modalities")]
    [Tooltip("Enable feature-based (ORB) texture tracking on top of edge tracking. Adds robustness on textured surfaces.")]
    [SerializeField] private bool _enableTextureTracking = false;

    [Header("Edge Outline (visual only — not tracking algo)")]
    [SerializeField] private float _outlineCreaseAngle = 60f;
    [SerializeField] private bool _showInternalEdges = true;
    [SerializeField] private bool _hideSourceMesh = true;
    [SerializeField] private float _edgeWidth = 2f;
    [SerializeField] private Color _edgeColor = new Color(0f, 1f, 1f, 0.9f);

    [Header("Debug")]
    [Tooltip("Show on-screen debug overlay (model state, position, distance).")]
    [SerializeField] private bool _showDebugOverlay = true;

    private GameObject _instance;
    private string _status = "Waiting for tracker...";
    private float _placedDistance;
    private Vector2 _panelScroll;
    /// <summary>Raw mesh longest dimension when localScale=1. Captured once at load. Constant after.</summary>
    private float _meshUnitMaxDim;
    /// <summary>Text-field buffer for direct dimension entry.</summary>
    private string _dimInputText;

    private void Start()
    {
        StartCoroutine(LoadDeferred());
    }

    private IEnumerator LoadDeferred()
    {
        _status = "Waiting for XRTrackerManager...";
        float timeout = Time.time + 10f;
        bool trackerReady = false;
        while (Time.time < timeout)
        {
            var mgr = XRTrackerManager.Instance;
            if (mgr != null && mgr.IsInitialized) { trackerReady = true; break; }
            yield return null;
        }
        Debug.Log($"[LargeBuildingTracker] Tracker init: {(trackerReady ? "ready" : "timeout — proceeding anyway (PC?)")}");

        _status = "Waiting for camera...";
        Camera cam = null;
        timeout = Time.time + 3f;
        while (Time.time < timeout)
        {
            cam = ResolveCamera();
            if (cam != null && cam.fieldOfView > 1f) break;
            yield return null;
        }

        if (cam == null)
        {
            _status = "ERROR: No camera available";
            Debug.LogError("[LargeBuildingTracker] No camera available — aborting load.");
            yield break;
        }

        Debug.Log($"[LargeBuildingTracker] Camera ready: pos={cam.transform.position} fov={cam.fieldOfView:F1} aspect={cam.aspect:F2}");
        LoadAndFit(cam);
    }

    private void LoadAndFit(Camera cam)
    {
        _status = $"Loading: {_resourcePath}";
        var prefab = Resources.Load<GameObject>(_resourcePath);
        if (prefab == null)
        {
            _status = $"ERROR: Resources.Load failed for '{_resourcePath}'";
            Debug.LogError($"[LargeBuildingTracker] Resources.Load failed: {_resourcePath}");
            return;
        }

        _instance = Instantiate(prefab);
        _instance.name = prefab.name;
        _instance.transform.position = Vector3.zero;
        _instance.transform.rotation = Quaternion.identity;

        var filters = _instance.GetComponentsInChildren<MeshFilter>(true)
            .Where(f => f.sharedMesh != null).ToList();
        if (filters.Count == 0)
        {
            _status = $"ERROR: '{_instance.name}' has no MeshFilters";
            Debug.LogError($"[LargeBuildingTracker] '{_instance.name}' has no MeshFilters.");
            return;
        }

        // ── Capture raw mesh dimensions at unit scale (constant, used for any future re-scale) ──
        _instance.transform.localScale = Vector3.one;
        Bounds boundsAtUnit = ComputeBounds(_instance);
        _meshUnitMaxDim = Mathf.Max(boundsAtUnit.size.x, boundsAtUnit.size.y, boundsAtUnit.size.z);

        // ── GUARANTEE accurate lossyScale (geometry_unit_in_meter) ──
        // SDK reads transform.lossyScale.x as "1 mesh unit = N meters".
        // If user provides _realWorldLongestDimensionM, scale model so longest axis = that value.
        ApplyRealWorldDimensionScale();

        Bounds rawBounds = ComputeBounds(_instance);
        Debug.Log($"[LargeBuildingTracker] LOADED '{_instance.name}' " +
                  $"meshes={filters.Count}, mesh_unit_max={_meshUnitMaxDim:F2}, " +
                  $"world_bounds.size={rawBounds.size} m, " +
                  $"lossyScale={_instance.transform.lossyScale.x:F6} (= geometry_unit_in_meter sent to SDK)");
        Debug.Log($"[LargeBuildingTracker] Building dimensions: W={rawBounds.size.x:F1}m " +
                  $"H={rawBounds.size.y:F1}m D={rawBounds.size.z:F1}m");

        CenterPivotOnBounds(_instance);

        var bounds = ComputeBounds(_instance);
        float distance = ComputeFitDistance(bounds, cam);
        _placedDistance = distance;

        _instance.transform.position = cam.transform.position + cam.transform.forward * distance;
        Debug.Log($"[LargeBuildingTracker] Bounds size={bounds.size} → fit distance={distance:F2}m, placed at {_instance.transform.position}");

        float requiredFar = distance + bounds.size.magnitude;
        if (cam.farClipPlane < requiredFar)
            cam.farClipPlane = requiredFar * 1.5f;

        var viewpoint = new GameObject("Viewpoint");
        viewpoint.transform.SetParent(_instance.transform, false);
        viewpoint.transform.localPosition = new Vector3(0, 0, -distance);
        viewpoint.transform.localRotation = Quaternion.identity;

        _instance.SetActive(false);
        var body = _instance.AddComponent<TrackedBody>();
        body.BodyId = _instance.name;
        body.MeshFilters = filters;
        body.TrackingMethod = _trackingMethod;
        body.InitialPoseSource = InitialPoseSource.Viewpoint;
        body.InitialViewpoint = viewpoint.transform;
        body.IsStationary = _isStationary;
        body.SmoothTime = _smoothTime;
        body.RotationStability = _rotationStability;
        body.PositionStability = _positionStability;
        body.UseCustomStartThreshold = true;
        body.CustomQualityToStart = _qualityToStart;
        body.UseCustomStopThreshold = true;
        body.CustomQualityToStop = _qualityToStop;
        body.UseCustomNiceQuality = _useCustomNiceQuality;
        body.CustomNiceQualityThreshold = _customNiceQuality;

        // Edge tracking algo settings (separate from outline visual settings)
        var edgeSettings = body.EdgeTracking;
        edgeSettings.SearchRadius = _edgeSearchRadius;
        edgeSettings.MinGradient = _edgeMinGradient;
        edgeSettings.SampleStep = _edgeSampleStep;
        edgeSettings.UseKeyframe = _edgeUseKeyframe;
        edgeSettings.KeyframeRotationDeg = _edgeKeyframeRotationDeg;
        edgeSettings.KeyframeTranslation = _edgeKeyframeTranslation;
        edgeSettings.EnableCreaseEdges = _edgeEnableCreaseEdges;
        edgeSettings.CreaseEdgeAngle = _edgeCreaseAngle;

        body.EnableTextureTracking = _enableTextureTracking;
        _instance.SetActive(true);

        body.SetInitialPose(viewpoint.transform);

        // Push runtime updates to native side
        body.UpdateStabilityParameters();
        body.UpdateEdgeTrackingParameters();

        var outline = _instance.AddComponent<TrackedBodyOutline>();
        SetFieldSafe(outline, "_creaseAngle", _outlineCreaseAngle);
        SetFieldSafe(outline, "_showInternalEdges", _showInternalEdges);
        SetFieldSafe(outline, "_edgeWidth", _edgeWidth);
        SetFieldSafe(outline, "_edgeColor", _edgeColor);
        var setDirty = outline.GetType().GetMethod("SetDirty",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        setDirty?.Invoke(outline, null);

        if (_hideSourceMesh)
            StartCoroutine(DeferHideSourceMesh(outline));

        _instance.AddComponent<TouchManipulator>();
        _instance.AddComponent<TrackingStatsHUD>();

        _status = $"OK: {_instance.name} @ {distance:F1}m";
        Debug.Log($"[LargeBuildingTracker] Setup complete — '{_instance.name}' " +
                  $"at distance {distance:F2}m, bounds={bounds.size}, padding={_fitPadding}");
    }

    private void LateUpdate()
    {
        // Re-publish UI rects every frame so TouchManipulator (which runs after, order 20000)
        // sees the current layout and skips touches over our panels.
        TouchManipulator.BlockedRects.Clear();
        if (_showDebugOverlay)
            TouchManipulator.BlockedRects.Add(GetDebugRect());
        TouchManipulator.BlockedRects.Add(GetControlPanelRect());
    }

    // TrackingStatsHUD panel occupies top-left ~24..344px. Place debug overlay below it.
    private static Rect GetDebugRect()
    {
        float w = Mathf.Min(Screen.width - 20, 820f);
        float h = Mathf.Min(Screen.height - 380f, 480f);
        return new Rect(10, 360, w, h);
    }

    private static Rect GetControlPanelRect()
    {
        float w = Mathf.Clamp(Screen.width * 0.30f, 340f, 520f);
        float h = Screen.height * 0.96f;
        float x = Screen.width - w - 10;
        float y = (Screen.height - h) * 0.5f;
        return new Rect(x, y, w, h);
    }

    private void OnGUI()
    {
        DrawDebugOverlay();
        DrawControlPanel();
    }

    private void DrawDebugOverlay()
    {
        if (!_showDebugOverlay) return;

        Rect rect = GetDebugRect();

        // Semi-transparent background so text is readable on top of camera feed
        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = prev;

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.Max(Screen.height / 50, 13),
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.yellow },
            padding = new RectOffset(10, 10, 8, 8),
            wordWrap = true,
            clipping = TextClipping.Clip
        };

        string text = $"[DEBUG] {_status}";
        if (_instance != null)
        {
            var cam = ResolveCamera();
            float dist = cam != null ? Vector3.Distance(cam.transform.position, _instance.transform.position) : -1f;
            var renderers = _instance.GetComponentsInChildren<Renderer>(true);
            int vis = 0, hid = 0;
            foreach (var r in renderers)
            {
                if (r.enabled && !r.forceRenderingOff) vis++;
                else hid++;
            }
            var body = _instance.GetComponent<TrackedBody>();
            string trackingState = body == null ? "no body"
                : $"{(body.IsTracking ? "TRACKING" : (body.IsRegistered ? "registered" : "not registered"))}";

            // ── Model size check (CRITICAL for tracking lock) ──
            var bounds = ComputeBounds(_instance);
            Vector3 lossy = _instance.transform.lossyScale;
            text += $"\n=== MODEL SIZE (real-world m) ===" +
                    $"\nW={bounds.size.x:F2}  H={bounds.size.y:F2}  D={bounds.size.z:F2}" +
                    $"\nLossyScale: {lossy.x:F4} (= geometry_unit_in_meter)" +
                    $"\nFitDist: {_placedDistance:F2}m  CamDist: {dist:F2}m";
            text += $"\n=== STATE ===" +
                    $"\nPos: {_instance.transform.position}" +
                    $"\nRenderers: {vis} vis / {hid} hid" +
                    $"\nBody: {trackingState}";
            if (cam != null)
                text += $"\nCam fov={cam.fieldOfView:F1} far={cam.farClipPlane:F1}";
            var mgr = XRTrackerManager.Instance;
            if (mgr != null)
                text += $"\nMgr: init={mgr.IsInitialized} ready={mgr.IsTrackingReady} inProg={mgr.IsTrackingInProgress}";
        }
        GUI.Label(rect, text, style);
    }

    private GUIStyle _btnStyle, _labelStyle, _headerStyle, _bgStyle, _sliderStyle, _thumbStyle, _toggleStyle, _vScrollStyle, _vScrollThumbStyle;
    private void EnsureStyles()
    {
        int fs = Mathf.Max(Screen.height / 44, 14);
        if (_btnStyle == null || _btnStyle.fontSize != fs)
        {
            // Default-sized text/buttons/sliders/toggles — only the scrollbar is enlarged for mobile.
            _btnStyle = new GUIStyle(GUI.skin.button)
            { fontSize = fs, fontStyle = FontStyle.Bold, wordWrap = true };
            _labelStyle = new GUIStyle(GUI.skin.label)
            { fontSize = fs, normal = { textColor = Color.white }, wordWrap = true };
            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = fs + 2, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.6f, 1f, 1f, 1f) },
                wordWrap = true
            };
            _bgStyle = new GUIStyle(GUI.skin.box);
            _sliderStyle = new GUIStyle(GUI.skin.horizontalSlider);
            _thumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb);
            _toggleStyle = new GUIStyle(GUI.skin.toggle) { fontSize = fs };

            // Mobile-friendly scrollbar — KEPT large for finger touch.
            float scrollW = Mathf.Max(fs * 2.4f, 56f);
            GUI.skin.verticalScrollbar.fixedWidth = scrollW;
            GUI.skin.verticalScrollbarThumb.fixedWidth = scrollW;
            GUI.skin.verticalScrollbarUpButton.fixedWidth = scrollW;
            GUI.skin.verticalScrollbarUpButton.fixedHeight = 0f;
            GUI.skin.verticalScrollbarDownButton.fixedWidth = scrollW;
            GUI.skin.verticalScrollbarDownButton.fixedHeight = 0f;
            _vScrollStyle = GUI.skin.verticalScrollbar;
            _vScrollThumbStyle = GUI.skin.verticalScrollbarThumb;
        }
    }

    private void DrawControlPanel()
    {
        EnsureStyles();
        var mgr = XRTrackerManager.Instance;
        Rect panel = GetControlPanelRect();

        // Background plate
        var bgColor = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = bgColor;

        GUILayout.BeginArea(new Rect(panel.x + 6, panel.y + 6, panel.width - 12, panel.height - 12));
        // alwaysShowVertical=true so the user can always see the scrollbar handle
        _panelScroll = GUILayout.BeginScrollView(_panelScroll, false, true,
            GUIStyle.none, _vScrollStyle, GUI.skin.box);

        // Reserve horizontal space for the (enlarged) scrollbar so content never overlaps it.
        // Unity's BeginScrollView reserves only ~16px in layout regardless of our drawn scrollbar
        // width, so we explicitly clamp content to (panel - drawn scrollbar - safety buffer).
        float scrollW = _vScrollStyle != null ? _vScrollStyle.fixedWidth : 56f;
        float contentWidth = panel.width - 12 - scrollW - 24;   // +extra buffer to never overlap
        GUILayout.BeginVertical(GUILayout.MaxWidth(contentWidth), GUILayout.Width(contentWidth));

        GUI.changed = false;

        // ── Manager API actions ──
        GUILayout.Label("Tracker", _headerStyle);
        if (GUILayout.Button("Restart", _btnStyle))
        { if (mgr != null) mgr.StartDetection(); _status = "Restart Detection requested"; }

        bool inProgress = mgr != null && mgr.IsTrackingInProgress;
        if (GUILayout.Button(inProgress ? "Pause" : "Resume", _btnStyle))
        {
            if (mgr != null) { if (inProgress) mgr.PauseTracking(); else mgr.ResumeTracking(); }
        }

        bool fusionOn = mgr != null && mgr.UseARPoseFusion;
        bool newFusion = GUILayout.Toggle(fusionOn, $"  AR Fusion: {(fusionOn ? "ON" : "OFF")}", _toggleStyle);
        if (newFusion != fusionOn && mgr != null) mgr.UseARPoseFusion = newFusion;

        if (GUILayout.Button("Recenter", _btnStyle))
            RecenterToCamera();

        if (GUILayout.Button("⚡ STICK MODE", _btnStyle))
            ApplyStickModePreset();

        // Hard reset — reload scene from scratch (full tracker re-init)
        var prevBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(1f, 0.5f, 0.5f, 1f);
        if (GUILayout.Button("⟳ Restart Scene", _btnStyle))
            ReloadScene();
        GUI.backgroundColor = prevBg;

        // ── Real-world size (drives lossyScale = geometry_unit_in_meter) ──
        Section("Real Size (m)");
        float prevDim = _realWorldLongestDimensionM;

        _realWorldLongestDimensionM = Slider("Axis", _realWorldLongestDimensionM, 0f, 100f, "F2");

        // Fine ±buttons — split into two rows so labels stay readable on narrow panels
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("-1", _btnStyle))    _realWorldLongestDimensionM = Mathf.Max(0f, _realWorldLongestDimensionM - 1f);
        if (GUILayout.Button("-0.1", _btnStyle))  _realWorldLongestDimensionM = Mathf.Max(0f, _realWorldLongestDimensionM - 0.1f);
        if (GUILayout.Button("-0.01", _btnStyle)) _realWorldLongestDimensionM = Mathf.Max(0f, _realWorldLongestDimensionM - 0.01f);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+0.01", _btnStyle)) _realWorldLongestDimensionM += 0.01f;
        if (GUILayout.Button("+0.1", _btnStyle))  _realWorldLongestDimensionM += 0.1f;
        if (GUILayout.Button("+1", _btnStyle))    _realWorldLongestDimensionM += 1f;
        GUILayout.EndHorizontal();

        // Direct text input — precise typing
        GUILayout.BeginHorizontal();
        GUILayout.Label("Type:", _labelStyle, GUILayout.ExpandWidth(false));
        _dimInputText = GUILayout.TextField(_dimInputText ?? _realWorldLongestDimensionM.ToString("F2"));
        if (GUILayout.Button("Apply", _btnStyle, GUILayout.ExpandWidth(false)))
        {
            if (float.TryParse(_dimInputText, out float typed) && typed >= 0f)
                _realWorldLongestDimensionM = typed;
        }
        GUILayout.EndHorizontal();

        if (Mathf.Abs(prevDim - _realWorldLongestDimensionM) > 0.001f)
        {
            _dimInputText = _realWorldLongestDimensionM.ToString("F2");
            ApplyRealWorldDimensionScale();
            RecenterToCamera();   // re-fit + reposition + refresh viewpoint
        }
        if (_meshUnitMaxDim > 0f)
        {
            float currentLossy = _instance != null ? _instance.transform.lossyScale.x : 1f;
            GUILayout.Label($"  mesh_unit_max={_meshUnitMaxDim:F2}  lossyScale={currentLossy:F4}", _labelStyle);
        }

        // ── Quality Thresholds ──
        Section("Quality");
        _qualityToStart = Slider("Start", _qualityToStart, 0f, 1f, "F2");
        _qualityToStop  = Slider("Stop",  _qualityToStop,  0f, 1f, "F2");

        // Nice Quality — gates ANCHOR placement (separate from Start/Stop)
        _useCustomNiceQuality = GUILayout.Toggle(_useCustomNiceQuality,
            $"  Custom Nice Q ({(_useCustomNiceQuality ? "ON" : "OFF — uses SDK 0.8")})", _toggleStyle);
        if (_useCustomNiceQuality)
            _customNiceQuality = Slider("Nice Q (anchor trigger)", _customNiceQuality, 0f, 1f, "F2");
        // Quick action: snap Nice = Start so anchor fires the moment tracking locks on
        if (GUILayout.Button("⇩ Nice = Start", _btnStyle))
        {
            _useCustomNiceQuality = true;
            _customNiceQuality = _qualityToStart;
            ApplyTunableToBody();
        }

        // ── Stability / Smoothing ──
        Section("Stability");
        _smoothTime         = Slider("Smooth s", _smoothTime,        0f,    1f,      "F2");
        _rotationStability  = Slider("Rot",      _rotationStability, 1f,    50000f,  "F0");
        _positionStability  = Slider("Pos",      _positionStability, 1f,    100000f, "F0");

        // ── Edge Detection ──
        Section("Edge");
        _edgeSearchRadius = Slider("Search",   _edgeSearchRadius, 0.005f, 0.2f, "F4");
        _edgeMinGradient  = Slider("Gradient", _edgeMinGradient,  1f,     765f, "F0");
        _edgeSampleStep   = Slider("Step px",  _edgeSampleStep,   2f,     20f,  "F1");

        // ── Edge Keyframe ──
        Section("Keyframe");
        _edgeUseKeyframe          = GUILayout.Toggle(_edgeUseKeyframe, "  Use Keyframe", _toggleStyle);
        _edgeKeyframeRotationDeg  = Slider("Rot °",    _edgeKeyframeRotationDeg, 0.1f,  10f,  "F2");
        _edgeKeyframeTranslation  = Slider("Trans m",  _edgeKeyframeTranslation, 0.001f, 0.1f, "F3");

        // ── Crease Edges ──
        Section("Crease");
        _edgeEnableCreaseEdges = GUILayout.Toggle(_edgeEnableCreaseEdges, "  Crease Edges", _toggleStyle);
        _edgeCreaseAngle       = Slider("Angle °", _edgeCreaseAngle, 5f, 90f, "F0");

        // ── Modalities ──
        Section("Modes");
        _enableTextureTracking = GUILayout.Toggle(_enableTextureTracking, "  Texture (ORB)", _toggleStyle);
        _isStationary = GUILayout.Toggle(_isStationary, "  Stationary", _toggleStyle);

        // ── Visual: Edges only ──
        bool prevHide = _hideSourceMesh;
        _hideSourceMesh = GUILayout.Toggle(_hideSourceMesh, "  Hide Source", _toggleStyle);
        if (prevHide != _hideSourceMesh) ApplySourceMeshVisibility();

        if (GUI.changed) ApplyTunableToBody();

        GUILayout.EndVertical();
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void Section(string title)
    {
        GUILayout.Space(6);
        GUILayout.Label(title, _headerStyle);
    }

    private float Slider(string label, float value, float min, float max, string fmt)
    {
        GUILayout.Label($"{label}: {value.ToString(fmt)}", _labelStyle);
        return GUILayout.HorizontalSlider(value, min, max, _sliderStyle, _thumbStyle);
    }

    /// <summary>
    /// Push current serialized values to the live TrackedBody and notify the SDK.
    /// Cheap; safe to call every UI change. Guards against native calls before init.
    /// </summary>
    private void ApplyTunableToBody()
    {
        if (_instance == null) return;
        var mgr = XRTrackerManager.Instance;
        if (mgr == null || !mgr.IsInitialized) return;
        var body = _instance.GetComponent<TrackedBody>();
        if (body == null) return;

        body.IsStationary = _isStationary;
        body.SmoothTime = _smoothTime;
        body.RotationStability = _rotationStability;
        body.PositionStability = _positionStability;
        body.UseCustomStartThreshold = true;
        body.CustomQualityToStart = _qualityToStart;
        body.UseCustomStopThreshold = true;
        body.CustomQualityToStop = _qualityToStop;
        body.UseCustomNiceQuality = _useCustomNiceQuality;
        body.CustomNiceQualityThreshold = _customNiceQuality;

        var e = body.EdgeTracking;
        e.SearchRadius = _edgeSearchRadius;
        e.MinGradient = _edgeMinGradient;
        e.SampleStep = _edgeSampleStep;
        e.UseKeyframe = _edgeUseKeyframe;
        e.KeyframeRotationDeg = _edgeKeyframeRotationDeg;
        e.KeyframeTranslation = _edgeKeyframeTranslation;
        e.EnableCreaseEdges = _edgeEnableCreaseEdges;
        e.CreaseEdgeAngle = _edgeCreaseAngle;

        body.EnableTextureTracking = _enableTextureTracking;

        body.UpdateStabilityParameters();
        body.UpdateEdgeTrackingParameters();
    }

    private void ApplySourceMeshVisibility()
    {
        if (_instance == null) return;
        var outline = _instance.GetComponent<TrackedBodyOutline>();
        if (outline == null) return;
        var prop = outline.GetType().GetProperty("HideSourceMesh",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        if (prop != null) prop.SetValue(outline, _hideSourceMesh);
    }

    /// <summary>
    /// "Stick to anything" preset: maximally permissive thresholds + AR Fusion + texture tracking.
    /// Trades precision for resilience — locks on more easily and stays locked through occlusion.
    /// </summary>
    private void ApplyStickModePreset()
    {
        _qualityToStart = 0.30f;        // very easy first lock
        _qualityToStop  = 0.15f;        // hold on through bad frames
        _smoothTime     = 0.5f;         // smooth pose transitions
        _rotationStability = 25000f;    // more rigid (less drift)
        _positionStability = 80000f;
        _isStationary = true;

        // Edge detection — wider tolerance for fast camera motion + faint edges
        _edgeSearchRadius = 0.08f;
        _edgeMinGradient  = 6f;
        _edgeSampleStep   = 5f;

        // Keyframe — tighter refresh so reused edges follow movement
        _edgeUseKeyframe = true;
        _edgeKeyframeRotationDeg = 2f;
        _edgeKeyframeTranslation = 0.02f;

        // Crease — buildings have lots of corners/window frames
        _edgeEnableCreaseEdges = true;
        _edgeCreaseAngle = 45f;         // catch more subtle creases

        // Extra modality — ORB texture tracking on top of edge
        _enableTextureTracking = true;

        var mgr = XRTrackerManager.Instance;
        if (mgr != null) mgr.UseARPoseFusion = true;   // backbone for stationary objects

        ApplyTunableToBody();
        _status = "STICK MODE applied";
        Debug.Log("[LargeBuildingTracker] STICK MODE preset applied. " +
                  "qualityStart=0.30/stop=0.15, search=0.08, gradient=6, AR fusion=ON, texture=ON, crease=ON@45°.");
    }

    /// <summary>
    /// Set transform.localScale so that the model's longest world-space axis equals
    /// _realWorldLongestDimensionM. Uses cached _meshUnitMaxDim so it's stable even
    /// when called repeatedly at runtime. No-op if either value is 0.
    /// </summary>
    private void ApplyRealWorldDimensionScale()
    {
        if (_instance == null) return;
        if (_meshUnitMaxDim <= 0.0001f) return;
        if (_realWorldLongestDimensionM <= 0f) return;

        float newScale = _realWorldLongestDimensionM / _meshUnitMaxDim;
        _instance.transform.localScale = Vector3.one * newScale;
        Debug.Log($"[LargeBuildingTracker] Real-world size set to {_realWorldLongestDimensionM}m → " +
                  $"localScale={newScale:F6}, lossyScale={_instance.transform.lossyScale.x:F6}");
    }

    private void ReloadScene()
    {
        var scene = SceneManager.GetActiveScene();
        Debug.Log($"[LargeBuildingTracker] Reloading scene '{scene.name}' (full tracker re-init)");
        _status = $"Reloading {scene.name}...";
        SceneManager.LoadScene(scene.buildIndex);
    }

    private void RecenterToCamera()
    {
        if (_instance == null) return;
        var cam = ResolveCamera();
        if (cam == null) return;

        var bounds = ComputeBounds(_instance);
        float distance = ComputeFitDistance(bounds, cam);
        _placedDistance = distance;

        _instance.transform.position = cam.transform.position + cam.transform.forward * distance;

        var viewpoint = _instance.transform.Find("Viewpoint");
        if (viewpoint != null)
            viewpoint.localPosition = new Vector3(0, 0, -distance);

        var body = _instance.GetComponent<TrackedBody>();
        if (body != null && viewpoint != null)
            body.SetInitialPose(viewpoint);

        _status = $"Recentered @ {distance:F1}m";
        Debug.Log($"[LargeBuildingTracker] Recentered to camera, distance={distance:F2}m");
    }

    /// <summary>
    /// Compute distance from camera so the bounding box's front face fits the frustum.
    /// </summary>
    private float ComputeFitDistance(Bounds bounds, Camera cam)
    {
        float vfov = cam.fieldOfView * Mathf.Deg2Rad;
        float halfTanV = Mathf.Tan(vfov * 0.5f);
        float aspect = cam.aspect > 0.01f ? cam.aspect : (float)Screen.width / Screen.height;

        float distForHeight = (bounds.size.y * 0.5f) / halfTanV;
        float distForWidth = (bounds.size.x * 0.5f) / (halfTanV * aspect);
        float fitDist = Mathf.Max(distForHeight, distForWidth) * _fitPadding;

        // Push back so the front face (closest part of bbox) sits at fitDist
        return fitDist + bounds.size.z * 0.5f;
    }

    private static Camera ResolveCamera()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var mgr = XRTrackerManager.Instance;
            if (mgr != null && mgr.MainCamera != null)
                cam = mgr.MainCamera;
        }
        return cam;
    }

    private static void CenterPivotOnBounds(GameObject root)
    {
        var b = ComputeBounds(root);
        if (b.size == Vector3.zero) return;
        Vector3 offset = root.transform.position - b.center;
        foreach (Transform child in root.transform)
            child.position += offset;
    }

    private static Bounds ComputeBounds(GameObject root)
    {
        var filters = root.GetComponentsInChildren<MeshFilter>(true);
        bool first = true;
        Bounds b = new Bounds(root.transform.position, Vector3.zero);
        foreach (var f in filters)
        {
            if (f.sharedMesh == null) continue;
            var mb = f.sharedMesh.bounds;
            Vector3 min = mb.min, max = mb.max;
            Vector3[] corners = {
                new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z),
            };
            foreach (var c in corners)
            {
                Vector3 w = f.transform.TransformPoint(c);
                if (first) { b = new Bounds(w, Vector3.zero); first = false; }
                else b.Encapsulate(w);
            }
        }
        return b;
    }

    private IEnumerator DeferHideSourceMesh(TrackedBodyOutline outline)
    {
        if (outline == null) yield break;
        // Larger building meshes + slower mobile CPUs need more time. PC usually <1s.
        const float TIMEOUT_SECONDS = 20f;
        float timeout = Time.time + TIMEOUT_SECONDS;
        bool ready = false;
        int waitedFrames = 0;
        while (outline != null && Time.time < timeout)
        {
            var crease = GetPropertySafe<Mesh>(outline, "CreaseMesh");
            var edge = GetPropertySafe<Mesh>(outline, "EdgeMesh");
            int cv = crease != null ? crease.vertexCount : 0;
            int ev = edge != null ? edge.vertexCount : 0;
            if (cv > 0 || ev > 0)
            {
                Debug.Log($"[LargeBuildingTracker] Edge mesh ready after {waitedFrames} frames (crease={cv}v, edge={ev}v)");
                ready = true;
                break;
            }
            waitedFrames++;
            yield return null;
        }
        if (outline == null) yield break;
        if (!ready)
        {
            _status = "WARN: edge mesh not built (FBX isReadable=0?) — keeping source visible";
            Debug.LogWarning($"[LargeBuildingTracker] Edge mesh not built after {TIMEOUT_SECONDS}s. " +
                             "Likely causes: FBX 'Read/Write' not enabled, or mesh too large. Source mesh kept visible.");
            yield break;
        }

        var prop = outline.GetType().GetProperty("HideSourceMesh",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        if (prop != null)
            prop.SetValue(outline, _hideSourceMesh);
    }

    private static T GetPropertySafe<T>(object target, string propName) where T : class
    {
        var type = target.GetType();
        while (type != null)
        {
            var prop = type.GetProperty(propName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (prop != null) return prop.GetValue(target) as T;
            type = type.BaseType;
        }
        return null;
    }

    private static void SetFieldSafe(object target, string fieldName, object value)
    {
        var type = target.GetType();
        while (type != null)
        {
            var field = type.GetField(fieldName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            if (field != null) { field.SetValue(target, value); return; }
            type = type.BaseType;
        }
    }
}
