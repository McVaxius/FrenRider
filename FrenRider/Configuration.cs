using Dalamud.Configuration;
using System;

namespace FrenRider;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public const int CurrentVersion = ConfigurationMigration.CurrentVersion;

    public int Version { get; set; } = CurrentVersion;

    // --- Global UI Settings ---
    public bool IsConfigWindowMovable { get; set; } = true;
    public bool DtrBarEnabled { get; set; } = true;
    public int DtrBarMode { get; set; } = 0; // 0=text-only, 1=icon+text, 2=icon-only
    public string DtrIconEnabled { get; set; } = "\uE03C";
    public string DtrIconDisabled { get; set; } = "\uE03D";
    public bool KrangleEnabled { get; set; } = false;
    public float LeftPanelWidth { get; set; } = 240f;
    public bool DontMoveWhileCasting { get; set; } = ConfigurationMigration.DefaultDontMoveWhileCasting;

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
        return migrated;
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
