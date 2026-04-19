using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class VRAuthManager : MonoBehaviour
{
    private const string DeviceIdPlayerPrefsKey = "VR_DEVICE_ID";
    private const string AccessTokenPlayerPrefsKey = "VR_ACCESS_TOKEN";
    private const string UserIdPlayerPrefsKey = "VR_USER_ID";
    private const string UsernamePlayerPrefsKey = "VR_USERNAME";
    private const int DefaultCodeExpirySeconds = 120;
    private const int PollIntervalMilliseconds = 2000;

    private CancellationTokenSource pollingCancellation;
    private bool initialized;
    private bool lastNotifiedAuthState;
    private int lastBroadcastRemainingSeconds = -1;
    private string deviceId = string.Empty;
    private string accessToken = string.Empty;
    private string userId = string.Empty;
    private string username = string.Empty;
    private string currentCode = string.Empty;
    private string statusMessage = "Not logged in.";
    private DateTime codeExpiresAtUtc = DateTime.MinValue;

    public event Action StateUpdated;
    public event Action<bool> AuthenticationChanged;

    public string DeviceId => deviceId;
    public string AccessToken => accessToken;
    public string UserId => userId;
    public string Username => username;
    public string CurrentCode => currentCode;
    public string StatusMessage => statusMessage;
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(accessToken);
    public bool IsPending => !IsAuthenticated && !string.IsNullOrWhiteSpace(currentCode);
    public int RemainingSeconds
    {
        get
        {
            if (!IsPending) return 0;
            return Math.Max(0, (int)Math.Ceiling((codeExpiresAtUtc - DateTime.UtcNow).TotalSeconds));
        }
    }

    private void Awake()
    {
        EnsureDeviceId();
        lastNotifiedAuthState = IsAuthenticated;
    }

    private void Update()
    {
        if (!IsPending)
        {
            return;
        }

        int remainingSeconds = RemainingSeconds;
        if (remainingSeconds <= 0)
        {
            HandleExpiredCode("Login code expired. Request a new one.");
            return;
        }

        if (remainingSeconds != lastBroadcastRemainingSeconds)
        {
            lastBroadcastRemainingSeconds = remainingSeconds;
            NotifyStateUpdated();
        }
    }

    private void OnDisable()
    {
        StopPolling();
    }

    private void OnDestroy()
    {
        StopPolling();
    }

    public void Initialize()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        EnsureDeviceId();
        LoadSavedSession();
    }

    public void LoadSavedSession()
    {
        EnsureDeviceId();

        string storedToken = PlayerPrefs.GetString(AccessTokenPlayerPrefsKey, string.Empty)?.Trim() ?? string.Empty;
        string storedUserId = PlayerPrefs.GetString(UserIdPlayerPrefsKey, string.Empty)?.Trim() ?? string.Empty;
        string storedUsername = PlayerPrefs.GetString(UsernamePlayerPrefsKey, string.Empty)?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(storedToken))
        {
            accessToken = string.Empty;
            userId = string.Empty;
            username = string.Empty;
            currentCode = string.Empty;
            codeExpiresAtUtc = DateTime.MinValue;
            statusMessage = "Not logged in.";

            if (ApiManager.Instance != null)
            {
                ApiManager.Instance.ClearAuthToken();
            }

            NotifyStateUpdated();
            return;
        }

        accessToken = storedToken;
        userId = storedUserId;
        username = storedUsername;
        currentCode = string.Empty;
        codeExpiresAtUtc = DateTime.MinValue;
        statusMessage = string.IsNullOrWhiteSpace(username)
            ? "Saved VR session restored."
            : $"Logged in as {username}.";

        if (ApiManager.Instance != null)
        {
            ApiManager.Instance.SetAuthToken(accessToken);
        }

        NotifyStateUpdated();
    }

    public async Task RequestLoginCode()
    {
        if (IsAuthenticated)
        {
            statusMessage = string.IsNullOrWhiteSpace(username)
                ? "You are already logged in."
                : $"Already logged in as {username}.";
            NotifyStateUpdated();
            return;
        }

        EnsureDeviceId();
        StopPolling();
        ResetPairingCode();

        ApiManager apiManager = ApiManager.Instance;
        if (apiManager == null)
        {
            statusMessage = "API manager was not found in the scene.";
            NotifyStateUpdated();
            return;
        }

        statusMessage = "Requesting login code...";
        NotifyStateUpdated();

        VRLoginCodeResponse response = null;
        try
        {
            response = await apiManager.RequestVrLoginCodeAsync(deviceId);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VRAuthManager] RequestLoginCode failed: {ex.Message}");
        }

        if (response == null || !response.success || string.IsNullOrWhiteSpace(response.code))
        {
            statusMessage = response != null && !string.IsNullOrWhiteSpace(response.message)
                ? response.message
                : "Unable to request a login code.";
            NotifyStateUpdated();
            return;
        }

        currentCode = response.code.Trim();
        codeExpiresAtUtc = DateTime.UtcNow.AddSeconds(response.expiresIn > 0 ? response.expiresIn : DefaultCodeExpirySeconds);
        statusMessage = "Waiting for approval...";
        lastBroadcastRemainingSeconds = -1;
        Debug.Log($"[VRAuthManager] Login code received: {currentCode}");
        NotifyStateUpdated();
        StartPolling();
    }

    public Task RefreshLoginCode()
    {
        return RequestLoginCode();
    }

    public void HandleUnauthorizedSession(string message = "Your VR session expired. Please log in again.")
    {
        Logout(message);
    }

    public void Logout(string message = "You have been logged out.")
    {
        StopPolling();
        ResetPairingCode();

        accessToken = string.Empty;
        userId = string.Empty;
        username = string.Empty;
        statusMessage = message;

        PlayerPrefs.DeleteKey(AccessTokenPlayerPrefsKey);
        PlayerPrefs.DeleteKey(UserIdPlayerPrefsKey);
        PlayerPrefs.DeleteKey(UsernamePlayerPrefsKey);
        PlayerPrefs.Save();

        if (ApiManager.Instance != null)
        {
            ApiManager.Instance.ClearAuthToken();
        }

        NotifyStateUpdated();
    }

    private void EnsureDeviceId()
    {
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            return;
        }

        string stored = PlayerPrefs.GetString(DeviceIdPlayerPrefsKey, string.Empty)?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(stored))
        {
            deviceId = stored;
            return;
        }

        deviceId = Guid.NewGuid().ToString("N");
        PlayerPrefs.SetString(DeviceIdPlayerPrefsKey, deviceId);
        PlayerPrefs.Save();
    }

    private void StartPolling()
    {
        StopPolling();
        pollingCancellation = new CancellationTokenSource();
        _ = PollLoginStatusLoopAsync(pollingCancellation.Token);
    }

    private void StopPolling()
    {
        if (pollingCancellation == null)
        {
            return;
        }

        pollingCancellation.Cancel();
        pollingCancellation.Dispose();
        pollingCancellation = null;
    }

    private async Task PollLoginStatusLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && IsPending)
        {
            if (RemainingSeconds <= 0)
            {
                HandleExpiredCode("Login code expired. Request a new one.");
                return;
            }

            ApiManager apiManager = ApiManager.Instance;
            if (apiManager == null)
            {
                statusMessage = "API manager was not found in the scene.";
                NotifyStateUpdated();
                return;
            }

            VRLoginPollResponse response = null;
            try
            {
                response = await apiManager.PollVrLoginStatusAsync(currentCode, deviceId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VRAuthManager] PollLoginStatus failed: {ex.Message}");
            }

            if (cancellationToken.IsCancellationRequested || !IsPending)
            {
                return;
            }

            if (response != null)
            {
                string normalizedStatus = string.IsNullOrWhiteSpace(response.status)
                    ? string.Empty
                    : response.status.Trim().ToLowerInvariant();

                if (response.success && normalizedStatus == "approved" && !string.IsNullOrWhiteSpace(response.accessToken))
                {
                    CompleteLogin(response);
                    return;
                }

                if (normalizedStatus == "expired" || apiManager.LastResponseStatusCode == 404 || apiManager.LastResponseStatusCode == 410)
                {
                    HandleExpiredCode(!string.IsNullOrWhiteSpace(response.message) ? response.message : "Login code expired. Request a new one.");
                    return;
                }

                if (normalizedStatus == "pending")
                {
                    statusMessage = "Waiting for approval...";
                }
                else if (!string.IsNullOrWhiteSpace(response.message))
                {
                    statusMessage = response.message;
                }

                NotifyStateUpdated();
            }

            try
            {
                await Task.Delay(PollIntervalMilliseconds, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private void CompleteLogin(VRLoginPollResponse response)
    {
        StopPolling();
        ResetPairingCode();

        accessToken = response.accessToken.Trim();
        userId = response.user != null ? (response.user.id ?? string.Empty).Trim() : string.Empty;
        username = response.user != null ? (response.user.username ?? string.Empty).Trim() : string.Empty;
        statusMessage = string.IsNullOrWhiteSpace(username)
            ? "VR login complete."
            : $"Logged in as {username}.";

        PlayerPrefs.SetString(AccessTokenPlayerPrefsKey, accessToken);
        PlayerPrefs.SetString(UserIdPlayerPrefsKey, userId);
        PlayerPrefs.SetString(UsernamePlayerPrefsKey, username);
        PlayerPrefs.Save();

        if (ApiManager.Instance != null)
        {
            ApiManager.Instance.SetAuthToken(accessToken);
        }

        NotifyStateUpdated();
    }

    private void HandleExpiredCode(string message)
    {
        StopPolling();
        ResetPairingCode();
        statusMessage = string.IsNullOrWhiteSpace(message)
            ? "Login code expired. Request a new one."
            : message;
        NotifyStateUpdated();
    }

    private void ResetPairingCode()
    {
        currentCode = string.Empty;
        codeExpiresAtUtc = DateTime.MinValue;
        lastBroadcastRemainingSeconds = -1;
    }

    private void NotifyStateUpdated()
    {
        bool authState = IsAuthenticated;
        if (authState != lastNotifiedAuthState)
        {
            lastNotifiedAuthState = authState;
            AuthenticationChanged?.Invoke(authState);
        }

        StateUpdated?.Invoke();
    }
}
