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
}

[Serializable]
public class LessonData
{
    public string id;
    public string title;
    public string duration;
    public int order;
    public bool isCompleted;
}

[Serializable]
public class ApiResponse<T>
{
    public bool success;
    public List<T> data;
}
