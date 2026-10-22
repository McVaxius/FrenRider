using System;
using System.Linq;
using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using FrenRider.Models;

namespace FrenRider.Windows;

public partial class ConfigWindow
{
    private LimitBreakRole selectedLimitBreakRole = LimitBreakRole.Melee;
    private void DrawLimitBreakTab(CharacterConfig config)
        => DrawLimitBreakSettings(config.LimitBreak, selectedLimitBreakRole,
            role => selectedLimitBreakRole = role, SaveEditingAccount,
            label => DrawDefaultSettingSyncButton(label, inline: false));

    private static readonly string[] GaugeLabels = { "Full available gauge", "LB1 only", "LB2 only", "LB3 only" };
    private static readonly string[] TargetLabels = { "Boss only", "Current target", "Any enemy" };
    internal static string LimitBreakRoleLabel(LimitBreakRole role) => role switch
    {
        LimitBreakRole.PhysicalRanged => "Physical ranged", LimitBreakRole.MagicalRanged => "Magical ranged",
        _ => role.ToString(),
    };

    internal static void DrawLimitBreakSettings(LimitBreakSettings settings, LimitBreakRole selected,
        Action<LimitBreakRole> select, Action save, Action<string> sync)
    {
        using (UiText.Font(UiFontRole.PluginName)) UiGui.Text("Limit Break");
        if (ImGui.IsItemHovered()) UiGui.SetTooltip("Choose when each role may spend the party gauge.");
        UiGui.TextDisabled("FrenRider must be active and in combat.");
        var wide = ImGui.GetContentRegionAvail().X >= 840 * MaterialTheme.Metrics.Scale;
        uint damageRoot = 0;
        if (wide)
        {
            ImGui.PushID("Damage roles");
            damageRoot = ImGui.GetID("##Card");
            ImGui.PopID();
        }
        RoleButtons(selected, select, true, damageRoot);
        var title = LimitBreakRoleLabel(selected);
        Card(title, () =>
        {
            if (selected == LimitBreakRole.Tank) TankControls(settings.Tank, save, sync);
            else if (selected == LimitBreakRole.Healer) HealerControls(settings.Healer, save, sync);
            else DamageControls(settings.Damage(selected)!, selected, save, sync);
        }, wide && selected is not LimitBreakRole.Tank and not LimitBreakRole.Healer ? "Damage roles" : title);
        Card("Role overview", () =>
        {
            using var rows = MaterialTable.PushTightRows();
            if (ImGui.BeginTable("##LimitBreakRoleOverview", 2,
                    ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.NoSavedSettings))
            {
                try
                {
                    var roleWidth = new[] { LimitBreakRole.Tank, LimitBreakRole.Healer, LimitBreakRole.Melee,
                        LimitBreakRole.PhysicalRanged, LimitBreakRole.MagicalRanged }
                        .Max(role => MaterialText.Measure(UiText.T(LimitBreakRoleLabel(role))).X);
                    ImGui.TableSetupColumn("##Role", ImGuiTableColumnFlags.WidthFixed,
                        Math.Min(roleWidth, ImGui.GetContentRegionAvail().X * .35f));
                    ImGui.TableSetupColumn("##Policy", ImGuiTableColumnFlags.WidthStretch);
                    foreach (var role in new[] { LimitBreakRole.Tank, LimitBreakRole.Healer, LimitBreakRole.Melee,
                                 LimitBreakRole.PhysicalRanged, LimitBreakRole.MagicalRanged })
                    {
                        ImGui.TableNextColumn();
                        using (UiText.Font(UiFontRole.BodyStrong)) UiGui.TextWrapped(LimitBreakRoleLabel(role));
                        ImGui.TableNextColumn(); UiGui.TextWrapped(LimitBreakSummary(settings, role));
                    }
                }
                finally { ImGui.EndTable(); }
            }
            UiGui.TextWrapped("Saved policies only. This overview is not live telemetry.");
        });
    }

    private static void RoleButtons(LimitBreakRole selected, Action<LimitBreakRole> select, bool all, uint damageRoot = 0)
    {
        var first = true;
        foreach (var role in new[] { LimitBreakRole.Tank, LimitBreakRole.Healer, LimitBreakRole.Melee,
                     LimitBreakRole.PhysicalRanged, LimitBreakRole.MagicalRanged })
        {
            if (!all && role is LimitBreakRole.Tank or LimitBreakRole.Healer) continue;
            var label = LimitBreakRoleLabel(role);
            var width = MaterialText.Measure(UiText.T(label)).X + ImGui.GetFrameHeight()
                + ImGui.GetStyle().ItemInnerSpacing.X;
            if (!first && ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + width
                <= ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X) ImGui.SameLine();
            var restoreDamageRoot = damageRoot != 0 && role is not LimitBreakRole.Tank and not LimitBreakRole.Healer;
            if (restoreDamageRoot) ImGuiP.PushOverrideID(damageRoot);
            try { if (UiGui.RadioButton(label, selected == role)) select(role); }
            finally { if (restoreDamageRoot) ImGui.PopID(); }
            first = false;
        }
        ImGui.Spacing();
    }

    private static void DamageControls(DamageLimitBreakPolicy p, LimitBreakRole role, Action save, Action<string> sync)
    {
        ImGui.PushID(role.ToString());
        try
        {
            var enabled = p.Enabled;
            if (UiGui.Checkbox("Automatic LB", ref enabled)) { p.Enabled = enabled; save(); }
            var gauge = (int)p.Gauge;
            Field("Gauge requirement");
            if (UiGui.Combo("##Gauge", ref gauge, GaugeLabels, GaugeLabels.Length)) { p.Gauge = (LimitBreakGauge)gauge; save(); }
            if (ImGui.IsItemHovered()) UiGui.SetTooltip("Full available waits until every available bar is full.");
            var target = (int)p.Target;
            Field("Target");
            if (UiGui.Combo("##Target", ref target, TargetLabels, TargetLabels.Length)) { p.Target = (LimitBreakTarget)target; save(); }
            var hp = p.TargetHpPercent;
            Field("Target HP at or below (%)");
            if (UiGui.InputFloat("##TargetHp", ref hp)) { p.TargetHpPercent = Percent(hp); save(); }
            var count = p.MinimumEnemies;
            Field("Minimum enemies");
            if (UiGui.InputInt("##Enemies", ref count)) { p.MinimumEnemies = Math.Clamp(count, 1, 100); save(); }
            if (ImGui.IsItemHovered()) UiGui.SetTooltip("100% permits use as soon as the gauge is ready. Count includes enemies inside the action area.");
            UiGui.TextWrapped(UiText.F("Example: {0}", DamageSummary(p)));
            sync(LimitBreakRoleLabel(role) + " Limit Break");
        }
        finally { ImGui.PopID(); }
    }

    private static void TankControls(TankLimitBreakPolicy p, Action save, Action<string> sync)
    {
        ImGui.PushID("TankLB");
        try
        {
            var enabled = p.Enabled;
            if (UiGui.Checkbox("Automatic LB", ref enabled)) { p.Enabled = enabled; save(); }
            var gauge = (int)p.Gauge;
            Field("Gauge requirement");
            if (UiGui.Combo("##Gauge", ref gauge, GaugeLabels, GaugeLabels.Length)) { p.Gauge = (LimitBreakGauge)gauge; save(); }
            var hp = p.PartyHpPercent;
            Field("Party average HP at or below (%)");
            if (UiGui.InputFloat("##PartyHp", ref hp)) { p.PartyHpPercent = Percent(hp); save(); }
            UiGui.TextWrapped("Damage reduction. Independent of damage-role policies.");
            sync("Tank Limit Break");
        }
        finally { ImGui.PopID(); }
    }

    private static void HealerControls(HealerLimitBreakPolicy p, Action save, Action<string> sync)
    {
        ImGui.PushID("HealerLB");
        try
        {
            var enabled = p.Enabled;
            if (UiGui.Checkbox("Automatic LB", ref enabled)) { p.Enabled = enabled; save(); }
            UiGui.SettingsSection("Revive — LB3");
            var revive = p.Revive;
            if (UiGui.Checkbox("Use when party members are KO", ref revive)) { p.Revive = revive; save(); }
            var count = p.MinimumKoMembers;
            Field("Minimum KO members");
            if (UiGui.InputInt("##KO", ref count)) { p.MinimumKoMembers = Math.Clamp(count, 1, 7); save(); }
            UiGui.SettingsSection("Heal — LB1 / LB2");
            var heal = p.Heal;
            if (UiGui.Checkbox("Allow healing Limit Break", ref heal)) { p.Heal = heal; save(); }
            var hp = p.PartyHpPercent;
            Field("Party average HP at or below (%)");
            if (UiGui.InputFloat("##PartyHp", ref hp)) { p.PartyHpPercent = Percent(hp); save(); }
            UiGui.TextWrapped("Revival requires LB3. Healing never spends LB3.");
            sync("Healer Limit Break");
        }
        finally { ImGui.PopID(); }
    }

    private static float Percent(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 100) : 100;
    private static void Field(string label)
    {
        var scale = MaterialTheme.Metrics.Scale;
        var choices = label == "Gauge requirement" ? GaugeLabels : label == "Target" ? TargetLabels : null;
        var minimum = choices is null ? 120 * scale : choices.Max(value => MaterialText.Measure(UiText.T(value)).X)
            + ImGui.GetFrameHeight() + ImGui.GetStyle().FramePadding.X * 2;
        var width = Math.Max(minimum, Math.Min(300 * scale, ImGui.GetContentRegionAvail().X * .5f));
        var labelWidth = MaterialText.Measure(UiText.T(label)).X;
        var available = ImGui.GetContentRegionAvail().X;
        var start = ImGui.GetCursorPosX();
        if (labelWidth + ImGui.GetStyle().ItemSpacing.X + width <= available)
        {
            ImGui.AlignTextToFramePadding();
            UiGui.TextUnformatted(label);
            ImGui.SameLine(start + available - width);
        }
        else UiGui.TextWrapped(label);
        ImGui.SetNextItemWidth(Math.Min(width, Math.Max(1, ImGui.GetContentRegionAvail().X)));
    }

    private static void Card(string title, Action draw, string? idTitle = null)
    {
        ImGui.PushID(idTitle ?? title);
        try
        {
            using var style = new MaterialStyleScope();
            style.Style(ImGuiStyleVar.CellPadding, Vector2.Zero);
            // Keep the old native table seed without its padded background frame.
            if (!ImGui.BeginTable("##Card", 1, ImGuiTableFlags.NoSavedSettings)) return;
            try
            {
                ImGui.TableNextColumn();
                UiGui.SettingsSection(title);
                draw();
            }
            finally { ImGui.EndTable(); }
        }
        finally { ImGui.PopID(); }
    }

    internal static string LimitBreakSummary(LimitBreakSettings settings, LimitBreakRole role)
    {
        if (role == LimitBreakRole.Tank)
            return UiText.F("{0}; {1}; party HP ≤ {2}%", UiText.T(settings.Tank.Enabled ? "On" : "Off"),
                UiText.T(GaugeLabels[(int)settings.Tank.Gauge]), settings.Tank.PartyHpPercent);
        if (role == LimitBreakRole.Healer)
            return UiText.F("{0}; revive LB3: {1} ({2}+ KO); heal LB1/LB2: {3} (HP ≤ {4}%)",
                UiText.T(settings.Healer.Enabled ? "On" : "Off"), UiText.T(settings.Healer.Revive ? "On" : "Off"),
                settings.Healer.MinimumKoMembers, UiText.T(settings.Healer.Heal ? "On" : "Off"), settings.Healer.PartyHpPercent);
        return DamageSummary(settings.Damage(role)!);
    }

    private static string DamageSummary(DamageLimitBreakPolicy p)
        => UiText.F("{0}; {1}; {2}; target HP ≤ {3}%; {4}+ enemies", UiText.T(p.Enabled ? "On" : "Off"),
            UiText.T(GaugeLabels[(int)p.Gauge]), UiText.T(TargetLabels[(int)p.Target]), p.TargetHpPercent, p.MinimumEnemies);
}
