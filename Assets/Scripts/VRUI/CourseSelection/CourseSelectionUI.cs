using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(UIDocument))]
public class CourseSelectionUI : MonoBehaviour
{
    private enum SettingsTarget
    {
        Slide,
        Quiz,
        Video,
        CourseSelection
    }

    [Header("UI Toolkit Assets")]
    [SerializeField] private VisualTreeAsset lessonSelectionViewTree;
    [SerializeField] private StyleSheet courseSelectionStyle;

    [Header("List")]
    [SerializeField, Min(120f)] private float listItemHeight = 220f;

    [Header("Editor Test")]
    [SerializeField] private bool useMockDataInEditor = true;
    [SerializeField] private bool alwaysUseMockData = false;
    [SerializeField] private bool fallbackToMockOnApiErrorInEditor = false;

    [Header("VR World Placement")]
    [SerializeField] private VRPanelAnchorManager anchorManager;

    [Header("Video Popup Window")]
    [SerializeField] private bool openYouTubeExternally = true;
    [SerializeField] private bool autoOpenYouTubeWhenResolveFails = false;
    [SerializeField] private VideoPopupWindow videoPopupWindow;
    [SerializeField] private VideoWindowModeController videoWindowModeController;
    [SerializeField, Min(0.5f)] private float videoWindowDistance = 2.1f;
    [SerializeField] private float videoWindowHeightOffset = 0.0f;
    [SerializeField] private Vector2 videoWindowSize = new Vector2(3.2f, 1.8f);

    [Header("Slide Popup Window")]
    [SerializeField] private SlidePopupWindow slidePopupWindow;
    [SerializeField, Min(0.5f)] private float slideWindowDistance = 1.7f;
    [SerializeField] private float slideWindowHeightOffset = 0.05f;

    [Header("Quiz Popup Window")]
    [SerializeField] private QuizPopupWindow quizPopupWindow;
    [SerializeField, Min(0.5f)] private float quizWindowDistance = 1.7f;
    [SerializeField] private float quizWindowHeightOffset = 0.05f;

    [Header("Independent Quiz/Slide Screens")]
    [SerializeField] private Transform slideScreenAnchor;
    [SerializeField] private Transform quizScreenAnchor;
    [SerializeField] private bool enableSlideDebugLogs = true;

    [Header("Timed Video Quiz Popup")]
    [SerializeField] private TimedQuizPopupWindow timedQuizPopupWindow;
    [SerializeField] private VideoQuizScheduler videoQuizScheduler;
    [SerializeField, Min(0.5f)] private float timedQuizWindowDistance = 1.45f;
    [SerializeField] private float timedQuizWindowHeightOffset = 0.0f;

    private UIDocument uiDocument;
    private VisualElement lessonSelectionWindow;
    private VisualElement coursesPage;
    private VisualElement sectionsPage;
    private ListView courseList;
    private Label statusLabel;
    private Button backButton;
    private Button closeButton;
    private Button settingsButton;
    private Button videoModeButton;
    private Label sectionsTitle;
    private ScrollView sectionsScroll;
    private Label sectionsStatus;
    private VisualElement settingsOverlay;
    private VisualElement settingsPanel;
    private VisualElement settingsContent;
    private VisualElement settingsTabs;
    private SettingsTarget activeSettingsTarget = SettingsTarget.Slide;
    private readonly List<CourseData> courses = new List<CourseData>();
    private readonly List<LessonData> activeLessons = new List<LessonData>();
    private readonly List<LessonItemElement> renderedLessonItems = new List<LessonItemElement>();
    private CourseData activeCourse;
    private string selectedLessonId;
    private int refreshCoursesRequestVersion;
    private int openSectionsRequestVersion;

    private async void Start()
    {
        if (anchorManager == null)
        {
            anchorManager = GetComponent<VRPanelAnchorManager>();
        }

        if (videoPopupWindow == null)
        {
            videoPopupWindow = FindAnyObjectByType<VideoPopupWindow>();
        }

        if (videoWindowModeController == null)
        {
            videoWindowModeController = EnsureVideoWindowModeController();
        }

        if (slidePopupWindow == null)
        {
            slidePopupWindow = FindAnyObjectByType<SlidePopupWindow>();
        }

        if (quizPopupWindow == null)
        {
            quizPopupWindow = FindAnyObjectByType<QuizPopupWindow>();
        }

        if (timedQuizPopupWindow == null)
        {
            timedQuizPopupWindow = FindAnyObjectByType<TimedQuizPopupWindow>();
        }

        if (videoQuizScheduler == null)
        {
            videoQuizScheduler = FindAnyObjectByType<VideoQuizScheduler>();
        }

        BuildUi();
        await RefreshCourses();
    }

    private void LateUpdate()
    {
        // VRPanelAnchorManager handles placement now
    }

    public async Task RefreshCourses()
    {
        int requestVersion = ++refreshCoursesRequestVersion;
        if (statusLabel != null) statusLabel.text = "Loading courses...";

        bool usedMock = false;

        try
        {
            if (ApiManager.Instance == null)
            {
                throw new InvalidOperationException("ApiManager was not found in the scene.");
            }

            List<CourseData> response;

            if (alwaysUseMockData)
            {
                usedMock = true;
                response = BuildMockCourses();
            }
            else
            {
                response = await ApiManager.Instance.GetCoursesAsync();
                if (requestVersion != refreshCoursesRequestVersion)
                {
                    return;
                }

                // Chỉ fallback mock trong Editor khi API trả rỗng và bạn bật option này.
                if ((response == null || response.Count == 0) && Application.isEditor && useMockDataInEditor)
                {
                    usedMock = true;
                    response = BuildMockCourses();
                }
            }

            courses.Clear();
            if (response != null)
            {
                courses.AddRange(response);
            }

            if (courseList != null)
            {
                courseList.Rebuild();
            }

            if (statusLabel != null)
            {
                statusLabel.text = courses.Count == 0
                    ? "No courses available."
                    : usedMock
                        ? $"Loaded {courses.Count} courses (Editor mock)."
                        : $"Loaded {courses.Count} courses.";
            }
        }
        catch (Exception ex)
        {
            if (requestVersion != refreshCoursesRequestVersion)
            {
                return;
            }
            Debug.LogError($"[CourseSelectionUI] Không thể tải danh sách khóa học: {ex.Message}");

            if (Application.isEditor && fallbackToMockOnApiErrorInEditor)
            {
                courses.Clear();
                courses.AddRange(BuildMockCourses());
                if (courseList != null)
                {
                    courseList.Rebuild();
                }

                if (statusLabel != null)
                {
                    statusLabel.text = "The API failed. Showing mock data in the Editor for UI testing.";
                }
                return;
            }

            if (statusLabel != null)
            {
                string msg = ex.Message ?? "Unknown error";
                if (msg.IndexOf("Insecure connection not allowed", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    statusLabel.text = "HTTP bi chan. Vao Player Settings > Allow downloads over HTTP = Always allowed.";
                }
                else
                {
                    statusLabel.text = $"Failed to load courses: {msg}";
                }
            }
        }
    }

    private List<CourseData> BuildMockCourses()
    {
        return new List<CourseData>
        {
            new CourseData
            {
                id = "mock-01",
                title = "Unity",
                description = "Mock course for UI testing",
                thumbnailUrl = "https://res.cloudinary.com/dwxy9oepm/image/upload/v1774103355/CourseImg/ia27tkvhrkjzydrgd1wv.png",
                progress = 6,
                totalLessons = 207,
                completedLessons = 12,
                isEnrolled = true
            },
            new CourseData
            {
                id = "mock-02",
                title = "test",
                description = "Mock course for UI testing",
                thumbnailUrl = "https://res.cloudinary.com/dwxy9oepm/image/upload/v1774947803/CourseImg/elfr48wncyrdun983vyc.webp",
                progress = 2,
                totalLessons = 166,
                completedLessons = 4,
                isEnrolled = true
            }
        };
    }

    private void BuildUi()
    {
        uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null)
        {
            Debug.LogError("[CourseSelectionUI] UIDocument component không tồn tại.");
            return;
        }

#if UNITY_EDITOR
        AutoAssignSeparatedViewTreesInEditor();
#endif

        if (lessonSelectionViewTree == null)
        {
            Debug.LogError("[CourseSelectionUI] Missing lesson selection view tree.");
            return;
        }

        VisualElement root = uiDocument.rootVisualElement;
        root.Clear();
        root.AddToClassList("root");

        if (courseSelectionStyle != null)
        {
            root.styleSheets.Add(courseSelectionStyle);
        }

        lessonSelectionWindow = new VisualElement { name = "lesson-selection-window-root" };
        lessonSelectionWindow.AddToClassList("screen-window-root");
        lessonSelectionViewTree.CloneTree(lessonSelectionWindow);
        if (Application.isPlaying)
        {
            lessonSelectionWindow.style.display = DisplayStyle.None;
            lessonSelectionWindow.style.opacity = 0f;
        }
        root.Add(lessonSelectionWindow);

        coursesPage = lessonSelectionWindow.Q<VisualElement>("courses-page");
        sectionsPage = lessonSelectionWindow.Q<VisualElement>("sections-page");
        statusLabel = lessonSelectionWindow.Q<Label>("status-label");
        courseList = lessonSelectionWindow.Q<ListView>("course-list");
        backButton = lessonSelectionWindow.Q<Button>("back-button");
        closeButton = EnsureCloseButton();
        settingsButton = EnsureSettingsButton();
        videoModeButton = EnsureVideoModeButton();
        sectionsTitle = lessonSelectionWindow.Q<Label>("sections-title");
        sectionsScroll = lessonSelectionWindow.Q<ScrollView>("sections-scroll");
        sectionsStatus = lessonSelectionWindow.Q<Label>("sections-status");
        EnsureSettingsPanel();

        if (timedQuizPopupWindow == null)
        {
            timedQuizPopupWindow = GetOrCreatePopupComponent<TimedQuizPopupWindow>("TimedQuizPopupWindowHost");
        }

        if (videoQuizScheduler == null)
        {
            videoQuizScheduler = GetOrCreatePopupComponent<VideoQuizScheduler>("VideoQuizSchedulerHost");
        }

        if (backButton != null)
        {
            backButton.clicked += ShowCoursesPage;
        }

        if (closeButton != null)
        {
            closeButton.clicked -= CloseCourseSelection;
            closeButton.clicked += CloseCourseSelection;
        }

        if (videoModeButton != null)
        {
            videoModeButton.clicked -= ToggleVideoWindowMode;
            videoModeButton.clicked += ToggleVideoWindowMode;
        }

        if (settingsButton != null)
        {
            settingsButton.clicked -= ToggleSettingsPanel;
            settingsButton.clicked += ToggleSettingsPanel;
        }

        if (videoPopupWindow != null)
        {
            videoPopupWindow.SetPinButtonVisible(false);
        }

        ConfigureListView();
        ShowCoursesPage();
        UpdateVideoModeButton();
    }

    private Button EnsureCloseButton()
    {
        if (lessonSelectionWindow == null)
        {
            return null;
        }

        Button button = lessonSelectionWindow.Q<Button>("course-selection-close-button");
        if (button != null)
        {
            return button;
        }

        button = new Button { name = "course-selection-close-button", text = "Close" };
        button.style.position = Position.Absolute;
        button.style.top = 18f;
        button.style.right = 18f;
        button.style.height = 34f;
        button.style.minWidth = 88f;
        button.style.paddingLeft = 12f;
        button.style.paddingRight = 12f;
        button.style.backgroundColor = new Color(0.92f, 0.95f, 1f, 0.98f);
        button.style.color = new Color(0.08f, 0.12f, 0.18f, 1f);
        button.style.borderTopLeftRadius = 10f;
        button.style.borderTopRightRadius = 10f;
        button.style.borderBottomLeftRadius = 10f;
        button.style.borderBottomRightRadius = 10f;
        button.style.borderTopWidth = 1f;
        button.style.borderRightWidth = 1f;
        button.style.borderBottomWidth = 1f;
        button.style.borderLeftWidth = 1f;
        button.style.borderTopColor = new Color(0.72f, 0.81f, 0.93f, 1f);
        button.style.borderRightColor = new Color(0.72f, 0.81f, 0.93f, 1f);
        button.style.borderBottomColor = new Color(0.72f, 0.81f, 0.93f, 1f);
        button.style.borderLeftColor = new Color(0.72f, 0.81f, 0.93f, 1f);
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        lessonSelectionWindow.Add(button);
        button.BringToFront();
        return button;
    }

    private Button EnsureVideoModeButton()
    {
        if (lessonSelectionWindow == null)
        {
            return null;
        }

        Button button = lessonSelectionWindow.Q<Button>("course-selection-video-mode-button");
        if (button != null)
        {
            return button;
        }

        button = new Button { name = "course-selection-video-mode-button", text = "Float Video" };
        button.style.position = Position.Absolute;
        button.style.top = 18f;
        button.style.right = 116f;
        button.style.height = 34f;
        button.style.minWidth = 110f;
        button.style.paddingLeft = 12f;
        button.style.paddingRight = 12f;
        button.style.backgroundColor = new Color(0.92f, 0.95f, 1f, 0.98f);
        button.style.color = new Color(0.08f, 0.12f, 0.18f, 1f);
        button.style.borderTopLeftRadius = 10f;
        button.style.borderTopRightRadius = 10f;
        button.style.borderBottomLeftRadius = 10f;
        button.style.borderBottomRightRadius = 10f;
        button.style.borderTopWidth = 1f;
        button.style.borderRightWidth = 1f;
        button.style.borderBottomWidth = 1f;
        button.style.borderLeftWidth = 1f;
        button.style.borderTopColor = new Color(0.72f, 0.81f, 0.93f, 1f);
        button.style.borderRightColor = new Color(0.72f, 0.81f, 0.93f, 1f);
        button.style.borderBottomColor = new Color(0.72f, 0.81f, 0.93f, 1f);
        button.style.borderLeftColor = new Color(0.72f, 0.81f, 0.93f, 1f);
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        lessonSelectionWindow.Add(button);
        button.BringToFront();
        return button;
    }

    private Button EnsureSettingsButton()
    {
        if (lessonSelectionWindow == null)
        {
            return null;
        }

        Button button = lessonSelectionWindow.Q<Button>("course-selection-settings-button");
        if (button != null)
        {
            return button;
        }

        button = new Button { name = "course-selection-settings-button", text = "\u2699" };
        button.style.position = Position.Absolute;
        button.style.top = 18f;
        button.style.right = 236f;
        button.style.width = 34f;
        button.style.height = 34f;
        button.style.paddingLeft = 0f;
        button.style.paddingRight = 0f;
        button.style.paddingTop = 0f;
        button.style.paddingBottom = 0f;
        button.style.backgroundColor = new Color(0.92f, 0.95f, 1f, 0.98f);
        button.style.color = new Color(0.08f, 0.12f, 0.18f, 1f);
        button.style.borderTopLeftRadius = 10f;
        button.style.borderTopRightRadius = 10f;
        button.style.borderBottomLeftRadius = 10f;
        button.style.borderBottomRightRadius = 10f;
        button.style.borderTopWidth = 1f;
        button.style.borderRightWidth = 1f;
        button.style.borderBottomWidth = 1f;
        button.style.borderLeftWidth = 1f;
        button.style.borderTopColor = new Color(0.72f, 0.81f, 0.93f, 1f);
        button.style.borderRightColor = new Color(0.72f, 0.81f, 0.93f, 1f);
        button.style.borderBottomColor = new Color(0.72f, 0.81f, 0.93f, 1f);
        button.style.borderLeftColor = new Color(0.72f, 0.81f, 0.93f, 1f);
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        button.style.fontSize = 16f;
        lessonSelectionWindow.Add(button);
        button.BringToFront();
        return button;
    }

    private VideoWindowModeController EnsureVideoWindowModeController()
    {
        if (videoWindowModeController != null)
        {
            return videoWindowModeController;
        }

        videoWindowModeController = FindAnyObjectByType<VideoWindowModeController>();
        if (videoWindowModeController != null)
        {
            return videoWindowModeController;
        }

        if (videoPopupWindow == null)
        {
            videoPopupWindow = FindAnyObjectByType<VideoPopupWindow>();
        }

        if (videoPopupWindow == null)
        {
            return null;
        }

        videoWindowModeController = videoPopupWindow.GetComponent<VideoWindowModeController>();
        if (videoWindowModeController == null)
        {
            videoWindowModeController = videoPopupWindow.gameObject.AddComponent<VideoWindowModeController>();
        }

        return videoWindowModeController;
    }

    private void EnsureSettingsPanel()
    {
        if (lessonSelectionWindow == null)
        {
            return;
        }

        settingsOverlay = lessonSelectionWindow.Q<VisualElement>("course-selection-settings-overlay");
        if (settingsOverlay != null)
        {
            settingsPanel = settingsOverlay.Q<VisualElement>("course-selection-settings-panel");
            settingsTabs = settingsOverlay.Q<VisualElement>("course-selection-settings-tabs");
            settingsContent = settingsOverlay.Q<VisualElement>("course-selection-settings-content");
            RebuildSettingsContent();
            return;
        }

        settingsOverlay = new VisualElement { name = "course-selection-settings-overlay" };
        settingsOverlay.style.position = Position.Absolute;
        settingsOverlay.style.top = 60f;
        settingsOverlay.style.right = 18f;
        settingsOverlay.style.width = 360f;
        settingsOverlay.style.display = DisplayStyle.None;

        settingsPanel = new VisualElement { name = "course-selection-settings-panel" };
        settingsPanel.style.paddingLeft = 14f;
        settingsPanel.style.paddingRight = 14f;
        settingsPanel.style.paddingTop = 14f;
        settingsPanel.style.paddingBottom = 14f;
        settingsPanel.style.backgroundColor = new Color(0.98f, 0.99f, 1f, 0.98f);
        settingsPanel.style.borderTopLeftRadius = 16f;
        settingsPanel.style.borderTopRightRadius = 16f;
        settingsPanel.style.borderBottomLeftRadius = 16f;
        settingsPanel.style.borderBottomRightRadius = 16f;
        settingsPanel.style.borderTopWidth = 1f;
        settingsPanel.style.borderRightWidth = 1f;
        settingsPanel.style.borderBottomWidth = 1f;
        settingsPanel.style.borderLeftWidth = 1f;
        settingsPanel.style.borderTopColor = new Color(0.72f, 0.81f, 0.93f, 1f);
        settingsPanel.style.borderRightColor = new Color(0.72f, 0.81f, 0.93f, 1f);
        settingsPanel.style.borderBottomColor = new Color(0.72f, 0.81f, 0.93f, 1f);
        settingsPanel.style.borderLeftColor = new Color(0.72f, 0.81f, 0.93f, 1f);

        Label title = new Label("Window Settings");
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.fontSize = 18f;
        title.style.color = new Color(0.08f, 0.12f, 0.18f, 1f);
        title.style.marginBottom = 12f;
        settingsPanel.Add(title);

        settingsTabs = new VisualElement { name = "course-selection-settings-tabs" };
        settingsTabs.style.flexDirection = FlexDirection.Row;
        settingsTabs.style.flexWrap = Wrap.Wrap;
        settingsTabs.style.marginBottom = 12f;
        settingsPanel.Add(settingsTabs);

        settingsContent = new VisualElement { name = "course-selection-settings-content" };
        settingsPanel.Add(settingsContent);

        settingsOverlay.Add(settingsPanel);
        lessonSelectionWindow.Add(settingsOverlay);
        settingsOverlay.BringToFront();
        RebuildSettingsContent();
    }

    private Button CreateSettingsTabButton(string text, SettingsTarget target)
    {
        Button button = new Button(() =>
        {
            activeSettingsTarget = target;
            RebuildSettingsContent();
        })
        {
            text = text
        };
        button.style.height = 28f;
        button.style.paddingLeft = 10f;
        button.style.paddingRight = 10f;
        button.style.backgroundColor = activeSettingsTarget == target
            ? new Color(0.2f, 0.55f, 0.93f, 0.98f)
            : new Color(0.92f, 0.95f, 1f, 0.98f);
        button.style.color = activeSettingsTarget == target ? Color.white : new Color(0.08f, 0.12f, 0.18f, 1f);
        button.style.borderTopLeftRadius = 8f;
        button.style.borderTopRightRadius = 8f;
        button.style.borderBottomLeftRadius = 8f;
        button.style.borderBottomRightRadius = 8f;
        button.style.marginRight = 8f;
        button.style.marginBottom = 8f;
        button.style.borderTopWidth = 0f;
        button.style.borderRightWidth = 0f;
        button.style.borderBottomWidth = 0f;
        button.style.borderLeftWidth = 0f;
        return button;
    }

    private void ToggleSettingsPanel()
    {
        EnsureSettingsPanel();
        if (settingsOverlay == null)
        {
            return;
        }

        bool open = settingsOverlay.style.display != DisplayStyle.Flex;
        settingsOverlay.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
        if (open)
        {
            RebuildSettingsContent();
        }
    }

    private void RebuildSettingsContent()
    {
        if (settingsContent == null)
        {
            return;
        }

        if (settingsTabs != null)
        {
            settingsTabs.Clear();
            settingsTabs.Add(CreateSettingsTabButton("Slide", SettingsTarget.Slide));
            settingsTabs.Add(CreateSettingsTabButton("Quiz", SettingsTarget.Quiz));
            settingsTabs.Add(CreateSettingsTabButton("Video", SettingsTarget.Video));
            settingsTabs.Add(CreateSettingsTabButton("Course Selection", SettingsTarget.CourseSelection));
        }

        settingsContent.Clear();

        switch (activeSettingsTarget)
        {
            case SettingsTarget.Slide:
                AddSettingsHeader("Slide Window");
                AddSettingSlider("Distance", slideWindowDistance, 0.5f, 4f, value =>
                {
                    slideWindowDistance = value;
                    RepositionVisibleSlideWindow();
                });
                AddSettingSlider("Height", slideWindowHeightOffset, -1f, 1f, value =>
                {
                    slideWindowHeightOffset = value;
                    RepositionVisibleSlideWindow();
                });
                AddSettingSlider("Horizontal Offset", slidePopupWindow != null ? slidePopupWindow.HorizontalOffset : -0.35f, -2f, 2f, value =>
                {
                    if (slidePopupWindow != null)
                    {
                        slidePopupWindow.SetPlacementOffsets(slideWindowDistance, slideWindowHeightOffset, value);
                    }
                });
                break;
            case SettingsTarget.Quiz:
                AddSettingsHeader("Quiz Window");
                AddSettingSlider("Distance", quizWindowDistance, 0.5f, 4f, value =>
                {
                    quizWindowDistance = value;
                    RepositionVisibleQuizWindow();
                });
                AddSettingSlider("Height", quizWindowHeightOffset, -1f, 1f, value =>
                {
                    quizWindowHeightOffset = value;
                    RepositionVisibleQuizWindow();
                });
                AddSettingSlider("Horizontal Offset", quizPopupWindow != null ? quizPopupWindow.HorizontalOffset : -0.35f, -2f, 2f, value =>
                {
                    if (quizPopupWindow != null)
                    {
                        quizPopupWindow.SetPlacementOffsets(quizWindowDistance, quizWindowHeightOffset, value);
                    }
                });
                break;
            case SettingsTarget.Video:
                AddSettingsHeader("Video Window");
                VideoWindowModeController controller = EnsureVideoWindowModeController();
                float videoFloatDistance = controller != null ? controller.FloatingDistance : 3f;
                float videoFloatHeight = controller != null ? controller.FloatingHeightOffset : 0.02f;
                AddSettingSlider("Float Distance", videoFloatDistance, 0.5f, 5f, value =>
                {
                    controller = EnsureVideoWindowModeController();
                    controller?.SetFloatingPlacement(value, controller.FloatingHeightOffset);
                    UpdateVideoModeButton();
                });
                AddSettingSlider("Float Height", videoFloatHeight, -1f, 1f, value =>
                {
                    controller = EnsureVideoWindowModeController();
                    controller?.SetFloatingPlacement(controller.FloatingDistance, value);
                    UpdateVideoModeButton();
                });
                break;
            case SettingsTarget.CourseSelection:
                AddSettingsHeader("Course Selection");
                CourseToggleController toggleController = GetCourseToggleController();
                float panelDistance = toggleController != null ? toggleController.PanelDistance : 1.1f;
                float panelHeight = toggleController != null ? toggleController.PanelHeightOffset : 0.02f;
                float panelRight = toggleController != null ? toggleController.PanelRightOffset : 0.22f;
                AddSettingSlider("Distance", panelDistance, 0.3f, 3f, value =>
                {
                    toggleController = GetCourseToggleController();
                    toggleController?.SetPanelPlacement(value, toggleController.PanelHeightOffset, toggleController.PanelRightOffset);
                });
                AddSettingSlider("Height", panelHeight, -1f, 1f, value =>
                {
                    toggleController = GetCourseToggleController();
                    toggleController?.SetPanelPlacement(toggleController.PanelDistance, value, toggleController.PanelRightOffset);
                });
                AddSettingSlider("Right Offset", panelRight, -2f, 2f, value =>
                {
                    toggleController = GetCourseToggleController();
                    toggleController?.SetPanelPlacement(toggleController.PanelDistance, toggleController.PanelHeightOffset, value);
                });
                break;
        }
    }

    private void AddSettingsHeader(string text)
    {
        Label label = new Label(text);
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.fontSize = 14f;
        label.style.color = new Color(0.17f, 0.24f, 0.35f, 1f);
        label.style.marginBottom = 8f;
        settingsContent.Add(label);
    }

    private void AddSettingSlider(string labelText, float currentValue, float minValue, float maxValue, Action<float> onChanged)
    {
        VisualElement row = new VisualElement();
        row.style.marginBottom = 10f;

        Label label = new Label($"{labelText}: {currentValue:0.00}");
        label.style.fontSize = 12f;
        label.style.color = new Color(0.32f, 0.39f, 0.5f, 1f);
        label.style.marginBottom = 4f;
        row.Add(label);

        Slider slider = new Slider(minValue, maxValue)
        {
            value = currentValue
        };
        slider.RegisterValueChangedCallback(evt =>
        {
            label.text = $"{labelText}: {evt.newValue:0.00}";
            onChanged?.Invoke(evt.newValue);
        });
        row.Add(slider);
        settingsContent.Add(row);
    }

    private void ToggleVideoWindowMode()
    {
        VideoWindowModeController controller = EnsureVideoWindowModeController();
        if (controller == null)
        {
            return;
        }

        controller.ToggleMode();
        UpdateVideoModeButton();
    }

    private void UpdateVideoModeButton()
    {
        if (videoModeButton == null)
        {
            return;
        }

        VideoWindowModeController controller = EnsureVideoWindowModeController();
        videoModeButton.style.display = DisplayStyle.Flex;
        bool canToggle = controller != null && videoPopupWindow != null && videoPopupWindow.IsPlaying;
        videoModeButton.SetEnabled(canToggle);
        videoModeButton.text = controller != null && controller.IsFloatingMode ? "Dock Video" : "Float Video";
    }

    private void CloseCourseSelection()
    {
        CourseToggleController toggleController = GetComponent<CourseToggleController>();
        if (toggleController == null)
        {
            toggleController = FindAnyObjectByType<CourseToggleController>();
        }

        if (toggleController != null)
        {
            toggleController.SetOpen(false);
            return;
        }

        if (uiDocument != null && uiDocument.rootVisualElement != null)
        {
            uiDocument.rootVisualElement.style.display = DisplayStyle.None;
            uiDocument.rootVisualElement.style.opacity = 0f;
        }
    }

#if UNITY_EDITOR
    private void AutoAssignSeparatedViewTreesInEditor()
    {
        if (lessonSelectionViewTree == null)
        {
            lessonSelectionViewTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/VRCourseSelection/LessonSelectionView.uxml");
        }

        if (courseSelectionStyle == null)
        {
            courseSelectionStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/UI/VRCourseSelection/CourseSelectionUI.uss");
        }
    }
#endif

    private void ConfigureListView()
    {
        if (courseList == null) return;

        courseList.fixedItemHeight = Mathf.Max(250f, listItemHeight);
        courseList.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
        courseList.selectionType = SelectionType.None;
        courseList.itemsSource = courses;

        courseList.makeItem = () =>
        {
            return new CourseCardElement();
        };

        courseList.bindItem = (element, index) =>
        {
            if (index < 0 || index >= courses.Count) return;

            CourseData course = courses[index];
            CourseCardElement card = element as CourseCardElement;
            if (card == null) return;

            card.Bind(course, selected => _ = OpenSectionsPageAsync(selected));
            _ = LoadThumbnailIntoAsync(card, course.thumbnailUrl, card.BindVersion);
        };
    }

    private async Task OpenSectionsPageAsync(CourseData course)
    {
        if (course == null || ApiManager.Instance == null) return;
        int requestVersion = ++openSectionsRequestVersion;

        activeCourse = course;
        activeLessons.Clear();
        selectedLessonId = null;

        if (sectionsTitle != null)
        {
            sectionsTitle.text = string.IsNullOrWhiteSpace(course.title)
                ? "Sections"
                : $"{course.title} - Sections";
        }

        if (sectionsStatus != null)
        {
            sectionsStatus.text = "Loading sections and lessons...";
        }

        if (sectionsScroll != null)
        {
            sectionsScroll.Clear();
        }

        ShowSectionsPage();

        try
        {
            List<SectionData> sections = await ApiManager.Instance.GetCourseSectionsAsync(course.id);
            if (requestVersion != openSectionsRequestVersion)
            {
                return;
            }

            if (HasRenderableSections(sections))
            {
                RenderExplicitSections(sections);
                return;
            }

            // Fallback cuoi cung neu endpoint hien tai chi tra lesson list.
            List<LessonData> lessons = await ApiManager.Instance.GetLessonsAsync(course.id);
            if (requestVersion != openSectionsRequestVersion)
            {
                return;
            }

            RenderSections(lessons);
        }
        catch (Exception ex)
        {
            if (requestVersion != openSectionsRequestVersion)
            {
                return;
            }

            Debug.LogError($"[CourseSelectionUI] Load sections failed: {ex.Message}");
            if (sectionsStatus != null)
            {
                sectionsStatus.text = $"Failed to load sections: {ex.Message}";
            }
        }
    }

    private void RenderExplicitSections(List<SectionData> sections)
    {
        if (sectionsScroll == null) return;

        sectionsScroll.Clear();
        activeLessons.Clear();
        renderedLessonItems.Clear();

        if (sections == null || sections.Count == 0)
        {
            if (sectionsStatus != null)
            {
                sectionsStatus.text = "This course has no sections yet.";
            }
            return;
        }

        List<SectionData> sortedSections = sections
            .Where(s => s != null)
            .OrderBy(s => GetSectionSortKey(GetSectionDisplayName(s)))
            .ThenBy(s => GetSectionDisplayName(s), StringComparer.OrdinalIgnoreCase)
            .ToList();

        int totalLessons = 0;
        foreach (SectionData section in sortedSections)
        {
            string sectionName = GetSectionDisplayName(section);
            List<LessonData> sectionLessons = GetSectionLessons(section);
            int lessonCount = sectionLessons != null ? sectionLessons.Count : 0;
            totalLessons += lessonCount;

            SectionItemElement sectionItem = new SectionItemElement();
            sectionItem.SetHeader(sectionName, lessonCount);
            sectionItem.SetExpanded(false);

            if (sectionLessons != null)
            {
                List<LessonData> sortedLessons = sectionLessons
                    .OrderBy(l => l != null ? l.order : int.MaxValue)
                    .ThenBy(l => l != null ? l.title : string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (LessonData lesson in sortedLessons)
                {
                    if (lesson == null) continue;
                    activeLessons.Add(lesson);

                    LessonItemElement row = BuildLessonRow(lesson);
                    sectionItem.AddLesson(row);
                }
            }

            sectionsScroll.Add(sectionItem);
        }

        if (sectionsStatus != null)
        {
            sectionsStatus.text = $"{sortedSections.Count} sections, {totalLessons} lessons.";
        }

        UpdateActiveCourseProgressUI();
    }

    private bool HasRenderableSections(List<SectionData> sections)
    {
        if (sections == null || sections.Count == 0) return false;

        for (int i = 0; i < sections.Count; i++)
        {
            List<LessonData> lessons = GetSectionLessons(sections[i]);
            if (lessons != null && lessons.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private void RenderSections(List<LessonData> lessons)
    {
        if (sectionsScroll == null) return;

        sectionsScroll.Clear();
        activeLessons.Clear();
        renderedLessonItems.Clear();

        if (lessons == null || lessons.Count == 0)
        {
            if (sectionsStatus != null)
            {
                sectionsStatus.text = "This course has no lessons yet.";
            }
            return;
        }

        List<LessonData> orderedLessons = lessons
            .OrderBy(l => l != null ? l.order : int.MaxValue)
            .ToList();

        activeLessons.AddRange(orderedLessons.Where(l => l != null));

        Dictionary<string, List<LessonData>> grouped = BuildSectionGroups(orderedLessons);
        List<string> sectionOrder = grouped.Keys.ToList();

        List<string> sortedSections = sectionOrder
            .OrderBy(GetSectionSortKey)
            .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (string sectionName in sortedSections)
        {
            List<LessonData> sectionLessons = grouped[sectionName];
            sectionLessons = sectionLessons
                .OrderBy(l => l != null ? l.order : int.MaxValue)
                .ThenBy(l => l != null ? l.title : string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();

            SectionItemElement sectionItem = new SectionItemElement();
            sectionItem.SetHeader(sectionName, sectionLessons.Count);
            sectionItem.SetExpanded(false);

            foreach (LessonData lesson in sectionLessons)
            {
                LessonItemElement row = BuildLessonRow(lesson);
                sectionItem.AddLesson(row);
            }

            sectionsScroll.Add(sectionItem);
        }

        if (sectionsStatus != null)
        {
            sectionsStatus.text = $"{sortedSections.Count} sections, {lessons.Count} lessons.";
        }

        UpdateActiveCourseProgressUI();
    }

    private LessonItemElement BuildLessonRow(LessonData lesson)
    {
        LessonItemElement row = new LessonItemElement();
        bool selected = lesson != null && !string.IsNullOrWhiteSpace(selectedLessonId) && lesson.id == selectedLessonId;
        row.Bind(lesson, selected);
        row.CompletionChanged += (newValue) =>
        {
            _ = OnLessonCompletionChangedAsync(lesson, newValue, row);
        };
        row.Clicked += () => OnLessonClicked(lesson);
        renderedLessonItems.Add(row);
        return row;
    }

    private async Task OnLessonCompletionChangedAsync(LessonData lesson, bool completed, LessonItemElement row)
    {
        if (lesson == null || activeCourse == null || ApiManager.Instance == null) return;

        bool previous = lesson.isCompleted;
        lesson.isCompleted = completed;
        row?.Bind(lesson, lesson.id == selectedLessonId);
        UpdateActiveCourseProgressUI();

        bool synced = await ApiManager.Instance.UpdateLessonCompletionAsync(activeCourse.id, lesson.id, lesson.videoUrl, completed);
        if (synced) return;

        // Revert when backend sync failed.
        lesson.isCompleted = previous;
        row?.Bind(lesson, lesson.id == selectedLessonId);
        UpdateActiveCourseProgressUI();

        if (sectionsStatus != null)
        {
            sectionsStatus.text = "Unable to save progress to the server. Changes were rolled back.";
        }
    }

    private void UpdateActiveCourseProgressUI()
    {
        if (activeCourse == null) return;

        int total = activeCourse.totalLessons > 0 ? activeCourse.totalLessons : activeLessons.Count;
        int completed = activeLessons.Count(l => l != null && l.isCompleted);

        activeCourse.totalLessons = total;
        activeCourse.completedLessons = Mathf.Clamp(completed, 0, Mathf.Max(0, total));
        activeCourse.progress = total > 0
            ? Mathf.Clamp(Mathf.RoundToInt((activeCourse.completedLessons / (float)total) * 100f), 0, 100)
            : 0;

        if (courseList != null)
        {
            courseList.Rebuild();
        }

        if (sectionsStatus != null)
        {
            sectionsStatus.text = $"Progress: {activeCourse.completedLessons} / {Mathf.Max(0, total)} lessons ({activeCourse.progress}%)";
        }
    }

    private Dictionary<string, List<LessonData>> BuildSectionGroups(List<LessonData> orderedLessons)
    {
        Dictionary<string, List<LessonData>> grouped = new Dictionary<string, List<LessonData>>();

        // Strategy A: Use explicit section fields from backend when available.
        bool hasExplicitSections = orderedLessons.Any(l => !string.IsNullOrWhiteSpace(GetExplicitSectionName(l)));
        if (hasExplicitSections)
        {
            foreach (LessonData lesson in orderedLessons)
            {
                string sectionName = GetExplicitSectionName(lesson);
                if (string.IsNullOrWhiteSpace(sectionName))
                {
                    sectionName = "Course Content";
                }

                sectionName = NormalizeSectionLabel(sectionName);
                EnsureSectionBucket(sectionName, grouped);
                grouped[sectionName].Add(lesson);
            }

            return grouped;
        }

        // Strategy B: Detect section header rows like "01 - Introduction & Setup".
        int headerCount = orderedLessons.Count(l => TryParseSectionHeaderTitle(l != null ? l.title : null, out _));
        if (headerCount > 0)
        {
            string currentSection = null;

            foreach (LessonData lesson in orderedLessons)
            {
                string title = lesson != null ? lesson.title : null;
                if (TryParseSectionHeaderTitle(title, out string headerSection))
                {
                    currentSection = NormalizeSectionLabel(headerSection);
                    EnsureSectionBucket(currentSection, grouped);
                    continue; // Header row itself is not a lesson item.
                }

                if (string.IsNullOrWhiteSpace(currentSection))
                {
                    currentSection = "Course Content";
                    EnsureSectionBucket(currentSection, grouped);
                }

                grouped[currentSection].Add(lesson);
            }

            return grouped;
        }

        // Strategy C: Final fallback - keep all lessons under one section.
        const string defaultSection = "Course Content";
        EnsureSectionBucket(defaultSection, grouped);
        grouped[defaultSection].AddRange(orderedLessons);
        return grouped;
    }

    private static string GetSectionName(LessonData lesson)
    {
        if (lesson == null) return "Section";

        if (!string.IsNullOrWhiteSpace(lesson.sectionTitle)) return lesson.sectionTitle;
        if (!string.IsNullOrWhiteSpace(lesson.sectionName)) return lesson.sectionName;
        if (!string.IsNullOrWhiteSpace(lesson.section)) return lesson.section;

        return "Course Content";
    }

    private static string GetSectionDisplayName(SectionData section)
    {
        if (section == null) return "Course Content";

        string name = null;
        if (!string.IsNullOrWhiteSpace(section.sectionTitle)) name = section.sectionTitle;
        else if (!string.IsNullOrWhiteSpace(section.sectionName)) name = section.sectionName;
        else if (!string.IsNullOrWhiteSpace(section.section)) name = section.section;
        else if (!string.IsNullOrWhiteSpace(section.title)) name = section.title;
        else if (!string.IsNullOrWhiteSpace(section.name)) name = section.name;
        else if (!string.IsNullOrWhiteSpace(section.code)) name = section.code;

        return NormalizeSectionLabel(name);
    }

    private static List<LessonData> GetSectionLessons(SectionData section)
    {
        if (section == null) return null;
        if (section.lessons != null && section.lessons.Count > 0) return section.lessons;
        if (section.videos != null && section.videos.Count > 0) return section.videos;
        if (section.items != null && section.items.Count > 0) return section.items;
        return new List<LessonData>();
    }

    private void OnLessonClicked(LessonData lesson)
    {
        selectedLessonId = lesson != null ? lesson.id : null;
        RefreshLessonSelectionVisuals();

        Debug.Log($"[CourseSelectionUI] OnLessonClicked start id={lesson?.id} title='{lesson?.title}' type='{lesson?.type}'");

        if (enableSlideDebugLogs)
        {
            Debug.Log($"[CourseSelectionUI] Click lesson id={lesson?.id} title='{lesson?.title}' type='{lesson?.type}' slides={(lesson?.slides != null ? lesson.slides.Count : 0)} slideTextLen={(lesson?.slideText != null ? lesson.slideText.Length : 0)} videoUrlEmpty={string.IsNullOrWhiteSpace(lesson?.videoUrl)}");
        }

        // IMPORTANT: Route by explicit content type first.
        // Slide must have precedence over quiz because some backend payloads include quiz stubs on slide lessons.
        bool slideLesson = IsSlideLesson(lesson);
        bool videoLesson = IsVideoLesson(lesson);
        bool quizLesson = IsQuizLesson(lesson);
        if (enableSlideDebugLogs)
        {
            Debug.Log($"[CourseSelectionUI] Route decision slide={slideLesson} video={videoLesson} quiz={quizLesson}");
        }

        if (slideLesson)
        {
            Debug.Log($"[CourseSelectionUI] Routing to slide popup id={lesson?.id} title='{lesson?.title}'");
            if (ShowSlideIndependentScreen(lesson))
            {
                return;
            }

            if (sectionsStatus != null)
            {
                sectionsStatus.text = "This slide lesson has no valid data, or SlidePopupWindow is not assigned.";
            }
            return;
        }

        if (videoLesson)
        {
            _ = PlayVideoLessonAsync(lesson);
            return;
        }

        if (quizLesson)
        {
            if (ShowQuizIndependentScreen(lesson))
            {
                return;
            }

            if (sectionsStatus != null)
            {
                sectionsStatus.text = "This quiz lesson has no valid data, or QuizPopupWindow is not assigned.";
            }
            return;
        }

        // Last fallback: unknown non-video/non-quiz lessons are treated as slide shells
        // so the window is still visible and easier to debug real data issues.
        if (ShowSlideIndependentScreen(lesson))
        {
            return;
        }

        if (IsQuizLesson(lesson))
        {
            if (sectionsStatus != null)
            {
                sectionsStatus.text = "This lesson was identified as a quiz, but it has no valid question data.";
            }
            return;
        }

        if (IsSlideLesson(lesson))
        {
            if (sectionsStatus != null)
            {
                sectionsStatus.text = "This lesson was identified as a slide, but it has no valid slide content.";
            }
            return;
        }

        if (sectionsStatus != null)
        {
            sectionsStatus.text = "This lesson does not have a supported content type yet.";
        }
    }

    private bool ShowSlideIndependentScreen(LessonData lesson)
    {
        if (lesson == null) return false;

        Debug.Log($"[CourseSelectionUI] ShowSlideIndependentScreen invoked id={lesson.id} title='{lesson.title}' type='{lesson.type}'");

        try
        {
            if (enableSlideDebugLogs)
            {
                Debug.Log($"[CourseSelectionUI] ShowSlideIndependentScreen start for lesson id={lesson.id} title='{lesson.title}' type='{lesson.type}'");
            }

            if (slidePopupWindow == null)
            {
                slidePopupWindow = FindAnyObjectByType<SlidePopupWindow>();
                if (slidePopupWindow == null)
                {
                    slidePopupWindow = GetOrCreatePopupComponent<SlidePopupWindow>("SlidePopupWindowHost", false);
                }
                if (slidePopupWindow == null)
                {
                    Debug.LogError("[CourseSelectionUI] SlidePopupWindow is null after create attempt.");
                    return false;
                }
            }

            Debug.Log($"[CourseSelectionUI] slidePopupWindow instance={slidePopupWindow.name}, active={slidePopupWindow.gameObject.activeInHierarchy}, enabled={slidePopupWindow.enabled}");

            if (enableSlideDebugLogs)
            {
                Debug.Log($"[CourseSelectionUI] slidePopupWindow found: {slidePopupWindow.name}, anchor={(slideScreenAnchor != null ? slideScreenAnchor.name : "<none>")}");
            }

            Transform viewer = GetViewerTransform();
            bool useSceneAnchor = !Application.isPlaying
                && slideScreenAnchor != null
                && slideScreenAnchor.gameObject.activeInHierarchy;

            if (useSceneAnchor)
            {
                slidePopupWindow.PlaceAtAnchor(slideScreenAnchor);
            }
            else
            {
                // In play mode, slide windows should behave like floating popups in front of the player.
                slidePopupWindow.PlaceInFrontOf(viewer, slideWindowDistance, slideWindowHeightOffset);
            }

            bool shown = slidePopupWindow.Show(lesson, viewer, slideWindowDistance, slideWindowHeightOffset);
            Debug.Log($"[CourseSelectionUI] slidePopupWindow.Show returned={shown}");
            if (enableSlideDebugLogs)
            {
                Transform rt = slidePopupWindow.WindowRootTransform;
                Debug.Log($"[CourseSelectionUI] slidePopupWindow.Show returned={shown}, rootNull={rt == null}, active={(rt != null && rt.gameObject.activeSelf)}, pos={(rt != null ? rt.position.ToString() : "<null>")}, scale={(rt != null ? rt.localScale.ToString() : "<null>")}");
            }
            if (!shown) return false;

            if (quizPopupWindow != null)
            {
                quizPopupWindow.HideWindow();
            }

            if (sectionsStatus != null)
            {
                sectionsStatus.text = "Opened the standalone Slide screen in the scene.";
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CourseSelectionUI] ShowSlideIndependentScreen exception: {ex}");
            if (sectionsStatus != null)
            {
                sectionsStatus.text = $"Failed to open the Slide screen: {ex.Message}";
            }
            return false;
        }
    }

    private bool ShowQuizIndependentScreen(LessonData lesson)
    {
        if (lesson == null) return false;

        if (quizPopupWindow == null)
        {
            quizPopupWindow = FindAnyObjectByType<QuizPopupWindow>();
            if (quizPopupWindow == null)
            {
                quizPopupWindow = GetOrCreatePopupComponent<QuizPopupWindow>("QuizPopupWindowHost", false);
            }
            if (quizPopupWindow == null) return false;
        }

        if (!quizPopupWindow.CanHandle(lesson)) return false;

        Transform viewer = GetViewerTransform();
        bool useSceneAnchor = !Application.isPlaying
            && quizScreenAnchor != null
            && quizScreenAnchor.gameObject.activeInHierarchy;

        if (useSceneAnchor)
        {
            quizPopupWindow.PlaceAtAnchor(quizScreenAnchor);
        }
        else
        {
            quizPopupWindow.PlaceInFrontOf(viewer, quizWindowDistance, quizWindowHeightOffset);
        }

        bool shown = quizPopupWindow.Show(lesson, viewer, quizWindowDistance, quizWindowHeightOffset);
        if (!shown) return false;

        if (slidePopupWindow != null)
        {
            slidePopupWindow.HideWindow();
        }

        if (sectionsStatus != null)
        {
            sectionsStatus.text = "Opened the standalone Quiz screen in the scene.";
        }

        return true;
    }

    private static bool IsSlideLesson(LessonData lesson)
    {
        if (lesson == null) return false;
        string type = string.IsNullOrWhiteSpace(lesson.type) ? string.Empty : lesson.type.Trim().ToLowerInvariant();
        if (type.Contains("slide") || type.Contains("presentation") || type.Contains("ppt") || type.Contains("document")) return true;
        if (lesson.slides != null && lesson.slides.Count > 0) return true;
        if (!string.IsNullOrWhiteSpace(lesson.slideText)) return true;

        // Fallback heuristics for schema drift where slide text may be delivered in title/description-like fields.
        if (!string.IsNullOrWhiteSpace(lesson.title) && lesson.title.IndexOf("slide", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return false;
    }

    private static bool IsQuizLesson(LessonData lesson)
    {
        if (lesson == null) return false;
        string type = string.IsNullOrWhiteSpace(lesson.type) ? string.Empty : lesson.type.Trim().ToLowerInvariant();
        if (type.Contains("quiz") || type.Contains("question")) return true;
        if (HasAnyValidQuizQuestions(lesson.quizQuestions)) return true;
        if (HasAnyValidQuizQuestions(lesson.questions)) return true;
        if (HasAnyValidQuizQuestions(lesson.quizzes)) return true;
        return IsMeaningfulQuizQuestion(lesson.quizQuestion);
    }

    private static bool HasAnyValidQuizQuestions(List<QuizQuestionData> list)
    {
        if (list == null || list.Count == 0) return false;
        for (int i = 0; i < list.Count; i++)
        {
            if (IsMeaningfulQuizQuestion(list[i])) return true;
        }
        return false;
    }

    private static bool IsMeaningfulQuizQuestion(QuizQuestionData q)
    {
        if (q == null) return false;

        bool hasPrompt = !string.IsNullOrWhiteSpace(q.question)
            || !string.IsNullOrWhiteSpace(q.text)
            || !string.IsNullOrWhiteSpace(q.prompt);

        bool hasOptions = (q.options != null && q.options.Count > 0)
            || (q.answers != null && q.answers.Count > 0)
            || (q.choices != null && q.choices.Count > 0);

        return hasPrompt || hasOptions;
    }

    private void RefreshLessonSelectionVisuals()
    {
        for (int i = 0; i < renderedLessonItems.Count; i++)
        {
            LessonItemElement item = renderedLessonItems[i];
            if (item == null) continue;
            bool selected = !string.IsNullOrWhiteSpace(selectedLessonId) && item.LessonId == selectedLessonId;
            item.SetSelected(selected);
        }
    }

    private bool IsVideoLesson(LessonData lesson)
    {
        if (lesson == null) return false;

        if (!string.IsNullOrWhiteSpace(lesson.type))
        {
            string t = lesson.type.Trim().ToLowerInvariant();
            if (t == "video" || t == "lecture") return true;
        }

        if (!string.IsNullOrWhiteSpace(lesson.videoUrl)) return true;

        string title = lesson.title ?? string.Empty;
        return title.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
            || title.EndsWith(".mov", StringComparison.OrdinalIgnoreCase)
            || title.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);
    }

    private async Task PlayVideoLessonAsync(LessonData lesson)
    {
        if (lesson == null) return;

        string sourceUrl = lesson.videoUrl;
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            if (sectionsStatus != null)
            {
                sectionsStatus.text = "This video lesson does not have a videoUrl from the API.";
            }
            return;
        }

        string resolvedUrl = null;
        bool resolverAttempted = false;
        if (ApiManager.Instance != null)
        {
            resolverAttempted = true;
            if (sectionsStatus != null)
            {
                sectionsStatus.text = "Resolving the stream source...";
            }

            try
            {
                resolvedUrl = await ApiManager.Instance.ResolvePlayableStreamUrlAsync(sourceUrl, activeCourse != null ? activeCourse.id : null, lesson.id, "m3u8");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CourseSelectionUI] Stream resolve failed, fallback direct URL: {ex.Message}");
            }
        }

        string url = NormalizeVideoUrlForPlayer(!string.IsNullOrWhiteSpace(resolvedUrl) ? resolvedUrl : sourceUrl);

        if (IsYouTubeUrl(url))
        {
            bool allowExternalOpen = openYouTubeExternally && (!resolverAttempted || autoOpenYouTubeWhenResolveFails);
            if (allowExternalOpen)
            {
                Application.OpenURL(url);
                const string openedMsg = "Opened YouTube externally for playback (Unity VideoPlayer does not support watch/shorts links directly).";
                if (sectionsStatus != null) sectionsStatus.text = openedMsg;
                Debug.Log("[CourseSelectionUI] Opened YouTube URL externally.");
                return;
            }

            string resolverReason = ApiManager.Instance != null ? ApiManager.Instance.LastStreamResolveErrorMessage : null;
            string msg = string.IsNullOrWhiteSpace(resolverReason)
                ? "Watch/shorts YouTube links cannot be played directly by Unity VideoPlayer. Use an mp4/m3u8 stream URL or a backend-transcoded URL."
                : $"Unity could not play the YouTube video because backend stream resolution failed: {resolverReason}. Install ytdl-core/yt-dlp on the server or use an mp4/m3u8 stream URL.";
            if (sectionsStatus != null) sectionsStatus.text = msg;
            Debug.Log("[CourseSelectionUI] Unsupported YouTube webpage URL for VideoPlayer.");
            return;
        }

        if (videoPopupWindow == null)
        {
            videoPopupWindow = FindAnyObjectByType<VideoPopupWindow>();
            if (videoPopupWindow == null)
            {
                if (sectionsStatus != null)
                {
                    sectionsStatus.text = "No VideoPopupWindow is available for playback.";
                }
                return;
            }
        }

        if (sectionsStatus != null)
        {
            string displayTitle = string.IsNullOrWhiteSpace(lesson.title) ? "Video" : lesson.title;
            sectionsStatus.text = $"Opening video: {displayTitle}";
        }

        try
        {
            Transform viewer = anchorManager != null && anchorManager.cameraAnchor != null
                ? anchorManager.cameraAnchor
                : (Camera.main != null ? Camera.main.transform : null);

            string displayTitle = string.IsNullOrWhiteSpace(lesson.title) ? "Video" : lesson.title;
            await videoPopupWindow.PlayUrlAsync(title: displayTitle, url: url, viewer: viewer, distance: videoWindowDistance, heightOffset: videoWindowHeightOffset, windowSize: videoWindowSize);
            videoWindowModeController = EnsureVideoWindowModeController();
            if (videoWindowModeController != null)
            {
                videoWindowModeController.BindViewer(viewer);
            }
            if (videoPopupWindow != null)
            {
                videoPopupWindow.SetPinButtonVisible(false);
            }
            UpdateVideoModeButton();

            if (videoQuizScheduler != null)
            {
                videoQuizScheduler.BindVideoPlayer(videoPopupWindow.Player);
                videoQuizScheduler.BindTimedQuizPopup(timedQuizPopupWindow);
                videoQuizScheduler.SetPopupPlacement(timedQuizWindowDistance, timedQuizWindowHeightOffset);
                videoQuizScheduler.SetPauseVideoWhenQuizShown(false);

                // If lesson is YouTube and scene has a bridge component, use bridge time callback; otherwise fallback to VideoPlayer.time.
                Func<double> ytTimeProvider = BuildYouTubeTimeProvider(url);
                videoQuizScheduler.StartTracking(lesson, sourceUrl, viewer, ytTimeProvider);
            }

            if (sectionsStatus != null) sectionsStatus.text = "Playing video";
        }
        catch (Exception ex)
        {
            if (ex is NotSupportedException)
            {
                Debug.LogWarning($"[CourseSelectionUI] Play video not supported: {ex.Message}");
            }
            else
            {
                Debug.LogError($"[CourseSelectionUI] Play video failed: {ex.Message}");
            }

            if (sectionsStatus != null) sectionsStatus.text = $"Unable to play the video: {ex.Message}";
        }
    }

    private static bool IsYouTubeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        return url.IndexOf("youtube.com/watch", StringComparison.OrdinalIgnoreCase) >= 0
            || url.IndexOf("youtube.com/shorts/", StringComparison.OrdinalIgnoreCase) >= 0
            || url.IndexOf("youtu.be/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string NormalizeVideoUrlForPlayer(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl)) return rawUrl;

        string trimmed = rawUrl.Trim();
        if (!TryExtractGoogleDriveFileId(trimmed, out string fileId))
        {
            return trimmed;
        }

        // Unity VideoPlayer cannot play Drive /preview HTML pages.
        // Convert to direct file endpoint.
        return $"https://drive.google.com/uc?export=download&id={fileId}";
    }

    private static bool TryExtractGoogleDriveFileId(string url, out string fileId)
    {
        fileId = null;
        if (string.IsNullOrWhiteSpace(url)) return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri parsedUri))
        {
            return false;
        }

        string host = (parsedUri.Host ?? string.Empty).ToLowerInvariant();
        bool isDriveHost = host == "drive.google.com" || host == "docs.google.com";
        if (!isDriveHost)
        {
            // Prevent accidental conversion of non-Drive URLs (for example googlevideo links with id=...)
            return false;
        }

        Match pathMatch = Regex.Match(url, @"/file/d/([^/?#]+)", RegexOptions.IgnoreCase);
        if (pathMatch.Success)
        {
            fileId = pathMatch.Groups[1].Value;
            return !string.IsNullOrWhiteSpace(fileId);
        }

        int queryIndex = url.IndexOf('?');
        if (queryIndex < 0 || queryIndex >= url.Length - 1) return false;

        string query = url.Substring(queryIndex + 1);
        string[] pairs = query.Split('&');
        foreach (string pair in pairs)
        {
            if (string.IsNullOrWhiteSpace(pair)) continue;

            int eqIndex = pair.IndexOf('=');
            if (eqIndex <= 0) continue;

            string key = pair.Substring(0, eqIndex);
            if (!key.Equals("id", StringComparison.OrdinalIgnoreCase)) continue;

            string value = pair.Substring(eqIndex + 1);
            if (string.IsNullOrWhiteSpace(value)) continue;

            fileId = Uri.UnescapeDataString(value);
            return !string.IsNullOrWhiteSpace(fileId);
        }

        return false;
    }

    private void StopVideo()
    {
        if (videoPopupWindow != null)
        {
            videoPopupWindow.StopAndHide();
            videoPopupWindow.SetPinButtonVisible(false);
        }

        if (videoQuizScheduler != null)
        {
            videoQuizScheduler.StopAndClear();
        }

        if (timedQuizPopupWindow != null)
        {
            timedQuizPopupWindow.HideWindow(resumeVideo: false);
        }

        UpdateVideoModeButton();
    }

    private CourseToggleController GetCourseToggleController()
    {
        CourseToggleController toggleController = GetComponent<CourseToggleController>();
        if (toggleController == null)
        {
            toggleController = FindAnyObjectByType<CourseToggleController>();
        }

        return toggleController;
    }

    private void RepositionVisibleSlideWindow()
    {
        if (slidePopupWindow == null || slidePopupWindow.WindowRootTransform == null || !slidePopupWindow.WindowRootTransform.gameObject.activeInHierarchy)
        {
            return;
        }

        Transform viewer = GetViewerTransform();
        if (viewer != null)
        {
            slidePopupWindow.PlaceInFrontOf(viewer, slideWindowDistance, slideWindowHeightOffset);
        }
    }

    private void RepositionVisibleQuizWindow()
    {
        if (quizPopupWindow == null || quizPopupWindow.WindowRootTransform == null || !quizPopupWindow.WindowRootTransform.gameObject.activeInHierarchy)
        {
            return;
        }

        Transform viewer = GetViewerTransform();
        if (viewer != null)
        {
            quizPopupWindow.PlaceInFrontOf(viewer, quizWindowDistance, quizWindowHeightOffset);
        }
    }

    private Func<double> BuildYouTubeTimeProvider(string sourceUrl)
    {
        if (!IsYouTubeUrl(sourceUrl)) return null;

        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour mb = behaviours[i];
            if (mb == null) continue;

            Type t = mb.GetType();

            MethodInfo method = t.GetMethod("GetCurrentTimeSeconds", BindingFlags.Public | BindingFlags.Instance);
            if (method != null && method.ReturnType == typeof(double) && method.GetParameters().Length == 0)
            {
                return () => (double)method.Invoke(mb, null);
            }

            method = t.GetMethod("GetCurrentTime", BindingFlags.Public | BindingFlags.Instance);
            if (method != null && method.ReturnType == typeof(float) && method.GetParameters().Length == 0)
            {
                return () => (float)method.Invoke(mb, null);
            }

            PropertyInfo prop = t.GetProperty("CurrentTimeSeconds", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanRead)
            {
                if (prop.PropertyType == typeof(double))
                {
                    return () => (double)prop.GetValue(mb);
                }

                if (prop.PropertyType == typeof(float))
                {
                    return () => (float)prop.GetValue(mb);
                }
            }
        }

        return null;
    }

    private Transform GetViewerTransform()
    {
        if (anchorManager != null && anchorManager.cameraAnchor != null)
        {
            return anchorManager.cameraAnchor;
        }

        return Camera.main != null ? Camera.main.transform : transform;
    }

    private static string GetExplicitSectionName(LessonData lesson)
    {
        if (lesson == null) return null;

        if (!string.IsNullOrWhiteSpace(lesson.sectionTitle)) return lesson.sectionTitle.Trim();
        if (!string.IsNullOrWhiteSpace(lesson.sectionName)) return lesson.sectionName.Trim();
        if (!string.IsNullOrWhiteSpace(lesson.section)) return lesson.section.Trim();

        return null;
    }

    private static string NormalizeSectionLabel(string sectionName)
    {
        if (string.IsNullOrWhiteSpace(sectionName)) return "Course Content";

        string normalized = sectionName.Trim();
        Match m = Regex.Match(normalized, @"^\s*(\d{1,3})\s*[-:\.]\s*(.+)$");
        if (m.Success && int.TryParse(m.Groups[1].Value, out int n))
        {
            string name = m.Groups[2].Value.Trim();
            return string.IsNullOrWhiteSpace(name) ? $"{n:D2}" : $"{n:D2} - {name}";
        }

        return normalized;
    }

    private static bool TryParseSectionHeaderTitle(string title, out string sectionHeader)
    {
        sectionHeader = null;
        if (string.IsNullOrWhiteSpace(title)) return false;

        Match m = Regex.Match(title, @"^\s*(\d{1,3})\s*[-:\.]\s*(.+)$");
        if (!m.Success) return false;

        string number = m.Groups[1].Value;
        string name = m.Groups[2].Value.Trim();
        if (string.IsNullOrWhiteSpace(name)) return false;

        if (int.TryParse(number, out int n))
        {
            sectionHeader = $"{n:D2} - {name}";
        }
        else
        {
            sectionHeader = $"{number} - {name}";
        }

        return true;
    }

    private static void EnsureSectionBucket(string sectionName, Dictionary<string, List<LessonData>> grouped)
    {
        if (string.IsNullOrWhiteSpace(sectionName)) sectionName = "Course Content";

        if (!grouped.ContainsKey(sectionName))
        {
            grouped[sectionName] = new List<LessonData>();
        }
    }

    private static int GetSectionSortKey(string sectionName)
    {
        if (string.IsNullOrWhiteSpace(sectionName)) return int.MaxValue;

        Match codeMatch = Regex.Match(sectionName, @"(\d+)");
        if (codeMatch.Success && int.TryParse(codeMatch.Groups[1].Value, out int code))
        {
            return code;
        }

        return int.MaxValue;
    }

    private void ShowCoursesPage()
    {
        if (coursesPage != null) coursesPage.RemoveFromClassList("hidden");
        if (sectionsPage != null) sectionsPage.AddToClassList("hidden");
        if (slidePopupWindow != null) slidePopupWindow.HideWindow();
        if (quizPopupWindow != null) quizPopupWindow.HideWindow();
        UpdateVideoModeButton();

        if (anchorManager != null) anchorManager.SetMode(VRPanelAnchorManager.PanelMode.Browsing);
    }

    private void ShowSectionsPage()
    {
        if (coursesPage != null) coursesPage.AddToClassList("hidden");
        if (sectionsPage != null) sectionsPage.RemoveFromClassList("hidden");
        if (slidePopupWindow != null) slidePopupWindow.HideWindow();
        if (quizPopupWindow != null) quizPopupWindow.HideWindow();
        UpdateVideoModeButton();

        if (anchorManager != null) anchorManager.SetMode(VRPanelAnchorManager.PanelMode.Browsing);
    }

    private void OnDestroy()
    {
        refreshCoursesRequestVersion++;
        openSectionsRequestVersion++;
        StopVideo();
    }

    private async Task LoadThumbnailIntoAsync(CourseCardElement card, string thumbnailUrl, int version)
    {
        if (card == null || ApiManager.Instance == null) return;
        if (string.IsNullOrWhiteSpace(thumbnailUrl)) return;

        Texture2D texture = await ApiManager.Instance.DownloadImageAsync(thumbnailUrl);
        if (texture == null) return;

        card.SetThumbnail(texture, version);
    }

    private T GetOrCreatePopupComponent<T>(string hostName, bool parentToSelf = true) where T : Component
    {
        Transform host = transform.Find(hostName);
        if (host == null)
        {
            GameObject existing = GameObject.Find(hostName);
            if (existing != null)
            {
                host = existing.transform;
            }
        }
        if (host == null)
        {
            GameObject go = new GameObject(hostName);
            if (parentToSelf)
            {
                go.transform.SetParent(transform, false);
            }
            host = go.transform;
        }

        T comp = host.GetComponent<T>();
        if (comp == null)
        {
            comp = host.gameObject.AddComponent<T>();
        }

        return comp;
    }
}
