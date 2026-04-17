using UnityEngine;
using IV.FormulaTracker;

public class XRTrackerTest : MonoBehaviour
{
    [SerializeField] private TrackedBody trackedBody;

    private void OnEnable()
    {
        XRTrackerManager.OnTrackerInitialized += HandleTrackerInitialized;

        var manager = XRTrackerManager.Instance;
        if (manager != null)
            manager.OnCameraSelected += HandleCameraSelected;

        if (trackedBody != null)
        {
            trackedBody.OnStartTracking.AddListener(HandleStartTracking);
            trackedBody.OnStopTracking.AddListener(HandleStopTracking);
            trackedBody.OnPoseUpdated += HandlePoseUpdated;
        }
    }

    private void OnDisable()
    {
        XRTrackerManager.OnTrackerInitialized -= HandleTrackerInitialized;

        var manager = XRTrackerManager.Instance;
        if (manager != null)
            manager.OnCameraSelected -= HandleCameraSelected;

        if (trackedBody != null)
        {
            trackedBody.OnStartTracking.RemoveListener(HandleStartTracking);
            trackedBody.OnStopTracking.RemoveListener(HandleStopTracking);
            trackedBody.OnPoseUpdated -= HandlePoseUpdated;
        }
    }

    private void HandleTrackerInitialized()
    {
        Debug.Log($"[XRTrackerTest] Tracker initialized. License: {XRTrackerManager.Instance.LicenseStatus}");
        XRTrackerManager.Instance.StartDetection();
    }

    private void HandleCameraSelected(FTCameraDevice device)
    {
        Debug.Log($"[XRTrackerTest] Camera selected: {device.name} ({XRTrackerManager.Instance.ImageWidth}x{XRTrackerManager.Instance.ImageHeight})");
    }

    private void HandleStartTracking()
    {
        Debug.Log($"[XRTrackerTest] Tracking started for body: {trackedBody.name}");
    }

    private void HandleStopTracking()
    {
        Debug.Log($"[XRTrackerTest] Tracking stopped for body: {trackedBody.name}");
    }

    private void HandlePoseUpdated(Vector3 position, Quaternion rotation)
    {
        Debug.Log($"[XRTrackerTest] Pose pos={position} rot={rotation.eulerAngles}");
    }

    private void Update()
    {
        if (trackedBody == null || !trackedBody.IsTracking) return;
        if (Time.frameCount % 60 != 0) return;

        Debug.Log($"[XRTrackerTest] status={trackedBody.TrackingStatus} " +
                  $"err={trackedBody.ProjectionErrorAverage:F3} " +
                  $"coverage={trackedBody.EdgeCoverageAverage:F2} " +
                  $"visibility={trackedBody.Visibility:F2}");
    }
}
