using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Threading.Tasks;

public class CourseSelectionPanel : MonoBehaviour
{
    [Header("UI Prefabs & Containers")]
    [SerializeField] private GameObject courseCardPrefab;
    [SerializeField] private Transform contentContainer;
    [SerializeField] private Button closeButton;
    [SerializeField] private GameObject loadingSpinner;
    [SerializeField] private LessonListPanel lessonListPanel;

    [Header("VR Comfort Settings")]
    [SerializeField] public float distanceFromUser = 2.2f; // Khoảng cách thoải mái cho VR
    [SerializeField] public float tiltAngle = -20f;       // Nghiêng nhẹ xuống để dễ nhìn
    [SerializeField] public float smoothFollowSpeed = 2.0f; 

    private Transform mainCameraTransform;

    private void Start()
    {
        mainCameraTransform = Camera.main.transform;
        closeButton.onClick.AddListener(() => gameObject.SetActive(false));
        RefreshCourses();
    }

    private void LateUpdate()
    {
        if (mainCameraTransform == null) return;

        // 1. Tính toán vị trí mục tiêu (phía trước người dùng)
        Vector3 targetPos = mainCameraTransform.position + mainCameraTransform.forward * distanceFromUser;
        
        // 2. Smooth follow vị trí
        transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * smoothFollowSpeed);

        // 3. Xoay panel đối diện người dùng nhưng giữ góc nghiêng tiltAngle
        Vector3 directionToUser = transform.position - mainCameraTransform.position;
        if (directionToUser != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(directionToUser);
            targetRot *= Quaternion.Euler(tiltAngle, 0, 0); // Áp dụng độ nghiêng
            
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * smoothFollowSpeed);
        }
    }

    public async void RefreshCourses()
    {
        if (loadingSpinner != null) loadingSpinner.SetActive(true);
        
        // Xóa các card cũ
        foreach (Transform child in contentContainer)
        {
            Destroy(child.gameObject);
        }

        var courses = await ApiManager.Instance.GetCoursesAsync();
        
        if (courses != null)
        {
            foreach (var course in courses)
            {
                var cardObj = Instantiate(courseCardPrefab, contentContainer);
                var card = cardObj.GetComponent<CourseCard>();
                card.Setup(course, OnCourseSelected);
            }
        }

        if (loadingSpinner != null) loadingSpinner.SetActive(false);
    }

    private void OnCourseSelected(CourseData data)
    {
        gameObject.SetActive(false);
        if (lessonListPanel != null)
        {
            lessonListPanel.gameObject.SetActive(true);
            lessonListPanel.ShowLessons(data);
        }
    }
}
