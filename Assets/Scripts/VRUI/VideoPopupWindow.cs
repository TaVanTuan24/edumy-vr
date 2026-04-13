using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
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

    private VideoPlayer videoPlayer;
    private RenderTexture renderTexture;
    private MeshRenderer windowRenderer;
    private Material windowMaterial;
    private bool createdRuntimeWindow;
    private string cachedLocalVideoPath;

    public VideoPlayer Player => videoPlayer;

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

    public async Task PlayUrlAsync(string url, Transform viewer, float distance, float heightOffset, Vector2 windowSize)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        if (IsYouTubeUrl(url))
        {
            EnsureWindowExists(forceEditorCreation: false);
            EnsureEditorPreviewMaterial();
            throw new NotSupportedException("YouTube watch URL is a webpage, not a direct media stream. Please use direct mp4/m3u8 URL or a backend-transcoded stream URL.");
        }

        EnsureWindowExists(forceEditorCreation: false);
        EnsureVideoResources();
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
        if (videoPlayer != null && videoPlayer.isPlaying)
        {
            videoPlayer.Stop();
        }

        if (windowRenderer != null)
        {
            windowRenderer.enabled = false;
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
    }

    private void ApplyWindowSize(Vector2 size)
    {
        if (windowTransform == null) return;

        float width = Mathf.Max(0.5f, size.x);
        float height = Mathf.Max(0.3f, size.y);
        windowTransform.localScale = new Vector3(width, height, 1f);
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
