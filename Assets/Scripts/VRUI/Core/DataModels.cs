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
    public string slideText;
    public float slideCanvasWidth;
    public float slideCanvasHeight;
    public string duration;
    public int order;
    public int sectionOrder;
    public bool isCompleted;
    public int quizQuestionsCount;
    public int slideCount;
    public string sectionTitle;
    public string section;
    public string sectionName;
    public List<string> slides;
    public List<SlidePageData> slidePages;
    public List<QuizQuestionData> quizQuestions;
    public List<QuizQuestionData> questions;
    public List<QuizQuestionData> quizzes;
    public QuizQuestionData quizQuestion;
    public List<TimedQuizData> timedQuizzes;
    public List<TimedQuizData> interactiveQuizzes;
    public List<TimedQuizData> popupQuizzes;
    public List<TimedQuizData> videoQuizzes;
}

[Serializable]
public class SlidePageData
{
    public string id;
    public string title;
    public string layout;
    public string theme;
    public float canvasWidth;
    public float canvasHeight;
    public List<SlideElementData> elements;
}

[Serializable]
public class SlideElementData
{
    public string id;
    public string type;
    public float x;
    public float y;
    public float width;
    public float height;
    public string text;
    public string src;
    public float fontSize;
    public string color;
    public string align;
    public bool bold;
}

[Serializable]
public class QuizQuestionData
{
    public string question;
    public string text;
    public string prompt;
    public string explanation;
    public string explain;
    public string reason;
    public string solution;
    public string wrongExplanation;
    public List<string> options;
    public List<string> answers;
    public List<string> choices;
    public int correctIndex;
    public int correctAnswer;
}

[Serializable]
public class TimedQuizData
{
    public string id;

    public float triggerTimeSec;
    public float triggerTime;
    public float time;
    public string timecode;
    public string showAt;
    public string timestamp;
    public string startAt;

    public string question;
    public string text;
    public string prompt;
    public string explanation;
    public string explain;
    public string reason;
    public string solution;
    public string wrongExplanation;

    public List<string> options;
    public List<string> answers;
    public List<string> choices;

    public int correctIndex;
    public int correctAnswer;

    public bool pauseVideo;
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
