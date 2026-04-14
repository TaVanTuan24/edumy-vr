using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Independent world-space video popup window used by course menu as a pure selector.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class VideoPopupWindow : MonoBehaviour
{
    private static readonly Color FrameBackColor = new Color(0.88f, 0.94f, 1f, 1f);
    private static readonly Color FrameBarColor = new Color(0.79f, 0.88f, 0.99f, 1f);
    private static readonly Color PanelColor = new Color(0.97f, 0.985f, 1f, 0.98f);
    private static readonly Color PanelBorderColor = new Color(0.8f, 0.88f, 0.98f, 1f);
    private static readonly Color PrimaryButtonColor = new Color(0.2f, 0.55f, 0.93f, 0.98f);
    private static readonly Color SecondaryButtonColor = new Color(0.89f, 0.94f, 1f, 0.98f);
    private static readonly Color TextColor = new Color(0.1f, 0.16f, 0.24f, 1f);

    [SerializeField] private Transform windowTransform;
    [SerializeField] private Vector2 defaultWindowSize = new Vector2(3.2f, 1.8f);
    [SerializeField] private Vector2Int renderTextureSize = new Vector2Int(1920, 1080);
    [Header("Placement")]
    [SerializeField] private bool keepPlacedTransformOnPlay = true;
    [SerializeField] private bool keepPlacedScaleOnPlay = true;
    [Header("Editor Preview")]
    [SerializeField] private bool autoCreateWindowInEditor = true;
    [SerializeField] private Color editorPreviewColor = new Color(0.9f, 0.95f, 1f, 1f);
    [Header("Audio")]
    [SerializeField] private AudioSource videoAudioSource;
    [SerializeField, Range(0f, 1f)] private float audioVolume = 1f;
    [SerializeField] private bool spatialAudio = false;
    [Header("Controls")]
    [SerializeField] private bool showControls = true;
    [SerializeField] private Vector3 controlsScale = new Vector3(0.001f, 0.0015f, 0.0012f);
    [SerializeField] private float seekStepSeconds = 10f;

    private VideoPlayer videoPlayer;
    private RenderTexture renderTexture;
    private MeshRenderer windowRenderer;
    private Material windowMaterial;
    private bool createdRuntimeWindow;
    private string cachedLocalVideoPath;
    private Canvas controlsCanvas;
    private GraphicRaycaster controlsRaycaster;
    private RectTransform controlsRootRect;
    private Button playPauseButton;
    private Button stopButton;
    private Button rewindButton;
    private Button forwardButton;
    private Slider seekSlider;
    private TMP_Text timeLabel;
    private TMP_Text titleLabel;
    private bool controlsBound;
    private bool isSeeking;
    private bool createdRuntimeControlsCanvas;
    private string currentVideoTitle = "Video";

    public VideoPlayer Player => videoPlayer;
    public bool IsPlaying => videoPlayer != null && videoPlayer.isPlaying;

    private void Reset()
    {
        EnsureWindowExists(forceEditorCreation: true);
        ApplyWindowSize(defaultWindowSize);
        EnsureEditorPreviewMaterial();
    }

    private void OnValidate()
    {
        if (!autoCreateWindowInEditor) return;
        if (Application.isPlaying) return;

        EnsureWindowExists(forceEditorCreation: true);
        ApplyWindowSize(defaultWindowSize);
        EnsureEditorPreviewMaterial();
    }

    public async Task PlayUrlAsync(string url, string title, Transform viewer, float distance, float heightOffset, Vector2 windowSize)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        currentVideoTitle = string.IsNullOrWhiteSpace(title) ? "Video" : title.Trim();

        if (IsYouTubeUrl(url))
        {
            EnsureWindowExists(forceEditorCreation: false);
            EnsureEditorPreviewMaterial();
            throw new NotSupportedException("YouTube watch URL is a webpage, not a direct media stream. Please use direct mp4/m3u8 URL or a backend-transcoded stream URL.");
        }

        EnsureWindowExists(forceEditorCreation: false);
        EnsureVideoResources();
        EnsureControlsCanvas();
        bool autoPlace = !keepPlacedTransformOnPlay || createdRuntimeWindow;
        if (autoPlace)
        {
            PositionWindow(viewer, distance, heightOffset);
        }

        if (!keepPlacedScaleOnPlay || createdRuntimeWindow)
        {
            ResizeWindow(windowSize);
        }

        Exception lastError = null;
        List<string> candidates = BuildPlaybackCandidates(url);

        try
        {
            foreach (string candidate in candidates)
            {
                try
                {
                    Debug.Log($"[VideoPopupWindow] Try stream source: {candidate}");
                    await PrepareAndPlayAsync(candidate);
                    if (windowRenderer != null)
                    {
                        windowRenderer.enabled = true;
                    }
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            // Final fallback for Google Drive on Windows: download video to local temp file and play via file://
            foreach (string candidate in candidates)
            {
                try
                {
                    string localFile = await DownloadVideoToLocalTempAsync(candidate);
                    string fileUrl = $"file://{localFile.Replace("\\", "/")}";
                    Debug.Log($"[VideoPopupWindow] Try local fallback: {fileUrl}");
                    await PrepareAndPlayAsync(fileUrl);

                    if (windowRenderer != null)
                    {
                        windowRenderer.enabled = true;
                    }
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            // Additional Google Drive endpoint fallback.
            if (TryExtractGoogleDriveFileId(url, out string driveFileId))
            {
                try
                {
                    string localFile = await DownloadGoogleDriveToLocalTempAsync(driveFileId);
                    if (!string.IsNullOrWhiteSpace(localFile))
                    {
                        string fileUrl = $"file://{localFile.Replace("\\", "/")}";
                        Debug.Log($"[VideoPopupWindow] Try Google local fallback: {fileUrl}");
                        await PrepareAndPlayAsync(fileUrl);

                        if (windowRenderer != null)
                        {
                            windowRenderer.enabled = true;
                        }
                        return;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            throw lastError ?? new Exception("No playable URL candidate.");
        }
        catch (Exception ex)
        {
            throw new Exception($"Video popup could not play source. {ex.Message}", ex);
        }
    }

    private async Task PrepareAndPlayAsync(string sourceUrl)
    {
        var tcs = new TaskCompletionSource<bool>();
        void Prepared(VideoPlayer _) => tcs.TrySetResult(true);
        void Error(VideoPlayer _, string msg) => tcs.TrySetException(new Exception(msg));

        videoPlayer.prepareCompleted += Prepared;
        videoPlayer.errorReceived += Error;

        try
        {
            if (windowRenderer != null)
            {
                // Keep surface visible during prepare to simplify debugging.
                windowRenderer.enabled = true;
            }

            videoPlayer.Stop();
            videoPlayer.source = VideoSource.Url;
            videoPlayer.url = sourceUrl;
            videoPlayer.isLooping = false;
            ConfigureVideoAudio();
            videoPlayer.Prepare();
            await tcs.Task;
            ConfigureVideoAudio();
            videoPlayer.Play();
            RefreshControlsUi();
        }
        finally
        {
            videoPlayer.prepareCompleted -= Prepared;
            videoPlayer.errorReceived -= Error;
        }
    }

    private List<string> BuildPlaybackCandidates(string url)
    {
        var candidates = new List<string>();
        AddUnique(candidates, url);

        if (TryExtractGoogleDriveFileId(url, out string fileId))
        {
            AddUnique(candidates, $"https://drive.google.com/uc?export=download&id={fileId}");
            AddUnique(candidates, $"https://drive.google.com/uc?export=download&id={fileId}&confirm=t");
            AddUnique(candidates, $"https://drive.usercontent.google.com/download?id={fileId}&export=download&confirm=t");
        }

        return candidates;
    }

    private static void AddUnique(List<string> list, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!list.Contains(value)) list.Add(value);
    }

    private async Task<string> DownloadGoogleDriveToLocalTempAsync(string fileId)
    {
        string downloadUrl = $"https://drive.google.com/uc?export=download&id={fileId}&confirm=t";
        using UnityWebRequest req = UnityWebRequest.Get(downloadUrl);
        req.downloadHandler = new DownloadHandlerBuffer();

        await SendRequestAsync(req);

        if (req.result != UnityWebRequest.Result.Success)
        {
            throw new Exception($"Google Drive download failed: {req.error}");
        }

        byte[] data = req.downloadHandler.data;
        if (data == null || data.Length == 0)
        {
            throw new Exception("Downloaded video is empty.");
        }

        string filePath = Path.Combine(Application.temporaryCachePath, $"vr_video_{fileId}.mp4");
        File.WriteAllBytes(filePath, data);
        cachedLocalVideoPath = filePath;
        return filePath;
    }

    private async Task<string> DownloadVideoToLocalTempAsync(string sourceUrl)
    {
        using UnityWebRequest req = UnityWebRequest.Get(sourceUrl);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.timeout = 25;

        await SendRequestAsync(req);

        if (req.result != UnityWebRequest.Result.Success)
        {
            throw new Exception($"Download fallback failed: {req.error}");
        }

        string contentType = req.GetResponseHeader("Content-Type") ?? string.Empty;
        byte[] data = req.downloadHandler.data;
        if (data == null || data.Length < 1024)
        {
            throw new Exception("Downloaded fallback file too small or empty.");
        }

        // Avoid storing HTML interstitial pages as mp4.
        if (!string.IsNullOrEmpty(contentType) && contentType.IndexOf("video", StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new Exception($"Fallback content-type is not video: {contentType}");
        }

        string filePath = Path.Combine(Application.temporaryCachePath, $"vr_video_{Mathf.Abs(sourceUrl.GetHashCode())}.mp4");
        File.WriteAllBytes(filePath, data);
        cachedLocalVideoPath = filePath;
        return filePath;
    }

    private static Task SendRequestAsync(UnityWebRequest req)
    {
        var tcs = new TaskCompletionSource<bool>();
        UnityWebRequestAsyncOperation op = req.SendWebRequest();
        op.completed += _ => tcs.TrySetResult(true);
        return tcs.Task;
    }

    private static bool TryExtractGoogleDriveFileId(string url, out string fileId)
    {
        fileId = null;
        if (string.IsNullOrWhiteSpace(url)) return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri parsedUri))
        {
            return false;
        }

        string host = (parsedUri.Host ?? string.Empty).ToLowerInvariant();
        bool isDriveHost = host == "drive.google.com" || host == "docs.google.com";
        if (!isDriveHost)
        {
            // Do not treat arbitrary URLs with id=... as Google Drive files.
            return false;
        }

        int marker = url.IndexOf("/file/d/", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0)
        {
            string rest = url.Substring(marker + 8);
            int slash = rest.IndexOf('/');
            fileId = slash >= 0 ? rest.Substring(0, slash) : rest;
            int q = fileId.IndexOf('?');
            if (q >= 0) fileId = fileId.Substring(0, q);
            return !string.IsNullOrWhiteSpace(fileId);
        }

        int queryStart = url.IndexOf('?');
        if (queryStart < 0 || queryStart >= url.Length - 1) return false;

        string query = url.Substring(queryStart + 1);
        string[] pairs = query.Split('&');
        foreach (string pair in pairs)
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0) continue;

            string key = pair.Substring(0, eq);
            if (!key.Equals("id", StringComparison.OrdinalIgnoreCase)) continue;

            string val = pair.Substring(eq + 1);
            if (string.IsNullOrWhiteSpace(val)) continue;

            fileId = Uri.UnescapeDataString(val);
            return !string.IsNullOrWhiteSpace(fileId);
        }

        return false;
    }

    private static bool IsYouTubeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return url.IndexOf("youtube.com/watch", StringComparison.OrdinalIgnoreCase) >= 0
            || url.IndexOf("youtu.be/", StringComparison.OrdinalIgnoreCase) >= 0
            || url.IndexOf("youtube.com/shorts/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public void StopAndHide()
    {
        if (videoPlayer != null)
        {
            videoPlayer.Stop();
        }

        if (windowRenderer != null)
        {
            windowRenderer.enabled = false;
        }

        if (controlsCanvas != null)
        {
            controlsCanvas.gameObject.SetActive(false);
        }
    }

    public void PausePlayback()
    {
        if (videoPlayer != null && videoPlayer.isPlaying)
        {
            videoPlayer.Pause();
        }
    }

    public void ResumePlayback()
    {
        if (videoPlayer != null && !videoPlayer.isPlaying)
        {
            videoPlayer.Play();
        }
        RefreshControlsUi();
    }

    private void EnsureWindow()
    {
        EnsureWindowExists(forceEditorCreation: false);
    }

    private void EnsureWindowExists(bool forceEditorCreation)
    {
        if (windowTransform == null)
        {
            if (!Application.isPlaying && !forceEditorCreation) return;

            GameObject window = GameObject.CreatePrimitive(PrimitiveType.Quad);
            window.name = "CourseVideoWindow";
            windowTransform = window.transform;
            windowTransform.SetParent(transform, false);
            windowTransform.localPosition = Vector3.zero;
            windowTransform.localRotation = Quaternion.identity;

            if (Application.isPlaying)
            {
                createdRuntimeWindow = true;
            }

            Collider col = window.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
        }

        windowRenderer = windowTransform.GetComponent<MeshRenderer>();
        if (windowRenderer == null)
        {
            windowRenderer = windowTransform.GetComponentInChildren<MeshRenderer>();
        }

        if (windowRenderer == null)
        {
            GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
            surface.name = "VideoScreenSurface";
            surface.transform.SetParent(windowTransform, false);
            surface.transform.localPosition = Vector3.zero;
            surface.transform.localRotation = Quaternion.identity;
            windowRenderer = surface.GetComponent<MeshRenderer>();

            Collider col = surface.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
        }

        EnsureFrameVisuals();
        EnsureControlsCanvas();
    }

    private void EnsureFrameVisuals()
    {
        if (windowRenderer == null) return;

        Transform surfaceTx = windowRenderer.transform;
        Transform backplate = surfaceTx.Find("VideoFrameBackplate");
        if (backplate == null)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "VideoFrameBackplate";
            backplate = go.transform;
            backplate.SetParent(surfaceTx, false);
            Collider col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
        }

        backplate.localPosition = new Vector3(0f, 0f, 0.01f);
        backplate.localScale = new Vector3(1.08f, 1.14f, 1f);
        ApplyUnlitColor(backplate.GetComponent<MeshRenderer>(), FrameBackColor);

        Transform topBar = surfaceTx.Find("VideoFrameTopBar");
        if (topBar == null)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "VideoFrameTopBar";
            topBar = go.transform;
            topBar.SetParent(surfaceTx, false);
            Collider col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
        }

        topBar.localPosition = new Vector3(0f, 0.54f, -0.001f);
        topBar.localScale = new Vector3(1f, 0.08f, 1f);
        ApplyUnlitColor(topBar.GetComponent<MeshRenderer>(), FrameBarColor);
    }

    private static void ApplyUnlitColor(MeshRenderer renderer, Color color)
    {
        if (renderer == null) return;

        Material target = Application.isPlaying ? renderer.material : renderer.sharedMaterial;
        if (target == null || target.shader == null || target.shader.name != "Unlit/Color")
        {
            Material created = new Material(Shader.Find("Unlit/Color"));
            if (Application.isPlaying)
            {
                renderer.material = created;
                target = renderer.material;
            }
            else
            {
                renderer.sharedMaterial = created;
                target = renderer.sharedMaterial;
            }
        }

        target.color = color;
    }

    private void EnsureEditorPreviewMaterial()
    {
        if (Application.isPlaying) return;
        if (windowTransform == null) return;
        if (windowRenderer == null) windowRenderer = windowTransform.GetComponent<MeshRenderer>();
        if (windowRenderer == null) return;

        Material previewMat = windowRenderer.sharedMaterial;
        if (previewMat == null || previewMat.shader == null || previewMat.shader.name != "Unlit/Color")
        {
            previewMat = new Material(Shader.Find("Unlit/Color"));
            previewMat.name = "VideoPopupWindow_Preview";
        }

        previewMat.color = editorPreviewColor;
        windowRenderer.sharedMaterial = previewMat;
        windowRenderer.enabled = true;
    }

    private void EnsureVideoResources()
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
        }

        if (videoAudioSource == null)
        {
            videoAudioSource = GetComponent<AudioSource>();
            if (videoAudioSource == null)
            {
                videoAudioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        videoAudioSource.playOnAwake = false;
        videoAudioSource.loop = false;
        videoAudioSource.spatialBlend = spatialAudio ? 1f : 0f;
        videoAudioSource.volume = Mathf.Clamp01(audioVolume);

        ConfigureVideoAudio();

        if (renderTexture == null)
        {
            renderTexture = new RenderTexture(renderTextureSize.x, renderTextureSize.y, 0, RenderTextureFormat.ARGB32)
            {
                name = "CourseVideoPopupRT"
            };
            renderTexture.Create();
        }

        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.targetTexture = renderTexture;

        if (!keepPlacedScaleOnPlay)
        {
            ApplyWindowSize(defaultWindowSize);
        }

        if (windowMaterial == null)
        {
            windowMaterial = new Material(Shader.Find("Unlit/Texture"));
        }

        windowMaterial.mainTexture = renderTexture;
        if (windowRenderer != null)
        {
            windowRenderer.sharedMaterial = windowMaterial;
        }

        if (controlsCanvas != null)
        {
            controlsCanvas.gameObject.SetActive(showControls);
        }
    }

    private void ConfigureVideoAudio()
    {
        if (videoPlayer == null) return;

        videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
        videoPlayer.controlledAudioTrackCount = 1;
        videoPlayer.EnableAudioTrack(0, true);

        if (videoAudioSource != null)
        {
            videoAudioSource.mute = false;
            videoAudioSource.volume = Mathf.Clamp01(audioVolume);
            videoAudioSource.spatialBlend = spatialAudio ? 1f : 0f;
            videoPlayer.SetTargetAudioSource(0, videoAudioSource);
        }
    }

    private void PositionWindow(Transform viewer, float distance, float heightOffset)
    {
        if (windowTransform == null) return;

        Transform cameraTx = viewer != null ? viewer : (Camera.main != null ? Camera.main.transform : null);
        if (cameraTx == null) return;

        Vector3 forward = cameraTx.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = cameraTx.forward;
        }
        forward.Normalize();

        Vector3 pos = cameraTx.position + (forward * Mathf.Max(0.5f, distance));
        pos += Vector3.up * heightOffset;
        windowTransform.position = pos;

        Vector3 toViewer = cameraTx.position - pos;
        toViewer.y = 0f;
        if (toViewer.sqrMagnitude > 0.0001f)
        {
            windowTransform.rotation = Quaternion.LookRotation(toViewer.normalized, Vector3.up);
        }
    }

    private void ResizeWindow(Vector2 windowSize)
    {
        if (windowTransform == null) return;

        Transform scaleTarget = windowRenderer != null ? windowRenderer.transform : windowTransform;

        float width = Mathf.Max(0.5f, windowSize.x > 0f ? windowSize.x : defaultWindowSize.x);
        float height = Mathf.Max(0.3f, windowSize.y > 0f ? windowSize.y : defaultWindowSize.y);
        scaleTarget.localScale = new Vector3(width, height, 1f);
        if (createdRuntimeControlsCanvas)
        {
            LayoutControlsCanvas(width, height);
        }
    }

    private void ApplyWindowSize(Vector2 size)
    {
        if (windowTransform == null) return;

        float width = Mathf.Max(0.5f, size.x);
        float height = Mathf.Max(0.3f, size.y);
        windowTransform.localScale = new Vector3(width, height, 1f);
        if (createdRuntimeControlsCanvas)
        {
            LayoutControlsCanvas(width, height);
        }
    }

    private void Update()
    {
        if (!Application.isPlaying) return;
        RefreshControlsUi();
    }

    private void EnsureControlsCanvas()
    {
        if (!showControls || windowTransform == null)
        {
            return;
        }

        Transform existing = windowTransform.Find("VideoControlsCanvas");
        if (existing == null)
        {
            GameObject go = new GameObject("VideoControlsCanvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(CanvasScaler));
            go.transform.SetParent(windowTransform, false);
            existing = go.transform;
            createdRuntimeControlsCanvas = true;
        }

        controlsCanvas = existing.GetComponent<Canvas>();
        controlsRaycaster = existing.GetComponent<GraphicRaycaster>();
        controlsRootRect = existing as RectTransform;

        controlsCanvas.renderMode = RenderMode.WorldSpace;
        CanvasScaler scaler = existing.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

        if (createdRuntimeControlsCanvas)
        {
            existing.localScale = controlsScale;
            LayoutControlsCanvas(defaultWindowSize.x, defaultWindowSize.y);
        }

        XRRuntimeUiHelper.EnsureWorldSpaceCanvasInteraction(existing.gameObject);
        BuildControlsUi();
        EnsureEventSystemSupport();
    }

    private void LayoutControlsCanvas(float width, float height)
    {
        if (controlsRootRect == null) return;

        controlsRootRect.anchorMin = new Vector2(0.5f, 0.5f);
        controlsRootRect.anchorMax = new Vector2(0.5f, 0.5f);
        controlsRootRect.pivot = new Vector2(0.5f, 0.5f);
        controlsRootRect.sizeDelta = new Vector2(1280f, 720f);
        controlsRootRect.localPosition = new Vector3(0f, -0.134f, -0.01f);
        controlsRootRect.localRotation = Quaternion.identity;
        controlsRootRect.localScale = controlsScale;
    }

    private void BuildControlsUi()
    {
        if (controlsRootRect == null) return;

        Image panel = FindOrCreateImage(controlsRootRect, "ControlsPanel");
        RectTransform panelRect = panel.rectTransform;
        panelRect.anchorMin = new Vector2(0.08f, 0.03f);
        panelRect.anchorMax = new Vector2(0.92f, 0.18f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        panel.color = PanelColor;

        Outline outline = panel.GetComponent<Outline>();
        if (outline == null) outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = PanelBorderColor;
        outline.effectDistance = new Vector2(1f, -1f);

        titleLabel = FindOrCreateText(panelRect, "VideoTitle", new Vector2(0.02f, 0.58f), new Vector2(0.32f, 0.94f), 28f, FontStyles.Bold, TextAlignmentOptions.Left);
        timeLabel = FindOrCreateText(panelRect, "TimeLabel", new Vector2(0.76f, 0.58f), new Vector2(0.98f, 0.94f), 24f, FontStyles.Normal, TextAlignmentOptions.Right);

        playPauseButton = FindOrCreateButton(panelRect, "PlayPauseButton", new Vector2(0.02f, 0.12f), new Vector2(0.12f, 0.52f), "Play", PrimaryButtonColor, Color.white);
        stopButton = FindOrCreateButton(panelRect, "StopButton", new Vector2(0.13f, 0.12f), new Vector2(0.21f, 0.52f), "Stop", SecondaryButtonColor, TextColor);
        rewindButton = FindOrCreateButton(panelRect, "RewindButton", new Vector2(0.22f, 0.12f), new Vector2(0.30f, 0.52f), "-10", SecondaryButtonColor, TextColor);
        forwardButton = FindOrCreateButton(panelRect, "ForwardButton", new Vector2(0.31f, 0.12f), new Vector2(0.39f, 0.52f), "+10", SecondaryButtonColor, TextColor);
        seekSlider = FindOrCreateSlider(panelRect, "SeekSlider", new Vector2(0.41f, 0.16f), new Vector2(0.98f, 0.48f));

        if (controlsBound) return;

        if (playPauseButton != null) playPauseButton.onClick.AddListener(TogglePlayPause);
        if (stopButton != null) stopButton.onClick.AddListener(StopPlayback);
        if (rewindButton != null) rewindButton.onClick.AddListener(() => SeekBy(-Mathf.Max(1f, seekStepSeconds)));
        if (forwardButton != null) forwardButton.onClick.AddListener(() => SeekBy(Mathf.Max(1f, seekStepSeconds)));
        if (seekSlider != null)
        {
            seekSlider.onValueChanged.AddListener(OnSeekSliderChanged);
            EventTrigger trigger = seekSlider.gameObject.GetComponent<EventTrigger>();
            if (trigger == null) trigger = seekSlider.gameObject.AddComponent<EventTrigger>();
            AddEventTrigger(trigger, EventTriggerType.PointerDown, () => isSeeking = true);
            AddEventTrigger(trigger, EventTriggerType.PointerUp, () =>
            {
                isSeeking = false;
                ApplySeekSlider();
            });
        }

        controlsBound = true;
    }

    private void RefreshControlsUi()
    {
        if (!showControls || videoPlayer == null || controlsCanvas == null)
        {
            return;
        }

        controlsCanvas.gameObject.SetActive(windowRenderer != null && windowRenderer.enabled);

        if (titleLabel != null)
        {
            titleLabel.text = currentVideoTitle;
            titleLabel.color = TextColor;
        }

        if (timeLabel != null)
        {
            float current = (float)Math.Max(0d, videoPlayer.time);
            float total = videoPlayer.length > 0d ? (float)videoPlayer.length : 0f;
            timeLabel.text = $"{FormatTime(current)} / {FormatTime(total)}";
            timeLabel.color = TextColor;
        }

        if (playPauseButton != null)
        {
            TMP_Text txt = playPauseButton.GetComponentInChildren<TMP_Text>(true);
            if (txt != null) txt.text = videoPlayer.isPlaying ? "Pause" : "Play";
        }

        if (seekSlider != null && !isSeeking)
        {
            float total = videoPlayer.length > 0d ? (float)videoPlayer.length : 1f;
            seekSlider.minValue = 0f;
            seekSlider.maxValue = Mathf.Max(1f, total);
            seekSlider.SetValueWithoutNotify((float)Math.Max(0d, videoPlayer.time));
        }
    }

    private void TogglePlayPause()
    {
        if (videoPlayer == null) return;
        if (videoPlayer.isPlaying) videoPlayer.Pause();
        else videoPlayer.Play();
        RefreshControlsUi();
    }

    private void StopPlayback()
    {
        if (videoPlayer == null) return;
        videoPlayer.Stop();
        RefreshControlsUi();
    }

    private void SeekBy(float deltaSeconds)
    {
        if (videoPlayer == null) return;
        double length = videoPlayer.length;
        double current = videoPlayer.time;
        double target = current + deltaSeconds;
        if (length > 0d)
        {
            target = Math.Max(0d, Math.Min(length, target));
        }
        else
        {
            target = Math.Max(0d, target);
        }
        videoPlayer.time = target;
        RefreshControlsUi();
    }

    private void OnSeekSliderChanged(float _)
    {
        if (isSeeking)
        {
            RefreshControlsUi();
        }
    }

    private void ApplySeekSlider()
    {
        if (videoPlayer == null || seekSlider == null) return;
        videoPlayer.time = seekSlider.value;
        RefreshControlsUi();
    }

    private void EnsureEventSystemSupport()
    {
        if (!Application.isPlaying) return;
        XRRuntimeUiHelper.EnsureEventSystemSupportsXR();
    }

    private static void AddEventTrigger(EventTrigger trigger, EventTriggerType type, Action action)
    {
        if (trigger == null || action == null) return;
        EventTrigger.Entry entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => action());
        trigger.triggers.Add(entry);
    }

    private static Image FindOrCreateImage(RectTransform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing == null)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            existing = go.transform;
        }

        return existing.GetComponent<Image>();
    }

    private static TMP_Text FindOrCreateText(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, float fontSize, FontStyles fontStyle, TextAlignmentOptions alignment)
    {
        Transform existing = parent.Find(name);
        if (existing == null)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            existing = go.transform;
        }

        RectTransform rect = existing.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        TMP_Text text = existing.GetComponent<TMP_Text>();
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = alignment;
        text.color = TextColor;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    private static Button FindOrCreateButton(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, string label, Color backgroundColor, Color textColor)
    {
        Transform existing = parent.Find(name);
        if (existing == null)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            GameObject textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(go.transform, false);
            existing = go.transform;
        }

        RectTransform rect = existing.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = existing.GetComponent<Image>();
        image.color = backgroundColor;

        Button button = existing.GetComponent<Button>();
        TMP_Text text = existing.GetComponentInChildren<TMP_Text>(true);
        text.text = label;
        text.fontSize = 22f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = textColor;

        RectTransform textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return button;
    }

    private static Slider FindOrCreateSlider(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        Transform existing = parent.Find(name);
        if (existing == null)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(parent, false);

            GameObject background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(go.transform, false);
            GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(go.transform, false);
            GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(fillArea.transform, false);
            GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(go.transform, false);
            GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(handleArea.transform, false);

            existing = go.transform;
        }

        RectTransform rect = existing.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Slider slider = existing.GetComponent<Slider>();
        slider.direction = Slider.Direction.LeftToRight;

        RectTransform backgroundRect = existing.Find("Background").GetComponent<RectTransform>();
        backgroundRect.anchorMin = new Vector2(0f, 0.35f);
        backgroundRect.anchorMax = new Vector2(1f, 0.65f);
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;
        Image backgroundImage = backgroundRect.GetComponent<Image>();
        backgroundImage.color = new Color(0.86f, 0.91f, 0.97f, 1f);

        RectTransform fillAreaRect = existing.Find("Fill Area").GetComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0f, 0.35f);
        fillAreaRect.anchorMax = new Vector2(1f, 0.65f);
        fillAreaRect.offsetMin = new Vector2(6f, 0f);
        fillAreaRect.offsetMax = new Vector2(-6f, 0f);

        RectTransform fillRect = existing.Find("Fill Area/Fill").GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        Image fillImage = fillRect.GetComponent<Image>();
        fillImage.color = new Color(0.22f, 0.56f, 0.93f, 1f);

        RectTransform handleAreaRect = existing.Find("Handle Slide Area").GetComponent<RectTransform>();
        handleAreaRect.anchorMin = Vector2.zero;
        handleAreaRect.anchorMax = Vector2.one;
        handleAreaRect.offsetMin = new Vector2(10f, 0f);
        handleAreaRect.offsetMax = new Vector2(-10f, 0f);

        RectTransform handleRect = existing.Find("Handle Slide Area/Handle").GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(18f, 18f);
        Image handleImage = handleRect.GetComponent<Image>();
        handleImage.color = Color.white;

        slider.targetGraphic = handleImage;
        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        return slider;
    }

    private static string FormatTime(float sec)
    {
        sec = Mathf.Max(0f, sec);
        int h = Mathf.FloorToInt(sec / 3600f);
        int m = Mathf.FloorToInt((sec % 3600f) / 60f);
        int s = Mathf.FloorToInt(sec % 60f);
        return h > 0 ? $"{h:00}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
    }

    private void OnDestroy()
    {
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }

        if (windowMaterial != null)
        {
            Destroy(windowMaterial);
        }

        if (createdRuntimeWindow && windowTransform != null)
        {
            Destroy(windowTransform.gameObject);
        }

        if (!string.IsNullOrWhiteSpace(cachedLocalVideoPath) && File.Exists(cachedLocalVideoPath))
        {
            try
            {
                File.Delete(cachedLocalVideoPath);
            }
            catch
            {
                // Ignore cleanup errors for temp files.
            }
        }
    }

    [ContextMenu("Create/Refresh Video Window")]
    private void CreateOrRefreshVideoWindow()
    {
        EnsureWindowExists(forceEditorCreation: true);
        if (!keepPlacedScaleOnPlay)
        {
            ApplyWindowSize(defaultWindowSize);
        }
        EnsureEditorPreviewMaterial();
    }
}
