using System;
using System.Collections.Generic;

[Serializable]
public class CourseData
{
    public string id;
    public string title;
    public string description;
    public string thumbnailUrl;
    public int progress;
    public int totalLessons;
    public int completedLessons;
    public bool isEnrolled;
}

[Serializable]
public class LessonData
{
    public string id;
    public string title;
    public string type;
    public string videoUrl;
    public string duration;
    public int order;
    public int sectionOrder;
    public bool isCompleted;
    public string sectionTitle;
    public string section;
    public string sectionName;
}

[Serializable]
public class SectionData
{
    public string id;
    public string sectionId;
    public string sectionTitle;
    public string sectionName;
    public string section;
    public string title;
    public string name;
    public string code;
    public int order;
    public List<LessonData> lessons;
    public List<LessonData> videos;
    public List<LessonData> items;
}

[Serializable]
public class ApiResponse<T>
{
    public bool success;
    public List<T> data;
}

[Serializable]
public class ApiError
{
    public string code;
    public string message;
    public string details;
}

[Serializable]
public class StreamResolveRequest
{
    public string sourceUrl;
    public string preferredFormat;
    public string courseId;
    public string lessonId;
}

[Serializable]
public class StreamResolveData
{
    public string resolvedUrl;
    public string format;
    public string provider;
    public string expiresAt;
}

[Serializable]
public class StreamResolveResponse
{
    public bool success;
    public StreamResolveData data;
    public ApiError error;
}
