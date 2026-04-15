using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class SlideViewer
{
    private readonly VisualElement pageRoot;
    private readonly Label titleLabel;
    private readonly Label counterLabel;
    private readonly Label contentLabel;
    private readonly Button prevButton;
    private readonly Button nextButton;
    private readonly Button zoomInButton;
    private readonly Button zoomOutButton;
    private readonly Button fullscreenButton;
    private readonly VisualElement canvasRoot;

    private readonly List<string> slides = new List<string>();
    private int currentIndex;
    private float zoom = 1f;

    public SlideViewer(VisualElement pageRoot)
    {
        this.pageRoot = pageRoot;
        titleLabel = pageRoot.Q<Label>("slide-window-title");
        counterLabel = pageRoot.Q<Label>("slide-counter");
        contentLabel = pageRoot.Q<Label>("slide-content");
        prevButton = pageRoot.Q<Button>("slide-prev-button");
        nextButton = pageRoot.Q<Button>("slide-next-button");
        zoomInButton = pageRoot.Q<Button>("slide-zoom-in");
        zoomOutButton = pageRoot.Q<Button>("slide-zoom-out");
        fullscreenButton = pageRoot.Q<Button>("slide-fullscreen");
        canvasRoot = pageRoot.Q<VisualElement>("slide-canvas");

        if (prevButton != null) prevButton.clicked += Prev;
        if (nextButton != null) nextButton.clicked += Next;
        if (zoomInButton != null) zoomInButton.clicked += ZoomIn;
        if (zoomOutButton != null) zoomOutButton.clicked += ZoomOut;
        if (fullscreenButton != null) fullscreenButton.clicked += ToggleFullscreen;

        pageRoot.RegisterCallback<KeyDownEvent>(OnKeyDown);
    }

    public bool BindLesson(LessonData lesson)
    {
        slides.Clear();
        currentIndex = 0;
        zoom = 1f;

        if (titleLabel != null)
        {
            titleLabel.text = string.IsNullOrWhiteSpace(lesson != null ? lesson.title : null)
                ? "Slides"
                : lesson.title;
        }

        if (lesson != null && lesson.slides != null && lesson.slides.Count > 0)
        {
            for (int i = 0; i < lesson.slides.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(lesson.slides[i]))
                {
                    slides.Add(lesson.slides[i]);
                }
            }
        }

        if (slides.Count == 0 && !string.IsNullOrWhiteSpace(lesson != null ? lesson.slideText : null))
        {
            slides.Add(lesson.slideText.Trim());
        }

        Render(false);
        return slides.Count > 0;
    }

    public void OnPageShown()
    {
        pageRoot.Focus();
        Render(false);
    }

    private void Prev()
    {
        if (currentIndex <= 0) return;
        currentIndex--;
        Render(true, false);
    }

    private void Next()
    {
        if (currentIndex >= slides.Count - 1) return;
        currentIndex++;
        Render(true, true);
    }

    private void ZoomIn()
    {
        zoom = Mathf.Clamp(zoom + 0.1f, 0.8f, 1.8f);
        ApplyZoom();
    }

    private void ZoomOut()
    {
        zoom = Mathf.Clamp(zoom - 0.1f, 0.8f, 1.8f);
        ApplyZoom();
    }

    private void ToggleFullscreen()
    {
        Screen.fullScreen = !Screen.fullScreen;
    }

    private void OnKeyDown(KeyDownEvent evt)
    {
        if (pageRoot.resolvedStyle.display == DisplayStyle.None) return;

        if (evt.keyCode == KeyCode.LeftArrow)
        {
            Prev();
            evt.StopImmediatePropagation();
        }
        else if (evt.keyCode == KeyCode.RightArrow)
        {
            Next();
            evt.StopImmediatePropagation();
        }
    }

    private void Render(bool animate = false, bool fromRight = true)
    {
        if (slides.Count == 0)
        {
            if (counterLabel != null) counterLabel.text = "0 / 0";
            if (contentLabel != null) contentLabel.text = "No slides available.";
            SetNavState(false, false);
            return;
        }

        if (counterLabel != null)
        {
            counterLabel.text = $"{currentIndex + 1} / {slides.Count}";
        }

        if (contentLabel != null)
        {
            contentLabel.text = slides[currentIndex];
        }

        ApplyZoom();
        SetNavState(currentIndex > 0, currentIndex < slides.Count - 1);

        if (!animate || canvasRoot == null) return;

        canvasRoot.RemoveFromClassList("slide-enter-right");
        canvasRoot.RemoveFromClassList("slide-enter-left");
        canvasRoot.AddToClassList(fromRight ? "slide-enter-right" : "slide-enter-left");

        canvasRoot.schedule.Execute(() =>
        {
            canvasRoot.RemoveFromClassList("slide-enter-right");
            canvasRoot.RemoveFromClassList("slide-enter-left");
        }).ExecuteLater(180);
    }

    private void ApplyZoom()
    {
        if (canvasRoot == null) return;
        canvasRoot.style.scale = new Scale(new Vector3(zoom, zoom, 1f));
    }

    private void SetNavState(bool canPrev, bool canNext)
    {
        if (prevButton != null) prevButton.SetEnabled(canPrev);
        if (nextButton != null) nextButton.SetEnabled(canNext);
    }
}
