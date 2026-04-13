using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;

public class VideoControlsOverlay
{
    private readonly VisualElement pageRoot;
    private readonly VisualElement surfaceTapZone;
    private readonly VisualElement controlsOverlay;
    private readonly Button centerPlayButton;
    private readonly Button barPlayButton;
    private readonly Slider progressSlider;
    private readonly Label timeLabel;
    private readonly Slider volumeSlider;
    private readonly Button speedButton;
    private readonly Button fullscreenButton;

    private readonly Func<VideoPlayer> playerProvider;

    private IVisualElementScheduledItem tickItem;
    private float hideAtRealtime;
    private bool userDraggingProgress;
    private float[] speedOptions = { 1f, 1.25f, 1.5f, 2f };
    private int speedIndex;

    public VideoControlsOverlay(VisualElement pageRoot, Func<VideoPlayer> playerProvider)
    {
        this.pageRoot = pageRoot;
        this.playerProvider = playerProvider;

        surfaceTapZone = pageRoot.Q<VisualElement>("video-surface-hit-zone");
        controlsOverlay = pageRoot.Q<VisualElement>("video-controls-overlay");
        centerPlayButton = pageRoot.Q<Button>("video-play-center");
        barPlayButton = pageRoot.Q<Button>("video-play-toggle");
        progressSlider = pageRoot.Q<Slider>("video-progress");
        timeLabel = pageRoot.Q<Label>("video-time");
        volumeSlider = pageRoot.Q<Slider>("video-volume");
        speedButton = pageRoot.Q<Button>("video-speed");
        fullscreenButton = pageRoot.Q<Button>("video-fullscreen");

        if (surfaceTapZone != null)
        {
            surfaceTapZone.RegisterCallback<PointerMoveEvent>(_ => ShowControls());
            surfaceTapZone.RegisterCallback<PointerDownEvent>(_ => TogglePlayPause());
        }

        if (centerPlayButton != null) centerPlayButton.clicked += TogglePlayPause;
        if (barPlayButton != null) barPlayButton.clicked += TogglePlayPause;

        if (progressSlider != null)
        {
            progressSlider.RegisterCallback<PointerDownEvent>(_ => userDraggingProgress = true);
            progressSlider.RegisterCallback<PointerUpEvent>(_ =>
            {
                userDraggingProgress = false;
                SeekToSliderValue();
            });
            progressSlider.RegisterValueChangedCallback(_ =>
            {
                if (userDraggingProgress)
                {
                    ShowControls();
                    UpdateTimeLabelFromSlider();
                }
            });
        }

        if (volumeSlider != null)
        {
            volumeSlider.lowValue = 0f;
            volumeSlider.highValue = 1f;
            volumeSlider.value = 1f;
            volumeSlider.RegisterValueChangedCallback(evt =>
            {
                VideoPlayer player = playerProvider?.Invoke();
                if (player == null) return;
                player.SetDirectAudioVolume(0, Mathf.Clamp01(evt.newValue));
                ShowControls();
            });
        }

        if (speedButton != null)
        {
            speedButton.clicked += CycleSpeed;
            speedButton.text = "1x";
        }

        if (fullscreenButton != null)
        {
            fullscreenButton.clicked += () =>
            {
                Screen.fullScreen = !Screen.fullScreen;
                ShowControls();
            };
        }

        tickItem = pageRoot.schedule.Execute(Tick).Every(120);
        ShowControls();
    }

    public void OnPageShown()
    {
        ShowControls();
        Tick();
    }

    private void Tick()
    {
        VideoPlayer player = playerProvider?.Invoke();
        if (player == null) return;

        if (!userDraggingProgress && progressSlider != null)
        {
            double length = player.length;
            if (length > 0.001)
            {
                progressSlider.lowValue = 0f;
                progressSlider.highValue = (float)length;
                progressSlider.SetValueWithoutNotify((float)player.time);
            }
        }

        UpdateTimeLabel(player);
        UpdatePlayButtons(player);

        if (controlsOverlay != null)
        {
            bool visible = Time.realtimeSinceStartup < hideAtRealtime || !player.isPlaying;
            controlsOverlay.EnableInClassList("is-visible", visible);
            controlsOverlay.EnableInClassList("is-hidden", !visible);
        }
    }

    private void TogglePlayPause()
    {
        VideoPlayer player = playerProvider?.Invoke();
        if (player == null) return;

        if (player.isPlaying) player.Pause();
        else player.Play();

        ShowControls();
        Tick();
    }

    private void SeekToSliderValue()
    {
        VideoPlayer player = playerProvider?.Invoke();
        if (player == null || progressSlider == null) return;

        player.time = Mathf.Clamp(progressSlider.value, progressSlider.lowValue, progressSlider.highValue);
        ShowControls();
    }

    private void UpdateTimeLabel(VideoPlayer player)
    {
        if (timeLabel == null) return;

        float current = (float)Math.Max(0d, player.time);
        float total = player.length > 0d ? (float)player.length : 0f;
        timeLabel.text = $"{FormatTime(current)} / {FormatTime(total)}";
    }

    private void UpdateTimeLabelFromSlider()
    {
        if (timeLabel == null || progressSlider == null) return;
        float current = progressSlider.value;
        float total = progressSlider.highValue;
        timeLabel.text = $"{FormatTime(current)} / {FormatTime(total)}";
    }

    private void UpdatePlayButtons(VideoPlayer player)
    {
        string icon = player.isPlaying ? "Pause" : "Play";
        if (centerPlayButton != null) centerPlayButton.text = icon;
        if (barPlayButton != null) barPlayButton.text = icon;
    }

    private void CycleSpeed()
    {
        VideoPlayer player = playerProvider?.Invoke();
        if (player == null) return;

        speedIndex = (speedIndex + 1) % speedOptions.Length;
        float speed = speedOptions[speedIndex];
        player.playbackSpeed = speed;
        if (speedButton != null)
        {
            speedButton.text = speed.ToString("0.##") + "x";
        }

        ShowControls();
    }

    private void ShowControls()
    {
        hideAtRealtime = Time.realtimeSinceStartup + 2.2f;

        if (controlsOverlay != null)
        {
            controlsOverlay.AddToClassList("is-visible");
            controlsOverlay.RemoveFromClassList("is-hidden");
        }
    }

    private static string FormatTime(float sec)
    {
        sec = Mathf.Max(0f, sec);
        int h = Mathf.FloorToInt(sec / 3600f);
        int m = Mathf.FloorToInt((sec % 3600f) / 60f);
        int s = Mathf.FloorToInt(sec % 60f);

        if (h > 0)
        {
            return $"{h:00}:{m:00}:{s:00}";
        }

        return $"{m:00}:{s:00}";
    }
}
