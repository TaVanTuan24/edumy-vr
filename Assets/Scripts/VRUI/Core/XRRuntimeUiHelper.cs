using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class XRRuntimeUiHelper
{
    private const string TrackedDeviceGraphicRaycasterTypeName =
        "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit";

    private const string InputSystemUiModuleTypeName =
        "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem";

    // XRUIInputModule is the correct XR-aware input module that routes tracked device
    // rays (controller pointers) to TrackedDeviceGraphicRaycaster and PanelRaycaster.
    // Without this, only screen-space pointer simulation works — which is why the
    // hamburger UGUI button worked but UI Toolkit panels and other UGUI canvases did not.
    private const string XRUIInputModuleTypeName =
        "UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule, Unity.XR.Interaction.Toolkit";

    // XRUIToolkitManager is required in Unity 6+ (XR Interaction Toolkit 3.x) to route
    // world space UI Toolkit events correctly.
    private const string XRUIToolkitManagerTypeName =
        "UnityEngine.XR.Interaction.Toolkit.UI.XRUIToolkitManager, Unity.XR.Interaction.Toolkit";

    private static bool loggedEventSystemSetup;

    public static void EnsureWorldSpaceCanvasInteraction(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        if (target.GetComponent<GraphicRaycaster>() == null)
        {
            target.AddComponent<GraphicRaycaster>();
        }

        bool added = TryAddComponentByTypeName(target, TrackedDeviceGraphicRaycasterTypeName);
        if (added)
        {
            Debug.Log($"[XRRuntimeUiHelper] TrackedDeviceGraphicRaycaster added to '{target.name}'.");
        }
    }

    public static void EnsureEventSystemSupportsXR()
    {
        EventSystem eventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
        if (eventSystem == null)
        {
            GameObject go = new GameObject("EventSystem", typeof(EventSystem));
            eventSystem = go.GetComponent<EventSystem>();
            Debug.Log("[XRRuntimeUiHelper] Created new EventSystem.");
        }

        if (eventSystem == null)
        {
            return;
        }

        // 1. Try adding XRUIInputModule first — this is the correct module for VR
        //    that makes TrackedDeviceGraphicRaycaster and PanelRaycaster work with
        //    XR controller rays/hand pointers.
        bool xrUiModulePresent = TryAddComponentByTypeName(eventSystem.gameObject, XRUIInputModuleTypeName);

        // TryAddComponentByTypeName returns false both when the type already exists AND when
        // it can't be resolved. Check if it's already on the EventSystem to avoid
        // unnecessary fallback additions.
        if (!xrUiModulePresent)
        {
            Type xrUiModuleType = Type.GetType(XRUIInputModuleTypeName, false);
            if (xrUiModuleType != null && eventSystem.GetComponent(xrUiModuleType) != null)
            {
                xrUiModulePresent = true;
            }
        }

        // 2. Fallback: if XRUIInputModule could not be resolved at all, try InputSystemUIInputModule
        if (!xrUiModulePresent)
        {
            TryAddComponentByTypeName(eventSystem.gameObject, InputSystemUiModuleTypeName);
        }

        // 3. Remove conflicting StandaloneInputModule if any XR-capable module is present.
        //    StandaloneInputModule conflicts with XRUIInputModule / InputSystemUIInputModule;
        //    having both causes one to be disabled and can break VR pointer routing.
        RemoveConflictingStandaloneInputModule(eventSystem);

        // 4. Ultimate fallback: add StandaloneInputModule only if nothing else exists.
        if (eventSystem.GetComponent<BaseInputModule>() == null)
        {
            eventSystem.gameObject.AddComponent<StandaloneInputModule>();
            Debug.LogWarning("[XRRuntimeUiHelper] No XR input module could be resolved. " +
                "Added StandaloneInputModule as fallback. VR pointer interaction may not work correctly.");
        }

        // 5. Setup UI Toolkit XR Manager (Required for Unity 6 / XRI 3.x)
        EnsureXRUIToolkitManager();

        // Log the setup once per session for Quest debugging
        if (!loggedEventSystemSetup)
        {
            loggedEventSystemSetup = true;
            BaseInputModule[] modules = eventSystem.GetComponents<BaseInputModule>();
            string moduleNames = "";
            for (int i = 0; i < modules.Length; i++)
            {
                if (i > 0) moduleNames += ", ";
                moduleNames += modules[i].GetType().Name;
                moduleNames += modules[i].enabled ? " (enabled)" : " (DISABLED)";
            }
            Debug.Log($"[XRRuntimeUiHelper] EventSystem '{eventSystem.gameObject.name}' " +
                $"input modules: [{moduleNames}]");
        }
    }

    private static void EnsureXRUIToolkitManager()
    {
        Type managerType = Type.GetType(XRUIToolkitManagerTypeName, false);
        if (managerType == null) return;

        if (UnityEngine.Object.FindAnyObjectByType(managerType) != null)
        {
            return; // Manager already exists in scene
        }

        GameObject managerGo = new GameObject("XR UI Toolkit Manager");
        managerGo.AddComponent(managerType);
        Debug.Log("[XRRuntimeUiHelper] Created XR UI Toolkit Manager for World Space UI Toolkit support.");
    }

    /// <summary>
    /// Tries to add a component by its assembly-qualified type name.
    /// Returns true if the component was newly added, false if it already existed or could not be resolved.
    /// </summary>
    public static bool TryAddComponentByTypeName(GameObject target, string assemblyQualifiedTypeName)
    {
        if (target == null || string.IsNullOrWhiteSpace(assemblyQualifiedTypeName))
        {
            return false;
        }

        Type type = Type.GetType(assemblyQualifiedTypeName, false);
        if (type == null || !typeof(Component).IsAssignableFrom(type))
        {
            return false;
        }

        if (target.GetComponent(type) != null)
        {
            return false;
        }

        target.AddComponent(type);
        return true;
    }

    /// <summary>
    /// Removes StandaloneInputModule if a more capable input module (XRUIInputModule
    /// or InputSystemUIInputModule) is already present. Multiple input modules on the
    /// same EventSystem conflict and cause one to be auto-disabled by Unity, which can
    /// silently break VR input.
    /// </summary>
    private static void RemoveConflictingStandaloneInputModule(EventSystem eventSystem)
    {
        if (eventSystem == null)
        {
            return;
        }

        StandaloneInputModule standaloneModule = eventSystem.GetComponent<StandaloneInputModule>();
        if (standaloneModule == null)
        {
            return;
        }

        // Check if there's another, better input module present
        BaseInputModule[] allModules = eventSystem.GetComponents<BaseInputModule>();
        bool hasBetterModule = false;
        for (int i = 0; i < allModules.Length; i++)
        {
            BaseInputModule module = allModules[i];
            if (module == null || module == standaloneModule)
            {
                continue;
            }

            // Any non-Standalone module is considered "better" for XR purposes
            hasBetterModule = true;
            break;
        }

        if (hasBetterModule)
        {
            Debug.Log($"[XRRuntimeUiHelper] Removing conflicting StandaloneInputModule from " +
                $"EventSystem '{eventSystem.gameObject.name}'. XR-capable module is present.");

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(standaloneModule);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(standaloneModule);
            }
        }
    }
}
