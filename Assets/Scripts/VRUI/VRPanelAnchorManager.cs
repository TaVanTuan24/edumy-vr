using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Manages VR panel anchoring, smooth transitions, and diagnostic setup for world-space UI Toolkit.
/// Supports two modes: Browsing (side) and Video (front).
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

    [Header("Hard Clamp")]
    [SerializeField] private bool hardClampToFov = true;
    [SerializeField, Range(15f, 85f)] private float maxHorizontalFovAngle = 42f;
    [SerializeField, Min(0.2f)] private float minDistanceFromCamera = 0.75f;

    [Header("UI Surface (Optional RenderTexture)")]
    [SerializeField] private bool useRenderTextureSurface = true;
    [SerializeField] private Vector2Int rtResolution = new Vector2Int(1920, 1080);
    [SerializeField] private Transform uiSurfaceTransform;
    [SerializeField, Min(0.2f)] private float surfaceHeightMeters = 0.5f;
    [SerializeField, Range(1.0f, 2.2f)] private float surfaceAspect = 1.25f;
    [SerializeField] private bool forceSurfaceLocalOrientation = true;
    [SerializeField] private bool rotateSurface180Y = false;
    [SerializeField] private bool flipTextureHorizontally = false;
    [SerializeField] private bool flipTextureVertically = true;
    [SerializeField] private bool liveApplySurfaceSettings = true;

    private PanelMode currentMode = PanelMode.Browsing;
    private Vector3 initialLocalScale;
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private Vector3 targetScale;
    private RenderTexture uiRenderTexture;
    private Material surfaceMaterial;

    private void Awake()
    {
        initialLocalScale = transform.localScale;
        targetScale = initialLocalScale;
        
        if (cameraAnchor == null) cameraAnchor = Camera.main?.transform;
    }

    private void Start()
    {
        SetupWorldSpaceUISurface();
        SetMode(PanelMode.Browsing, true);
    }

    private void LateUpdate()
    {
        if (followViewerInPlayMode)
        {
            UpdateTargets(false);
            ApplySmoothTransitions();
        }
        else if (staticModeScaleWithPanelMode)
        {
            ApplyScaleOnlyTransition();
        }

        if (liveApplySurfaceSettings && useRenderTextureSurface)
        {
            ApplySurfaceVisualSettings();
        }
    }

    /// <summary>
    /// Configures the panel to be rendered into a RenderTexture and displayed on a world surface (quad).
    /// </summary>
    [ContextMenu("Setup World Space UI Surface")]
    public void SetupWorldSpaceUISurface()
    {
        UIDocument uiDoc = GetComponent<UIDocument>();
        if (uiDoc == null || uiDoc.panelSettings == null)
        {
            Debug.LogWarning("[VRPanelAnchor] UIDocument or PanelSettings missing. Interaction may fail.");
            return;
        }

        if (useRenderTextureSurface)
        {
            // 1. Ensure RenderTexture
            if (uiRenderTexture == null)
            {
                uiRenderTexture = new RenderTexture(rtResolution.x, rtResolution.y, 24, RenderTextureFormat.ARGB32)
                {
                    name = "VRCoursePanel_RT",
                    antiAliasing = 4,
                    useMipMap = false
                };
                uiRenderTexture.Create();
            }

            // 2. Assign to PanelSettings
            uiDoc.panelSettings.targetTexture = uiRenderTexture;
            uiDoc.panelSettings.clearColor = true;

            // 3. Setup Surface Material
            if (uiSurfaceTransform == null)
            {
                // Find or create a child quad for display
                Transform existing = transform.Find("UISurface");
                if (existing == null)
                {
                    GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    quad.name = "UISurface";
                    quad.transform.SetParent(transform);
                    quad.transform.localPosition = Vector3.zero;
                    quad.transform.localRotation = Quaternion.identity;
                    uiSurfaceTransform = quad.transform;
                    
                    // Remove MeshCollider from primitive, we want our specific BoxCollider
                    DestroyImmediate(quad.GetComponent<MeshCollider>());
                }
                else
                {
                    uiSurfaceTransform = existing;
                }
            }

            if (forceSurfaceLocalOrientation)
            {
                uiSurfaceTransform.localPosition = Vector3.zero;
                uiSurfaceTransform.localRotation = rotateSurface180Y
                    ? Quaternion.Euler(0f, 180f, 0f)
                    : Quaternion.identity;
            }

            // Make panel compact and remove excessive empty area in world-space.
            float clampedAspect = Mathf.Clamp(surfaceAspect, 1.0f, 2.2f);
            float clampedHeight = Mathf.Max(0.2f, surfaceHeightMeters);
            uiSurfaceTransform.localScale = new Vector3(clampedAspect * clampedHeight, clampedHeight, 1f);

            MeshRenderer renderer = uiSurfaceTransform.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Material mat = surfaceMaterial != null ? surfaceMaterial : renderer.sharedMaterial;
                if (mat == null || mat.shader == null || mat.shader.name != "Unlit/Texture")
                {
                    mat = new Material(Shader.Find("Unlit/Texture"));
                }

                mat.mainTexture = uiRenderTexture;
                surfaceMaterial = mat;
                ApplyTextureFlipToMaterial(mat);
                renderer.sharedMaterial = mat;
            }

            // 4. Interaction routing
            if (uiSurfaceTransform.GetComponent<PanelRaycaster>() == null)
                uiSurfaceTransform.gameObject.AddComponent<PanelRaycaster>();

            BoxCollider col = uiSurfaceTransform.GetComponent<BoxCollider>();
            if (col == null)
                col = uiSurfaceTransform.gameObject.AddComponent<BoxCollider>();
            
            // Matches quad size
            col.center = Vector3.zero;
            col.size = new Vector3(1f, 1f, 0.02f);

            Debug.Log("[VRPanelAnchor] World-space UI surface initialized via RenderTexture.");
        }
    }

    private void ApplySurfaceVisualSettings()
    {
        if (uiSurfaceTransform == null) return;

        if (forceSurfaceLocalOrientation)
        {
            uiSurfaceTransform.localPosition = Vector3.zero;
            uiSurfaceTransform.localRotation = rotateSurface180Y
                ? Quaternion.Euler(0f, 180f, 0f)
                : Quaternion.identity;
        }

        float clampedAspect = Mathf.Clamp(surfaceAspect, 1.0f, 2.2f);
        float clampedHeight = Mathf.Max(0.2f, surfaceHeightMeters);
        uiSurfaceTransform.localScale = new Vector3(clampedAspect * clampedHeight, clampedHeight, 1f);

        MeshRenderer renderer = uiSurfaceTransform.GetComponent<MeshRenderer>();
        if (renderer != null && renderer.sharedMaterial != null)
        {
            ApplyTextureFlipToMaterial(renderer.sharedMaterial);
        }
    }

    private void ApplyTextureFlipToMaterial(Material mat)
    {
        if (mat == null) return;

        float scaleX = flipTextureHorizontally ? -1f : 1f;
        float scaleY = flipTextureVertically ? -1f : 1f;
        mat.mainTextureScale = new Vector2(scaleX, scaleY);
        mat.mainTextureOffset = new Vector2(flipTextureHorizontally ? 1f : 0f, flipTextureVertically ? 1f : 0f);
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
            targetRotation = Quaternion.Euler(0f, euler.y, 0f);
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

    private void OnDestroy()
    {
        if (uiRenderTexture != null)
        {
            uiRenderTexture.Release();
            Destroy(uiRenderTexture);
        }

        if (surfaceMaterial != null)
        {
            Destroy(surfaceMaterial);
            surfaceMaterial = null;
        }
    }

    [ContextMenu("Validate Setup")]
    public void ValidateSetup()
    {
        if (Camera.main == null) Debug.LogError("No MainCamera found.");
        if (GetComponent<UIDocument>() == null) Debug.LogError("UIDocument missing on this object.");
        if (rightHandAnchor == null) Debug.LogWarning("Right hand anchor missing (will fallback to HMD).");
        Debug.Log("Validation complete.");
    }

    [ContextMenu("Snap to Browsing Mode")]
    public void SnapBrowsing() => SetMode(PanelMode.Browsing, true);

    [ContextMenu("Snap to Video Mode")]
    public void SnapVideo() => SetMode(PanelMode.Video, true);
}
