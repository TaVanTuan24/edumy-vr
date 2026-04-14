using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class CourseToggleController : MonoBehaviour
{
    [Header("Toggle UI")]
    [SerializeField] private VisualTreeAsset toggleViewTree;
    [SerializeField] private StyleSheet toggleStyle;
    [SerializeField] private UIDocument toggleDocumentOverride;

    [Header("Course Selection Target")]
    [SerializeField] private CourseSelectionUI courseSelectionUI;
    [SerializeField] private UIDocument courseSelectionDocument;
    [SerializeField] private string courseSelectionRootName = "lesson-selection-window-root";

    [Header("Open/Close")]
    [SerializeField] private bool startOpened = true;
    [SerializeField] private bool animate = true;
    [SerializeField, Min(0.05f)] private float animationDuration = 0.22f;
    [SerializeField, Min(0f)] private float slideDistancePixels = 36f;

    [Header("Optional VR Position")]
    [SerializeField] private bool positionRightOfViewerOnOpen = true;
    [SerializeField] private Transform viewerTransform;
    [SerializeField, Min(0.3f)] private float panelDistance = 1.1f;
    [SerializeField] private float panelHeightOffset = 0.02f;
    [SerializeField] private float panelRightOffset = 0.22f;

    [Header("Screen Overlay Fallback")]
    [SerializeField] private bool forceScreenOverlayButton = true;
    [SerializeField, Min(8f)] private float overlayMargin = 18f;
    [SerializeField, Min(36f)] private float overlaySize = 56f;

    private UIDocument toggleDocument;
    private Button toggleButton;
    private VisualElement toggleRoot;
    private VisualElement courseRoot;
    private bool isOpen;
    private Coroutine transitionRoutine;
    private Coroutine ensureUiRoutine;
    private float nextHealthCheckTime;
    private GUIStyle overlayButtonStyle;

    private void Awake()
    {
        toggleDocument = toggleDocumentOverride != null ? toggleDocumentOverride : GetComponent<UIDocument>();
#if UNITY_EDITOR
        AutoAssignInEditor();
#endif
        ResolveCourseTarget();
        isOpen = startOpened;
    }

    private void OnEnable()
    {
        if (ensureUiRoutine != null)
        {
            StopCoroutine(ensureUiRoutine);
        }
        ensureUiRoutine = StartCoroutine(InitializeAfterUiReady());
        ResolveCourseTarget();
        nextHealthCheckTime = 0f;
    }

    private void OnDisable()
    {
        if (ensureUiRoutine != null)
        {
            StopCoroutine(ensureUiRoutine);
            ensureUiRoutine = null;
        }

        if (toggleButton != null)
        {
            toggleButton.clicked -= OnToggleClicked;
        }
    }

    private IEnumerator InitializeAfterUiReady()
    {
        // CourseSelectionUI builds in Start() and clears root; wait so toggle is injected after that.
        yield return null;
        yield return null;

        ResolveCourseTarget();
        BuildToggleUi();
        ApplyImmediateState(isOpen);
        UpdateToggleVisualState();
        ensureUiRoutine = null;
    }

    private void LateUpdate()
    {
        if (isOpen && positionRightOfViewerOnOpen)
        {
            PlaceCoursePanelNearViewer();
        }

        if (Time.unscaledTime < nextHealthCheckTime)
        {
            return;
        }

        nextHealthCheckTime = Time.unscaledTime + 0.35f;
        EnsureToggleStillPresent();
    }

    private void EnsureToggleStillPresent()
    {
        if (toggleDocument == null)
        {
            return;
        }

        VisualElement root = toggleDocument.rootVisualElement;
        if (root == null)
        {
            return;
        }

        if (root.Q<Button>("course-toggle-button") == null)
        {
            BuildToggleUi();
            ApplyImmediateState(isOpen);
            UpdateToggleVisualState();
            return;
        }

        toggleRoot = root.Q<VisualElement>("course-toggle-root");
        toggleButton = root.Q<Button>("course-toggle-button");
        EnforceBottomRightPlacement();
        EnforceToggleButtonVisual();
    }

    private void BuildToggleUi()
    {
        if (toggleDocument == null)
        {
            return;
        }

        VisualElement root = toggleDocument.rootVisualElement;

        if (toggleStyle != null && !root.styleSheets.Contains(toggleStyle))
        {
            root.styleSheets.Add(toggleStyle);
        }

        if (toggleViewTree == null)
        {
            Debug.LogError("[CourseToggleController] Missing toggleViewTree.");
            return;
        }

        if (root.Q<VisualElement>("course-toggle-root") == null)
        {
            toggleViewTree.CloneTree(root);
        }

        toggleRoot = root.Q<VisualElement>("course-toggle-root");
        toggleButton = root.Q<Button>("course-toggle-button");
        if (toggleButton == null)
        {
            CreateRuntimeFallbackToggle(root);
            toggleRoot = root.Q<VisualElement>("course-toggle-root");
            toggleButton = root.Q<Button>("course-toggle-button");
            if (toggleButton == null)
            {
                Debug.LogError("[CourseToggleController] Toggle button not found.");
                return;
            }
        }

        EnforceBottomRightPlacement();
        EnforceToggleButtonVisual();

        toggleButton.clicked -= OnToggleClicked;
        toggleButton.clicked += OnToggleClicked;
    }

    private void CreateRuntimeFallbackToggle(VisualElement root)
    {
        var fallbackRoot = new VisualElement { name = "course-toggle-root" };
        fallbackRoot.style.position = Position.Absolute;
        fallbackRoot.style.right = 18f;
        fallbackRoot.style.bottom = 18f;
        fallbackRoot.style.width = 64f;
        fallbackRoot.style.height = 64f;
        fallbackRoot.style.justifyContent = Justify.Center;
        fallbackRoot.style.alignItems = Align.Center;
        fallbackRoot.pickingMode = PickingMode.Position;

        var fallbackButton = new Button { name = "course-toggle-button", text = "Menu" };
        fallbackButton.style.width = 56f;
        fallbackButton.style.height = 56f;
        fallbackButton.style.unityFontStyleAndWeight = FontStyle.Bold;
        fallbackButton.style.backgroundColor = new Color(0.96f, 0.98f, 1f, 1f);
        fallbackButton.style.color = new Color(0.08f, 0.33f, 0.68f, 1f);
        fallbackButton.style.borderTopLeftRadius = 16f;
        fallbackButton.style.borderTopRightRadius = 16f;
        fallbackButton.style.borderBottomLeftRadius = 16f;
        fallbackButton.style.borderBottomRightRadius = 16f;
        fallbackButton.style.borderTopWidth = 1f;
        fallbackButton.style.borderRightWidth = 1f;
        fallbackButton.style.borderBottomWidth = 1f;
        fallbackButton.style.borderLeftWidth = 1f;
        fallbackButton.style.borderTopColor = new Color(0.42f, 0.62f, 0.87f, 1f);
        fallbackButton.style.borderRightColor = new Color(0.42f, 0.62f, 0.87f, 1f);
        fallbackButton.style.borderBottomColor = new Color(0.42f, 0.62f, 0.87f, 1f);
        fallbackButton.style.borderLeftColor = new Color(0.42f, 0.62f, 0.87f, 1f);

        fallbackRoot.Add(fallbackButton);
        root.Add(fallbackRoot);
    }

    private void EnforceBottomRightPlacement()
    {
        if (toggleRoot == null)
        {
            return;
        }

        toggleRoot.style.position = Position.Absolute;
        toggleRoot.style.right = 18f;
        toggleRoot.style.bottom = 18f;
        toggleRoot.style.left = StyleKeyword.Auto;
        toggleRoot.style.top = StyleKeyword.Auto;
        toggleRoot.style.width = 64f;
        toggleRoot.style.height = 64f;
        toggleRoot.style.display = DisplayStyle.Flex;
        toggleRoot.style.visibility = Visibility.Visible;
        toggleRoot.style.opacity = 1f;
        toggleRoot.BringToFront();
    }

    private void EnforceToggleButtonVisual()
    {
        if (toggleButton == null)
        {
            return;
        }

        toggleButton.style.width = 56f;
        toggleButton.style.height = 56f;
        toggleButton.style.display = DisplayStyle.Flex;
        toggleButton.style.visibility = Visibility.Visible;
        toggleButton.style.opacity = 1f;
        toggleButton.style.backgroundColor = isOpen
            ? new Color(0.13f, 0.48f, 0.91f, 1f)
            : new Color(0.96f, 0.98f, 1f, 1f);
        toggleButton.style.color = isOpen
            ? new Color(1f, 1f, 1f, 1f)
            : new Color(0.08f, 0.33f, 0.68f, 1f);

        if (string.IsNullOrWhiteSpace(toggleButton.text))
        {
            toggleButton.text = "Menu";
        }
    }

    private void ResolveCourseTarget()
    {
        if (courseSelectionUI == null)
        {
            courseSelectionUI = FindAnyObjectByType<CourseSelectionUI>();
        }

        if (courseSelectionDocument == null && courseSelectionUI != null)
        {
            courseSelectionDocument = courseSelectionUI.GetComponent<UIDocument>();
        }

        courseRoot = null;
        if (courseSelectionDocument != null)
        {
            VisualElement root = courseSelectionDocument.rootVisualElement;
            if (root != null)
            {
                courseRoot = string.IsNullOrWhiteSpace(courseSelectionRootName)
                    ? root
                    : root.Q<VisualElement>(courseSelectionRootName);

                if (courseRoot == null)
                {
                    courseRoot = root.Q<VisualElement>("lesson-selection-window");
                }
            }
        }
    }

    private void OnToggleClicked()
    {
        ResolveCourseTarget();
        SetOpen(!isOpen);
    }

    public void SetOpen(bool open)
    {
        isOpen = open;
        UpdateToggleVisualState();

        if (positionRightOfViewerOnOpen && open)
        {
            PlaceCoursePanelNearViewer();
        }

        if (animate && courseRoot != null)
        {
            if (transitionRoutine != null)
            {
                StopCoroutine(transitionRoutine);
            }
            transitionRoutine = StartCoroutine(AnimateCourseRoot(open));
        }
        else
        {
            ApplyImmediateState(open);
        }
    }

    private void ApplyImmediateState(bool open)
    {
        if (courseSelectionDocument != null && courseSelectionDocument.rootVisualElement != null)
        {
            VisualElement docRoot = courseSelectionDocument.rootVisualElement;
            docRoot.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            docRoot.style.opacity = open ? 1f : 0f;
        }

        if (courseRoot != null)
        {
            courseRoot.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            courseRoot.style.opacity = open ? 1f : 0f;
            SetTranslateX(courseRoot, 0f);
            return;
        }

        // Do not disable GameObject here; the toggle icon lives on the same document and must stay visible.
    }

    private IEnumerator AnimateCourseRoot(bool open)
    {
        if (courseSelectionDocument != null && courseSelectionDocument.rootVisualElement != null)
        {
            VisualElement docRoot = courseSelectionDocument.rootVisualElement;
            if (open)
            {
                docRoot.style.display = DisplayStyle.Flex;
                docRoot.style.opacity = 1f;
            }
            else
            {
                docRoot.style.opacity = 0f;
            }
        }

        if (courseRoot == null)
        {
            if (!open && courseSelectionDocument != null && courseSelectionDocument.rootVisualElement != null)
            {
                courseSelectionDocument.rootVisualElement.style.display = DisplayStyle.None;
            }
            yield break;
        }

        float duration = Mathf.Max(0.05f, animationDuration);
        float elapsed = 0f;

        if (open)
        {
            courseRoot.style.display = DisplayStyle.Flex;
            courseRoot.style.opacity = 0f;
            SetTranslateX(courseRoot, slideDistancePixels);
        }

        float fromOpacity = courseRoot.resolvedStyle.opacity;
        float toOpacity = open ? 1f : 0f;
        float fromX = open ? slideDistancePixels : 0f;
        float toX = open ? 0f : slideDistancePixels;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);

            courseRoot.style.opacity = Mathf.Lerp(fromOpacity, toOpacity, eased);
            SetTranslateX(courseRoot, Mathf.Lerp(fromX, toX, eased));
            yield return null;
        }

        courseRoot.style.opacity = toOpacity;
        SetTranslateX(courseRoot, toX);
        courseRoot.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;

        if (!open && courseSelectionDocument != null && courseSelectionDocument.rootVisualElement != null)
        {
            courseSelectionDocument.rootVisualElement.style.display = DisplayStyle.None;
        }

        transitionRoutine = null;
    }

    private static void SetTranslateX(VisualElement element, float x)
    {
        element.style.translate = new StyleTranslate(
            new Translate(
                new Length(x, LengthUnit.Pixel),
                new Length(0f, LengthUnit.Pixel)
            )
        );
    }

    private void UpdateToggleVisualState()
    {
        if (toggleButton == null)
        {
            return;
        }

        toggleButton.EnableInClassList("is-active", isOpen);
        toggleButton.tooltip = isOpen ? "Hide Course Selection" : "Show Course Selection";
        EnforceBottomRightPlacement();
        EnforceToggleButtonVisual();
    }

    private void OnGUI()
    {
        if (!forceScreenOverlayButton || !Application.isPlaying)
        {
            return;
        }

        EnsureOverlayGuiStyle();

        float size = Mathf.Max(36f, overlaySize);
        float margin = Mathf.Max(8f, overlayMargin);
        Rect buttonRect = new Rect(
            Screen.width - margin - size,
            Screen.height - margin - size,
            size,
            size
        );

        Color prevColor = GUI.backgroundColor;
        GUI.backgroundColor = isOpen
            ? new Color(0.13f, 0.48f, 0.91f, 1f)
            : new Color(0.96f, 0.98f, 1f, 1f);

        if (GUI.Button(buttonRect, GUIContent.none, overlayButtonStyle))
        {
            OnToggleClicked();
        }

        DrawHamburgerGlyph(buttonRect);

        GUI.backgroundColor = prevColor;
    }

    private void DrawHamburgerGlyph(Rect buttonRect)
    {
        float lineWidth = buttonRect.width * 0.44f;
        float lineHeight = Mathf.Max(2f, buttonRect.height * 0.06f);
        float centerX = buttonRect.x + buttonRect.width * 0.5f;
        float centerY = buttonRect.y + buttonRect.height * 0.5f;
        float spacing = buttonRect.height * 0.15f;

        Color prevColor = GUI.color;
        GUI.color = isOpen ? Color.white : new Color(0.08f, 0.33f, 0.68f, 1f);

        GUI.DrawTexture(new Rect(centerX - lineWidth * 0.5f, centerY - spacing - lineHeight * 0.5f, lineWidth, lineHeight), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(centerX - lineWidth * 0.5f, centerY - lineHeight * 0.5f, lineWidth, lineHeight), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(centerX - lineWidth * 0.5f, centerY + spacing - lineHeight * 0.5f, lineWidth, lineHeight), Texture2D.whiteTexture);

        GUI.color = prevColor;
    }

    private void EnsureOverlayGuiStyle()
    {
        if (overlayButtonStyle != null)
        {
            overlayButtonStyle.normal.textColor = isOpen ? Color.white : new Color(0.08f, 0.33f, 0.68f, 1f);
            overlayButtonStyle.hover.textColor = overlayButtonStyle.normal.textColor;
            overlayButtonStyle.active.textColor = overlayButtonStyle.normal.textColor;
            return;
        }

        overlayButtonStyle = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            fontSize = 16,
            clipping = TextClipping.Clip
        };

        overlayButtonStyle.normal.textColor = new Color(0.08f, 0.33f, 0.68f, 1f);
        overlayButtonStyle.hover.textColor = overlayButtonStyle.normal.textColor;
        overlayButtonStyle.active.textColor = overlayButtonStyle.normal.textColor;
    }

    private void PlaceCoursePanelNearViewer()
    {
        if (courseSelectionUI == null)
        {
            return;
        }

        Transform panelTransform = courseSelectionUI.transform;
        Transform viewer = viewerTransform != null
            ? viewerTransform
            : (Camera.main != null ? Camera.main.transform : null);

        if (panelTransform == null || viewer == null)
        {
            return;
        }

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

        Vector3 targetPos = viewer.position
            + forward * panelDistance
            + right * panelRightOffset
            + Vector3.up * panelHeightOffset;

        panelTransform.position = targetPos;

        Vector3 toViewer = viewer.position - panelTransform.position;
        toViewer.y = 0f;
        if (toViewer.sqrMagnitude > 0.001f)
        {
            Quaternion lookRotation = Quaternion.LookRotation(toViewer.normalized, Vector3.up);
            if (RequiresCoursePanelFacingFlip())
            {
                lookRotation *= Quaternion.Euler(0f, 180f, 0f);
            }

            panelTransform.rotation = lookRotation;
        }
    }

    private bool RequiresCoursePanelFacingFlip()
    {
        UIDocument document = courseSelectionDocument;
        if (document == null && courseSelectionUI != null)
        {
            document = courseSelectionUI.GetComponent<UIDocument>();
        }

        return document != null
            && document.panelSettings != null
            && document.panelSettings.targetTexture == null;
    }

#if UNITY_EDITOR
    private void AutoAssignInEditor()
    {
        if (toggleViewTree == null)
        {
            toggleViewTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/UI/VRCourseSelection/CourseToggleButton.uxml"
            );
        }

        if (toggleStyle == null)
        {
            toggleStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/UI/VRCourseSelection/CourseToggleButton.uss"
            );
        }
    }
#endif
}
