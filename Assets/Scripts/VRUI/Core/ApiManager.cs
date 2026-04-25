using UnityEngine;
using UnityEngine.Networking;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using TMPro;

public class ApiManager : MonoBehaviour
{
    private const int MinimumRequestTimeoutSeconds = 5;
    private const int DefaultRequestTimeoutSeconds = 15;
    private static readonly string[] ConnectionTestEndpoints = { "api/health", "health", string.Empty };

    public static ApiManager Instance { get; private set; }

    [Header("Networking")]
    [SerializeField, Min(MinimumRequestTimeoutSeconds)] private int requestTimeoutSeconds = DefaultRequestTimeoutSeconds;
    [SerializeField] private bool enableVerboseLogs;

    [Header("Authentication")]
    [Tooltip("JWT Token dùng để xác thực. Token này sẽ được gửi tự động trong header Authorization.")]
    [SerializeField] private string authToken = string.Empty;
    [SerializeField] private bool loadTokenFromPlayerPrefs;
    [SerializeField] private bool persistTokenInPlayerPrefs;

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI errorTextDisplay; 

    public string BaseUrl => ApiConfig.BaseUrl;
    public bool HasAuthToken => !string.IsNullOrWhiteSpace(authToken);
    private bool lessonProgressSyncDisabled;
    private bool lessonProgressSyncDisableLogged;
    public string LastStreamResolveErrorCode { get; private set; }
    public string LastStreamResolveErrorMessage { get; private set; }
    public long LastResponseStatusCode { get; private set; }

    public class ConnectionTestResult
    {
        public bool success;
        public string testedBaseUrl;
        public string testedEndpoint;
        public string testedUrl;
        public long statusCode;
        public string message;
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadAuthToken();
            AppStateManager.Instance.SetBackendStatus(BackendConnectionState.Unknown, baseUrl: BaseUrl);
            
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void SetAuthToken(string token)
    {
        authToken = string.IsNullOrWhiteSpace(token) ? string.Empty : token.Trim();
        if (!persistTokenInPlayerPrefs)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(authToken))
        {
            PlayerPrefs.SetString(VRSessionKeys.AccessToken, authToken);
            if (PlayerPrefs.HasKey(VRSessionKeys.LegacyJwtToken))
            {
                PlayerPrefs.DeleteKey(VRSessionKeys.LegacyJwtToken);
            }
            PlayerPrefs.Save();
        }
        else
        {
            bool changed = false;
            if (PlayerPrefs.HasKey(VRSessionKeys.AccessToken))
            {
                PlayerPrefs.DeleteKey(VRSessionKeys.AccessToken);
                changed = true;
            }

            if (PlayerPrefs.HasKey(VRSessionKeys.LegacyJwtToken))
            {
                PlayerPrefs.DeleteKey(VRSessionKeys.LegacyJwtToken);
                changed = true;
            }

            if (changed)
            {
                PlayerPrefs.Save();
            }
        }
    }

    public void ClearAuthToken()
    {
        authToken = string.Empty;

        bool changed = false;
        if (PlayerPrefs.HasKey(VRSessionKeys.AccessToken))
        {
            PlayerPrefs.DeleteKey(VRSessionKeys.AccessToken);
            changed = true;
        }

        if (PlayerPrefs.HasKey(VRSessionKeys.LegacyJwtToken))
        {
            PlayerPrefs.DeleteKey(VRSessionKeys.LegacyJwtToken);
            changed = true;
        }

        if (changed)
        {
            PlayerPrefs.Save();
        }
    }

    public async Task<List<CourseData>> GetCoursesAsync()
    {
        // Đồng bộ endpoint theo cùng pattern với lessons
        string url = BuildUrl("api/vr/courses");
        LogVerbose($"[ApiManager] Fetching courses from: {url}");
        var response = await SendGetRequest<ApiResponse<CourseData>>(url);
        return response?.data;
    }

    public async Task<List<LessonData>> GetLessonsAsync(string courseId)
    {
        string url = BuildUrl($"api/vr/courses/{courseId}/lessons");
        string jsonResponse = await SendGetRawRequest(url);
        if (string.IsNullOrWhiteSpace(jsonResponse)) return null;

        List<LessonData> lessons = ParseLessonsFromJson(jsonResponse);
        LogVerbose($"[ApiManager] Parsed lessons count: {(lessons == null ? 0 : lessons.Count)}");
        return lessons;
    }

    public async Task<List<SectionData>> GetCourseSectionsAsync(string courseId)
    {
        string url = BuildUrl($"api/vr/courses/{courseId}/lessons");
        string jsonResponse = await SendGetRawRequest(url);
        if (string.IsNullOrWhiteSpace(jsonResponse)) return null;

        List<SectionData> sections = ParseSectionsFromJson(jsonResponse);
        LogVerbose($"[ApiManager] Parsed sections count: {(sections == null ? 0 : sections.Count)}");
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
            LogVerbose($"[ApiManager] Stream resolve failed ({response.error.code}): {LastStreamResolveErrorMessage}");
        }
        else
        {
            LastStreamResolveErrorMessage = "The backend did not return a playable stream.";
        }

        return null;
    }

    public async Task<VRLoginCodeResponse> RequestVrLoginCodeAsync(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return new VRLoginCodeResponse
            {
                success = false,
                message = "Device ID is required."
            };
        }

        if (!TryValidateVrAuthRequest(out string validationError))
        {
            return CreateLoginCodeErrorResponse(validationError);
        }

        string url = BuildUrl("api/vr-auth/request-code");
        VRLoginCodeRequest payload = new VRLoginCodeRequest
        {
            deviceId = deviceId.Trim()
        };

        VRLoginCodeResponse response = await SendPostJsonRequestForResponse<VRLoginCodeResponse>(url, JsonUtility.ToJson(payload));
        if (response != null)
        {
            if (!response.success && string.IsNullOrWhiteSpace(response.message))
            {
                response.message = BuildVrAuthFailureMessage("Unable to request a login code.");
            }

            return response;
        }

        return CreateLoginCodeErrorResponse(BuildVrAuthFailureMessage("Unable to request a login code."));
    }

    public async Task<VRLoginPollResponse> PollVrLoginStatusAsync(string code, string deviceId)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(deviceId))
        {
            return new VRLoginPollResponse
            {
                success = false,
                status = "expired",
                message = "Missing pairing code or device ID."
            };
        }

        if (!TryValidateVrAuthRequest(out string validationError))
        {
            return CreateLoginPollErrorResponse("error", validationError);
        }

        string url = BuildUrl(
            $"api/vr-auth/poll/{UnityWebRequest.EscapeURL(code.Trim())}?deviceId={UnityWebRequest.EscapeURL(deviceId.Trim())}"
        );

        VRLoginPollResponse response = await SendGetRequestForObject<VRLoginPollResponse>(url);
        if (response != null)
        {
            if (!response.success && string.IsNullOrWhiteSpace(response.message) && LastResponseStatusCode != 404 && LastResponseStatusCode != 410)
            {
                response.message = BuildVrAuthFailureMessage("Unable to verify the login code yet.");
            }

            return response;
        }

        return CreateLoginPollErrorResponse("error", BuildVrAuthFailureMessage("Unable to verify the login code yet."));
    }

    private string BuildUrl(string relativePath)
    {
        return ApiConfig.BuildUrl(relativePath);
    }

    private bool TryValidateVrAuthRequest(out string errorMessage)
    {
        errorMessage = null;

        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            errorMessage = "No network connection is available on this device.";
            return false;
        }

        string baseUrl = BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            errorMessage = "The API base URL is not configured.";
            return false;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri uri))
        {
            errorMessage = $"The API base URL is invalid: {baseUrl}";
            return false;
        }

        string host = uri.Host ?? string.Empty;
        bool isLoopbackHost =
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase);

        if (Application.platform == RuntimePlatform.Android && isLoopbackHost)
        {
            errorMessage = "Quest cannot reach localhost. Open Settings and update Backend Server URL to your PC LAN IP, for example http://192.168.1.10:3000.";
            return false;
        }

        return true;
    }

    private void ApplyRequestDefaults(UnityWebRequest webRequest)
    {
        if (webRequest == null)
        {
            return;
        }

        webRequest.timeout = Mathf.Max(MinimumRequestTimeoutSeconds, requestTimeoutSeconds);
        ApplyAuthorizationHeader(webRequest);
    }

    private VRLoginCodeResponse CreateLoginCodeErrorResponse(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            ShowError(message);
        }

        return new VRLoginCodeResponse
        {
            success = false,
            message = string.IsNullOrWhiteSpace(message) ? "Unable to request a login code." : message
        };
    }

    private VRLoginPollResponse CreateLoginPollErrorResponse(string status, string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            ShowError(message);
        }

        return new VRLoginPollResponse
        {
            success = false,
            status = string.IsNullOrWhiteSpace(status) ? "error" : status,
            message = string.IsNullOrWhiteSpace(message) ? "Unable to verify the login code yet." : message
        };
    }

    private string BuildVrAuthFailureMessage(string fallbackMessage)
    {
        if (LastResponseStatusCode == 404)
        {
            return "The VR auth endpoint was not found. Verify the backend route and API URL.";
        }

        if (LastResponseStatusCode == 429)
        {
            return "Too many requests. Please wait a moment and try again.";
        }

        if (LastResponseStatusCode == 0)
        {
            return $"Cannot connect to backend at {BaseUrl}. Open Settings and update Backend Server URL, then test the connection again.";
        }

        if (LastResponseStatusCode >= 500)
        {
            return "The backend returned a server error while processing VR login.";
        }

        return fallbackMessage;
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(string baseUrlOverride = null)
    {
        string baseUrlToTest = string.IsNullOrWhiteSpace(baseUrlOverride) ? BaseUrl : ApiConfig.NormalizeBaseUrl(baseUrlOverride);
        if (!ApiConfig.TryNormalizeBaseUrl(baseUrlToTest, out string normalizedBaseUrl, out string errorMessage))
        {
            AppStateManager.Instance.SetBackendStatus(BackendConnectionState.Unreachable, errorMessage, baseUrlToTest);
            return new ConnectionTestResult
            {
                success = false,
                testedBaseUrl = baseUrlToTest,
                testedUrl = baseUrlToTest,
                message = errorMessage
            };
        }

        ConnectionTestResult lastHttpResponse = null;
        for (int i = 0; i < ConnectionTestEndpoints.Length; i++)
        {
            string endpoint = ConnectionTestEndpoints[i];
            ConnectionTestResult probe = await ProbeConnectionEndpointAsync(normalizedBaseUrl, endpoint);
            if (probe.success)
            {
                AppStateManager.Instance.SetBackendStatus(BackendConnectionState.Connected, probe.message, normalizedBaseUrl);
                return probe;
            }

            if (probe.statusCode > 0)
            {
                lastHttpResponse = probe;
            }
        }

        ConnectionTestResult failure = lastHttpResponse ?? new ConnectionTestResult
        {
            success = false,
            testedBaseUrl = normalizedBaseUrl,
            testedUrl = normalizedBaseUrl,
            message = $"Cannot connect to server at {normalizedBaseUrl}."
        };
        AppStateManager.Instance.SetBackendStatus(BackendConnectionState.Unreachable, failure.message, normalizedBaseUrl);
        return failure;
    }

    private void LoadAuthToken()
    {
        authToken = string.IsNullOrWhiteSpace(authToken) ? string.Empty : authToken.Trim();
        if (!string.IsNullOrWhiteSpace(authToken))
        {
            return;
        }

        if (!loadTokenFromPlayerPrefs)
        {
            return;
        }

        string storedToken = string.Empty;
        if (PlayerPrefs.HasKey(VRSessionKeys.AccessToken))
        {
            storedToken = PlayerPrefs.GetString(VRSessionKeys.AccessToken)?.Trim() ?? string.Empty;
        }
        else if (PlayerPrefs.HasKey(VRSessionKeys.LegacyJwtToken))
        {
            storedToken = PlayerPrefs.GetString(VRSessionKeys.LegacyJwtToken)?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(storedToken))
            {
                PlayerPrefs.SetString(VRSessionKeys.AccessToken, storedToken);
            }
            PlayerPrefs.DeleteKey(VRSessionKeys.LegacyJwtToken);
            PlayerPrefs.Save();
        }

        if (!string.IsNullOrWhiteSpace(storedToken))
        {
            authToken = storedToken;
            LogVerbose("[ApiManager] Loaded auth token from PlayerPrefs.");
        }
    }

    public void ApplyAuthorizationHeader(UnityWebRequest webRequest)
    {
        if (webRequest == null || string.IsNullOrWhiteSpace(authToken))
        {
            return;
        }

        webRequest.SetRequestHeader("Authorization", "Bearer " + authToken);
    }

    private async Task<ConnectionTestResult> ProbeConnectionEndpointAsync(string normalizedBaseUrl, string endpoint)
    {
        string url = ApiConfig.BuildUrl(endpoint, normalizedBaseUrl);
        using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
        {
            webRequest.timeout = Mathf.Max(MinimumRequestTimeoutSeconds, requestTimeoutSeconds);

            var operation = webRequest.SendWebRequest();
            while (!operation.isDone) await Task.Yield();

            long statusCode = webRequest.responseCode;
            string endpointLabel = string.IsNullOrWhiteSpace(endpoint) ? "/" : "/" + endpoint.TrimStart('/');
            bool hasHttpResponse = statusCode > 0;
            bool reachable = hasHttpResponse && statusCode < 500;

            if (webRequest.result == UnityWebRequest.Result.Success || reachable)
            {
                return new ConnectionTestResult
                {
                    success = true,
                    testedBaseUrl = normalizedBaseUrl,
                    testedEndpoint = endpointLabel,
                    testedUrl = url,
                    statusCode = statusCode,
                    message = reachable && webRequest.result != UnityWebRequest.Result.Success
                        ? $"Connected to {normalizedBaseUrl} (HTTP {statusCode} from {endpointLabel})."
                        : $"Connected to {normalizedBaseUrl} using {endpointLabel}."
                };
            }

            return new ConnectionTestResult
            {
                success = false,
                testedBaseUrl = normalizedBaseUrl,
                testedEndpoint = endpointLabel,
                testedUrl = url,
                statusCode = statusCode,
                message = $"Cannot connect to backend at {normalizedBaseUrl}. Endpoint {endpointLabel} failed: {webRequest.error}"
            };
        }
    }

    private async Task<T> SendGetRequest<T>(string url)
    {
        if (errorTextDisplay != null) errorTextDisplay.gameObject.SetActive(false);
        LastResponseStatusCode = 0;

        using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
        {
            ApplyRequestDefaults(webRequest);
            
            var operation = webRequest.SendWebRequest();
            while (!operation.isDone) await Task.Yield();

            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                LastResponseStatusCode = webRequest.responseCode;
                PublishBackendStatusForResponse(webRequest.responseCode, null, url);
                string jsonResponse = webRequest.downloadHandler.text;
                LogVerbose($"[ApiManager] Raw JSON: {jsonResponse}");
                
                try 
                {
                    // Thử parse theo cấu trúc ApiResponse<T>
                    return JsonUtility.FromJson<T>(jsonResponse);
                }
                catch (System.Exception ex)
                {
                    LogVerbose($"[ApiManager] JsonUtility fail. Checking if it's a raw array: {ex.Message}");
                    
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
                LastResponseStatusCode = statusCode;
                HandleUnauthorizedResponse(statusCode);
                string responseBody = webRequest.downloadHandler?.text;
                PublishBackendStatusForResponse(statusCode, webRequest.error, url);
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
        LastResponseStatusCode = 0;

        using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
        {
            ApplyRequestDefaults(webRequest);

            var operation = webRequest.SendWebRequest();
            while (!operation.isDone) await Task.Yield();

            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                LastResponseStatusCode = webRequest.responseCode;
                PublishBackendStatusForResponse(webRequest.responseCode, null, url);
                string jsonResponse = webRequest.downloadHandler.text;
                LogVerbose($"[ApiManager] Raw JSON: {jsonResponse}");
                return jsonResponse;
            }

            long statusCode = webRequest.responseCode;
            LastResponseStatusCode = statusCode;
            HandleUnauthorizedResponse(statusCode);
            string responseBody = webRequest.downloadHandler?.text;
            PublishBackendStatusForResponse(statusCode, webRequest.error, url);
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
        LastResponseStatusCode = 0;

        using (UnityWebRequest webRequest = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(jsonBody) ? "{}" : jsonBody);
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");

            ApplyRequestDefaults(webRequest);

            var operation = webRequest.SendWebRequest();
            while (!operation.isDone) await Task.Yield();

            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                LastResponseStatusCode = webRequest.responseCode;
                PublishBackendStatusForResponse(webRequest.responseCode, null, url);
                return PostStatus.Success;
            }

            long statusCode = webRequest.responseCode;
            LastResponseStatusCode = statusCode;
            HandleUnauthorizedResponse(statusCode);
            string responseBody = webRequest.downloadHandler?.text;
            PublishBackendStatusForResponse(statusCode, webRequest.error, url);
            Debug.LogWarning($"[ApiManager] POST failed: {webRequest.error} | Status: {statusCode} | URL: {url} | Body: {responseBody}");
            return statusCode == 404 ? PostStatus.NotFound : PostStatus.Failed;
        }
    }

    private async Task<T> SendGetRequestForObject<T>(string url)
    {
        if (errorTextDisplay != null) errorTextDisplay.gameObject.SetActive(false);
        LastResponseStatusCode = 0;

        using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
        {
            ApplyRequestDefaults(webRequest);

            var operation = webRequest.SendWebRequest();
            while (!operation.isDone) await Task.Yield();

            string responseBody = webRequest.downloadHandler?.text;
            LastResponseStatusCode = webRequest.responseCode;

            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                PublishBackendStatusForResponse(LastResponseStatusCode, null, url);
                if (string.IsNullOrWhiteSpace(responseBody)) return default;

                try
                {
                    return JsonUtility.FromJson<T>(responseBody);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ApiManager] Parse GET response failed: {ex.Message} | URL: {url} | Body: {responseBody}");
                    return default;
                }
            }

            HandleUnauthorizedResponse(LastResponseStatusCode);
            PublishBackendStatusForResponse(LastResponseStatusCode, webRequest.error, url);
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

            Debug.LogWarning($"[ApiManager] GET failed: {webRequest.error} | Status: {LastResponseStatusCode} | URL: {url} | Body: {responseBody}");
            return default;
        }
    }

    private async Task<T> SendPostJsonRequestForResponse<T>(string url, string jsonBody)
    {
        if (errorTextDisplay != null) errorTextDisplay.gameObject.SetActive(false);
        LastResponseStatusCode = 0;

        using (UnityWebRequest webRequest = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(jsonBody) ? "{}" : jsonBody);
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");

            ApplyRequestDefaults(webRequest);

            var operation = webRequest.SendWebRequest();
            while (!operation.isDone) await Task.Yield();

            string responseBody = webRequest.downloadHandler?.text;
            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                LastResponseStatusCode = webRequest.responseCode;
                PublishBackendStatusForResponse(webRequest.responseCode, null, url);
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
            LastResponseStatusCode = statusCode;
            HandleUnauthorizedResponse(statusCode);
            PublishBackendStatusForResponse(statusCode, webRequest.error, url);
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
                LogVerbose($"[ApiManager] Stream resolve endpoint returned HTTP {statusCode}. Backend resolver may be missing ytdl/yt-dlp. Body: {responseBody}");
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
        
        string fullUrl = ApiConfig.IsAbsoluteUrl(processedUrl) ? processedUrl : ApiConfig.BuildUrl(processedUrl);
        LogVerbose($"[ApiManager] Loading image: {fullUrl}");

        // 2. Sử dụng UnityWebRequest thường để lấy mảng byte
        using (UnityWebRequest webRequest = UnityWebRequest.Get(fullUrl))
        {
            ApplyRequestDefaults(webRequest);

            var operation = webRequest.SendWebRequest();
            while (!operation.isDone) await Task.Yield();

            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                PublishBackendStatusForResponse(webRequest.responseCode, null, fullUrl);
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
                HandleUnauthorizedResponse(webRequest.responseCode);
                PublishBackendStatusForResponse(webRequest.responseCode, webRequest.error, fullUrl);
                Debug.LogError($"[ApiManager] Lỗi tải dữ liệu ảnh: {webRequest.error} | URL: {fullUrl}");
                return null;
            }
        }
    }

    private void HandleUnauthorizedResponse(long statusCode)
    {
        if (statusCode != 401 || string.IsNullOrWhiteSpace(authToken))
        {
            return;
        }

        ClearAuthToken();
        AppStateManager.Instance.SetBackendStatus(BackendConnectionState.Unauthorized, "Backend authorization failed.", BaseUrl);

        VRAuthManager authManager = FindAnyObjectByType<VRAuthManager>();
        if (authManager != null)
        {
            authManager.HandleUnauthorizedSession();
        }
    }

    private void LogVerbose(string message)
    {
        if ((!enableVerboseLogs && !AppStateManager.IsVerboseLoggingEnabled) || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Debug.Log(message);
    }

    private void PublishBackendStatusForResponse(long statusCode, string requestError, string url)
    {
        BackendConnectionState state = BackendConnectionState.Unknown;
        string message = string.Empty;

        if (statusCode == 401)
        {
            state = BackendConnectionState.Unauthorized;
            message = "Backend authorization failed.";
        }
        else if (statusCode == 0)
        {
            state = BackendConnectionState.Unreachable;
            message = string.IsNullOrWhiteSpace(requestError)
                ? $"Cannot connect to backend at {BaseUrl}."
                : $"Cannot connect to backend at {BaseUrl}. {requestError}";
        }
        else
        {
            state = BackendConnectionState.Connected;
            message = $"Backend reachable via {url}";
        }

        AppStateManager.Instance.SetBackendStatus(state, message, BaseUrl);
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
