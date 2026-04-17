using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[DisallowMultipleComponent]
public class SpatialWindow : MonoBehaviour
{
    [Serializable]
    public class PinStateChangedEvent : UnityEvent<bool> { }

    [Header("Targets")]
    [SerializeField] private Transform windowRoot;
    [SerializeField] private Transform contentBoundsSource;
    [SerializeField] private Transform viewer;

    [Header("Follow")]
    [SerializeField] private bool startPinned = true;
    [SerializeField] private bool followWhenUnpinned = true;
    [SerializeField] private float followDistance = 1.6f;
    [SerializeField] private float horizontalFollowOffset = 0f;
    [SerializeField] private float verticalFollowOffset = 0f;
    [SerializeField] private float minFollowDistance = 0.65f;
    [SerializeField] private float maxFollowDistance = 3.5f;
    [SerializeField] private float followPositionLerp = 9f;
    [SerializeField] private float followRotationLerp = 10f;
    [SerializeField] private bool faceViewerWhileFollowing = true;
    [SerializeField] private bool flattenFollowDirection = true;
    [SerializeField] private bool flipForwardToFaceViewer = true;

    [Header("Resize")]
    [SerializeField] private bool allowResize = false;
    [SerializeField] private float minScaleMultiplier = 0.6f;
    [SerializeField] private float maxScaleMultiplier = 1.8f;

    [Header("Interaction Chrome")]
    [SerializeField] private bool chromeVisible = true;
    [SerializeField] private bool interactionsEnabled = true;
    [SerializeField] private Color headerColor = new Color(0.14f, 0.42f, 0.72f, 0.38f);
    [SerializeField] private Color resizeColor = new Color(0.15f, 0.78f, 0.92f, 0.5f);
    [SerializeField] private float headerHeightWorld = 0.085f;
    [SerializeField] private float headerDepthWorld = 0.02f;
    [SerializeField] private float handleSizeWorld = 0.06f;
    [SerializeField] private float chromeForwardOffset = 0.018f;

    [Header("Events")]
    [SerializeField] private PinStateChangedEvent onPinStateChanged = new PinStateChangedEvent();

    private readonly Vector3[] worldCorners = new Vector3[4];

    private Transform chromeRoot;
    private SpatialWindowHandle dragHandle;
    private SpatialWindowHandle[] resizeHandles;
    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private Vector3 originalScale;
    private bool originalCaptured;
    private bool isPinned;
    private bool isDragging;
    private bool isResizing;
    private IXRSelectInteractor activeInteractor;
    private SpatialWindowHandle activeHandle;
    private Vector3 dragLocalPositionOffset;
    private Quaternion dragLocalRotationOffset;
    private Vector3 resizeStartScale;
    private float resizeStartDistance;

    public Transform WindowRoot => windowRoot != null ? windowRoot : transform;
    public Transform ContentBoundsSource => contentBoundsSource;
    public bool IsPinned => isPinned;
    public bool InteractionsEnabled => interactionsEnabled;
    public bool ChromeVisible => chromeVisible;

    private void Awake()
    {
        EnsureSetup();
        CaptureOriginalTransformIfNeeded();
        isPinned = startPinned;
    }

    private void Start()
    {
        if (viewer == null && Camera.main != null)
        {
            viewer = Camera.main.transform;
        }

        ApplyChromeVisibility();
        ApplyInteractionsEnabled();
        onPinStateChanged?.Invoke(isPinned);
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying || windowRoot == null)
        {
            return;
        }

        if (isDragging || isResizing)
        {
            return;
        }

        if (isPinned || !followWhenUnpinned || viewer == null || !windowRoot.gameObject.activeInHierarchy)
        {
            return;
        }

        Pose targetPose = BuildFollowPose(viewer, followDistance, horizontalFollowOffset, verticalFollowOffset);
        float positionBlend = 1f - Mathf.Exp(-Mathf.Max(0.01f, followPositionLerp) * Time.deltaTime);
        float rotationBlend = 1f - Mathf.Exp(-Mathf.Max(0.01f, followRotationLerp) * Time.deltaTime);
        windowRoot.position = Vector3.Lerp(windowRoot.position, targetPose.position, positionBlend);
        windowRoot.rotation = Quaternion.Slerp(windowRoot.rotation, targetPose.rotation, rotationBlend);
    }

    public void ConfigureWindowRoot(Transform newWindowRoot, Transform newBoundsSource = null)
    {
        windowRoot = newWindowRoot != null ? newWindowRoot : transform;
        if (newBoundsSource != null)
        {
            contentBoundsSource = newBoundsSource;
        }

        EnsureSetup();
        RefreshChromeLayout();
    }

    public void SetViewer(Transform targetViewer)
    {
        viewer = targetViewer;
    }

    public void SetFollowSettings(float distance, float heightOffset, float horizontalOffset = 0f)
    {
        followDistance = Mathf.Clamp(distance, minFollowDistance, maxFollowDistance);
        verticalFollowOffset = heightOffset;
        horizontalFollowOffset = horizontalOffset;
    }

    public void SetPinned(bool value, bool snapInFrontWhenUnpinned = true)
    {
        EnsureSetup();
        isPinned = value;

        if (!isPinned && snapInFrontWhenUnpinned && viewer != null)
        {
            MoveInFrontOfViewer(viewer, followDistance, verticalFollowOffset, horizontalFollowOffset);
        }

        onPinStateChanged?.Invoke(isPinned);
    }

    public void TogglePinned()
    {
        SetPinned(!isPinned);
    }

    public void SetChromeVisible(bool visible)
    {
        chromeVisible = visible;
        ApplyChromeVisibility();
    }

    public void SetInteractionsEnabled(bool enabled)
    {
        interactionsEnabled = enabled;
        ApplyInteractionsEnabled();
    }

    public void SetAllowResize(bool enabled)
    {
        allowResize = enabled;
        ApplyInteractionsEnabled();
        RefreshChromeLayout();
    }

    public void RemoveChrome()
    {
        if (chromeRoot != null)
        {
            Destroy(chromeRoot.gameObject);
        }

        chromeRoot = null;
        dragHandle = null;
        resizeHandles = null;
    }

    public void MoveInFrontOfViewer(Transform targetViewer, float distance, float heightOffset, float horizontalOffset = 0f)
    {
        if (targetViewer == null)
        {
            return;
        }

        viewer = targetViewer;
        followDistance = Mathf.Clamp(distance, minFollowDistance, maxFollowDistance);
        verticalFollowOffset = heightOffset;
        horizontalFollowOffset = horizontalOffset;

        Pose pose = BuildFollowPose(targetViewer, followDistance, horizontalOffset, heightOffset);
        windowRoot.position = pose.position;
        windowRoot.rotation = pose.rotation;
    }

    public void PlaceAtAnchor(Transform anchor, bool copyScale = false)
    {
        if (anchor == null)
        {
            return;
        }

        EnsureSetup();
        windowRoot.position = anchor.position;
        windowRoot.rotation = anchor.rotation;
        if (copyScale)
        {
            windowRoot.localScale = anchor.lossyScale;
        }

        RefreshChromeLayout();
    }

    public void SetWorldPose(Vector3 position, Quaternion rotation)
    {
        EnsureSetup();
        windowRoot.position = position;
        windowRoot.rotation = rotation;
        RefreshChromeLayout();
    }

    public void SetLocalScale(Vector3 scale)
    {
        EnsureSetup();
        windowRoot.localScale = scale;
        RefreshChromeLayout();
    }

    public void RestoreOriginalTransform()
    {
        EnsureSetup();
        CaptureOriginalTransformIfNeeded();
        windowRoot.position = originalPosition;
        windowRoot.rotation = originalRotation;
        windowRoot.localScale = originalScale;
        RefreshChromeLayout();
    }

    public void RefreshChromeLayout()
    {
        if (chromeRoot == null || windowRoot == null)
        {
            return;
        }

        if (!TryGetWindowCorners(out Vector3 bottomLeft, out Vector3 topLeft, out Vector3 topRight, out Vector3 bottomRight))
        {
            return;
        }

        Vector3 worldForward = windowRoot.forward * chromeForwardOffset;
        Vector3 localTopLeft = windowRoot.InverseTransformPoint(topLeft + worldForward);
        Vector3 localTopRight = windowRoot.InverseTransformPoint(topRight + worldForward);
        Vector3 localBottomLeft = windowRoot.InverseTransformPoint(bottomLeft + worldForward);
        Vector3 localBottomRight = windowRoot.InverseTransformPoint(bottomRight + worldForward);

        Vector3 headerMid = (localTopLeft + localTopRight) * 0.5f;
        float widthLocal = Vector3.Distance(localTopLeft, localTopRight);
        float headerHeightLocal = WorldLengthToLocalY(headerHeightWorld);
        float headerDepthLocal = WorldLengthToLocalZ(headerDepthWorld);

        if (dragHandle != null)
        {
            Transform dragTransform = dragHandle.transform;
            dragTransform.localPosition = headerMid + new Vector3(0f, headerHeightLocal * 0.5f, 0f);
            dragTransform.localRotation = Quaternion.identity;
            dragTransform.localScale = new Vector3(widthLocal, headerHeightLocal, headerDepthLocal);
        }

        if (resizeHandles == null)
        {
            return;
        }

        float handleSizeLocalX = WorldLengthToLocalX(handleSizeWorld);
        float handleSizeLocalY = WorldLengthToLocalY(handleSizeWorld);
        float handleSizeLocalZ = WorldLengthToLocalZ(handleSizeWorld * 0.6f);

        Vector3[] localCorners = { localTopLeft, localTopRight, localBottomLeft, localBottomRight };
        for (int i = 0; i < resizeHandles.Length; i++)
        {
            SpatialWindowHandle handle = resizeHandles[i];
            if (handle == null)
            {
                continue;
            }

            handle.transform.localPosition = localCorners[i];
            handle.transform.localRotation = Quaternion.identity;
            handle.transform.localScale = new Vector3(handleSizeLocalX, handleSizeLocalY, handleSizeLocalZ);
            handle.gameObject.SetActive(allowResize);
        }
    }

    public void OnHandleSelectEntered(SpatialWindowHandle handle, IXRSelectInteractor interactor)
    {
        if (!interactionsEnabled || handle == null || interactor == null)
        {
            return;
        }

        EnsureSetup();
        SetPinned(true, false);

        activeHandle = handle;
        activeInteractor = interactor;

        if (handle.Kind == SpatialWindowHandle.HandleKind.Resize && allowResize)
        {
            isResizing = true;
            isDragging = false;
            resizeStartScale = windowRoot.localScale;
            resizeStartDistance = GetInteractorDistance(interactor, handle);
        }
        else
        {
            isDragging = true;
            isResizing = false;

            Transform attachTransform = interactor.GetAttachTransform(handle);
            if (attachTransform != null)
            {
                dragLocalPositionOffset = Quaternion.Inverse(attachTransform.rotation) * (windowRoot.position - attachTransform.position);
                dragLocalRotationOffset = Quaternion.Inverse(attachTransform.rotation) * windowRoot.rotation;
            }
        }
    }

    public void ProcessHandleSelection(SpatialWindowHandle handle, IXRSelectInteractor interactor)
    {
        if (!interactionsEnabled || handle == null || interactor == null || handle != activeHandle)
        {
            return;
        }

        if (handle.Kind == SpatialWindowHandle.HandleKind.Resize && allowResize && isResizing)
        {
            UpdateResize(interactor, handle);
            return;
        }

        if (isDragging)
        {
            UpdateDrag(interactor, handle);
        }
    }

    public void OnHandleSelectExited(SpatialWindowHandle handle, IXRSelectInteractor interactor)
    {
        if (handle != activeHandle)
        {
            return;
        }

        isDragging = false;
        isResizing = false;
        activeHandle = null;
        activeInteractor = null;
        RefreshChromeLayout();
    }

    private void EnsureSetup()
    {
        if (windowRoot == null)
        {
            windowRoot = transform;
        }

        if (contentBoundsSource == null)
        {
            contentBoundsSource = ResolveDefaultBoundsSource();
        }

        EnsureChrome();
    }

    private Transform ResolveDefaultBoundsSource()
    {
        if (windowRoot == null)
        {
            return transform;
        }

        Transform uiSurface = windowRoot.Find("UISurface");
        if (uiSurface != null)
        {
            return uiSurface;
        }

        RectTransform rect = windowRoot.GetComponent<RectTransform>();
        if (rect != null)
        {
            return rect;
        }

        Renderer renderer = windowRoot.GetComponentInChildren<Renderer>(true);
        if (renderer != null)
        {
            return renderer.transform;
        }

        return windowRoot;
    }

    private void EnsureChrome()
    {
        if (chromeRoot != null)
        {
            RefreshChromeMaterials();
            return;
        }

        Transform existing = windowRoot.Find("SpatialChrome");
        if (existing != null)
        {
            chromeRoot = existing;
        }
        else
        {
            chromeRoot = new GameObject("SpatialChrome").transform;
            chromeRoot.SetParent(windowRoot, false);
        }

        dragHandle = EnsureHandle("HeaderHandle", SpatialWindowHandle.HandleKind.Drag, headerColor);
        resizeHandles = new[]
        {
            EnsureHandle("ResizeHandle_TopLeft", SpatialWindowHandle.HandleKind.Resize, resizeColor),
            EnsureHandle("ResizeHandle_TopRight", SpatialWindowHandle.HandleKind.Resize, resizeColor),
            EnsureHandle("ResizeHandle_BottomLeft", SpatialWindowHandle.HandleKind.Resize, resizeColor),
            EnsureHandle("ResizeHandle_BottomRight", SpatialWindowHandle.HandleKind.Resize, resizeColor),
        };

        ApplyChromeVisibility();
        ApplyInteractionsEnabled();
        RefreshChromeLayout();
    }

    private SpatialWindowHandle EnsureHandle(string name, SpatialWindowHandle.HandleKind kind, Color color)
    {
        Transform existing = chromeRoot.Find(name);
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(chromeRoot, false);
        }

        SpatialWindowHandle handle = go.GetComponent<SpatialWindowHandle>();
        if (handle == null)
        {
            handle = go.AddComponent<SpatialWindowHandle>();
        }
        handle.Configure(this, kind);

        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material material = Application.isPlaying ? renderer.material : renderer.sharedMaterial;
            if (material == null || material.shader == null || material.shader.name != "Unlit/Color")
            {
                material = new Material(Shader.Find("Unlit/Color"));
            }

            material.color = color;
            if (Application.isPlaying)
            {
                renderer.material = material;
            }
            else
            {
                renderer.sharedMaterial = material;
            }
        }

        BoxCollider collider = go.GetComponent<BoxCollider>();
        if (collider == null)
        {
            collider = go.AddComponent<BoxCollider>();
        }
        collider.isTrigger = false;

        return handle;
    }

    private void RefreshChromeMaterials()
    {
        if (dragHandle != null)
        {
            ApplyHandleColor(dragHandle.gameObject, headerColor);
        }

        if (resizeHandles == null)
        {
            return;
        }

        for (int i = 0; i < resizeHandles.Length; i++)
        {
            if (resizeHandles[i] != null)
            {
                ApplyHandleColor(resizeHandles[i].gameObject, resizeColor);
            }
        }
    }

    private static void ApplyHandleColor(GameObject target, Color color)
    {
        if (target == null)
        {
            return;
        }

        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer == null)
        {
            return;
        }

        Material material = Application.isPlaying ? renderer.material : renderer.sharedMaterial;
        if (material == null || material.shader == null || material.shader.name != "Unlit/Color")
        {
            material = new Material(Shader.Find("Unlit/Color"));
        }

        material.color = color;
        if (Application.isPlaying)
        {
            renderer.material = material;
        }
        else
        {
            renderer.sharedMaterial = material;
        }
    }

    private void ApplyChromeVisibility()
    {
        if (chromeRoot != null)
        {
            chromeRoot.gameObject.SetActive(chromeVisible);
        }
    }

    private void ApplyInteractionsEnabled()
    {
        if (dragHandle != null)
        {
            dragHandle.enabled = interactionsEnabled;
            Collider collider = dragHandle.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = interactionsEnabled;
            }
        }

        if (resizeHandles == null)
        {
            return;
        }

        bool resizeEnabled = interactionsEnabled && allowResize;
        for (int i = 0; i < resizeHandles.Length; i++)
        {
            SpatialWindowHandle handle = resizeHandles[i];
            if (handle == null)
            {
                continue;
            }

            handle.enabled = resizeEnabled;
            Collider collider = handle.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = resizeEnabled;
            }
            handle.gameObject.SetActive(resizeEnabled);
        }
    }

    private void UpdateDrag(IXRSelectInteractor interactor, SpatialWindowHandle handle)
    {
        Transform attachTransform = interactor.GetAttachTransform(handle);
        if (attachTransform == null)
        {
            return;
        }

        windowRoot.position = attachTransform.position + attachTransform.rotation * dragLocalPositionOffset;
        windowRoot.rotation = attachTransform.rotation * dragLocalRotationOffset;
        RefreshChromeLayout();
    }

    private void UpdateResize(IXRSelectInteractor interactor, SpatialWindowHandle handle)
    {
        float currentDistance = GetInteractorDistance(interactor, handle);
        if (resizeStartDistance <= 0.0001f || currentDistance <= 0.0001f)
        {
            return;
        }

        float ratio = currentDistance / resizeStartDistance;
        Vector3 scaled = resizeStartScale * ratio;
        Vector3 minScale = originalScale * Mathf.Max(0.05f, minScaleMultiplier);
        Vector3 maxScale = originalScale * Mathf.Max(minScaleMultiplier, maxScaleMultiplier);

        scaled.x = Mathf.Clamp(scaled.x, minScale.x, maxScale.x);
        scaled.y = Mathf.Clamp(scaled.y, minScale.y, maxScale.y);
        scaled.z = Mathf.Clamp(scaled.z, minScale.z, maxScale.z);
        windowRoot.localScale = scaled;
        RefreshChromeLayout();
    }

    private float GetInteractorDistance(IXRSelectInteractor interactor, SpatialWindowHandle handle)
    {
        Transform attachTransform = interactor.GetAttachTransform(handle);
        if (attachTransform == null || windowRoot == null)
        {
            return 0f;
        }

        return Vector3.Distance(windowRoot.position, attachTransform.position);
    }

    private Pose BuildFollowPose(Transform targetViewer, float distance, float horizontalOffset, float heightOffset)
    {
        Vector3 forward = targetViewer.forward;
        Vector3 right = targetViewer.right;

        if (flattenFollowDirection)
        {
            forward.y = 0f;
            right.y = 0f;
        }

        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = targetViewer.forward;
        }
        if (right.sqrMagnitude < 0.0001f)
        {
            right = targetViewer.right;
        }

        forward.Normalize();
        right.Normalize();

        float clampedDistance = Mathf.Clamp(distance, minFollowDistance, maxFollowDistance);
        Vector3 position = targetViewer.position
            + forward * clampedDistance
            + right * horizontalOffset
            + Vector3.up * heightOffset;

        Quaternion rotation = windowRoot != null ? windowRoot.rotation : transform.rotation;
        if (faceViewerWhileFollowing)
        {
            Vector3 toViewer = targetViewer.position + (Vector3.up * heightOffset) - position;
            if (flattenFollowDirection)
            {
                toViewer.y = 0f;
            }

            if (toViewer.sqrMagnitude > 0.0001f)
            {
                rotation = Quaternion.LookRotation(toViewer.normalized, Vector3.up);
                if (flipForwardToFaceViewer)
                {
                    rotation *= Quaternion.Euler(0f, 180f, 0f);
                }
            }
        }

        return new Pose(position, rotation);
    }

    private bool TryGetWindowCorners(out Vector3 bottomLeft, out Vector3 topLeft, out Vector3 topRight, out Vector3 bottomRight)
    {
        Transform source = contentBoundsSource != null ? contentBoundsSource : windowRoot;
        if (source == null)
        {
            bottomLeft = topLeft = topRight = bottomRight = Vector3.zero;
            return false;
        }

        RectTransform rectTransform = source as RectTransform;
        if (rectTransform != null)
        {
            rectTransform.GetWorldCorners(worldCorners);
            bottomLeft = worldCorners[0];
            topLeft = worldCorners[1];
            topRight = worldCorners[2];
            bottomRight = worldCorners[3];
            return true;
        }

        MeshFilter meshFilter = source.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            meshFilter = source.GetComponentInChildren<MeshFilter>(true);
        }

        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            Bounds bounds = meshFilter.sharedMesh.bounds;
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            bottomLeft = source.TransformPoint(center + new Vector3(-extents.x, -extents.y, 0f));
            topLeft = source.TransformPoint(center + new Vector3(-extents.x, extents.y, 0f));
            topRight = source.TransformPoint(center + new Vector3(extents.x, extents.y, 0f));
            bottomRight = source.TransformPoint(center + new Vector3(extents.x, -extents.y, 0f));
            return true;
        }

        Vector3 position = source.position;
        Vector3 right = source.right * 0.5f;
        Vector3 up = source.up * 0.35f;
        bottomLeft = position - right - up;
        topLeft = position - right + up;
        topRight = position + right + up;
        bottomRight = position + right - up;
        return true;
    }

    private float WorldLengthToLocalX(float worldLength)
    {
        float scale = Mathf.Abs(windowRoot.lossyScale.x);
        return scale < 0.0001f ? worldLength : worldLength / scale;
    }

    private float WorldLengthToLocalY(float worldLength)
    {
        float scale = Mathf.Abs(windowRoot.lossyScale.y);
        return scale < 0.0001f ? worldLength : worldLength / scale;
    }

    private float WorldLengthToLocalZ(float worldLength)
    {
        float scale = Mathf.Abs(windowRoot.lossyScale.z);
        return scale < 0.0001f ? worldLength : worldLength / scale;
    }

    private void CaptureOriginalTransformIfNeeded()
    {
        if (originalCaptured || windowRoot == null)
        {
            return;
        }

        originalPosition = windowRoot.position;
        originalRotation = windowRoot.rotation;
        originalScale = windowRoot.localScale;
        originalCaptured = true;
    }
}
