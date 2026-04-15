using UnityEngine.UIElements;

[UxmlElement]
public partial class SectionItemElement : VisualElement
{
    private readonly Foldout foldout;

    public VisualElement ContentContainer => foldout;

    public SectionItemElement()
    {
        AddToClassList("section-item");

        foldout = new Foldout
        {
            value = false
        };
        foldout.AddToClassList("section-item__foldout");

        Add(foldout);
    }

    public void SetHeader(string sectionName, int lessonCount)
    {
        string safeName = string.IsNullOrWhiteSpace(sectionName) ? "Section" : sectionName;
        foldout.text = $"{safeName} ({lessonCount})";
    }

    public void SetExpanded(bool expanded)
    {
        foldout.value = expanded;
    }

    public void AddLesson(VisualElement lessonItem)
    {
        if (lessonItem == null) return;
        foldout.Add(lessonItem);
    }
}
