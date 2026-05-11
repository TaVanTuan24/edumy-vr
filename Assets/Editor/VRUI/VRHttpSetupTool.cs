#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class VRHttpSetupTool
{
    [MenuItem("Tools/VRUI/Networking/Enable HTTP Downloads (Android)")]
    public static void EnableHttpDownloadsAndroid()
    {
        try
        {
            Type playerSettingsType = typeof(PlayerSettings);
            PropertyInfo prop = playerSettingsType.GetProperty("insecureHttpOption", BindingFlags.Public | BindingFlags.Static);

            if (prop == null)
            {
                Debug.LogWarning("[VRHttpSetupTool] PlayerSettings.insecureHttpOption was not found. Enable it manually in Player Settings > Other Settings > Allow downloads over HTTP.");
                EditorUtility.DisplayDialog(
                    "HTTP Setup",
                    "The insecureHttpOption API was not found. Enable it manually in Player Settings > Other Settings > Allow downloads over HTTP = Always allowed.",
                    "OK");
                return;
            }

            Type enumType = prop.PropertyType;
            object alwaysAllowed = Enum.Parse(enumType, "AlwaysAllowed");
            prop.SetValue(null, alwaysAllowed);

            AssetDatabase.SaveAssets();
            Debug.Log("[VRHttpSetupTool] Enabled Allow downloads over HTTP = AlwaysAllowed.");
            EditorUtility.DisplayDialog(
                "HTTP Setup",
                "Enabled Allow downloads over HTTP = AlwaysAllowed.",
                "OK");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[VRHttpSetupTool] Failed to enable HTTP downloads: {ex.Message}");
            EditorUtility.DisplayDialog(
                "HTTP Setup",
                "Could not set this automatically. Open Player Settings > Other Settings > Allow downloads over HTTP and choose Always allowed.",
                "OK");
        }
    }
}
#endif
