using System;
using UnityEngine;

public static class ApiConfig
{
    private const string ResourcesAssetName = "BackendConfig";
    private const string ProductionBaseUrl = "https://edumy.onrender.com";
    private const string FallbackBaseUrl = ProductionBaseUrl;

    private static BackendConfig cachedConfig;
    private static bool missingConfigLogged;

    public static string BaseUrl => ResolveBaseUrl();
    public static string DefaultBaseUrl => NormalizeBaseUrl(LoadConfig()?.apiBaseUrl);

    public static string BuildUrl(string endpoint)
    {
        return BuildUrl(endpoint, BaseUrl);
    }

    public static string BuildUrl(string endpoint, string baseUrl)
    {
        string normalizedBaseUrl = NormalizeBaseUrl(baseUrl);
        string normalizedEndpoint = string.IsNullOrWhiteSpace(endpoint)
            ? string.Empty
            : endpoint.Trim();

        if (string.IsNullOrWhiteSpace(normalizedBaseUrl))
        {
            return normalizedEndpoint;
        }

        if (IsAbsoluteUrl(normalizedEndpoint))
        {
            return normalizedEndpoint;
        }

        return $"{normalizedBaseUrl}/{normalizedEndpoint.TrimStart('/')}";
    }

    public static string GetOverrideBaseUrl()
    {
        MigrateLegacyOverrideIfNeeded();
        string overrideBaseUrl = NormalizeBaseUrl(PlayerPrefs.GetString(VRSessionKeys.BackendBaseUrlOverride, string.Empty));
        if (IsLocalDevelopmentBaseUrl(overrideBaseUrl))
        {
            Debug.LogWarning($"[ApiConfig] Ignoring saved local backend URL '{overrideBaseUrl}'. Using production backend '{ProductionBaseUrl}'.");
            ClearOverrideBaseUrl();
            return string.Empty;
        }

        return overrideBaseUrl;
    }

    public static void SetOverrideBaseUrl(string baseUrl)
    {
        string normalized = NormalizeBaseUrl(baseUrl);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            ClearOverrideBaseUrl();
            return;
        }

        PlayerPrefs.SetString(VRSessionKeys.BackendBaseUrlOverride, normalized);
        if (PlayerPrefs.HasKey(VRSessionKeys.LegacyApiBaseUrl))
        {
            PlayerPrefs.DeleteKey(VRSessionKeys.LegacyApiBaseUrl);
        }
        PlayerPrefs.Save();
    }

    public static void ClearOverrideBaseUrl()
    {
        bool changed = false;
        if (PlayerPrefs.HasKey(VRSessionKeys.BackendBaseUrlOverride))
        {
            PlayerPrefs.DeleteKey(VRSessionKeys.BackendBaseUrlOverride);
            changed = true;
        }

        if (PlayerPrefs.HasKey(VRSessionKeys.LegacyApiBaseUrl))
        {
            PlayerPrefs.DeleteKey(VRSessionKeys.LegacyApiBaseUrl);
            changed = true;
        }

        if (changed)
        {
            PlayerPrefs.Save();
        }
    }

    public static bool TryNormalizeBaseUrl(string rawValue, out string normalizedBaseUrl, out string errorMessage)
    {
        normalizedBaseUrl = NormalizeBaseUrl(rawValue);
        if (string.IsNullOrWhiteSpace(normalizedBaseUrl))
        {
            errorMessage = "Backend Server URL cannot be empty.";
            return false;
        }

        if (!Uri.TryCreate(normalizedBaseUrl, UriKind.Absolute, out Uri uri))
        {
            errorMessage = $"Backend Server URL is invalid: {rawValue}";
            return false;
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Backend Server URL must start with http:// or https://.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            errorMessage = "Backend Server URL must include a valid host name or IP address.";
            return false;
        }

        errorMessage = null;
        return true;
    }

    public static string NormalizeBaseUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().TrimEnd('/');
    }

    public static bool IsAbsoluteUrl(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && Uri.TryCreate(value, UriKind.Absolute, out Uri uri)
            && !string.IsNullOrWhiteSpace(uri.Scheme);
    }

    private static string ResolveBaseUrl()
    {
        string overrideBaseUrl = GetOverrideBaseUrl();
        if (!string.IsNullOrWhiteSpace(overrideBaseUrl))
        {
            return overrideBaseUrl;
        }

        string defaultBaseUrl = DefaultBaseUrl;
        if (!string.IsNullOrWhiteSpace(defaultBaseUrl))
        {
            return defaultBaseUrl;
        }

        return FallbackBaseUrl;
    }

    private static BackendConfig LoadConfig()
    {
        if (cachedConfig != null)
        {
            return cachedConfig;
        }

        cachedConfig = Resources.Load<BackendConfig>(ResourcesAssetName);
        if (cachedConfig == null && !missingConfigLogged)
        {
            missingConfigLogged = true;
            Debug.LogWarning($"[ApiConfig] Resources/BackendConfig.asset was not found. Falling back to {FallbackBaseUrl}.");
        }

        return cachedConfig;
    }

    private static bool IsLocalDevelopmentBaseUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri))
        {
            return false;
        }

        string host = uri.Host ?? string.Empty;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || host.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (System.Net.IPAddress.TryParse(host, out System.Net.IPAddress address))
        {
            byte[] bytes = address.GetAddressBytes();
            if (bytes.Length == 4)
            {
                return bytes[0] == 10
                    || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                    || (bytes[0] == 192 && bytes[1] == 168);
            }
        }

        return false;
    }

    private static void MigrateLegacyOverrideIfNeeded()
    {
        if (PlayerPrefs.HasKey(VRSessionKeys.BackendBaseUrlOverride))
        {
            return;
        }

        if (!PlayerPrefs.HasKey(VRSessionKeys.LegacyApiBaseUrl))
        {
            return;
        }

        string legacyValue = NormalizeBaseUrl(PlayerPrefs.GetString(VRSessionKeys.LegacyApiBaseUrl, string.Empty));
        if (!string.IsNullOrWhiteSpace(legacyValue))
        {
            PlayerPrefs.SetString(VRSessionKeys.BackendBaseUrlOverride, legacyValue);
        }

        PlayerPrefs.DeleteKey(VRSessionKeys.LegacyApiBaseUrl);
        PlayerPrefs.Save();
    }
}
