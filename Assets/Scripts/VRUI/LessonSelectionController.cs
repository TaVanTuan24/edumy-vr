using UnityEngine.UIElements;

public class LessonSelectionController
{
    public VisualElement Root { get; }
    public VisualElement CoursesPage { get; }
    public VisualElement SectionsPage { get; }

    public ListView CourseList { get; }
    public Label StatusLabel { get; }
    public Button BackButton { get; }
    public Label SectionsTitle { get; }
    public ScrollView SectionsScroll { get; }
    public Label SectionsStatus { get; }

    public LessonSelectionController(VisualElement root)
    {
        Root = root;
        CoursesPage = root.Q<VisualElement>("courses-page");
        SectionsPage = root.Q<VisualElement>("sections-page");

        CourseList = root.Q<ListView>("course-list");
        StatusLabel = root.Q<Label>("status-label");
        BackButton = root.Q<Button>("back-button");
        SectionsTitle = root.Q<Label>("sections-title");
        SectionsScroll = root.Q<ScrollView>("sections-scroll");
        SectionsStatus = root.Q<Label>("sections-status");
    }

    public void ShowCourses()
    {
        CoursesPage?.RemoveFromClassList("hidden");
        SectionsPage?.AddToClassList("hidden");
    }

    public void ShowSections()
    {
        CoursesPage?.AddToClassList("hidden");
        SectionsPage?.RemoveFromClassList("hidden");
    }
}
