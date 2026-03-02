using Dalamud.Configuration;
using System;

namespace FrenRider;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // --- Global UI Settings ---
    public bool IsConfigWindowMovable { get; set; } = true;
    public bool DtrBarEnabled { get; set; } = true;
    public int DtrBarMode { get; set; } = 0; // 0=text-only, 1=icon+text, 2=icon-only
    public bool KrangleEnabled { get; set; } = false;
    public float LeftPanelWidth { get; set; } = 240f;

    // --- Account Tracking ---
    public string LastAccountId { get; set; } = "";

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
