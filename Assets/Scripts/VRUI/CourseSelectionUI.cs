using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class CourseSelectionUI : MonoBehaviour
{
    [Header("UI Toolkit Assets")]
    [SerializeField] private VisualTreeAsset courseSelectionTree;
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

    private UIDocument uiDocument;
    private VisualElement coursesPage;
    private VisualElement sectionsPage;
    private VisualElement videoPage;
    private ListView courseList;
    private Label statusLabel;
    private Button backButton;
    private Label sectionsTitle;
    private ScrollView sectionsScroll;
    private Label sectionsStatus;
    private Button videoBackButton;
    private Label videoTitle;
    private VisualElement videoSurface;
    private Label videoStatus;
    private readonly List<CourseData> courses = new List<CourseData>();
    private readonly List<LessonData> activeLessons = new List<LessonData>();
    private CourseData activeCourse;

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

        if (courseSelectionTree == null)
        {
            Debug.LogError("[CourseSelectionUI] Chưa gán UXML vào courseSelectionTree.");
            return;
        }

        VisualElement root = uiDocument.rootVisualElement;
        root.Clear();

        if (courseSelectionStyle != null)
        {
            root.styleSheets.Add(courseSelectionStyle);
        }

        courseSelectionTree.CloneTree(root);

        coursesPage = root.Q<VisualElement>("courses-page");
        sectionsPage = root.Q<VisualElement>("sections-page");
        statusLabel = root.Q<Label>("status-label");
        courseList = root.Q<ListView>("course-list");
        backButton = root.Q<Button>("back-button");
        sectionsTitle = root.Q<Label>("sections-title");
        sectionsScroll = root.Q<ScrollView>("sections-scroll");
        sectionsStatus = root.Q<Label>("sections-status");
        videoPage = root.Q<VisualElement>("video-page");
        videoBackButton = root.Q<Button>("video-back-button");
        videoTitle = root.Q<Label>("video-title");
        videoSurface = root.Q<VisualElement>("video-surface");
        videoStatus = root.Q<Label>("video-status");

        if (backButton != null)
        {
            backButton.clicked += ShowCoursesPage;
        }

        if (videoBackButton != null)
        {
            videoBackButton.clicked += () =>
            {
                StopVideo();
                ShowSectionsPage();
            };
        }

        ConfigureListView();
        ShowCoursesPage();
    }

    private void ConfigureListView()
    {
        if (courseList == null) return;

        courseList.fixedItemHeight = listItemHeight;
        courseList.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
        courseList.selectionType = SelectionType.None;
        courseList.itemsSource = courses;

        courseList.makeItem = () =>
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("course-row");

            VisualElement thumb = new VisualElement();
            thumb.AddToClassList("course-thumb");

            VisualElement content = new VisualElement();
            content.AddToClassList("course-content");

            Label title = new Label();
            title.AddToClassList("course-title");

            Label progressLabel = new Label();
            progressLabel.AddToClassList("course-progress-label");

            VisualElement progressTrack = new VisualElement();
            progressTrack.AddToClassList("course-progress-track");

            VisualElement progressFill = new VisualElement();
            progressFill.AddToClassList("course-progress-fill");
            progressTrack.Add(progressFill);

            Button viewButton = new Button();
            viewButton.text = "View";
            viewButton.AddToClassList("course-view-button");
            viewButton.RegisterCallback<PointerEnterEvent>(_ =>
            {
                viewButton.AddToClassList("is-hovered");
            });
            viewButton.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                viewButton.RemoveFromClassList("is-hovered");
                viewButton.RemoveFromClassList("is-pressed");
            });
            viewButton.RegisterCallback<PointerDownEvent>(_ =>
            {
                viewButton.AddToClassList("is-pressed");
            });
            viewButton.RegisterCallback<PointerUpEvent>(_ =>
            {
                viewButton.RemoveFromClassList("is-pressed");
            });

            CourseRowRefs refs = new CourseRowRefs
            {
                thumb = thumb,
                title = title,
                progressLabel = progressLabel,
                progressFill = progressFill,
                viewButton = viewButton,
                bindVersion = 0
            };

            viewButton.clicked += () =>
            {
                CourseData selected = viewButton.userData as CourseData;
                if (selected != null)
                {
                    _ = OpenSectionsPageAsync(selected);
                }
            };

            content.Add(title);
            content.Add(progressLabel);
            content.Add(progressTrack);
            content.Add(viewButton);

            row.Add(thumb);
            row.Add(content);
            row.userData = refs;

            return row;
        };

        courseList.bindItem = (element, index) =>
        {
            if (index < 0 || index >= courses.Count) return;

            CourseData course = courses[index];
            CourseRowRefs refs = element.userData as CourseRowRefs;
            if (refs == null) return;

            refs.bindVersion++;
            int version = refs.bindVersion;

            refs.title.text = string.IsNullOrWhiteSpace(course.title) ? "(Không có tiêu đề)" : course.title;
            int percent = Mathf.Clamp(course.progress, 0, 100);
            int total = Mathf.Max(0, course.totalLessons);
            int completed = Mathf.Clamp(course.completedLessons, 0, total > 0 ? total : int.MaxValue);

            if (refs.progressLabel != null)
            {
                refs.progressLabel.text = total > 0
                    ? $"Tiến trình: {percent}% ({completed}/{total} bài)"
                    : $"Tiến trình: {percent}%";
            }

            if (refs.progressFill != null)
            {
                refs.progressFill.style.width = Length.Percent(percent);
            }

            refs.viewButton.userData = course;
            refs.thumb.style.backgroundImage = StyleKeyword.None;

            _ = LoadThumbnailIntoAsync(refs, course.thumbnailUrl, version);
        };
    }

    private async Task OpenSectionsPageAsync(CourseData course)
    {
        if (course == null || ApiManager.Instance == null) return;

        activeCourse = course;
        activeLessons.Clear();

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

            Foldout foldout = new Foldout
            {
                text = $"{sectionName} ({lessonCount})",
                value = false
            };
            foldout.AddToClassList("section-foldout");

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

                    VisualElement row = BuildLessonRow(lesson);
                    foldout.Add(row);
                }
            }

            sectionsScroll.Add(foldout);
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

            Foldout foldout = new Foldout
            {
                text = $"{sectionName} ({sectionLessons.Count})",
                value = false
            };
            foldout.AddToClassList("section-foldout");

            foreach (LessonData lesson in sectionLessons)
            {
                VisualElement row = BuildLessonRow(lesson);
                foldout.Add(row);
            }

            sectionsScroll.Add(foldout);
        }

        if (sectionsStatus != null)
        {
            sectionsStatus.text = $"Tổng {sortedSections.Count} section, {lessons.Count} lesson.";
        }

        UpdateActiveCourseProgressUI();
    }

    private VisualElement BuildLessonRow(LessonData lesson)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("lesson-row");

        VisualElement left = new VisualElement();
        left.AddToClassList("lesson-main");

        Label lessonLabel = new Label();
        lessonLabel.AddToClassList("lesson-label");
        string lessonTitle = string.IsNullOrWhiteSpace(lesson != null ? lesson.title : null) ? "(Không có tiêu đề)" : lesson.title;
        lessonLabel.text = lesson != null && lesson.order > 0
            ? $"{lesson.order:D3} {lessonTitle}"
            : lessonTitle;
        left.Add(lessonLabel);

        Label completedPill = new Label(lesson != null && lesson.isCompleted ? "Completed" : "Not yet");
        completedPill.AddToClassList("lesson-completed-pill");
        if (lesson != null && lesson.isCompleted)
        {
            completedPill.AddToClassList("is-completed");
        }
        left.Add(completedPill);

        VisualElement right = new VisualElement();
        right.AddToClassList("lesson-meta");

        Toggle completeToggle = new Toggle();
        completeToggle.AddToClassList("lesson-checkbox");
        completeToggle.SetValueWithoutNotify(lesson != null && lesson.isCompleted);
        completeToggle.tooltip = "Đánh dấu đã học xong";

        completeToggle.RegisterCallback<ClickEvent>(evt =>
        {
            evt.StopPropagation();
        });

        completeToggle.RegisterValueChangedCallback(evt =>
        {
            _ = OnLessonCompletionChangedAsync(lesson, evt.newValue, completeToggle, completedPill);
        });

        right.Add(completeToggle);

        row.RegisterCallback<ClickEvent>(evt =>
        {
            evt.StopPropagation();
            OnLessonClicked(lesson);
        });

        row.Add(left);
        row.Add(right);
        return row;
    }

    private async Task OnLessonCompletionChangedAsync(LessonData lesson, bool completed, Toggle sourceToggle, Label completedPill)
    {
        if (lesson == null || activeCourse == null || ApiManager.Instance == null) return;

        bool previous = lesson.isCompleted;
        lesson.isCompleted = completed;
        UpdateCompletedPill(completedPill, completed);
        UpdateActiveCourseProgressUI();

        bool synced = await ApiManager.Instance.UpdateLessonCompletionAsync(activeCourse.id, lesson.id, lesson.videoUrl, completed);
        if (synced) return;

        // Revert when backend sync failed.
        lesson.isCompleted = previous;
        if (sourceToggle != null)
        {
            sourceToggle.SetValueWithoutNotify(previous);
        }
        UpdateCompletedPill(completedPill, previous);
        UpdateActiveCourseProgressUI();

        if (sectionsStatus != null)
        {
            sectionsStatus.text = "Không thể lưu tiến độ lên server. Đã hoàn tác thay đổi.";
        }
    }

    private void UpdateCompletedPill(Label pill, bool isCompleted)
    {
        if (pill == null) return;
        pill.text = isCompleted ? "Completed" : "Not yet";
        if (isCompleted) pill.AddToClassList("is-completed");
        else pill.RemoveFromClassList("is-completed");
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
        if (!IsVideoLesson(lesson))
        {
            if (sectionsStatus != null)
            {
                sectionsStatus.text = "Bài này không phải video (quiz/slide sẽ làm sau).";
            }
            return;
        }

        _ = PlayVideoLessonAsync(lesson);
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
            if (videoStatus != null)
            {
                videoStatus.text = "Bài video chưa có videoUrl trong API.";
            }
            return;
        }

        string resolvedUrl = null;
        bool resolverAttempted = false;
        if (ApiManager.Instance != null)
        {
            resolverAttempted = true;
            if (videoStatus != null)
            {
                videoStatus.text = "Đang phân giải nguồn phát...";
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
                if (videoStatus != null) videoStatus.text = openedMsg;
                if (sectionsStatus != null) sectionsStatus.text = openedMsg;
                Debug.Log("[CourseSelectionUI] Opened YouTube URL externally.");
                return;
            }

            string resolverReason = ApiManager.Instance != null ? ApiManager.Instance.LastStreamResolveErrorMessage : null;
            string msg = string.IsNullOrWhiteSpace(resolverReason)
                ? "Link YouTube dạng watch/shorts không phát trực tiếp bằng Unity VideoPlayer. Hãy dùng link stream mp4/m3u8 hoặc URL đã transcode từ backend."
                : $"Không thể phát YouTube trong Unity vì backend resolve lỗi: {resolverReason}. Hãy cài ytdl-core/yt-dlp trên server hoặc dùng link stream mp4/m3u8.";
            if (videoStatus != null) videoStatus.text = msg;
            if (sectionsStatus != null) sectionsStatus.text = msg;
            Debug.Log("[CourseSelectionUI] Unsupported YouTube webpage URL for VideoPlayer.");
            return;
        }

        if (videoPopupWindow == null)
        {
            videoPopupWindow = FindAnyObjectByType<VideoPopupWindow>();
            if (videoPopupWindow == null)
            {
                if (videoStatus != null)
                {
                    videoStatus.text = "Chưa gán VideoPopupWindow trong scene.";
                }
                if (sectionsStatus != null)
                {
                    sectionsStatus.text = "Chưa có VideoPopupWindow để phát video.";
                }
                return;
            }
        }

        if (videoTitle != null)
        {
            videoTitle.text = string.IsNullOrWhiteSpace(lesson.title) ? "Video" : lesson.title;
        }

        if (videoStatus != null)
        {
            videoStatus.text = !string.IsNullOrWhiteSpace(resolvedUrl)
                ? "Đang tải stream đã phân giải..."
                : "Đang tải video...";
        }

        if (sectionsStatus != null)
        {
            sectionsStatus.text = $"Đang mở video: {videoTitle?.text}";
        }

        try
        {
            Transform viewer = anchorManager != null && anchorManager.cameraAnchor != null
                ? anchorManager.cameraAnchor
                : (Camera.main != null ? Camera.main.transform : null);

            await videoPopupWindow.PlayUrlAsync(url, viewer, videoWindowDistance, videoWindowHeightOffset, videoWindowSize);

            if (keepMenuVisibleWhenPlaying) ShowSectionsPage();
            else ShowVideoPage();

            if (videoStatus != null)
            {
                videoStatus.text = "Đang phát video";
            }
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

            if (videoStatus != null)
            {
                videoStatus.text = $"Không phát được video: {ex.Message}";
            }
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
        if (videoSurface != null)
        {
            videoSurface.style.backgroundImage = StyleKeyword.None;
        }

        if (videoPopupWindow != null)
        {
            videoPopupWindow.StopAndHide();
        }
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
        if (videoPage != null) videoPage.AddToClassList("hidden");

        if (anchorManager != null) anchorManager.SetMode(VRPanelAnchorManager.PanelMode.Browsing);
    }

    private void ShowSectionsPage()
    {
        if (coursesPage != null) coursesPage.AddToClassList("hidden");
        if (sectionsPage != null) sectionsPage.RemoveFromClassList("hidden");
        if (videoPage != null) videoPage.AddToClassList("hidden");

        if (anchorManager != null) anchorManager.SetMode(VRPanelAnchorManager.PanelMode.Browsing);
    }

    private void ShowVideoPage()
    {
        if (coursesPage != null) coursesPage.AddToClassList("hidden");
        if (sectionsPage != null) sectionsPage.AddToClassList("hidden");
        if (videoPage != null) videoPage.RemoveFromClassList("hidden");

        if (anchorManager != null) anchorManager.SetMode(VRPanelAnchorManager.PanelMode.Video);
    }

    private void OnDestroy()
    {
        StopVideo();
    }

    private async Task LoadThumbnailIntoAsync(CourseRowRefs refs, string thumbnailUrl, int version)
    {
        if (refs == null || refs.thumb == null || ApiManager.Instance == null) return;
        if (string.IsNullOrWhiteSpace(thumbnailUrl)) return;

        Texture2D texture = await ApiManager.Instance.DownloadImageAsync(thumbnailUrl);
        if (texture == null || refs.bindVersion != version) return;

        refs.thumb.style.backgroundImage = new StyleBackground(texture);
    }

    private class CourseRowRefs
    {
        public VisualElement thumb;
        public Label title;
        public Label progressLabel;
        public VisualElement progressFill;
        public Button viewButton;
        public int bindVersion;
    }
}
