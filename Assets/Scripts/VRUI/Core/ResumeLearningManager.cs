using System;
using UnityEngine;

[Serializable]
public class ResumeLessonData
{
    public int resumeVersion;
    public string userId;
    public string courseId;
    public string courseTitle;
    public string lessonId;
    public string lessonTitle;
    public string lessonType;
    public string sectionName;
    public int sectionIndex;
    public int lessonIndex;
    public double videoTimeSeconds;
    public long savedAtUnixSeconds;
    public long updatedAtUnixSeconds;
}

[DisallowMultipleComponent]
public class ResumeLearningManager : MonoBehaviour
{
    public const string PlayerPrefsKey = "EDUMY_LAST_LESSON";
    private const int CurrentResumeVersion = 1;
    private const float DebounceSeconds = 0.5f;
    private const double VideoTimeFlushIntervalSeconds = 7d;

    private static ResumeLearningManager instance;

    private ResumeLessonData pendingData;
    private bool pendingDirty;
    private float nextFlushAt;
    private Func<double> trackedVideoTimeProvider;
    private double lastTrackedFlushVideoTime = -1d;

    public static ResumeLearningManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<ResumeLearningManager>();
                if (instance == null)
                {
                    GameObject go = new GameObject(nameof(ResumeLearningManager));
                    instance = go.AddComponent<ResumeLearningManager>();
                }
            }

            return instance;
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        ProcessTrackedVideoTime();

        if (!pendingDirty || Time.unscaledTime < nextFlushAt)
        {
            return;
        }

        FlushPendingData();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            StopTrackingVideoResume(flushImmediately: true);
            FlushPendingData();
        }
    }

    private void OnApplicationQuit()
    {
        StopTrackingVideoResume(flushImmediately: true);
        FlushPendingData();
    }

    public static void SaveLastLesson(ResumeLessonData data, bool immediate = false)
    {
        if (data == null)
        {
            return;
        }

        Instance.SaveInternal(data, immediate);
    }

    public static void UpdateVideoTime(double videoTimeSeconds, bool immediate = false)
    {
        Instance.UpdateVideoTimeInternal(videoTimeSeconds, immediate);
    }

    public static void StartTrackingVideoResume(ResumeLessonData seedData, Func<double> timeProvider)
    {
        Instance.StartTrackingInternal(seedData, timeProvider);
    }

    public static void StopTrackingVideoResume(bool flushImmediately = true)
    {
        if (instance == null)
        {
            return;
        }

        instance.StopTrackingInternal(flushImmediately);
    }

    public static bool TryLoadLastLesson(out ResumeLessonData data)
    {
        return Instance.TryLoadInternal(out data);
    }

    public static void ClearLastLessonForUser(string userId)
    {
        Instance.ClearInternal(userId);
    }

    public static bool IsValidForCurrentUser(string userId, ResumeLessonData data)
    {
        return ValidateForUser(data, userId);
    }

    public static ResumeLessonData Build(
        string userId,
        CourseData course,
        LessonData lesson,
        string lessonType,
        string sectionName,
        int sectionIndex,
        int lessonIndex,
        double videoTimeSeconds = 0d)
    {
        if (course == null || lesson == null)
        {
            return null;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return new ResumeLessonData
        {
            resumeVersion = CurrentResumeVersion,
            userId = string.IsNullOrWhiteSpace(userId) ? string.Empty : userId.Trim(),
            courseId = string.IsNullOrWhiteSpace(course.id) ? string.Empty : course.id.Trim(),
            courseTitle = string.IsNullOrWhiteSpace(course.title) ? "Course" : course.title.Trim(),
            lessonId = string.IsNullOrWhiteSpace(lesson.id) ? string.Empty : lesson.id.Trim(),
            lessonTitle = string.IsNullOrWhiteSpace(lesson.title) ? "Lesson" : lesson.title.Trim(),
            lessonType = string.IsNullOrWhiteSpace(lessonType) ? "lesson" : lessonType.Trim().ToLowerInvariant(),
            sectionName = string.IsNullOrWhiteSpace(sectionName) ? "Course Content" : sectionName.Trim(),
            sectionIndex = Mathf.Max(0, sectionIndex),
            lessonIndex = Mathf.Max(0, lessonIndex),
            videoTimeSeconds = Math.Max(0d, videoTimeSeconds),
            savedAtUnixSeconds = now,
            updatedAtUnixSeconds = now
        };
    }

    private void SaveInternal(ResumeLessonData data, bool immediate)
    {
        if (!ValidateCore(data))
        {
            return;
        }

        ResumeLessonData clone = Clone(data);
        clone.resumeVersion = CurrentResumeVersion;
        if (clone.savedAtUnixSeconds <= 0)
        {
            clone.savedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
        clone.updatedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        pendingData = clone;
        pendingDirty = true;
        nextFlushAt = immediate ? 0f : Time.unscaledTime + DebounceSeconds;

        if (immediate)
        {
            FlushPendingData();
        }
    }

    private void UpdateVideoTimeInternal(double videoTimeSeconds, bool immediate)
    {
        if (!ValidateCore(pendingData))
        {
            if (!TryLoadInternal(out ResumeLessonData loaded))
            {
                return;
            }

            pendingData = loaded;
        }

        videoTimeSeconds = Math.Max(0d, videoTimeSeconds);
        if (Math.Abs(pendingData.videoTimeSeconds - videoTimeSeconds) < 0.25d && !immediate)
        {
            return;
        }

        pendingData.videoTimeSeconds = videoTimeSeconds;
        pendingData.updatedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        pendingDirty = true;
        nextFlushAt = immediate ? 0f : Time.unscaledTime + DebounceSeconds;

        if (immediate)
        {
            FlushPendingData();
        }
    }

    private void StartTrackingInternal(ResumeLessonData seedData, Func<double> timeProvider)
    {
        if (seedData != null)
        {
            SaveInternal(seedData, immediate: true);
        }

        trackedVideoTimeProvider = timeProvider;
        lastTrackedFlushVideoTime = pendingData != null ? pendingData.videoTimeSeconds : -1d;
    }

    private void StopTrackingInternal(bool flushImmediately)
    {
        if (trackedVideoTimeProvider != null)
        {
            double currentTime = SafeGetTrackedVideoTime();
            if (currentTime >= 0d)
            {
                UpdateVideoTimeInternal(currentTime, flushImmediately);
            }
        }

        trackedVideoTimeProvider = null;
        lastTrackedFlushVideoTime = -1d;

        if (flushImmediately)
        {
            FlushPendingData();
        }
    }

    private void ProcessTrackedVideoTime()
    {
        if (trackedVideoTimeProvider == null)
        {
            return;
        }

        double currentTime = SafeGetTrackedVideoTime();
        if (currentTime < 0d)
        {
            return;
        }

        if (lastTrackedFlushVideoTime < 0d || Math.Abs(currentTime - lastTrackedFlushVideoTime) >= VideoTimeFlushIntervalSeconds)
        {
            lastTrackedFlushVideoTime = currentTime;
            UpdateVideoTimeInternal(currentTime, immediate: false);
        }
    }

    private double SafeGetTrackedVideoTime()
    {
        if (trackedVideoTimeProvider == null)
        {
            return -1d;
        }

        try
        {
            return trackedVideoTimeProvider();
        }
        catch
        {
            return -1d;
        }
    }

    private bool TryLoadInternal(out ResumeLessonData data)
    {
        data = null;
        if (!PlayerPrefs.HasKey(PlayerPrefsKey))
        {
            return false;
        }

        string json = PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
        {
            ClearInternal(null);
            return false;
        }

        try
        {
            data = JsonUtility.FromJson<ResumeLessonData>(json);
        }
        catch
        {
            data = null;
            ClearInternal(null);
            return false;
        }

        if (!ValidateCore(data))
        {
            data = null;
            ClearInternal(null);
            return false;
        }

        return true;
    }

    private void ClearInternal(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            pendingData = null;
            pendingDirty = false;
            trackedVideoTimeProvider = null;
            lastTrackedFlushVideoTime = -1d;
            if (PlayerPrefs.HasKey(PlayerPrefsKey))
            {
                PlayerPrefs.DeleteKey(PlayerPrefsKey);
                PlayerPrefs.Save();
            }
            return;
        }

        if (!TryLoadInternalIgnoringCorruption(out ResumeLessonData existing))
        {
            if (PlayerPrefs.HasKey(PlayerPrefsKey))
            {
                PlayerPrefs.DeleteKey(PlayerPrefsKey);
                PlayerPrefs.Save();
            }
            return;
        }

        if (!ValidateForUser(existing, userId))
        {
            return;
        }

        pendingData = null;
        pendingDirty = false;
        trackedVideoTimeProvider = null;
        lastTrackedFlushVideoTime = -1d;
        if (PlayerPrefs.HasKey(PlayerPrefsKey))
        {
            PlayerPrefs.DeleteKey(PlayerPrefsKey);
            PlayerPrefs.Save();
        }
    }

    private bool TryLoadInternalIgnoringCorruption(out ResumeLessonData data)
    {
        data = null;
        if (!PlayerPrefs.HasKey(PlayerPrefsKey))
        {
            return false;
        }

        string json = PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            data = JsonUtility.FromJson<ResumeLessonData>(json);
        }
        catch
        {
            data = null;
            return false;
        }

        return data != null;
    }

    private void FlushPendingData()
    {
        if (!pendingDirty || !ValidateCore(pendingData))
        {
            pendingDirty = false;
            return;
        }

        pendingData.resumeVersion = CurrentResumeVersion;
        pendingData.updatedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (pendingData.savedAtUnixSeconds <= 0)
        {
            pendingData.savedAtUnixSeconds = pendingData.updatedAtUnixSeconds;
        }

        // Future backend sync can hook here before or after local persistence.
        PlayerPrefs.SetString(PlayerPrefsKey, JsonUtility.ToJson(pendingData));
        PlayerPrefs.Save();
        pendingDirty = false;
    }

    private static ResumeLessonData Clone(ResumeLessonData source)
    {
        if (source == null)
        {
            return null;
        }

        return new ResumeLessonData
        {
            resumeVersion = source.resumeVersion,
            userId = source.userId,
            courseId = source.courseId,
            courseTitle = source.courseTitle,
            lessonId = source.lessonId,
            lessonTitle = source.lessonTitle,
            lessonType = source.lessonType,
            sectionName = source.sectionName,
            sectionIndex = source.sectionIndex,
            lessonIndex = source.lessonIndex,
            videoTimeSeconds = source.videoTimeSeconds,
            savedAtUnixSeconds = source.savedAtUnixSeconds,
            updatedAtUnixSeconds = source.updatedAtUnixSeconds
        };
    }

    private static bool ValidateCore(ResumeLessonData data)
    {
        if (data == null)
        {
            return false;
        }

        if (data.resumeVersion <= 0)
        {
            data.resumeVersion = CurrentResumeVersion;
        }

        if (string.IsNullOrWhiteSpace(data.courseId) || string.IsNullOrWhiteSpace(data.lessonId))
        {
            return false;
        }

        data.courseId = data.courseId.Trim();
        data.lessonId = data.lessonId.Trim();
        data.courseTitle = string.IsNullOrWhiteSpace(data.courseTitle) ? "Course" : data.courseTitle.Trim();
        data.lessonTitle = string.IsNullOrWhiteSpace(data.lessonTitle) ? "Lesson" : data.lessonTitle.Trim();
        data.lessonType = string.IsNullOrWhiteSpace(data.lessonType) ? "lesson" : data.lessonType.Trim().ToLowerInvariant();
        data.sectionName = string.IsNullOrWhiteSpace(data.sectionName) ? "Course Content" : data.sectionName.Trim();
        data.sectionIndex = Mathf.Max(0, data.sectionIndex);
        data.lessonIndex = Mathf.Max(0, data.lessonIndex);
        data.videoTimeSeconds = Math.Max(0d, data.videoTimeSeconds);
        return true;
    }

    private static bool ValidateForUser(ResumeLessonData data, string userId)
    {
        if (!ValidateCore(data))
        {
            return false;
        }

        string normalizedUserId = string.IsNullOrWhiteSpace(userId) ? string.Empty : userId.Trim();
        if (string.IsNullOrWhiteSpace(normalizedUserId))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(data.userId)
            && string.Equals(data.userId.Trim(), normalizedUserId, StringComparison.Ordinal);
    }
}
