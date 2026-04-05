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
                Debug.LogWarning("[VRHttpSetupTool] Khong tim thay PlayerSettings.insecureHttpOption. Hay bat tay trong Player Settings > Other Settings > Allow downloads over HTTP.");
                EditorUtility.DisplayDialog(
                    "HTTP Setup",
                    "Khong tim thay insecureHttpOption API. Vui long bat tay trong Player Settings > Other Settings > Allow downloads over HTTP = Always allowed.",
                    "OK");
                return;
            }

            Type enumType = prop.PropertyType;
            object alwaysAllowed = Enum.Parse(enumType, "AlwaysAllowed");
            prop.SetValue(null, alwaysAllowed);

            AssetDatabase.SaveAssets();
            Debug.Log("[VRHttpSetupTool] Da bat Allow downloads over HTTP = AlwaysAllowed.");
            EditorUtility.DisplayDialog(
                "HTTP Setup",
                "Da bat Allow downloads over HTTP = AlwaysAllowed.",
                "OK");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[VRHttpSetupTool] Loi khi bat HTTP downloads: {ex.Message}");
            EditorUtility.DisplayDialog(
                "HTTP Setup",
                "Khong the set tu dong. Hay vao Player Settings > Other Settings > Allow downloads over HTTP va chon Always allowed.",
                "OK");
        }
    }
}
#endif
