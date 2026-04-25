using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class OnboardingManager : MonoBehaviour
{
    private const string FirstRunKey = "EDUMY_FIRST_RUN_DONE";
    private const float CanvasDistance = 0.9f;
    private const float HighlightPulseSpeed = 2.5f;
    private const float PanelHighlightDownOffset = 0.05f;

    private static OnboardingManager instance;

    private CourseSelectionUI courseSelectionUI;
    private CourseToggleController courseToggleController;
    private Canvas canvas;
    private RectTransform rootRect;
    private RectTransform dimmerRect;
    private RectTransform cardRect;
    private RectTransform highlightRect;
    private TMP_Text titleText;
    private TMP_Text bodyText;
    private TMP_Text stepText;
    private TMP_Text helperText;
    private Button backButton;
    private Button nextButton;
    private Button skipButton;
    private bool isActive;
    private bool hasBoundAppState;
    private int currentStepIndex;

    private readonly StepData[] steps =
    {
        new StepData("Welcome", "Welcome to VR Learning App. This quick tour will show you how to browse courses, pair your account, and open lessons comfortably in VR.", HighlightTarget.None, false, OnboardingActionType.None),
        new StepData("Open Menu", "Try pointing at the highlighted menu button and selecting it to open the Course Selection panel.", HighlightTarget.MenuButton, true, OnboardingActionType.MenuOpened),
        new StepData("Login Pairing", "Try selecting Get Login Code on the login panel. The tour will continue as soon as pairing starts, but you can still press Next if you want to continue.", HighlightTarget.CoursePanel, true, OnboardingActionType.LoginCodeRequested),
        new StepData("Choose Course", "Select any course from the list to open its lesson sections. This step also advances if the panel already shows a selected course.", HighlightTarget.CoursePanel, true, OnboardingActionType.CourseSelected),
        new StepData("Open Lesson", "Select any lesson in the section list. Video, slide, and quiz lessons all count for this step.", HighlightTarget.CoursePanel, true, OnboardingActionType.LessonOpened),
        new StepData("Window Controls", "Open a content window or try the dock/float controls. This step advances when a learning window becomes active.", HighlightTarget.VideoWindow, true, OnboardingActionType.ContentWindowOpened),
        new StepData("Ready To Learn", "You are ready to start. Reopen the menu at any time and use Continue Learning to jump back into your last lesson.", HighlightTarget.None, false, OnboardingActionType.None),
    };

    private enum HighlightTarget
    {
        None,
        MenuButton,
        CoursePanel,
        VideoWindow
    }

    private readonly struct StepData
    {
        public readonly string title;
        public readonly string body;
        public readonly HighlightTarget target;
        public readonly bool requiresAction;
        public readonly OnboardingActionType actionType;

        public StepData(string title, string body, HighlightTarget target, bool requiresAction, OnboardingActionType actionType)
        {
            this.title = title;
            this.body = body;
            this.target = target;
            this.requiresAction = requiresAction;
            this.actionType = actionType;
        }
    }

    public static OnboardingManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<OnboardingManager>();
                if (instance == null)
                {
                    GameObject go = new GameObject(nameof(OnboardingManager));
                    instance = go.AddComponent<OnboardingManager>();
                }
            }

            return instance;
        }
    }

    public static bool IsCompleted => PlayerPrefs.GetInt(FirstRunKey, 0) == 1;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void LateUpdate()
    {
        if (!isActive)
        {
            return;
        }

        EnsureUi();
        UpdateCanvasPose();
        UpdateHighlight();
        EvaluateCurrentStepProgress();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }

        UnbindAppState();
    }

    public void TryStart(CourseSelectionUI selectionUI, CourseToggleController toggleController)
    {
        if (selectionUI == null || IsCompleted || isActive)
        {
            return;
        }

        courseSelectionUI = selectionUI;
        courseToggleController = toggleController;
        EnsureUi();
        BindAppState();
        isActive = true;
        currentStepIndex = 0;
        rootRect.gameObject.SetActive(true);
        AppStateManager.Instance.SetOnboardingState(OnboardingFlowState.Running);
        if (courseToggleController != null)
        {
            courseToggleController.SetOpen(false);
        }

        ApplyStep();
        ToastManager.ShowInfo("VR onboarding started. You can skip it at any time.", 3.2f);
    }

    private void EnsureUi()
    {
        if (canvas != null && rootRect != null)
        {
            return;
        }

        canvas = GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
        }

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = short.MaxValue - 1;

        if (GetComponent<GraphicRaycaster>() == null)
        {
            gameObject.AddComponent<GraphicRaycaster>();
        }

        CanvasScaler scaler = GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = gameObject.AddComponent<CanvasScaler>();
        }
        scaler.dynamicPixelsPerUnit = 10f;

        rootRect = canvas.GetComponent<RectTransform>();
        if (rootRect == null)
        {
            rootRect = gameObject.AddComponent<RectTransform>();
        }
        rootRect.sizeDelta = new Vector2(1280f, 760f);
        transform.localScale = new Vector3(0.0011f, 0.0011f, 0.0011f);

        XRRuntimeUiHelper.EnsureWorldSpaceCanvasInteraction(gameObject);
        XRRuntimeUiHelper.EnsureEventSystemSupportsXR();

        BuildDimmer();
        BuildHighlight();
        BuildCard();
        rootRect.gameObject.SetActive(false);
    }

    private void BindAppState()
    {
        if (hasBoundAppState)
        {
            return;
        }

        AppStateManager state = AppStateManager.Instance;
        state.OnMenuStateChanged += HandleMenuStateChanged;
        state.OnAuthStateChanged += HandleAuthStateChanged;
        state.OnCurrentCourseChanged += HandleCurrentCourseChanged;
        state.OnCurrentLessonChanged += HandleCurrentLessonChanged;
        state.OnActiveWindowChanged += HandleActiveWindowChanged;
        state.OnOnboardingAction += HandleOnboardingAction;
        hasBoundAppState = true;
    }

    private void UnbindAppState()
    {
        if (!hasBoundAppState || !AppStateManager.IsAvailable)
        {
            return;
        }

        AppStateManager state = AppStateManager.Instance;
        state.OnMenuStateChanged -= HandleMenuStateChanged;
        state.OnAuthStateChanged -= HandleAuthStateChanged;
        state.OnCurrentCourseChanged -= HandleCurrentCourseChanged;
        state.OnCurrentLessonChanged -= HandleCurrentLessonChanged;
        state.OnActiveWindowChanged -= HandleActiveWindowChanged;
        state.OnOnboardingAction -= HandleOnboardingAction;
        hasBoundAppState = false;
    }

    private void BuildDimmer()
    {
        if (dimmerRect != null)
        {
            return;
        }

        GameObject dimmerGo = new GameObject("Dimmer", typeof(RectTransform), typeof(Image));
        dimmerGo.transform.SetParent(rootRect, false);
        dimmerRect = dimmerGo.GetComponent<RectTransform>();
        dimmerRect.anchorMin = Vector2.zero;
        dimmerRect.anchorMax = Vector2.one;
        dimmerRect.offsetMin = Vector2.zero;
        dimmerRect.offsetMax = Vector2.zero;

        Image image = dimmerGo.GetComponent<Image>();
        image.color = new Color(0.02f, 0.04f, 0.08f, 0.72f);
        image.raycastTarget = false;
    }

    private void BuildHighlight()
    {
        if (highlightRect != null)
        {
            return;
        }

        GameObject highlightGo = new GameObject("Highlight", typeof(RectTransform), typeof(Image), typeof(Outline));
        highlightGo.transform.SetParent(rootRect, false);
        highlightRect = highlightGo.GetComponent<RectTransform>();
        highlightRect.anchorMin = new Vector2(0.5f, 0.5f);
        highlightRect.anchorMax = new Vector2(0.5f, 0.5f);
        highlightRect.pivot = new Vector2(0.5f, 0.5f);

        Image image = highlightGo.GetComponent<Image>();
        image.color = new Color(0.23f, 0.66f, 1f, 0.18f);
        image.raycastTarget = false;

        Outline outline = highlightGo.GetComponent<Outline>();
        outline.effectColor = new Color(0.45f, 0.81f, 1f, 0.95f);
        outline.effectDistance = new Vector2(4f, -4f);
    }

    private void BuildCard()
    {
        if (cardRect != null)
        {
            return;
        }

        GameObject cardGo = new GameObject("OnboardingCard", typeof(RectTransform), typeof(Image));
        cardGo.transform.SetParent(rootRect, false);
        cardRect = cardGo.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 0f);
        cardRect.anchorMax = new Vector2(0.5f, 0f);
        cardRect.pivot = new Vector2(0.5f, 0f);
        cardRect.sizeDelta = new Vector2(760f, 320f);
        cardRect.anchoredPosition = new Vector2(0f, 42f);

        Image cardImage = cardGo.GetComponent<Image>();
        cardImage.color = new Color(0.1f, 0.16f, 0.24f, 0.98f);

        titleText = CreateText("Title", cardRect, "Welcome", 40f, FontStyles.Bold, TextAlignmentOptions.TopLeft, Color.white);
        titleText.rectTransform.anchorMin = new Vector2(0.06f, 0.78f);
        titleText.rectTransform.anchorMax = new Vector2(0.94f, 0.94f);
        titleText.rectTransform.offsetMin = Vector2.zero;
        titleText.rectTransform.offsetMax = Vector2.zero;

        bodyText = CreateText("Body", cardRect, string.Empty, 25f, FontStyles.Normal, TextAlignmentOptions.TopLeft, new Color(0.92f, 0.96f, 1f, 1f));
        bodyText.rectTransform.anchorMin = new Vector2(0.06f, 0.42f);
        bodyText.rectTransform.anchorMax = new Vector2(0.94f, 0.76f);
        bodyText.rectTransform.offsetMin = Vector2.zero;
        bodyText.rectTransform.offsetMax = Vector2.zero;
        bodyText.textWrappingMode = TextWrappingModes.Normal;

        helperText = CreateText("Helper", cardRect, string.Empty, 18f, FontStyles.Italic, TextAlignmentOptions.TopLeft, new Color(0.59f, 0.81f, 1f, 1f));
        helperText.rectTransform.anchorMin = new Vector2(0.06f, 0.28f);
        helperText.rectTransform.anchorMax = new Vector2(0.94f, 0.4f);
        helperText.rectTransform.offsetMin = Vector2.zero;
        helperText.rectTransform.offsetMax = Vector2.zero;
        helperText.textWrappingMode = TextWrappingModes.Normal;

        stepText = CreateText("Step", cardRect, "1/7", 20f, FontStyles.Bold, TextAlignmentOptions.TopLeft, new Color(0.56f, 0.78f, 1f, 1f));
        stepText.rectTransform.anchorMin = new Vector2(0.06f, 0.16f);
        stepText.rectTransform.anchorMax = new Vector2(0.4f, 0.26f);
        stepText.rectTransform.offsetMin = Vector2.zero;
        stepText.rectTransform.offsetMax = Vector2.zero;

        backButton = CreateButton("Back", cardRect, new Vector2(0.42f, 0.05f), new Vector2(0.58f, 0.18f), new Color(0.88f, 0.93f, 1f, 1f), new Color(0.09f, 0.23f, 0.41f, 1f));
        nextButton = CreateButton("Next", cardRect, new Vector2(0.60f, 0.05f), new Vector2(0.78f, 0.18f), new Color(0.19f, 0.56f, 0.94f, 1f), Color.white);
        skipButton = CreateButton("Skip", cardRect, new Vector2(0.80f, 0.05f), new Vector2(0.96f, 0.18f), new Color(0.93f, 0.39f, 0.39f, 1f), Color.white);

        backButton.onClick.AddListener(HandleBackClicked);
        nextButton.onClick.AddListener(HandleNextClicked);
        skipButton.onClick.AddListener(SkipTutorial);
    }

    private void HandleBackClicked()
    {
        currentStepIndex = Mathf.Max(0, currentStepIndex - 1);
        ApplyStep();
    }

    private void HandleNextClicked()
    {
        if (currentStepIndex >= steps.Length - 1)
        {
            CompleteTutorial();
            return;
        }

        currentStepIndex++;
        ApplyStep();
    }

    private void ApplyStep()
    {
        currentStepIndex = Mathf.Clamp(currentStepIndex, 0, steps.Length - 1);
        StepData step = steps[currentStepIndex];

        if (titleText != null) titleText.text = step.title;
        if (bodyText != null) bodyText.text = step.body;
        if (stepText != null) stepText.text = $"Step {currentStepIndex + 1}/{steps.Length}";
        if (helperText != null)
        {
            helperText.text = step.requiresAction
                ? "Try the highlighted action now. If detection does not advance automatically, you can still press Next."
                : "Use Next to continue or Skip to close the tutorial.";
        }

        if (backButton != null) backButton.interactable = currentStepIndex > 0;
        if (nextButton != null)
        {
            TMP_Text label = nextButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = currentStepIndex == steps.Length - 1 ? "Finish" : "Next";
            }
        }

        if (courseToggleController != null)
        {
            bool shouldMenuBeOpen = currentStepIndex >= 2;
            courseToggleController.SetOpen(shouldMenuBeOpen);
        }
    }

    private void SkipTutorial()
    {
        MarkCompleted(OnboardingFlowState.Skipped);
        ToastManager.ShowInfo("Onboarding skipped. You can start learning right away.", 3f);
        CloseTutorial();
    }

    private void CompleteTutorial()
    {
        MarkCompleted(OnboardingFlowState.Completed);
        ToastManager.ShowSuccess("Onboarding complete. You're ready to learn.", 3.4f);
        CloseTutorial();
    }

    private void MarkCompleted(OnboardingFlowState finalState)
    {
        PlayerPrefs.SetInt(FirstRunKey, 1);
        PlayerPrefs.Save();
        AppStateManager.Instance.SetOnboardingState(finalState);
    }

    private void CloseTutorial()
    {
        isActive = false;
        if (rootRect != null)
        {
            rootRect.gameObject.SetActive(false);
        }
        UnbindAppState();
    }

    private void UpdateCanvasPose()
    {
        Transform viewer = Camera.main != null ? Camera.main.transform : null;
        if (viewer == null)
        {
            return;
        }

        Vector3 position = viewer.position + (viewer.forward * CanvasDistance);
        transform.position = position;
        transform.rotation = Quaternion.LookRotation(viewer.position - position, Vector3.up) * Quaternion.Euler(0f, 180f, 0f);
    }

    private void UpdateHighlight()
    {
        if (highlightRect == null)
        {
            return;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            highlightRect.gameObject.SetActive(false);
            return;
        }

        StepData step = steps[Mathf.Clamp(currentStepIndex, 0, steps.Length - 1)];
        Transform target = ResolveTargetTransform(step.target);
        if (target == null)
        {
            highlightRect.gameObject.SetActive(false);
            return;
        }

        Vector3 targetWorldPosition = target.position;
        if (step.target == HighlightTarget.CoursePanel)
        {
            targetWorldPosition -= target.up * PanelHighlightDownOffset;
        }

        Vector3 screenPos = mainCamera.WorldToViewportPoint(targetWorldPosition);
        if (screenPos.z <= 0f || screenPos.x < 0f || screenPos.x > 1f || screenPos.y < 0f || screenPos.y > 1f)
        {
            highlightRect.gameObject.SetActive(false);
            return;
        }

        highlightRect.gameObject.SetActive(true);
        highlightRect.anchoredPosition = new Vector2(
            (screenPos.x - 0.5f) * rootRect.sizeDelta.x,
            (screenPos.y - 0.5f) * rootRect.sizeDelta.y);

        Vector2 baseSize = GetHighlightSize(step.target);
        float pulse = 1f + Mathf.Sin(Time.unscaledTime * HighlightPulseSpeed) * 0.06f;
        highlightRect.sizeDelta = baseSize * pulse;
    }

    private Transform ResolveTargetTransform(HighlightTarget target)
    {
        return target switch
        {
            HighlightTarget.MenuButton => courseToggleController != null && courseToggleController.FloatingButtonTransform != null
                ? courseToggleController.FloatingButtonTransform
                : (courseSelectionUI != null ? courseSelectionUI.transform : null),
            HighlightTarget.CoursePanel => courseSelectionUI != null ? courseSelectionUI.transform : null,
            HighlightTarget.VideoWindow => courseSelectionUI != null && courseSelectionUI.VideoWindowTransform != null
                ? courseSelectionUI.VideoWindowTransform
                : (courseSelectionUI != null ? courseSelectionUI.transform : null),
            _ => null
        };
    }

    private static Vector2 GetHighlightSize(HighlightTarget target)
    {
        return target switch
        {
            HighlightTarget.MenuButton => new Vector2(150f, 150f),
            HighlightTarget.VideoWindow => new Vector2(520f, 320f),
            HighlightTarget.CoursePanel => new Vector2(640f, 420f),
            _ => Vector2.zero
        };
    }

    private void EvaluateCurrentStepProgress()
    {
        if (!isActive)
        {
            return;
        }

        StepData step = steps[Mathf.Clamp(currentStepIndex, 0, steps.Length - 1)];
        if (!step.requiresAction || !IsCurrentStepSatisfied(step))
        {
            return;
        }

        AdvanceStepFromAction();
    }

    private bool IsCurrentStepSatisfied(StepData step)
    {
        AppStateManager state = AppStateManager.Instance;
        return step.actionType switch
        {
            OnboardingActionType.MenuOpened => state.IsMenuOpen,
            OnboardingActionType.LoginCodeRequested => state.AuthState == AppAuthState.Pairing,
            OnboardingActionType.CourseSelected => !string.IsNullOrWhiteSpace(state.CurrentCourse.courseId),
            OnboardingActionType.LessonOpened => !string.IsNullOrWhiteSpace(state.CurrentLesson.lessonId),
            OnboardingActionType.ContentWindowOpened => state.ActiveWindow != ActiveContentWindowType.None,
            OnboardingActionType.DockFloatToggled => state.ActiveWindow != ActiveContentWindowType.None,
            _ => false
        };
    }

    private void AdvanceStepFromAction()
    {
        if (currentStepIndex >= steps.Length - 1)
        {
            CompleteTutorial();
            return;
        }

        currentStepIndex++;
        ApplyStep();
    }

    private void HandleMenuStateChanged(bool _)
    {
        EvaluateCurrentStepProgress();
    }

    private void HandleAuthStateChanged(AppAuthState _)
    {
        EvaluateCurrentStepProgress();
    }

    private void HandleCurrentCourseChanged(CourseStateSnapshot _)
    {
        EvaluateCurrentStepProgress();
    }

    private void HandleCurrentLessonChanged(LessonStateSnapshot _)
    {
        EvaluateCurrentStepProgress();
    }

    private void HandleActiveWindowChanged(ActiveContentWindowType _)
    {
        EvaluateCurrentStepProgress();
    }

    private void HandleOnboardingAction(OnboardingActionSnapshot action)
    {
        if (!isActive || action == null)
        {
            return;
        }

        StepData step = steps[Mathf.Clamp(currentStepIndex, 0, steps.Length - 1)];
        if (step.actionType == action.actionType)
        {
            AdvanceStepFromAction();
        }
    }

    private static TMP_Text CreateText(string name, RectTransform parent, string value, float fontSize, FontStyles style, TextAlignmentOptions alignment, Color color)
    {
        GameObject textGo = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(parent, false);
        TMP_Text text = textGo.GetComponent<TMP_Text>();
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private static Button CreateButton(string label, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Color background, Color textColor)
    {
        GameObject buttonGo = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonGo.transform.SetParent(parent, false);

        RectTransform rect = buttonGo.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = buttonGo.GetComponent<Image>();
        image.color = background;

        Button button = buttonGo.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = background;
        colors.highlightedColor = Color.Lerp(background, Color.white, 0.18f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = Color.Lerp(background, Color.black, 0.15f);
        colors.disabledColor = new Color(background.r, background.g, background.b, background.a * 0.5f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        TMP_Text text = CreateText("Label", rect, label, 24f, FontStyles.Bold, TextAlignmentOptions.Center, textColor);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;

        return button;
    }
}
