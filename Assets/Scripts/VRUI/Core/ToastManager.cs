using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum ToastMessageType
{
    Success,
    Error,
    Info,
    Warning
}

[DisallowMultipleComponent]
public class ToastManager : MonoBehaviour
{
    private const float ToastDurationDefault = 3f;
    private const float ToastFadeDuration = 0.2f;
    private const int MaxVisibleToasts = 3;
    private const int MaxQueuedToasts = 8;
    private const float DuplicateSuppressionSeconds = 1.75f;

    [Header("VR Loading Placement")]
    [SerializeField] private Vector3 loadingPanelOffset = new Vector3(0f, 0f, 0.04f);
    [SerializeField, Min(0.5f)] private float fallbackDistance = 1.25f;
    [SerializeField] private Vector3 fallbackLoadingPanelOffset = Vector3.zero;
    [SerializeField, Min(0.01f)] private float fallbackMoveThreshold = 0.22f;
    [SerializeField, Range(1f, 90f)] private float fallbackAngleThreshold = 20f;
    [SerializeField, Min(0.1f)] private float poseSmoothSpeed = 5f;

    private static ToastManager instance;

    private readonly List<RectTransform> activeToastItems = new List<RectTransform>();
    private readonly Queue<ToastRequest> pendingQueue = new Queue<ToastRequest>();
    private readonly List<ToastRequest> deferredUntilLoadingClears = new List<ToastRequest>();
    private readonly Dictionary<string, float> recentToastTimes = new Dictionary<string, float>();

    private Canvas canvas;
    private RectTransform rootRect;
    private RectTransform toastStackRect;
    private RectTransform loadingOverlayRect;
    private TMP_Text loadingMessageText;
    private TMP_Text loadingSpinnerText;
    private Image loadingOverlayImage;
    private int loadingRequestCount;
    private bool blockLoadingInput = true;
    private bool loadingVisible;
    private bool hasFallbackPose;
    private Vector3 currentCanvasPosition;
    private Quaternion currentCanvasRotation = Quaternion.identity;
    private Vector3 fallbackAnchorPosition;
    private Quaternion fallbackAnchorRotation = Quaternion.identity;

    private readonly struct ToastRequest
    {
        public readonly string message;
        public readonly ToastMessageType type;
        public readonly float duration;

        public ToastRequest(string message, ToastMessageType type, float duration)
        {
            this.message = message;
            this.type = type;
            this.duration = duration;
        }
    }

    public static ToastManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<ToastManager>();
                if (instance == null)
                {
                    GameObject go = new GameObject(nameof(ToastManager));
                    instance = go.AddComponent<ToastManager>();
                }
            }

            return instance;
        }
    }

    public static bool IsLoadingVisible => instance != null && instance.loadingVisible;

    public static void ShowToast(string message, ToastMessageType type = ToastMessageType.Info, float duration = ToastDurationDefault)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Instance.EnqueueToast(new ToastRequest(message.Trim(), type, Mathf.Max(1.25f, duration)), deferWhileLoading: true);
    }

    public static void ShowSuccess(string message, float duration = ToastDurationDefault)
    {
        ShowToast(message, ToastMessageType.Success, duration);
    }

    public static void ShowError(string message, float duration = 4f)
    {
        ShowToast(message, ToastMessageType.Error, duration);
    }

    public static void ShowInfo(string message, float duration = ToastDurationDefault)
    {
        ShowToast(message, ToastMessageType.Info, duration);
    }

    public static void ShowWarning(string message, float duration = 3.5f)
    {
        ShowToast(message, ToastMessageType.Warning, duration);
    }

    public static void ShowErrorAfterLoading(string message, float duration = 4f)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Instance.EnqueueToast(new ToastRequest(message.Trim(), ToastMessageType.Error, Mathf.Max(1.5f, duration)), deferWhileLoading: true);
    }

    public static void ShowLoading(string message = "Loading...", bool blockInput = true)
    {
        Instance.ShowLoadingInternal(message, blockInput);
    }

    public static void HideLoading()
    {
        if (instance == null)
        {
            return;
        }

        instance.HideLoadingInternal();
    }

    public static void HideAllLoading()
    {
        if (instance == null)
        {
            return;
        }

        instance.HideAllLoadingInternal();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureBootstrap()
    {
        _ = Instance;
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
        EnsureUi();
    }

    private void LateUpdate()
    {
        EnsureUi();
        UpdateCanvasPose();
        UpdateLoadingSpinner();
        TryShowNextPendingToast();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void EnsureUi()
    {
        if (canvas != null && rootRect != null)
        {
            return;
        }

        canvas = GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
        }

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = short.MaxValue;

        if (GetComponent<GraphicRaycaster>() == null)
        {
            gameObject.AddComponent<GraphicRaycaster>();
        }

        CanvasScaler scaler = GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = gameObject.AddComponent<CanvasScaler>();
        }
        scaler.dynamicPixelsPerUnit = 10f;

        rootRect = canvas.GetComponent<RectTransform>();
        if (rootRect == null)
        {
            rootRect = gameObject.AddComponent<RectTransform>();
        }

        rootRect.sizeDelta = new Vector2(1280f, 720f);
        transform.localScale = new Vector3(0.0011f, 0.0011f, 0.0011f);

        XRRuntimeUiHelper.EnsureWorldSpaceCanvasInteraction(gameObject);
        XRRuntimeUiHelper.EnsureEventSystemSupportsXR();

        EnsureToastStack();
        EnsureLoadingOverlay();
    }

    private void EnsureToastStack()
    {
        if (toastStackRect != null)
        {
            return;
        }

        GameObject stackGo = new GameObject("ToastStack", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        stackGo.transform.SetParent(rootRect, false);
        toastStackRect = stackGo.GetComponent<RectTransform>();
        toastStackRect.anchorMin = new Vector2(0.5f, 0f);
        toastStackRect.anchorMax = new Vector2(0.5f, 0f);
        toastStackRect.pivot = new Vector2(0.5f, 0f);
        toastStackRect.anchoredPosition = new Vector2(0f, 48f);
        toastStackRect.sizeDelta = new Vector2(520f, 0f);

        VerticalLayoutGroup layout = stackGo.GetComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.LowerCenter;
        layout.childControlHeight = false;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        layout.spacing = 12f;

        ContentSizeFitter fitter = stackGo.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
    }

    private void EnsureLoadingOverlay()
    {
        if (loadingOverlayRect != null)
        {
            return;
        }

        GameObject overlayGo = new GameObject("LoadingOverlay", typeof(RectTransform), typeof(Image));
        overlayGo.transform.SetParent(rootRect, false);
        loadingOverlayRect = overlayGo.GetComponent<RectTransform>();
        loadingOverlayRect.anchorMin = Vector2.zero;
        loadingOverlayRect.anchorMax = Vector2.one;
        loadingOverlayRect.offsetMin = Vector2.zero;
        loadingOverlayRect.offsetMax = Vector2.zero;

        loadingOverlayImage = overlayGo.GetComponent<Image>();
        loadingOverlayImage.color = new Color(0.03f, 0.06f, 0.12f, 0.68f);
        loadingOverlayImage.raycastTarget = true;
        overlayGo.SetActive(false);

        GameObject panelGo = new GameObject("LoadingPanel", typeof(RectTransform), typeof(Image));
        panelGo.transform.SetParent(loadingOverlayRect, false);
        RectTransform panelRect = panelGo.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(440f, 190f);
        panelRect.anchoredPosition = Vector2.zero;

        Image panelImage = panelGo.GetComponent<Image>();
        panelImage.color = new Color(0.1f, 0.16f, 0.25f, 0.96f);
        panelImage.raycastTarget = false;

        loadingSpinnerText = CreateText("Spinner", panelRect, "o", 74, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);
        RectTransform spinnerRect = loadingSpinnerText.rectTransform;
        spinnerRect.anchorMin = new Vector2(0.5f, 0.58f);
        spinnerRect.anchorMax = new Vector2(0.5f, 0.58f);
        spinnerRect.pivot = new Vector2(0.5f, 0.5f);
        spinnerRect.sizeDelta = new Vector2(90f, 90f);
        spinnerRect.anchoredPosition = Vector2.zero;

        loadingMessageText = CreateText("Message", panelRect, "Loading...", 30, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);
        RectTransform messageRect = loadingMessageText.rectTransform;
        messageRect.anchorMin = new Vector2(0.1f, 0.18f);
        messageRect.anchorMax = new Vector2(0.9f, 0.42f);
        messageRect.offsetMin = Vector2.zero;
        messageRect.offsetMax = Vector2.zero;
    }

    private void EnqueueToast(ToastRequest request, bool deferWhileLoading)
    {
        EnsureUi();
        CleanupRecentToastMap();

        if (ShouldSuppressDuplicate(request))
        {
            return;
        }

        if (HasQueuedDuplicate(request))
        {
            return;
        }

        if (deferWhileLoading && loadingVisible)
        {
            if (deferredUntilLoadingClears.Count >= MaxQueuedToasts)
            {
                deferredUntilLoadingClears.RemoveAt(0);
            }
            deferredUntilLoadingClears.Add(request);
            return;
        }

        if (activeToastItems.Count < MaxVisibleToasts)
        {
            ShowToastNow(request);
            return;
        }

        if (pendingQueue.Count >= MaxQueuedToasts)
        {
            pendingQueue.Dequeue();
        }

        pendingQueue.Enqueue(request);
    }

    private void ShowToastNow(ToastRequest request)
    {
        MarkToastShown(request);
        RectTransform toastRect = BuildToastItem(request);
        activeToastItems.Add(toastRect);
        StartCoroutine(ToastLifecycleCoroutine(toastRect, request.duration));
    }

    private void TryShowNextPendingToast()
    {
        if (loadingVisible || activeToastItems.Count >= MaxVisibleToasts || pendingQueue.Count == 0)
        {
            return;
        }

        ToastRequest request = pendingQueue.Dequeue();
        ShowToastNow(request);
    }

    private void FlushDeferredToasts()
    {
        if (deferredUntilLoadingClears.Count == 0)
        {
            return;
        }

        deferredUntilLoadingClears.Sort((a, b) => GetPriority(b.type).CompareTo(GetPriority(a.type)));
        for (int i = 0; i < deferredUntilLoadingClears.Count; i++)
        {
            EnqueueToast(deferredUntilLoadingClears[i], deferWhileLoading: false);
        }
        deferredUntilLoadingClears.Clear();
    }

    private void ShowLoadingInternal(string message, bool blockInput)
    {
        EnsureUi();

        if (loadingRequestCount > 32)
        {
            HideAllLoadingInternal();
        }

        loadingRequestCount = Mathf.Max(0, loadingRequestCount) + 1;
        blockLoadingInput = loadingRequestCount == 1 ? blockInput : (blockLoadingInput || blockInput);

        if (loadingMessageText != null)
        {
            loadingMessageText.text = string.IsNullOrWhiteSpace(message) ? "Loading..." : message.Trim();
        }

        if (loadingOverlayImage != null)
        {
            loadingOverlayImage.raycastTarget = blockLoadingInput;
        }

        if (loadingOverlayRect != null)
        {
            loadingOverlayRect.gameObject.SetActive(true);
        }

        loadingVisible = true;
        AppStateManager.Instance.SetLoadingState(true);
    }

    private void HideLoadingInternal()
    {
        loadingRequestCount = Mathf.Max(0, loadingRequestCount - 1);
        if (loadingRequestCount > 0)
        {
            return;
        }

        FinishLoadingState();
    }

    private void HideAllLoadingInternal()
    {
        loadingRequestCount = 0;
        FinishLoadingState();
    }

    private void FinishLoadingState()
    {
        blockLoadingInput = true;
        loadingVisible = false;

        if (loadingOverlayRect != null)
        {
            loadingOverlayRect.gameObject.SetActive(false);
        }

        AppStateManager.Instance.SetLoadingState(false);
        FlushDeferredToasts();
    }

    private RectTransform BuildToastItem(ToastRequest request)
    {
        GameObject toastGo = new GameObject($"Toast_{request.type}", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(LayoutElement));
        toastGo.transform.SetParent(toastStackRect, false);

        RectTransform rect = toastGo.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(520f, 96f);

        LayoutElement layout = toastGo.GetComponent<LayoutElement>();
        layout.preferredHeight = 96f;
        layout.minHeight = 88f;

        Image background = toastGo.GetComponent<Image>();
        background.color = GetToastBackgroundColor(request.type);
        background.raycastTarget = false;

        CanvasGroup group = toastGo.GetComponent<CanvasGroup>();
        group.alpha = 0f;

        GameObject accentGo = new GameObject("Accent", typeof(RectTransform), typeof(Image));
        accentGo.transform.SetParent(rect, false);
        RectTransform accentRect = accentGo.GetComponent<RectTransform>();
        accentRect.anchorMin = new Vector2(0f, 0f);
        accentRect.anchorMax = new Vector2(0f, 1f);
        accentRect.pivot = new Vector2(0f, 0.5f);
        accentRect.sizeDelta = new Vector2(18f, 0f);
        accentRect.anchoredPosition = Vector2.zero;
        accentGo.GetComponent<Image>().color = GetToastAccentColor(request.type);

        TMP_Text iconText = CreateText("Icon", rect, GetToastIcon(request.type), 34, FontStyles.Bold, TextAlignmentOptions.Center, Color.white);
        RectTransform iconRect = iconText.rectTransform;
        iconRect.anchorMin = new Vector2(0f, 0f);
        iconRect.anchorMax = new Vector2(0f, 1f);
        iconRect.pivot = new Vector2(0f, 0.5f);
        iconRect.sizeDelta = new Vector2(96f, 0f);
        iconRect.anchoredPosition = new Vector2(26f, 0f);

        TMP_Text messageText = CreateText("Message", rect, request.message, 22, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, Color.white);
        RectTransform messageRect = messageText.rectTransform;
        messageRect.anchorMin = new Vector2(0f, 0f);
        messageRect.anchorMax = new Vector2(1f, 1f);
        messageRect.offsetMin = new Vector2(102f, 12f);
        messageRect.offsetMax = new Vector2(-20f, -12f);
        messageText.textWrappingMode = TextWrappingModes.Normal;

        return rect;
    }

    private IEnumerator ToastLifecycleCoroutine(RectTransform toastRect, float duration)
    {
        if (toastRect == null)
        {
            yield break;
        }

        CanvasGroup group = toastRect.GetComponent<CanvasGroup>();
        float elapsed = 0f;
        while (elapsed < ToastFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            if (group != null)
            {
                group.alpha = Mathf.Clamp01(elapsed / ToastFadeDuration);
            }
            yield return null;
        }

        if (group != null)
        {
            group.alpha = 1f;
        }

        yield return new WaitForSecondsRealtime(duration);

        elapsed = 0f;
        while (elapsed < ToastFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            if (group != null)
            {
                group.alpha = 1f - Mathf.Clamp01(elapsed / ToastFadeDuration);
            }
            yield return null;
        }

        activeToastItems.Remove(toastRect);
        if (toastRect != null)
        {
            Destroy(toastRect.gameObject);
        }
    }

    private void UpdateCanvasPose()
    {
        Transform anchor = ResolvePanelAnchor();
        Vector3 targetPosition;
        Quaternion targetRotation;

        if (anchor != null)
        {
            targetPosition = anchor.TransformPoint(loadingPanelOffset);
            targetRotation = anchor.rotation;
            hasFallbackPose = false;
        }
        else
        {
            if (!TryBuildFallbackPose(out targetPosition, out targetRotation))
            {
                return;
            }
        }

        if (currentCanvasRotation == Quaternion.identity)
        {
            currentCanvasPosition = targetPosition;
            currentCanvasRotation = targetRotation;
        }
        else
        {
            float blend = 1f - Mathf.Exp(-poseSmoothSpeed * Time.unscaledDeltaTime);
            currentCanvasPosition = Vector3.Lerp(currentCanvasPosition, targetPosition, blend);
            currentCanvasRotation = Quaternion.Slerp(currentCanvasRotation, targetRotation, blend);
        }

        transform.position = currentCanvasPosition;
        transform.rotation = currentCanvasRotation;
    }

    private bool TryBuildFallbackPose(out Vector3 targetPosition, out Quaternion targetRotation)
    {
        targetPosition = Vector3.zero;
        targetRotation = Quaternion.identity;

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            return false;
        }

        Transform viewer = mainCamera.transform;
        Vector3 forward = viewer.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
        {
            forward = viewer.forward;
        }
        forward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, forward);
        if (right.sqrMagnitude < 0.001f)
        {
            right = viewer.right;
        }
        else
        {
            right.Normalize();
        }

        float effectiveDistance = Mathf.Max(0.5f, fallbackDistance + fallbackLoadingPanelOffset.z);
        Vector3 desiredPosition = viewer.position
            + (forward * effectiveDistance)
            + (right * fallbackLoadingPanelOffset.x)
            + (Vector3.up * fallbackLoadingPanelOffset.y);
        Quaternion desiredRotation = Quaternion.LookRotation(viewer.position - desiredPosition, Vector3.up) * Quaternion.Euler(0f, 180f, 0f);

        if (!hasFallbackPose)
        {
            fallbackAnchorPosition = desiredPosition;
            fallbackAnchorRotation = desiredRotation;
            hasFallbackPose = true;
        }
        else
        {
            float angleDelta = Quaternion.Angle(fallbackAnchorRotation, desiredRotation);
            float positionDelta = Vector3.Distance(fallbackAnchorPosition, desiredPosition);
            if (angleDelta >= fallbackAngleThreshold || positionDelta >= fallbackMoveThreshold)
            {
                fallbackAnchorPosition = desiredPosition;
                fallbackAnchorRotation = desiredRotation;
            }
        }

        targetPosition = fallbackAnchorPosition;
        targetRotation = fallbackAnchorRotation;
        return true;
    }

    private Transform ResolvePanelAnchor()
    {
        if (!AppStateManager.IsAvailable || !AppStateManager.Instance.IsMenuOpen)
        {
            return null;
        }

        CourseSelectionUI courseSelectionUI = FindAnyObjectByType<CourseSelectionUI>();
        return courseSelectionUI != null ? courseSelectionUI.transform : null;
    }

    private void UpdateLoadingSpinner()
    {
        if (!loadingVisible || loadingSpinnerText == null)
        {
            return;
        }

        loadingSpinnerText.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -Time.unscaledTime * 180f);
    }

    private bool ShouldSuppressDuplicate(ToastRequest request)
    {
        string key = BuildToastKey(request);
        return recentToastTimes.TryGetValue(key, out float lastShownTime)
            && Time.unscaledTime - lastShownTime < DuplicateSuppressionSeconds;
    }

    private bool HasQueuedDuplicate(ToastRequest request)
    {
        string key = BuildToastKey(request);
        for (int i = 0; i < deferredUntilLoadingClears.Count; i++)
        {
            if (BuildToastKey(deferredUntilLoadingClears[i]) == key)
            {
                return true;
            }
        }

        foreach (ToastRequest queued in pendingQueue)
        {
            if (BuildToastKey(queued) == key)
            {
                return true;
            }
        }

        return false;
    }

    private void MarkToastShown(ToastRequest request)
    {
        recentToastTimes[BuildToastKey(request)] = Time.unscaledTime;
    }

    private void CleanupRecentToastMap()
    {
        List<string> expiredKeys = null;
        foreach (KeyValuePair<string, float> entry in recentToastTimes)
        {
            if (Time.unscaledTime - entry.Value < DuplicateSuppressionSeconds)
            {
                continue;
            }

            expiredKeys ??= new List<string>();
            expiredKeys.Add(entry.Key);
        }

        if (expiredKeys == null)
        {
            return;
        }

        for (int i = 0; i < expiredKeys.Count; i++)
        {
            recentToastTimes.Remove(expiredKeys[i]);
        }
    }

    private static string BuildToastKey(ToastRequest request)
    {
        return $"{request.type}:{request.message}";
    }

    private static int GetPriority(ToastMessageType type)
    {
        return type switch
        {
            ToastMessageType.Error => 4,
            ToastMessageType.Warning => 3,
            ToastMessageType.Success => 2,
            _ => 1
        };
    }

    private static TMP_Text CreateText(string name, RectTransform parent, string textValue, float fontSize, FontStyles style, TextAlignmentOptions alignment, Color color)
    {
        GameObject textGo = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(parent, false);

        TMP_Text text = textGo.GetComponent<TMP_Text>();
        text.text = textValue;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        return text;
    }

    private static Color GetToastBackgroundColor(ToastMessageType type)
    {
        return type switch
        {
            ToastMessageType.Success => new Color(0.08f, 0.39f, 0.22f, 0.96f),
            ToastMessageType.Error => new Color(0.48f, 0.12f, 0.12f, 0.97f),
            ToastMessageType.Warning => new Color(0.45f, 0.31f, 0.07f, 0.97f),
            _ => new Color(0.12f, 0.29f, 0.52f, 0.96f)
        };
    }

    private static Color GetToastAccentColor(ToastMessageType type)
    {
        return type switch
        {
            ToastMessageType.Success => new Color(0.24f, 0.85f, 0.49f, 1f),
            ToastMessageType.Error => new Color(1f, 0.42f, 0.42f, 1f),
            ToastMessageType.Warning => new Color(1f, 0.79f, 0.34f, 1f),
            _ => new Color(0.48f, 0.77f, 1f, 1f)
        };
    }

    private static string GetToastIcon(ToastMessageType type)
    {
        return type switch
        {
            ToastMessageType.Success => "OK",
            ToastMessageType.Error => "!",
            ToastMessageType.Warning => "!",
            _ => "i"
        };
    }
}
