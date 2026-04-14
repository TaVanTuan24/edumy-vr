using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Management;

public class VRSimulatorBootstrap : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateBootstrap()
    {
        GameObject go = new GameObject(nameof(VRSimulatorBootstrap));
        DontDestroyOnLoad(go);
        go.AddComponent<VRSimulatorBootstrap>();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        StartCoroutine(BootstrapRoutine());
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void HandleSceneLoaded(Scene _, LoadSceneMode __)
    {
        StartCoroutine(BootstrapRoutine());
    }

    private IEnumerator BootstrapRoutine()
    {
        yield return null;
        yield return null;
        yield return EnsureXrRunning();
        ApplySceneBindings();
    }

    private static IEnumerator EnsureXrRunning()
    {
        XRGeneralSettings settings = XRGeneralSettings.Instance;
        XRManagerSettings manager = settings != null ? settings.Manager : null;
        if (manager == null)
        {
            yield break;
        }

        if (manager.activeLoader == null)
        {
            yield return manager.InitializeLoader();
        }

        if (manager.activeLoader != null)
        {
            manager.StartSubsystems();
        }
    }

    private static void ApplySceneBindings()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            mainCamera = FindAnyObjectByType<Camera>();
        }

        Transform rightHand = FindRightHandTransform();

        VRPanelAnchorManager[] panelManagers = FindObjectsByType<VRPanelAnchorManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < panelManagers.Length; i++)
        {
            VRPanelAnchorManager panelManager = panelManagers[i];
            if (panelManager == null)
            {
                continue;
            }

            if (panelManager.cameraAnchor == null && mainCamera != null)
            {
                panelManager.cameraAnchor = mainCamera.transform;
            }

            if (panelManager.rightHandAnchor == null && rightHand != null)
            {
                panelManager.rightHandAnchor = rightHand;
            }
        }

        #if UNITY_EDITOR
        if (IsXrActive())
        {
            EditorFlyCameraController[] flyCameras = FindObjectsByType<EditorFlyCameraController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < flyCameras.Length; i++)
            {
                if (flyCameras[i] != null)
                {
                    flyCameras[i].enabled = false;
                }
            }
        }
        #endif
    }

    private static Transform FindRightHandTransform()
    {
        string[] candidateNames =
        {
            "Right Hand",
            "RightHand",
            "RightHand Controller",
            "Right Controller",
            "XR Controller Right",
        };

        foreach (string candidateName in candidateNames)
        {
            GameObject candidate = GameObject.Find(candidateName);
            if (candidate != null)
            {
                return candidate.transform;
            }
        }

        List<XRInputSubsystem> subsystems = new List<XRInputSubsystem>();
        SubsystemManager.GetSubsystems(subsystems);
        if (subsystems.Count > 0 && Camera.main != null)
        {
            Transform cameraRoot = Camera.main.transform.root;
            Transform found = FindChildByPartialName(cameraRoot, "right hand");
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static Transform FindChildByPartialName(Transform root, string needle)
    {
        if (root == null || string.IsNullOrWhiteSpace(needle))
        {
            return null;
        }

        string loweredNeedle = needle.ToLowerInvariant();
        Queue<Transform> queue = new Queue<Transform>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            Transform current = queue.Dequeue();
            if (current.name.ToLowerInvariant().Contains(loweredNeedle))
            {
                return current;
            }

            for (int i = 0; i < current.childCount; i++)
            {
                queue.Enqueue(current.GetChild(i));
            }
        }

        return null;
    }

    private static bool IsXrActive()
    {
        List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);
        for (int i = 0; i < displays.Count; i++)
        {
            if (displays[i] != null && displays[i].running)
            {
                return true;
            }
        }

        return false;
    }
}
