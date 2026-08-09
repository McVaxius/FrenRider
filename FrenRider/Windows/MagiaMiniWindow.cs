using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using FrenRider.Services;

namespace FrenRider.Windows;

public sealed class MagiaMiniWindow : Window, IDisposable
{
    public MagiaMiniWindow()
        : base("Fren Rider Mini###FrenRiderMagiaMini")
    {
        Flags = ImGuiWindowFlags.AlwaysAutoResize
                | ImGuiWindowFlags.NoScrollbar
                | ImGuiWindowFlags.NoScrollWithMouse;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        ImGui.TextUnformatted("MAGIA");

        if (ImGui.Button("Attack"))
            GameHelpers.SendChatCommand("/magiaauto attack", "MAGIA mini");

        ImGui.SameLine();
        if (ImGui.Button("Defense"))
            GameHelpers.SendChatCommand("/magiaauto defense", "MAGIA mini");

        ImGui.SameLine();
        if (ImGui.Button("Off"))
            GameHelpers.SendChatCommand("/magiaauto off", "MAGIA mini");
    }
}
