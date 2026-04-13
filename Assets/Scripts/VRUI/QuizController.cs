using UnityEngine.UIElements;

public class QuizController
{
    public VisualElement Root { get; }
    public Button CloseButton { get; }
    public QuizQuestionView View { get; }

    public QuizController(VisualElement root)
    {
        Root = root;
        CloseButton = root.Q<Button>("quiz-close-button");
        View = new QuizQuestionView(root);
    }
}
