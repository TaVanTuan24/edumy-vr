using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[DisallowMultipleComponent]
public class SlidePopupWindow : MonoBehaviour
{
    [SerializeField] private Transform windowTransform;
    [SerializeField] private Vector2 canvasSize = new Vector2(1100f, 720f);
    [SerializeField] private Vector3 windowScale = new Vector3(0.0014f, 0.0014f, 0.0014f);
    [SerializeField] private bool keepPlacedTransformOnShow = true;
    [SerializeField] private bool autoCreateWindowInEditor = true;
    [SerializeField] private bool showPreviewInEditor = true;
    [SerializeField] private bool autoPlaceInFrontWhenPlaying = false;

    private Canvas canvas;
    private RectTransform rootRect;
    private TMP_Text titleText;
    private TMP_Text indicatorText;
    private TMP_Text contentText;
    private Button closeButton;
    private Button prevButton;
    private Button nextButton;

    private readonly List<string> activeSlides = new List<string>();
    private int currentSlideIndex;
    private bool bindingsAdded;
    private bool createdRuntimeWindow;

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

        EnsureWindowExists(true);
        if (windowTransform != null)
        {
            windowTransform.gameObject.SetActive(showPreviewInEditor);
        }
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
        if (t.Contains("slide") || t.Contains("presentation")) return true;
        if (lesson.slides != null && lesson.slides.Count > 0) return true;
        if (!string.IsNullOrWhiteSpace(lesson.slideText)) return true;
        return false;
    }

    public bool Show(LessonData lesson, Transform viewer, float distance, float heightOffset)
    {
        if (lesson == null) return false;

        EnsureWindowExists(false);
        if (windowTransform == null || canvas == null) return false;

        activeSlides.Clear();
        activeSlides.AddRange(BuildSlidesForLesson(lesson));
        currentSlideIndex = 0;

        if (titleText != null)
        {
            titleText.text = string.IsNullOrWhiteSpace(lesson.title) ? "Slide" : lesson.title;
        }
        bool shouldAutoPlace = !keepPlacedTransformOnShow
            || (createdRuntimeWindow && Application.isPlaying)
            || (Application.isPlaying && autoPlaceInFrontWhenPlaying);
        if (shouldAutoPlace)
        {
            PositionWindow(viewer, distance, heightOffset);
        }

        windowTransform.gameObject.SetActive(true);
        Render();
        return true;
    }

    public void HideWindow()
    {
        if (windowTransform != null)
        {
            windowTransform.gameObject.SetActive(false);
        }
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
        windowTransform.localScale = windowScale;

        BuildUiIfMissing();
        BindEventsOnce();
    }

    private void BuildUiIfMissing()
    {
        Transform panel = windowTransform.Find("Panel");
        if (panel == null)
        {
            panel = CreatePanel(windowTransform).transform;
        }

        RectTransform panelRect = panel.GetComponent<RectTransform>();

        titleText = FindOrCreateText(panel, "Title", new Vector2(0.04f, 0.86f), new Vector2(0.96f, 0.97f), 42, FontStyles.Bold);
        indicatorText = FindOrCreateText(panel, "Indicator", new Vector2(0.04f, 0.79f), new Vector2(0.45f, 0.85f), 28, FontStyles.Normal);
        contentText = FindOrCreateText(panel, "Content", new Vector2(0.04f, 0.14f), new Vector2(0.96f, 0.77f), 30, FontStyles.Normal);
        contentText.textWrappingMode = TextWrappingModes.Normal;
        contentText.alignment = TextAlignmentOptions.TopLeft;

        closeButton = FindOrCreateButton(panelRect, "CloseButton", "Close", new Vector2(0.84f, 0.86f), new Vector2(0.96f, 0.96f));
        prevButton = FindOrCreateButton(panelRect, "PrevButton", "Prev", new Vector2(0.04f, 0.03f), new Vector2(0.22f, 0.11f));
        nextButton = FindOrCreateButton(panelRect, "NextButton", "Next", new Vector2(0.24f, 0.03f), new Vector2(0.42f, 0.11f));
    }

    private void BindEventsOnce()
    {
        if (bindingsAdded) return;

        if (closeButton != null) closeButton.onClick.AddListener(HideWindow);
        if (prevButton != null) prevButton.onClick.AddListener(Prev);
        if (nextButton != null) nextButton.onClick.AddListener(Next);

        bindingsAdded = true;
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
            contentText.text = activeSlides.Count > 0 ? activeSlides[currentSlideIndex] : "Khong co noi dung slide.";
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

        windowTransform.position = viewer.position + forward * Mathf.Max(0.2f, distance) + Vector3.up * heightOffset;

        Vector3 lookTarget = viewer.position + Vector3.up * heightOffset;
        Vector3 dir = lookTarget - windowTransform.position;
        if (dir.sqrMagnitude > 0.001f)
        {
            windowTransform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
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
        image.color = new Color(0.08f, 0.1f, 0.12f, 0.94f);
        return panel;
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
        image.color = new Color(0.18f, 0.38f, 0.62f, 0.97f);

        Button button = existing.GetComponent<Button>();

        TextMeshProUGUI text = existing.GetComponentInChildren<TextMeshProUGUI>(true);
        text.text = label;
        text.color = Color.white;
        text.fontSize = 26f;
        text.alignment = TextAlignmentOptions.Center;

        RectTransform textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return button;
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
}
