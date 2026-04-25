using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
public class RecentCourseEntry
{
    public string courseId;
    public string courseTitle;
    public long openedAtUnixSeconds;
}

[Serializable]
public class StudyBookmarkData
{
    public string bookmarkId;
    public string userId;
    public string courseId;
    public string courseTitle;
    public string lessonId;
    public string lessonTitle;
    public string lessonType;
    public string sectionName;
    public int sectionIndex;
    public int lessonIndex;
    public double timestampSeconds;
    public int slideIndex;
    public int questionIndex;
    public long createdAtUnixSeconds;
}

[Serializable]
public class FavoriteCoursesStore
{
    public int version = 1;
    public List<string> courseIds = new List<string>();
}

[Serializable]
public class RecentCoursesStore
{
    public int version = 1;
    public List<RecentCourseEntry> items = new List<RecentCourseEntry>();
}

[Serializable]
public class StudyBookmarksStore
{
    public int version = 1;
    public List<StudyBookmarkData> items = new List<StudyBookmarkData>();
}

public static class LocalStudyStateManager
{
    private const int RecentCourseLimit = 10;
    private const int BookmarkLimit = 30;

    public static List<string> GetFavoriteCourseIds(string userId)
    {
        FavoriteCoursesStore store = LoadStore(GetFavoritesKey(userId), new FavoriteCoursesStore());
        return store.courseIds != null
            ? store.courseIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.Ordinal).ToList()
            : new List<string>();
    }

    public static bool IsFavoriteCourse(string userId, string courseId)
    {
        if (string.IsNullOrWhiteSpace(courseId))
        {
            return false;
        }

        return GetFavoriteCourseIds(userId).Contains(courseId.Trim());
    }

    public static bool ToggleFavoriteCourse(string userId, string courseId)
    {
        if (string.IsNullOrWhiteSpace(courseId))
        {
            return false;
        }

        FavoriteCoursesStore store = LoadStore(GetFavoritesKey(userId), new FavoriteCoursesStore());
        if (store.courseIds == null)
        {
            store.courseIds = new List<string>();
        }

        string normalizedCourseId = courseId.Trim();
        if (store.courseIds.Contains(normalizedCourseId))
        {
            store.courseIds.RemoveAll(id => string.Equals(id, normalizedCourseId, StringComparison.Ordinal));
            SaveStore(GetFavoritesKey(userId), store);
            return false;
        }

        store.courseIds.Add(normalizedCourseId);
        SaveStore(GetFavoritesKey(userId), store);
        return true;
    }

    public static List<RecentCourseEntry> GetRecentCourses(string userId)
    {
        RecentCoursesStore store = LoadStore(GetRecentCoursesKey(userId), new RecentCoursesStore());
        return store.items != null
            ? store.items
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.courseId))
                .OrderByDescending(item => item.openedAtUnixSeconds)
                .ToList()
            : new List<RecentCourseEntry>();
    }

    public static void TrackRecentCourse(string userId, CourseData course)
    {
        if (course == null || string.IsNullOrWhiteSpace(course.id))
        {
            return;
        }

        RecentCoursesStore store = LoadStore(GetRecentCoursesKey(userId), new RecentCoursesStore());
        if (store.items == null)
        {
            store.items = new List<RecentCourseEntry>();
        }

        string normalizedCourseId = course.id.Trim();
        store.items.RemoveAll(item => item == null || string.Equals(item.courseId, normalizedCourseId, StringComparison.Ordinal));
        store.items.Insert(0, new RecentCourseEntry
        {
            courseId = normalizedCourseId,
            courseTitle = string.IsNullOrWhiteSpace(course.title) ? "Course" : course.title.Trim(),
            openedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        if (store.items.Count > RecentCourseLimit)
        {
            store.items.RemoveRange(RecentCourseLimit, store.items.Count - RecentCourseLimit);
        }

        SaveStore(GetRecentCoursesKey(userId), store);
    }

    public static List<StudyBookmarkData> GetBookmarks(string userId)
    {
        StudyBookmarksStore store = LoadStore(GetBookmarksKey(userId), new StudyBookmarksStore());
        return store.items != null
            ? store.items
                .Where(item => ValidateBookmark(item, userId))
                .OrderByDescending(item => item.createdAtUnixSeconds)
                .ToList()
            : new List<StudyBookmarkData>();
    }

    public static bool IsBookmarked(string userId, string courseId, string lessonId, string lessonType, double timestampSeconds = 0d, int slideIndex = -1, int questionIndex = -1)
    {
        return FindBookmark(userId, courseId, lessonId, lessonType, timestampSeconds, slideIndex, questionIndex) != null;
    }

    public static StudyBookmarkData FindBookmark(string userId, string courseId, string lessonId, string lessonType, double timestampSeconds = 0d, int slideIndex = -1, int questionIndex = -1)
    {
        List<StudyBookmarkData> items = GetBookmarks(userId);
        string normalizedCourseId = string.IsNullOrWhiteSpace(courseId) ? string.Empty : courseId.Trim();
        string normalizedLessonId = string.IsNullOrWhiteSpace(lessonId) ? string.Empty : lessonId.Trim();
        string normalizedLessonType = string.IsNullOrWhiteSpace(lessonType) ? string.Empty : lessonType.Trim().ToLowerInvariant();

        return items.FirstOrDefault(item =>
            string.Equals(item.courseId, normalizedCourseId, StringComparison.Ordinal)
            && string.Equals(item.lessonId ?? string.Empty, normalizedLessonId, StringComparison.Ordinal)
            && string.Equals(item.lessonType ?? string.Empty, normalizedLessonType, StringComparison.Ordinal)
            && Math.Abs(item.timestampSeconds - Math.Max(0d, timestampSeconds)) < 0.6d
            && item.slideIndex == Mathf.Max(-1, slideIndex)
            && item.questionIndex == Mathf.Max(-1, questionIndex));
    }

    public static bool ToggleBookmark(string userId, StudyBookmarkData bookmark)
    {
        if (!ValidateBookmark(bookmark, userId))
        {
            return false;
        }

        StudyBookmarksStore store = LoadStore(GetBookmarksKey(userId), new StudyBookmarksStore());
        if (store.items == null)
        {
            store.items = new List<StudyBookmarkData>();
        }

        StudyBookmarkData existing = store.items.FirstOrDefault(item => SameBookmark(item, bookmark));
        if (existing != null)
        {
            store.items.Remove(existing);
            SaveStore(GetBookmarksKey(userId), store);
            return false;
        }

        bookmark.bookmarkId = string.IsNullOrWhiteSpace(bookmark.bookmarkId) ? Guid.NewGuid().ToString("N") : bookmark.bookmarkId;
        bookmark.createdAtUnixSeconds = bookmark.createdAtUnixSeconds > 0 ? bookmark.createdAtUnixSeconds : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        store.items.Insert(0, bookmark);
        if (store.items.Count > BookmarkLimit)
        {
            store.items.RemoveRange(BookmarkLimit, store.items.Count - BookmarkLimit);
        }

        SaveStore(GetBookmarksKey(userId), store);
        return true;
    }

    public static void RemoveBookmark(string userId, StudyBookmarkData bookmark)
    {
        if (bookmark == null)
        {
            return;
        }

        StudyBookmarksStore store = LoadStore(GetBookmarksKey(userId), new StudyBookmarksStore());
        if (store.items == null)
        {
            return;
        }

        store.items.RemoveAll(item => SameBookmark(item, bookmark));
        SaveStore(GetBookmarksKey(userId), store);
    }

    public static StudyBookmarkData BuildBookmark(
        string userId,
        CourseData course,
        LessonData lesson,
        string lessonType,
        string sectionName,
        int sectionIndex,
        int lessonIndex,
        double timestampSeconds = 0d,
        int slideIndex = -1,
        int questionIndex = -1)
    {
        if (course == null || string.IsNullOrWhiteSpace(course.id))
        {
            return null;
        }

        return new StudyBookmarkData
        {
            bookmarkId = Guid.NewGuid().ToString("N"),
            userId = NormalizeUserId(userId),
            courseId = course.id.Trim(),
            courseTitle = string.IsNullOrWhiteSpace(course.title) ? "Course" : course.title.Trim(),
            lessonId = lesson != null && !string.IsNullOrWhiteSpace(lesson.id) ? lesson.id.Trim() : string.Empty,
            lessonTitle = lesson != null && !string.IsNullOrWhiteSpace(lesson.title) ? lesson.title.Trim() : string.Empty,
            lessonType = string.IsNullOrWhiteSpace(lessonType) ? "course" : lessonType.Trim().ToLowerInvariant(),
            sectionName = string.IsNullOrWhiteSpace(sectionName) ? "Course Content" : sectionName.Trim(),
            sectionIndex = Mathf.Max(-1, sectionIndex),
            lessonIndex = Mathf.Max(-1, lessonIndex),
            timestampSeconds = Math.Max(0d, timestampSeconds),
            slideIndex = Mathf.Max(-1, slideIndex),
            questionIndex = Mathf.Max(-1, questionIndex),
            createdAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
    }

    private static bool SameBookmark(StudyBookmarkData a, StudyBookmarkData b)
    {
        if (a == null || b == null)
        {
            return false;
        }

        return string.Equals(a.userId ?? string.Empty, b.userId ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(a.courseId ?? string.Empty, b.courseId ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(a.lessonId ?? string.Empty, b.lessonId ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(a.lessonType ?? string.Empty, b.lessonType ?? string.Empty, StringComparison.Ordinal)
            && Math.Abs(a.timestampSeconds - b.timestampSeconds) < 0.6d
            && a.slideIndex == b.slideIndex
            && a.questionIndex == b.questionIndex;
    }

    private static bool ValidateBookmark(StudyBookmarkData bookmark, string userId)
    {
        if (bookmark == null || string.IsNullOrWhiteSpace(bookmark.courseId))
        {
            return false;
        }

        string normalizedUserId = NormalizeUserId(userId);
        string bookmarkUserId = NormalizeUserId(bookmark.userId);
        if (!string.IsNullOrWhiteSpace(normalizedUserId) && !string.Equals(bookmarkUserId, normalizedUserId, StringComparison.Ordinal))
        {
            return false;
        }

        bookmark.userId = bookmarkUserId;
        bookmark.courseId = bookmark.courseId.Trim();
        bookmark.courseTitle = string.IsNullOrWhiteSpace(bookmark.courseTitle) ? "Course" : bookmark.courseTitle.Trim();
        bookmark.lessonId = string.IsNullOrWhiteSpace(bookmark.lessonId) ? string.Empty : bookmark.lessonId.Trim();
        bookmark.lessonTitle = string.IsNullOrWhiteSpace(bookmark.lessonTitle) ? string.Empty : bookmark.lessonTitle.Trim();
        bookmark.lessonType = string.IsNullOrWhiteSpace(bookmark.lessonType) ? "course" : bookmark.lessonType.Trim().ToLowerInvariant();
        bookmark.sectionName = string.IsNullOrWhiteSpace(bookmark.sectionName) ? "Course Content" : bookmark.sectionName.Trim();
        bookmark.sectionIndex = Mathf.Max(-1, bookmark.sectionIndex);
        bookmark.lessonIndex = Mathf.Max(-1, bookmark.lessonIndex);
        bookmark.slideIndex = Mathf.Max(-1, bookmark.slideIndex);
        bookmark.questionIndex = Mathf.Max(-1, bookmark.questionIndex);
        bookmark.timestampSeconds = Math.Max(0d, bookmark.timestampSeconds);
        return true;
    }

    private static string NormalizeUserId(string userId)
    {
        return string.IsNullOrWhiteSpace(userId) ? "guest" : userId.Trim();
    }

    private static string GetFavoritesKey(string userId) => $"EDUMY_FAVORITES_{NormalizeUserId(userId)}";
    private static string GetRecentCoursesKey(string userId) => $"EDUMY_RECENT_COURSES_{NormalizeUserId(userId)}";
    private static string GetBookmarksKey(string userId) => $"EDUMY_BOOKMARKS_{NormalizeUserId(userId)}";

    private static T LoadStore<T>(string key, T fallback) where T : class
    {
        if (!PlayerPrefs.HasKey(key))
        {
            return fallback;
        }

        string json = PlayerPrefs.GetString(key, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
        {
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            return fallback;
        }

        try
        {
            T result = JsonUtility.FromJson<T>(json);
            return result ?? fallback;
        }
        catch
        {
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            return fallback;
        }
    }

    private static void SaveStore<T>(string key, T value) where T : class
    {
        if (value == null)
        {
            return;
        }

        PlayerPrefs.SetString(key, JsonUtility.ToJson(value));
        PlayerPrefs.Save();
    }
}
