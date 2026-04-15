using System;
using UnityEngine;
using UnityEngine.UIElements;

[UxmlElement]
public partial class CourseCardElement : VisualElement
{
    public int BindVersion { get; private set; }

    private readonly VisualElement thumbnail;
    private readonly Label topicLabel;
    private readonly Label titleLabel;
    private readonly Label descriptionLabel;
    private readonly Label lessonCountLabel;
    private readonly Label progressLabel;
    private readonly VisualElement progressFill;
    private readonly Button openButton;

    private CourseData boundCourse;

    public CourseCardElement()
    {
        AddToClassList("course-card");

        thumbnail = new VisualElement();
        thumbnail.AddToClassList("course-card__thumb");

        VisualElement content = new VisualElement();
        content.AddToClassList("course-card__content");

        topicLabel = new Label();
        topicLabel.AddToClassList("course-card__topic");

        titleLabel = new Label();
        titleLabel.AddToClassList("course-card__title");

        descriptionLabel = new Label();
        descriptionLabel.AddToClassList("course-card__description");

        lessonCountLabel = new Label();
        lessonCountLabel.AddToClassList("course-card__meta");

        VisualElement progressRow = new VisualElement();
        progressRow.AddToClassList("course-card__progress-row");

        progressLabel = new Label();
        progressLabel.AddToClassList("course-card__progress-label");

        VisualElement progressTrack = new VisualElement();
        progressTrack.AddToClassList("course-card__progress-track");
        progressFill = new VisualElement();
        progressFill.AddToClassList("course-card__progress-fill");
        progressTrack.Add(progressFill);

        openButton = new Button();
        openButton.text = "Open";
        openButton.AddToClassList("course-card__open-button");

        progressRow.Add(progressLabel);
        progressRow.Add(progressTrack);

        content.Add(topicLabel);
        content.Add(titleLabel);
        content.Add(descriptionLabel);
        content.Add(lessonCountLabel);
        content.Add(progressRow);
        content.Add(openButton);

        Add(thumbnail);
        Add(content);
    }

    public void Bind(CourseData course, Action<CourseData> onOpen)
    {
        BindVersion++;
        boundCourse = course;

        string title = string.IsNullOrWhiteSpace(course != null ? course.title : null)
            ? "Untitled Course"
            : course.title;
        string description = string.IsNullOrWhiteSpace(course != null ? course.description : null)
            ? "No description yet."
            : course.description;
        int totalLessons = Mathf.Max(0, course != null ? course.totalLessons : 0);
        int completedLessons = Mathf.Clamp(course != null ? course.completedLessons : 0, 0, totalLessons > 0 ? totalLessons : int.MaxValue);
        int progress = Mathf.Clamp(course != null ? course.progress : 0, 0, 100);

        topicLabel.text = BuildTopicLabel(title, description);
        titleLabel.text = title;
        descriptionLabel.text = description;
        lessonCountLabel.text = totalLessons > 0
            ? $"Lessons: {completedLessons}/{totalLessons}"
            : "Lessons: 0";
        progressLabel.text = $"Progress {progress}%";
        progressFill.style.width = Length.Percent(progress);

        thumbnail.style.backgroundImage = StyleKeyword.None;

        openButton.clicked -= HandleOpenClicked;
        openButton.clicked += HandleOpenClicked;

        void HandleOpenClicked()
        {
            onOpen?.Invoke(boundCourse);
        }
    }

    public void SetThumbnail(Texture2D texture, int expectedBindVersion)
    {
        if (expectedBindVersion != BindVersion) return;
        if (texture == null) return;

        thumbnail.style.backgroundImage = new StyleBackground(texture);
    }

    private static string BuildTopicLabel(string title, string description)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            string[] parts = title.Split(new[] { ' ', '-', ':' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                return parts[0].ToUpperInvariant();
            }
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            string[] parts = description.Split(new[] { ' ', '-', ':' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                return parts[0].ToUpperInvariant();
            }
        }

        return "COURSE";
    }
}
