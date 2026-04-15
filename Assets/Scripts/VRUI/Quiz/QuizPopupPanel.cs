using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
public class QuizPopupPanel : MonoBehaviour
{
    private VisualElement quizPage;
    private Button quizCloseButton;
    private Button quizNextButton;
    private Label quizTitle;
    private Label quizPageIndicator;
    private Label quizQuestion;
    private Label quizFeedback;
    private readonly List<Button> quizOptionButtons = new List<Button>();

    private readonly List<QuizQuestionData> activeQuizQuestions = new List<QuizQuestionData>();
    private readonly Dictionary<int, int> quizSelectedByQuestionIndex = new Dictionary<int, int>();
    private int currentQuizQuestionIndex;
    private Action onClose;
    private bool eventsBound;

    public void Initialize(VisualElement root, Action closeAction)
    {
        onClose = closeAction;
        if (root == null) return;

        quizPage = root.Q<VisualElement>("quiz-page");
        quizCloseButton = root.Q<Button>("quiz-close-button");
        quizNextButton = root.Q<Button>("quiz-next-button");
        quizTitle = root.Q<Label>("quiz-title");
        quizPageIndicator = root.Q<Label>("quiz-page-indicator");
        quizQuestion = root.Q<Label>("quiz-question");
        quizFeedback = root.Q<Label>("quiz-feedback");

        quizOptionButtons.Clear();
        quizOptionButtons.Add(root.Q<Button>("quiz-option-0"));
        quizOptionButtons.Add(root.Q<Button>("quiz-option-1"));
        quizOptionButtons.Add(root.Q<Button>("quiz-option-2"));
        quizOptionButtons.Add(root.Q<Button>("quiz-option-3"));

        EnsureQuizOptionButtons(root);

        if (!eventsBound)
        {
            if (quizCloseButton != null)
            {
                quizCloseButton.clicked += CloseInternal;
            }

            if (quizNextButton != null)
            {
                quizNextButton.clicked += MoveNextQuizStep;
            }

            for (int i = 0; i < quizOptionButtons.Count; i++)
            {
                int index = i;
                Button button = quizOptionButtons[i];
                if (button == null) continue;
                button.clicked += () => SelectQuizOption(index);
            }

            eventsBound = true;
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

    public bool Open(LessonData lesson)
    {
        if (quizPage == null || lesson == null) return false;

        activeQuizQuestions.Clear();
        activeQuizQuestions.AddRange(BuildQuizForLesson(lesson));
        quizSelectedByQuestionIndex.Clear();
        currentQuizQuestionIndex = 0;

        if (quizTitle != null)
        {
            quizTitle.text = string.IsNullOrWhiteSpace(lesson.title) ? "Quiz" : lesson.title;
        }

        RenderQuizQuestion();
        quizPage.RemoveFromClassList("hidden");
        return true;
    }

    private void EnsureQuizOptionButtons(VisualElement root)
    {
        bool hasAny = false;
        for (int i = 0; i < quizOptionButtons.Count; i++)
        {
            if (quizOptionButtons[i] != null)
            {
                hasAny = true;
                break;
            }
        }

        if (hasAny) return;

        VisualElement container = quizQuestion != null ? quizQuestion.parent : null;
        if (container == null)
        {
            container = root.Q<VisualElement>("quiz-page");
        }

        if (container == null) return;

        quizOptionButtons.Clear();
        for (int i = 0; i < 4; i++)
        {
            Button button = new Button { name = $"quiz-option-{i}", text = $"Option {i + 1}" };
            button.AddToClassList("quiz-option");
            container.Add(button);
            quizOptionButtons.Add(button);
        }

        Debug.LogWarning("[QuizPopupPanel] Quiz option buttons were missing in UXML. Created fallback option buttons at runtime.");
    }

    public void Hide()
    {
        if (quizPage != null)
        {
            quizPage.AddToClassList("hidden");
        }
    }

    private void CloseInternal()
    {
        Hide();
        onClose?.Invoke();
    }

    private List<QuizQuestionData> BuildQuizForLesson(LessonData lesson)
    {
        List<QuizQuestionData> list = new List<QuizQuestionData>();

        if (lesson != null && lesson.quizQuestions != null)
        {
            foreach (QuizQuestionData q in lesson.quizQuestions)
            {
                if (q == null || string.IsNullOrWhiteSpace(q.question)) continue;
                if (q.options == null || q.options.Count == 0) continue;
                list.Add(q);
            }
        }

        if (list.Count == 0)
        {
            string baseTitle = string.IsNullOrWhiteSpace(lesson != null ? lesson.title : null) ? "Quiz" : lesson.title;
            for (int i = 1; i <= 10; i++)
            {
                list.Add(new QuizQuestionData
                {
                    question = $"{baseTitle} - Câu hỏi {i}: Chọn đáp án đúng.",
                    options = new List<string> { "Đáp án A", "Đáp án B", "Đáp án C", "Đáp án D" },
                    correctIndex = i % 4
                });
            }
        }

        return list;
    }

    private void SelectQuizOption(int optionIndex)
    {
        if (activeQuizQuestions.Count == 0) return;
        if (currentQuizQuestionIndex >= activeQuizQuestions.Count) return;

        QuizQuestionData q = activeQuizQuestions[currentQuizQuestionIndex];
        if (q == null || q.options == null || optionIndex < 0 || optionIndex >= q.options.Count) return;

        quizSelectedByQuestionIndex[currentQuizQuestionIndex] = optionIndex;
        RenderQuizQuestion();
    }

    private void MoveNextQuizStep()
    {
        if (activeQuizQuestions.Count == 0) return;

        if (!quizSelectedByQuestionIndex.ContainsKey(currentQuizQuestionIndex))
        {
            if (quizFeedback != null)
            {
                quizFeedback.text = "Bạn cần chọn đáp án trước khi sang câu tiếp theo.";
            }
            return;
        }

        currentQuizQuestionIndex++;
        RenderQuizQuestion();
    }

    private void RenderQuizQuestion()
    {
        if (activeQuizQuestions.Count == 0)
        {
            if (quizQuestion != null) quizQuestion.text = "Không có dữ liệu quiz.";
            if (quizPageIndicator != null) quizPageIndicator.text = "Question 0/0";
            if (quizNextButton != null) quizNextButton.SetEnabled(false);
            return;
        }

        if (currentQuizQuestionIndex >= activeQuizQuestions.Count)
        {
            ShowQuizSummary();
            return;
        }

        QuizQuestionData q = activeQuizQuestions[currentQuizQuestionIndex];
        if (quizPageIndicator != null)
        {
            quizPageIndicator.text = $"Question {currentQuizQuestionIndex + 1}/{activeQuizQuestions.Count}";
        }

        if (quizQuestion != null)
        {
            quizQuestion.text = q.question;
        }

        int selectedIndex = quizSelectedByQuestionIndex.TryGetValue(currentQuizQuestionIndex, out int selected) ? selected : -1;

        for (int i = 0; i < quizOptionButtons.Count; i++)
        {
            Button b = quizOptionButtons[i];
            if (b == null) continue;

            bool hasOption = q.options != null && i < q.options.Count;
            if (!hasOption)
            {
                b.style.display = DisplayStyle.None;
                continue;
            }

            b.style.display = DisplayStyle.Flex;
            b.text = q.options[i];
            if (i == selectedIndex) b.AddToClassList("is-selected");
            else b.RemoveFromClassList("is-selected");
        }

        if (quizFeedback != null)
        {
            quizFeedback.text = selectedIndex >= 0 ? "Đã chọn đáp án. Bấm Next để qua câu tiếp." : "Chọn 1 đáp án.";
        }

        if (quizNextButton != null)
        {
            quizNextButton.text = currentQuizQuestionIndex == activeQuizQuestions.Count - 1 ? "Finish" : "Next Question";
            quizNextButton.SetEnabled(selectedIndex >= 0);
        }
    }

    private void ShowQuizSummary()
    {
        int total = activeQuizQuestions.Count;
        int score = 0;

        for (int i = 0; i < total; i++)
        {
            QuizQuestionData q = activeQuizQuestions[i];
            if (q == null) continue;
            if (quizSelectedByQuestionIndex.TryGetValue(i, out int selected) && selected == q.correctIndex)
            {
                score++;
            }
        }

        if (quizPageIndicator != null)
        {
            quizPageIndicator.text = "Quiz Result";
        }

        if (quizQuestion != null)
        {
            quizQuestion.text = $"Hoàn thành quiz. Kết quả: {score}/{total}";
        }

        foreach (Button b in quizOptionButtons)
        {
            if (b == null) continue;
            b.style.display = DisplayStyle.None;
        }

        if (quizFeedback != null)
        {
            quizFeedback.text = $"Tổng kết: {score}/{total}";
        }

        if (quizNextButton != null)
        {
            quizNextButton.SetEnabled(false);
            quizNextButton.text = "Finished";
        }
    }
}
