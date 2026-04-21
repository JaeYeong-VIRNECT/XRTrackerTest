using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using IV.FormulaTracker;

/// <summary>
/// Loads all model prefabs from Resources folder and allows sequential switching.
/// Each model is instantiated with TrackedBody + edge outline + touch manipulation.
/// Attach this to the same GameObject as XRTrackerManager (e.g. AR_Tracker).
/// </summary>
public class ModelSelector : MonoBehaviour
{

    [Header("Tracking")]
    [Tooltip("Tracking method for loaded models.")]
    [SerializeField] private TrackingMethod _trackingMethod = TrackingMethod.Edge;

    [Tooltip("Mark models as stationary (enables AR pose fusion).")]
    [SerializeField] private bool _isStationary = true;

    [Tooltip("Pose smoothing time in seconds. 0 = instant.")]
    [SerializeField] private float _smoothTime = 0.35f;

    [Tooltip("Rotation stability (Tikhonov). Higher = smoother.")]
    [SerializeField] private float _rotationStability = 15000f;

    [Tooltip("Position stability (Tikhonov). Higher = smoother.")]
    [SerializeField] private float _positionStability = 60000f;

    [Header("Model Placement")]
    [Tooltip("Target size (longest axis in meters) for models placed in front of camera.")]
    [SerializeField] private float _targetSize = 0.3f;

    [Tooltip("Distance from camera to place the model center.")]
    [SerializeField] private float _placementDistance = 0.6f;

    [Header("Edge Outline")]
    [Tooltip("Crease angle threshold for edge detection.")]
    [SerializeField] private float _creaseAngle = 60f;

    [Tooltip("Show internal edges (creases) in addition to silhouette.")]
    [SerializeField] private bool _showInternalEdges = true;

    [Tooltip("Hide original mesh renderers, show only edge outlines.")]
    [SerializeField] private bool _hideSourceMesh = true;

    [Tooltip("Edge line width in pixels.")]
    [SerializeField] private float _edgeWidth = 2f;

    [Tooltip("Edge outline color.")]
    [SerializeField] private Color _edgeColor = new Color(0f, 1f, 1f, 0.9f);

    private List<string> _modelNames = new List<string>();
    private int _currentIndex = -1;
    private GameObject _currentModelInstance;

    // Persist user-adjusted thresholds across model reloads
    private float _qualityToStart = 0.8f;
    private float _qualityToStop = 0.5f;

    public float QualityToStart
    {
        get => _qualityToStart;
        set => _qualityToStart = Mathf.Clamp01(value);
    }
    public float QualityToStop
    {
        get => _qualityToStop;
        set => _qualityToStop = Mathf.Clamp01(value);
    }

    public IReadOnlyList<string> ModelNames => _modelNames;
    public int CurrentIndex => _currentIndex;
    public string CurrentModelName => _currentIndex >= 0 && _currentIndex < _modelNames.Count
        ? _modelNames[_currentIndex]
        : null;
    public GameObject CurrentModelInstance => _currentModelInstance;

    public event System.Action<string, int> OnModelChanged;

    // Debug overlay
    private string _debugText = "Waiting...";

    private void Start()
    {
        LoadModelList();

        if (_modelNames.Count > 0)
        {
            Debug.Log($"[ModelSelector] Loaded {_modelNames.Count} models. Selecting first model.");
            StartCoroutine(LoadFirstModelDeferred());
        }
        else
        {
            Debug.LogWarning("[ModelSelector] No models found in Resources folder.");
        }
    }

    private IEnumerator LoadFirstModelDeferred()
    {
        // Wait until XRTrackerManager is fully initialized (FT_InitInjected complete)
        float timeout = Time.time + 10f;
        while (Time.time < timeout)
        {
            var mgr = XRTrackerManager.Instance;
            if (mgr != null && mgr.IsInitialized)
                break;
            yield return null;
        }

        // Also wait for camera to be ready
        Camera cam = null;
        timeout = Time.time + 3f;
        while (Time.time < timeout)
        {
            cam = Camera.main;
            if (cam == null)
            {
                var mgr = XRTrackerManager.Instance;
                if (mgr != null && mgr.MainCamera != null)
                    cam = mgr.MainCamera;
            }
            if (cam != null && cam.fieldOfView > 1f)
                break;
            yield return null;
        }

        Debug.Log($"[ModelSelector] Ready — tracker initialized, camera fov={cam?.fieldOfView:F1}");
        LoadModel(0);
    }

    private void LoadModelList()
    {
        _modelNames.Clear();

        var allObjects = Resources.LoadAll<GameObject>("");
        foreach (var obj in allObjects)
        {
            var filters = obj.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.sharedMesh != null).ToArray();
            if (filters.Length > 0)
                _modelNames.Add(obj.name);
        }

        Resources.UnloadUnusedAssets();
        Debug.Log($"[ModelSelector] Found {_modelNames.Count} models in Resources: {string.Join(", ", _modelNames)}");
    }

    public void NextModel()
    {
        if (_modelNames.Count == 0) return;
        SwitchModel((_currentIndex + 1) % _modelNames.Count);
    }

    public void PreviousModel()
    {
        if (_modelNames.Count == 0) return;
        SwitchModel((_currentIndex - 1 + _modelNames.Count) % _modelNames.Count);
    }

    public void SelectModel(int index)
    {
        if (index < 0 || index >= _modelNames.Count) return;
        if (index == _currentIndex && _currentModelInstance != null) return;
        SwitchModel(index);
    }

    /// <summary>
    /// Force reload the current model (same index). Used for full tracking reset.
    /// </summary>
    public void ReloadCurrentModel()
    {
        if (_currentIndex >= 0 && _currentIndex < _modelNames.Count)
        {
            int idx = _currentIndex;
            _currentIndex = -1; // bypass same-index guard in SwitchModel
            SwitchModel(idx);
        }
    }

    private bool _switching;

    private void SwitchModel(int index)
    {
        if (_switching) return;
        StopAllCoroutines();
        StartCoroutine(SwitchModelCoroutine(index));
    }

    /// <summary>
    /// Destroy old model, wait for native unregister, destroy anchor, create new model.
    /// No scene reload — avoids FT_Shutdown/FT_InitInjected race crash.
    /// </summary>
    private IEnumerator SwitchModelCoroutine(int index)
    {
        _switching = true;

        // 1. Destroy old model — triggers TrackedBody.OnDisable → async UnregisterBody
        if (_currentModelInstance != null)
        {
            Destroy(_currentModelInstance);
            _currentModelInstance = null;
        }

        // 2. Wait for async UnregisterBody to complete (needs ~2 frames)
        yield return null;
        yield return null;

        // 3. Destroy AR anchor (body list is empty now, so ResetAll only destroys anchor)
        if (TrackedBodyManager.Instance != null)
        {
            TrackedBodyManager.Instance.ResetAll();
            Debug.Log("[ModelSelector] Anchor cleared");
        }

        // 4. Create new model
        LoadModel(index);
        _switching = false;
    }

    /// <summary>
    /// Instantiate and setup a single model (called once on scene Start).
    /// </summary>
    private void LoadModel(int index)
    {
        _currentIndex = index;
        string modelName = _modelNames[_currentIndex];

        var prefab = Resources.Load<GameObject>(modelName);
        if (prefab == null)
        {
            Debug.LogError($"[ModelSelector] Failed to load model: {modelName}");
            return;
        }

        _currentModelInstance = Instantiate(prefab);
        _currentModelInstance.name = modelName;
        _currentModelInstance.transform.position = Vector3.zero;
        _currentModelInstance.transform.rotation = Quaternion.identity;

        // Center pivot on bounds center — shift children so bounds center aligns with transform origin
        CenterPivotOnBounds(_currentModelInstance);

        SetupModel(_currentModelInstance);

        Debug.Log($"[ModelSelector] Loaded model [{_currentIndex + 1}/{_modelNames.Count}]: {modelName}");
        OnModelChanged?.Invoke(modelName, _currentIndex);
    }

    private void SetupModel(GameObject modelRoot)
    {
        var filters = modelRoot.GetComponentsInChildren<MeshFilter>(true)
            .Where(f => f.sharedMesh != null).ToList();

        if (filters.Count == 0)
        {
            Debug.LogError($"[ModelSelector] Model '{modelRoot.name}' has no MeshFilters with valid meshes.");
            return;
        }

        // 1. Place model in front of camera
        PlaceInFrontOfCamera(modelRoot);

        // 2. Create viewpoint for tracking initial pose
        // Viewpoint = where the camera is relative to the model during detection.
        // Must match actual placement distance, NOT model size.
        var viewpoint = new GameObject("Viewpoint");
        viewpoint.transform.SetParent(modelRoot.transform, false);
        viewpoint.transform.localPosition = new Vector3(0, 0, -_placementDistance);
        viewpoint.transform.localRotation = Quaternion.identity;

        // 3. Add TrackedBody (disabled first to prevent premature RegisterBody)
        modelRoot.SetActive(false);
        var body = modelRoot.AddComponent<TrackedBody>();
        body.BodyId = modelRoot.name;
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
        modelRoot.SetActive(true);

        body.SetInitialPose(viewpoint.transform);

        Debug.Log($"[ModelSelector] Registered '{modelRoot.name}' with lossyScale={modelRoot.transform.lossyScale.x:F6} (geometry_unit_in_meter)");

        // 4. Add TrackedBodyOutline (edge-only rendering)
        var outline = modelRoot.AddComponent<TrackedBodyOutline>();
        SetFieldSafe(outline, "_creaseAngle", _creaseAngle);
        SetFieldSafe(outline, "_showInternalEdges", _showInternalEdges);
        SetFieldSafe(outline, "_edgeWidth", _edgeWidth);
        SetFieldSafe(outline, "_edgeColor", _edgeColor);
        var setDirtyMethod = outline.GetType().GetMethod("SetDirty",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        if (setDirtyMethod != null) setDirtyMethod.Invoke(outline, null);

        if (_hideSourceMesh)
            StartCoroutine(DeferHideSourceMesh(outline));

        // 5. Add TouchManipulator for user interaction
        modelRoot.AddComponent<TouchManipulator>();

        // 6. Add TrackingStatsHUD
        modelRoot.AddComponent<TrackingStatsHUD>();
    }

    /// <summary>
    /// Shift all children so that the bounds center aligns with the root transform origin.
    /// This makes the model rotate/place around its visual center without changing geometry.
    /// </summary>
    private void CenterPivotOnBounds(GameObject modelRoot)
    {
        // Use MeshFilter bounds (reliable before first render, unlike Renderer.bounds)
        var filters = modelRoot.GetComponentsInChildren<MeshFilter>(true);
        if (filters.Length == 0) return;

        // Compute world-space bounds from mesh data
        bool first = true;
        Bounds bounds = new Bounds();
        foreach (var f in filters)
        {
            if (f.sharedMesh == null) continue;
            var mb = f.sharedMesh.bounds;
            // Transform all 8 corners to world space
            Vector3 min = mb.min, max = mb.max;
            Vector3[] corners = {
                new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z),
            };
            foreach (var c in corners)
            {
                Vector3 world = f.transform.TransformPoint(c);
                if (first) { bounds = new Bounds(world, Vector3.zero); first = false; }
                else bounds.Encapsulate(world);
            }
        }

        Vector3 offset = modelRoot.transform.position - bounds.center;
        foreach (Transform child in modelRoot.transform)
            child.position += offset;

        Debug.Log($"[ModelSelector] CenterPivot: bounds center was {bounds.center}, offset={offset}, size={bounds.size}");
    }

    private void ScaleModelToTargetSize(GameObject modelRoot)
    {
        var bounds = ComputeBounds(modelRoot);
        float maxExtent = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (maxExtent <= 0f) return;

        float scaleFactor = _targetSize / maxExtent;
        modelRoot.transform.localScale = Vector3.one * scaleFactor;

        Debug.Log($"[ModelSelector] Scaled '{modelRoot.name}' by {scaleFactor:F6} " +
                  $"(original extent: {maxExtent:F2}m → target: {_targetSize:F2}m, " +
                  $"geometry_unit_in_meter will be {scaleFactor:F6})");
    }

    private void PlaceInFrontOfCamera(GameObject modelRoot)
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var mgr = XRTrackerManager.Instance;
            if (mgr != null && mgr.MainCamera != null)
                cam = mgr.MainCamera;
        }

        // Always place at fixed distance — user adjusts with pinch
        float distance = _placementDistance;

        Vector3 targetPos;
        if (cam != null)
            targetPos = cam.transform.position + cam.transform.forward * distance;
        else
            targetPos = new Vector3(0, 0, distance);

        // Use root position directly (CenterPivotOnBounds already aligned it with bounds center)
        modelRoot.transform.position = targetPos;

        // Ensure camera can see distant/large models
        if (cam != null && cam.farClipPlane < 1000f)
            cam.farClipPlane = 1000f;

        Debug.Log($"[ModelSelector] Placed '{modelRoot.name}' at {targetPos}, dist={distance:F2}m");
    }

    /// <summary>
    /// Wait for EdgeOutlineRenderer to actually build its edge mesh with real geometry,
    /// then hide source meshes. Checks vertexCount > 0 to confirm edges are truly built.
    /// If edges never build (timeout), keeps source meshes visible as fallback.
    /// </summary>
    private IEnumerator DeferHideSourceMesh(TrackedBodyOutline outline)
    {
        if (outline == null) yield break;

        // Poll until edge meshes have actual vertices (up to 8 seconds timeout)
        float timeout = Time.time + 8f;
        bool edgesReady = false;
        while (outline != null && Time.time < timeout)
        {
            var creaseMesh = GetPropertySafe<Mesh>(outline, "CreaseMesh");
            var edgeMesh = GetPropertySafe<Mesh>(outline, "EdgeMesh");

            int creaseVerts = creaseMesh != null ? creaseMesh.vertexCount : 0;
            int edgeVerts = edgeMesh != null ? edgeMesh.vertexCount : 0;

            if (creaseVerts > 0 || edgeVerts > 0)
            {
                Debug.Log($"[ModelSelector] Edge mesh ready: crease={creaseVerts}v, edge={edgeVerts}v");
                edgesReady = true;
                break;
            }
            yield return null;
        }

        if (outline == null) yield break;

        if (!edgesReady)
        {
            Debug.LogWarning("[ModelSelector] Edge mesh not built after timeout — keeping source meshes visible");
            yield break;
        }

        // Use property setter via reflection to trigger ApplyHideSourceMesh()
        var prop = outline.GetType().GetProperty("HideSourceMesh",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public);
        if (prop != null)
        {
            prop.SetValue(outline, true);
            Debug.Log("[ModelSelector] Deferred HideSourceMesh applied (edge mesh ready)");
        }
        else
        {
            // Fallback: set field and manually hide renderers
            SetFieldSafe(outline, "_hideSourceMesh", true);
            var go = outline.gameObject;
            if (go != null)
            {
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                    r.forceRenderingOff = true;
            }
            Debug.Log("[ModelSelector] Deferred HideSourceMesh applied via fallback");
        }
    }

    [UnityEngine.Scripting.Preserve]
    private static T GetPropertySafe<T>(object target, string propName) where T : class
    {
        var type = target.GetType();
        while (type != null)
        {
            var prop = type.GetProperty(propName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public);
            if (prop != null)
                return prop.GetValue(target) as T;
            type = type.BaseType;
        }
        return null;
    }

    // ── Mobile-optimized Runtime UI ──

    [UnityEngine.Scripting.Preserve]
    private static void SetFieldSafe(object target, string fieldName, object value)
    {
        var type = target.GetType();
        while (type != null)
        {
            var field = type.GetField(fieldName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }
            type = type.BaseType;
        }
        Debug.LogWarning($"[ModelSelector] Field '{fieldName}' not found on {target.GetType().Name}");
    }

    private static Bounds ComputeBounds(GameObject root)
    {
        // Use MeshFilter.sharedMesh.bounds — reliable before first render (unlike Renderer.bounds)
        var filters = root.GetComponentsInChildren<MeshFilter>(true);
        if (filters.Length == 0)
            return new Bounds(root.transform.position, Vector3.one * 0.1f);

        bool first = true;
        Bounds bounds = new Bounds();
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
                Vector3 world = f.transform.TransformPoint(c);
                if (first) { bounds = new Bounds(world, Vector3.zero); first = false; }
                else bounds.Encapsulate(world);
            }
        }

        return bounds;
    }

    // ── Mobile-optimized Runtime UI ──

    private GUIStyle _buttonStyle;
    private GUIStyle _labelStyle;
    private bool _stylesInitialized;
    private int _lastScreenHeight;

    // Swipe detection for model switching
    private Vector2 _swipeStart;
    private bool _swipeTracking;
    private const float SwipeMinDistance = 80f;
    private const float SwipeMaxVertical = 120f;

    private void InitStyles()
    {
        if (_stylesInitialized && _lastScreenHeight == Screen.height) return;

        int fontSize = Mathf.Max(Screen.height / 25, 18);

        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = fontSize,
            fontStyle = FontStyle.Bold
        };

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.Max(Screen.height / 30, 16),
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        _lastScreenHeight = Screen.height;
        _stylesInitialized = true;
    }

    private void Update()
    {
        DetectSwipe();

        // Debug overlay
        if (_currentModelInstance != null)
        {
            var cam = Camera.main;
            var renderers = _currentModelInstance.GetComponentsInChildren<Renderer>(true);
            int visible = 0, hidden = 0;
            foreach (var r in renderers)
            {
                if (r.enabled && !r.forceRenderingOff) visible++;
                else hidden++;
            }
            var pos = _currentModelInstance.transform.position;
            float dist = cam != null ? Vector3.Distance(cam.transform.position, pos) : -1f;
            _debugText = $"Model: {_currentModelInstance.name}\n" +
                         $"Pos: {pos}\n" +
                         $"Scale: {_currentModelInstance.transform.localScale}\n" +
                         $"Dist to cam: {dist:F2}m\n" +
                         $"Renderers: {visible} vis, {hidden} hid\n" +
                         $"farClip: {cam?.farClipPlane:F1} fov: {cam?.fieldOfView:F1}";
        }
    }

    private void DetectSwipe()
    {
        if (Input.touchCount != 3) { _swipeTracking = false; return; }

        Touch t = Input.GetTouch(0);
        if (t.phase == TouchPhase.Began)
        {
            _swipeStart = t.position;
            _swipeTracking = true;
        }
        else if (_swipeTracking && t.phase == TouchPhase.Ended)
        {
            Vector2 delta = t.position - _swipeStart;
            if (Mathf.Abs(delta.x) > SwipeMinDistance && Mathf.Abs(delta.y) < SwipeMaxVertical)
            {
                if (delta.x > 0) PreviousModel();
                else NextModel();
            }
            _swipeTracking = false;
        }
    }

    private void OnGUI()
    {
        // Debug overlay
        GUI.skin.label.fontSize = 24;
        GUI.color = Color.yellow;
        GUI.Label(new Rect(10, 200, Screen.width - 20, 350), _debugText);
        GUI.color = Color.white;

        if (_modelNames.Count == 0) return;

        InitStyles();

        // Safe area for notch/punch-hole displays
        Rect safe = Screen.safeArea;
        float w = safe.width;
        float h = safe.height;
        float safeX = safe.x;
        float safeY = Screen.height - safe.yMax; // GUI Y is top-down

        float btnW = Mathf.Max(w * 0.18f, 100f);
        float btnH = Mathf.Max(h * 0.08f, 50f);
        float margin = w * 0.02f;
        float topY = safeY + h * 0.01f;

        // Model name label (top center)
        string label = _currentIndex >= 0
            ? $"{_modelNames[_currentIndex]}  ({_currentIndex + 1}/{_modelNames.Count})"
            : "No model";
        GUI.Label(new Rect(safeX + btnW + margin * 2, topY, w - (btnW + margin * 2) * 2, btnH), label, _labelStyle);

        // Previous button (top left)
        if (GUI.Button(new Rect(safeX + margin, topY, btnW, btnH), "\u25C0 Prev", _buttonStyle))
            PreviousModel();

        // Next button (top right)
        if (GUI.Button(new Rect(safeX + w - btnW - margin, topY, btnW, btnH), "Next \u25B6", _buttonStyle))
            NextModel();
    }
}
