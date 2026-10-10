using Dalamud.Configuration;
using System;

namespace FrenRider;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public const int CurrentVersion = ConfigurationMigration.CurrentVersion;

    public int Version { get; set; } = CurrentVersion;

    // --- Global UI Settings ---
    public string UiLanguage { get; set; } = "en";
    public uint UiAccentRgb { get; set; } = 0x70C677;
    public bool UiCompact { get; set; } = true;
    public bool UiCompactVisibleOnMainWindow { get; set; }
    public bool UiTransparencyVisibleOnMainWindow { get; set; }
    public bool UiCompactDefaultsApplied { get; set; }
    [Newtonsoft.Json.JsonExtensionData]
    public System.Collections.Generic.Dictionary<string, Newtonsoft.Json.Linq.JToken>? AdditionalSettings { get; set; }

    internal bool ApplyCompactDefaults()
    {
        if (UiCompactDefaultsApplied) return false;
        UiCompact = true;
        UiCompactVisibleOnMainWindow = UiTransparencyVisibleOnMainWindow = false;
        UiCompactDefaultsApplied = true;
        return true;
    }
    public bool UiLanguageVisibleOnMainWindow { get; set; } = true;
    public bool UiTransparencyEnabled { get; set; } = true;
    public int UiWindowOpacityPercent { get; set; } = 100;
    public bool UiAutoFade { get; set; } = true;
    public int UiFadedOpacityPercent { get; set; } = 50;
    public float UiUnfocusedDelaySeconds { get; set; } = 10;
    public bool IsConfigWindowMovable { get; set; } = true;
    public bool DtrBarEnabled { get; set; } = true;
    public int DtrBarMode { get; set; } = 0; // 0=text-only, 1=icon+text, 2=icon-only
    public string DtrIconEnabled { get; set; } = "\uE03C";
    public string DtrIconDisabled { get; set; } = "\uE03D";
    public bool KrangleEnabled { get; set; } = false;
    public float LeftPanelWidth { get; set; } = 240f;
    public bool DontMoveWhileCasting { get; set; } = ConfigurationMigration.DefaultDontMoveWhileCasting;

    // Opt-in development probe; independent of character and temporary DAD profiles.
    public bool ChocoboProbeAfterReload { get; set; } = false;

    // --- Video Notifications ---
    public bool VideoNotificationsEnabled { get; set; } = false;
    public int VideoWindowX { get; set; } = 100;
    public int VideoWindowY { get; set; } = 100;
    public int VideoWindowWidth { get; set; } = 640;
    public int VideoWindowHeight { get; set; } = 480;
    public bool VideoMuteAudio { get; set; } = true;
    public string EmbeddedVideosFolder { get; set; } = "videos";

    // --- Account Tracking ---
    public string LastAccountId { get; set; } = "";

    internal bool MigrateToCurrentVersion()
    {
        var version = Version;
        var dontMoveWhileCasting = DontMoveWhileCasting;
        var migrated = ConfigurationMigration.Apply(ref version, ref dontMoveWhileCasting);

        Version = version;
        DontMoveWhileCasting = dontMoveWhileCasting;
        return ApplyCompactDefaults() || migrated;
    }

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}

internal static class ConfigurationMigration
{
    internal const int CurrentVersion = 2;
    internal const bool DefaultDontMoveWhileCasting = true;

    internal static bool Apply(ref int version, ref bool dontMoveWhileCasting)
    {
        if (version >= CurrentVersion)
            return false;

        if (version < 2)
            dontMoveWhileCasting = true;

        version = CurrentVersion;
        return true;
    }
}
