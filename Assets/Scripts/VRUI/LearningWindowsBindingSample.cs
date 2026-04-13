using UnityEngine;
using UnityEngine.UIElements;

public class LearningWindowsBindingSample : MonoBehaviour
{
    [SerializeField] private UIDocument quizDocument;
    [SerializeField] private UIDocument slideDocument;
    [SerializeField] private UIDocument videoDocument;

    private VisualElement quizRoot;
    private VisualElement slideRoot;
    private VisualElement videoRoot;

    private Label quizProgressLabel;
    private Label quizFeedbackLabel;
    private Label quizFeedbackChip;
    private ProgressBar quizProgressBar;

    private Label slideCounterLabel;
    private ProgressBar slideProgressBar;
    private Label videoStatusLabel;

    private void Awake()
    {
        CacheQuiz();
        CacheSlide();
        CacheVideo();
    }

    public void ShowQuizOnly()
    {
        SetDisplay(quizRoot, true);
        SetDisplay(slideRoot, false);
        SetDisplay(videoRoot, false);
    }

    public void ShowSlideOnly()
    {
        SetDisplay(quizRoot, false);
        SetDisplay(slideRoot, true);
        SetDisplay(videoRoot, false);
    }

    public void ShowVideoOnly()
    {
        SetDisplay(quizRoot, false);
        SetDisplay(slideRoot, false);
        SetDisplay(videoRoot, true);
    }

    public void UpdateQuizProgress(int currentQuestion, int totalQuestions)
    {
        int safeTotal = Mathf.Max(1, totalQuestions);
        int safeCurrent = Mathf.Clamp(currentQuestion, 0, safeTotal);

        if (quizProgressLabel != null)
        {
            quizProgressLabel.text = $"Question {safeCurrent} / {safeTotal}";
        }

        if (quizProgressBar != null)
        {
            quizProgressBar.lowValue = 0f;
            quizProgressBar.highValue = safeTotal;
            quizProgressBar.value = safeCurrent;
        }
    }

    public void SetQuizFeedback(bool isCorrect, string message)
    {
        if (quizFeedbackLabel == null || quizFeedbackChip == null) return;

        quizFeedbackLabel.text = string.IsNullOrWhiteSpace(message)
            ? (isCorrect ? "Correct" : "Incorrect")
            : message;

        quizFeedbackChip.text = isCorrect ? "Correct" : "Incorrect";

        quizFeedbackLabel.EnableInClassList("is-correct", isCorrect);
        quizFeedbackLabel.EnableInClassList("is-wrong", !isCorrect);
    }

    public void SetAnswerState(VisualElement answerCard, bool selected, bool correct, bool wrong)
    {
        if (answerCard == null) return;
        answerCard.EnableInClassList("is-selected", selected);
        answerCard.EnableInClassList("is-correct", correct);
        answerCard.EnableInClassList("is-wrong", wrong);
        answerCard.EnableInClassList("is-disabled", !selected && (correct || wrong));
    }

    public void UpdateSlideProgress(int currentSlide, int totalSlides)
    {
        int safeTotal = Mathf.Max(1, totalSlides);
        int safeCurrent = Mathf.Clamp(currentSlide, 0, safeTotal);

        if (slideCounterLabel != null)
        {
            slideCounterLabel.text = $"Slide {safeCurrent} / {safeTotal}";
        }

        if (slideProgressBar != null)
        {
            slideProgressBar.lowValue = 0f;
            slideProgressBar.highValue = safeTotal;
            slideProgressBar.value = safeCurrent;
        }
    }

    public void UpdateVideoStatus(string status)
    {
        if (videoStatusLabel == null) return;
        videoStatusLabel.text = string.IsNullOrWhiteSpace(status) ? "Ready" : status;
    }

    private void CacheQuiz()
    {
        quizRoot = quizDocument != null ? quizDocument.rootVisualElement.Q<VisualElement>("quiz-window") : null;
        if (quizRoot == null) return;

        quizProgressLabel = quizRoot.Q<Label>("quiz-progress-label");
        quizProgressBar = quizRoot.Q<ProgressBar>("quiz-progress");
        quizFeedbackLabel = quizRoot.Q<Label>("quiz-feedback");
        quizFeedbackChip = quizRoot.Q<Label>("quiz-feedback-chip");
    }

    private void CacheSlide()
    {
        slideRoot = slideDocument != null ? slideDocument.rootVisualElement.Q<VisualElement>("slide-window") : null;
        if (slideRoot == null) return;

        slideCounterLabel = slideRoot.Q<Label>("slide-counter");
        slideProgressBar = slideRoot.Q<ProgressBar>("slide-progress");
    }

    private void CacheVideo()
    {
        videoRoot = videoDocument != null ? videoDocument.rootVisualElement.Q<VisualElement>("video-window") : null;
        if (videoRoot == null) return;

        videoStatusLabel = videoRoot.Q<Label>("video-status");
    }

    private static void SetDisplay(VisualElement element, bool visible)
    {
        if (element == null) return;
        element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}