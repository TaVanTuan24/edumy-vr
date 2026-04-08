using System.IO;
using UnityEditor;
using UnityEngine;

public static class VRUIPopupPrefabCreator
{
    private const string PrefabFolder = "Assets/Prefabs/VRUI";
    private const string SlidePrefabPath = PrefabFolder + "/SlidePopupWindow.prefab";
    private const string QuizPrefabPath = PrefabFolder + "/QuizPopupWindow.prefab";

    [MenuItem("Tools/VRUI/Create Popup Prefabs")]
    public static void CreatePopupPrefabs()
    {
        EnsureFolderHierarchy(PrefabFolder);

        CreateSlidePopupPrefab();
        CreateQuizPopupPrefab();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[VRUIPopupPrefabCreator] Popup prefabs are ready at Assets/Prefabs/VRUI.");
    }

    [InitializeOnLoadMethod]
    private static void AutoCreateIfMissing()
    {
        bool missingSlide = !File.Exists(SlidePrefabPath);
        bool missingQuiz = !File.Exists(QuizPrefabPath);
        if (!missingSlide && !missingQuiz)
        {
            return;
        }

        CreatePopupPrefabs();
    }

    private static void CreateSlidePopupPrefab()
    {
        GameObject root = new GameObject("SlidePopupWindow");
        SlidePopupWindow comp = root.AddComponent<SlidePopupWindow>();

        DeleteAllChildren(root.transform);

        GameObject windowRoot = new GameObject("SlidePopupWindowRoot", typeof(RectTransform));
        windowRoot.transform.SetParent(root.transform, false);

        SetObjectField(comp, "windowTransform", windowRoot.transform);

        PrefabUtility.SaveAsPrefabAsset(root, SlidePrefabPath);
        Object.DestroyImmediate(root);
    }

    private static void CreateQuizPopupPrefab()
    {
        GameObject root = new GameObject("QuizPopupWindow");
        QuizPopupWindow comp = root.AddComponent<QuizPopupWindow>();

        DeleteAllChildren(root.transform);

        GameObject windowRoot = new GameObject("QuizPopupWindowRoot", typeof(RectTransform));
        windowRoot.transform.SetParent(root.transform, false);

        SetObjectField(comp, "windowTransform", windowRoot.transform);

        PrefabUtility.SaveAsPrefabAsset(root, QuizPrefabPath);
        Object.DestroyImmediate(root);
    }

    private static void SetObjectField(Object target, string fieldName, Object value)
    {
        SerializedObject so = new SerializedObject(target);
        SerializedProperty sp = so.FindProperty(fieldName);
        if (sp != null)
        {
            sp.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void EnsureFolderHierarchy(string folder)
    {
        string[] parts = folder.Split('/');
        if (parts.Length == 0 || parts[0] != "Assets")
        {
            return;
        }

        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string child = parts[i];
            string candidate = current + "/" + child;
            if (!AssetDatabase.IsValidFolder(candidate))
            {
                AssetDatabase.CreateFolder(current, child);
            }
            current = candidate;
        }
    }

    private static void DeleteAllChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(parent.GetChild(i).gameObject);
        }
    }
}
