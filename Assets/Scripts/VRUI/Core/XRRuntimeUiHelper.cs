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

        TryAddComponentByTypeName(target, TrackedDeviceGraphicRaycasterTypeName);
    }

    public static void EnsureEventSystemSupportsXR()
    {
        EventSystem eventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
        if (eventSystem == null)
        {
            GameObject go = new GameObject("EventSystem", typeof(EventSystem));
            eventSystem = go.GetComponent<EventSystem>();
        }

        if (eventSystem == null)
        {
            return;
        }

        TryAddComponentByTypeName(eventSystem.gameObject, InputSystemUiModuleTypeName);

        if (eventSystem.GetComponent<BaseInputModule>() == null)
        {
            eventSystem.gameObject.AddComponent<StandaloneInputModule>();
        }
    }

    public static void TryAddComponentByTypeName(GameObject target, string assemblyQualifiedTypeName)
    {
        if (target == null || string.IsNullOrWhiteSpace(assemblyQualifiedTypeName))
        {
            return;
        }

        Type type = Type.GetType(assemblyQualifiedTypeName, false);
        if (type == null || !typeof(Component).IsAssignableFrom(type))
        {
            return;
        }

        if (target.GetComponent(type) != null)
        {
            return;
        }

        target.AddComponent(type);
    }
}
