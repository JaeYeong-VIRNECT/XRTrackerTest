using System.Collections.Generic;
using UnityEngine;
using IV.FormulaTracker;

[DefaultExecutionOrder(20000)]
public class TouchManipulator : MonoBehaviour
{
    /// <summary>
    /// Screen-space rects (GUI coords, top-left origin) that swallow touches.
    /// UI scripts populate this each frame; manipulator skips input over any registered rect.
    /// </summary>
    public static readonly List<Rect> BlockedRects = new List<Rect>();

    public static bool IsScreenPointBlocked(Vector2 screenPos)
    {
        Vector2 guiPos = new Vector2(screenPos.x, Screen.height - screenPos.y);
        for (int i = 0; i < BlockedRects.Count; i++)
            if (BlockedRects[i].Contains(guiPos)) return true;
        return false;
    }

    [SerializeField] private Camera cam;
    [SerializeField] private float rotateSpeed = 0.3f;
    [SerializeField] private float mouseRotateSpeed = 0.3f;
    [SerializeField] private float mouseMoveSpeed = 1f;
    [SerializeField] private float scrollScaleSpeed = 0.1f;
    [SerializeField] private float minScale = 0.05f;
    [SerializeField] private float maxScale = 20f;
    [SerializeField, Tooltip("Ignore touches within this many px of top/bottom edges (watermark, HUD).")]
    private int edgeIgnorePixels = 140;
    [SerializeField, Tooltip("Ignore touches within this many px of the RIGHT edge (runtime controls panel).")]
    private int rightEdgeIgnorePixels = 420;
    [SerializeField] private float keyRotateDegPerSec = 90f;
    [SerializeField] private float keyScalePerSec = 0.8f;
    [SerializeField] private float keyMovePerSec = 0.3f;

    Vector3 _pivotLocal;
    float _pinchPrev;
    Vector2 _midPrev;
    float _anglePrev;
    bool _twoActive;
    Vector3 _mousePrev;
    TrackedBody _body;
    float _boundsSize = 1f;

    // Per-finger lock: any touch that BEGAN over UI is ignored for its entire lifetime,
    // even if the finger drags off the UI. Cleared on Ended/Canceled.
    readonly HashSet<int> _uiLockedFingers = new HashSet<int>();
    bool _mouseLockedToUi;

    void Start()
    {
        _body = GetComponent<TrackedBody>();
        RecomputePivot();
        ComputeBoundsSize();
    }

    void ComputeBoundsSize()
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) { _boundsSize = 1f; return; }
        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        _boundsSize = Mathf.Max(b.size.magnitude, 0.01f);
    }

    public void RecomputePivot()
    {
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { _pivotLocal = Vector3.zero; return; }
        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        _pivotLocal = transform.InverseTransformPoint(b.center);
    }

    Vector3 PivotWorld => transform.TransformPoint(_pivotLocal);

    bool InIgnoreEdge(Vector2 p)
    {
        if (p.y < edgeIgnorePixels || p.y > Screen.height - edgeIgnorePixels) return true;
        if (p.x > Screen.width - rightEdgeIgnorePixels) return true;
        return false;
    }

    public void SetRightEdgeIgnorePixels(int pixels)
    {
        rightEdgeIgnorePixels = Mathf.Max(0, pixels);
    }

    void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        // When TrackedBody is actively tracking, SDK controls the transform — skip manipulation
        if (_body != null && _body.IsTracking)
            return;

        bool hadInput = false;

        HandleKeys();

        if (Input.touchCount > 0) hadInput = HandleTouch();
        else hadInput = HandleMouse() || hadInput;

        // After user manipulates the model, update the detection pose so SDK
        // uses this new orientation for its next ApplyDetectorPose call
        if (hadInput && _body != null && _body.IsRegistered && !_body.IsTracking)
        {
            _body.SetInitialPose(cam.transform);
        }
    }

    void HandleKeys()
    {
        float dt = Time.unscaledDeltaTime;
        Vector3 piv = PivotWorld;

        if (Input.GetKey(KeyCode.Q))
            transform.RotateAround(piv, cam.transform.up, -keyRotateDegPerSec * dt);
        if (Input.GetKey(KeyCode.E))
            transform.RotateAround(piv, cam.transform.up, keyRotateDegPerSec * dt);
        if (Input.GetKey(KeyCode.R))
            transform.RotateAround(piv, cam.transform.right, -keyRotateDegPerSec * dt);
        if (Input.GetKey(KeyCode.F))
            transform.RotateAround(piv, cam.transform.right, keyRotateDegPerSec * dt);

        float scaleStep = 1f + keyScalePerSec * dt;
        if (Input.GetKey(KeyCode.Equals) || Input.GetKey(KeyCode.KeypadPlus))
            transform.localScale = Vector3.one * Mathf.Clamp(transform.localScale.x * scaleStep, minScale, maxScale);
        if (Input.GetKey(KeyCode.Minus) || Input.GetKey(KeyCode.KeypadMinus))
            transform.localScale = Vector3.one * Mathf.Clamp(transform.localScale.x / scaleStep, minScale, maxScale);

        Vector3 move = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) move += cam.transform.up;
        if (Input.GetKey(KeyCode.S)) move -= cam.transform.up;
        if (Input.GetKey(KeyCode.A)) move -= cam.transform.right;
        if (Input.GetKey(KeyCode.D)) move += cam.transform.right;
        if (move.sqrMagnitude > 0.001f)
            transform.position += move.normalized * keyMovePerSec * dt;
    }

    bool HandleTouch()
    {
        bool moved = false;
        int n = Input.touchCount;

        // Update per-finger UI lock state. Once a touch begins on UI, mark the finger
        // and ignore it until Ended/Canceled — prevents drag-off-UI from rotating.
        for (int i = 0; i < n; i++)
        {
            Touch t = Input.GetTouch(i);
            if (t.phase == TouchPhase.Began)
            {
                if (IsScreenPointBlocked(t.position) || InIgnoreEdge(t.position))
                    _uiLockedFingers.Add(t.fingerId);
            }
            else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
            {
                _uiLockedFingers.Remove(t.fingerId);
            }
        }

        // If ANY active finger is locked to UI, ignore all manipulation this frame
        for (int i = 0; i < n; i++)
        {
            if (_uiLockedFingers.Contains(Input.GetTouch(i).fingerId))
            { _twoActive = false; _pinchPrev = 0f; return false; }
        }

        if (n >= 2)
        {
            Touch a = Input.GetTouch(0), b = Input.GetTouch(1);
            // Block when either finger is over a UI panel
            if (IsScreenPointBlocked(a.position) || IsScreenPointBlocked(b.position))
            { _twoActive = false; _pinchPrev = 0f; return false; }

            float d = Vector2.Distance(a.position, b.position);
            Vector2 mid = (a.position + b.position) * 0.5f;
            float angle = Mathf.Atan2(b.position.y - a.position.y, b.position.x - a.position.x) * Mathf.Rad2Deg;

            if (!_twoActive || a.phase == TouchPhase.Began || b.phase == TouchPhase.Began)
            { _pinchPrev = d; _midPrev = mid; _anglePrev = angle; _twoActive = true; return false; }

            // Pinch → move closer/farther (along camera forward axis)
            float pinchDelta = d - _pinchPrev;
            float distToCam = Mathf.Max(Vector3.Distance(cam.transform.position, transform.position), 0.1f);
            float moveAmount = pinchDelta * -distToCam / Screen.height;
            transform.position += cam.transform.forward * moveAmount;

            // Two-finger drag → move (perpendicular to camera)
            Vector3 screen = cam.WorldToScreenPoint(PivotWorld);
            screen.x += (mid - _midPrev).x;
            screen.y += (mid - _midPrev).y;
            Vector3 newPivotWorld = cam.ScreenToWorldPoint(screen);
            transform.position += (newPivotWorld - PivotWorld);

            // Two-finger twist → rotate around camera forward
            float angleDelta = Mathf.DeltaAngle(_anglePrev, angle);
            if (Mathf.Abs(angleDelta) > 0.1f)
                transform.RotateAround(PivotWorld, cam.transform.forward, angleDelta);

            _pinchPrev = d; _midPrev = mid; _anglePrev = angle;
            moved = true;
        }
        else if (n == 1)
        {
            _twoActive = false; _pinchPrev = 0f;
            Touch t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Moved && !InIgnoreEdge(t.position) && !IsScreenPointBlocked(t.position))
            {
                Vector3 piv = PivotWorld;
                transform.RotateAround(piv, cam.transform.up, -t.deltaPosition.x * rotateSpeed);
                transform.RotateAround(piv, cam.transform.right, t.deltaPosition.y * rotateSpeed);
                moved = true;
            }
        }
        else { _twoActive = false; _pinchPrev = 0f; }
        return moved;
    }

    bool HandleMouse()
    {
        bool moved = false;
        Vector3 mouse = Input.mousePosition;
        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
        {
            _mousePrev = mouse;
            // Lock mouse drag to UI if pressed over UI; cleared on release
            _mouseLockedToUi = IsScreenPointBlocked(mouse) || InIgnoreEdge(mouse);
        }
        if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1) && !Input.GetMouseButton(2))
            _mouseLockedToUi = false;

        Vector3 delta = mouse - _mousePrev;

        if (_mouseLockedToUi) { _mousePrev = mouse; return false; }

        if (Input.GetMouseButton(0) && !InIgnoreEdge(mouse) && !IsScreenPointBlocked(mouse))
        {
            Vector3 piv = PivotWorld;
            transform.RotateAround(piv, cam.transform.up, -delta.x * mouseRotateSpeed);
            transform.RotateAround(piv, cam.transform.right, delta.y * mouseRotateSpeed);
            moved = delta.sqrMagnitude > 0.01f;
        }
        else if ((Input.GetMouseButton(1) || Input.GetMouseButton(2)) && !IsScreenPointBlocked(mouse))
        {
            Vector3 screen = cam.WorldToScreenPoint(PivotWorld);
            screen.x += delta.x * mouseMoveSpeed;
            screen.y += delta.y * mouseMoveSpeed;
            Vector3 newPivotWorld = cam.ScreenToWorldPoint(screen);
            transform.position += (newPivotWorld - PivotWorld);
            moved = delta.sqrMagnitude > 0.01f;
        }

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.0001f)
        {
            float s = Mathf.Clamp(transform.localScale.x * (1f + scroll * scrollScaleSpeed * 10f), minScale, maxScale);
            transform.localScale = Vector3.one * s;
            moved = true;
        }

        _mousePrev = mouse;
        return moved;
    }
}
