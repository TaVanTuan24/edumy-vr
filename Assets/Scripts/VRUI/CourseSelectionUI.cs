using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

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
    private VideoPlayer videoPlayer;
    private RenderTexture videoRenderTexture;

    private async void Start()
    {
        BuildUi();
        await RefreshCourses();
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

            Button viewButton = new Button();
            viewButton.text = "View";
            viewButton.AddToClassList("course-view-button");

            CourseRowRefs refs = new CourseRowRefs
            {
                thumb = thumb,
                title = title,
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
            refs.viewButton.userData = course;
            refs.thumb.style.backgroundImage = StyleKeyword.None;

            _ = LoadThumbnailIntoAsync(refs, course.thumbnailUrl, version);
        };
    }

    private async Task OpenSectionsPageAsync(CourseData course)
    {
        if (course == null || ApiManager.Instance == null) return;

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
                    VisualElement row = new VisualElement();
                    row.AddToClassList("lesson-row");

                    Label lessonLabel = new Label();
                    lessonLabel.AddToClassList("lesson-label");
                    string lessonTitle = string.IsNullOrWhiteSpace(lesson != null ? lesson.title : null) ? "(Không có tiêu đề)" : lesson.title;
                    lessonLabel.text = lesson != null && lesson.order > 0
                        ? $"Bài {lesson.order}: {lessonTitle}"
                        : lessonTitle;

                    row.RegisterCallback<ClickEvent>(evt =>
                    {
                        evt.StopPropagation();
                        OnLessonClicked(lesson);
                    });

                    row.Add(lessonLabel);
                    foldout.Add(row);
                }
            }

            sectionsScroll.Add(foldout);
        }

        if (sectionsStatus != null)
        {
            sectionsStatus.text = $"Tổng {sortedSections.Count} section, {totalLessons} lesson.";
        }
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
                VisualElement row = new VisualElement();
                row.AddToClassList("lesson-row");

                Label lessonLabel = new Label();
                lessonLabel.AddToClassList("lesson-label");
                string lessonTitle = string.IsNullOrWhiteSpace(lesson.title) ? "(Không có tiêu đề)" : lesson.title;
                lessonLabel.text = lesson.order > 0
                    ? $"Bài {lesson.order}: {lessonTitle}"
                    : lessonTitle;

                row.RegisterCallback<ClickEvent>(evt =>
                {
                    evt.StopPropagation();
                    OnLessonClicked(lesson);
                });

                row.Add(lessonLabel);
                foldout.Add(row);
            }

            sectionsScroll.Add(foldout);
        }

        if (sectionsStatus != null)
        {
            sectionsStatus.text = $"Tổng {sortedSections.Count} section, {lessons.Count} lesson.";
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

        string url = NormalizeVideoUrlForPlayer(lesson.videoUrl);
        if (string.IsNullOrWhiteSpace(url))
        {
            if (videoStatus != null)
            {
                videoStatus.text = "Bài video chưa có videoUrl trong API.";
            }
            return;
        }

        EnsureVideoPlayer();
        if (videoPlayer == null || videoSurface == null) return;

        if (videoTitle != null)
        {
            videoTitle.text = string.IsNullOrWhiteSpace(lesson.title) ? "Video" : lesson.title;
        }

        if (videoStatus != null)
        {
            videoStatus.text = "Đang tải video...";
        }

        ShowVideoPage();

        videoPlayer.Stop();
        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = url;
        videoPlayer.isLooping = false;

        var tcs = new TaskCompletionSource<bool>();
        void Prepared(VideoPlayer _) => tcs.TrySetResult(true);
        void Error(VideoPlayer _, string msg) => tcs.TrySetException(new Exception(msg));

        videoPlayer.prepareCompleted += Prepared;
        videoPlayer.errorReceived += Error;

        try
        {
            videoPlayer.Prepare();
            await tcs.Task;
            videoPlayer.Play();

            if (videoStatus != null)
            {
                videoStatus.text = "Đang phát video";
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CourseSelectionUI] Play video failed: {ex.Message}");
            if (videoStatus != null)
            {
                videoStatus.text = $"Không phát được video: {ex.Message}";
            }
        }
        finally
        {
            videoPlayer.prepareCompleted -= Prepared;
            videoPlayer.errorReceived -= Error;
        }
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

    private void EnsureVideoPlayer()
    {
        if (videoPlayer == null)
        {
            videoPlayer = gameObject.GetComponent<VideoPlayer>();
            if (videoPlayer == null)
            {
                videoPlayer = gameObject.AddComponent<VideoPlayer>();
            }

            videoPlayer.playOnAwake = false;
            videoPlayer.waitForFirstFrame = true;
            videoPlayer.skipOnDrop = true;
            videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
        }

        if (videoRenderTexture == null)
        {
            videoRenderTexture = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32)
            {
                name = "CourseVideoRT"
            };
            videoRenderTexture.Create();
        }

        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.targetTexture = videoRenderTexture;

        if (videoSurface != null)
        {
            Background bg = Background.FromRenderTexture(videoRenderTexture);
            videoSurface.style.backgroundImage = new StyleBackground(bg);
        }
    }

    private void StopVideo()
    {
        if (videoPlayer != null)
        {
            if (videoPlayer.isPlaying) videoPlayer.Stop();
            videoPlayer.targetTexture = null;
        }

        if (videoSurface != null)
        {
            videoSurface.style.backgroundImage = StyleKeyword.None;
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
    }

    private void ShowSectionsPage()
    {
        if (coursesPage != null) coursesPage.AddToClassList("hidden");
        if (sectionsPage != null) sectionsPage.RemoveFromClassList("hidden");
        if (videoPage != null) videoPage.AddToClassList("hidden");
    }

    private void ShowVideoPage()
    {
        if (coursesPage != null) coursesPage.AddToClassList("hidden");
        if (sectionsPage != null) sectionsPage.AddToClassList("hidden");
        if (videoPage != null) videoPage.RemoveFromClassList("hidden");
    }

    private void OnDestroy()
    {
        StopVideo();
        if (videoRenderTexture != null)
        {
            videoRenderTexture.Release();
            Destroy(videoRenderTexture);
            videoRenderTexture = null;
        }
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
        public Button viewButton;
        public int bindVersion;
    }
}
