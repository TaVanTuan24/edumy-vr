using UnityEngine.UIElements;

public class SlideController
{
    public VisualElement Root { get; }
    public Button CloseButton { get; }
    public SlideViewer Viewer { get; }

    public SlideController(VisualElement root)
    {
        Root = root;
        CloseButton = root.Q<Button>("slide-close-button");
        Viewer = new SlideViewer(root);
    }
}
