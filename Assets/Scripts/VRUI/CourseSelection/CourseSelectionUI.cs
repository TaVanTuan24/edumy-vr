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
    private VisualElement vrLoginPanel;
    private VisualElement vrLoginCodeGroup;
    private ListView courseList;
    private Label statusLabel;
    private Label vrLoginStatusLabel;
    private Label vrLoginCodeLabel;
    private Label vrLoginTimerLabel;
    private Label vrLoginUserLabel;
    private Button backButton;
    private Button closeButton;
    private Button settingsButton;
    private Button videoModeButton;
    private Button vrLoginRequestButton;
    private Button vrLoginRefreshButton;
    private Label sectionsTitle;
    private ScrollView sectionsScroll;
    private Label sectionsStatus;
    private VisualElement settingsOverlay;
    private VisualElement settingsPanel;
    private VisualElement settingsContent;
    private VisualElement settingsTabs;
    private SettingsTarget activeSettingsTarget = SettingsTarget.Slide;
    private bool isLogoutConfirmationVisible;
    private readonly List<CourseData> courses = new List<CourseData>();
    private readonly List<LessonData> activeLessons = new List<LessonData>();
    private readonly List<LessonItemElement> renderedLessonItems = new List<LessonItemElement>();
    private CourseData activeCourse;
    private string selectedLessonId;
    private int refreshCoursesRequestVersion;
    private int openSectionsRequestVersion;
    private VRAuthManager vrAuthManager;

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
        EnsureVrAuthManager();
        UpdateVrLoginUi();

        if (vrAuthManager != null && vrAuthManager.IsAuthenticated)
        {
            await RefreshCourses();
        }
        else
        {
            ResetCourseViewForLoggedOutState();
        }
    }

    private void LateUpdate()
    {
        // VRPanelAnchorManager handles placement now
    }

    public async Task RefreshCourses()
    {
        int requestVersion = ++refreshCoursesRequestVersion;
        if (statusLabel != null) statusLabel.text = "Loading courses...";

        if (!alwaysUseMockData && vrAuthManager != null && !vrAuthManager.IsAuthenticated)
        {
            ResetCourseViewForLoggedOutState();
            return;
        }

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

                if (response == null && ApiManager.Instance.LastResponseStatusCode == 401)
                {
                    if (vrAuthManager != null)
                    {
                        vrAuthManager.HandleUnauthorizedSession();
                    }
                    else
                    {
                        ResetCourseViewForLoggedOutState("You must log in to view your courses.");
                    }
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
        vrLoginPanel = lessonSelectionWindow.Q<VisualElement>("vr-login-panel");
        vrLoginCodeGroup = lessonSelectionWindow.Q<VisualElement>("vr-login-code-group");
        statusLabel = lessonSelectionWindow.Q<Label>("status-label");
        vrLoginStatusLabel = lessonSelectionWindow.Q<Label>("vr-login-status");
        vrLoginCodeLabel = lessonSelectionWindow.Q<Label>("vr-login-code");
        vrLoginTimerLabel = lessonSelectionWindow.Q<Label>("vr-login-timer");
        vrLoginUserLabel = lessonSelectionWindow.Q<Label>("vr-login-user");
        courseList = lessonSelectionWindow.Q<ListView>("course-list");
        backButton = lessonSelectionWindow.Q<Button>("back-button");
        closeButton = EnsureCloseButton();
        settingsButton = EnsureSettingsButton();
        videoModeButton = EnsureVideoModeButton();
        vrLoginRequestButton = lessonSelectionWindow.Q<Button>("vr-login-request-button");
        vrLoginRefreshButton = lessonSelectionWindow.Q<Button>("vr-login-refresh-button");
        sectionsTitle = lessonSelectionWindow.Q<Label>("sections-title");
        sectionsScroll = lessonSelectionWindow.Q<ScrollView>("sections-scroll");
        sectionsStatus = lessonSelectionWindow.Q<Label>("sections-status");
        EnsureVrLoginPanelUi();
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

        if (vrLoginRequestButton != null)
        {
            vrLoginRequestButton.clicked -= HandleVrLoginRequestClicked;
            vrLoginRequestButton.clicked += HandleVrLoginRequestClicked;
        }

        if (vrLoginRefreshButton != null)
        {
            vrLoginRefreshButton.clicked -= HandleVrLoginRefreshClicked;
            vrLoginRefreshButton.clicked += HandleVrLoginRefreshClicked;
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
                AddSettingsSeparator();
                AddAccountSettingsSection();
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

    private void AddSettingsSeparator()
    {
        if (settingsContent == null)
        {
            return;
        }

        VisualElement separator = new VisualElement();
        separator.style.height = 1f;
        separator.style.marginTop = 8f;
        separator.style.marginBottom = 12f;
        separator.style.backgroundColor = new Color(0.85f, 0.89f, 0.95f, 1f);
        settingsContent.Add(separator);
    }

    private void AddAccountSettingsSection()
    {
        if (settingsContent == null)
        {
            return;
        }

        AddSettingsHeader("Account");

        bool isAuthenticated = vrAuthManager != null && vrAuthManager.IsAuthenticated;
        string username = vrAuthManager != null ? vrAuthManager.Username : string.Empty;
        string accountMessage = isAuthenticated
            ? string.IsNullOrWhiteSpace(username)
                ? "This VR device is signed in."
                : $"Signed in as {username}."
            : "This VR device is currently signed out.";

        VisualElement accountCard = new VisualElement();
        accountCard.style.paddingLeft = 12f;
        accountCard.style.paddingRight = 12f;
        accountCard.style.paddingTop = 12f;
        accountCard.style.paddingBottom = 12f;
        accountCard.style.marginBottom = 10f;
        accountCard.style.backgroundColor = new Color(1f, 0.97f, 0.97f, 1f);
        accountCard.style.borderTopLeftRadius = 12f;
        accountCard.style.borderTopRightRadius = 12f;
        accountCard.style.borderBottomLeftRadius = 12f;
        accountCard.style.borderBottomRightRadius = 12f;
        accountCard.style.borderTopWidth = 1f;
        accountCard.style.borderRightWidth = 1f;
        accountCard.style.borderBottomWidth = 1f;
        accountCard.style.borderLeftWidth = 1f;
        accountCard.style.borderTopColor = new Color(0.93f, 0.76f, 0.76f, 1f);
        accountCard.style.borderRightColor = new Color(0.93f, 0.76f, 0.76f, 1f);
        accountCard.style.borderBottomColor = new Color(0.93f, 0.76f, 0.76f, 1f);
        accountCard.style.borderLeftColor = new Color(0.93f, 0.76f, 0.76f, 1f);
        settingsContent.Add(accountCard);

        Label accountTitle = new Label("Authentication");
        accountTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        accountTitle.style.fontSize = 13f;
        accountTitle.style.color = new Color(0.48f, 0.14f, 0.14f, 1f);
        accountTitle.style.marginBottom = 4f;
        accountCard.Add(accountTitle);

        Label accountStatus = new Label(accountMessage);
        accountStatus.style.whiteSpace = WhiteSpace.Normal;
        accountStatus.style.fontSize = 12f;
        accountStatus.style.color = new Color(0.4f, 0.24f, 0.24f, 1f);
        accountStatus.style.marginBottom = 10f;
        accountCard.Add(accountStatus);

        if (isLogoutConfirmationVisible && isAuthenticated)
        {
            Label confirmLabel = new Label("Are you sure you want to log out?");
            confirmLabel.style.whiteSpace = WhiteSpace.Normal;
            confirmLabel.style.fontSize = 12f;
            confirmLabel.style.color = new Color(0.56f, 0.17f, 0.17f, 1f);
            confirmLabel.style.marginBottom = 8f;
            accountCard.Add(confirmLabel);

            VisualElement confirmActions = new VisualElement();
            confirmActions.style.flexDirection = FlexDirection.Row;
            confirmActions.style.justifyContent = Justify.FlexEnd;
            confirmActions.style.flexWrap = Wrap.Wrap;
            accountCard.Add(confirmActions);

            Button cancelButton = CreateSettingsActionButton(
                "Cancel",
                HandleLogoutCanceled,
                new Color(1f, 1f, 1f, 0.95f),
                new Color(0.23f, 0.27f, 0.33f, 1f),
                new Color(0.79f, 0.83f, 0.9f, 1f));
            cancelButton.style.marginRight = 8f;
            confirmActions.Add(cancelButton);

            Button confirmButton = CreateSettingsActionButton(
                "Confirm Logout",
                HandleLogoutConfirmed,
                new Color(0.84f, 0.19f, 0.21f, 0.98f),
                Color.white,
                new Color(0.7f, 0.12f, 0.15f, 1f));
            confirmActions.Add(confirmButton);
            return;
        }

        Button logoutButton = CreateSettingsActionButton(
            isAuthenticated ? "Logout" : "Signed Out",
            HandleLogoutRequested,
            isAuthenticated ? new Color(1f, 0.92f, 0.92f, 1f) : new Color(0.96f, 0.96f, 0.96f, 1f),
            isAuthenticated ? new Color(0.62f, 0.12f, 0.12f, 1f) : new Color(0.53f, 0.58f, 0.66f, 1f),
            isAuthenticated ? new Color(0.89f, 0.5f, 0.5f, 1f) : new Color(0.86f, 0.88f, 0.92f, 1f));
        logoutButton.SetEnabled(isAuthenticated);
        accountCard.Add(logoutButton);
    }

    private Button CreateSettingsActionButton(string text, Action onClicked, Color backgroundColor, Color textColor, Color borderColor)
    {
        Button button = new Button(() => onClicked?.Invoke())
        {
            text = text
        };
        button.style.minHeight = 34f;
        button.style.paddingLeft = 12f;
        button.style.paddingRight = 12f;
        button.style.paddingTop = 6f;
        button.style.paddingBottom = 6f;
        button.style.backgroundColor = backgroundColor;
        button.style.color = textColor;
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        button.style.borderTopLeftRadius = 10f;
        button.style.borderTopRightRadius = 10f;
        button.style.borderBottomLeftRadius = 10f;
        button.style.borderBottomRightRadius = 10f;
        button.style.borderTopWidth = 1f;
        button.style.borderRightWidth = 1f;
        button.style.borderBottomWidth = 1f;
        button.style.borderLeftWidth = 1f;
        button.style.borderTopColor = borderColor;
        button.style.borderRightColor = borderColor;
        button.style.borderBottomColor = borderColor;
        button.style.borderLeftColor = borderColor;
        return button;
    }

    private void HandleLogoutRequested()
    {
        if (vrAuthManager == null || !vrAuthManager.IsAuthenticated)
        {
            isLogoutConfirmationVisible = false;
            RebuildSettingsContent();
            return;
        }

        isLogoutConfirmationVisible = true;
        RebuildSettingsContent();
    }

    private void HandleLogoutCanceled()
    {
        if (!isLogoutConfirmationVisible)
        {
            return;
        }

        isLogoutConfirmationVisible = false;
        RebuildSettingsContent();
    }

    private void HandleLogoutConfirmed()
    {
        isLogoutConfirmationVisible = false;
        PrepareForLoggedOutUiTransition();

        if (vrAuthManager != null)
        {
            vrAuthManager.Logout("You have been logged out. Request a new login code to continue.");
            return;
        }

        ApiManager.Instance?.ClearAuthToken();
        ResetCourseViewForLoggedOutState("You have been logged out. Request a new login code to continue.");
        UpdateVrLoginUi();
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
        bool hasVideoSystem = controller != null && videoPopupWindow != null && videoPopupWindow.Player != null;
        bool hasMediaLoaded = hasVideoSystem && !string.IsNullOrEmpty(videoPopupWindow.Player.url);
        videoModeButton.SetEnabled(hasMediaLoaded);
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

        if (!alwaysUseMockData && vrAuthManager != null && !vrAuthManager.IsAuthenticated)
        {
            ResetCourseViewForLoggedOutState();
            return;
        }

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

        string normalizedType = DetermineLessonType(lesson);
        if (enableSlideDebugLogs)
        {
            Debug.Log($"[CourseSelectionUI] Route decision normalizedType={normalizedType} slideCount={GetSlideCount(lesson)} quizCount={GetQuizQuestionCount(lesson)} timedQuizCount={GetTimedQuizCount(lesson)} hasPlayableVideo={HasPlayableVideoSource(lesson)}");
        }

        if (normalizedType == "slide")
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

        if (normalizedType == "video")
        {
            _ = PlayVideoLessonAsync(lesson);
            return;
        }

        if (normalizedType == "quiz")
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

        Debug.LogWarning($"[CourseSelectionUI] Unknown lesson type. id={lesson?.id} title='{lesson?.title}' rawType='{lesson?.type}'");
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

    private static string DetermineLessonType(LessonData lesson)
    {
        if (lesson == null) return "unknown";

        string type = string.IsNullOrWhiteSpace(lesson.type) ? string.Empty : lesson.type.Trim().ToLowerInvariant();
        bool hasSlides = HasRealSlides(lesson);
        bool hasQuiz = HasRealQuizQuestions(lesson);
        bool hasTimedQuiz = HasRealTimedQuizEvents(lesson);
        bool hasVideo = HasPlayableVideoSource(lesson);

        if (type == "slide" || type == "presentation" || type == "ppt" || type == "document")
        {
            return hasSlides ? "slide" : "unknown";
        }

        if (type == "quiz")
        {
            return (hasQuiz || hasTimedQuiz) ? "quiz" : "unknown";
        }

        if (type == "video" || type == "lecture")
        {
            return hasVideo ? "video" : "unknown";
        }

        if (hasSlides) return "slide";
        if (hasQuiz) return "quiz";
        if (hasVideo) return "video";
        if (hasTimedQuiz) return "quiz";

        return "unknown";
    }

    private static bool HasRealSlides(LessonData lesson)
    {
        if (lesson == null) return false;
        return GetSlideCount(lesson) > 0 || !string.IsNullOrWhiteSpace(lesson.slideText);
    }

    private static int GetSlideCount(LessonData lesson)
    {
        if (lesson == null) return 0;
        if (lesson.slideCount > 0) return lesson.slideCount;
        return lesson.slides != null ? lesson.slides.Count : 0;
    }

    private static bool HasRealQuizQuestions(LessonData lesson)
    {
        if (lesson == null) return false;
        if (lesson.quizQuestionsCount > 0) return true;
        if (HasAnyValidQuizQuestions(lesson.quizQuestions)) return true;
        if (HasAnyValidQuizQuestions(lesson.questions)) return true;
        if (HasAnyValidQuizQuestions(lesson.quizzes)) return true;
        return IsMeaningfulQuizQuestion(lesson.quizQuestion);
    }

    private static bool HasRealTimedQuizEvents(LessonData lesson)
    {
        if (lesson == null) return false;
        return HasAnyValidTimedQuizQuestions(lesson.timedQuizzes)
            || HasAnyValidTimedQuizQuestions(lesson.interactiveQuizzes)
            || HasAnyValidTimedQuizQuestions(lesson.popupQuizzes)
            || HasAnyValidTimedQuizQuestions(lesson.videoQuizzes);
    }

    private static int GetQuizQuestionCount(LessonData lesson)
    {
        if (lesson == null) return 0;
        if (lesson.quizQuestionsCount > 0) return lesson.quizQuestionsCount;

        int count = 0;
        if (lesson.quizQuestions != null) count += lesson.quizQuestions.Count;
        if (lesson.questions != null) count += lesson.questions.Count;
        if (lesson.quizzes != null) count += lesson.quizzes.Count;
        if (lesson.quizQuestion != null) count += 1;
        return count;
    }

    private static int GetTimedQuizCount(LessonData lesson)
    {
        if (lesson == null) return 0;
        int count = 0;
        if (lesson.timedQuizzes != null) count += lesson.timedQuizzes.Count;
        if (lesson.interactiveQuizzes != null) count += lesson.interactiveQuizzes.Count;
        if (lesson.popupQuizzes != null) count += lesson.popupQuizzes.Count;
        if (lesson.videoQuizzes != null) count += lesson.videoQuizzes.Count;
        return count;
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

    private static bool HasAnyValidTimedQuizQuestions(List<TimedQuizData> list)
    {
        if (list == null || list.Count == 0) return false;
        for (int i = 0; i < list.Count; i++)
        {
            if (IsMeaningfulTimedQuizQuestion(list[i])) return true;
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

        return hasPrompt && hasOptions;
    }

    private static bool IsMeaningfulTimedQuizQuestion(TimedQuizData q)
    {
        if (q == null) return false;

        bool hasPrompt = !string.IsNullOrWhiteSpace(q.question)
            || !string.IsNullOrWhiteSpace(q.text)
            || !string.IsNullOrWhiteSpace(q.prompt);

        bool hasOptions = (q.options != null && q.options.Count > 0)
            || (q.answers != null && q.answers.Count > 0)
            || (q.choices != null && q.choices.Count > 0);

        return hasPrompt && hasOptions;
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
        return DetermineLessonType(lesson) == "video";
    }

    private static bool HasPlayableVideoSource(LessonData lesson)
    {
        if (lesson == null) return false;
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

    private void EnsureVrLoginPanelUi()
    {
        if (coursesPage == null)
        {
            Debug.LogWarning("[CourseSelectionUI] courses-page was not found. VR login panel cannot be created.");
            return;
        }

        bool hasAllRefs = vrLoginPanel != null
            && vrLoginCodeGroup != null
            && vrLoginStatusLabel != null
            && vrLoginCodeLabel != null
            && vrLoginTimerLabel != null
            && vrLoginUserLabel != null
            && vrLoginRequestButton != null
            && vrLoginRefreshButton != null;

        if (hasAllRefs)
        {
            return;
        }

        Debug.LogWarning("[CourseSelectionUI] VR login UI references are missing from the runtime UXML. Building a fallback panel in code.");

        if (vrLoginPanel == null)
        {
            vrLoginPanel = new VisualElement { name = "vr-login-panel" };
            vrLoginPanel.AddToClassList("vr-login-panel");

            var header = new VisualElement();
            header.AddToClassList("vr-login-panel__header");

            var title = new Label("VR Login");
            title.AddToClassList("vr-login-panel__title");
            header.Add(title);

            vrLoginUserLabel = new Label();
            vrLoginUserLabel.name = "vr-login-user";
            vrLoginUserLabel.AddToClassList("vr-login-panel__user");
            header.Add(vrLoginUserLabel);

            vrLoginPanel.Add(header);

            vrLoginStatusLabel = new Label("Not logged in.");
            vrLoginStatusLabel.name = "vr-login-status";
            vrLoginStatusLabel.AddToClassList("vr-login-panel__status");
            vrLoginPanel.Add(vrLoginStatusLabel);

            vrLoginCodeGroup = new VisualElement { name = "vr-login-code-group" };
            vrLoginCodeGroup.AddToClassList("vr-login-code-group");

            var caption = new Label("Pairing PIN");
            caption.AddToClassList("vr-login-panel__caption");
            vrLoginCodeGroup.Add(caption);

            vrLoginCodeLabel = new Label("00000");
            vrLoginCodeLabel.name = "vr-login-code";
            vrLoginCodeLabel.AddToClassList("vr-login-panel__code");
            vrLoginCodeGroup.Add(vrLoginCodeLabel);

            vrLoginTimerLabel = new Label("Expires in 02:00");
            vrLoginTimerLabel.name = "vr-login-timer";
            vrLoginTimerLabel.AddToClassList("vr-login-panel__timer");
            vrLoginCodeGroup.Add(vrLoginTimerLabel);

            vrLoginPanel.Add(vrLoginCodeGroup);

            var actions = new VisualElement();
            actions.AddToClassList("vr-login-panel__actions");

            vrLoginRequestButton = new Button
            {
                name = "vr-login-request-button",
                text = "Get Login Code"
            };
            vrLoginRequestButton.AddToClassList("vr-login-primary-button");
            actions.Add(vrLoginRequestButton);

            vrLoginRefreshButton = new Button
            {
                name = "vr-login-refresh-button",
                text = "Refresh Code"
            };
            vrLoginRefreshButton.AddToClassList("vr-login-secondary-button");
            actions.Add(vrLoginRefreshButton);

            vrLoginPanel.Add(actions);

            int insertIndex = courseList != null ? coursesPage.IndexOf(courseList) : -1;
            if (insertIndex >= 0)
            {
                coursesPage.Insert(insertIndex, vrLoginPanel);
            }
            else
            {
                coursesPage.Add(vrLoginPanel);
            }
        }
        else
        {
            if (vrLoginUserLabel == null)
            {
                vrLoginUserLabel = vrLoginPanel.Q<Label>("vr-login-user");
            }
            if (vrLoginStatusLabel == null)
            {
                vrLoginStatusLabel = vrLoginPanel.Q<Label>("vr-login-status");
            }
            if (vrLoginCodeGroup == null)
            {
                vrLoginCodeGroup = vrLoginPanel.Q<VisualElement>("vr-login-code-group");
            }
            if (vrLoginCodeLabel == null)
            {
                vrLoginCodeLabel = vrLoginPanel.Q<Label>("vr-login-code");
            }
            if (vrLoginTimerLabel == null)
            {
                vrLoginTimerLabel = vrLoginPanel.Q<Label>("vr-login-timer");
            }
            if (vrLoginRequestButton == null)
            {
                vrLoginRequestButton = vrLoginPanel.Q<Button>("vr-login-request-button");
            }
            if (vrLoginRefreshButton == null)
            {
                vrLoginRefreshButton = vrLoginPanel.Q<Button>("vr-login-refresh-button");
            }
        }

        WarnIfVrLoginRefMissing(vrLoginPanel, "vr-login-panel");
        WarnIfVrLoginRefMissing(vrLoginCodeGroup, "vr-login-code-group");
        WarnIfVrLoginRefMissing(vrLoginStatusLabel, "vr-login-status");
        WarnIfVrLoginRefMissing(vrLoginCodeLabel, "vr-login-code");
        WarnIfVrLoginRefMissing(vrLoginTimerLabel, "vr-login-timer");
        WarnIfVrLoginRefMissing(vrLoginUserLabel, "vr-login-user");
        WarnIfVrLoginRefMissing(vrLoginRequestButton, "vr-login-request-button");
        WarnIfVrLoginRefMissing(vrLoginRefreshButton, "vr-login-refresh-button");
    }

    private void WarnIfVrLoginRefMissing(UnityEngine.Object reference, string elementName)
    {
        if (reference == null)
        {
            Debug.LogWarning($"[CourseSelectionUI] Missing VR login UI reference: {elementName}");
        }
    }

    private void WarnIfVrLoginRefMissing(VisualElement reference, string elementName)
    {
        if (reference == null)
        {
            Debug.LogWarning($"[CourseSelectionUI] Missing VR login UI reference: {elementName}");
        }
    }

    private void EnsureVrAuthManager()
    {
        if (vrAuthManager == null)
        {
            vrAuthManager = GetComponent<VRAuthManager>();
        }

        if (vrAuthManager == null)
        {
            vrAuthManager = gameObject.AddComponent<VRAuthManager>();
        }

        vrAuthManager.StateUpdated -= HandleVrAuthStateUpdated;
        vrAuthManager.StateUpdated += HandleVrAuthStateUpdated;
        vrAuthManager.AuthenticationChanged -= HandleVrAuthenticationChanged;
        vrAuthManager.AuthenticationChanged += HandleVrAuthenticationChanged;
        vrAuthManager.Initialize();
    }

    private async void HandleVrAuthenticationChanged(bool isAuthenticated)
    {
        UpdateVrLoginUi();

        if (isAuthenticated)
        {
            await RefreshCourses();
            return;
        }

        ResetCourseViewForLoggedOutState();
    }

    private void HandleVrAuthStateUpdated()
    {
        UpdateVrLoginUi();
    }

    private async void HandleVrLoginRequestClicked()
    {
        if (vrAuthManager == null)
        {
            return;
        }

        await vrAuthManager.RequestLoginCode();
    }

    private async void HandleVrLoginRefreshClicked()
    {
        if (vrAuthManager == null)
        {
            return;
        }

        await vrAuthManager.RefreshLoginCode();
    }

    private void UpdateVrLoginUi()
    {
        bool isAuthenticated = vrAuthManager != null && vrAuthManager.IsAuthenticated;
        bool isPending = vrAuthManager != null && vrAuthManager.IsPending;
        string code = vrAuthManager != null ? vrAuthManager.CurrentCode : string.Empty;
        string username = vrAuthManager != null ? vrAuthManager.Username : string.Empty;
        string authStatus = vrAuthManager != null ? vrAuthManager.StatusMessage : "Not logged in.";
        int remainingSeconds = vrAuthManager != null ? vrAuthManager.RemainingSeconds : 0;

        if (vrLoginStatusLabel != null)
        {
            vrLoginStatusLabel.text = authStatus;
        }
        else
        {
            Debug.LogWarning("[CourseSelectionUI] vr-login-status label is missing. PIN state cannot be shown.");
        }

        if (vrLoginUserLabel != null)
        {
            vrLoginUserLabel.text = string.IsNullOrWhiteSpace(username)
                ? "Signed in"
                : username;
            vrLoginUserLabel.style.display = isAuthenticated ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (vrLoginCodeGroup != null)
        {
            vrLoginCodeGroup.style.display = isPending ? DisplayStyle.Flex : DisplayStyle.None;
        }
        else
        {
            Debug.LogWarning("[CourseSelectionUI] vr-login-code-group is missing. PIN container cannot be shown.");
        }

        if (vrLoginCodeLabel != null)
        {
            vrLoginCodeLabel.text = string.IsNullOrWhiteSpace(code) ? "00000" : code;
            vrLoginCodeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            vrLoginCodeLabel.style.fontSize = 40f;
            vrLoginCodeLabel.style.opacity = isPending ? 1f : 0.8f;
        }
        else
        {
            Debug.LogWarning("[CourseSelectionUI] vr-login-code label is missing. The pairing PIN cannot be rendered.");
        }

        if (vrLoginTimerLabel != null)
        {
            int minutes = Mathf.Max(0, remainingSeconds) / 60;
            int seconds = Mathf.Max(0, remainingSeconds) % 60;
            vrLoginTimerLabel.text = $"Expires in {minutes:00}:{seconds:00}";
        }
        else
        {
            Debug.LogWarning("[CourseSelectionUI] vr-login-timer label is missing. Countdown cannot be shown.");
        }

        if (vrLoginRequestButton != null)
        {
            vrLoginRequestButton.style.display = isAuthenticated || isPending ? DisplayStyle.None : DisplayStyle.Flex;
            vrLoginRequestButton.SetEnabled(!isAuthenticated && !isPending);
        }

        if (vrLoginRefreshButton != null)
        {
            vrLoginRefreshButton.style.display = isPending ? DisplayStyle.Flex : DisplayStyle.None;
            vrLoginRefreshButton.SetEnabled(isPending);
        }

        if (courseList != null)
        {
            courseList.style.display = isAuthenticated || alwaysUseMockData ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (!isAuthenticated && !alwaysUseMockData && statusLabel != null)
        {
            statusLabel.text = isPending
                ? "Approve the pairing PIN on your profile page to load courses."
                : "Not logged in. Request a login code to pair this device.";
        }

        if (isPending)
        {
            Debug.Log($"[CourseSelectionUI] Showing pairing PIN in UI: {code}");
        }
    }

    private void ResetCourseViewForLoggedOutState(string message = "Log in with the VR pairing code to view your courses.")
    {
        PrepareForLoggedOutUiTransition();
        courses.Clear();
        activeLessons.Clear();
        renderedLessonItems.Clear();
        activeCourse = null;
        selectedLessonId = null;

        if (courseList != null)
        {
            courseList.Rebuild();
            courseList.style.display = alwaysUseMockData ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (sectionsScroll != null)
        {
            sectionsScroll.Clear();
        }

        if (sectionsStatus != null)
        {
            sectionsStatus.text = string.Empty;
        }

        if (statusLabel != null)
        {
            statusLabel.text = message;
        }
    }

    private void PrepareForLoggedOutUiTransition()
    {
        refreshCoursesRequestVersion++;
        openSectionsRequestVersion++;
        isLogoutConfirmationVisible = false;

        if (settingsOverlay != null)
        {
            settingsOverlay.style.display = DisplayStyle.None;
        }

        StopVideo();
        ShowCoursesPage();
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
        if (vrAuthManager != null)
        {
            vrAuthManager.StateUpdated -= HandleVrAuthStateUpdated;
            vrAuthManager.AuthenticationChanged -= HandleVrAuthenticationChanged;
        }
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
