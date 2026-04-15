using System;
using UnityEngine.UIElements;
using UnityEngine.Video;

public class VideoController
{
    public VisualElement Root { get; }
    public Button BackButton { get; }
    public Label TitleLabel { get; }
    public Label StatusLabel { get; }
    public VisualElement Surface { get; }

    public VideoControlsOverlay Overlay { get; }

    public VideoController(VisualElement root, Func<VideoPlayer> playerProvider)
    {
        Root = root;
        BackButton = root.Q<Button>("video-back-button");
        TitleLabel = root.Q<Label>("video-title");
        StatusLabel = root.Q<Label>("video-status");
        Surface = root.Q<VisualElement>("video-surface");
        Overlay = new VideoControlsOverlay(root, playerProvider);
    }
}
