using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class QuizQuestionView
{
    private readonly VisualElement pageRoot;
    private readonly Label titleLabel;
    private readonly Label progressLabel;
    private readonly ProgressBar progressBar;
    private readonly Label questionLabel;
    private readonly Label feedbackLabel;
    private readonly VisualElement answersContainer;
    private readonly Button prevButton;
    private readonly Button nextButton;
    private readonly Button retryButton;

    private readonly List<QuizQuestionData> questions = new List<QuizQuestionData>();
    private readonly List<AnswerCard> answerCards = new List<AnswerCard>();
    private readonly Dictionary<int, int> selectedAnswers = new Dictionary<int, int>();

    private int currentIndex;
    private bool summaryMode;

    public QuizQuestionView(VisualElement pageRoot)
    {
        this.pageRoot = pageRoot;
        titleLabel = pageRoot.Q<Label>("quiz-window-title");
        progressLabel = pageRoot.Q<Label>("quiz-progress-label");
        progressBar = pageRoot.Q<ProgressBar>("quiz-progress");
        questionLabel = pageRoot.Q<Label>("quiz-question");
        feedbackLabel = pageRoot.Q<Label>("quiz-feedback");
        answersContainer = pageRoot.Q<VisualElement>("quiz-answers");
        prevButton = pageRoot.Q<Button>("quiz-prev-button");
        nextButton = pageRoot.Q<Button>("quiz-next-button");
        retryButton = pageRoot.Q<Button>("quiz-retry-button");

        if (prevButton != null) prevButton.clicked += Prev;
        if (nextButton != null) nextButton.clicked += Next;
        if (retryButton != null) retryButton.clicked += Retry;
    }

    public bool BindLesson(LessonData lesson)
    {
        summaryMode = false;
        currentIndex = 0;
        selectedAnswers.Clear();
        questions.Clear();

        if (titleLabel != null)
        {
            titleLabel.text = string.IsNullOrWhiteSpace(lesson != null ? lesson.title : null)
                ? "Quiz"
                : lesson.title;
        }

        AddQuestions(questions, SelectStandaloneQuizSource(lesson));

        NormalizeQuestions();
        Debug.Log($"[QuizQuestionView] quiz source counts lesson={lesson?.id} quizQuestions={(lesson?.quizQuestions != null ? lesson.quizQuestions.Count : 0)} questions={(lesson?.questions != null ? lesson.questions.Count : 0)} quizzes={(lesson?.quizzes != null ? lesson.quizzes.Count : 0)} final={questions.Count}");
        Render();
        return questions.Count > 0;
    }

    public void OnPageShown()
    {
        Render();
    }

    private void Render()
    {
        if (questions.Count == 0)
        {
            if (progressLabel != null) progressLabel.text = "Question 0/0";
            if (questionLabel != null) questionLabel.text = "No quiz available.";
            if (feedbackLabel != null) feedbackLabel.text = string.Empty;
            if (answersContainer != null) answersContainer.Clear();
            SetNavigationState(false, false, false);
            return;
        }

        if (summaryMode)
        {
            RenderSummary();
            return;
        }

        QuizQuestionData question = questions[currentIndex];
        int total = questions.Count;
        int number = currentIndex + 1;

        if (progressLabel != null) progressLabel.text = $"Question {number}/{total}";
        if (progressBar != null)
        {
            progressBar.lowValue = 0;
            progressBar.highValue = total;
            progressBar.value = number;
            progressBar.title = string.Empty;
        }

        if (questionLabel != null)
        {
            string displayQuestion = FirstNonEmpty(question.question, question.prompt, question.text);
            questionLabel.text = string.IsNullOrWhiteSpace(displayQuestion)
                ? "Question"
                : displayQuestion;
            Debug.Log($"[QuizQuestionView] render pass questionIndex={currentIndex} chosenText='{questionLabel.text}'");
        }

        bool answered = selectedAnswers.TryGetValue(currentIndex, out int selectedIndex);
        int correctIndex = Mathf.Clamp(question.correctIndex, 0, Mathf.Max(0, question.options.Count - 1));

        RenderAnswerCards(question, answered, selectedIndex, correctIndex);

        if (feedbackLabel != null)
        {
            if (!answered)
            {
                feedbackLabel.text = "Choose one answer.";
                feedbackLabel.RemoveFromClassList("is-correct");
                feedbackLabel.RemoveFromClassList("is-wrong");
            }
            else if (selectedIndex == correctIndex)
            {
                feedbackLabel.text = "Correct!";
                feedbackLabel.AddToClassList("is-correct");
                feedbackLabel.RemoveFromClassList("is-wrong");
            }
            else
            {
                string correctText = question.options[correctIndex];
                string explanation = FirstNonEmpty(question.explanation, question.explain, question.reason, question.solution, question.wrongExplanation);
                feedbackLabel.text = string.IsNullOrWhiteSpace(explanation)
                    ? $"Incorrect. Correct answer: {correctText}"
                    : $"Incorrect. Correct answer: {correctText}\nExplanation: {explanation}";
                feedbackLabel.AddToClassList("is-wrong");
                feedbackLabel.RemoveFromClassList("is-correct");
            }
        }

        SetNavigationState(currentIndex > 0, answered, true);
    }

    private void RenderSummary()
    {
        int total = questions.Count;
        int score = 0;
        for (int i = 0; i < total; i++)
        {
            QuizQuestionData q = questions[i];
            if (selectedAnswers.TryGetValue(i, out int selected) && selected == q.correctIndex)
            {
                score++;
            }
        }

        if (progressLabel != null) progressLabel.text = "Result";
        if (progressBar != null)
        {
            progressBar.lowValue = 0;
            progressBar.highValue = Mathf.Max(1, total);
            progressBar.value = score;
            progressBar.title = string.Empty;
        }

        if (questionLabel != null)
        {
            questionLabel.text = $"Your score: {score}/{total}";
        }

        if (feedbackLabel != null)
        {
            feedbackLabel.text = score >= Mathf.CeilToInt(total * 0.7f)
                ? "Great work. You passed this quiz."
                : "Keep going. Review the lesson and retry.";
            feedbackLabel.RemoveFromClassList("is-correct");
            feedbackLabel.RemoveFromClassList("is-wrong");
        }

        if (answersContainer != null)
        {
            answersContainer.Clear();
        }

        SetNavigationState(false, false, false, true);
    }

    private void RenderAnswerCards(QuizQuestionData question, bool answered, int selectedIndex, int correctIndex)
    {
        if (answersContainer == null) return;

        answersContainer.Clear();
        answerCards.Clear();

        for (int i = 0; i < question.options.Count; i++)
        {
            AnswerCard card = new AnswerCard();
            card.Bind(i, question.options[i]);
            card.Clicked += OnAnswerCardClicked;

            if (!answered)
            {
                card.SetState(AnswerCard.CardState.Idle);
            }
            else if (i == correctIndex)
            {
                card.SetState(AnswerCard.CardState.Correct);
            }
            else if (i == selectedIndex && selectedIndex != correctIndex)
            {
                card.SetState(AnswerCard.CardState.Wrong);
            }
            else
            {
                card.SetState(AnswerCard.CardState.Disabled);
            }

            answerCards.Add(card);
            answersContainer.Add(card);
        }

        Debug.Log($"[QuizQuestionView] instantiated answer cards={answerCards.Count} for questionIndex={currentIndex}");
    }

    private void OnAnswerCardClicked(AnswerCard card)
    {
        if (card == null) return;
        if (selectedAnswers.ContainsKey(currentIndex)) return;

        selectedAnswers[currentIndex] = card.Index;
        Render();
    }

    private void Prev()
    {
        if (summaryMode) return;
        if (currentIndex <= 0) return;
        currentIndex--;
        Render();
    }

    private void Next()
    {
        if (summaryMode) return;
        if (!selectedAnswers.ContainsKey(currentIndex)) return;

        if (currentIndex >= questions.Count - 1)
        {
            summaryMode = true;
        }
        else
        {
            currentIndex++;
        }

        Render();
    }

    private void Retry()
    {
        summaryMode = false;
        currentIndex = 0;
        selectedAnswers.Clear();
        Render();
    }

    private void NormalizeQuestions()
    {
        for (int i = questions.Count - 1; i >= 0; i--)
        {
            QuizQuestionData q = questions[i];
            if (q == null)
            {
                questions.RemoveAt(i);
                continue;
            }

            if (q.options == null || q.options.Count == 0)
            {
                if (q.answers != null && q.answers.Count > 0) q.options = new List<string>(q.answers);
                else if (q.choices != null && q.choices.Count > 0) q.options = new List<string>(q.choices);
            }

            if (string.IsNullOrWhiteSpace(q.question))
            {
                q.question = FirstNonEmpty(q.text, q.prompt, "Question");
            }

            if (q.options == null || q.options.Count == 0)
            {
                questions.RemoveAt(i);
                continue;
            }

            for (int j = 0; j < q.options.Count; j++)
            {
                if (string.IsNullOrWhiteSpace(q.options[j]))
                {
                    q.options[j] = $"Option {j + 1}";
                }
            }

            if (q.correctIndex >= 0 && q.correctIndex < q.options.Count)
            {
                continue;
            }

            if (q.correctAnswer > 0 && q.correctAnswer <= q.options.Count)
            {
                q.correctIndex = q.correctAnswer - 1;
            }
            else if (q.correctAnswer >= 0 && q.correctAnswer < q.options.Count)
            {
                q.correctIndex = q.correctAnswer;
            }

            q.correctIndex = Mathf.Clamp(q.correctIndex, 0, q.options.Count - 1);
        }
    }

    private static void AddQuestions(List<QuizQuestionData> destination, List<QuizQuestionData> source)
    {
        if (destination == null || source == null) return;
        for (int i = 0; i < source.Count; i++)
        {
            if (source[i] != null) destination.Add(source[i]);
        }
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
        for (int i = 0; i < source.Count; i++)
        {
            QuizQuestionData question = source[i];
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

    private void SetNavigationState(bool canPrev, bool canNext, bool showNext, bool showRetry = false)
    {
        if (prevButton != null)
        {
            prevButton.SetEnabled(canPrev);
            prevButton.style.display = DisplayStyle.Flex;
        }

        if (nextButton != null)
        {
            nextButton.SetEnabled(canNext);
            nextButton.style.display = showNext ? DisplayStyle.Flex : DisplayStyle.None;
            nextButton.text = currentIndex >= questions.Count - 1 ? "Finish" : "Next";
        }

        if (retryButton != null)
        {
            retryButton.style.display = showRetry ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
