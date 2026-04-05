using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Threading.Tasks;

public class LessonListPanel : MonoBehaviour
{
    [Header("UI Prefabs & Containers")]
    [SerializeField] private GameObject lessonCardPrefab;
    [SerializeField] private Transform contentContainer;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private Button backButton;
    [SerializeField] private GameObject loadingSpinner;
    [SerializeField] private GameObject courseSelectionPanel;

    [Header("VR Comfort Settings")]
    [SerializeField] public float distanceFromUser = 2.2f;
    [SerializeField] public float tiltAngle = -20f;
    [SerializeField] public float smoothFollowSpeed = 2.0f;

    private Transform mainCameraTransform;

    private void Start()
    {
        mainCameraTransform = Camera.main.transform;
        backButton.onClick.AddListener(OnBackClicked);
    }

    private void LateUpdate()
    {
        if (mainCameraTransform == null) return;

        // Đồng bộ logic follow tương tự CourseSelectionPanel
        Vector3 targetPos = mainCameraTransform.position + mainCameraTransform.forward * distanceFromUser;
        transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * smoothFollowSpeed);

        Vector3 directionToUser = transform.position - mainCameraTransform.position;
        if (directionToUser != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(directionToUser);
            targetRot *= Quaternion.Euler(tiltAngle, 0, 0);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * smoothFollowSpeed);
        }
    }

    private void OnBackClicked()
    {
        gameObject.SetActive(false);
        if (courseSelectionPanel != null)
        {
            courseSelectionPanel.SetActive(true);
        }
    }

    public async void ShowLessons(CourseData course)
    {
        titleText.text = $"Lessons - {course.title}";
        
        if (loadingSpinner != null) loadingSpinner.SetActive(true);

        // Clear existing cards
        foreach (Transform child in contentContainer)
        {
            Destroy(child.gameObject);
        }

        var lessons = await ApiManager.Instance.GetLessonsAsync(course.id);

        if (lessons != null)
        {
            foreach (var lesson in lessons)
            {
                var cardObj = Instantiate(lessonCardPrefab, contentContainer);
                var card = cardObj.GetComponent<LessonCard>();
                card.Setup(lesson, OnLessonClicked);
            }
        }

        if (loadingSpinner != null) loadingSpinner.SetActive(false);
    }

    private void OnLessonClicked(LessonData data)
    {
        Debug.Log($"Opening lesson ID: {data.id} - {data.title}");
    }
}
