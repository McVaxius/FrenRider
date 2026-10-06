using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using AethertekUI;

namespace FrenRider.Windows;

internal static class UiHelpers
{
    private const float MinWrapWidth = 80f;

    public static readonly Vector4 Green = new(0.35f, 0.9f, 0.45f, 1f);
    public static readonly Vector4 Blue = new(0.35f, 0.7f, 1f, 1f);
    public static readonly Vector4 Yellow = new(1f, 0.82f, 0.28f, 1f);
    public static readonly Vector4 Orange = new(1f, 0.55f, 0.25f, 1f);
    public static readonly Vector4 Red = new(1f, 0.35f, 0.35f, 1f);
    public static readonly Vector4 Grey = new(0.62f, 0.62f, 0.62f, 1f);
    public static readonly Vector4 Muted = new(0.48f, 0.48f, 0.48f, 1f);

    public static float Scale(float value)
        => value * Math.Max(0.01f, ImGuiHelpers.GlobalScale);

    public static Vector2 Scale(Vector2 value)
        => value * Math.Max(0.01f, ImGuiHelpers.GlobalScale);

    public static void SectionHeader(string label)
    {
        ImGui.Spacing();
        MaterialText.TextColored(MaterialTheme.Current.Colors.Secondary, UiText.T(label));
        ImGui.Separator();
    }

    public static void StatusPill(string label, Vector4 color, string? tooltip = null)
    {
        var buttonColor = new Vector4(color.X * 0.35f, color.Y * 0.35f, color.Z * 0.35f, 0.95f);
        var hoveredColor = new Vector4(color.X * 0.45f, color.Y * 0.45f, color.Z * 0.45f, 1f);
        ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoveredColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, buttonColor);
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        var pos = ImGui.GetCursorScreenPos();
        var text = UiText.T(label);
        using var lineHeight = MaterialText.PushLineHeight(text);
        var scale = MaterialTheme.Metrics.Scale;
        var padding = ImGui.GetStyle().FramePadding;
        var size = new Vector2(MathF.Ceiling(MaterialText.Measure(text).X + padding.X * 2 + 20 * scale),
            Math.Max(MaterialControls.Metrics.Height, ImGui.GetFrameHeight()));
        ImGui.Button($"{label}##pill{label}{pos.X:F0}{pos.Y:F0}", size);
        var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(4);
        var ink = color; ink.W *= ImGui.GetStyle().Alpha;
        var dl = ImGui.GetWindowDrawList();
        dl.AddCircleFilled(new Vector2(min.X + padding.X + 6 * scale, (min.Y + max.Y) * .5f), 6 * scale, ImGui.ColorConvertFloat4ToU32(ink), 24);
        MaterialText.AddText(dl,new Vector2(min.X + padding.X + 20 * scale, min.Y + (size.Y - MaterialText.Measure(text).Y) * .5f), ImGui.ColorConvertFloat4ToU32(ink), text);
        dl.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(color.X, color.Y, color.Z, .35f * ImGui.GetStyle().Alpha)), Scale(4));
        if (!string.IsNullOrWhiteSpace(tooltip) && ImGui.IsItemHovered())
            UiGui.SetTooltip(tooltip);
    }

    public static void AlignedRow(string label, string value, Vector4? valueColor = null, float labelWidth = 138f)
    {
        label = UiText.T(label);
        var rowStartX = ImGui.GetCursorPosX();
        var scaledLabelWidth = Scale(labelWidth);
        var itemSpacing = ImGui.GetStyle().ItemSpacing.X;
        var labelTextWidth = MaterialText.Measure(label).X;
        if (labelTextWidth + itemSpacing > scaledLabelWidth)
        {
            MaterialText.TextDisabled(label);
            ImGui.SetCursorPosX(rowStartX + Math.Min(scaledLabelWidth, ImGui.GetContentRegionAvail().X * 0.35f));
            SafeWrappedText(value, valueColor);
            ImGui.SetCursorPosX(rowStartX);
            return;
        }

        MaterialText.TextDisabled(label);
        ImGui.SameLine(rowStartX + scaledLabelWidth);
        SafeWrappedText(value, valueColor);
        ImGui.SetCursorPosX(rowStartX);
    }

    public static void SafeWrappedText(string text, Vector4? color = null, bool localize = true)
    {
        if (localize) text = UiText.T(text);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(Scale(MinWrapWidth), ImGui.GetContentRegionAvail().X));
        try
        {
        if (color.HasValue)
            MaterialText.TextColored(color.Value, text);
        else
            MaterialText.Text(text);
        }
        finally { ImGui.PopTextWrapPos(); }
    }

    public static void ReadOnlyField(string text, Vector4? color = null, bool localize = true)
    {
        if (localize) text = UiText.T(text);
        var c = MaterialTheme.Current.Colors;
        var origin = ImGui.GetCursorScreenPos();
        var padding = ImGui.GetStyle().FramePadding;
        var width = Math.Max(Scale(40), ImGui.GetContentRegionAvail().X);
        var size = MaterialText.Measure(text, false, Math.Max(1, width - padding.X * 2));
        var height = Math.Max(MaterialControls.Metrics.Height, size.Y + padding.Y * 2);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, origin + new Vector2(width, height), MaterialCanvas.Color(c.SurfaceContainer), Scale(4));
        dl.AddRect(origin, origin + new Vector2(width, height), MaterialCanvas.Color(c.OutlineVariant), Scale(4));
        MaterialText.AddText(dl,ImGui.GetFont(), ImGui.GetFontSize(), origin + padding, MaterialCanvas.Color(color ?? c.OnSurface), text, Math.Max(1, width - padding.X * 2));
        ImGui.Dummy(new Vector2(width, height));
    }

    public static void WarningStrip(string text)
    {
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.34f, 0.10f, 0.08f, 0.55f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4f);
        var padding = ImGui.GetStyle().WindowPadding;
        var width = Math.Max(Scale(MinWrapWidth), ImGui.GetContentRegionAvail().X - padding.X * 2);
        var height = Math.Max(ImGui.GetTextLineHeightWithSpacing() * 2.4f,
            MaterialText.Measure(UiText.T(text), false, width).Y + padding.Y * 2);
        ImGui.BeginChild($"##WarningStrip{text.GetHashCode()}", new Vector2(0f, MathF.Ceiling(height)), true);
        try { SafeWrappedText(text, Red); }
        finally
        {
            ImGui.EndChild();
            ImGui.PopStyleVar();
            ImGui.PopStyleColor();
        }
    }
}
