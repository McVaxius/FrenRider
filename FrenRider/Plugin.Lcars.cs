using System;
using System.Collections.Generic;
using System.Linq;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using FrenRider.Windows;
using FrenRider.Services;

namespace FrenRider;

public sealed partial class Plugin
{
    private MaterialUiSoundPlayer? uiSoundPlayer;
    private readonly Dictionary<Window, TitleBarButton> lcarsSoundButtons = new();
    internal bool LcarsActive => Configuration.UiStylingEnabled && Configuration.UiStyle == FrenRiderUiStyle.Classic && Configuration.UiColors?.Mode == MaterialColorMode.Lcars;

    internal void SetKrangle(bool enabled)
    {
        Configuration.KrangleEnabled = enabled;
        KrangleService.ClearCache();
        Configuration.Save();
    }

    private void PlayLcarsSound(MaterialUiSoundCue cue)
    {
        if (!LcarsActive || Configuration.UiLcarsSoundsMuted) return;
        (uiSoundPlayer ??= new MaterialUiSoundPlayer()).TryPlay(cue);
    }

    internal void SetLcarsSounds(bool enabled)
    {
        Configuration.UiLcarsSoundsMuted = !enabled;
        if (!enabled) uiSoundPlayer?.Stop();
        Configuration.Save();
    }

    private void RefreshLcarsSoundButtons()
    {
        foreach (var owner in WindowSystem.Windows.OfType<Window>())
        {
            if ((owner.Flags & ImGuiWindowFlags.NoTitleBar) != 0) continue;
            if (!lcarsSoundButtons.TryGetValue(owner, out var button))
            {
                button = new TitleBarButton
                {
                    Priority = (int)Math.Max(int.MinValue + 1L, owner.TitleBarButtons.Select(static entry => (long)entry.Priority).DefaultIfEmpty(0).Min() - 1),
                    IconOffset = new(2, 1),
                    Click = mouse =>
                    {
                        if (mouse == ImGuiMouseButton.Left && LcarsActive) SetLcarsSounds(Configuration.UiLcarsSoundsMuted);
                    },
                    ShowTooltip = () => MaterialText.SetTooltip(UiText.T(Configuration.UiLcarsSoundsMuted ? "UI sounds muted" : "UI sounds on")),
                };
                lcarsSoundButtons.Add(owner, button);
            }
            button.Icon = Configuration.UiLcarsSoundsMuted ? FontAwesomeIcon.VolumeMute : FontAwesomeIcon.VolumeUp;
            if (LcarsActive && !owner.TitleBarButtons.Contains(button)) owner.TitleBarButtons.Add(button);
            else if (!LcarsActive) owner.TitleBarButtons.Remove(button);
        }
    }

    private void DrawLcarsSounds()
    {
        MaterialSettings.Section(UiText.T("LCARS UI sounds"));
        ImGui.BeginDisabled(!LcarsActive);
        try
        {
            var enabled = !Configuration.UiLcarsSoundsMuted;
            if (UiGui.HeaderToggle("UI sounds", ref enabled)) SetLcarsSounds(enabled);
            UiGui.TextWrapped("Custom UI tones. The titlebar speaker toggles the same setting.");
            ImGui.BeginDisabled(Configuration.UiLcarsSoundsMuted);
            try
            {
                // Preview its full tone without also submitting the ordinary button click tone.
                var theme = MaterialTheme.Current;
                var feedback = theme.Feedback;
                bool preview;
                try { theme.Feedback = null; preview = UiGui.Button("Preview sound"); }
                finally { theme.Feedback = feedback; }
                if (preview) PlayLcarsSound(MaterialUiSoundCue.Preview);
            }
            finally { ImGui.EndDisabled(); }
        }
        finally { ImGui.EndDisabled(); }
        if (!LcarsActive) UiGui.TextDisabled("Select LCARS to use UI sounds.");
        else if (Configuration.UiLcarsSoundsMuted) UiGui.TextDisabled("Turn on UI sounds to preview.");
        if (uiSoundPlayer?.Error != null) UiGui.TextDisabled("Audio output is unavailable.");
    }
}
