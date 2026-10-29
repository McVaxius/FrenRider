using Dalamud.Configuration;
using System;

namespace FrenRider;

public enum FrenRiderUiStyle { Classic, Glassmorphism, Spatial, Cyberpunk }

[Serializable]
public class Configuration : IPluginConfiguration
{
    public const int CurrentVersion = ConfigurationMigration.CurrentVersion;
    internal const uint DefaultGlassWave1Rgb = 0x65C7D2;
    internal static readonly uint DefaultGlassWave2Rgb = DefaultGlassWaveRgb(1.2f);
    internal static readonly uint DefaultGlassWave3Rgb = DefaultGlassWaveRgb(2.35f);
    internal const uint DefaultLcarsBackgroundRgb = 0x07090D;
    internal const uint DefaultLcarsBar1Rgb = 0xFFAA66;
    internal const uint DefaultLcarsBar2Rgb = 0xCC99CC;
    internal const uint DefaultLcarsBar3Rgb = 0x9999FF;

    internal static AethertekUI.MaterialColorSettings DefaultPalette(FrenRiderUiStyle style) => style switch
    {
        FrenRiderUiStyle.Glassmorphism => new()
            { Mode = AethertekUI.MaterialColorMode.Picker, BackgroundRgb = 0x10232D, ControlsRgb = DefaultGlassWave1Rgb, TextRgb = 0xEDF7FA },
        FrenRiderUiStyle.Spatial => new()
            { Mode = AethertekUI.MaterialColorMode.Picker, BackgroundRgb = 0xE8EDF4, ControlsRgb = 0x6678A8, TextRgb = 0x1C2538 },
        FrenRiderUiStyle.Cyberpunk => new()
            { Mode = AethertekUI.MaterialColorMode.Picker, BackgroundRgb = 0x0B0D13, ControlsRgb = 0x19DCE8, TextRgb = 0xEAF8FF },
        _ => new(),
    };

    private static uint DefaultGlassWaveRgb(float angle)
    {
        var primary = new System.Numerics.Vector3(((DefaultGlassWave1Rgb >> 16) & 255) / 255f,
            ((DefaultGlassWave1Rgb >> 8) & 255) / 255f, (DefaultGlassWave1Rgb & 255) / 255f);
        var lch = AethertekUI.MaterialColor.LabToLch(AethertekUI.MaterialColor.SrgbToOklab(primary));
        return AethertekUI.MaterialColorSettings.ToRgb(
            AethertekUI.MaterialColor.GamutMap(lch.X, lch.Y, lch.Z + angle));
    }

    internal static float NormalizeGlassAnimationSpeed(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, .1f, 10f) : 1f;

    public int Version { get; set; } = CurrentVersion;

    // --- Global UI Settings ---
    public string UiLanguage { get; set; } = "en";
    public uint UiAccentRgb { get; set; } = 0x70C677;
    public AethertekUI.MaterialColorSettings UiColors { get; set; } = new();
    public FrenRiderUiStyle UiStyle { get; set; }
    public bool UiStylingEnabled { get; set; } = true;
    public bool UiWindowsLocked { get; set; }
    public AethertekUI.MaterialColorSettings UiGlassColors { get; set; } = DefaultPalette(FrenRiderUiStyle.Glassmorphism);
    public AethertekUI.MaterialColorSettings UiSpatialColors { get; set; } = DefaultPalette(FrenRiderUiStyle.Spatial);
    public AethertekUI.MaterialColorSettings UiCyberpunkColors { get; set; } = DefaultPalette(FrenRiderUiStyle.Cyberpunk);
    public uint UiGlassWave1Rgb { get; set; } = DefaultGlassWave1Rgb;
    public uint UiGlassWave2Rgb { get; set; } = DefaultGlassWave2Rgb;
    public uint UiGlassWave3Rgb { get; set; } = DefaultGlassWave3Rgb;
    public uint UiLcarsBackgroundRgb { get; set; } = DefaultLcarsBackgroundRgb;
    public uint UiLcarsBar1Rgb { get; set; } = DefaultLcarsBar1Rgb;
    public uint UiLcarsBar2Rgb { get; set; } = DefaultLcarsBar2Rgb;
    public uint UiLcarsBar3Rgb { get; set; } = DefaultLcarsBar3Rgb;
    public bool UiGlassAnimation { get; set; } = true;
    private float uiGlassAnimationSpeed = 1f;
    public float UiGlassAnimationSpeed
    {
        get => uiGlassAnimationSpeed;
        set => uiGlassAnimationSpeed = NormalizeGlassAnimationSpeed(value);
    }
    public bool UiLcarsSoundsMuted { get; set; } = true;
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
    public bool UiShowTitlebarStatus { get; set; } = true;
    public bool UiDetailedTitlebarStatus { get; set; } = true;
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
