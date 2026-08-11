using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Windows;

public sealed class MagiaMiniWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MagiaMiniWindow(Plugin plugin)
        : base("Fren Rider Mini###FrenRiderMagiaMini")
    {
        this.plugin = plugin;
        Flags = ImGuiWindowFlags.AlwaysAutoResize
                | ImGuiWindowFlags.NoScrollbar
                | ImGuiWindowFlags.NoScrollWithMouse;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        var config = plugin.ConfigManager.GetActiveConfig();

        var enabled = config.Enabled;
        if (ImGui.Checkbox("FrenRider", ref enabled))
            plugin.ConfigManager.SetFrenRiderEnabled(enabled);

        ImGui.SameLine();
        ImGui.TextDisabled(enabled ? "ON" : "OFF");

        DrawFrenSelector(config);

        if (!IsEurekaTerritory(plugin.ZoneService.TerritoryId))
            return;

        ImGui.Separator();
        ImGui.TextUnformatted("MAGIA");

        if (ImGui.Button("Attack"))
            GameHelpers.SendChatCommand("/magiaauto attack", "Fren Rider mini");

        ImGui.SameLine();
        if (ImGui.Button("Defense"))
            GameHelpers.SendChatCommand("/magiaauto defense", "Fren Rider mini");

        ImGui.SameLine();
        if (ImGui.Button("Off"))
            GameHelpers.SendChatCommand("/magiaauto off", "Fren Rider mini");
    }

    private void DrawFrenSelector(CharacterConfig config)
    {
        ImGui.TextUnformatted("Fren");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220f);

        var preview = string.IsNullOrWhiteSpace(config.FrenName)
            ? "<none>"
            : Display(config.FrenName);

        if (!ImGui.BeginCombo("##FrenRiderMiniFren", preview))
            return;

        var noneSelected = string.IsNullOrWhiteSpace(config.FrenName);
        if (ImGui.Selectable("<none>", noneSelected))
        {
            config.FrenName = string.Empty;
            plugin.ConfigManager.SaveCurrentAccount();
        }

        if (noneSelected)
            ImGui.SetItemDefaultFocus();

        var partyCount = Plugin.PartyList.Length;
        if (partyCount == 0)
        {
            ImGui.TextDisabled("Not in a party");
            ImGui.EndCombo();
            return;
        }

        for (var i = 0; i < partyCount; i++)
        {
            var member = Plugin.PartyList[i];
            if (member == null)
                continue;

            var memberName = member.Name.ToString();
            var worldName = member.World.Value.Name.ToString();
            var identity = $"{memberName}@{worldName}";
            var selected = string.Equals(config.FrenName, identity, StringComparison.OrdinalIgnoreCase);

            if (ImGui.Selectable(Display(identity), selected))
            {
                config.FrenName = identity;
                plugin.ConfigManager.SaveCurrentAccount();
            }

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private string Display(string identity)
        => plugin.Configuration.KrangleEnabled
            ? KrangleService.KrangleName(identity)
            : identity;

    private static bool IsEurekaTerritory(uint territoryId)
        => territoryId is 732 or 763 or 795 or 827;
}
