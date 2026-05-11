using System;
using UnityEngine;

public enum AppAuthState
{
    SignedOut,
    Pairing,
    SignedIn
}

public enum ActiveContentWindowType
{
    None,
    Video,
    Quiz,
    Slide
}

public enum BackendConnectionState
{
    Unknown,
    Connected,
    Unreachable,
    Unauthorized
}

public enum OnboardingFlowState
{
    Inactive,
    Running,
    Completed,
    Skipped
}

public enum OnboardingActionType
{
    None,
    MenuOpened,
    LoginCodeRequested,
    CourseSelected,
    LessonOpened,
    ContentWindowOpened,
    DockFloatToggled
}

[Serializable]
public class CourseStateSnapshot
{
    public string courseId;
    public string courseTitle;
    public int sectionIndex;
}

[Serializable]
public class LessonStateSnapshot
{
    public string lessonId;
    public string lessonTitle;
    public string lessonType;
    public int sectionIndex;
    public int lessonIndex;
}

[Serializable]
public class BackendStatusSnapshot
{
    public BackendConnectionState state;
    public string message;
    public string baseUrl;
}

[Serializable]
public class OnboardingActionSnapshot
{
    public OnboardingActionType actionType;
    public string context;
}

[DisallowMultipleComponent]
public class AppStateManager : MonoBehaviour
{
    private const string VerboseLoggingPrefsKey = "EDUMY_VERBOSE_LOGGING";
    private static AppStateManager instance;

    public static AppStateManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<AppStateManager>();
                if (instance == null)
                {
                    GameObject go = new GameObject(nameof(AppStateManager));
                    instance = go.AddComponent<AppStateManager>();
                }
            }

            return instance;
        }
    }

    public static bool IsAvailable => instance != null;
    public static bool IsVerboseLoggingEnabled => Instance.verboseLoggingEnabled;

    public event Action<AppAuthState> OnAuthStateChanged;
    public event Action<CourseStateSnapshot> OnCurrentCourseChanged;
    public event Action<LessonStateSnapshot> OnCurrentLessonChanged;
    public event Action<ActiveContentWindowType> OnActiveWindowChanged;
    public event Action<BackendStatusSnapshot> OnBackendStatusChanged;
    public event Action<bool> OnLoadingStateChanged;
    public event Action<bool> OnMenuStateChanged;
    public event Action<OnboardingFlowState> OnOnboardingStateChanged;
    public event Action<OnboardingActionSnapshot> OnOnboardingAction;

    public AppAuthState AuthState { get; private set; } = AppAuthState.SignedOut;
    public string CurrentUserId { get; private set; } = string.Empty;
    public string CurrentUsername { get; private set; } = string.Empty;
    public CourseStateSnapshot CurrentCourse { get; } = new CourseStateSnapshot();
    public LessonStateSnapshot CurrentLesson { get; } = new LessonStateSnapshot();
    public ActiveContentWindowType ActiveWindow { get; private set; } = ActiveContentWindowType.None;
    public BackendStatusSnapshot BackendStatus { get; } = new BackendStatusSnapshot { state = BackendConnectionState.Unknown };
    public OnboardingFlowState OnboardingState { get; private set; } = OnboardingFlowState.Inactive;
    public bool IsLoading { get; private set; }
    public bool IsMenuOpen { get; private set; }

    [SerializeField] private bool verboseLoggingEnabled;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        verboseLoggingEnabled = PlayerPrefs.GetInt(VerboseLoggingPrefsKey, 0) == 1;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    public void SetVerboseLogging(bool enabled)
    {
        if (verboseLoggingEnabled == enabled)
        {
            return;
        }

        verboseLoggingEnabled = enabled;
        PlayerPrefs.SetInt(VerboseLoggingPrefsKey, enabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void SetAuthState(AppAuthState authState, string userId = null, string username = null)
    {
        string normalizedUserId = string.IsNullOrWhiteSpace(userId) ? string.Empty : userId.Trim();
        string normalizedUsername = string.IsNullOrWhiteSpace(username) ? string.Empty : username.Trim();

        bool changed = AuthState != authState
            || !string.Equals(CurrentUserId, normalizedUserId, StringComparison.Ordinal)
            || !string.Equals(CurrentUsername, normalizedUsername, StringComparison.Ordinal);

        AuthState = authState;
        CurrentUserId = normalizedUserId;
        CurrentUsername = normalizedUsername;

        if (!changed)
        {
            return;
        }

        if (authState == AppAuthState.SignedOut)
        {
            ClearCurrentCourse();
            ClearCurrentLesson();
            SetActiveWindow(ActiveContentWindowType.None);
        }

        OnAuthStateChanged?.Invoke(AuthState);
    }

    public void SetCurrentCourse(string courseId, string courseTitle, int sectionIndex = -1)
    {
        string normalizedCourseId = string.IsNullOrWhiteSpace(courseId) ? string.Empty : courseId.Trim();
        string normalizedCourseTitle = string.IsNullOrWhiteSpace(courseTitle) ? string.Empty : courseTitle.Trim();
        int normalizedSectionIndex = Mathf.Max(-1, sectionIndex);

        bool changed = !string.Equals(CurrentCourse.courseId, normalizedCourseId, StringComparison.Ordinal)
            || !string.Equals(CurrentCourse.courseTitle, normalizedCourseTitle, StringComparison.Ordinal)
            || CurrentCourse.sectionIndex != normalizedSectionIndex;

        CurrentCourse.courseId = normalizedCourseId;
        CurrentCourse.courseTitle = normalizedCourseTitle;
        CurrentCourse.sectionIndex = normalizedSectionIndex;

        if (changed)
        {
            OnCurrentCourseChanged?.Invoke(CurrentCourse);
        }
    }

    public void ClearCurrentCourse()
    {
        SetCurrentCourse(string.Empty, string.Empty, -1);
    }

    public void SetCurrentLesson(string lessonId, string lessonTitle, string lessonType, int sectionIndex, int lessonIndex)
    {
        string normalizedLessonId = string.IsNullOrWhiteSpace(lessonId) ? string.Empty : lessonId.Trim();
        string normalizedLessonTitle = string.IsNullOrWhiteSpace(lessonTitle) ? string.Empty : lessonTitle.Trim();
        string normalizedLessonType = string.IsNullOrWhiteSpace(lessonType) ? string.Empty : lessonType.Trim().ToLowerInvariant();
        int normalizedSectionIndex = Mathf.Max(-1, sectionIndex);
        int normalizedLessonIndex = Mathf.Max(-1, lessonIndex);

        bool changed = !string.Equals(CurrentLesson.lessonId, normalizedLessonId, StringComparison.Ordinal)
            || !string.Equals(CurrentLesson.lessonTitle, normalizedLessonTitle, StringComparison.Ordinal)
            || !string.Equals(CurrentLesson.lessonType, normalizedLessonType, StringComparison.Ordinal)
            || CurrentLesson.sectionIndex != normalizedSectionIndex
            || CurrentLesson.lessonIndex != normalizedLessonIndex;

        CurrentLesson.lessonId = normalizedLessonId;
        CurrentLesson.lessonTitle = normalizedLessonTitle;
        CurrentLesson.lessonType = normalizedLessonType;
        CurrentLesson.sectionIndex = normalizedSectionIndex;
        CurrentLesson.lessonIndex = normalizedLessonIndex;

        if (changed)
        {
            OnCurrentLessonChanged?.Invoke(CurrentLesson);
        }
    }

    public void ClearCurrentLesson()
    {
        SetCurrentLesson(string.Empty, string.Empty, string.Empty, -1, -1);
    }

    public void SetActiveWindow(ActiveContentWindowType windowType)
    {
        if (ActiveWindow == windowType)
        {
            return;
        }

        ActiveWindow = windowType;
        OnActiveWindowChanged?.Invoke(ActiveWindow);
    }

    public void SetBackendStatus(BackendConnectionState state, string message = null, string baseUrl = null)
    {
        string normalizedMessage = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();
        string normalizedBaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? string.Empty : baseUrl.Trim();

        bool changed = BackendStatus.state != state
            || !string.Equals(BackendStatus.message, normalizedMessage, StringComparison.Ordinal)
            || !string.Equals(BackendStatus.baseUrl, normalizedBaseUrl, StringComparison.Ordinal);

        BackendStatus.state = state;
        BackendStatus.message = normalizedMessage;
        BackendStatus.baseUrl = normalizedBaseUrl;

        if (changed)
        {
            OnBackendStatusChanged?.Invoke(BackendStatus);
        }
    }

    public void SetLoadingState(bool isLoading)
    {
        if (IsLoading == isLoading)
        {
            return;
        }

        IsLoading = isLoading;
        OnLoadingStateChanged?.Invoke(IsLoading);
    }

    public void SetMenuOpen(bool isOpen)
    {
        if (IsMenuOpen == isOpen)
        {
            return;
        }

        IsMenuOpen = isOpen;
        OnMenuStateChanged?.Invoke(IsMenuOpen);
    }

    public void SetOnboardingState(OnboardingFlowState state)
    {
        if (OnboardingState == state)
        {
            return;
        }

        OnboardingState = state;
        OnOnboardingStateChanged?.Invoke(OnboardingState);
    }

    public void NotifyOnboardingAction(OnboardingActionType actionType, string context = null)
    {
        if (actionType == OnboardingActionType.None)
        {
            return;
        }

        OnOnboardingAction?.Invoke(new OnboardingActionSnapshot
        {
            actionType = actionType,
            context = string.IsNullOrWhiteSpace(context) ? string.Empty : context.Trim()
        });
    }
}
