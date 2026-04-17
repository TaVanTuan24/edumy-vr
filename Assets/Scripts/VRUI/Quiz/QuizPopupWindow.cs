using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[DisallowMultipleComponent]
public class QuizPopupWindow : MonoBehaviour
{
    private static readonly Color OptionDefaultColor = new Color(0.92f, 0.96f, 1f, 0.98f);
    private static readonly Color OptionCorrectColor = new Color(0.894f, 0.98f, 0.933f, 0.98f);
    private static readonly Color OptionWrongColor = new Color(1f, 0.933f, 0.933f, 0.98f);
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
    [SerializeField] private bool followViewerWhileVisible = true;
    [SerializeField] private float horizontalOffset = -0.35f;
    [SerializeField] private float additionalHeightOffset = -0.35f;
    [SerializeField] private bool flipForwardToFaceViewer = true;

    private Canvas canvas;
    private RectTransform rootRect;
    private TMP_Text titleText;
    private TMP_Text indicatorText;
    private TMP_Text questionText;
    private TMP_Text feedbackText;
    private Button closeButton;
    private Button nextButton;
    private Button pinButton;
    private readonly List<Button> optionButtons = new List<Button>();

    private readonly List<QuizQuestionData> activeQuizQuestions = new List<QuizQuestionData>();
    private readonly Dictionary<int, int> selectedByQuestion = new Dictionary<int, int>();
    private int currentQuestionIndex;
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
        if (lesson == null) return false;
        string t = (lesson.type ?? string.Empty).Trim().ToLowerInvariant();
        if (t.Contains("quiz") || t.Contains("question")) return true;
        if (lesson.quizQuestions != null && lesson.quizQuestions.Count > 0) return true;
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

        EnsureWindowExists(false);
        if (windowTransform == null || canvas == null) return false;

        activeQuizQuestions.Clear();
        activeQuizQuestions.AddRange(BuildQuizForLesson(lesson));
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

        titleText = FindOrCreateText(panel, "Title", new Vector2(0.05f, 0.9f), new Vector2(0.76f, 0.975f), 36, FontStyles.Bold, TextAlignmentOptions.Left);
        titleText.color = TitleTextColor;
        indicatorText = FindOrCreateText(panel, "Indicator", new Vector2(0.05f, 0.84f), new Vector2(0.45f, 0.9f), 22, FontStyles.Normal, TextAlignmentOptions.Left);
        indicatorText.color = new Color(0.34f, 0.43f, 0.58f, 1f);
        questionText = FindOrCreateText(panel, "Question", new Vector2(0.05f, 0.64f), new Vector2(0.95f, 0.82f), 27, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        questionText.textWrappingMode = TextWrappingModes.Normal;
        questionText.color = new Color(0.15f, 0.24f, 0.38f, 1f);

        feedbackText = FindOrCreateText(panel, "Feedback", new Vector2(0.05f, 0.14f), new Vector2(0.95f, 0.24f), 22, FontStyles.Italic, TextAlignmentOptions.TopLeft);
        feedbackText.textWrappingMode = TextWrappingModes.Normal;
        feedbackText.color = new Color(0.27f, 0.39f, 0.56f, 1f);

        closeButton = FindOrCreateButton(panelRect, "CloseButton", "Close", new Vector2(0.78f, 0.9f), new Vector2(0.95f, 0.975f));
        pinButton = FindOrCreateButton(panelRect, "PinButton", "📌", new Vector2(0.68f, 0.9f), new Vector2(0.77f, 0.975f));
        nextButton = FindOrCreateButton(panelRect, "NextButton", "Next", new Vector2(0.73f, 0.03f), new Vector2(0.95f, 0.11f));
        SetButtonBaseColor(closeButton, SecondaryButtonColor);
        SetButtonBaseColor(pinButton, SecondaryButtonColor);
        SetButtonBaseColor(nextButton, ActionButtonColor);

        optionButtons.Clear();
        float top = 0.6f;
        float height = 0.1f;
        for (int i = 0; i < 4; i++)
        {
            float yMax = top - i * 0.11f;
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
        if (nextButton != null) nextButton.onClick.AddListener(NextStep);

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

        SetButtonBaseColor(pinButton, spatialWindow.IsPinned ? ActionButtonColor : SecondaryButtonColor);
    }

    private List<QuizQuestionData> BuildQuizForLesson(LessonData lesson)
    {
        List<QuizQuestionData> list = new List<QuizQuestionData>();

        List<QuizQuestionData> source = new List<QuizQuestionData>();
        if (lesson != null)
        {
            AddQuestions(source, lesson.quizQuestions);
            AddQuestions(source, lesson.questions);
            AddQuestions(source, lesson.quizzes);
            if (lesson.quizQuestion != null)
            {
                source.Add(lesson.quizQuestion);
            }
        }

        if (source.Count > 0)
        {
            foreach (QuizQuestionData q in source)
            {
                if (q == null) continue;

                string questionText = FirstNonEmpty(q.question, q.text, q.prompt);
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

                int maxCorrect = Mathf.Max(0, normalizedOptions.Count - 1);
                int rawCorrectIndex = q.correctIndex;
                if (rawCorrectIndex <= 0 && q.correctAnswer > 0)
                {
                    // Some backends send correctAnswer as 1-based index.
                    rawCorrectIndex = q.correctAnswer - 1;
                }
                int correctedIndex = Mathf.Clamp(rawCorrectIndex, 0, maxCorrect);

                list.Add(new QuizQuestionData
                {
                    question = question,
                    options = normalizedOptions,
                    correctIndex = correctedIndex
                });
            }
        }

        if (list.Count == 0)
        {
            string baseTitle = string.IsNullOrWhiteSpace(lesson != null ? lesson.title : null) ? "Quiz" : lesson.title;
            for (int i = 1; i <= 5; i++)
            {
                list.Add(new QuizQuestionData
                {
                    question = $"{baseTitle} - Question {i}",
                    options = new List<string> { "Option A", "Option B", "Option C", "Option D" },
                    correctIndex = i % 4
                });
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

    private void RenderQuestion()
    {
        if (activeQuizQuestions.Count == 0)
        {
            if (indicatorText != null) indicatorText.text = "Question 0/0";
            if (questionText != null) questionText.text = "No quiz data.";
            if (feedbackText != null) feedbackText.text = string.Empty;
            if (nextButton != null) nextButton.interactable = false;
            return;
        }

        if (currentQuestionIndex >= activeQuizQuestions.Count)
        {
            RenderSummary();
            return;
        }

        QuizQuestionData q = activeQuizQuestions[currentQuestionIndex];
        if (indicatorText != null) indicatorText.text = $"Question {currentQuestionIndex + 1}/{activeQuizQuestions.Count}";
        if (questionText != null) questionText.text = q.question;

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
                }
                else if (i == correctIndex)
                {
                    image.color = OptionCorrectColor;
                }
                else if (i == selectedIndex && selectedIndex != correctIndex)
                {
                    image.color = OptionWrongColor;
                }
                else
                {
                    image.color = OptionDefaultColor;
                }
            }

            b.interactable = !hasAnswered;
        }

        if (feedbackText != null)
        {
            if (!hasAnswered)
            {
                feedbackText.text = "Select one answer.";
            }
            else if (selectedIndex == correctIndex)
            {
                feedbackText.text = "Chinh xac!";
            }
            else
            {
                string correctAnswerText = (correctIndex >= 0 && correctIndex < q.options.Count)
                    ? q.options[correctIndex]
                    : "(unknown)";
                feedbackText.text = $"Incorrect. Correct answer: {correctAnswerText}";
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
    }

    private void RenderSummary()
    {
        int total = activeQuizQuestions.Count;
        int score = 0;

        for (int i = 0; i < total; i++)
        {
            QuizQuestionData q = activeQuizQuestions[i];
            if (q == null) continue;
            if (selectedByQuestion.TryGetValue(i, out int selected) && selected == q.correctIndex)
            {
                score++;
            }
        }

        if (indicatorText != null) indicatorText.text = "Summary";
        if (questionText != null) questionText.text = $"Ket qua: {score}/{total}";
        if (feedbackText != null)
        {
            feedbackText.text = score >= Mathf.CeilToInt(total * 0.7f)
                ? "Ban da vuot quiz."
                : "Ban chua dat. Thu lai nhe.";
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

    private static void SetButtonBaseColor(Button button, Color color)
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
}

