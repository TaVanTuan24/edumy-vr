using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class CourseCard : MonoBehaviour
{
    [SerializeField] private RawImage thumbnail;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private Slider progressSlider;
    [SerializeField] private TextMeshProUGUI progressText;
    [SerializeField] private Button enterButton;

    private CourseData currentData;
    private Action<CourseData> onEnterClicked;

    public void Setup(CourseData data, Action<CourseData> callback)
    {
        currentData = data;
        onEnterClicked = callback;

        titleText.text = data.title;
        descriptionText.text = data.description;
        
        float progress = (float)data.completedLessons / data.totalLessons;
        progressSlider.value = progress;
        progressText.text = $"{data.completedLessons} / {data.totalLessons} Lessons";

        enterButton.onClick.RemoveAllListeners();
        enterButton.onClick.AddListener(() => onEnterClicked?.Invoke(currentData));

        LoadThumbnail(data.thumbnailUrl);
    }

    private async void LoadThumbnail(string url)
    {
        if (ApiManager.Instance != null)
        {
            var texture = await ApiManager.Instance.DownloadImageAsync(url);
            if (texture != null && thumbnail != null)
            {
                thumbnail.texture = texture;
            }
        }
    }
}
