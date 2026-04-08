using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.Video;

[DisallowMultipleComponent]
public class VideoQuizScheduler : MonoBehaviour
{
    [Header("Source")]
    [SerializeField] private VideoPlayer videoPlayer;
    [SerializeField] private TimedQuizPopupWindow timedQuizPopupWindow;

    [Header("Popup Placement")]
    [SerializeField, Min(0.5f)] private float popupDistance = 1.45f;
    [SerializeField] private float popupHeightOffset = 0.0f;

    [Header("Tracking")]
    [SerializeField, Min(0.05f)] private float drivePollingInterval = 0.15f;
    [SerializeField] private bool pauseVideoWhenQuizShown = false;
    [SerializeField] private bool useYouTubeApiTracking = true;

    private readonly List<ScheduledQuiz> schedule = new List<ScheduledQuiz>();

    private Transform viewerTransform;
    private bool videoPausedByQuiz;
    private bool running;

    private Func<double> youtubeTimeGetter;
    private bool ytTrackingAvailable;
    private float nextPollAt;

    private void Awake()
    {
        if (videoPlayer == null)
        {
            videoPlayer = GetComponent<VideoPlayer>();
        }

        if (timedQuizPopupWindow == null)
        {
            timedQuizPopupWindow = FindAnyObjectByType<TimedQuizPopupWindow>();
        }

        if (timedQuizPopupWindow != null)
        {
            timedQuizPopupWindow.OnWindowClosed -= HandlePopupClosed;
            timedQuizPopupWindow.OnWindowClosed += HandlePopupClosed;
        }
    }

    private void OnDestroy()
    {
        if (timedQuizPopupWindow != null)
        {
            timedQuizPopupWindow.OnWindowClosed -= HandlePopupClosed;
        }
    }

    public void BindVideoPlayer(VideoPlayer player)
    {
        videoPlayer = player;
    }

    public void BindTimedQuizPopup(TimedQuizPopupWindow popup)
    {
        if (timedQuizPopupWindow != null)
        {
            timedQuizPopupWindow.OnWindowClosed -= HandlePopupClosed;
        }

        timedQuizPopupWindow = popup;

        if (timedQuizPopupWindow != null)
        {
            timedQuizPopupWindow.OnWindowClosed -= HandlePopupClosed;
            timedQuizPopupWindow.OnWindowClosed += HandlePopupClosed;
        }
    }

    public void SetPopupPlacement(float distance, float heightOffset)
    {
        popupDistance = Mathf.Max(0.5f, distance);
        popupHeightOffset = heightOffset;
    }

    public void SetPauseVideoWhenQuizShown(bool shouldPause)
    {
        pauseVideoWhenQuizShown = shouldPause;
    }

    public void StopAndClear()
    {
        running = false;
        schedule.Clear();
        youtubeTimeGetter = null;
        ytTrackingAvailable = false;

        if (timedQuizPopupWindow != null)
        {
            timedQuizPopupWindow.HideWindow(resumeVideo: false);
        }
    }

    public void StartTracking(
        LessonData lesson,
        string sourceUrl,
        Transform viewer,
        Func<double> ytTimeProvider = null)
    {
        schedule.Clear();
        viewerTransform = viewer;
        youtubeTimeGetter = null;
        ytTrackingAvailable = false;
        running = false;

        if (lesson == null || timedQuizPopupWindow == null)
        {
            return;
        }

        List<TimedQuizData> quizzes = ExtractTimedQuizzes(lesson);
        if (quizzes.Count == 0)
        {
            return;
        }

        for (int i = 0; i < quizzes.Count; i++)
        {
            TimedQuizData q = quizzes[i];
            if (q == null) continue;

            float triggerSec = ResolveTriggerSeconds(q);
            if (triggerSec < 0f) continue;

            schedule.Add(new ScheduledQuiz
            {
                triggerTimeSec = triggerSec,
                quiz = q,
                fired = false
            });
        }

        if (schedule.Count == 0) return;

        schedule.Sort((a, b) => a.triggerTimeSec.CompareTo(b.triggerTimeSec));

        bool isYouTube = IsYouTubeUrl(sourceUrl);
        if (isYouTube && useYouTubeApiTracking)
        {
            youtubeTimeGetter = ytTimeProvider;
            ytTrackingAvailable = youtubeTimeGetter != null;
            if (!ytTrackingAvailable)
            {
                Debug.LogWarning("[VideoQuizScheduler] YouTube lesson detected but no YT API time provider was supplied. Falling back to VideoPlayer time.");
            }
        }

        nextPollAt = 0f;
        running = true;
    }

    private void Update()
    {
        if (!Application.isPlaying) return;
        if (!running || schedule.Count == 0) return;

        if (Time.unscaledTime < nextPollAt) return;
        nextPollAt = Time.unscaledTime + drivePollingInterval;

        double currentTime = GetCurrentTimeSeconds();
        if (currentTime < 0d) return;

        for (int i = 0; i < schedule.Count; i++)
        {
            ScheduledQuiz item = schedule[i];
            if (item.fired) continue;

            if (currentTime + 0.03d >= item.triggerTimeSec)
            {
                schedule[i] = ShowScheduledQuiz(item);
                break;
            }
            else
            {
                break;
            }
        }
    }

    private ScheduledQuiz ShowScheduledQuiz(ScheduledQuiz item)
    {
        item.fired = true;

        if (pauseVideoWhenQuizShown)
        {
            PauseVideoForQuiz();
        }

        if (timedQuizPopupWindow != null)
        {
            timedQuizPopupWindow.Show(item.quiz, viewerTransform, popupDistance, popupHeightOffset);
        }

        return item;
    }

    private void PauseVideoForQuiz()
    {
        if (videoPlayer == null) return;

        if (videoPlayer.isPlaying)
        {
            videoPlayer.Pause();
            videoPausedByQuiz = true;
        }
    }

    private void HandlePopupClosed(bool resumeVideo)
    {
        if (!resumeVideo || !videoPausedByQuiz) return;
        if (videoPlayer == null) return;

        videoPausedByQuiz = false;
        if (!videoPlayer.isPlaying)
        {
            videoPlayer.Play();
        }
    }

    private double GetCurrentTimeSeconds()
    {
        if (ytTrackingAvailable && youtubeTimeGetter != null)
        {
            double ytTime = youtubeTimeGetter();
            if (ytTime >= 0d) return ytTime;
        }

        if (videoPlayer == null) return -1d;
        return videoPlayer.time;
    }

    private static List<TimedQuizData> ExtractTimedQuizzes(LessonData lesson)
    {
        List<TimedQuizData> list = new List<TimedQuizData>();
        AddRange(list, lesson.interactiveQuizzes);
        AddRange(list, lesson.timedQuizzes);
        AddRange(list, lesson.popupQuizzes);
        AddRange(list, lesson.videoQuizzes);

        return list;
    }

    private static float ResolveTriggerSeconds(TimedQuizData quiz)
    {
        if (quiz == null) return -1f;

        if (quiz.triggerTimeSec > 0f) return quiz.triggerTimeSec;
        if (quiz.triggerTime > 0f) return quiz.triggerTime;
        if (quiz.time > 0f) return quiz.time;

        if (TryParseTimecode(quiz.timecode, out float byTimecode)) return byTimecode;
        if (TryParseTimecode(quiz.showAt, out float byShowAt)) return byShowAt;
        if (TryParseTimecode(quiz.timestamp, out float byTimestamp)) return byTimestamp;
        if (TryParseTimecode(quiz.startAt, out float byStartAt)) return byStartAt;

        return -1f;
    }

    private static bool TryParseTimecode(string value, out float seconds)
    {
        seconds = 0f;
        if (string.IsNullOrWhiteSpace(value)) return false;

        string trimmed = value.Trim();

        if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out float simple))
        {
            seconds = Mathf.Max(0f, simple);
            return true;
        }

        string[] parts = trimmed.Split(':');
        if (parts.Length == 2)
        {
            if (int.TryParse(parts[0], out int mm)
                && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float ss))
            {
                seconds = Mathf.Max(0f, mm * 60f + ss);
                return true;
            }

            return false;
        }

        if (parts.Length == 3)
        {
            if (int.TryParse(parts[0], out int hh)
                && int.TryParse(parts[1], out int mm)
                && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float ss))
            {
                seconds = Mathf.Max(0f, (hh * 3600f) + (mm * 60f) + ss);
                return true;
            }

            return false;
        }

        return false;
    }

    private static void AddRange<T>(List<T> destination, List<T> source)
    {
        if (destination == null || source == null) return;
        destination.AddRange(source.Where(x => x != null));
    }

    private static bool IsYouTubeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        return url.IndexOf("youtube.com/watch", StringComparison.OrdinalIgnoreCase) >= 0
            || url.IndexOf("youtube.com/shorts/", StringComparison.OrdinalIgnoreCase) >= 0
            || url.IndexOf("youtu.be/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    [Serializable]
    private struct ScheduledQuiz
    {
        public float triggerTimeSec;
        public TimedQuizData quiz;
        public bool fired;
    }
}
