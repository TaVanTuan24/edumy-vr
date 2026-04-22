using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Manages VR panel anchoring, smooth transitions, and XR interaction setup
/// for world-space UI Toolkit panels.
/// Supports two placement modes: Browsing (side) and Video (front).
///
/// Architecture:
///   - The UIDocument's PanelSettings must have renderMode = WorldSpace.
///   - A BoxCollider on this GameObject enables XR ray hit detection.
///   - The EventSystem + XRUIInputModule handle all event routing automatically.
///   - No RenderTexture intermediary is used.
/// </summary>
public class VRPanelAnchorManager : MonoBehaviour
{
    public enum PanelMode { Browsing, Video }

    [Header("Anchors")]
    [Tooltip("The VR Rig's Right Hand Controller anchor.")]
    public Transform rightHandAnchor;
    [Tooltip("The VR Rig's Main Camera (HMD).")]
    public Transform cameraAnchor;

    [Header("Browsing Mode Settings")]
    [SerializeField] private Vector3 browsingOffset = new Vector3(0.34f, -0.03f, 0.62f);
    [SerializeField] private bool anchorBrowsingToCamera = true;
    [SerializeField] private float positionLerpSpeed = 6.0f;

    [Header("Video Mode Settings")]
    [SerializeField] private float videoDistance = 1.35f;
    [SerializeField] private float videoHeightOffset = 0.0f;
    [SerializeField] private Vector3 videoScaleMultiplier = new Vector3(1.75f, 1.75f, 1.75f);

    [Header("Transition Settings")]
    [SerializeField] private float rotationLerpSpeed = 10.0f;
    [SerializeField] private float scaleLerpSpeed = 6.0f;

    [Header("Debug Placement")]
    [SerializeField] private bool followViewerInPlayMode = true;
    [SerializeField] private bool staticModeScaleWithPanelMode = false;
    [SerializeField] private bool allowSpatialWindowOverride = true;

    [Header("Hard Clamp")]
    [SerializeField] private bool hardClampToFov = true;
    [SerializeField, Range(15f, 85f)] private float maxHorizontalFovAngle = 42f;
    [SerializeField, Min(0.2f)] private float minDistanceFromCamera = 0.75f;

    private PanelMode currentMode = PanelMode.Browsing;
    private Vector3 initialLocalScale;
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private Vector3 targetScale;
    private SpatialWindow spatialWindow;

    private void Awake()
    {
        initialLocalScale = transform.localScale;
        targetScale = initialLocalScale;
        spatialWindow = GetComponent<SpatialWindow>();
        
        if (cameraAnchor == null) cameraAnchor = Camera.main?.transform;
    }

    private void Start()
    {
        try
        {
            DestroyLegacyUISurface();
            EnsureWorldSpaceInteraction();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[VRPanelAnchor] Failed to initialize world-space UI interaction.\n{ex}");
        }

        SetMode(PanelMode.Browsing, true);
    }

    private void LateUpdate()
    {
        bool spatialWindowOwnsPlacement = allowSpatialWindowOverride
            && spatialWindow != null
            && spatialWindow.enabled;

        if (!spatialWindowOwnsPlacement && followViewerInPlayMode)
        {
            UpdateTargets(false);
            ApplySmoothTransitions();
        }
        else if (!spatialWindowOwnsPlacement && staticModeScaleWithPanelMode)
        {
            ApplyScaleOnlyTransition();
        }
    }

    /// <summary>
    /// Configures native World Space UI Toolkit interaction for XR.
    /// Ensures the UIDocument has a BoxCollider for ray detection and
    /// that the EventSystem has XRUIInputModule for tracked device support.
    /// </summary>
    [ContextMenu("Ensure World Space Interaction")]
    public void EnsureWorldSpaceInteraction()
    {
        UIDocument uiDoc = GetComponent<UIDocument>();
        if (uiDoc == null || uiDoc.panelSettings == null)
        {
            Debug.LogWarning("[VRPanelAnchor] UIDocument or PanelSettings missing. Interaction may fail.");
            return;
        }

        // Ensure no stale targetTexture overrides the native World Space render mode.
        uiDoc.panelSettings.targetTexture = null;
        uiDoc.panelSettings.clearColor = false;

        // Ensure a BoxCollider on this GameObject so XR rays can hit the UI panel.
        // Unity 6 World Space UI Toolkit uses this collider for interaction.
        // PanelSettings with ColliderUpdateMode=Auto will auto-resize it to match the panel.
        BoxCollider col = GetComponent<BoxCollider>();
        if (col == null)
        {
            col = gameObject.AddComponent<BoxCollider>();
        }
        col.isTrigger = true;

        // Set a reasonable default size in case auto-sizing hasn't run yet.
        if (col.size.sqrMagnitude < 0.001f)
        {
            col.size = new Vector3(1f, 1f, 0.01f);
            col.center = Vector3.zero;
        }

        // Add PanelEventHandler (required for some versions of XRI to route events to UI Toolkit)
        const string PanelEventHandlerType = "UnityEngine.UIElements.PanelEventHandler, UnityEngine.UIElementsModule";
        XRRuntimeUiHelper.TryAddComponentByTypeName(gameObject, PanelEventHandlerType);

        // Ensure the EventSystem has XRUIInputModule for tracked device ray support.
        XRRuntimeUiHelper.EnsureEventSystemSupportsXR();

        Debug.Log($"[VRPanelAnchor] World Space UI interaction active. " +
            $"UIDocument='{gameObject.name}' " +
            $"Collider={col != null} " +
            $"ColliderSize={col.size} " +
            $"PanelRenderMode={uiDoc.panelSettings.renderMode}");
    }

    /// <summary>
    /// Destroys any leftover "UISurface" child quad from the old RenderTexture pipeline.
    /// These quads have colliders that intercept XR rays, blocking native UI interaction.
    /// </summary>
    private void DestroyLegacyUISurface()
    {
        Transform uiSurface = transform.Find("UISurface");
        if (uiSurface == null) return;

        Debug.Log($"[VRPanelAnchor] Destroying legacy UISurface quad on '{gameObject.name}'. " +
            "This object was part of the old RenderTexture pipeline and would block XR interaction.");

        if (Application.isPlaying)
        {
            Destroy(uiSurface.gameObject);
        }
        else
        {
            DestroyImmediate(uiSurface.gameObject);
        }
    }

    public void SetMode(PanelMode mode, bool immediate = false)
    {
        currentMode = mode;

        if (!followViewerInPlayMode)
        {
            if (staticModeScaleWithPanelMode)
            {
                targetScale = currentMode == PanelMode.Video
                    ? Vector3.Scale(initialLocalScale, videoScaleMultiplier)
                    : initialLocalScale;

                if (immediate)
                {
                    transform.localScale = targetScale;
                }
            }

            return;
        }

        UpdateTargets(immediate);
        if (immediate) SnapToTarget();
    }

    private void UpdateTargets(bool immediate)
    {
        Transform activeAnchor = ResolveBrowsingAnchor();
        if (activeAnchor == null || cameraAnchor == null) return;

        if (currentMode == PanelMode.Browsing)
        {
            Vector3 forward = cameraAnchor.forward;
            Vector3 right = cameraAnchor.right;
            forward.y = 0; right.y = 0;
            forward.Normalize(); right.Normalize();

            // Keep browsing panel stable in HMD space to avoid drifting/tilting due to controller pose.
            targetPosition = activeAnchor.position + (right * browsingOffset.x) + (Vector3.up * browsingOffset.y) + (forward * browsingOffset.z);
            targetScale = initialLocalScale;
        }
        else // Video
        {
            Vector3 forward = cameraAnchor.forward;
            forward.y = 0; forward.Normalize();

            targetPosition = cameraAnchor.position + (forward * videoDistance) + (Vector3.up * videoHeightOffset);
            targetScale = Vector3.Scale(initialLocalScale, videoScaleMultiplier);
        }

        if (hardClampToFov)
        {
            targetPosition = ClampPositionToHorizontalFov(targetPosition, cameraAnchor.position, cameraAnchor.forward);
        }

        Vector3 directionToUser = cameraAnchor.position - targetPosition;
        directionToUser.y = 0;
        if (directionToUser.sqrMagnitude > 0.001f)
        {
            Quaternion look = Quaternion.LookRotation(directionToUser, Vector3.up);
            Vector3 euler = look.eulerAngles;
            // World-space UIDocument faces away from the camera by default,
            // so add 180° to face toward the viewer.
            float yaw = euler.y + 180f;
            targetRotation = Quaternion.Euler(0f, yaw, 0f);
        }
    }

    private Vector3 ClampPositionToHorizontalFov(Vector3 worldPos, Vector3 camPos, Vector3 camForward)
    {
        Vector3 flatForward = new Vector3(camForward.x, 0f, camForward.z);
        if (flatForward.sqrMagnitude < 0.0001f)
        {
            flatForward = Vector3.forward;
        }
        flatForward.Normalize();

        Vector3 toPanel = worldPos - camPos;
        float y = toPanel.y;

        Vector3 flatToPanel = new Vector3(toPanel.x, 0f, toPanel.z);
        float distance = flatToPanel.magnitude;
        if (distance < minDistanceFromCamera)
        {
            distance = minDistanceFromCamera;
        }

        if (flatToPanel.sqrMagnitude < 0.0001f)
        {
            flatToPanel = flatForward * distance;
        }

        float signedAngle = Vector3.SignedAngle(flatForward, flatToPanel.normalized, Vector3.up);
        float clampedAngle = Mathf.Clamp(signedAngle, -maxHorizontalFovAngle, maxHorizontalFovAngle);
        Vector3 clampedDir = Quaternion.AngleAxis(clampedAngle, Vector3.up) * flatForward;

        Vector3 clampedFlat = clampedDir * distance;
        return new Vector3(camPos.x + clampedFlat.x, camPos.y + y, camPos.z + clampedFlat.z);
    }

    private Transform ResolveBrowsingAnchor()
    {
        if (cameraAnchor == null)
            cameraAnchor = Camera.main != null ? Camera.main.transform : null;

        if (currentMode != PanelMode.Browsing)
            return cameraAnchor;

        if (anchorBrowsingToCamera || rightHandAnchor == null)
            return cameraAnchor;

        return rightHandAnchor;
    }

    private void ApplySmoothTransitions()
    {
        float dt = Time.unscaledDeltaTime;
        transform.position = Vector3.Lerp(transform.position, targetPosition, positionLerpSpeed * dt);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationLerpSpeed * dt);
        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, scaleLerpSpeed * dt);
    }

    private void SnapToTarget()
    {
        transform.position = targetPosition;
        transform.rotation = targetRotation;
        transform.localScale = targetScale;
    }

    private void ApplyScaleOnlyTransition()
    {
        float dt = Time.unscaledDeltaTime;
        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, scaleLerpSpeed * dt);
    }

    [ContextMenu("Validate Setup")]
    public void ValidateSetup()
    {
        if (Camera.main == null) Debug.LogError("No MainCamera found.");
        if (GetComponent<UIDocument>() == null) Debug.LogError("UIDocument missing on this object.");
        if (rightHandAnchor == null) Debug.LogWarning("Right hand anchor missing (will fallback to HMD).");
        BoxCollider col = GetComponent<BoxCollider>();
        if (col == null) Debug.LogError("BoxCollider missing. XR interaction will not work.");
        Debug.Log("Validation complete.");
    }

    [ContextMenu("Snap to Browsing Mode")]
    public void SnapBrowsing() => SetMode(PanelMode.Browsing, true);

    [ContextMenu("Snap to Video Mode")]
    public void SnapVideo() => SetMode(PanelMode.Video, true);
}
