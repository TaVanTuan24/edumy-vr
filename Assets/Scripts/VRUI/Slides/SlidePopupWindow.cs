using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[DisallowMultipleComponent]
public class SlidePopupWindow : MonoBehaviour
{
    private static readonly Vector3 DefaultWindowScale = new Vector3(0.0014f, 0.0014f, 0.0014f);
    private static readonly Color PanelColor = new Color(0.98f, 0.99f, 1f, 0.98f);
    private static readonly Color HeaderColor = new Color(0.82f, 0.9f, 1f, 0.99f);
    private static readonly Color PrimaryButtonColor = new Color(0.16f, 0.58f, 0.93f, 0.98f);
    private static readonly Color SecondaryButtonColor = new Color(0.92f, 0.95f, 1f, 0.98f);
    private static readonly Color TitleTextColor = new Color(0.05f, 0.08f, 0.13f, 1f);

    [SerializeField] private Transform windowTransform;
    [SerializeField] private Vector2 canvasSize = new Vector2(1100f, 720f);
    [SerializeField] private Vector3 windowScale = new Vector3(0.0014f, 0.0014f, 0.0014f);
    [SerializeField] private bool keepPlacedTransformOnShow = true;
    [SerializeField] private bool autoCreateWindowInEditor = true;
    [SerializeField] private bool showPreviewInEditor = true;
    [SerializeField] private bool autoPlaceInFrontWhenPlaying = false;
    [SerializeField] private bool followViewerWhileVisible = true;
    [SerializeField] private float horizontalOffset = -0.35f;
    [SerializeField] private float additionalHeightOffset = -0.35f;
    [SerializeField] private bool flipForwardToFaceViewer = true;
    [SerializeField] private bool enableDebugLogs = true;

    private Canvas canvas;
    private RectTransform rootRect;
    private TMP_Text titleText;
    private TMP_Text indicatorText;
    private TMP_Text contentText;
    private Button closeButton;
    private Button prevButton;
    private Button nextButton;
    private Button pinButton;

    private readonly List<string> activeSlides = new List<string>();
    private int currentSlideIndex;
    private bool bindingsAdded;
    private bool createdRuntimeWindow;
    private Transform activeViewer;
    private float activeDistance;
    private float activeHeightOffset;
    private SpatialWindow spatialWindow;

    private void Awake()
    {
        if (windowTransform == null)
        {
            Transform existing = transform.Find("SlidePopupWindowRoot");
            if (existing != null)
            {
                windowTransform = existing;
            }
        }
    }

    private void Reset()
    {
        EnsureWindowExists(true);
        if (!Application.isPlaying && windowTransform != null)
        {
            windowTransform.gameObject.SetActive(showPreviewInEditor);
        }
    }

    private void OnValidate()
    {
        if (Application.isPlaying) return;
        if (!autoCreateWindowInEditor) return;
        EnsureWindowExists(true);
        if (windowTransform != null)
        {
            windowTransform.gameObject.SetActive(showPreviewInEditor);
        }
    }

    private void Update()
    {
        if (Application.isPlaying) return;
        if (!autoCreateWindowInEditor) return;

        // Avoid rebuilding preview every editor frame; create only when missing.
        if (windowTransform == null)
        {
            EnsureWindowExists(true);
        }
        if (windowTransform != null)
        {
            windowTransform.gameObject.SetActive(showPreviewInEditor);
        }
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying) return;
        if (spatialWindow == null) return;
        spatialWindow.SetViewer(activeViewer);
    }

    [ContextMenu("Show Preview In Editor")]
    private void ShowPreviewInEditorNow()
    {
        showPreviewInEditor = true;
        EnsureWindowExists(true);
        if (!Application.isPlaying && windowTransform != null)
        {
            windowTransform.gameObject.SetActive(true);
        }
    }

    [ContextMenu("Hide Preview In Editor")]
    private void HidePreviewInEditorNow()
    {
        showPreviewInEditor = false;
        if (!Application.isPlaying && windowTransform != null)
        {
            windowTransform.gameObject.SetActive(false);
        }
    }

    public bool CanHandle(LessonData lesson)
    {
        if (lesson == null) return false;
        string t = (lesson.type ?? string.Empty).Trim().ToLowerInvariant();
        if (t.Contains("slide") || t.Contains("presentation") || t.Contains("ppt") || t.Contains("document")) return true;
        if (lesson.slides != null && lesson.slides.Count > 0) return true;
        if (!string.IsNullOrWhiteSpace(lesson.slideText)) return true;
        if (!string.IsNullOrWhiteSpace(lesson.title) && lesson.title.IndexOf("slide", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    public Transform WindowRootTransform => windowTransform;

    public void PlaceAtAnchor(Transform anchor, bool copyScale = false)
    {
        if (anchor == null) return;

        EnsureWindowExists(false);
        if (windowTransform == null) return;

        windowTransform.position = anchor.position;
        windowTransform.rotation = anchor.rotation;
        if (copyScale)
        {
            windowTransform.localScale = anchor.lossyScale;
        }

        EnsureSpatialWindow();
        spatialWindow?.SetPinned(true, false);
    }

    public void PlaceInFrontOf(Transform viewer, float distance, float heightOffset)
    {
        EnsureWindowExists(false);
        Debug.Log($"[SlidePopupWindow] PlaceInFrontOf invoked viewerNull={viewer == null} distance={distance} heightOffset={heightOffset}");
        if (windowTransform == null || viewer == null) return;
        EnsureSpatialWindow();
        if (spatialWindow != null)
        {
            float totalHeightOffset = heightOffset + additionalHeightOffset;
            spatialWindow.SetViewer(viewer);
            spatialWindow.SetFollowSettings(distance, totalHeightOffset, horizontalOffset);
            spatialWindow.MoveInFrontOfViewer(viewer, distance, totalHeightOffset, horizontalOffset);
            spatialWindow.SetPinned(false, false);
            return;
        }

        PositionWindow(viewer, distance, heightOffset);
    }

    public bool Show(LessonData lesson, Transform viewer, float distance, float heightOffset)
    {
        if (lesson == null) return false;

        Debug.Log($"[SlidePopupWindow] Show invoked id={lesson.id} title='{lesson.title}' type='{lesson.type}'");

        if (enableDebugLogs)
        {
            Debug.Log($"[SlidePopupWindow] Show start lesson id={lesson.id} title='{lesson.title}' type='{lesson.type}' viewerNull={viewer == null} keepPlaced={keepPlacedTransformOnShow}");
        }

        EnsureWindowExists(false);
        if (windowTransform == null || canvas == null)
        {
            if (enableDebugLogs)
            {
                Debug.LogWarning($"[SlidePopupWindow] Show aborted: windowTransformNull={windowTransform == null}, canvasNull={canvas == null}");
            }
            return false;
        }

        EnsureIndependentWorldRoot();

        activeSlides.Clear();
        activeSlides.AddRange(BuildSlidesForLesson(lesson));
        currentSlideIndex = 0;
        activeViewer = viewer;
        activeDistance = distance;
        activeHeightOffset = heightOffset;

        if (enableDebugLogs)
        {
            Debug.Log($"[SlidePopupWindow] slides prepared count={activeSlides.Count}");
        }

        if (titleText != null)
        {
            titleText.text = string.IsNullOrWhiteSpace(lesson.title) ? "Slide" : lesson.title;
            titleText.color = TitleTextColor;
        }
        bool shouldAutoPlace = Application.isPlaying
            || !keepPlacedTransformOnShow
            || createdRuntimeWindow
            || autoPlaceInFrontWhenPlaying;
        if (shouldAutoPlace)
        {
            PlaceInFrontOf(viewer, distance, heightOffset);
        }

        // Recover from broken inspector values (e.g. zero scale) that make the world-space canvas invisible.
        if (IsNearlyZeroScale(windowTransform.localScale))
        {
            Vector3 recovered = SanitizeWindowScale(windowScale);
            windowScale = recovered;
            windowTransform.localScale = recovered;
            if (enableDebugLogs)
            {
                Debug.LogWarning($"[SlidePopupWindow] Recovered invalid runtime scale. New scale={recovered}");
            }
        }

        if (canvas != null)
        {
            canvas.enabled = true;
        }

        windowTransform.gameObject.SetActive(true);
        EnsureSpatialWindow();
        spatialWindow?.SetPinned(false, false);
        UpdatePinButtonState();
        Render();
        EnsureVisibleInPlay(viewer, distance, heightOffset);

        if (enableDebugLogs)
        {
            Debug.Log($"[SlidePopupWindow] Show success active={windowTransform.gameObject.activeSelf} pos={windowTransform.position} rot={windowTransform.rotation.eulerAngles} scale={windowTransform.localScale}");
        }
        return true;
    }

    public void HideWindow()
    {
        if (windowTransform != null)
        {
            windowTransform.gameObject.SetActive(false);
        }

        activeViewer = null;
    }

    private void EnsureWindowExists(bool forceEditorCreation)
    {
        if (windowTransform == null)
        {
            if (!Application.isPlaying && !forceEditorCreation) return;

            GameObject root = new GameObject("SlidePopupWindowRoot", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            windowTransform = root.transform;
            createdRuntimeWindow = true;
            if (enableDebugLogs)
            {
                Debug.Log("[SlidePopupWindow] Created runtime window root.");
            }
        }

        windowTransform = EnsureRectTransformRoot(windowTransform);
        if (windowTransform == null)
        {
            Debug.LogError("[SlidePopupWindow] Could not create a valid RectTransform root.");
            return;
        }

        if (canvas == null)
        {
            canvas = windowTransform.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = windowTransform.gameObject.AddComponent<Canvas>();
            }
            canvas.renderMode = RenderMode.WorldSpace;

            if (windowTransform.GetComponent<GraphicRaycaster>() == null)
            {
                windowTransform.gameObject.AddComponent<GraphicRaycaster>();
            }

            CanvasScaler scaler = windowTransform.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = windowTransform.gameObject.AddComponent<CanvasScaler>();
            }
            scaler.dynamicPixelsPerUnit = 10f;
        }

        XRRuntimeUiHelper.EnsureWorldSpaceCanvasInteraction(windowTransform.gameObject);
        XRRuntimeUiHelper.EnsureEventSystemSupportsXR();

        rootRect = canvas.GetComponent<RectTransform>();
        if (rootRect == null)
        {
            rootRect = windowTransform as RectTransform;
        }
        if (rootRect == null)
        {
            Debug.LogError("[SlidePopupWindow] Missing RectTransform on canvas root.");
            return;
        }
        rootRect.sizeDelta = canvasSize;
        Vector3 safeScale = SanitizeWindowScale(windowScale);
        if (safeScale != windowScale)
        {
            windowScale = safeScale;
            if (enableDebugLogs)
            {
                Debug.LogWarning($"[SlidePopupWindow] Invalid windowScale detected. Auto-correct to {safeScale}");
            }
        }
        windowTransform.localScale = safeScale;

        if (enableDebugLogs && Application.isPlaying)
        {
            Debug.Log($"[SlidePopupWindow] EnsureWindowExists done size={canvasSize.ToString("F2")} scale={windowScale.ToString("F6")}");
        }

        BuildUiIfMissing();
        BindEventsOnce();
        EnsureSpatialWindow();
    }

    private void EnsureSpatialWindow()
    {
        if (windowTransform == null)
        {
            return;
        }

        if (spatialWindow == null)
        {
            spatialWindow = windowTransform.GetComponent<SpatialWindow>();
            if (spatialWindow == null)
            {
                spatialWindow = windowTransform.gameObject.AddComponent<SpatialWindow>();
            }
        }

        spatialWindow.ConfigureWindowRoot(windowTransform, rootRect != null ? rootRect : windowTransform);
        spatialWindow.SetViewer(activeViewer != null ? activeViewer : (Camera.main != null ? Camera.main.transform : null));
        spatialWindow.SetFollowSettings(activeDistance > 0f ? activeDistance : 1.7f, activeHeightOffset + additionalHeightOffset, horizontalOffset);
        spatialWindow.SetAllowResize(false);
        spatialWindow.SetChromeVisible(false);
        spatialWindow.SetInteractionsEnabled(false);
        if (Application.isPlaying)
        {
            spatialWindow.RemoveChrome();
        }
    }

    private void BuildUiIfMissing()
    {
        Transform panel = windowTransform.Find("Panel");
        if (panel == null)
        {
            panel = CreatePanel(windowTransform).transform;
        }

        EnsurePanelChrome(panel);

        RectTransform panelRect = panel.GetComponent<RectTransform>();

        titleText = FindOrCreateText(panel, "Title", new Vector2(0.05f, 0.88f), new Vector2(0.77f, 0.97f), 38, FontStyles.Bold);
        titleText.color = TitleTextColor;
        indicatorText = FindOrCreateText(panel, "Indicator", new Vector2(0.05f, 0.815f), new Vector2(0.47f, 0.875f), 23, FontStyles.Normal);
        contentText = FindOrCreateText(panel, "Content", new Vector2(0.05f, 0.15f), new Vector2(0.95f, 0.78f), 28, FontStyles.Normal);
        contentText.textWrappingMode = TextWrappingModes.Normal;
        contentText.alignment = TextAlignmentOptions.TopLeft;
        contentText.color = new Color(0.15f, 0.24f, 0.38f, 1f);
        indicatorText.color = new Color(0.27f, 0.39f, 0.56f, 1f);

        closeButton = FindOrCreateButton(panelRect, "CloseButton", "Close", new Vector2(0.79f, 0.885f), new Vector2(0.95f, 0.97f));
        pinButton = FindOrCreateButton(panelRect, "PinButton", "📌", new Vector2(0.69f, 0.885f), new Vector2(0.78f, 0.97f));
        prevButton = FindOrCreateButton(panelRect, "PrevButton", "Prev", new Vector2(0.05f, 0.03f), new Vector2(0.25f, 0.11f));
        nextButton = FindOrCreateButton(panelRect, "NextButton", "Next", new Vector2(0.27f, 0.03f), new Vector2(0.47f, 0.11f));
        SetButtonColor(closeButton, SecondaryButtonColor);
        SetButtonColor(pinButton, SecondaryButtonColor);
        SetButtonColor(prevButton, SecondaryButtonColor);
        SetButtonColor(nextButton, PrimaryButtonColor);
    }

    private void BindEventsOnce()
    {
        if (bindingsAdded) return;

        if (closeButton != null) closeButton.onClick.AddListener(HideWindow);
        if (pinButton != null) pinButton.onClick.AddListener(TogglePinnedState);
        if (prevButton != null) prevButton.onClick.AddListener(Prev);
        if (nextButton != null) nextButton.onClick.AddListener(Next);

        bindingsAdded = true;
        UpdatePinButtonState();
    }

    private void TogglePinnedState()
    {
        EnsureSpatialWindow();
        if (spatialWindow == null)
        {
            return;
        }

        spatialWindow.TogglePinned();
        UpdatePinButtonState();
    }

    private void UpdatePinButtonState()
    {
        if (pinButton == null || spatialWindow == null)
        {
            return;
        }

        TextMeshProUGUI label = pinButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            label.text = spatialWindow.IsPinned ? "📌" : "Pin";
        }

        SetButtonColor(pinButton, spatialWindow.IsPinned ? PrimaryButtonColor : SecondaryButtonColor);
    }

    private void Prev()
    {
        if (activeSlides.Count == 0) return;
        currentSlideIndex = Mathf.Max(0, currentSlideIndex - 1);
        Render();
    }

    private void Next()
    {
        if (activeSlides.Count == 0) return;
        currentSlideIndex = Mathf.Min(activeSlides.Count - 1, currentSlideIndex + 1);
        Render();
    }

    private void Render()
    {
        int total = Mathf.Max(1, activeSlides.Count);
        currentSlideIndex = Mathf.Clamp(currentSlideIndex, 0, total - 1);

        if (indicatorText != null)
        {
            indicatorText.text = $"Slide {currentSlideIndex + 1}/{total}";
        }

        if (contentText != null)
        {
            contentText.text = activeSlides.Count > 0 ? activeSlides[currentSlideIndex] : "No slide content.";
        }

        if (prevButton != null) prevButton.interactable = currentSlideIndex > 0;
        if (nextButton != null) nextButton.interactable = currentSlideIndex < total - 1;
    }

    private List<string> BuildSlidesForLesson(LessonData lesson)
    {
        List<string> slides = new List<string>();

        if (lesson != null && lesson.slides != null)
        {
            foreach (string page in lesson.slides)
            {
                if (string.IsNullOrWhiteSpace(page)) continue;
                slides.Add(page.Trim());
            }
        }

        if (slides.Count == 0 && lesson != null && !string.IsNullOrWhiteSpace(lesson.slideText))
        {
            string[] pages = lesson.slideText.Split(new[] { "\n---\n", "\r\n---\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string p in pages)
            {
                string page = p.Trim();
                if (!string.IsNullOrWhiteSpace(page)) slides.Add(page);
            }
        }

        if (slides.Count == 0)
        {
            slides.Add("No slide data from API.");
        }

        return slides;
    }

    private void PositionWindow(Transform viewer, float distance, float heightOffset)
    {
        if (windowTransform == null || viewer == null) return;

        Vector3 forward = viewer.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
        {
            forward = viewer.forward;
        }
        forward.Normalize();

        Vector3 right = viewer.right;
        right.y = 0f;
        if (right.sqrMagnitude < 0.001f)
        {
            right = Vector3.right;
        }
        right.Normalize();

        float totalHeightOffset = heightOffset + additionalHeightOffset;

        windowTransform.position = viewer.position
            + forward * Mathf.Max(0.2f, distance)
            + right * horizontalOffset
            + Vector3.up * totalHeightOffset;

        Vector3 lookTarget = viewer.position + Vector3.up * totalHeightOffset;
        Vector3 dir = lookTarget - windowTransform.position;
        if (dir.sqrMagnitude > 0.001f)
        {
            Quaternion look = Quaternion.LookRotation(dir.normalized, Vector3.up);
            if (flipForwardToFaceViewer)
            {
                look *= Quaternion.Euler(0f, 180f, 0f);
            }

            windowTransform.rotation = look;
        }
    }

    private static GameObject CreatePanel(Transform parent)
    {
        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);

        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = panel.GetComponent<Image>();
        image.color = PanelColor;
        return panel;
    }

    private static void EnsurePanelChrome(Transform panel)
    {
        Image panelImage = panel.GetComponent<Image>();
        if (panelImage != null)
        {
            panelImage.color = PanelColor;
        }

        Image headerBg = FindOrCreateFill(panel, "HeaderBg", new Vector2(0f, 0.82f), new Vector2(1f, 1f), HeaderColor);
        if (headerBg != null)
        {
            headerBg.transform.SetAsFirstSibling();
        }
    }

    private static Image FindOrCreateFill(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        Transform existing = parent.Find(name);
        if (existing == null)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            existing = go.transform;
        }

        RectTransform rect = existing.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = existing.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static TMP_Text FindOrCreateText(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, float fontSize, FontStyles style)
    {
        Transform existing = parent.Find(name);
        if (existing == null)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            existing = go.transform;
        }

        RectTransform rect = existing.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        TextMeshProUGUI text = existing.GetComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Left;
        return text;
    }

    private static Button FindOrCreateButton(RectTransform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax)
    {
        Transform existing = parent.Find(name);
        if (existing == null)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            existing = go.transform;

            GameObject textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(existing, false);
        }

        RectTransform rect = existing.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = existing.GetComponent<Image>();
        image.color = SecondaryButtonColor;

        Button button = existing.GetComponent<Button>();

        TextMeshProUGUI text = existing.GetComponentInChildren<TextMeshProUGUI>(true);
        text.text = label;
        text.color = new Color(0.15f, 0.3f, 0.49f, 1f);
        text.fontSize = 24f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;

        RectTransform textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return button;
    }

    private static void SetButtonColor(Button button, Color color)
    {
        if (button == null) return;
        Image image = button.GetComponent<Image>();
        if (image != null)
        {
            image.color = color;
        }
    }

    private static Transform EnsureRectTransformRoot(Transform root)
    {
        if (root == null) return null;

        if (root is RectTransform)
        {
            return root;
        }

        Transform parent = root.parent;
        GameObject replacement = new GameObject(root.name, typeof(RectTransform));
        replacement.transform.SetParent(parent, false);
        replacement.transform.position = root.position;
        replacement.transform.rotation = root.rotation;
        replacement.transform.localScale = root.localScale;

        while (root.childCount > 0)
        {
            root.GetChild(0).SetParent(replacement.transform, true);
        }

        if (Application.isPlaying)
        {
            Destroy(root.gameObject);
        }
        else
        {
            DestroyImmediate(root.gameObject);
        }

        return replacement.transform;
    }

    private void EnsureVisibleInPlay(Transform viewer, float distance, float heightOffset)
    {
        if (!Application.isPlaying || windowTransform == null) return;

        if (!windowTransform.gameObject.activeSelf)
        {
            windowTransform.gameObject.SetActive(true);
        }

        if (IsNearlyZeroScale(windowTransform.localScale) || windowTransform.lossyScale.sqrMagnitude < 0.00000001f)
        {
            windowTransform.localScale = SanitizeWindowScale(windowScale);
        }

        if (viewer == null) return;

        Vector3 toWindow = windowTransform.position - viewer.position;
        float distanceToViewer = toWindow.magnitude;
        bool invalidDistance = distanceToViewer < 0.05f || distanceToViewer > 30f;

        bool behindViewer = false;
        if (distanceToViewer > 0.001f)
        {
            behindViewer = Vector3.Dot(viewer.forward.normalized, toWindow.normalized) < 0.15f;
        }

        if (invalidDistance || behindViewer)
        {
            PositionWindow(viewer, distance, heightOffset);
        }
    }

    private void EnsureIndependentWorldRoot()
    {
        if (!Application.isPlaying || windowTransform == null) return;
        if (windowTransform.parent == null) return;

        Vector3 lossy = windowTransform.lossyScale;
        bool brokenByParentScale = IsNearlyZeroScale(lossy) || lossy.sqrMagnitude < 0.00000001f;
        if (!brokenByParentScale) return;

        windowTransform.SetParent(null, true);
        if (enableDebugLogs)
        {
            Debug.LogWarning("[SlidePopupWindow] Detached popup root from zero-scale parent to keep it visible.");
        }
    }

    private static bool IsNearlyZeroScale(Vector3 scale)
    {
        const float epsilon = 0.000001f;
        return Mathf.Abs(scale.x) < epsilon || Mathf.Abs(scale.y) < epsilon || Mathf.Abs(scale.z) < epsilon;
    }

    private static Vector3 SanitizeWindowScale(Vector3 raw)
    {
        const float minAbs = 0.0001f;

        float x = Mathf.Abs(raw.x) < minAbs ? DefaultWindowScale.x : raw.x;
        float y = Mathf.Abs(raw.y) < minAbs ? DefaultWindowScale.y : raw.y;
        float z = Mathf.Abs(raw.z) < minAbs ? DefaultWindowScale.z : raw.z;

        if (float.IsNaN(x) || float.IsInfinity(x)) x = DefaultWindowScale.x;
        if (float.IsNaN(y) || float.IsInfinity(y)) y = DefaultWindowScale.y;
        if (float.IsNaN(z) || float.IsInfinity(z)) z = DefaultWindowScale.z;

        return new Vector3(x, y, z);
    }
}

