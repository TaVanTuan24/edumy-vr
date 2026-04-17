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
        return BuildQuizForLesson(lesson).Count > 0;
    }

    public bool Open(LessonData lesson)
    {
        if (quizPage == null || lesson == null) return false;

        activeQuizQuestions.Clear();
        activeQuizQuestions.AddRange(BuildQuizForLesson(lesson));
        Debug.Log($"[QuizPopupPanel] quiz source counts lesson={lesson.id} quizQuestions={(lesson.quizQuestions != null ? lesson.quizQuestions.Count : 0)} questions={(lesson.questions != null ? lesson.questions.Count : 0)} quizzes={(lesson.quizzes != null ? lesson.quizzes.Count : 0)} final={activeQuizQuestions.Count}");
        if (activeQuizQuestions.Count == 0) return false;
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
        List<QuizQuestionData> source = SelectStandaloneQuizSource(lesson);
        if (source != null)
        {
            foreach (QuizQuestionData q in source)
            {
                string text = FirstNonEmpty(q != null ? q.question : null, q != null ? q.prompt : null, q != null ? q.text : null);
                if (q == null || string.IsNullOrWhiteSpace(text)) continue;
                if (q.options == null || q.options.Count == 0)
                {
                    if (q.answers != null && q.answers.Count > 0) q.options = new List<string>(q.answers);
                    else if (q.choices != null && q.choices.Count > 0) q.options = new List<string>(q.choices);
                }
                if (q.options == null || q.options.Count == 0) continue;
                q.question = text.Trim();
                list.Add(q);
            }
        }

        return list;
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

    private static bool HasValidStandaloneQuizQuestions(List<QuizQuestionData> source)
    {
        if (source == null || source.Count == 0) return false;
        foreach (QuizQuestionData question in source)
        {
            if (question == null) continue;
            string text = FirstNonEmpty(question.question, question.prompt, question.text);
            if (string.IsNullOrWhiteSpace(text)) continue;
            int optionsCount = question.options != null && question.options.Count > 0
                ? question.options.Count
                : (question.answers != null && question.answers.Count > 0 ? question.answers.Count : (question.choices != null ? question.choices.Count : 0));
            if (optionsCount > 0) return true;
        }
        return false;
    }

    private static List<QuizQuestionData> SelectStandaloneQuizSource(LessonData lesson)
    {
        if (lesson == null) return new List<QuizQuestionData>();
        if (HasValidStandaloneQuizQuestions(lesson.quizQuestions)) return lesson.quizQuestions;
        if (HasValidStandaloneQuizQuestions(lesson.questions)) return lesson.questions;
        if (HasValidStandaloneQuizQuestions(lesson.quizzes)) return lesson.quizzes;
        if (lesson.quizQuestion != null && !string.IsNullOrWhiteSpace(FirstNonEmpty(lesson.quizQuestion.question, lesson.quizQuestion.prompt, lesson.quizQuestion.text)))
        {
            return new List<QuizQuestionData> { lesson.quizQuestion };
        }
        return new List<QuizQuestionData>();
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
                quizFeedback.text = "Select an answer before moving to the next question.";
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
            if (quizQuestion != null) quizQuestion.text = "No quiz data.";
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
            string text = FirstNonEmpty(q != null ? q.question : null, q != null ? q.prompt : null, q != null ? q.text : null);
            quizQuestion.text = string.IsNullOrWhiteSpace(text) ? "Question" : text;
            Debug.Log($"[QuizPopupPanel] render pass questionIndex={currentQuizQuestionIndex} chosenText='{quizQuestion.text}'");
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
            quizFeedback.text = selectedIndex >= 0 ? "Answer selected. Press Next to continue." : "Select one answer.";
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
            quizQuestion.text = $"Quiz complete. Score: {score}/{total}";
        }

        foreach (Button b in quizOptionButtons)
        {
            if (b == null) continue;
            b.style.display = DisplayStyle.None;
        }

        if (quizFeedback != null)
        {
            quizFeedback.text = $"Summary: {score}/{total}";
        }

        if (quizNextButton != null)
        {
            quizNextButton.SetEnabled(false);
            quizNextButton.text = "Finished";
        }
    }
}
