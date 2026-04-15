using System;
using UnityEngine;
using UnityEngine.UIElements;

[UxmlElement]
public partial class AnswerCard : VisualElement
{
    public enum CardState
    {
        Idle,
        Selected,
        Correct,
        Wrong,
        Disabled
    }

    private readonly Label keyLabel;
    private readonly Label textLabel;

    public int Index { get; private set; }
    public string AnswerText { get; private set; }

    public event Action<AnswerCard> Clicked;

    public AnswerCard()
    {
        AddToClassList("answer-card");

        keyLabel = new Label();
        keyLabel.AddToClassList("answer-card__key");

        textLabel = new Label();
        textLabel.AddToClassList("answer-card__text");

        Add(keyLabel);
        Add(textLabel);

        RegisterCallback<ClickEvent>(_ =>
        {
            if (ClassListContains("is-disabled")) return;
            Clicked?.Invoke(this);
        });
    }

    public void Bind(int index, string text)
    {
        Index = index;
        AnswerText = text ?? string.Empty;
        keyLabel.text = GetKey(index);
        textLabel.text = string.IsNullOrWhiteSpace(text) ? $"Option {index + 1}" : text;
        SetState(CardState.Idle);
    }

    public void SetState(CardState state)
    {
        RemoveFromClassList("is-selected");
        RemoveFromClassList("is-correct");
        RemoveFromClassList("is-wrong");
        RemoveFromClassList("is-disabled");

        switch (state)
        {
            case CardState.Selected:
                AddToClassList("is-selected");
                break;
            case CardState.Correct:
                AddToClassList("is-correct");
                AddToClassList("is-disabled");
                break;
            case CardState.Wrong:
                AddToClassList("is-wrong");
                AddToClassList("is-disabled");
                break;
            case CardState.Disabled:
                AddToClassList("is-disabled");
                break;
        }
    }

    private static string GetKey(int index)
    {
        return index switch
        {
            0 => "A",
            1 => "B",
            2 => "C",
            3 => "D",
            _ => (index + 1).ToString()
        };
    }
}
