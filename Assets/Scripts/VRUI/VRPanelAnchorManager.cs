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
    [SerializeField] private Vector3 browsingOffset = new Vector3(0.55f, -0.05f, 0.4f);
    [SerializeField] private float positionLerpSpeed = 6.0f;

    [Header("Video Mode Settings")]
    [SerializeField] private float videoDistance = 1.35f;
    [SerializeField] private float videoHeightOffset = 0.0f;
    [SerializeField] private Vector3 videoScaleMultiplier = new Vector3(1.75f, 1.75f, 1.75f);

    [Header("Transition Settings")]
    [SerializeField] private float rotationLerpSpeed = 10.0f;
    [SerializeField] private float scaleLerpSpeed = 6.0f;

    [Header("UI Surface (Optional RenderTexture)")]
    [SerializeField] private bool useRenderTextureSurface = true;
    [SerializeField] private Vector2Int rtResolution = new Vector2Int(1920, 1080);
    [SerializeField] private Transform uiSurfaceTransform;

    private PanelMode currentMode = PanelMode.Browsing;
    private Vector3 initialLocalScale;
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private Vector3 targetScale;
    private RenderTexture uiRenderTexture;

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
        UpdateTargets(false);
        ApplySmoothTransitions();
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
                    quad.transform.localRotation = Quaternion.Euler(0, 180, 0); // Facade
                    // Scale quad based on aspect ratio
                    float aspect = (float)rtResolution.x / rtResolution.y;
                    quad.transform.localScale = new Vector3(aspect, 1f, 1f);
                    uiSurfaceTransform = quad.transform;
                    
                    // Remove MeshCollider from primitive, we want our specific BoxCollider
                    DestroyImmediate(quad.GetComponent<MeshCollider>());
                }
                else
                {
                    uiSurfaceTransform = existing;
                }
            }

            MeshRenderer renderer = uiSurfaceTransform.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Material mat = new Material(Shader.Find("Unlit/Texture"));
                mat.mainTexture = uiRenderTexture;
                renderer.sharedMaterial = mat;
            }

            // 4. Interaction routing
            if (uiSurfaceTransform.GetComponent<PanelRaycaster>() == null)
                uiSurfaceTransform.gameObject.AddComponent<PanelRaycaster>();

            BoxCollider col = uiSurfaceTransform.GetComponent<BoxCollider>();
            if (col == null)
                col = uiSurfaceTransform.gameObject.AddComponent<BoxCollider>();
            
            // Matches quad size
            float aspectVal = (float)rtResolution.x / rtResolution.y;
            col.size = new Vector3(aspectVal, 1f, 0.01f);

            Debug.Log("[VRPanelAnchor] World-space UI surface initialized via RenderTexture.");
        }
    }

    public void SetMode(PanelMode mode, bool immediate = false)
    {
        currentMode = mode;
        UpdateTargets(immediate);
        if (immediate) SnapToTarget();
    }

    private void UpdateTargets(bool immediate)
    {
        Transform activeAnchor = (currentMode == PanelMode.Browsing && rightHandAnchor != null) ? rightHandAnchor : cameraAnchor;
        if (activeAnchor == null || cameraAnchor == null) return;

        if (currentMode == PanelMode.Browsing)
        {
            Vector3 forward = cameraAnchor.forward;
            Vector3 right = cameraAnchor.right;
            forward.y = 0; right.y = 0;
            forward.Normalize(); right.Normalize();

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

        Vector3 directionToUser = cameraAnchor.position - targetPosition;
        directionToUser.y = 0;
        if (directionToUser.sqrMagnitude > 0.001f)
            targetRotation = Quaternion.LookRotation(directionToUser, Vector3.up);
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

    private void OnDestroy()
    {
        if (uiRenderTexture != null)
        {
            uiRenderTexture.Release();
            Destroy(uiRenderTexture);
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
