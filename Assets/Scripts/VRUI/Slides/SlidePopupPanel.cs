using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
public class SlidePopupPanel : MonoBehaviour
{
    private VisualElement slidePage;
    private Button slideCloseButton;
    private Button slidePrevButton;
    private Button slideNextButton;
    private Label slideTitle;
    private Label slidePageIndicator;
    private Label slideContent;

    private readonly List<string> activeSlides = new List<string>();
    private int currentSlideIndex;
    private Action onClose;

    public void Initialize(VisualElement root, Action closeAction)
    {
        onClose = closeAction;
        if (root == null) return;

        slidePage = root.Q<VisualElement>("slide-page");
        slideCloseButton = root.Q<Button>("slide-close-button");
        slidePrevButton = root.Q<Button>("slide-prev-button");
        slideNextButton = root.Q<Button>("slide-next-button");
        slideTitle = root.Q<Label>("slide-title");
        slidePageIndicator = root.Q<Label>("slide-page-indicator");
        slideContent = root.Q<Label>("slide-content");

        if (slideCloseButton != null)
        {
            slideCloseButton.clicked -= CloseInternal;
            slideCloseButton.clicked += CloseInternal;
        }

        if (slidePrevButton != null)
        {
            slidePrevButton.clicked -= Prev;
            slidePrevButton.clicked += Prev;
        }

        if (slideNextButton != null)
        {
            slideNextButton.clicked -= Next;
            slideNextButton.clicked += Next;
        }
    }

    public bool CanHandle(LessonData lesson)
    {
        if (lesson == null) return false;
        string t = (lesson.type ?? string.Empty).Trim().ToLowerInvariant();
        if (t.Contains("slide") || t.Contains("presentation")) return true;
        if (lesson.slides != null && lesson.slides.Count > 0) return true;
        if (!string.IsNullOrWhiteSpace(lesson.slideText)) return true;
        return false;
    }

    public bool Open(LessonData lesson)
    {
        if (slidePage == null || lesson == null) return false;

        activeSlides.Clear();
        activeSlides.AddRange(BuildSlidesForLesson(lesson));
        currentSlideIndex = 0;

        if (slideTitle != null)
        {
            slideTitle.text = string.IsNullOrWhiteSpace(lesson.title) ? "Slide" : lesson.title;
        }

        Render();
        slidePage.RemoveFromClassList("hidden");
        return true;
    }

    public void Hide()
    {
        if (slidePage != null)
        {
            slidePage.AddToClassList("hidden");
        }
    }

    private void CloseInternal()
    {
        Hide();
        onClose?.Invoke();
    }

    private void Prev()
    {
        if (activeSlides.Count == 0) return;
        currentSlideIndex = Mathf.Max(0, currentSlideIndex - 1);
        Render();
    }

    private void Next()
    {
        if (activeSlides.Count == 0) return;
        currentSlideIndex = Mathf.Min(activeSlides.Count - 1, currentSlideIndex + 1);
        Render();
    }

    private void Render()
    {
        int total = Mathf.Max(1, activeSlides.Count);
        currentSlideIndex = Mathf.Clamp(currentSlideIndex, 0, total - 1);

        if (slidePageIndicator != null)
        {
            slidePageIndicator.text = $"Slide {currentSlideIndex + 1}/{total}";
        }

        if (slideContent != null)
        {
            slideContent.text = activeSlides.Count > 0 ? activeSlides[currentSlideIndex] : "Không có slide.";
        }

        if (slidePrevButton != null) slidePrevButton.SetEnabled(currentSlideIndex > 0);
        if (slideNextButton != null) slideNextButton.SetEnabled(currentSlideIndex < total - 1);
    }

    private List<string> BuildSlidesForLesson(LessonData lesson)
    {
        List<string> slides = new List<string>();

        if (lesson != null && lesson.slides != null)
        {
            foreach (string page in lesson.slides)
            {
                if (string.IsNullOrWhiteSpace(page)) continue;
                slides.Add(page.Trim());
            }
        }

        if (slides.Count == 0 && lesson != null && !string.IsNullOrWhiteSpace(lesson.slideText))
        {
            string[] pages = lesson.slideText
                .Split(new[] { "\n---\n", "\r\n---\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string p in pages)
            {
                string page = p.Trim();
                if (!string.IsNullOrWhiteSpace(page)) slides.Add(page);
            }
        }

        if (slides.Count == 0)
        {
            string title = string.IsNullOrWhiteSpace(lesson != null ? lesson.title : null) ? "Slide lesson" : lesson.title;
            for (int i = 1; i <= 6; i++)
            {
                slides.Add($"{title}\n\nSlide {i}/6\n\nNội dung slide chưa được backend trả về. Bạn có thể gửi mảng slides hoặc slideText (phân trang bằng ---).");
            }
        }

        return slides;
    }
}
