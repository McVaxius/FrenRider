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
        var display = UiText.T(label);
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
        var translated=UiText.T(visible);
        using var height = MaterialText.PushLineHeight(translated);
        var foreground=ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        var gap=ImGui.GetStyle().ItemInnerSpacing;
        // Native Checkbox sizes its hit area from the original label. Adjust that size for the
        // translated ink while keeping the native widget and its original ID.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing,new Vector2(Math.Max(0,gap.X+MaterialText.Measure(translated).X-MaterialText.Measure(visible).X),gap.Y));
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
