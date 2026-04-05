using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class VRUICleanupUtility
{
    [MenuItem("Tools/VRUI/Cleanup/Remove Missing Scripts (All Scenes + Prefabs)")]
    public static void CleanupAllScenesAndPrefabs()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[VRUICleanup] Dung Play Mode truoc khi cleanup.");
            return;
        }

        if (!EditorUtility.DisplayDialog(
                "VRUI Cleanup",
                "Se quet tat ca Scene va Prefab trong Assets de xoa Missing Script. Tiep tuc?",
                "Tiep tuc",
                "Huy"))
        {
            return;
        }

        int totalRemoved = 0;
        int touchedScenes = 0;
        int touchedPrefabs = 0;
        SceneSetup[] originalSetup = EditorSceneManager.GetSceneManagerSetup();

        try
        {
            string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
            foreach (string guid in sceneGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;

                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                int removed = RemoveMissingInScene(scene);
                if (removed > 0)
                {
                    touchedScenes++;
                    totalRemoved += removed;
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }

            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            foreach (string guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                int removed = RemoveMissingRecursive(root);
                if (removed > 0)
                {
                    touchedPrefabs++;
                    totalRemoved += removed;
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[VRUICleanup] Loi trong qua trinh cleanup: {ex.Message}");
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[VRUICleanup] Hoan tat. Removed={totalRemoved}, Scenes={touchedScenes}, Prefabs={touchedPrefabs}");
        EditorUtility.DisplayDialog(
            "VRUI Cleanup Done",
            $"Da xoa {totalRemoved} Missing Script.\nScene da luu: {touchedScenes}\nPrefab da luu: {touchedPrefabs}",
            "OK");
    }

    [MenuItem("Tools/VRUI/Cleanup/Remove Missing Scripts (Open Scenes Only)")]
    public static void CleanupOpenScenesOnly()
    {
        int totalRemoved = 0;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            int removed = RemoveMissingInScene(scene);
            if (removed > 0)
            {
                totalRemoved += removed;
                EditorSceneManager.MarkSceneDirty(scene);
            }
        }

        Debug.Log($"[VRUICleanup] Open scenes cleanup complete. Removed={totalRemoved}");
    }

    private static int RemoveMissingInScene(Scene scene)
    {
        int removed = 0;
        GameObject[] roots = scene.GetRootGameObjects();
        foreach (GameObject root in roots)
        {
            removed += RemoveMissingRecursive(root);
        }
        return removed;
    }

    private static int RemoveMissingRecursive(GameObject go)
    {
        int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);

        Transform transform = go.transform;
        for (int i = 0; i < transform.childCount; i++)
        {
            removed += RemoveMissingRecursive(transform.GetChild(i).gameObject);
        }

        return removed;
    }
}