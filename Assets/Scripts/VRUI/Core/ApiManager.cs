using UnityEngine;
using UnityEngine.Networking;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using TMPro;

public class ApiManager : MonoBehaviour
{
    private const string JwtTokenPlayerPrefsKey = "JWT_TOKEN";
    private const string JwtTokenEnvironmentVariable = "EDUMY_VR_JWT_TOKEN";

    public static ApiManager Instance { get; private set; }

    [Header("API Settings")]
    [Tooltip("Địa chỉ LAN IP của bạn (Ví dụ: http://192.168.1.1:3000)")]
    [SerializeField] private string localUrl = "http://192.168.1.1:3000";
    
    [Tooltip("Địa chỉ production (Ví dụ: https://api.edumy-vr.com)")]
    [SerializeField] private string productionUrl = "https://api.edumy-vr.com";

    [Tooltip("Bật để sử dụng Production URL, tắt để sử dụng Local URL")]
    public bool useProduction = false;

    [Header("Authentication")]
    [Tooltip("JWT Token dùng để xác thực. Token này sẽ được gửi tự động trong header Authorization.")]
    [SerializeField] private string authToken = string.Empty;
    [SerializeField] private bool loadTokenFromPlayerPrefs;
    [SerializeField] private bool persistTokenInPlayerPrefs;

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI errorTextDisplay; 
    [SerializeField] private TextMeshProUGUI debugUrlText;    

    public string BaseUrl => useProduction ? productionUrl : localUrl;
    public bool HasAuthToken => !string.IsNullOrWhiteSpace(authToken);
    private bool lessonProgressSyncDisabled;
    private bool lessonProgressSyncDisableLogged;
    public string LastStreamResolveErrorCode { get; private set; }
    public string LastStreamResolveErrorMessage { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadAuthToken();
            
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // Log thông tin khởi tạo theo yêu cầu
        Debug.Log($"ApiManager initialized. BaseUrl: {BaseUrl} | Auth configured: {HasAuthToken}");
        
        if (debugUrlText != null) 
            debugUrlText.text = $"API: {BaseUrl}";
    }

    public void SetAuthToken(string token)
    {
        authToken = string.IsNullOrWhiteSpace(token) ? string.Empty : token.Trim();
        if (persistTokenInPlayerPrefs && !string.IsNullOrWhiteSpace(authToken))
        {
            PlayerPrefs.SetString(JwtTokenPlayerPrefsKey, authToken);
            PlayerPrefs.Save();
        }
        else if (PlayerPrefs.HasKey(JwtTokenPlayerPrefsKey))
        {
            PlayerPrefs.DeleteKey(JwtTokenPlayerPrefsKey);
            PlayerPrefs.Save();
        }
        Debug.Log($"[ApiManager] Auth token updated. Persisted: {persistTokenInPlayerPrefs && !string.IsNullOrWhiteSpace(authToken)}");
    }

    public void ClearAuthToken()
    {
        authToken = string.Empty;

        if (PlayerPrefs.HasKey(JwtTokenPlayerPrefsKey))
        {
            PlayerPrefs.DeleteKey(JwtTokenPlayerPrefsKey);
            PlayerPrefs.Save();
        }
    }

    public async Task<List<CourseData>> GetCoursesAsync()
    {
        // Đồng bộ endpoint theo cùng pattern với lessons
        string url = BuildUrl("api/vr/courses");
        Debug.Log($"[ApiManager] Fetching courses from: {url}");
        var response = await SendGetRequest<ApiResponse<CourseData>>(url);
        return response?.data;
    }

    public async Task<List<LessonData>> GetLessonsAsync(string courseId)
    {
        string url = BuildUrl($"api/vr/courses/{courseId}/lessons");
        string jsonResponse = await SendGetRawRequest(url);
        if (string.IsNullOrWhiteSpace(jsonResponse)) return null;

        List<LessonData> lessons = ParseLessonsFromJson(jsonResponse);
        Debug.Log($"[ApiManager] Parsed lessons count: {(lessons == null ? 0 : lessons.Count)}");
        return lessons;
    }

    public async Task<List<SectionData>> GetCourseSectionsAsync(string courseId)
    {
        string url = BuildUrl($"api/vr/courses/{courseId}/lessons");
        string jsonResponse = await SendGetRawRequest(url);
        if (string.IsNullOrWhiteSpace(jsonResponse)) return null;

        List<SectionData> sections = ParseSectionsFromJson(jsonResponse);
        Debug.Log($"[ApiManager] Parsed sections count: {(sections == null ? 0 : sections.Count)}");
        return sections;
    }

    public async Task<bool> UpdateLessonCompletionAsync(string courseId, string lessonId, string videoUrl, bool completed)
    {
        if (lessonProgressSyncDisabled)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(courseId) || string.IsNullOrWhiteSpace(lessonId))
        {
            Debug.LogWarning("[ApiManager] UpdateLessonCompletionAsync missing courseId/lessonId.");
            return false;
        }

        ProgressUpdateRequest payload = new ProgressUpdateRequest
        {
            lessonId = lessonId,
            video = string.IsNullOrWhiteSpace(videoUrl) ? string.Empty : videoUrl,
            completed = completed
        };

        string body = JsonUtility.ToJson(payload);
        string[] candidates = new[]
        {
            BuildUrl($"api/vr/courses/{courseId}/progress"),
            BuildUrl($"api/courses/{courseId}/progress"),
            BuildUrl($"courses/{courseId}/progress")
        };

        bool all404 = true;
        foreach (string url in candidates)
        {
            PostStatus status = await SendPostJsonRequest(url, body);
            if (status == PostStatus.Success)
            {
                return true;
            }

            if (status != PostStatus.NotFound)
            {
                all404 = false;
            }
        }

        if (all404)
        {
            lessonProgressSyncDisabled = true;
            if (!lessonProgressSyncDisableLogged)
            {
                lessonProgressSyncDisableLogged = true;
                Debug.LogWarning("[ApiManager] Lesson progress sync endpoint not found (404). Auto disable server sync and keep local realtime update.");
            }

            // Keep UI interaction smooth even when backend endpoint has not been deployed yet.
            return true;
        }

        return false;
    }

    public async Task<string> ResolvePlayableStreamUrlAsync(string sourceUrl, string courseId, string lessonId, string preferredFormat = "m3u8")
    {
        if (string.IsNullOrWhiteSpace(sourceUrl)) return null;

        LastStreamResolveErrorCode = null;
        LastStreamResolveErrorMessage = null;

        string url = BuildUrl("api/vr/stream/resolve");
        StreamResolveRequest payload = new StreamResolveRequest
        {
            sourceUrl = sourceUrl,
            preferredFormat = string.IsNullOrWhiteSpace(preferredFormat) ? "m3u8" : preferredFormat,
            courseId = courseId,
            lessonId = lessonId
        };

        string body = JsonUtility.ToJson(payload);
        StreamResolveResponse response = await SendPostJsonRequestForResponse<StreamResolveResponse>(url, body);

        if (response != null && response.success && response.data != null && !string.IsNullOrWhiteSpace(response.data.resolvedUrl))
        {
            return response.data.resolvedUrl;
        }

        if (response != null && response.error != null && !string.IsNullOrWhiteSpace(response.error.message))
        {
            LastStreamResolveErrorCode = response.error.code;
            LastStreamResolveErrorMessage = string.IsNullOrWhiteSpace(response.error.details)
                ? response.error.message
                : $"{response.error.message} | {response.error.details}";
            Debug.Log($"[ApiManager] Stream resolve failed ({response.error.code}): {LastStreamResolveErrorMessage}");
        }
        else
        {
            LastStreamResolveErrorMessage = "The backend did not return a playable stream.";
        }

        return null;
    }

    private string BuildUrl(string relativePath)
    {
        string baseUrl = BaseUrl?.TrimEnd('/') ?? string.Empty;
        string path = relativePath?.TrimStart('/') ?? string.Empty;
        return $"{baseUrl}/{path}";
    }

    private void LoadAuthToken()
    {
        if (!string.IsNullOrWhiteSpace(authToken))
        {
            authToken = authToken.Trim();
            return;
        }

        string envToken = Environment.GetEnvironmentVariable(JwtTokenEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(envToken))
        {
            authToken = envToken.Trim();
            Debug.Log($"[ApiManager] Loaded auth token from environment variable {JwtTokenEnvironmentVariable}.");
            return;
        }

        if (loadTokenFromPlayerPrefs && PlayerPrefs.HasKey(JwtTokenPlayerPrefsKey))
        {
            authToken = PlayerPrefs.GetString(JwtTokenPlayerPrefsKey)?.Trim() ?? string.Empty;
            Debug.Log("[ApiManager] Loaded auth token from PlayerPrefs.");
        }
    }

    private void TrySetAuthHeader(UnityWebRequest webRequest)
    {
        if (webRequest == null || string.IsNullOrWhiteSpace(authToken))
        {
            return;
        }

        webRequest.SetRequestHeader("Authorization", "Bearer " + authToken);
    }

    private async Task<T> SendGetRequest<T>(string url)
    {
        if (errorTextDisplay != null) errorTextDisplay.gameObject.SetActive(false);

        using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
        {
            TrySetAuthHeader(webRequest);
            
            var operation = webRequest.SendWebRequest();
            while (!operation.isDone) await Task.Yield();

            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                string jsonResponse = webRequest.downloadHandler.text;
                Debug.Log($"[ApiManager] Raw JSON: {jsonResponse}");
                
                try 
                {
                    // Thử parse theo cấu trúc ApiResponse<T>
                    return JsonUtility.FromJson<T>(jsonResponse);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[ApiManager] JsonUtility fail. Checking if it's a raw array: {ex.Message}");
                    
                    // Nếu là mảng JSON thuần túy (e.g. [{}, {}]), JsonUtility không parse được trực tiếp.
                    // Cần bọc lại để parse.
                    if (jsonResponse.TrimStart().StartsWith("["))
                    {
                        string wrappedJson = "{\"success\":true,\"data\":" + jsonResponse + "}";
                        return JsonUtility.FromJson<T>(wrappedJson);
                    }
                    throw;
                }
            }
            else
            {
                long statusCode = webRequest.responseCode;
                string responseBody = webRequest.downloadHandler?.text;
                Debug.LogError($"[ApiManager] Request failed: {webRequest.error} | Status: {statusCode} | URL: {url} | Body: {responseBody}");

                if (statusCode == 404)
                {
                    ShowError("API endpoint was not found (404). Check the BaseUrl and endpoint path.");
                }
                return default;
            }
        }
    }

    private async Task<string> SendGetRawRequest(string url)
    {
        if (errorTextDisplay != null) errorTextDisplay.gameObject.SetActive(false);

        using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
        {
            TrySetAuthHeader(webRequest);

            var operation = webRequest.SendWebRequest();
            while (!operation.isDone) await Task.Yield();

            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                string jsonResponse = webRequest.downloadHandler.text;
                Debug.Log($"[ApiManager] Raw JSON: {jsonResponse}");
                return jsonResponse;
            }

            long statusCode = webRequest.responseCode;
            string responseBody = webRequest.downloadHandler?.text;
            Debug.LogError($"[ApiManager] Request failed: {webRequest.error} | Status: {statusCode} | URL: {url} | Body: {responseBody}");

            if (statusCode == 404)
            {
                ShowError("API endpoint was not found (404). Check the BaseUrl and endpoint path.");
            }

            return null;
        }
    }

    private async Task<PostStatus> SendPostJsonRequest(string url, string jsonBody)
    {
        if (errorTextDisplay != null) errorTextDisplay.gameObject.SetActive(false);

        using (UnityWebRequest webRequest = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(jsonBody) ? "{}" : jsonBody);
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");

            TrySetAuthHeader(webRequest);

            var operation = webRequest.SendWebRequest();
            while (!operation.isDone) await Task.Yield();

            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                return PostStatus.Success;
            }

            long statusCode = webRequest.responseCode;
            string responseBody = webRequest.downloadHandler?.text;
            Debug.LogWarning($"[ApiManager] POST failed: {webRequest.error} | Status: {statusCode} | URL: {url} | Body: {responseBody}");
            return statusCode == 404 ? PostStatus.NotFound : PostStatus.Failed;
        }
    }

    private async Task<T> SendPostJsonRequestForResponse<T>(string url, string jsonBody)
    {
        if (errorTextDisplay != null) errorTextDisplay.gameObject.SetActive(false);

        using (UnityWebRequest webRequest = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(jsonBody) ? "{}" : jsonBody);
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");

            TrySetAuthHeader(webRequest);

            var operation = webRequest.SendWebRequest();
            while (!operation.isDone) await Task.Yield();

            string responseBody = webRequest.downloadHandler?.text;
            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                if (string.IsNullOrWhiteSpace(responseBody)) return default;

                try
                {
                    return JsonUtility.FromJson<T>(responseBody);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ApiManager] Parse POST response failed: {ex.Message} | URL: {url} | Body: {responseBody}");
                    return default;
                }
            }

            long statusCode = webRequest.responseCode;
            if (!string.IsNullOrWhiteSpace(responseBody))
            {
                try
                {
                    T parsedErrorResponse = JsonUtility.FromJson<T>(responseBody);
                    if (parsedErrorResponse != null)
                    {
                        return parsedErrorResponse;
                    }
                }
                catch
                {
                    // Ignore parse errors and continue with generic logging below.
                }
            }

            bool isStreamResolve = url.IndexOf("/api/vr/stream/resolve", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isStreamResolve)
            {
                Debug.Log($"[ApiManager] Stream resolve endpoint returned HTTP {statusCode}. Backend resolver may be missing ytdl/yt-dlp. Body: {responseBody}");
            }
            else
            {
                Debug.LogWarning($"[ApiManager] POST failed: {webRequest.error} | Status: {statusCode} | URL: {url} | Body: {responseBody}");
            }

            return default;
        }
    }

    private enum PostStatus
    {
        Success,
        NotFound,
        Failed
    }

    private List<LessonData> ParseLessonsFromJson(string jsonResponse)
    {
        if (string.IsNullOrWhiteSpace(jsonResponse)) return null;

        string trimmed = jsonResponse.TrimStart();

        // 1) Raw array: [{...}, {...}]
        if (trimmed.StartsWith("["))
        {
            try
            {
                RawListWrapper<LessonData> wrapped = JsonUtility.FromJson<RawListWrapper<LessonData>>("{\"data\":" + jsonResponse + "}");
                if (wrapped != null && wrapped.data != null) return wrapped.data;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ApiManager] Parse raw lesson array failed: {ex.Message}");
            }
        }

        // 2) Standard envelope: { success, data: [...] }
        try
        {
            ApiResponse<LessonData> apiResponse = JsonUtility.FromJson<ApiResponse<LessonData>>(jsonResponse);
            if (apiResponse != null && apiResponse.data != null && apiResponse.data.Count > 0)
            {
                return apiResponse.data;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ApiManager] Parse ApiResponse<LessonData> failed: {ex.Message}");
        }

        // 3) Alternative envelope: { lessons: [...] }
        try
        {
            LessonsEnvelope lessonsEnvelope = JsonUtility.FromJson<LessonsEnvelope>(jsonResponse);
            if (lessonsEnvelope != null && lessonsEnvelope.lessons != null && lessonsEnvelope.lessons.Count > 0)
            {
                return lessonsEnvelope.lessons;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ApiManager] Parse LessonsEnvelope failed: {ex.Message}");
        }

        // 4) Section-based envelope: { sections: [{ section/title/name, lessons|videos|items: [...] }] }
        try
        {
            SectionsEnvelope sectionsEnvelope = JsonUtility.FromJson<SectionsEnvelope>(jsonResponse);
            List<LessonData> flattened = FlattenSections(sectionsEnvelope != null ? sectionsEnvelope.sections : null);
            if (flattened != null && flattened.Count > 0)
            {
                return flattened;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ApiManager] Parse SectionsEnvelope failed: {ex.Message}");
        }

        Debug.LogWarning("[ApiManager] Lessons JSON parsed but no lesson items were found.");
        return new List<LessonData>();
    }

    private List<SectionData> ParseSectionsFromJson(string jsonResponse)
    {
        if (string.IsNullOrWhiteSpace(jsonResponse)) return null;

        string trimmed = jsonResponse.TrimStart();

        // 1) Raw array
        if (trimmed.StartsWith("["))
        {
            try
            {
                RawListWrapper<SectionData> wrapped = JsonUtility.FromJson<RawListWrapper<SectionData>>("{\"data\":" + jsonResponse + "}");
                if (wrapped != null && wrapped.data != null && wrapped.data.Count > 0 && HasAnyNestedLessons(wrapped.data))
                {
                    NormalizeSections(wrapped.data);
                    return wrapped.data;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ApiManager] Parse raw section array failed: {ex.Message}");
            }
        }

        // 2) { sections: [...] }
        try
        {
            SectionDataEnvelope envelope = JsonUtility.FromJson<SectionDataEnvelope>(jsonResponse);
            if (envelope != null && envelope.sections != null && envelope.sections.Count > 0 && HasAnyNestedLessons(envelope.sections))
            {
                NormalizeSections(envelope.sections);
                return envelope.sections;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ApiManager] Parse SectionsEnvelope root failed: {ex.Message}");
        }

        // 3) { data: { sections:[...] } }
        try
        {
            SectionDataDataEnvelope dataEnvelope = JsonUtility.FromJson<SectionDataDataEnvelope>(jsonResponse);
            if (dataEnvelope != null && dataEnvelope.data != null && dataEnvelope.data.sections != null && dataEnvelope.data.sections.Count > 0 && HasAnyNestedLessons(dataEnvelope.data.sections))
            {
                NormalizeSections(dataEnvelope.data.sections);
                return dataEnvelope.data.sections;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ApiManager] Parse SectionsDataEnvelope failed: {ex.Message}");
        }

        // 4) { success, data:[{section...}] }
        try
        {
            ApiResponse<SectionData> response = JsonUtility.FromJson<ApiResponse<SectionData>>(jsonResponse);
            if (response != null && response.data != null && response.data.Count > 0 && HasAnyNestedLessons(response.data))
            {
                NormalizeSections(response.data);
                return response.data;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ApiManager] Parse ApiResponse<SectionData> failed: {ex.Message}");
        }

        return null;
    }

    private void NormalizeSections(List<SectionData> sections)
    {
        if (sections == null) return;

        for (int i = 0; i < sections.Count; i++)
        {
            SectionData section = sections[i];
            if (section == null) continue;

            string sectionLabel = ResolveSectionDataLabel(section, i + 1);
            List<LessonData> sectionLessons = ResolveSectionLessons(section);

            if (sectionLessons == null) continue;

            foreach (LessonData lesson in sectionLessons)
            {
                if (lesson == null) continue;
                if (string.IsNullOrWhiteSpace(lesson.sectionTitle)) lesson.sectionTitle = sectionLabel;
                if (string.IsNullOrWhiteSpace(lesson.sectionName)) lesson.sectionName = sectionLabel;
                if (string.IsNullOrWhiteSpace(lesson.section)) lesson.section = sectionLabel;
            }
        }
    }

    private bool HasAnyNestedLessons(List<SectionData> sections)
    {
        if (sections == null || sections.Count == 0) return false;

        for (int i = 0; i < sections.Count; i++)
        {
            SectionData s = sections[i];
            if (s == null) continue;

            bool hasNested = (s.lessons != null && s.lessons.Count > 0)
                || (s.videos != null && s.videos.Count > 0)
                || (s.items != null && s.items.Count > 0);

            if (hasNested) return true;
        }

        return false;
    }

    private List<LessonData> ResolveSectionLessons(SectionData section)
    {
        if (section == null) return null;
        if (section.lessons != null && section.lessons.Count > 0) return section.lessons;
        if (section.videos != null && section.videos.Count > 0) return section.videos;
        if (section.items != null && section.items.Count > 0) return section.items;
        return null;
    }

    private string ResolveSectionDataLabel(SectionData section, int index)
    {
        if (section != null)
        {
            if (!string.IsNullOrWhiteSpace(section.sectionTitle)) return section.sectionTitle.Trim();
            if (!string.IsNullOrWhiteSpace(section.sectionName)) return section.sectionName.Trim();
            if (!string.IsNullOrWhiteSpace(section.section)) return section.section.Trim();
            if (!string.IsNullOrWhiteSpace(section.title)) return section.title.Trim();
            if (!string.IsNullOrWhiteSpace(section.name)) return section.name.Trim();
            if (!string.IsNullOrWhiteSpace(section.code)) return section.code.Trim();
        }

        return $"Section {index:D2}";
    }

    private List<LessonData> FlattenSections(List<SectionPayload> sections)
    {
        if (sections == null || sections.Count == 0) return null;

        List<LessonData> result = new List<LessonData>();
        for (int i = 0; i < sections.Count; i++)
        {
            SectionPayload section = sections[i];
            string sectionLabel = ResolveSectionLabel(section, i + 1);

            List<LessonData> source = null;
            if (section != null)
            {
                if (section.lessons != null && section.lessons.Count > 0) source = section.lessons;
                else if (section.videos != null && section.videos.Count > 0) source = section.videos;
                else if (section.items != null && section.items.Count > 0) source = section.items;
            }

            if (source == null) continue;

            foreach (LessonData lesson in source)
            {
                if (lesson == null) continue;

                if (string.IsNullOrWhiteSpace(lesson.sectionTitle)) lesson.sectionTitle = sectionLabel;
                if (string.IsNullOrWhiteSpace(lesson.sectionName)) lesson.sectionName = sectionLabel;
                if (string.IsNullOrWhiteSpace(lesson.section)) lesson.section = sectionLabel;

                result.Add(lesson);
            }
        }

        return result;
    }

    private string ResolveSectionLabel(SectionPayload section, int index)
    {
        if (section != null)
        {
            if (!string.IsNullOrWhiteSpace(section.sectionTitle)) return section.sectionTitle.Trim();
            if (!string.IsNullOrWhiteSpace(section.sectionName)) return section.sectionName.Trim();
            if (!string.IsNullOrWhiteSpace(section.section)) return section.section.Trim();
            if (!string.IsNullOrWhiteSpace(section.title)) return section.title.Trim();
            if (!string.IsNullOrWhiteSpace(section.name)) return section.name.Trim();
        }

        return $"Section {index:D2}";
    }

    [Serializable]
    private class RawListWrapper<T>
    {
        public List<T> data;
    }

    [Serializable]
    private class LessonsEnvelope
    {
        public List<LessonData> lessons;
    }

    [Serializable]
    private class SectionsEnvelope
    {
        public List<SectionPayload> sections;
    }

    [Serializable]
    private class SectionsDataEnvelope
    {
        public SectionsEnvelope data;
    }

    [Serializable]
    private class SectionDataEnvelope
    {
        public List<SectionData> sections;
    }

    [Serializable]
    private class SectionDataDataEnvelope
    {
        public SectionDataEnvelope data;
    }

    [Serializable]
    private class SectionPayload
    {
        public string sectionTitle;
        public string sectionName;
        public string section;
        public string title;
        public string name;
        public List<LessonData> lessons;
        public List<LessonData> videos;
        public List<LessonData> items;
    }

    [Serializable]
    private class ProgressUpdateRequest
    {
        public string lessonId;
        public string video;
        public bool completed;
    }

    private void ShowError(string message)
    {
        if (errorTextDisplay != null)
        {
            errorTextDisplay.text = message;
            errorTextDisplay.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// Tải ảnh mượt mà hơn bằng cách sử dụng Raw Bytes + LoadImage (Hỗ trợ tốt nhất cho JPG/PNG)
    /// </summary>
    public async Task<Texture2D> DownloadImageAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;

        // 1. CHUYỂN ĐỔI URL CLOUDINARY SANG JPG (Ép Cloudinary trả về JPG thay vì WebP)
        string processedUrl = ConvertCloudinaryUrlToJpg(url);
        
        string fullUrl = processedUrl.StartsWith("http") ? processedUrl : $"{BaseUrl}{processedUrl}";
        Debug.Log($"[ApiManager] Đang tải ảnh: {fullUrl}");

        // 2. Sử dụng UnityWebRequest thường để lấy mảng byte
        using (UnityWebRequest webRequest = UnityWebRequest.Get(fullUrl))
        {
            TrySetAuthHeader(webRequest);

            var operation = webRequest.SendWebRequest();
            while (!operation.isDone) await Task.Yield();

            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                byte[] results = webRequest.downloadHandler.data;

                // 3. Sử dụng LoadImage để giải mã dữ liệu ảnh (Xử lý được hầu hết định dạng JPG/PNG)
                Texture2D texture = new Texture2D(2, 2);
                if (texture.LoadImage(results)) 
                {
                    return texture;
                }
                else
                {
                    Debug.LogError($"[ApiManager] LoadImage thất bại cho URL: {fullUrl}");
                    return null;
                }
            }
            else
            {
                Debug.LogError($"[ApiManager] Lỗi tải dữ liệu ảnh: {webRequest.error} | URL: {fullUrl}");
                return null;
            }
        }
    }

    /// <summary>
    /// Helper: Ép buộc Cloudinary trả về định dạng JPG để Unity dễ xử lý
    /// </summary>
    private string ConvertCloudinaryUrlToJpg(string url)
    {
        if (string.IsNullOrEmpty(url) || !url.Contains("cloudinary.com")) return url;

        string newUrl = url;

        // Xử lý đổi đuôi file sang .jpg nếu đang là .webp hoặc .jpeg
        if (newUrl.ToLower().EndsWith(".webp") || newUrl.ToLower().EndsWith(".jpeg"))
        {
            int lastDot = newUrl.LastIndexOf('.');
            newUrl = newUrl.Substring(0, lastDot) + ".jpg";
        }

        // Chèn tham số f_jpg vào sau /upload/ để Cloudinary thực hiện convert
        string searchTag = "/upload/";
        int index = newUrl.IndexOf(searchTag);
        if (index != -1)
        {
            int insertIndex = index + searchTag.Length;
            if (!newUrl.Contains("/f_jpg/"))
            {
                newUrl = newUrl.Insert(insertIndex, "f_jpg/");
            }
        }

        return newUrl;
    }
    }
