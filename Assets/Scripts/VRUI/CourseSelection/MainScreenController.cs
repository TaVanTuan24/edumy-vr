using System.Collections.Generic;
using UnityEngine.UIElements;

public class MainScreenController
{
    private readonly Dictionary<UiScreenState, VisualElement> windows = new Dictionary<UiScreenState, VisualElement>();

    public UiScreenState CurrentState { get; private set; } = UiScreenState.LessonSelection;

    public void Register(UiScreenState state, VisualElement root)
    {
        if (root == null) return;
        windows[state] = root;
    }

    public void Show(UiScreenState state)
    {
        CurrentState = state;

        foreach (KeyValuePair<UiScreenState, VisualElement> kv in windows)
        {
            if (kv.Value == null) continue;
            kv.Value.style.display = kv.Key == state ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
