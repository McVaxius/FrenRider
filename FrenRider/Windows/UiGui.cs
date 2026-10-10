using System;
using System.Linq;
using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Windows;

// Native widgets receive their original English labels/IDs. Only their visible label is painted in the selected locale.
// This also preserves English-derived helper IDs and existing saved window identities.
internal static class UiGui
{
    private static uint settingWindow;
    private static float settingFieldWidth;
    private static string settingLabel = "";
    private static bool settingLabelDrawn;
    private static float sectionLabelWidth;
    private static float settingRowX;
    internal static bool SettingsDefaultProfile { get; set; }
    internal static void ResetSettingsAlignment() => sectionLabelWidth = 0;
    internal static bool InSettingRow => settingWindow != 0 && ImGuiP.GetCurrentWindow().ID == settingWindow;
    internal static void SettingFieldWidth(float width) => settingFieldWidth = Math.Max(1, width);
    internal static SettingRowScope SettingRow(string label) => new(label);
    internal static bool SettingColumn(int column)
    {
        if (!InSettingRow) return false;
        if (column is 2 or 3 && settingLabelDrawn) ImGui.SameLine();
        return true;
    }

    internal static void SettingLabel()
    {
        if (!InSettingRow || settingLabelDrawn) return;
        ImGui.AlignTextToFramePadding();
        MaterialText.Text(UiText.T(ShortLabel(settingLabel.Split("##", 2)[0])));
        settingLabelDrawn = true;
    }

    internal static void SettingsSection(string label, params string[] fieldLabels)
    {
        sectionLabelWidth = fieldLabels.Select(field => MaterialText.Measure(UiText.T(ShortLabel(field))).X).DefaultIfEmpty(0).Max();
        MaterialSettings.Section(UiText.T(label));
    }
    internal static float SettingsMinimum(params string[] labels)
        => Math.Max(390 * MaterialTheme.Metrics.Scale, labels.Select(label => MaterialText.Measure(UiText.T(ShortLabel(label))).X)
            .DefaultIfEmpty(0).Max() + 210 * MaterialTheme.Metrics.Scale
                + (SettingsDefaultProfile ? MaterialText.Measure(UiText.T("Sync all")).X + 2 * ImGui.GetStyle().FramePadding.X : 0));

    private static string ShortLabel(string label) => label switch
    {
        "Fly You Fools (fly alongside instead of pillion)" => "Fly You Fools",
        "Try Teleport to Fren When Out of Zone" => "Teleport to fren",
        "Nudge in duty when fren not nearby/in-zone" => "Nudge in duty when fren is absent",
        "Use Phoenix Downs for recovery" => "Use Phoenix Downs",
        "Revive anyone within range outdoors" => "Revive anyone outdoors",
        "Allow Phoenix Down use during combat" => "Allow use during combat",
        "Respawn after death outside duties after" => "Respawn outside duties",
        "Respawn after death inside duties after" => "Respawn inside duties",
        "Mount-up to chase fren if >" => "Mount-up to chase",
        "Mount Name (if flying solo)" => "Mount Name",
        "Update Interval (seconds)" => "Update Interval",
        "Social Distance (yalms)" => "Social Distance",
        "Harmonized Cling Reset Ticks" => "Harmonized reset",
        "Rotation Plugin (Foray)" => "Rotation Plugin (Foray)",
        "Configure rotation preset manually" => "Configure rotation preset manually",
        "BMR reduce activation range for outdoor areas" => "Reduce outdoor activation range",
        "BMR Disable Hunt Modules" => "Disable Hunt Modules",
        "BMR Disable Queen Lunatender" => "Disable Queen Lunatender",
        "Capture HP: more than 5 levels below you" => "Capture HP: >5 levels below",
        "Capture HP: within 5 levels below you or equal" => "Capture HP: within 5 levels",
        "Repair At % Durability" => "Repair At",
        "Automatically allocate Chocobo skills" => "Automatically allocate skills",
        "First skill tree" => "First tree",
        "Second skill tree (optional)" => "Second tree",
        "Third skill tree (optional)" => "Third tree",
        _ => label,
    };

    internal ref struct SettingRowScope
    {
        private MaterialStyleScope style;
        private readonly uint previousWindow;
        private readonly float previousWidth;
        private readonly string previousLabel;
        private readonly bool previousDrawn;
        private readonly float previousRowX;
        public SettingRowScope(string label)
        {
            previousWindow = settingWindow;
            previousWidth = settingFieldWidth;
            previousLabel = settingLabel;
            previousDrawn = settingLabelDrawn;
            previousRowX = settingRowX;
            settingRowX = ImGui.GetCursorPosX();
            settingFieldWidth = 0;
            settingLabel = label;
            settingLabelDrawn = false;
            style = new MaterialStyleScope();
            style.Style(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, 2 * MaterialTheme.Metrics.Scale));
            ImGui.BeginGroup();
            settingWindow = ImGuiP.GetCurrentWindow().ID;
        }
        public void Dispose()
        {
            settingWindow = previousWindow;
            settingFieldWidth = previousWidth;
            settingLabel = previousLabel;
            settingLabelDrawn = previousDrawn;
            settingRowX = previousRowX;
            ImGui.EndGroup();
            style.Dispose();
        }
    }
    internal static void DrawCompanionPurchases(Plugin plugin, CharacterConfig config, bool mini)
    {
        using var font = UiText.Font(UiFontRole.Action);
        using var controls = MaterialControls.Push(MaterialControlContext.Toolbar);
        var available = Plugin.ClientState.IsLoggedIn
            && plugin.ConfigManager.TryGetActiveConfig(out var active) && ReferenceEquals(active, config);
        var greens = available ? GameHelpers.GetCompanionSupplyStock((int)GameHelpers.GysahlGreensItemId) : -1;
        var food = available ? GameHelpers.GetCompanionSupplyStock(config.ChocoboFoodItemId) : -1;
        var greensCount = greens < 0 ? "-" : greens.ToString();
        var foodCount = food < 0 ? "-" : food.ToString();
        var greensLabel = mini ? greensCount : UiText.T("BUY GREENS") + " · " + greensCount;
        var foodLabel = mini ? foodCount : UiText.T("BUY FOOD") + " · " + foodCount;
        var stopLabel = UiText.T("Stop companion purchasing");

        ImGui.BeginDisabled(!available);
        try
        {
            if (Button("BUY GREENS", display: greensLabel, icon: MaterialIcon.Cart)) plugin.PurchaseChocoboGreensNow();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                MaterialText.SetTooltip(UiText.T("BUY GREENS") + "\n" + UiText.F("Gysahl Greens: {0}", greensCount)
                    + "\n" + UiText.T("Gysahl Greens stock target") + ": " + config.ChocoboGreensStockTarget);
            ContinuePurchaseAction(foodLabel, icon: true);
            if (Button("BUY FOOD", display: foodLabel, icon: MaterialIcon.Utensils)) plugin.PurchaseChocoboFoodNow();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                MaterialText.SetTooltip(UiText.T("BUY FOOD") + "\n" + UiText.F("Selected food: {0} | NQ stock: {1} | Target: {2}",
                    UiText.T(ConfigWindow.ChocoboFoodName(config.ChocoboFoodItemId)), foodCount, config.ChocoboFoodStockTarget)
                    + "\n\n" + ConfigWindow.ChocoboFoodTooltip(config.ChocoboFoodItemId)
                    + "\n" + UiText.T("Buying companion food from Vath requires beast tribe progression through The Naming of Vath. Purchase is unavailable until the vendor is unlocked."));
        }
        finally { ImGui.EndDisabled(); }
        ContinuePurchaseAction(mini ? "" : stopLabel, icon: true);
        if (mini ? IconButton("Stop companion purchasing", MaterialIcon.Stop)
            : Button("Stop companion purchasing", icon: MaterialIcon.Stop)) plugin.StopChocoboPurchasing();
        if (ImGui.IsItemHovered()) MaterialText.SetTooltip(stopLabel + "\n" + UiText.T(plugin.ChocoboPurchaseStatus));
    }

    private static void ContinuePurchaseAction(string display, bool icon)
    {
        var width = display.Length == 0 ? Math.Max(ImGui.GetFrameHeight(), MaterialControls.Metrics.Height)
            : MaterialText.Measure(display).X + (icon ? ImGui.GetTextLineHeight() + UiHelpers.Scale(8) : 0)
                + ImGui.GetStyle().FramePadding.X * 2;
        var right = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + width <= right) ImGui.SameLine();
    }

    internal static void TextUnformatted(string text) => MaterialText.Text(UiText.T(text));
    internal static void TextWrapped(string text) => MaterialText.TextWrapped(UiText.T(text));
    internal static void TextDisabled(string text)
    {
        ImGui.PushTextWrapPos(0);
        try { MaterialText.TextDisabled(UiText.T(text)); }
        finally { ImGui.PopTextWrapPos(); }
    }
    internal static void Text(string text) => MaterialText.Text(UiText.T(text));
    internal static void BulletText(string text) => MaterialText.BulletText(UiText.T(text));
    internal static void TextColored(Vector4 color, string text) => MaterialText.TextColored(color, UiText.T(text));
    internal static void SetTooltip(string text) => MaterialText.SetTooltip(UiText.T(text));
    internal static bool BeginCombo(string label, string preview, ImGuiComboFlags flags = ImGuiComboFlags.None, bool literalPreview = false)
    {
        BeginField(label);
        try
        {
            var open = MaterialText.BeginCombo("", literalPreview ? preview : UiText.T(preview), flags);
            if (!open) ImGui.PopID();
            return open;
        }
        catch { ImGui.PopID(); throw; }
    }
    internal static void EndCombo() { ImGui.EndCombo(); ImGui.PopID(); }
    internal static bool Combo(string label, ref int value, string options)
    {
        var items = options.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        return Combo(label, ref value, items, items.Length);
    }
    internal static bool BeginTabItem(string label, ImGuiTabItemFlags flags = ImGuiTabItemFlags.None)
    {
        var display = UiText.T(label);
        using var height = MaterialText.PushLineHeight(display);
        var pad = ImGui.GetStyle().FramePadding;
        var foreground = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        ImGui.SetNextItemWidth(MathF.Ceiling(Math.Max(MaterialText.Measure(display).X, MaterialText.Measure(label).X) + pad.X * 2));
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        var open = ImGui.BeginTabItem(label, flags);
        ImGui.PopStyleColor();
        foreground.W *= ImGui.GetStyle().Alpha;
        var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(min, max, true);
        try
        {
        MaterialText.AddText(dl,min + (max - min - MaterialText.Measure(display)) * .5f, ImGui.ColorConvertFloat4ToU32(foreground), display);
        }
        catch { if (open) ImGui.EndTabItem(); throw; }
        finally { dl.PopClipRect(); }
        return open;
    }
    internal static bool RadioButton(string label, bool active)
    {
        var display = UiText.T(InSettingRow ? ShortLabel(label) : label);
        if (InSettingRow) settingLabelDrawn = true;
        using var height = MaterialText.PushLineHeight(display);
        var gap = ImGui.GetStyle().ItemInnerSpacing;
        var foreground = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, new Vector2(gap.X + Math.Max(0, MaterialText.Measure(display).X - MaterialText.Measure(label).X), gap.Y));
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        var clicked = ImGui.RadioButton(label, active);
        ImGui.PopStyleColor(); ImGui.PopStyleVar();
        foreground.W *= ImGui.GetStyle().Alpha;
        MaterialText.AddText(ImGui.GetWindowDrawList(),ImGui.GetItemRectMin() + new Vector2(ImGui.GetFrameHeight() + gap.X, ImGui.GetStyle().FramePadding.Y), ImGui.ColorConvertFloat4ToU32(foreground), display);
        return clicked;
    }
    internal static bool Button(string label, Vector2 size, MaterialIcon? icon = null, Vector4? gradientBottom = null)
    {
        var translated = UiText.T(label.Split("##", 2)[0]);
        using var controls = ImGui.GetStyle().FramePadding.Y == 0 || MaterialControls.Context == MaterialControlContext.Dense
            ? default(MaterialControls.ControlScope) : MaterialControls.Push(MaterialControlContext.Toolbar);
        var paddingY = ImGui.GetStyle().FramePadding.Y;
        using var height = MaterialText.PushLineHeight(translated);
        size.Y = Math.Max(size.Y, Math.Max(ImGui.GetTextLineHeight(), MaterialText.Measure(translated).Y) + 2 * paddingY);
        var foreground = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        var iconSize = ImGui.GetTextLineHeight();
        var iconWidth = icon.HasValue ? iconSize + 8 * MaterialTheme.Metrics.Scale : 0;
        size.X = MaterialLayout.FitNextItemWidth(size.X, MaterialText.Measure(translated).X + iconWidth + ImGui.GetStyle().FramePadding.X * 2);
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        var clicked = ImGui.Button(label, size);
        ImGui.PopStyleColor();
        var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(min, max, true);
        try
        {
        if (gradientBottom.HasValue)
        {
            var fill = ImGui.GetStyle().Colors[(int)(ImGui.IsItemActive() ? ImGuiCol.ButtonActive : ImGui.IsItemHovered() ? ImGuiCol.ButtonHovered : ImGuiCol.Button)];
            MaterialCanvas.Surface(min, max, fill, gradientBottom.Value, ImGui.GetStyle().FrameRounding);
            dl.AddRect(min, max, MaterialCanvas.Color(MaterialColor.Layer(fill, Vector4.One, .16f)), ImGui.GetStyle().FrameRounding);
            if (ImGui.IsItemFocused() && ImGui.GetIO().NavVisible) dl.AddRect(min + Vector2.One, max - Vector2.One, MaterialCanvas.Color(MaterialTheme.Current.Colors.Primary), ImGui.GetStyle().FrameRounding, ImDrawFlags.None, 2);
        }
        foreground.W *= ImGui.GetStyle().Alpha;
        var textSize = MaterialText.Measure(translated);
        var position = min + (max - min - new Vector2(textSize.X + iconWidth, Math.Max(textSize.Y, iconSize))) * .5f;
        if (icon.HasValue) MaterialIcons.Draw(icon.Value, position, iconSize, foreground, 1);
        MaterialText.AddText(dl,position + new Vector2(iconWidth, 0), ImGui.ColorConvertFloat4ToU32(foreground), translated);
        }
        finally { dl.PopClipRect(); }
        return clicked;
    }

    // Native controls retain the original ID, hit testing, focus and keyboard behavior.
    internal static bool HeaderToggle(string original, ref bool value, float minimumLabelWidth = 0, Vector2? trackSize = null)
    {
        var s = MaterialTheme.Metrics.Scale;
        var c = MaterialTheme.Current.Colors;
        var text = UiText.T(original);
        var size = trackSize ?? new Vector2(44, 26);
        var radius = size.Y * .5f;
        var width = Math.Max(MaterialText.Measure(text).X, minimumLabelWidth * s) + (size.X + 8) * s;
        var textSize = MaterialText.Measure(text);
        var height = Math.Max(Math.Max(28, size.Y) * s, Math.Max(ImGui.GetTextLineHeight(), textSize.Y) + 4 * s);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(0, Math.Max(0, (height - ImGui.GetTextLineHeight()) * .5f)));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, new Vector2(Math.Max(0, width - ImGui.GetFrameHeight() - MaterialText.Measure(original).X), 0));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0);
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.CheckMark, Vector4.Zero);
        var changed = ImGui.Checkbox(original, ref value);
        ImGui.PopStyleColor(5);
        ImGui.PopStyleVar(3);
        var min = ImGui.GetItemRectMin();
        var track = min + new Vector2(width - size.X * s, Math.Max(0, (height - size.Y * s) * .5f));
        var fill = value ? c.Primary : c.Outline;
        if (ImGui.IsItemHovered()) fill = MaterialColor.Layer(fill, c.OnSurface, .08f);
        if (ImGui.IsItemActive()) fill = MaterialColor.Layer(fill, c.OnSurface, .12f);
        var foreground = c.OnSurface;
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(track, track + size * s, MaterialCanvas.Color(fill), radius * s);
        dl.AddCircleFilled(track + new Vector2(value ? size.X - radius : radius, radius) * s, (radius - 3) * s, MaterialCanvas.Color(foreground), 24);
        if (ImGui.IsItemFocused() && ImGui.GetIO().NavVisible) dl.AddRect(min, min + new Vector2(width, height), MaterialCanvas.Color(c.Primary), 4 * s);
        MaterialText.AddText(dl,min + new Vector2(0, Math.Max(0, (height - textSize.Y) * .5f)), MaterialCanvas.Color(foreground), text);
        return changed;
    }

    internal static bool SliderFloat(string label, ref float value, float min, float max, string format)
    {
        BeginField(label);
        try { return ImGui.SliderFloat("", ref value, min, max, format); }
        finally { ImGui.PopID(); }
    }
    internal static bool CollapsingHeader(string label, ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.None)
    {
        using var height = MaterialText.PushLineHeight(UiText.T(label.Split("##", 2)[0]));
        var position = ImGui.GetCursorScreenPos();
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        var open = ImGui.CollapsingHeader(label, flags);
        ImGui.PopStyleColor();
        var c = MaterialTheme.Current.Colors;
        var foreground = c.OnSurface;
        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), true);
        try
        {
        ImGuiP.RenderArrow(dl, position + ImGui.GetStyle().FramePadding, MaterialCanvas.Color(foreground), open ? ImGuiDir.Down : ImGuiDir.Right, .7f);
        MaterialText.AddText(dl,position + new Vector2(ImGui.GetFontSize() + ImGui.GetStyle().FramePadding.X * 2, ImGui.GetStyle().FramePadding.Y), MaterialCanvas.Color(foreground), UiText.T(label));
        }
        finally { dl.PopClipRect(); }
        return open;
    }
    internal static bool Button(string label,string? display=null,MaterialIcon? icon=null)
    {
        var translated=display ?? UiText.T(label.Split("##",2)[0]);
        using var controls = ImGui.GetStyle().FramePadding.Y == 0 || MaterialControls.Context == MaterialControlContext.Dense
            ? default(MaterialControls.ControlScope) : MaterialControls.Push(MaterialControlContext.Toolbar);
        using var height = MaterialText.PushLineHeight(translated);
        var iconSize=ImGui.GetTextLineHeight();
        var iconWidth=icon.HasValue?iconSize+8*MaterialTheme.Metrics.Scale:0;
        var width=MaterialLayout.FitNextItemWidth(0,MaterialText.Measure(translated).X+iconWidth+2*ImGui.GetStyle().FramePadding.X);
        var foreground=ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);
        var clicked=ImGui.Button(label,new Vector2(width,0));
        ImGui.PopStyleColor();
        var min=ImGui.GetItemRectMin(); var max=ImGui.GetItemRectMax();
        foreground.W*=ImGui.GetStyle().Alpha;
        ImGui.GetWindowDrawList().PushClipRect(min,max,true);
        try
        {
        var textSize=MaterialText.Measure(translated);
        var position=min+(max-min-new Vector2(textSize.X+iconWidth,Math.Max(textSize.Y,iconSize)))*.5f;
        if(icon.HasValue) MaterialIcons.Draw(icon.Value,position,iconSize,foreground,1);
        MaterialText.AddText(ImGui.GetWindowDrawList(),position+new Vector2(iconWidth,0),ImGui.ColorConvertFloat4ToU32(foreground),translated);
        }
        finally { ImGui.GetWindowDrawList().PopClipRect(); }
        return clicked;
    }
    internal static bool SmallButton(string label,string? display=null)
    {
        // Native small buttons use the same ID and behavior with zero vertical padding.
        using var padding = new MaterialStyleScope();
        padding.Style(ImGuiStyleVar.FramePadding,new Vector2(ImGui.GetStyle().FramePadding.X,0));
        return Button(label,display);
    }
    internal static bool IconButton(string original, MaterialIcon icon)
    {
        var size = Math.Max(ImGui.GetFrameHeight(), MaterialControls.Metrics.Height);
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
        var clicked = ImGui.Button(original, new Vector2(size));
        ImGui.PopStyleColor();
        var min = ImGui.GetItemRectMin();
        MaterialIcons.Draw(icon, min + new Vector2(size * .2f), size * .6f, MaterialTheme.Current.Colors.OnSurface);
        if (ImGui.IsItemHovered()) SetTooltip(original);
        return clicked;
    }
    internal static bool Checkbox(string label,ref bool value)
    {
        var visible=label.Split("##",2)[0];
        var translated=UiText.T(InSettingRow ? ShortLabel(settingLabel.Split("##",2)[0]) : visible);
        if (InSettingRow) settingLabelDrawn = true;
        using var height = MaterialText.PushLineHeight(translated);
        var foreground=ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        var gap=ImGui.GetStyle().ItemInnerSpacing;
        // Native Checkbox sizes its hit area from the original label. Adjust that size for the
        // translated ink while keeping the native widget and its original ID.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing,new Vector2(InSettingRow
            ? gap.X+MaterialText.Measure(translated).X-MaterialText.Measure(visible).X
            : Math.Max(0,gap.X+MaterialText.Measure(translated).X-MaterialText.Measure(visible).X),gap.Y));
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);
        var changed=ImGui.Checkbox(label,ref value);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        var p=ImGui.GetItemRectMin()+new Vector2(ImGui.GetFrameHeight()+gap.X,ImGui.GetStyle().FramePadding.Y);
        foreground.W*=ImGui.GetStyle().Alpha;
        MaterialText.AddText(ImGui.GetWindowDrawList(),p,ImGui.ColorConvertFloat4ToU32(foreground),translated);
        return changed;
    }
    internal static bool Selectable(string original,bool selected = false,string? display=null)
    {
        var translated=display ?? UiText.T(original.Split("##",2)[0]);
        var origin=ImGui.GetCursorScreenPos();
        var width=ImGui.GetContentRegionAvail().X;
        var foreground=ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);
        var clicked=ImGui.Selectable(original,selected,ImGuiSelectableFlags.None,new Vector2(0,Math.Max(ImGui.GetTextLineHeight(), MaterialText.Measure(translated).Y)));
        ImGui.PopStyleColor();
        foreground.W*=ImGui.GetStyle().Alpha;
        var dl=ImGui.GetWindowDrawList();
        MaterialText.AddText(dl,origin,ImGui.ColorConvertFloat4ToU32(foreground),translated);
        if(MaterialText.Measure(translated).X>width && ImGui.IsItemHovered()) MaterialText.SetTooltip(translated);
        return clicked;
    }
    private static void BeginField(string label, bool hasStepButtons = false)
    {
        if (InSettingRow)
        {
            var requestedWidth = settingFieldWidth > 0 ? settingFieldWidth : ImGui.CalcItemWidth();
            if (!settingLabelDrawn)
            {
                SettingLabel();
                ImGui.SameLine();
                if (sectionLabelWidth > 0)
                    ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), settingRowX + sectionLabelWidth + ImGui.GetStyle().ItemSpacing.X));
            }
            else ImGui.SameLine();
            var available = Math.Max(1, ImGui.GetContentRegionAvail().X);
            var fieldMinimum = Math.Max(70 * MaterialTheme.Metrics.Scale, MaterialText.Measure("000000").X + 2 * ImGui.GetStyle().FramePadding.X);
            if (hasStepButtons) fieldMinimum += 2 * (ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X);
            else if (settingFieldWidth > 0) fieldMinimum = Math.Min(fieldMinimum, settingFieldWidth);
            if (available < fieldMinimum + 26 * MaterialTheme.Metrics.Scale)
            {
                ImGui.NewLine();
                available = Math.Max(1, ImGui.GetContentRegionAvail().X);
            }
            ImGui.SetNextItemWidth(Math.Max(fieldMinimum, Math.Min(requestedWidth, available - 26 * MaterialTheme.Metrics.Scale)));
            settingFieldWidth = 0;
            ImGuiP.PushOverrideID(ImGui.GetID(label));
            return;
        }
        var requested = ImGui.CalcItemWidth();
        var minimum = Math.Max(80 * MaterialTheme.Metrics.Scale,
            MaterialText.Measure("00000000").X + 2 * ImGui.GetStyle().FramePadding.X);
        if (hasStepButtons) minimum += 2 * (ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X);
        var visible = label.Split("##", 2)[0];
        if (visible.Length != 0) MaterialText.Text(UiText.T(visible));
        ImGui.SetNextItemWidth(MaterialLayout.FitNextItemWidth(requested, MathF.Ceiling(minimum)));
        // Empty native labels remove hidden English layout width; overriding the seed retains the original widget ID.
        ImGuiP.PushOverrideID(ImGui.GetID(label));
    }
    internal static bool InputText(string label,ref string value,int length, ImGuiInputTextFlags flags = ImGuiInputTextFlags.None)
    { BeginField(label); try { using var height=MaterialText.PushLineHeight(value); return MaterialShapedInput.SingleLine("","",ref value,length,flags); } finally { ImGui.PopID(); } }
    internal static bool InputInt(string label,ref int value, int step = 0, int fastStep = 0)
    { BeginField(label,step>0); try { return ImGui.InputInt("",ref value,step,fastStep); } finally { ImGui.PopID(); } }
    internal static bool InputFloat(string label,ref float value, float step = 0, float fastStep = 0, string format = "%.3f", ImGuiInputTextFlags flags = ImGuiInputTextFlags.None)
    { BeginField(label,step>0); try { return ImGui.InputFloat("",ref value,step,fastStep,format,flags); } finally { ImGui.PopID(); } }
    internal static bool Combo(string label,ref int value,string[] options,int count)
    {
        var changed=false;
        if(BeginCombo(label,value>=0 && value<count?options[value]:""))
        {
            try
            {
            for(var index=0;index<count;index++)
            {
                ImGui.PushID(index);
                try
                {
                    if(Selectable(options[index],value==index)) { changed=value!=index;value=index; }
                    if(value==index) ImGui.SetItemDefaultFocus();
                }
                finally { ImGui.PopID(); }
            }
            }
            finally { EndCombo(); }
        }
        return changed;
    }
    // Appearance controls retain their native numeric field, format, steps and caption placement.
    internal static bool AppearanceSliderInt(string label, ref int value, int min, int max, string format, ImGuiSliderFlags flags)
    {
        if (InSettingRow)
        {
            BeginField(label);
            try { return ImGui.SliderInt("", ref value, min, max, format, flags); }
            finally { ImGui.PopID(); }
        }
        var nativeLabel = UiText.T(label.Split("##", 2)[0]) + label[label.Split("##", 2)[0].Length..];
        if (!MaterialText.RequiresShaping(nativeLabel)) return ImGui.SliderInt(nativeLabel, ref value, min, max, format, flags);
        using var height = MaterialText.PushLineHeight(UiText.T(label.Split("##", 2)[0]));
        var origin = ImGui.GetCursorScreenPos(); var width = ImGui.CalcItemWidth();
        var drawing = ImGui.GetWindowDrawList(); var parent = ImGuiP.GetCurrentWindow(); var previousMax = parent.DC.CursorMaxPos;
        drawing.PushClipRect(new Vector2(origin.X, parent.Pos.Y), new Vector2(origin.X + width, parent.Pos.Y + parent.Size.Y), true);
        bool changed;
        try { changed = ImGui.SliderInt(label, ref value, min, max, format, flags); }
        finally { drawing.PopClipRect(); }
        AppearanceFieldLabel(label, origin, width, drawing, parent, previousMax);
        return changed;
    }
    internal static bool AppearanceInputFloat(string label, ref float value)
    {
        if (InSettingRow)
        {
            BeginField(label);
            try { return ImGui.InputFloat("", ref value); }
            finally { ImGui.PopID(); }
        }
        var visible = label.Split("##", 2)[0]; var nativeLabel = UiText.T(visible) + label[visible.Length..];
        if (!MaterialText.RequiresShaping(nativeLabel)) return ImGui.InputFloat(nativeLabel, ref value);
        using var height = MaterialText.PushLineHeight(UiText.T(visible));
        var origin = ImGui.GetCursorScreenPos(); var width = ImGui.CalcItemWidth();
        var drawing = ImGui.GetWindowDrawList(); var parent = ImGuiP.GetCurrentWindow(); var previousMax = parent.DC.CursorMaxPos;
        drawing.PushClipRect(new Vector2(origin.X, parent.Pos.Y), new Vector2(origin.X + width, parent.Pos.Y + parent.Size.Y), true);
        bool changed;
        try { changed = ImGui.InputFloat(label, ref value); }
        finally { drawing.PopClipRect(); }
        AppearanceFieldLabel(label, origin, width, drawing, parent, previousMax);
        return changed;
    }
    private static void AppearanceFieldLabel(string label, Vector2 origin, float width, ImDrawListPtr drawing, ImGuiWindowPtr parent, Vector2 previousMax)
    {
        var translated = UiText.T(label.Split("##", 2)[0]);
        var position = origin + new Vector2(width + ImGui.GetStyle().ItemInnerSpacing.X, ImGui.GetStyle().FramePadding.Y);
        MaterialText.AddText(drawing, position, ImGui.GetColorU32(ImGuiCol.Text), translated);
        var right = position.X + MaterialText.Measure(translated).X;
        parent.DC.CursorMaxPos = new Vector2(Math.Max(previousMax.X, right), parent.DC.CursorMaxPos.Y);
        parent.DC.CursorPosPrevLine = new Vector2(right, parent.DC.CursorPosPrevLine.Y);
    }
    internal static Vector2 ScaledTextSize(string text, float scale)
    {
        if (!MaterialText.RequiresShaping(text)) return MaterialText.Measure(text) * scale;
        var callerScale = ImGuiP.GetCurrentWindow().FontWindowScale;
        ImGui.SetWindowFontScale(callerScale * scale);
        try { return MaterialText.Measure(text); }
        finally { ImGui.SetWindowFontScale(callerScale); }
    }

    internal static void Title(string original,string translated,bool people=false,float brandSize=0,bool compactBrand=false)
        => TitleWithButtons(original, translated, null, people, brandSize, compactBrand);

    internal static float TitleMinimumWidth(Window owner, string translated, bool people = false, float brandSize = 0, bool compactBrand = false)
    {
        var style = ImGui.GetStyle();
        var nativeSize = ImGui.GetFontSize();
        var count = owner.TitleBarButtons.Count(button => !owner.IsClickthrough || button.AvailableClickthrough);
        if (owner.AllowPinning || owner.AllowClickthrough || owner.AllowBackgroundBlur) count++;
        var left = brandSize > 0 ? (compactBrand ? 40 : 42) * MaterialTheme.Metrics.Scale
            : style.FramePadding.X + ((owner.Flags & ImGuiWindowFlags.NoCollapse) == 0 && style.WindowMenuButtonPosition == ImGuiDir.Left
                ? nativeSize + style.ItemInnerSpacing.X : 0);
        var right = nativeSize + style.FramePadding.X * 2 + count * (nativeSize + style.ItemInnerSpacing.X);
        if ((owner.Flags & ImGuiWindowFlags.NoCollapse) == 0 && style.WindowMenuButtonPosition == ImGuiDir.Right)
            right += nativeSize + style.ItemInnerSpacing.X;
        using var font = UiText.Font(people ? compactBrand ? UiFontRole.BodyStrong : UiFontRole.PluginName : UiFontRole.Body);
        var renderSize = people ? ImGui.GetFontSize() : nativeSize;
        var iconWidth = people ? (brandSize > 0 ? brandSize * MaterialTheme.Metrics.Scale + 10 * MaterialTheme.Metrics.Scale
            : nativeSize + style.ItemInnerSpacing.X) : 0;
        return left + ScaledTextSize(translated, renderSize / ImGui.GetFontSize()).X + iconWidth + right + style.ItemInnerSpacing.X;
    }

    internal static unsafe void ImageTitle(Window owner, string visibleTitle,
        Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap icon)
    {
        var window = ImGuiP.FindWindowByName(owner.WindowName);
        if (window.Handle == null) return;
        var count = owner.TitleBarButtons.Count(button => !owner.IsClickthrough || button.AvailableClickthrough);
        if (owner.AllowPinning || owner.AllowClickthrough || owner.AllowBackgroundBlur) count++;
        var extraRight = count * (ImGuiP.CalcFontSize(window) + ImGui.GetStyle().ItemInnerSpacing.X);
        using var font = UiText.Font(UiFontRole.Body);
        MaterialWindowHeader.PaintTitle(window, visibleTitle, icon.Handle, icon.Size, extraRight, owner.ShowCloseButton);
    }

    internal static unsafe void TitleWithButtons(string original,string translated,Window? owner,bool people=false,float brandSize=0,bool compactBrand=false)
    {
        var window = owner is null ? ImGuiP.GetCurrentWindow() : ImGuiP.FindWindowByName(owner.WindowName);
        if (window.Handle == null || (window.Flags & ImGuiWindowFlags.NoTitleBar) != 0) return;
        var nativeFont = ImGui.GetFont();
        var s=ImGui.GetStyle(); var size=nativeFont.FontSize*nativeFont.Scale*ImGui.GetIO().FontGlobalScale*window.FontWindowScale;
        var height=ImGuiP.TitleBarHeight(window);
        var flags=window.Flags;
        var collapseOnLeft=(flags & (ImGuiWindowFlags.NoCollapse|ImGuiWindowFlags.Modal))==0 && s.WindowMenuButtonPosition==ImGuiDir.Left;
        var position=window.Pos+new Vector2(s.FramePadding.X+(collapseOnLeft?size+s.ItemInnerSpacing.X:0),s.FramePadding.Y);
        var erasePosition=position;
        var originalWidth=MaterialText.Measure(original).X;
        using var font=UiText.Font(people?compactBrand?UiFontRole.BodyStrong:UiFontRole.PluginName:UiFontRole.Body);
        var renderSize=people?ImGui.GetFontSize():size;
        var iconSize=brandSize>0?brandSize*MaterialTheme.Metrics.Scale:size;
        var iconWidth=people?iconSize+(brandSize>0?10*MaterialTheme.Metrics.Scale:s.ItemInnerSpacing.X):0;
        if(brandSize>0)position=window.Pos+new Vector2((compactBrand?40:42)*MaterialTheme.Metrics.Scale,(height-Math.Max(renderSize,iconSize))*.5f);
        var translatedSize=ScaledTextSize(translated,renderSize/ImGui.GetFontSize());
        var translatedWidth=translatedSize.X+iconWidth;
        var dl=window.DrawList;
        var rightButtons = size + s.FramePadding.X * 2;
        if (owner is not null)
        {
            var count = owner.TitleBarButtons.Count(button => !owner.IsClickthrough || button.AvailableClickthrough);
            if (owner.AllowPinning || owner.AllowClickthrough || owner.AllowBackgroundBlur) count++;
            rightButtons += count * (size + s.ItemInnerSpacing.X);
        }
        if ((flags & ImGuiWindowFlags.NoCollapse) == 0 && s.WindowMenuButtonPosition == ImGuiDir.Right)
            rightButtons += size + s.ItemInnerSpacing.X;
        dl.PushClipRect(new Vector2(Math.Min(position.X,erasePosition.X),window.Pos.Y),window.Pos+new Vector2(Math.Max(0, window.Size.X-rightButtons),height),false);
        try
        {
        var nav = ImGui.GetCurrentContext().NavWindow;
        var focused = nav.Handle != null && nav.RootWindow.ID == window.RootWindow.ID;
        var bg=s.Colors[(int)(focused?ImGuiCol.TitleBgActive:ImGuiCol.TitleBg)];
        dl.AddRectFilled(new Vector2(Math.Min(position.X,erasePosition.X),window.Pos.Y),
            new Vector2(Math.Max(erasePosition.X+originalWidth,position.X+translatedWidth),window.Pos.Y+height),ImGui.ColorConvertFloat4ToU32(bg));
        if(people) FrenRiderPresentation.People(position,iconSize,MaterialTheme.Current.Colors.Primary);
        var textPosition=position+new Vector2(iconWidth,0);
        if(brandSize>0)textPosition.Y=window.Pos.Y+(height-translatedSize.Y)*.5f;
        MaterialText.AddText(dl,ImGui.GetFont(),renderSize,textPosition,ImGui.ColorConvertFloat4ToU32(s.Colors[(int)ImGuiCol.Text]),translated);
        }
        finally { dl.PopClipRect(); }
    }
    internal static void TableHeadersRow(float height=0)
    {
        for(var column=0;column<ImGui.TableGetColumnCount();column++)
        {
            var translated=UiText.T(ImGui.TableGetColumnName(column));
            if(MaterialText.RequiresShaping(translated)) height=Math.Max(height,MaterialText.Measure(translated).Y+2*ImGui.GetStyle().CellPadding.Y);
        }
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers,height);
        for(var index=0;index<ImGui.TableGetColumnCount();index++)
        {
            if(!ImGui.TableSetColumnIndex(index)) continue;
            var original=ImGui.TableGetColumnName(index);
            var position=ImGui.GetCursorScreenPos();
            var available=ImGui.GetContentRegionAvail().X;
            var foreground=ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
            ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);
            ImGui.TableHeader(original);
            ImGui.PopStyleColor();
            var translated=UiText.T(original);
            foreground.W*=ImGui.GetStyle().Alpha;
            var dl=ImGui.GetWindowDrawList();
            dl.PushClipRect(position,position+new Vector2(Math.Max(1,available),Math.Max(ImGui.GetTextLineHeight(),MaterialText.Measure(translated).Y)),true);
            try
            {
            MaterialText.AddText(dl,position,ImGui.ColorConvertFloat4ToU32(foreground),translated);
            }
            finally { dl.PopClipRect(); }
            if(translated!=original && MaterialText.Measure(translated).X>available-16*AethertekUI.MaterialTheme.Metrics.Scale && ImGui.IsItemHovered())
                MaterialText.SetTooltip(translated);
        }
    }
}
