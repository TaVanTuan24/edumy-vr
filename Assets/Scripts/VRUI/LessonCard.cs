using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class LessonCard : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI lessonNumberText;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI durationText;
    [SerializeField] private GameObject completionCheckmark;
    [SerializeField] private Button lessonButton;

    private LessonData currentData;
    private Action<LessonData> onLessonClicked;

    public void Setup(LessonData data, Action<LessonData> callback)
    {
        currentData = data;
        onLessonClicked = callback;

        lessonNumberText.text = $"Lesson {data.order}";
        titleText.text = data.title;
        durationText.text = data.duration;
        completionCheckmark.SetActive(data.isCompleted);

        lessonButton.onClick.RemoveAllListeners();
        lessonButton.onClick.AddListener(() => onLessonClicked?.Invoke(currentData));
    }
}
