using UnityEngine;

[DisallowMultipleComponent]
public class VideoWindowModeController : MonoBehaviour
{
    public enum VideoDisplayMode
    {
        Fixed,
        Floating
    }

    [SerializeField] private VideoPopupWindow videoPopupWindow;
    [SerializeField] private Transform viewer;
    [SerializeField] private VideoDisplayMode defaultMode = VideoDisplayMode.Fixed;
    [SerializeField] private float floatingDistance = 3f;
    [SerializeField] private float floatingHeightOffset = 0.02f;

    private Vector3 fixedPosition;
    private Quaternion fixedRotation;
    private Vector3 fixedScale;
    private bool fixedPoseCaptured;
    private GameObject blackoutScreen;

    public VideoDisplayMode CurrentMode { get; private set; }
    public bool IsFloatingMode => CurrentMode == VideoDisplayMode.Floating;
    public float FloatingDistance => floatingDistance;
    public float FloatingHeightOffset => floatingHeightOffset;

    private void Awake()
    {
        if (videoPopupWindow == null)
        {
            videoPopupWindow = GetComponent<VideoPopupWindow>();
        }

        CaptureFixedPoseIfNeeded();
        EnsureBlackoutScreen();
        ApplyMode(defaultMode, forcePlaceFloating: false);
    }

    public void BindViewer(Transform targetViewer)
    {
        viewer = targetViewer;
        SpatialWindow spatialWindow = videoPopupWindow != null ? videoPopupWindow.SpatialWindow : null;
        if (spatialWindow != null)
        {
            spatialWindow.SetViewer(targetViewer);
        }
    }

    public void ToggleMode()
    {
        ApplyMode(IsFloatingMode ? VideoDisplayMode.Fixed : VideoDisplayMode.Floating, forcePlaceFloating: true);
    }

    public void SetFloatingPlacement(float distance, float heightOffset)
    {
        floatingDistance = Mathf.Max(0.5f, distance);
        floatingHeightOffset = heightOffset;

        if (IsFloatingMode)
        {
            ApplyMode(VideoDisplayMode.Floating, forcePlaceFloating: true);
        }
    }

    public void ApplyCurrentMode(bool forcePlaceFloating)
    {
        ApplyMode(CurrentMode == 0 && !fixedPoseCaptured ? defaultMode : CurrentMode, forcePlaceFloating);
    }

    public void ApplyMode(VideoDisplayMode mode, bool forcePlaceFloating)
    {
        if (videoPopupWindow == null)
        {
            return;
        }

        CaptureFixedPoseIfNeeded();
        EnsureBlackoutScreen();

        SpatialWindow spatialWindow = videoPopupWindow.SpatialWindow;
        if (spatialWindow != null && viewer != null)
        {
            spatialWindow.SetViewer(viewer);
        }

        CurrentMode = mode;

        if (mode == VideoDisplayMode.Floating)
        {
            if (blackoutScreen != null)
            {
                blackoutScreen.SetActive(true);
            }

            videoPopupWindow.SetSpatialInteractionEnabled(true);
            videoPopupWindow.SetChromeVisible(true);

            if (viewer != null && (forcePlaceFloating || !videoPopupWindow.IsWindowVisible))
            {
                videoPopupWindow.PlaceInFrontOf(viewer, floatingDistance, floatingHeightOffset);
            }
        }
        else
        {
            if (blackoutScreen != null)
            {
                blackoutScreen.SetActive(false);
            }

            videoPopupWindow.SetSpatialInteractionEnabled(false);
            videoPopupWindow.SetChromeVisible(false);
            videoPopupWindow.SetPinned(true, false);
            videoPopupWindow.SetWindowPose(fixedPosition, fixedRotation, fixedScale);
        }
    }

    public void HandleVideoStopped()
    {
        if (blackoutScreen != null && !IsFloatingMode)
        {
            blackoutScreen.SetActive(false);
        }
    }

    private void CaptureFixedPoseIfNeeded()
    {
        if (fixedPoseCaptured || videoPopupWindow == null || videoPopupWindow.WindowRootTransform == null)
        {
            return;
        }

        Transform windowRoot = videoPopupWindow.WindowRootTransform;
        fixedPosition = windowRoot.position;
        fixedRotation = windowRoot.rotation;
        fixedScale = windowRoot.localScale;
        fixedPoseCaptured = true;
    }

    private void EnsureBlackoutScreen()
    {
        if (blackoutScreen != null || videoPopupWindow == null || videoPopupWindow.WindowRootTransform == null)
        {
            return;
        }

        blackoutScreen = GameObject.CreatePrimitive(PrimitiveType.Quad);
        blackoutScreen.name = "FixedVideoBlackoutScreen";
        blackoutScreen.transform.SetParent(transform, false);
        blackoutScreen.transform.position = fixedPosition;
        blackoutScreen.transform.rotation = fixedRotation;
        blackoutScreen.transform.localScale = fixedScale;

        Renderer renderer = blackoutScreen.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material material = new Material(Shader.Find("Unlit/Color"));
            material.color = Color.black;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        Collider collider = blackoutScreen.GetComponent<Collider>();
        if (collider != null)
        {
            if (Application.isPlaying)
            {
                Destroy(collider);
            }
            else
            {
                DestroyImmediate(collider);
            }
        }

        blackoutScreen.SetActive(false);
    }
}
