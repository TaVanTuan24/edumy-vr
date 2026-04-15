using System;
using UnityEngine;
using UnityEngine.UIElements;

[UxmlElement]
public partial class LessonItemElement : VisualElement
{
    public string LessonId { get; private set; }

    private readonly Label typeIconLabel;
    private readonly Label titleLabel;
    private readonly Label subtitleLabel;
    private readonly Label completedPill;
    private readonly Toggle completeToggle;

    public event Action Clicked;
    public event Action<bool> CompletionChanged;

    public LessonItemElement()
    {
        AddToClassList("lesson-item");

        VisualElement left = new VisualElement();
        left.AddToClassList("lesson-item__left");

        typeIconLabel = new Label();
        typeIconLabel.AddToClassList("lesson-item__type-icon");

        VisualElement textCol = new VisualElement();
        textCol.AddToClassList("lesson-item__text-col");

        titleLabel = new Label();
        titleLabel.AddToClassList("lesson-item__title");

        subtitleLabel = new Label();
        subtitleLabel.AddToClassList("lesson-item__subtitle");

        completedPill = new Label();
        completedPill.AddToClassList("lesson-item__pill");

        textCol.Add(titleLabel);
        textCol.Add(subtitleLabel);

        left.Add(typeIconLabel);
        left.Add(textCol);

        VisualElement right = new VisualElement();
        right.AddToClassList("lesson-item__right");

        completeToggle = new Toggle();
        completeToggle.AddToClassList("lesson-item__toggle");

        right.Add(completedPill);
        right.Add(completeToggle);

        Add(left);
        Add(right);

        RegisterCallback<ClickEvent>(OnClicked);
        completeToggle.RegisterCallback<ClickEvent>((evt) => evt.StopPropagation());
        completeToggle.RegisterValueChangedCallback(evt => CompletionChanged?.Invoke(evt.newValue));
    }

    public void Bind(LessonData lesson, bool selected)
    {
        LessonId = lesson != null ? lesson.id : null;

        string lessonTitle = string.IsNullOrWhiteSpace(lesson != null ? lesson.title : null)
            ? "(Không có tiêu đề)"
            : lesson.title;

        string type = (lesson != null ? lesson.type : string.Empty) ?? string.Empty;
        string iconText = ResolveTypeTag(type);

        typeIconLabel.text = iconText;
        typeIconLabel.EnableInClassList("is-video", iconText == "VD");
        typeIconLabel.EnableInClassList("is-slide", iconText == "SL");
        typeIconLabel.EnableInClassList("is-quiz", iconText == "QZ");

        string orderText = lesson != null && lesson.order > 0 ? $"{lesson.order:D3} " : string.Empty;
        titleLabel.text = orderText + lessonTitle;
        subtitleLabel.text = string.IsNullOrWhiteSpace(type) ? "Unknown type" : type.ToUpperInvariant();

        bool isCompleted = lesson != null && lesson.isCompleted;
        completeToggle.SetValueWithoutNotify(isCompleted);
        completedPill.text = isCompleted ? "Completed" : "Not yet";
        completedPill.EnableInClassList("is-completed", isCompleted);

        SetSelected(selected);
    }

    public void SetSelected(bool selected)
    {
        EnableInClassList("is-selected", selected);
    }

    private static string ResolveTypeTag(string type)
    {
        string t = string.IsNullOrWhiteSpace(type) ? string.Empty : type.Trim().ToLowerInvariant();
        if (t.Contains("video") || t.Contains("lecture")) return "VD";
        if (t.Contains("slide")) return "SL";
        if (t.Contains("quiz") || t.Contains("question")) return "QZ";
        return "LS";
    }

    private void OnClicked(ClickEvent evt)
    {
        if (evt.target == completeToggle) return;
        Clicked?.Invoke();
    }
}
