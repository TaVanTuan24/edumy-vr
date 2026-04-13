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
    [SerializeField] private bool keepMenuVisibleWhenPlaying = true;
    [SerializeField] private bool openYouTubeExternally = true;
    [SerializeField] private bool autoOpenYouTubeWhenResolveFails = false;
    [SerializeField] private VideoPopupWindow videoPopupWindow;
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
    [SerializeField] private bool useIndependentQuizSlideScreens = true;
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
    private Label sectionsTitle;
    private ScrollView sectionsScroll;
    private Label sectionsStatus;
    private readonly List<CourseData> courses = new List<CourseData>();
    private readonly List<LessonData> activeLessons = new List<LessonData>();
    private readonly List<LessonItemElement> renderedLessonItems = new List<LessonItemElement>();
    private CourseData activeCourse;
    private string selectedLessonId;

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
        if (statusLabel != null) statusLabel.text = "Đang tải dữ liệu khóa học...";

        bool usedMock = false;

        try
        {
            if (ApiManager.Instance == null)
            {
                throw new InvalidOperationException("Khong tim thay ApiManager trong scene.");
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
                    ? "Chưa có khóa học để hiển thị."
                    : usedMock
                        ? $"Đã tải {courses.Count} khóa học (Mock Editor)."
                        : $"Đã tải {courses.Count} khóa học.";
            }
        }
        catch (Exception ex)
        {
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
                    statusLabel.text = "API lỗi, đang hiển thị dữ liệu mock để test UI trong Editor.";
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
                    statusLabel.text = $"Tai khoa hoc that bai: {msg}";
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
        root.Add(lessonSelectionWindow);

        coursesPage = lessonSelectionWindow.Q<VisualElement>("courses-page");
        sectionsPage = lessonSelectionWindow.Q<VisualElement>("sections-page");
        statusLabel = lessonSelectionWindow.Q<Label>("status-label");
        courseList = lessonSelectionWindow.Q<ListView>("course-list");
        backButton = lessonSelectionWindow.Q<Button>("back-button");
        sectionsTitle = lessonSelectionWindow.Q<Label>("sections-title");
        sectionsScroll = lessonSelectionWindow.Q<ScrollView>("sections-scroll");
        sectionsStatus = lessonSelectionWindow.Q<Label>("sections-status");

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

        ConfigureListView();
        ShowCoursesPage();
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
            sectionsStatus.text = "Đang tải section và lesson...";
        }

        if (sectionsScroll != null)
        {
            sectionsScroll.Clear();
        }

        ShowSectionsPage();

        try
        {
            List<SectionData> sections = await ApiManager.Instance.GetCourseSectionsAsync(course.id);
            if (HasRenderableSections(sections))
            {
                RenderExplicitSections(sections);
                return;
            }

            // Fallback cuoi cung neu endpoint hien tai chi tra lesson list.
            List<LessonData> lessons = await ApiManager.Instance.GetLessonsAsync(course.id);
            RenderSections(lessons);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CourseSelectionUI] Load sections failed: {ex.Message}");
            if (sectionsStatus != null)
            {
                sectionsStatus.text = $"Tải sections thất bại: {ex.Message}";
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
                sectionsStatus.text = "Khóa học này chưa có section.";
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
            sectionsStatus.text = $"Tổng {sortedSections.Count} section, {totalLessons} lesson.";
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
                sectionsStatus.text = "Khóa học này chưa có lesson.";
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
            sectionsStatus.text = $"Tổng {sortedSections.Count} section, {lessons.Count} lesson.";
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
            sectionsStatus.text = "Không thể lưu tiến độ lên server. Đã hoàn tác thay đổi.";
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
            sectionsStatus.text = $"Tiến độ học: {activeCourse.completedLessons} / {Mathf.Max(0, total)} bài ({activeCourse.progress}%)";
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
                    sectionName = "Nội dung khóa học";
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
                    currentSection = "Nội dung khóa học";
                    EnsureSectionBucket(currentSection, grouped);
                }

                grouped[currentSection].Add(lesson);
            }

            return grouped;
        }

        // Strategy C: Final fallback - keep all lessons under one section.
        const string defaultSection = "Nội dung khóa học";
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

        return "Nội dung khóa học";
    }

    private static string GetSectionDisplayName(SectionData section)
    {
        if (section == null) return "Nội dung khóa học";

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
        bool quizLesson = IsQuizLesson(lesson);
        if (enableSlideDebugLogs)
        {
            Debug.Log($"[CourseSelectionUI] Route decision slide={slideLesson} quiz={quizLesson} video={IsVideoLesson(lesson)}");
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
                sectionsStatus.text = "Bài học slide chưa có dữ liệu hợp lệ hoặc chưa gán SlidePopupWindow.";
            }
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
                sectionsStatus.text = "Bài học quiz chưa có dữ liệu hợp lệ hoặc chưa gán QuizPopupWindow.";
            }
            return;
        }

        if (IsVideoLesson(lesson))
        {
            _ = PlayVideoLessonAsync(lesson);
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
                sectionsStatus.text = "Bài học được nhận diện là quiz nhưng không có dữ liệu câu hỏi hợp lệ.";
            }
            return;
        }

        if (IsSlideLesson(lesson))
        {
            if (sectionsStatus != null)
            {
                sectionsStatus.text = "Bài học được nhận diện là slide nhưng không có nội dung slide hợp lệ.";
            }
            return;
        }

        if (sectionsStatus != null)
        {
            sectionsStatus.text = "Bài này chưa có kiểu nội dung hỗ trợ.";
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
            if (slideScreenAnchor != null && slideScreenAnchor.gameObject.activeInHierarchy)
            {
                slidePopupWindow.PlaceAtAnchor(slideScreenAnchor);
            }
            else
            {
                // Ensure the slide window is visible even when no anchor is configured.
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
                sectionsStatus.text = "Đã mở màn hình Slide độc lập trong scene.";
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CourseSelectionUI] ShowSlideIndependentScreen exception: {ex}");
            if (sectionsStatus != null)
            {
                sectionsStatus.text = $"Lỗi mở màn hình Slide: {ex.Message}";
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

        if (quizScreenAnchor != null)
        {
            quizPopupWindow.PlaceAtAnchor(quizScreenAnchor);
        }

        Transform viewer = GetViewerTransform();
        bool shown = quizPopupWindow.Show(lesson, viewer, quizWindowDistance, quizWindowHeightOffset);
        if (!shown) return false;

        if (slidePopupWindow != null)
        {
            slidePopupWindow.HideWindow();
        }

        if (sectionsStatus != null)
        {
            sectionsStatus.text = "Đã mở màn hình Quiz độc lập trong scene.";
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
                sectionsStatus.text = "Bài video chưa có videoUrl trong API.";
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
                sectionsStatus.text = "Đang phân giải nguồn phát...";
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
                const string openedMsg = "Đã mở YouTube bên ngoài để phát video (Unity VideoPlayer không hỗ trợ trực tiếp link watch/shorts).";
                if (sectionsStatus != null) sectionsStatus.text = openedMsg;
                Debug.Log("[CourseSelectionUI] Opened YouTube URL externally.");
                return;
            }

            string resolverReason = ApiManager.Instance != null ? ApiManager.Instance.LastStreamResolveErrorMessage : null;
            string msg = string.IsNullOrWhiteSpace(resolverReason)
                ? "Link YouTube dạng watch/shorts không phát trực tiếp bằng Unity VideoPlayer. Hãy dùng link stream mp4/m3u8 hoặc URL đã transcode từ backend."
                : $"Không thể phát YouTube trong Unity vì backend resolve lỗi: {resolverReason}. Hãy cài ytdl-core/yt-dlp trên server hoặc dùng link stream mp4/m3u8.";
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
                    sectionsStatus.text = "Chưa có VideoPopupWindow để phát video.";
                }
                return;
            }
        }

        if (sectionsStatus != null)
        {
            string displayTitle = string.IsNullOrWhiteSpace(lesson.title) ? "Video" : lesson.title;
            sectionsStatus.text = $"Đang mở video: {displayTitle}";
        }

        try
        {
            Transform viewer = anchorManager != null && anchorManager.cameraAnchor != null
                ? anchorManager.cameraAnchor
                : (Camera.main != null ? Camera.main.transform : null);

            await videoPopupWindow.PlayUrlAsync(url, viewer, videoWindowDistance, videoWindowHeightOffset, videoWindowSize);

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

            if (sectionsStatus != null) sectionsStatus.text = "Đang phát video";
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

            if (sectionsStatus != null) sectionsStatus.text = $"Không phát được video: {ex.Message}";
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
        }

        if (videoQuizScheduler != null)
        {
            videoQuizScheduler.StopAndClear();
        }

        if (timedQuizPopupWindow != null)
        {
            timedQuizPopupWindow.HideWindow(resumeVideo: false);
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
        if (string.IsNullOrWhiteSpace(sectionName)) return "Nội dung khóa học";

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
        if (string.IsNullOrWhiteSpace(sectionName)) sectionName = "Nội dung khóa học";

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

        if (anchorManager != null) anchorManager.SetMode(VRPanelAnchorManager.PanelMode.Browsing);
    }

    private void ShowSectionsPage()
    {
        if (coursesPage != null) coursesPage.AddToClassList("hidden");
        if (sectionsPage != null) sectionsPage.RemoveFromClassList("hidden");
        if (slidePopupWindow != null) slidePopupWindow.HideWindow();
        if (quizPopupWindow != null) quizPopupWindow.HideWindow();

        if (anchorManager != null) anchorManager.SetMode(VRPanelAnchorManager.PanelMode.Browsing);
    }

    private void OnDestroy()
    {
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
