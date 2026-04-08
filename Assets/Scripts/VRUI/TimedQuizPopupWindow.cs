using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[ExecuteAlways]
[DisallowMultipleComponent]
public class TimedQuizPopupWindow : MonoBehaviour
{
    private static readonly Color OptionDefaultColor = new Color(0.18f, 0.38f, 0.62f, 0.97f);
    private static readonly Color OptionCorrectColor = new Color(0.16f, 0.58f, 0.34f, 0.98f);
    private static readonly Color OptionWrongColor = new Color(0.74f, 0.24f, 0.24f, 0.98f);

    [SerializeField] private Transform windowTransform;
    [SerializeField] private Vector2 canvasSize = new Vector2(980f, 620f);
    [SerializeField] private Vector3 windowScale = new Vector3(0.00125f, 0.00125f, 0.00125f);
    [SerializeField] private bool keepPlacedTransformOnShow = true;
    [SerializeField] private bool autoCreateWindowInEditor = true;
    [SerializeField] private bool showPreviewInEditor = true;
    [SerializeField] private bool autoPlaceInFrontWhenPlaying = false;
    [SerializeField] private bool flipForwardToFaceViewer = true;
    [SerializeField, Min(0.1f)] private float autoCloseDelayCorrect = 2f;
    [SerializeField, Min(0.1f)] private float autoCloseDelayWrong = 5f;

    private Canvas canvas;
    private RectTransform rootRect;
    private TMP_Text titleText;
    private TMP_Text questionText;
    private TMP_Text feedbackText;
    private TMP_Text timerText;
    private Button closeButton;
    private readonly List<Button> optionButtons = new List<Button>();

    private TimedQuizData activeQuiz;
    private int selectedIndex = -1;
    private bool answered;
    private bool bindingsAdded;
    private bool createdRuntimeWindow;
    private Coroutine autoCloseCoroutine;

    public event Action<bool> OnWindowClosed;

    private void Awake()
    {
        if (windowTransform == null)
        {
            Transform existing = transform.Find("TimedQuizPopupWindowRoot");
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

    public bool Show(TimedQuizData quiz, Transform viewer, float distance, float heightOffset)
    {
        if (quiz == null) return false;

        EnsureWindowExists(false);
        if (windowTransform == null || canvas == null) return false;

        activeQuiz = quiz;
        selectedIndex = -1;
        answered = false;
        StopAutoClose();

        if (titleText != null)
        {
            titleText.text = "Quiz Trong Video";
        }

        if (timerText != null)
        {
            timerText.text = $"Moc thoi gian: {Mathf.Max(0f, GetTriggerSeconds(quiz)):0.0}s";
        }

        bool shouldAutoPlace = !keepPlacedTransformOnShow
            || (createdRuntimeWindow && Application.isPlaying)
            || (Application.isPlaying && autoPlaceInFrontWhenPlaying);
        if (shouldAutoPlace)
        {
            PositionWindow(viewer, distance, heightOffset);
        }

        windowTransform.gameObject.SetActive(true);
        RenderQuiz();
        return true;
    }

    public void HideWindow(bool resumeVideo = true)
    {
        StopAutoClose();

        if (windowTransform != null)
        {
            windowTransform.gameObject.SetActive(false);
        }

        OnWindowClosed?.Invoke(resumeVideo);
    }

    private void EnsureWindowExists(bool forceEditorCreation)
    {
        if (windowTransform == null)
        {
            if (!Application.isPlaying && !forceEditorCreation) return;

            GameObject root = new GameObject("TimedQuizPopupWindowRoot", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            windowTransform = root.transform;
            createdRuntimeWindow = true;
        }

        windowTransform = EnsureRectTransformRoot(windowTransform);
        if (windowTransform == null)
        {
            Debug.LogError("[TimedQuizPopupWindow] Could not create a valid RectTransform root.");
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
            Debug.LogError("[TimedQuizPopupWindow] Missing RectTransform on canvas root.");
            return;
        }
        rootRect.sizeDelta = canvasSize;
        windowTransform.localScale = windowScale;

        BuildUiIfMissing();
        BindEventsOnce();
        EnsureInteractionSupport();
    }

    private void EnsureInteractionSupport()
    {
        if (windowTransform != null)
        {
            if (windowTransform.GetComponent<GraphicRaycaster>() == null)
            {
                windowTransform.gameObject.AddComponent<GraphicRaycaster>();
            }

            // XR UI ray interaction support when XR Interaction Toolkit is installed.
            TryAddComponentByTypeName(
                windowTransform.gameObject,
                "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
        }

        if (!Application.isPlaying) return;

        EventSystem eventSystem = FindAnyObjectByType<EventSystem>();
        if (eventSystem == null)
        {
            GameObject go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            eventSystem = go.GetComponent<EventSystem>();
        }

        if (eventSystem != null)
        {
            // Prefer Input System UI module if package exists.
            TryAddComponentByTypeName(
                eventSystem.gameObject,
                "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");

            if (eventSystem.GetComponent<BaseInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<StandaloneInputModule>();
            }
        }
    }

    private static void TryAddComponentByTypeName(GameObject go, string assemblyQualifiedTypeName)
    {
        if (go == null || string.IsNullOrWhiteSpace(assemblyQualifiedTypeName)) return;

        Type type = Type.GetType(assemblyQualifiedTypeName, false);
        if (type == null) return;
        if (!typeof(Component).IsAssignableFrom(type)) return;
        if (go.GetComponent(type) != null) return;

        go.AddComponent(type);
    }

    private void BuildUiIfMissing()
    {
        Transform panel = windowTransform.Find("Panel");
        if (panel == null)
        {
            panel = CreatePanel(windowTransform).transform;
        }

        RectTransform panelRect = panel.GetComponent<RectTransform>();

        titleText = FindOrCreateText(panel, "Title", new Vector2(0.04f, 0.88f), new Vector2(0.76f, 0.98f), 38, FontStyles.Bold, TextAlignmentOptions.Left);
        timerText = FindOrCreateText(panel, "Timer", new Vector2(0.04f, 0.81f), new Vector2(0.96f, 0.89f), 22, FontStyles.Normal, TextAlignmentOptions.Left);
        questionText = FindOrCreateText(panel, "Question", new Vector2(0.04f, 0.58f), new Vector2(0.96f, 0.8f), 30, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        questionText.textWrappingMode = TextWrappingModes.Normal;

        feedbackText = FindOrCreateText(panel, "Feedback", new Vector2(0.04f, 0.1f), new Vector2(0.96f, 0.22f), 24, FontStyles.Italic, TextAlignmentOptions.TopLeft);
        feedbackText.textWrappingMode = TextWrappingModes.Normal;

        closeButton = FindOrCreateButton(panelRect, "CloseButton", "Continue", new Vector2(0.8f, 0.9f), new Vector2(0.96f, 0.98f));

        optionButtons.Clear();
        for (int i = 0; i < 4; i++)
        {
            float yTop = 0.54f - (i * 0.11f);
            float yBottom = yTop - 0.095f;
            Button b = FindOrCreateButton(panelRect, $"Option{i}", $"Option {i + 1}", new Vector2(0.04f, yBottom), new Vector2(0.96f, yTop));
            optionButtons.Add(b);
        }
    }

    private void BindEventsOnce()
    {
        if (bindingsAdded) return;

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(() => HideWindow(true));
        }

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
    }

    private void SelectOption(int optionIndex)
    {
        if (activeQuiz == null || answered) return;

        List<string> options = GetOptions(activeQuiz);
        if (options == null || optionIndex < 0 || optionIndex >= options.Count) return;

        selectedIndex = optionIndex;
        answered = true;
        RenderQuiz();

        int correctIndex = ResolveCorrectIndex(activeQuiz, options.Count);
        bool isCorrect = selectedIndex == correctIndex;
        float delay = isCorrect ? Mathf.Max(0.1f, autoCloseDelayCorrect) : Mathf.Max(0.1f, autoCloseDelayWrong);
        StartAutoClose(delay);
    }

    private void RenderQuiz()
    {
        if (activeQuiz == null)
        {
            if (questionText != null) questionText.text = "Khong co du lieu quiz.";
            if (feedbackText != null) feedbackText.text = string.Empty;
            if (closeButton != null) closeButton.interactable = true;
            return;
        }

        List<string> options = GetOptions(activeQuiz);
        string question = FirstNonEmpty(activeQuiz.question, activeQuiz.text, activeQuiz.prompt);
        if (string.IsNullOrWhiteSpace(question)) question = "Cau hoi";

        if (questionText != null)
        {
            questionText.text = question.Trim();
        }

        int correctIndex = ResolveCorrectIndex(activeQuiz, options != null ? options.Count : 0);

        for (int i = 0; i < optionButtons.Count; i++)
        {
            Button b = optionButtons[i];
            if (b == null) continue;

            bool hasOption = options != null && i < options.Count;
            b.gameObject.SetActive(hasOption);
            if (!hasOption) continue;

            TextMeshProUGUI label = b.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.text = string.IsNullOrWhiteSpace(options[i]) ? $"Lua chon {i + 1}" : options[i].Trim();
            }

            Image image = b.GetComponent<Image>();
            if (image != null)
            {
                if (!answered)
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

            b.interactable = !answered;
        }

        if (feedbackText != null)
        {
            if (!answered)
            {
                feedbackText.text = "Chon 1 dap an de tiep tuc video.";
            }
            else if (selectedIndex == correctIndex)
            {
                feedbackText.text = "Chinh xac! Bam Continue de tiep tuc video.";
            }
            else
            {
                string correctText = (options != null && correctIndex >= 0 && correctIndex < options.Count)
                    ? options[correctIndex]
                    : "(khong xac dinh)";
                string explanation = GetExplanationText(activeQuiz);
                if (string.IsNullOrWhiteSpace(explanation))
                {
                    feedbackText.text = $"Sai. Dap an dung: {correctText}.";
                }
                else
                {
                    feedbackText.text = $"Sai. Dap an dung: {correctText}.\nGiai thich: {explanation}";
                }
            }
        }

        if (closeButton != null)
        {
            closeButton.interactable = answered || options == null || options.Count == 0;
        }

        if (answered && selectedIndex == correctIndex && feedbackText != null)
        {
            feedbackText.text = $"Chinh xac!";
        }
    }

    private static string GetExplanationText(TimedQuizData quiz)
    {
        if (quiz == null) return string.Empty;
        return FirstNonEmpty(
            quiz.explanation,
            quiz.explain,
            quiz.reason,
            quiz.solution,
            quiz.wrongExplanation
        ) ?? string.Empty;
    }

    private void StartAutoClose(float delay)
    {
        if (!Application.isPlaying) return;

        StopAutoClose();
        autoCloseCoroutine = StartCoroutine(AutoCloseAfter(delay));
    }

    private IEnumerator AutoCloseAfter(float delay)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, delay));
        autoCloseCoroutine = null;
        HideWindow(true);
    }

    private void StopAutoClose()
    {
        if (autoCloseCoroutine == null) return;
        if (Application.isPlaying)
        {
            StopCoroutine(autoCloseCoroutine);
        }
        autoCloseCoroutine = null;
    }

    private static List<string> GetOptions(TimedQuizData quiz)
    {
        if (quiz == null) return null;
        if (quiz.options != null && quiz.options.Count > 0) return quiz.options;
        if (quiz.answers != null && quiz.answers.Count > 0) return quiz.answers;
        if (quiz.choices != null && quiz.choices.Count > 0) return quiz.choices;
        return null;
    }

    private static int ResolveCorrectIndex(TimedQuizData quiz, int optionCount)
    {
        if (quiz == null || optionCount <= 0) return 0;

        int rawIndex = quiz.correctIndex;
        if (rawIndex <= 0 && quiz.correctAnswer > 0)
        {
            rawIndex = quiz.correctAnswer - 1;
        }

        return Mathf.Clamp(rawIndex, 0, Mathf.Max(0, optionCount - 1));
    }

    public static float GetTriggerSeconds(TimedQuizData quiz)
    {
        if (quiz == null) return 0f;

        if (quiz.triggerTimeSec > 0f) return quiz.triggerTimeSec;
        if (quiz.triggerTime > 0f) return quiz.triggerTime;
        if (quiz.time > 0f) return quiz.time;

        if (TryParseTimeString(quiz.timecode, out float parsedByCode)) return parsedByCode;
        if (TryParseTimeString(quiz.showAt, out float parsedByShowAt)) return parsedByShowAt;
        if (TryParseTimeString(quiz.timestamp, out float parsedByTimestamp)) return parsedByTimestamp;
        if (TryParseTimeString(quiz.startAt, out float parsedByStartAt)) return parsedByStartAt;

        return 0f;
    }

    private static bool TryParseTimeString(string value, out float seconds)
    {
        seconds = 0f;
        if (string.IsNullOrWhiteSpace(value)) return false;

        string trimmed = value.Trim();

        if (float.TryParse(trimmed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float numeric))
        {
            seconds = Mathf.Max(0f, numeric);
            return true;
        }

        string[] parts = trimmed.Split(':');
        if (parts.Length == 2)
        {
            if (int.TryParse(parts[0], out int mm) && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ss))
            {
                seconds = Mathf.Max(0f, (mm * 60f) + ss);
                return true;
            }
            return false;
        }

        if (parts.Length == 3)
        {
            if (int.TryParse(parts[0], out int hh)
                && int.TryParse(parts[1], out int mm)
                && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ss))
            {
                seconds = Mathf.Max(0f, (hh * 3600f) + (mm * 60f) + ss);
                return true;
            }
        }

        return false;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        if (values == null) return null;
        for (int i = 0; i < values.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(values[i])) return values[i];
        }
        return null;
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
        image.color = new Color(0.08f, 0.1f, 0.12f, 0.94f);
        return panel;
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
        text.color = Color.white;
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
        text.color = Color.white;
        text.fontSize = 25f;
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
