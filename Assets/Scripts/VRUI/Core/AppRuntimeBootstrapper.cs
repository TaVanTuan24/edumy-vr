using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class AppRuntimeBootstrapper : MonoBehaviour
{
    private static AppRuntimeBootstrapper instance;
    private Coroutine bindRoutine;
    [SerializeField] private bool enableOnboarding = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureBootstrap()
    {
        if (instance != null)
        {
            return;
        }

        GameObject go = new GameObject(nameof(AppRuntimeBootstrapper));
        instance = go.AddComponent<AppRuntimeBootstrapper>();
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
        _ = AppStateManager.Instance;
        _ = ToastManager.Instance;
        _ = ResumeLearningManager.Instance;
        if (enableOnboarding)
        {
            _ = OnboardingManager.Instance;
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        StartBindRoutine();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        if (bindRoutine != null)
        {
            StopCoroutine(bindRoutine);
            bindRoutine = null;
        }
    }

    private void HandleSceneLoaded(Scene _, LoadSceneMode __)
    {
        StartBindRoutine();
    }

    private void StartBindRoutine()
    {
        if (bindRoutine != null)
        {
            StopCoroutine(bindRoutine);
        }

        bindRoutine = StartCoroutine(BindUiRoutine());
    }

    private IEnumerator BindUiRoutine()
    {
        for (int i = 0; i < 60; i++)
        {
            yield return null;

            CourseSelectionUI selectionUI = FindAnyObjectByType<CourseSelectionUI>();
            if (selectionUI == null)
            {
                continue;
            }

            CourseToggleController toggleController = selectionUI.GetComponent<CourseToggleController>();
            if (toggleController == null)
            {
                toggleController = FindAnyObjectByType<CourseToggleController>();
            }

            if (enableOnboarding)
            {
                OnboardingManager.Instance.TryStart(selectionUI, toggleController);
            }
            bindRoutine = null;
            yield break;
        }

        bindRoutine = null;
    }
}
