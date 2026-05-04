using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace FrenRider.Windows;

internal static class UiHelpers
{
    public static readonly Vector4 Green = new(0.35f, 0.9f, 0.45f, 1f);
    public static readonly Vector4 Blue = new(0.35f, 0.7f, 1f, 1f);
    public static readonly Vector4 Yellow = new(1f, 0.82f, 0.28f, 1f);
    public static readonly Vector4 Orange = new(1f, 0.55f, 0.25f, 1f);
    public static readonly Vector4 Red = new(1f, 0.35f, 0.35f, 1f);
    public static readonly Vector4 Grey = new(0.62f, 0.62f, 0.62f, 1f);
    public static readonly Vector4 Muted = new(0.48f, 0.48f, 0.48f, 1f);

    public static void SectionHeader(string label)
    {
        ImGui.Spacing();
        ImGui.TextColored(Blue, label);
        ImGui.Separator();
    }

    public static void StatusPill(string label, Vector4 color, string? tooltip = null)
    {
        var buttonColor = new Vector4(color.X * 0.35f, color.Y * 0.35f, color.Z * 0.35f, 0.95f);
        var hoveredColor = new Vector4(color.X * 0.45f, color.Y * 0.45f, color.Z * 0.45f, 1f);
        ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoveredColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, buttonColor);
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        var pos = ImGui.GetCursorScreenPos();
        ImGui.SmallButton($"{label}##pill{label}{pos.X:F0}{pos.Y:F0}");
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(4);

        if (!string.IsNullOrWhiteSpace(tooltip) && ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
    }

    public static void AlignedRow(string label, string value, Vector4? valueColor = null, float labelWidth = 138f)
    {
        var labelTextWidth = ImGui.CalcTextSize(label).X;
        if (labelTextWidth + ImGui.GetStyle().ItemSpacing.X > labelWidth)
        {
            ImGui.TextDisabled(label);
            ImGui.Indent(Math.Min(labelWidth, ImGui.GetContentRegionAvail().X * 0.35f));
            SafeWrappedText(value, valueColor);
            ImGui.Unindent();
            return;
        }

        ImGui.TextDisabled(label);
        ImGui.SameLine(labelWidth);
        SafeWrappedText(value, valueColor);
    }

    public static void SafeWrappedText(string text, Vector4? color = null)
    {
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(80f, ImGui.GetContentRegionAvail().X));
        if (color.HasValue)
            ImGui.TextColored(color.Value, text);
        else
            ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }

    public static void WarningStrip(string text)
    {
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.34f, 0.10f, 0.08f, 0.55f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4f);
        ImGui.BeginChild($"##WarningStrip{text.GetHashCode()}", new Vector2(0f, ImGui.GetTextLineHeightWithSpacing() * 2.4f), true);
        SafeWrappedText(text, Red);
        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }
}
