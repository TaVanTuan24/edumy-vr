using UnityEngine;
using UnityEngine.Networking;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using TMPro;

public class ApiManager : MonoBehaviour
{
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
    public string authToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1c2VySWQiOiI2OGE2OWIwYTA1NTA3MWI3ZTQ0MTBiOGYiLCJlbWFpbCI6ImFAZ2cuY29tIiwicm9sZSI6bnVsbCwiaWF0IjoxNzc1MzYyMTU0LCJleHAiOjE3Nzc5NTQxNTR9.QwtpOrEt_UaTFXZQLLE_QXZW6DGKFSMflrUH_17QHeg";

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI errorTextDisplay; 
    [SerializeField] private TextMeshProUGUI debugUrlText;    

    public string BaseUrl => useProduction ? productionUrl : localUrl;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Tải token từ PlayerPrefs nếu có (fallback), nếu không sẽ dùng token mặc định ở trên
            if (PlayerPrefs.HasKey("JWT_TOKEN"))
            {
                authToken = PlayerPrefs.GetString("JWT_TOKEN");
                Debug.Log("[ApiManager] Đã tải Token từ PlayerPrefs.");
            }
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // Log thông tin khởi tạo theo yêu cầu
        Debug.Log($"ApiManager initialized with JWT token and baseUrl: {BaseUrl}");
        
        if (debugUrlText != null) 
            debugUrlText.text = $"API: {BaseUrl}";
    }

    public void SetAuthToken(string token)
    {
        authToken = token;
        PlayerPrefs.SetString("JWT_TOKEN", token);
        PlayerPrefs.Save();
        Debug.Log("[ApiManager] Đã cập nhật và lưu Token mới.");
    }

    public async Task<List<CourseData>> GetCoursesAsync()
    {
        string url = $"{BaseUrl}/api/vr/courses";
        var response = await SendGetRequest<ApiResponse<CourseData>>(url);
        return response?.data;
    }

    public async Task<List<LessonData>> GetLessonsAsync(string courseId)
    {
        string url = $"{BaseUrl}/api/vr/courses/{courseId}/lessons";
        var response = await SendGetRequest<ApiResponse<LessonData>>(url);
        return response?.data;
    }

    private async Task<T> SendGetRequest<T>(string url)
    {
        if (errorTextDisplay != null) errorTextDisplay.gameObject.SetActive(false);

        using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
        {
            // Tự động thêm Authorization header vào mỗi request
            if (!string.IsNullOrEmpty(authToken))
            {
                webRequest.SetRequestHeader("Authorization", "Bearer " + authToken);
            }
            
            var operation = webRequest.SendWebRequest();

            while (!operation.isDone)
                await Task.Yield();

            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                string jsonResponse = webRequest.downloadHandler.text;
                return JsonUtility.FromJson<T>(jsonResponse);
            }
            else
            {
                long responseCode = webRequest.responseCode;
                string errorDetail = webRequest.error;
                
                Debug.LogError($"[ApiManager] Lỗi API ({responseCode}): {errorDetail} | URL: {url}");

                // XỬ LÝ LỖI 401 UNAUTHORIZED
                if (responseCode == 401)
                {
                    ShowError("401 Unauthorized - Token không hợp lệ hoặc đã hết hạn.\nVui lòng kiểm tra lại authToken trong ApiManager.");
                }
                else if (errorDetail.Contains("Cannot resolve destination host") || webRequest.result == UnityWebRequest.Result.ConnectionError)
                {
                    ShowError("Không thể kết nối đến máy chủ.\nVui lòng kiểm tra Base URL hoặc chạy backend locally.");
                }
                else
                {
                    ShowError($"Lỗi hệ thống ({responseCode}): {errorDetail}");
                }

                return default;
            }
        }
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
            if (!string.IsNullOrEmpty(authToken))
            {
                webRequest.SetRequestHeader("Authorization", "Bearer " + authToken);
            }

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
