using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;

[ExecuteAlways]
[DisallowMultipleComponent]
public class QuizPopupWindow : MonoBehaviour
{
    private class QuizReviewEntry
    {
        public int questionIndex;
        public string questionText;
        public string selectedAnswer;
        public string correctAnswer;
        public string explanation;
        public bool isCorrect;
    }

    private static readonly Color OptionDefaultColor = new Color(0.92f, 0.96f, 1f, 0.98f);
    private static readonly Color OptionCorrectColor = new Color(0.086f, 0.639f, 0.290f, 0.98f);
    private static readonly Color OptionWrongColor = new Color(0.863f, 0.149f, 0.149f, 0.98f);
    private static readonly Color OptionDefaultTextColor = new Color(0.15f, 0.3f, 0.49f, 1f);
    private static readonly Color OptionSelectedTextColor = new Color(1f, 1f, 1f, 1f);
    private static readonly Color PanelColor = new Color(0.985f, 0.992f, 1f, 0.98f);
    private static readonly Color HeaderColor = new Color(0.82f, 0.9f, 1f, 0.99f);
    private static readonly Color ActionButtonColor = new Color(0.16f, 0.58f, 0.93f, 0.98f);
    private static readonly Color SecondaryButtonColor = new Color(0.92f, 0.95f, 1f, 0.98f);
    private static readonly Color TitleTextColor = new Color(0.05f, 0.08f, 0.13f, 1f);

    [SerializeField] private Transform windowTransform;
    [SerializeField] private Vector2 canvasSize = new Vector2(1100f, 760f);
    [SerializeField] private Vector3 windowScale = new Vector3(0.0014f, 0.0014f, 0.0014f);
    [SerializeField] private bool keepPlacedTransformOnShow = true;
    [SerializeField] private bool autoCreateWindowInEditor = true;
    [SerializeField] private bool showPreviewInEditor = true;
    [SerializeField] private bool autoPlaceInFrontWhenPlaying = false;

    [SerializeField] private float horizontalOffset = -0.35f;
    [SerializeField] private float additionalHeightOffset = -0.35f;
    [SerializeField] private bool flipForwardToFaceViewer = true;
    [SerializeField] private bool enableDebugLogs = true;

    private Canvas canvas;
    private RectTransform rootRect;
    private TMP_Text titleText;
    private TMP_Text indicatorText;
    private TMP_Text questionText;
    private TMP_Text feedbackText;
    private ScrollRect reviewScrollRect;
    private RectTransform reviewContentRect;
    private Button closeButton;
    private Button prevButton;
    private Button nextButton;
    private Button pinButton;
    private Button reviewAllButton;
    private Button reviewIncorrectButton;
    private Button retryButton;
    private Button retryIncorrectButton;
    private readonly List<Button> optionButtons = new List<Button>();

    private readonly List<QuizQuestionData> activeQuizQuestions = new List<QuizQuestionData>();
    private readonly List<QuizQuestionData> originalQuizQuestions = new List<QuizQuestionData>();
    private readonly List<QuizReviewEntry> reviewEntries = new List<QuizReviewEntry>();
    private readonly Dictionary<int, int> selectedByQuestion = new Dictionary<int, int>();
    private int currentQuestionIndex;
    private bool bindingsAdded;
    private bool createdRuntimeWindow;
    private Transform activeViewer;
    private float activeDistance;
    private float activeHeightOffset;
    private SpatialWindow spatialWindow;
    private bool reviewIncorrectOnly;

    private void Awake()
    {
        if (windowTransform == null)
        {
            Transform existing = transform.Find("QuizPopupWindowRoot");
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
        return BuildQuizForLesson(lesson).Count > 0;
    }

    public Transform WindowRootTransform => windowTransform;
    public float HorizontalOffset => horizontalOffset;

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

    public void SetPlacementOffsets(float distance, float heightOffset, float horizontal)
    {
        horizontalOffset = horizontal;

        if (windowTransform != null && windowTransform.gameObject.activeInHierarchy)
        {
            Transform viewer = activeViewer != null ? activeViewer : (Camera.main != null ? Camera.main.transform : null);
            if (viewer != null)
            {
                PlaceInFrontOf(viewer, distance, heightOffset);
            }
        }
    }

    public bool Show(LessonData lesson, Transform viewer, float distance, float heightOffset)
    {
        if (lesson == null) return false;

        EnsureWindowExists(false);
        if (windowTransform == null || canvas == null) return false;

        activeQuizQuestions.Clear();
        activeQuizQuestions.AddRange(BuildQuizForLesson(lesson));
        originalQuizQuestions.Clear();
        originalQuizQuestions.AddRange(activeQuizQuestions);
        reviewEntries.Clear();
        reviewIncorrectOnly = false;
        if (enableDebugLogs)
        {
            Debug.Log($"[QuizPopupWindow] quiz source counts lesson={lesson.id} quizQuestions={SafeCount(lesson.quizQuestions)} questions={SafeCount(lesson.questions)} quizzes={SafeCount(lesson.quizzes)} timed={SafeCount(lesson.timedQuizzes)} interactive={SafeCount(lesson.interactiveQuizzes)} final={activeQuizQuestions.Count}");
        }
        if (activeQuizQuestions.Count == 0)
        {
            if (enableDebugLogs)
            {
                Debug.LogWarning($"[QuizPopupWindow] No standalone quiz questions available for lesson {lesson.id}.");
            }
            return false;
        }
        selectedByQuestion.Clear();
        currentQuestionIndex = 0;
        activeViewer = viewer;
        activeDistance = distance;
        activeHeightOffset = heightOffset;

        if (titleText != null)
        {
            titleText.text = string.IsNullOrWhiteSpace(lesson.title) ? "Quiz" : lesson.title;
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

        windowTransform.gameObject.SetActive(true);
        EnsureSpatialWindow();
        spatialWindow?.SetPinned(false, false);
        UpdatePinButtonState();
        RenderQuestion();
        return true;
    }

    public void HideWindow()
    {
        if (windowTransform != null)
        {
            windowTransform.gameObject.SetActive(false);
        }

        activeViewer = null;
        if (AppStateManager.IsAvailable && AppStateManager.Instance.ActiveWindow == ActiveContentWindowType.Quiz)
        {
            AppStateManager.Instance.SetActiveWindow(ActiveContentWindowType.None);
        }
    }

    private void EnsureWindowExists(bool forceEditorCreation)
    {
        if (windowTransform == null)
        {
            if (!Application.isPlaying && !forceEditorCreation) return;

            GameObject root = new GameObject("QuizPopupWindowRoot", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            windowTransform = root.transform;
            createdRuntimeWindow = true;
        }

        windowTransform = EnsureRectTransformRoot(windowTransform);
        if (windowTransform == null)
        {
            Debug.LogError("[QuizPopupWindow] Could not create a valid RectTransform root.");
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
            Debug.LogError("[QuizPopupWindow] Missing RectTransform on canvas root.");
            return;
        }
        rootRect.sizeDelta = canvasSize;
        windowTransform.localScale = windowScale;

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

        // VR-sized text: larger fonts for readability in-headset
        titleText = FindOrCreateText(panel, "Title", new Vector2(0.05f, 0.895f), new Vector2(0.64f, 0.975f), 40, FontStyles.Bold, TextAlignmentOptions.Left);
        titleText.color = TitleTextColor;
        indicatorText = FindOrCreateText(panel, "Indicator", new Vector2(0.05f, 0.83f), new Vector2(0.45f, 0.895f), 26, FontStyles.Normal, TextAlignmentOptions.Left);
        indicatorText.color = new Color(0.34f, 0.43f, 0.58f, 1f);
        questionText = FindOrCreateText(panel, "Question", new Vector2(0.05f, 0.70f), new Vector2(0.95f, 0.82f), 30, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        questionText.textWrappingMode = TextWrappingModes.Normal;
        questionText.color = new Color(0.15f, 0.24f, 0.38f, 1f);

        feedbackText = FindOrCreateText(panel, "Feedback", new Vector2(0.05f, 0.10f), new Vector2(0.95f, 0.19f), 24, FontStyles.Italic, TextAlignmentOptions.TopLeft);
        feedbackText.textWrappingMode = TextWrappingModes.Normal;
        feedbackText.color = new Color(0.27f, 0.39f, 0.56f, 1f);

        EnsureReviewViewport(panelRect);

        // VR-sized buttons: taller hit targets for XR ray interaction
        closeButton = FindOrCreateButton(panelRect, "CloseButton", "Close", new Vector2(0.78f, 0.89f), new Vector2(0.95f, 0.975f));
        pinButton = FindOrCreateButton(panelRect, "PinButton", "[Pin]", new Vector2(0.65f, 0.89f), new Vector2(0.77f, 0.975f));
        prevButton = FindOrCreateButton(panelRect, "PrevButton", "Previous", new Vector2(0.43f, 0.025f), new Vector2(0.68f, 0.11f));
        nextButton = FindOrCreateButton(panelRect, "NextButton", "Next", new Vector2(0.70f, 0.025f), new Vector2(0.95f, 0.11f));
        reviewAllButton = FindOrCreateButton(panelRect, "ReviewAllButton", "Review All", new Vector2(0.05f, 0.025f), new Vector2(0.25f, 0.11f));
        reviewIncorrectButton = FindOrCreateButton(panelRect, "ReviewWrongButton", "Wrong Only", new Vector2(0.27f, 0.025f), new Vector2(0.47f, 0.11f));
        retryButton = FindOrCreateButton(panelRect, "RetryButton", "Retry Quiz", new Vector2(0.49f, 0.025f), new Vector2(0.69f, 0.11f));
        retryIncorrectButton = FindOrCreateButton(panelRect, "RetryWrongButton", "Retry Wrong", new Vector2(0.71f, 0.025f), new Vector2(0.95f, 0.11f));
        SetButtonBaseColor(closeButton, SecondaryButtonColor);
        SetButtonBaseColor(pinButton, SecondaryButtonColor);
        SetButtonBaseColor(prevButton, SecondaryButtonColor);
        SetButtonBaseColor(nextButton, ActionButtonColor);
        SetButtonBaseColor(reviewAllButton, SecondaryButtonColor);
        SetButtonBaseColor(reviewIncorrectButton, SecondaryButtonColor);
        SetButtonBaseColor(retryButton, ActionButtonColor);
        SetButtonBaseColor(retryIncorrectButton, SecondaryButtonColor);

        // VR-sized answer options: taller cards with generous spacing for easy targeting
        optionButtons.Clear();
        float top = 0.665f;
        float height = 0.09f;
        for (int i = 0; i < 4; i++)
        {
            float yMax = top - i * 0.112f;
            float yMin = yMax - height;
            Button b = FindOrCreateButton(panelRect, $"Option{i}", $"Option {i + 1}", new Vector2(0.05f, yMin), new Vector2(0.95f, yMax));
            SetButtonBaseColor(b, OptionDefaultColor);
            optionButtons.Add(b);
        }
    }

    private void BindEventsOnce()
    {
        if (bindingsAdded) return;

        if (closeButton != null) closeButton.onClick.AddListener(HideWindow);
        if (pinButton != null) pinButton.onClick.AddListener(TogglePinnedState);
        if (prevButton != null) prevButton.onClick.AddListener(PreviousStep);
        if (nextButton != null) nextButton.onClick.AddListener(NextStep);
        if (reviewAllButton != null) reviewAllButton.onClick.AddListener(() => RenderReviewMode(false));
        if (reviewIncorrectButton != null) reviewIncorrectButton.onClick.AddListener(() => RenderReviewMode(true));
        if (retryButton != null) retryButton.onClick.AddListener(() => RestartQuiz(false));
        if (retryIncorrectButton != null) retryIncorrectButton.onClick.AddListener(() => RestartQuiz(true));

        for (int i = 0; i < optionButtons.Count; i++)
        {
            int index = i;
            Button b = optionButtons[i];
            if (b != null)
            {
                b.onClick.AddListener(() => SelectOption(index));
            }
        }

        bindingsAdded = true;
        UpdatePinButtonState();
    }

    private void PreviousStep()
    {
        if (currentQuestionIndex <= 0)
        {
            return;
        }

        currentQuestionIndex--;
        RenderQuestion();
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
            label.text = spatialWindow.IsPinned ? "[Pin]" : "Pin";
        }

        SetButtonBaseColor(pinButton, spatialWindow.IsPinned ? ActionButtonColor : SecondaryButtonColor);
    }

    private List<QuizQuestionData> BuildQuizForLesson(LessonData lesson)
    {
        List<QuizQuestionData> list = new List<QuizQuestionData>();
        List<QuizQuestionData> source = SelectStandaloneQuizSource(lesson);

        if (source.Count > 0)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (QuizQuestionData q in source)
            {
                if (q == null) continue;

                string questionText = GetQuestionText(q);
                string question = string.IsNullOrWhiteSpace(questionText)
                    ? string.Empty
                    : questionText.Trim();

                List<string> normalizedOptions = new List<string>();
                List<string> sourceOptions = null;
                if (q.options != null && q.options.Count > 0) sourceOptions = q.options;
                else if (q.answers != null && q.answers.Count > 0) sourceOptions = q.answers;
                else if (q.choices != null && q.choices.Count > 0) sourceOptions = q.choices;

                if (sourceOptions != null)
                {
                    for (int i = 0; i < sourceOptions.Count; i++)
                    {
                        string option = sourceOptions[i];
                        normalizedOptions.Add(string.IsNullOrWhiteSpace(option) ? $"Option {i + 1}" : option.Trim());
                    }
                }

                if (string.IsNullOrWhiteSpace(question) || normalizedOptions.Count == 0) continue;
                string fingerprint = $"{question}::{string.Join("|", normalizedOptions)}";
                if (!seen.Add(fingerprint)) continue;
                int correctedIndex = ResolveCorrectIndex(q, normalizedOptions.Count);

                list.Add(new QuizQuestionData
                {
                    question = question,
                    text = question,
                    prompt = question,
                    options = normalizedOptions,
                    answers = normalizedOptions,
                    choices = normalizedOptions,
                    explanation = q.explanation,
                    explain = q.explain,
                    reason = q.reason,
                    solution = q.solution,
                    wrongExplanation = q.wrongExplanation,
                    correctAnswer = q.correctAnswer,
                    correctIndex = correctedIndex
                });

                if (enableDebugLogs)
                {
                    Debug.Log($"[QuizPopupWindow] normalized question text='{question}' options={normalizedOptions.Count}");
                }
            }
        }

        return list;
    }

    private static void AddQuestions(List<QuizQuestionData> destination, List<QuizQuestionData> source)
    {
        if (destination == null || source == null) return;
        for (int i = 0; i < source.Count; i++)
        {
            QuizQuestionData q = source[i];
            if (q != null) destination.Add(q);
        }
    }

    private static string FirstNonEmpty(params string[] values)
    {
        if (values == null) return null;
        for (int i = 0; i < values.Length; i++)
        {
            string s = values[i];
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
        return null;
    }

    private static string GetQuestionText(QuizQuestionData question)
    {
        return FirstNonEmpty(question != null ? question.question : null, question != null ? question.prompt : null, question != null ? question.text : null);
    }

    private static int ResolveCorrectIndex(QuizQuestionData question, int optionCount)
    {
        if (question == null || optionCount <= 0)
        {
            return 0;
        }

        if (question.correctIndex >= 0 && question.correctIndex < optionCount)
        {
            return question.correctIndex;
        }

        int oneBasedAnswer = question.correctAnswer;
        if (oneBasedAnswer > 0 && oneBasedAnswer <= optionCount)
        {
            return oneBasedAnswer - 1;
        }

        if (question.correctAnswer >= 0 && question.correctAnswer < optionCount)
        {
            return question.correctAnswer;
        }

        return Mathf.Clamp(question.correctIndex, 0, optionCount - 1);
    }

    private static int SafeCount<T>(List<T> list)
    {
        return list != null ? list.Count : 0;
    }

    private static bool HasValidStandaloneQuizQuestions(List<QuizQuestionData> source)
    {
        if (source == null || source.Count == 0) return false;
        foreach (QuizQuestionData question in source)
        {
            if (question == null) continue;
            string text = GetQuestionText(question);
            List<string> options = question.options != null && question.options.Count > 0
                ? question.options
                : (question.answers != null && question.answers.Count > 0 ? question.answers : question.choices);
            if (!string.IsNullOrWhiteSpace(text) && options != null && options.Any(option => !string.IsNullOrWhiteSpace(option)))
            {
                return true;
            }
        }
        return false;
    }

    private static List<QuizQuestionData> SelectStandaloneQuizSource(LessonData lesson)
    {
        if (lesson == null) return new List<QuizQuestionData>();
        if (HasValidStandaloneQuizQuestions(lesson.quizQuestions)) return lesson.quizQuestions;
        if (HasValidStandaloneQuizQuestions(lesson.questions)) return lesson.questions;
        if (HasValidStandaloneQuizQuestions(lesson.quizzes)) return lesson.quizzes;
        if (lesson.quizQuestion != null && !string.IsNullOrWhiteSpace(GetQuestionText(lesson.quizQuestion))) return new List<QuizQuestionData> { lesson.quizQuestion };
        return new List<QuizQuestionData>();
    }

    private void SelectOption(int optionIndex)
    {
        if (activeQuizQuestions.Count == 0) return;
        if (currentQuestionIndex < 0 || currentQuestionIndex >= activeQuizQuestions.Count) return;

        QuizQuestionData q = activeQuizQuestions[currentQuestionIndex];
        if (q == null || q.options == null || optionIndex < 0 || optionIndex >= q.options.Count) return;

        selectedByQuestion[currentQuestionIndex] = optionIndex;
        RenderQuestion();
    }

    private void NextStep()
    {
        if (activeQuizQuestions.Count == 0) return;

        if (!selectedByQuestion.ContainsKey(currentQuestionIndex))
        {
            if (feedbackText != null)
            {
                feedbackText.text = "Please select an answer before moving to the next question.";
            }
            return;
        }

        currentQuestionIndex++;
        RenderQuestion();
    }

    public void TrySetQuestionIndex(int questionIndex)
    {
        if (activeQuizQuestions.Count == 0)
        {
            return;
        }

        currentQuestionIndex = Mathf.Clamp(questionIndex, 0, activeQuizQuestions.Count - 1);
        RenderQuestion();
    }

    private void RenderQuestion()
    {
        SetReviewModeVisible(false);

        if (activeQuizQuestions.Count == 0)
        {
            if (indicatorText != null) indicatorText.text = "Question 0/0";
            if (questionText != null) questionText.text = "No quiz data.";
            if (feedbackText != null) feedbackText.text = string.Empty;
            if (nextButton != null) nextButton.interactable = false;
            if (prevButton != null) prevButton.interactable = false;
            return;
        }

        if (currentQuestionIndex >= activeQuizQuestions.Count)
        {
            RenderSummary();
            return;
        }

        QuizQuestionData q = activeQuizQuestions[currentQuestionIndex];
        if (indicatorText != null) indicatorText.text = $"Question {currentQuestionIndex + 1}/{activeQuizQuestions.Count}";
        if (questionText != null)
        {
            string displayQuestion = GetQuestionText(q);
            questionText.text = string.IsNullOrWhiteSpace(displayQuestion) ? "Question" : displayQuestion;
            questionText.overflowMode = TextOverflowModes.Overflow;
            questionText.gameObject.SetActive(true);
            if (enableDebugLogs)
            {
                Debug.Log($"[QuizPopupWindow] render pass questionIndex={currentQuestionIndex} chosenText='{questionText.text}'");
            }
        }

        int selectedIndex = selectedByQuestion.TryGetValue(currentQuestionIndex, out int selected) ? selected : -1;
        int correctIndex = Mathf.Clamp(q.correctIndex, 0, Mathf.Max(0, q.options.Count - 1));
        bool hasAnswered = selectedIndex >= 0;

        for (int i = 0; i < optionButtons.Count; i++)
        {
            Button b = optionButtons[i];
            if (b == null) continue;

            bool hasOption = q.options != null && i < q.options.Count;
            b.gameObject.SetActive(hasOption);
            if (!hasOption) continue;

            TextMeshProUGUI label = b.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.text = q.options[i];
            }

            Image image = b.GetComponent<Image>();
            if (image != null)
            {
                if (!hasAnswered)
                {
                    image.color = OptionDefaultColor;
                    if (label != null) label.color = OptionDefaultTextColor;
                }
                else if (i == correctIndex)
                {
                    image.color = OptionCorrectColor;
                    if (label != null) label.color = OptionSelectedTextColor;
                }
                else if (i == selectedIndex && selectedIndex != correctIndex)
                {
                    image.color = OptionWrongColor;
                    if (label != null) label.color = OptionSelectedTextColor;
                }
                else
                {
                    image.color = OptionDefaultColor;
                    if (label != null) label.color = OptionDefaultTextColor;
                }
            }

            b.interactable = !hasAnswered;
        }

        if (feedbackText != null)
        {
            if (!hasAnswered)
            {
                feedbackText.text = "Select one answer.";
                feedbackText.color = new Color(0.27f, 0.39f, 0.56f, 1f);
            }
            else if (selectedIndex == correctIndex)
            {
                string explanation = FirstNonEmpty(q.explanation, q.explain, q.reason, q.solution);
                feedbackText.text = string.IsNullOrWhiteSpace(explanation)
                    ? "Correct!"
                    : $"Correct!\nExplanation: {explanation}";
                feedbackText.color = OptionCorrectColor;
            }
            else
            {
                string correctAnswerText = (correctIndex >= 0 && correctIndex < q.options.Count)
                    ? q.options[correctIndex]
                    : "(unknown)";
                string explanation = FirstNonEmpty(q.explanation, q.explain, q.reason, q.solution, q.wrongExplanation);
                feedbackText.text = string.IsNullOrWhiteSpace(explanation)
                    ? $"Incorrect. Correct answer: {correctAnswerText}"
                    : $"Incorrect. Correct answer: {correctAnswerText}\nExplanation: {explanation}";
                feedbackText.color = OptionWrongColor;
            }
        }

        if (nextButton != null)
        {
            nextButton.interactable = selectedIndex >= 0;
            TextMeshProUGUI label = nextButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.text = currentQuestionIndex == activeQuizQuestions.Count - 1 ? "Finish" : "Next";
            }
        }

        if (prevButton != null)
        {
            prevButton.interactable = currentQuestionIndex > 0;
        }
    }

    private void RenderSummary()
    {
        int total = activeQuizQuestions.Count;
        int score = 0;
        reviewEntries.Clear();

        for (int i = 0; i < total; i++)
        {
            QuizQuestionData q = activeQuizQuestions[i];
            if (q == null) continue;
            int selectedIndex = selectedByQuestion.TryGetValue(i, out int selected) ? selected : -1;
            int correctIndex = Mathf.Clamp(q.correctIndex, 0, Mathf.Max(0, q.options.Count - 1));
            bool isCorrect = selectedIndex == correctIndex;
            if (isCorrect)
            {
                score++;
            }

            string correctAnswer = correctIndex >= 0 && correctIndex < q.options.Count ? q.options[correctIndex] : "(unknown)";
            string selectedAnswer = selectedIndex >= 0 && selectedIndex < q.options.Count ? q.options[selectedIndex] : "(not answered)";
            reviewEntries.Add(new QuizReviewEntry
            {
                questionIndex = i,
                questionText = GetQuestionText(q),
                selectedAnswer = selectedAnswer,
                correctAnswer = correctAnswer,
                explanation = FirstNonEmpty(q.explanation, q.explain, q.reason, q.solution, q.wrongExplanation),
                isCorrect = isCorrect
            });
        }

        if (indicatorText != null) indicatorText.text = "Summary";
        if (questionText != null) questionText.text = $"Your score: {score}/{total}";
        if (feedbackText != null)
        {
            feedbackText.text = score >= Mathf.CeilToInt(total * 0.7f)
                ? "Great work. You passed this quiz."
                : "Keep going. Review the lesson and try again.";
        }

        foreach (Button b in optionButtons)
        {
            if (b != null) b.gameObject.SetActive(false);
        }

        if (nextButton != null)
        {
            nextButton.interactable = false;
            TextMeshProUGUI label = nextButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = "Done";
        }

        if (prevButton != null)
        {
            prevButton.interactable = false;
        }

        RenderReviewMode(false);
    }

    private void RenderReviewMode(bool incorrectOnly)
    {
        reviewIncorrectOnly = incorrectOnly;
        SetReviewModeVisible(true);
        if (questionText != null)
        {
            questionText.text = incorrectOnly ? "Reviewing incorrect answers" : "Quiz review";
        }
        BuildReviewEntriesUi();
    }

    private void RestartQuiz(bool incorrectOnly)
    {
        List<QuizQuestionData> source = originalQuizQuestions;
        if (incorrectOnly)
        {
            List<QuizQuestionData> wrongQuestions = new List<QuizQuestionData>();
            for (int i = 0; i < reviewEntries.Count; i++)
            {
                if (!reviewEntries[i].isCorrect && reviewEntries[i].questionIndex >= 0 && reviewEntries[i].questionIndex < originalQuizQuestions.Count)
                {
                    wrongQuestions.Add(originalQuizQuestions[reviewEntries[i].questionIndex]);
                }
            }

            if (wrongQuestions.Count > 0)
            {
                source = wrongQuestions;
            }
        }

        activeQuizQuestions.Clear();
        activeQuizQuestions.AddRange(source);
        selectedByQuestion.Clear();
        currentQuestionIndex = 0;
        reviewEntries.Clear();
        RenderQuestion();
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
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = panel.GetComponent<Image>();
        image.color = PanelColor;
        return panel;
    }

    private void EnsureReviewViewport(RectTransform panelRect)
    {
        if (panelRect == null)
        {
            return;
        }

        Transform viewport = panelRect.Find("ReviewViewport");
        if (viewport == null)
        {
            GameObject viewportGo = new GameObject("ReviewViewport", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
            viewportGo.transform.SetParent(panelRect, false);
            viewport = viewportGo.transform;
        }

        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.anchorMin = new Vector2(0.05f, 0.20f);
        viewportRect.anchorMax = new Vector2(0.95f, 0.69f);
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;

        Image viewportImage = viewport.GetComponent<Image>();
        viewportImage.color = new Color(0.95f, 0.98f, 1f, 0.92f);
        viewportImage.raycastTarget = true;

        Mask mask = viewport.GetComponent<Mask>();
        mask.showMaskGraphic = false;

        Transform content = viewport.Find("Content");
        if (content == null)
        {
            GameObject contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewport, false);
            content = contentGo.transform;
        }

        reviewContentRect = content.GetComponent<RectTransform>();
        reviewContentRect.anchorMin = new Vector2(0f, 1f);
        reviewContentRect.anchorMax = new Vector2(1f, 1f);
        reviewContentRect.pivot = new Vector2(0.5f, 1f);
        reviewContentRect.offsetMin = new Vector2(12f, 0f);
        reviewContentRect.offsetMax = new Vector2(-12f, 0f);

        VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        layout.spacing = 10f;

        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        reviewScrollRect = viewport.GetComponent<ScrollRect>();
        reviewScrollRect.viewport = viewportRect;
        reviewScrollRect.content = reviewContentRect;
        reviewScrollRect.horizontal = false;
        reviewScrollRect.vertical = true;
        reviewScrollRect.movementType = ScrollRect.MovementType.Clamped;

        viewport.gameObject.SetActive(false);
    }

    private void SetReviewModeVisible(bool visible)
    {
        if (reviewScrollRect != null)
        {
            reviewScrollRect.gameObject.SetActive(visible);
        }

        if (reviewAllButton != null) reviewAllButton.gameObject.SetActive(visible);
        if (reviewIncorrectButton != null) reviewIncorrectButton.gameObject.SetActive(visible);
        if (retryButton != null) retryButton.gameObject.SetActive(visible);
        if (retryIncorrectButton != null) retryIncorrectButton.gameObject.SetActive(visible);

        for (int i = 0; i < optionButtons.Count; i++)
        {
            if (!visible && optionButtons[i] != null)
            {
                // handled by RenderQuestion
            }
        }

        if (prevButton != null) prevButton.gameObject.SetActive(!visible);
        if (nextButton != null) nextButton.gameObject.SetActive(!visible);
    }

    private void BuildReviewEntriesUi()
    {
        if (reviewContentRect == null)
        {
            return;
        }

        for (int i = reviewContentRect.childCount - 1; i >= 0; i--)
        {
            Transform child = reviewContentRect.GetChild(i);
            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }

        IEnumerable<QuizReviewEntry> entries = reviewIncorrectOnly ? reviewEntries.Where(entry => !entry.isCorrect) : reviewEntries;
        bool anyEntry = false;
        foreach (QuizReviewEntry entry in entries)
        {
            anyEntry = true;
            CreateReviewCard(entry);
        }

        if (!anyEntry)
        {
            GameObject emptyGo = new GameObject("EmptyReviewState", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            emptyGo.transform.SetParent(reviewContentRect, false);
            LayoutElement layout = emptyGo.GetComponent<LayoutElement>();
            layout.preferredHeight = 80f;
            TextMeshProUGUI text = emptyGo.GetComponent<TextMeshProUGUI>();
            text.text = reviewIncorrectOnly ? "No incorrect answers to review." : "No review entries available.";
            text.fontSize = 22f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.27f, 0.39f, 0.56f, 1f);
        }
    }

    private void CreateReviewCard(QuizReviewEntry entry)
    {
        GameObject cardGo = new GameObject($"Review_{entry.questionIndex}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        cardGo.transform.SetParent(reviewContentRect, false);
        RectTransform rect = cardGo.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(0f, 220f);

        LayoutElement layout = cardGo.GetComponent<LayoutElement>();
        layout.preferredHeight = 220f;

        Image cardImage = cardGo.GetComponent<Image>();
        cardImage.color = entry.isCorrect ? new Color(0.90f, 0.97f, 0.92f, 1f) : new Color(1f, 0.92f, 0.92f, 1f);

        TMP_Text header = FindOrCreateText(cardGo.transform, "Header", new Vector2(0.04f, 0.74f), new Vector2(0.88f, 0.94f), 24f, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        header.text = $"Q{entry.questionIndex + 1} · {(entry.isCorrect ? "Correct" : "Wrong")}";
        header.color = entry.isCorrect ? OptionCorrectColor : OptionWrongColor;

        TMP_Text question = FindOrCreateText(cardGo.transform, "Question", new Vector2(0.04f, 0.48f), new Vector2(0.96f, 0.76f), 21f, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        question.text = entry.questionText;
        question.textWrappingMode = TextWrappingModes.Normal;
        question.overflowMode = TextOverflowModes.Overflow;
        question.color = TitleTextColor;

        TMP_Text answers = FindOrCreateText(cardGo.transform, "Answers", new Vector2(0.04f, 0.18f), new Vector2(0.96f, 0.48f), 18f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        answers.text = $"Your answer: {entry.selectedAnswer}\nCorrect answer: {entry.correctAnswer}";
        answers.textWrappingMode = TextWrappingModes.Normal;
        answers.overflowMode = TextOverflowModes.Overflow;
        answers.color = new Color(0.15f, 0.24f, 0.38f, 1f);

        TMP_Text explanation = FindOrCreateText(cardGo.transform, "Explanation", new Vector2(0.04f, 0.02f), new Vector2(0.76f, 0.19f), 16f, FontStyles.Italic, TextAlignmentOptions.TopLeft);
        explanation.text = string.IsNullOrWhiteSpace(entry.explanation) ? "No explanation available." : entry.explanation;
        explanation.textWrappingMode = TextWrappingModes.Normal;
        explanation.overflowMode = TextOverflowModes.Overflow;
        explanation.color = new Color(0.27f, 0.39f, 0.56f, 1f);

        Button bookmarkButton = FindOrCreateButton(rect, $"BookmarkButton_{entry.questionIndex}", "Save", new Vector2(0.79f, 0.04f), new Vector2(0.96f, 0.18f));
        SetButtonBaseColor(bookmarkButton, SecondaryButtonColor);
        bookmarkButton.onClick.RemoveAllListeners();
        bookmarkButton.onClick.AddListener(() => SaveReviewQuestionBookmark(entry));
    }

    private void SaveReviewQuestionBookmark(QuizReviewEntry entry)
    {
        if (entry == null || !AppStateManager.IsAvailable)
        {
            return;
        }

        CourseStateSnapshot courseState = AppStateManager.Instance.CurrentCourse;
        LessonStateSnapshot lessonState = AppStateManager.Instance.CurrentLesson;
        if (string.IsNullOrWhiteSpace(courseState.courseId) || string.IsNullOrWhiteSpace(lessonState.lessonId))
        {
            return;
        }

        CourseData course = new CourseData { id = courseState.courseId, title = courseState.courseTitle };
        LessonData lesson = new LessonData { id = lessonState.lessonId, title = lessonState.lessonTitle };
        StudyBookmarkData bookmark = LocalStudyStateManager.BuildBookmark(
            AppStateManager.Instance.CurrentUserId,
            course,
            lesson,
            "quiz",
            "Quiz Review",
            lessonState.sectionIndex,
            lessonState.lessonIndex,
            questionIndex: entry.questionIndex);

        bool saved = LocalStudyStateManager.ToggleBookmark(AppStateManager.Instance.CurrentUserId, bookmark);
        ToastManager.ShowInfo(saved ? $"Bookmarked question {entry.questionIndex + 1}." : $"Removed bookmark for question {entry.questionIndex + 1}.", 2.6f);
    }

    private static void EnsurePanelChrome(Transform panel)
    {
        Image panelImage = panel.GetComponent<Image>();
        if (panelImage != null)
        {
            panelImage.color = PanelColor;
        }

        Image headerBg = FindOrCreateFill(panel, "HeaderBg", new Vector2(0f, 0.84f), new Vector2(1f, 1f), HeaderColor);
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

    private static TMP_Text FindOrCreateText(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, float fontSize, FontStyles style, TextAlignmentOptions alignment)
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
        text.color = new Color(0.17f, 0.28f, 0.43f, 1f);
        text.alignment = alignment;
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
        image.color = OptionDefaultColor;

        Button button = existing.GetComponent<Button>();

        TextMeshProUGUI text = existing.GetComponentInChildren<TextMeshProUGUI>(true);
        text.text = label;
        text.color = new Color(0.15f, 0.3f, 0.49f, 1f);
        text.fontSize = 28f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;

        RectTransform textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return button;
    }

    private static void SetButtonBaseColor(Button button, Color color)
    {
        if (button == null) return;
        Image image = button.GetComponent<Image>();
        if (image != null)
        {
            image.color = color;
        }

        ColorBlock colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.22f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = Color.Lerp(color, Color.black, 0.16f);
        colors.disabledColor = new Color(color.r, color.g, color.b, color.a * 0.55f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;
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



