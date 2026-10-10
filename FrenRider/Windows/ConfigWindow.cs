using AethertekUI.Dalamud;
using AethertekUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using FrenRider.Models;
using FrenRider.Services;
using Lumina.Excel.Sheets;

namespace FrenRider.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly AethertekUI.Dalamud.MaterialSupportLog supportLog = new();
    private readonly MaterialWindowMotion motion = new();
    private readonly AethertekUI.MaterialWindowOpacity windowOpacity = new();
    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private readonly ConfigManager configManager;

    private string currentTab = "Profile";
    private string editingCharacterKey = "";
    private string editingRemoteRowId = "";
    private string observedActiveAccountId = "";
    private string observedActiveCharacterKey = "";
    private string accountAliasEdit = "";
    private string frenNameInput = "";
    private bool frenNameFocused = false;
    private string mountSearch = "";
    private string foodSearch = "";
    private bool isDraggingSplitter = false;
    private string whitelistInput = "";
    private string clingExclusionSearch = "";
    private uint clingExclusionAreaToAdd;
    private readonly List<(uint Id, string Name)> foodItems = new();
    private bool foodItemsLoaded = false;
    private string companionWindowStatus = "";
    private bool selectChocoboTab;

    private static readonly string[] CompanionStances = { "Free Stance", "Defender Stance", "Attacker Stance", "Healer Stance", "Follow" };
    private static readonly (int ItemId, string Name)[] ChocoboFoods =
    {
        (0, "None"),
        (7894, "Curiel Root"),
        (7895, "Sylkis Bud"),
        (7897, "Mimett Gourd"),
        (7898, "Tantalplant"),
        (7900, "Pahsana Fruit"),
    };
    internal static string ChocoboFoodName(int itemId)
        => ChocoboFoods.FirstOrDefault(food => food.ItemId == itemId).Name ?? "Unsupported companion food";
    internal static string ChocoboFoodTooltip(int itemId)
    {
        var effect = itemId switch
        {
            7894 => "Increases EXP earned by your chocobo companion.",
            7895 => "Increases your chocobo companion's attack potency.",
            7897 => "Increases your chocobo companion's healing magic potency.",
            7898 => "Increases your chocobo companion's maximum HP.",
            7900 => "Increases your chocobo companion's enmity.",
            _ => null,
        };
        return effect == null ? UiText.T(itemId == 0 ? "No companion food selected." : "Unsupported companion food")
            : UiText.T(ChocoboFoodName(itemId)) + "\n" + UiText.T(effect) + "\n\n"
                + UiText.T("Field feeding grants this buff. If this is your chocobo's favorite food, it grants the stronger version. Favorite food is established through stable training.");
    }
    private static readonly string[] ChocoboSkillTrees = { "Defender", "Attacker", "Healer" };
    private static readonly string[] ChocoboSkillPriorityLabels = { "First skill tree", "Second skill tree (optional)", "Third skill tree (optional)" };
    private static readonly string[] ClingTypes = { "NavMesh", "Visland", "BossMod Follow", "Vanilla Follow" };
    private static readonly string[] RotationPlugins = { "BMR", "DAEDALUS", "RSR", "VBM", "WRATH" };
    // Keep saved plugin IDs stable while displaying names alphabetically.
    private static readonly int[] RotationPluginIds = { 0, 4, 2, 1, 3 };
    private static readonly string[] DaedalusTargetModes = { "None", "Focus", "Split", "Kill Adds" };
    private static readonly string[] RsrOperatingModes = { "Auto", "Manual", "None", "Support" };
    private static readonly string[] RsrAggroTypes =
    {
        "All Attackable Targets",
        "Previously Engaged Targets",
        "All Targets When Solo in Duty",
        "All Targets When Solo",
        "Solo Deep Dungeon Smart",
    };
    private static readonly string[] BossModAIOptions = { "on", "off" };
    private static readonly string[] CleanupModes = { "Restore snapshot", "Turn everything off" };
    private static readonly string[] Positionals = { "Front", "Rear", "Any", "Auto" };
    private static readonly string[] FollowInCombatOptions = { "No", "Yes", "Auto" };
    private static readonly string[] AdsMaturityOptions = { "0 - Not Cleared", "1 - 1P Unsync Cleared", "2 - 1P Duty Support", "3 - 4P Sync Cleared" };
    private static readonly string[] LootTypes = { "unchanged", "need", "greed", "pass" };
    private static readonly string[] OnOff = { "Off", "On" };
    private static readonly string[] IdleActionModes = { "Specific Action", "Action From List" };
    private static readonly string[] IdleListModes = { "Default List", "Custom List" };
    private static readonly string[] RepairModes = { "Disabled", "Self", "NPC no-inn", "NPC No Inn + No TP", "NPC repair + inn room" };

    public ConfigWindow(Plugin plugin) : base("Fren Rider Settings###FrenRiderConfig")
    {
        Flags = ImGuiWindowFlags.None;
        Size = new Vector2(900, 550);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(650, 430),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        this.plugin = plugin;
        this.configuration = plugin.Configuration;
        this.configManager = plugin.ConfigManager;
        editingCharacterKey = configManager.ActiveCharacterKey;
        observedActiveAccountId = configManager.CurrentAccountId;
        observedActiveCharacterKey = configManager.ActiveCharacterKey;
    }

    public void Dispose() { }

    internal void OpenChocoboTesting()
    {
        selectChocoboTab = true;
        IsOpen = true;
    }

    private void EnsureFoodItemsLoaded()
    {
        if (foodItemsLoaded) return;
        foodItemsLoaded = true;

        try
        {
            var itemSheet = Plugin.DataManager.GetExcelSheet<Item>();
            if (itemSheet == null) return;

            foreach (var item in itemSheet)
            {
                if (item.RowId == 0) continue;
                if (item.ItemUICategory.RowId != 46) continue;

                var name = item.Name.ToString();
                if (string.IsNullOrWhiteSpace(name)) continue;

                foodItems.Add((item.RowId, name));
            }

            Plugin.Log.Information($"[ConfigWindow] Loaded {foodItems.Count} food items from Lumina");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[ConfigWindow] Failed to load food items: {ex.Message}");
        }
    }

    public override void PreDraw()
    {
        SyncEditingSelectionWithActiveCharacter();

        if (configuration.IsConfigWindowMovable)
            Flags &= ~ImGuiWindowFlags.NoMove;
        else
            Flags |= ImGuiWindowFlags.NoMove;

        // Update window title based on selected character (krangled if enabled)
        var remote = GetEditingRemoteProfile();
        var sel = editingCharacterKey;
        var displaySel = remote != null
            ? $"REMOTE: {Disp(GetRemoteDisplayLabel(remote))}"
            : string.IsNullOrEmpty(sel) ? "DEFAULT CONFIG" : Disp(sel);
        WindowName = $"Fren Rider Settings - {displaySel}###FrenRiderConfig";
        motion.Prepare(this, reducedMotion: false, roundedCorners: true);
    }

    public override void PostDraw()
    {
        motion.Restore(this);
        plugin.ApplyWindowOpacity(windowOpacity, WindowName);
    }

    public override void Draw()
    {
        motion.DrawChrome();
        var remote = GetEditingRemoteProfile();
        var config = remote?.Config ?? configManager.GetCurrentCharacterConfig(editingCharacterKey);

        var selectedLabel = remote is not null ? UiText.F("REMOTE: {0}", Disp(GetRemoteDisplayLabel(remote)))
            : string.IsNullOrEmpty(editingCharacterKey) ? UiText.T("DEFAULT CONFIG") : Disp(editingCharacterKey);
        UiGui.Title(WindowName.Split("###", 2)[0], UiText.F("Fren Rider Settings - {0}", selectedLabel));
        var panelWidth = Math.Clamp(configuration.LeftPanelWidth, UiHelpers.Scale(120), Math.Max(UiHelpers.Scale(120), ImGui.GetContentRegionAvail().X * .45f));

        // Left panel (user-resizable)
        ImGui.BeginChild("LeftPanel", new Vector2(panelWidth, 0), true);
        DrawLeftPanel();
        ImGui.EndChild();

        ImGui.SameLine();

        // Splitter handle (vertical drag bar)
        var cursorPos = ImGui.GetCursorScreenPos();
        var splitterHeight = ImGui.GetContentRegionAvail().Y;
        ImGui.InvisibleButton("##Splitter", new Vector2(UiHelpers.Scale(6), splitterHeight));
        if (ImGui.IsItemActive())
        {
            var delta = ImGui.GetIO().MouseDelta.X;
            if (delta != 0)
            {
                configuration.LeftPanelWidth = Math.Clamp(panelWidth + delta, 120f, 500f);
                if (!isDraggingSplitter)
                    isDraggingSplitter = true;
            }
        }
        else if (isDraggingSplitter)
        {
            isDraggingSplitter = false;
            configuration.Save();
        }
        if (ImGui.IsItemHovered() || ImGui.IsItemActive())
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);

        // Draw visible splitter line
        var drawList = ImGui.GetWindowDrawList();
        var lineColor = ImGui.IsItemHovered() || ImGui.IsItemActive()
            ? ImGui.GetColorU32(AethertekUI.MaterialTheme.Current.Colors.Primary)
            : ImGui.GetColorU32(AethertekUI.MaterialTheme.Current.Colors.OutlineVariant);
        drawList.AddLine(new Vector2(cursorPos.X + UiHelpers.Scale(2), cursorPos.Y), new Vector2(cursorPos.X + UiHelpers.Scale(2), cursorPos.Y + splitterHeight), lineColor, UiHelpers.Scale(2));

        ImGui.SameLine();

        // Right panel
        ImGui.SetNextWindowContentSize(new Vector2(Math.Max(ImGui.GetContentRegionAvail().X, UiHelpers.Scale(620)), 0));
        ImGui.BeginChild("RightPanel", Vector2.Zero, false, ImGuiWindowFlags.HorizontalScrollbar);
        DrawRightPanel(config, remote);
        ImGui.EndChild();
    }

    private void DrawLeftPanel()
    {
        var account = configManager.GetCurrentAccount();
        if (account == null)
        {
            UiGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1), "No account loaded.");
            UiGui.TextWrapped("Log in to a character to create one.");
            return;
        }

        // Account alias (editable)
        UiGui.TextColored(AethertekUI.MaterialTheme.Current.Colors.Secondary, "ACCOUNT");
        if (accountAliasEdit != account.AccountAlias)
            accountAliasEdit = account.AccountAlias;

        if (configuration.KrangleEnabled)
        {
            var krangledAlias = Disp(accountAliasEdit);
            ImGui.SetNextItemWidth(-1);
            UiGui.InputText("##AccountAliasKrangled", ref krangledAlias, 64, ImGuiInputTextFlags.ReadOnly);
            if (ImGui.IsItemHovered())
                UiGui.SetTooltip("Disable Krangle to edit the account alias.");
        }
        else
        {
            ImGui.SetNextItemWidth(-1);
            if (UiGui.InputText("##AccountAlias", ref accountAliasEdit, 64))
            {
                configManager.UpdateAccountAlias(accountAliasEdit);
            }
        }
        HelpMarker("Human-readable alias for this account group. Linked to account ID internally.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // DEFAULT CONFIG
        var isDefault = string.IsNullOrEmpty(editingRemoteRowId) && string.IsNullOrEmpty(editingCharacterKey);
        if (UiGui.Selectable("DEFAULT CONFIG", isDefault))
        {
            editingCharacterKey = "";
            editingRemoteRowId = "";
            SyncFrenNameInput();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Current character (green highlight, with spacing)
        var currentCharKey = GetCurrentCharacterKey();
        if (!string.IsNullOrEmpty(currentCharKey))
        {
            var isCurrent = string.IsNullOrEmpty(editingRemoteRowId) && editingCharacterKey == currentCharKey;
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 1f, 0.4f, 1));
            if (UiGui.Selectable(currentCharKey, isCurrent, Disp(currentCharKey)))
            {
                editingCharacterKey = currentCharKey;
                editingRemoteRowId = "";
                SyncFrenNameInput();
            }
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

        // Other characters sorted alphabetically (with spacing between)
        foreach (var charKey in configManager.GetSortedCharacterKeys())
        {
            if (charKey == currentCharKey) continue;
            var isSelected = string.IsNullOrEmpty(editingRemoteRowId) && editingCharacterKey == charKey;
            if (UiGui.Selectable(charKey, isSelected, Disp(charKey)))
            {
                editingCharacterKey = charKey;
                editingRemoteRowId = "";
                SyncFrenNameInput();
            }
            ImGui.Spacing();
        }

        var remoteProfiles = configManager.GetSortedRemoteProfiles().ToList();
        if (remoteProfiles.Count == 0)
            return;

        ImGui.Separator();
        ImGui.Spacing();
        UiGui.TextColored(new Vector4(0.85f, 0.5f, 1f, 1f), "REMOTE PROFILES (DAD)");
        UiGui.TextWrapped("Separate profiles for exact remote identities. They never become local character rows.");
        ImGui.Spacing();
        foreach (var remote in remoteProfiles)
        {
            var selected = string.Equals(editingRemoteRowId, remote.RowId, StringComparison.Ordinal);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.65f, 1f, 1f));
            if (UiGui.Selectable($"REMOTE: {GetRemoteDisplayLabel(remote)}##Remote{remote.RowId}", selected, UiText.F("REMOTE: {0}", Disp(GetRemoteDisplayLabel(remote)))))
            {
                editingRemoteRowId = remote.RowId;
                SyncFrenNameInput();
            }
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }
    }

    private void DrawRightPanel(CharacterConfig config, RemoteProfileRow? remote)
    {
        // --- Top bar: Krangle | Reset All (?) | Reset This (?) ---
        var isDefaultConfig = IsDefaultConfigSelected();
        var krangleEnabled = configuration.KrangleEnabled;
        if (UiGui.Checkbox("Krangle", ref krangleEnabled))
        {
            configuration.KrangleEnabled = krangleEnabled;
            configuration.Save();
            KrangleService.ClearCache();
        }
        HelpMarker("Garble all identifying text (character names, fren names, servers)\nwith military/exercise words. Useful for taking screenshots\nto report issues without revealing personal info.");

        // Right-align the buttons
        var avail = ImGui.GetContentRegionAvail().X;
        var buttonGroupWidth = remote != null ? 125f : isDefaultConfig ? 590f : 340f;
        if (avail >= buttonGroupWidth + UiHelpers.Scale(20))
            ImGui.SameLine(ImGui.GetCursorPosX() + avail - buttonGroupWidth);

        if (remote != null)
        {
            var ctrlHeld = ImGui.GetIO().KeyCtrl;
            if (!ctrlHeld) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.7f, 0.1f, 0.1f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.2f, 0.2f, 1));
            if (UiGui.Button("DELETE REMOTE") && ctrlHeld && configManager.DeleteRemoteProfile(remote.RowId))
            {
                editingRemoteRowId = "";
                SyncFrenNameInput();
            }
            ImGui.PopStyleColor(2);
            if (!ctrlHeld) ImGui.PopStyleVar();
            if (ImGui.IsItemHovered())
                UiGui.SetTooltip("Hold CTRL and click to delete this remote profile.");
        }
        else if (isDefaultConfig)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.45f, 0.7f, 1));
            if (UiGui.Button("Everything Sync"))
                ReportDefaultSync("all settings", configManager.ApplyDefaultToAllCharacters());
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
                UiGui.SetTooltip("Copy DEFAULT CONFIG character-profile settings to every character profile in this account.");

            ImGui.SameLine();
            var canSyncCurrentTab = ConfigManager.CanSyncDefaultTab(currentTab);
            if (!canSyncCurrentTab)
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.5f, 0.35f, 1));
            if (UiGui.Button("Full Tab Sync") && canSyncCurrentTab)
                ReportDefaultSync($"{GetCurrentTabDisplayName()} tab", configManager.ApplyDefaultTabToAllCharacters(currentTab));
            ImGui.PopStyleColor();

            if (!canSyncCurrentTab)
                ImGui.PopStyleVar();

            if (ImGui.IsItemHovered())
                UiGui.SetTooltip(canSyncCurrentTab
                    ? "Copy this DEFAULT CONFIG tab to every character profile in this account."
                    : "UI / About uses global settings and is not copied to character profiles.");

            ImGui.SameLine();
        }

        if (remote == null)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 1));
            if (UiGui.Button("Reset All"))
            {
                configManager.ResetCharacterToDefault(editingCharacterKey);
                SyncFrenNameInput();
            }
            ImGui.PopStyleColor();
            HelpMarker("Reset ALL tabs for this character to default values.\nIf editing DEFAULT CONFIG, resets to plugin defaults.");

            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.4f, 0.2f, 1));
            if (UiGui.Button("Reset This"))
            {
                configManager.ResetCharacterTabToDefault(editingCharacterKey, currentTab);
                SyncFrenNameInput();
            }
            ImGui.PopStyleColor();
            HelpMarker("Reset only the current tab for this character to default values.");
        }

        // DELETE button (only for non-default characters, requires CTRL)
        if (remote == null && !string.IsNullOrEmpty(editingCharacterKey))
        {
            ImGui.SameLine();
            var io = ImGui.GetIO();
            var ctrlHeld = io.KeyCtrl;
            var isActiveProfile = string.Equals(editingCharacterKey, configManager.ActiveCharacterKey, StringComparison.Ordinal);
            var canDelete = ctrlHeld && !isActiveProfile;
            if (!canDelete) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.7f, 0.1f, 0.1f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.2f, 0.2f, 1));
            if (UiGui.Button("DELETE") && canDelete && configManager.DeleteCharacter(editingCharacterKey))
            {
                editingCharacterKey = configManager.ActiveCharacterKey;
                SyncFrenNameInput();
            }
            ImGui.PopStyleColor(2);
            if (!canDelete) ImGui.PopStyleVar();
            if (ImGui.IsItemHovered())
                UiGui.SetTooltip(isActiveProfile
                    ? "The currently active character profile cannot be deleted while logged in."
                    : "Hold CTRL and click to delete this character's config.\nThis cannot be undone.");
        }

        ImGui.Spacing();

        if (remote != null)
            DrawRemoteProfileBanner(remote);

        bool tabsOpen;
        using (MaterialText.PushLineHeight(UiText.T("Profile"), UiText.T("Follow"), UiText.T("Combat"),
            UiText.T("Duty / ADS / Exit"), UiText.T("Automation"), UiText.T("Chocobo"), UiText.T("Window appearance"), UiText.T("UI / About")))
            tabsOpen = ImGui.BeginTabBar("FrenRiderTabs", ImGuiTabBarFlags.FittingPolicyScroll);
        if (tabsOpen)
        {
            if (UiGui.BeginTabItem("Profile"))
            {
                currentTab = "Profile";
                DrawPartyTab(config);
                ImGui.EndTabItem();
            }
            if (UiGui.BeginTabItem("Follow"))
            {
                currentTab = "Follow";
                DrawDistanceTab(config);
                ImGui.EndTabItem();
            }
            if (UiGui.BeginTabItem("Combat"))
            {
                currentTab = "Combat";
                DrawCombatTab(config);
                ImGui.EndTabItem();
            }
            if (UiGui.BeginTabItem("Duty / ADS / Exit"))
            {
                currentTab = "Duty";
                DrawDutyAdsExitTab(config);
                ImGui.EndTabItem();
            }
            if (UiGui.BeginTabItem("Automation"))
            {
                currentTab = "Automation";
                DrawAutomationTab(config);
                ImGui.EndTabItem();
            }
            if (UiGui.BeginTabItem("Chocobo", selectChocoboTab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
            {
                selectChocoboTab = false;
                currentTab = "Chocobo";
                DrawChocoboTab(config);
                ImGui.EndTabItem();
            }
            if (UiGui.BeginTabItem("Window appearance", ImGuiTabItemFlags.NoPushId))
            {
                currentTab = "UI";
                ImGui.PushID("UI / About");
                try { plugin.DrawWindowAppearance(); }
                finally { ImGui.PopID(); ImGui.EndTabItem(); }
            }
            if (UiGui.BeginTabItem("UI / About"))
            {
                currentTab = "UI";
                DrawUiAboutTab();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    private void DrawPartyTab(CharacterConfig config)
    {
        ImGui.Spacing();

        if (IsDefaultConfigSelected())
        {
            var enabledByDefault = config.Enabled;
            if (UiGui.Checkbox("Fren Rider enabled by default", ref enabledByDefault))
            {
                config.Enabled = enabledByDefault;
                configManager.SaveCurrentAccount();
            }
            HelpMarker("New character profiles inherit this setting. Existing characters change only when you use a sync action.");
            DrawDefaultSettingSyncButton("Fren Rider enabled by default");
            DrawAllFrenRiderButton(false);
            DrawAllFrenRiderButton(true);
            ImGui.Spacing();
        }

        // Fren Name with party dropdown and capitalization fix
        UiGui.Text("Fren Name");
        ImGui.SameLine();
        HelpMarker("Name of the party member to follow. Can be partial if unique.\nThe @Server part is cosmetic for display; targeting uses the name before @.\nNames are auto-capitalized. Select from party or type manually.");
        DrawDefaultSettingSyncButton("Fren Name");

        if (configuration.KrangleEnabled)
        {
            // Krangled: show read-only garbled name
            var krangled = Disp(config.FrenName);
            ImGui.SetNextItemWidth(300);
            UiGui.InputText("##FrenNameKrangled", ref krangled, 64, ImGuiInputTextFlags.ReadOnly);
            if (ImGui.IsItemHovered())
                UiGui.SetTooltip("Disable Krangle to edit fren name.");
        }
        else
        {
            if (frenNameInput != config.FrenName && !frenNameFocused)
                frenNameInput = config.FrenName;
            ImGui.SetNextItemWidth(300);
            UiGui.InputText("##FrenName", ref frenNameInput, 64);
            frenNameFocused = ImGui.IsItemActive();
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                config.FrenName = ConfigManager.FixNameCapitalization(frenNameInput);
                frenNameInput = config.FrenName;
                configManager.SaveCurrentAccount();
            }

            // Party member quick-select dropdown
            ImGui.SameLine();
            if (UiGui.BeginCombo("##PartySelect", "", ImGuiComboFlags.NoPreview | ImGuiComboFlags.PopupAlignLeft))
            {
                var partyCount = Plugin.PartyList.Length;
                if (partyCount > 0)
                {
                    for (var i = 0; i < partyCount; i++)
                    {
                        var member = Plugin.PartyList[i];
                        if (member == null) continue;
                        var memberName = member.Name.ToString();
                        var worldName = member.World.Value.Name.ToString();
                        var display = $"{memberName}@{worldName}";
                        if (UiGui.Selectable(display))
                        {
                            config.FrenName = display;
                            frenNameInput = display;
                            configManager.SaveCurrentAccount();
                        }
                    }
                }
                else
                {
                    UiGui.TextDisabled("Not in a party");
                }
                UiGui.EndCombo();
            }
            if (ImGui.IsItemHovered())
                UiGui.SetTooltip("Select from current party members");

            // Add to Whitelist button
            ImGui.SameLine();
            var currentFren = config.FrenName;
            var frenBase = currentFren.Split('@')[0].Trim();
            var canAddWl = !string.IsNullOrEmpty(frenBase) && !config.InviteWhitelist.Contains(frenBase);
            if (!canAddWl) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
            if (UiGui.SmallButton("WL+"))
            {
                if (canAddWl)
                {
                    config.InviteWhitelist.Add(ConfigManager.FixNameCapitalization(frenBase));
                    configManager.SaveCurrentAccount();
                }
            }
            if (!canAddWl) ImGui.PopStyleVar();
            if (ImGui.IsItemHovered())
                UiGui.SetTooltip(canAddWl
                    ? $"Add '{frenBase}' to Invite Whitelist"
                    : string.IsNullOrEmpty(frenBase) ? "No fren name set" : $"'{frenBase}' already in whitelist");
        }

        ImGui.Spacing();

        if (GetEditingRemoteProfile() == null)
        {
            UiGui.Text("DAD Profile Acceptance");
            ImGui.SameLine();
            HelpMarker("Temporary uses an in-memory profile only for the exact DAD proposal. Off opts this local character out. Permanent replaces this local character's profile while keeping this choice.");
            DrawDefaultSettingSyncButton("DAD Profile Acceptance");

            var acceptance = (int)config.ProfileAcceptancePolicy;
            ImGui.SetNextItemWidth(220f);
            if (UiGui.Combo("##DadProfileAcceptance", ref acceptance, "Temporary\0Off\0Permanent\0"))
            {
                config.ProfileAcceptancePolicy = (FrenRiderProfileAcceptancePolicy)acceptance;
                configManager.SaveCurrentAccount();
            }

            if (config.ProfileAcceptancePolicy == FrenRiderProfileAcceptancePolicy.Permanent)
                UiGui.TextColored(new Vector4(1f, 0.72f, 0.2f, 1f), "Incoming DAD profiles will replace this character's saved profile.");

            ImGui.Spacing();
        }

        // Fly You Fools
        var flyYouFools = config.FlyYouFools;
        if (UiGui.Checkbox("Fly You Fools (fly alongside instead of pillion)", ref flyYouFools))
        {
            config.FlyYouFools = flyYouFools;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("If enabled, you will summon your own mount instead of pillion riding.\nUseful for flying zones.\n\n⚠️ IMPORTANT: This feature requires you to be grouped with your fren.\nIt will not work properly if ungrouped (won't jump into air to follow).");

        DrawDefaultSettingSyncButton("Fly You Fools");

        var tryTeleport = config.TryTeleportToFrenWhenOutOfZone;
        if (UiGui.Checkbox("Try Teleport to Fren When Out of Zone", ref tryTeleport))
        {
            config.TryTeleportToFrenWhenOutOfZone = tryTeleport;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("When enabled, FrenRider waits after your configured fren is still in party but no longer visible, then asks Lifestream to teleport to a random unlocked aetheryte in that zone.");

        DrawDefaultSettingSyncButton("Try Teleport to Fren When Out of Zone");

        if (tryTeleport)
        {
            ImGui.Indent();
            var followLocalNetworks = config.FollowLocalAetheryteNetworks;
            if (UiGui.Checkbox("Follow local aetheryte networks", ref followLocalNetworks))
            {
                config.FollowLocalAetheryteNetworks = followLocalNetworks;
                configManager.SaveCurrentAccount();
            }
            ImGui.SameLine();
            HelpMarker("Immediately follow a fren's aethernet jump through Lifestream when you are at the same origin. Supports connected city, residential, and custom networks, including Bozja and Eureka.");
            DrawDefaultSettingSyncButton("Follow local aetheryte networks");
            ImGui.Unindent();
        }

        var teleportDelay = config.TeleportToFrenDelaySeconds;
        var clampedTeleportDelay = Math.Clamp(teleportDelay, 5, 300);
        if (teleportDelay != clampedTeleportDelay)
        {
            teleportDelay = clampedTeleportDelay;
            config.TeleportToFrenDelaySeconds = clampedTeleportDelay;
            configManager.SaveCurrentAccount();
        }

        ImGui.SetNextItemWidth(120);
        if (UiGui.InputInt("Teleport Delay (seconds)", ref teleportDelay))
        {
            config.TeleportToFrenDelaySeconds = Math.Clamp(teleportDelay, 5, 300);
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Delay before opening the party window and teleporting. Allowed range: 5-300 seconds. Default: 30.");

        DrawDefaultSettingSyncButton("Teleport Delay");

        var nudgeInDutyWithoutFren = config.NudgeInDutyWhenFrenNotNearbyOrInZone;
        if (UiGui.Checkbox("Nudge in duty when fren not nearby/in-zone", ref nudgeInDutyWithoutFren))
        {
            config.NudgeInDutyWhenFrenNotNearbyOrInZone = nudgeInDutyWithoutFren;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("When enabled, FrenRider may issue the fallback forward nudge inside duties even if the configured fren is not visible in your party/object table. Leave disabled for safer duty movement.");
        DrawDefaultSettingSyncButton("Nudge in duty when fren not nearby/in-zone");

        var phoenixRecovery = config.UsePhoenixDownsForRecovery;
        if (UiGui.Checkbox("Use Phoenix Downs for recovery", ref phoenixRecovery))
        {
            config.UsePhoenixDownsForRecovery = phoenixRecovery;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Recover party members in regular four-player dungeons and outdoors. A living healer within 20 yalms of the corpse blocks item use. Party recovery pauses progression and defers Return while a living party rescuer remains.");
        DrawDefaultSettingSyncButton("Use Phoenix Downs for recovery");

        var reviveAnyone = config.ReviveAnyoneOutdoors;
        if (UiGui.Checkbox("Revive anyone within range outdoors", ref reviveAnyone))
        {
            config.ReviveAnyoneOutdoors = reviveAnyone;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Additionally revive non-party players within 15 yalms outdoors. Party members and healers have priority. Never approach strangers.");
        DrawDefaultSettingSyncButton("Revive anyone within range outdoors");

        var phoenixInCombat = config.AllowPhoenixDownInCombat;
        if (UiGui.Checkbox("Allow Phoenix Down use during combat", ref phoenixInCombat))
        {
            config.AllowPhoenixDownInCombat = phoenixInCombat;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Applies in dungeons and outdoors. Off waits for the rescuer to leave combat before approaching or using an item; current combat may finish, but new pulls stay blocked. On still respects native item availability and every other recovery gate.");
        DrawDefaultSettingSyncButton("Allow Phoenix Down use during combat");

        var respawnOutsideDuties = config.RespawnOutsideDuties;
        if (UiGui.Checkbox("Respawn after death outside duties after", ref respawnOutsideDuties))
        {
            config.RespawnOutsideDuties = respawnOutsideDuties;
            configManager.SaveCurrentAccount();
        }

        var respawnDelay = Math.Max(1, config.RespawnOutsideDutiesDelaySeconds);
        if (respawnDelay != config.RespawnOutsideDutiesDelaySeconds)
        {
            config.RespawnOutsideDutiesDelaySeconds = respawnDelay;
            configManager.SaveCurrentAccount();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        if (UiGui.InputInt("seconds##RespawnOutsideDutiesDelay", ref respawnDelay))
        {
            config.RespawnOutsideDutiesDelaySeconds = Math.Max(1, respawnDelay);
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("After remaining continuously unconscious outside duties for this delay, open the Return prompt and accept it. Minimum: 1 second. Default: 60.");

        DrawDefaultSettingSyncButton("Respawn after death outside duties");

        var respawnInsideDuties = config.RespawnInsideDuties;
        if (UiGui.Checkbox("Respawn after death inside duties after", ref respawnInsideDuties))
        {
            config.RespawnInsideDuties = respawnInsideDuties;
            configManager.SaveCurrentAccount();
        }

        var respawnInsideDelay = Math.Max(1, config.RespawnInsideDutiesDelaySeconds);
        if (respawnInsideDelay != config.RespawnInsideDutiesDelaySeconds)
        {
            config.RespawnInsideDutiesDelaySeconds = respawnInsideDelay;
            configManager.SaveCurrentAccount();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        if (UiGui.InputInt("seconds##RespawnInsideDutiesDelay", ref respawnInsideDelay))
        {
            config.RespawnInsideDutiesDelaySeconds = Math.Max(1, respawnInsideDelay);
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("After remaining continuously unconscious inside duties for this delay, open the Return prompt and accept it. Duty scope is selected only by BoundByDuty. Minimum: 1 second. Default: 60.");

        DrawDefaultSettingSyncButton("Respawn after death inside duties");

        var mountUpToChaseFren = config.MountUpToChaseFren;
        if (UiGui.Checkbox("Mount-up to chase fren if >", ref mountUpToChaseFren))
        {
            config.MountUpToChaseFren = mountUpToChaseFren;
            configManager.SaveCurrentAccount();
        }

        var chaseDistance = float.IsFinite(config.MountUpToChaseFrenDistance)
            ? Math.Max(1f, config.MountUpToChaseFrenDistance)
            : 1f;
        if (chaseDistance != config.MountUpToChaseFrenDistance)
        {
            config.MountUpToChaseFrenDistance = chaseDistance;
            configManager.SaveCurrentAccount();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        if (UiGui.InputFloat("y from fren##MountUpToChaseFrenDistance", ref chaseDistance))
        {
            config.MountUpToChaseFrenDistance = float.IsFinite(chaseDistance)
                ? Math.Max(1f, chaseDistance)
                : 1f;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        UiGui.Text("after");
        ImGui.SameLine();

        var chaseDelay = Math.Clamp(config.MountUpToChaseFrenDelaySeconds, 0, 300);
        if (chaseDelay != config.MountUpToChaseFrenDelaySeconds)
        {
            config.MountUpToChaseFrenDelaySeconds = chaseDelay;
            configManager.SaveCurrentAccount();
        }

        ImGui.SetNextItemWidth(100);
        if (UiGui.InputInt("seconds##MountUpToChaseFrenDelaySeconds", ref chaseDelay))
        {
            config.MountUpToChaseFrenDelaySeconds = Math.Clamp(chaseDelay, 0, 300);
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Outside duties, summon your configured own mount and fly toward a visible fren after horizontal XZ distance exceeds this threshold continuously for the configured delay. Existing zone-specific Max Follow Distance still applies. Distance minimum: 1y. Delay range: 0-300 seconds; 0 means immediate. Defaults: 100y and 30 seconds.");

        DrawDefaultSettingSyncButton("Mount-up to chase fren");

        // Mount Name (searchable dropdown from game data)
        UiGui.Text("Mount Name (if flying solo)");
        ImGui.SameLine();
        HelpMarker("Select the mount to use when flying solo.\n'Mount Roulette' picks a random mount.\nType to search the list.");

        DrawDefaultSettingSyncButton("Mount Name");

        var mountNames = plugin.MountNames;
        var currentMount = config.FoolFlier;
        ImGui.SetNextItemWidth(300);
        if (UiGui.BeginCombo("##MountSelect", string.IsNullOrEmpty(currentMount) ? "(none)" : currentMount))
        {
            // Search field - fixed at top
            ImGui.SetNextItemWidth(-1);
            UiGui.InputText("##MountSearch", ref mountSearch, 64);
            ImGui.Separator();
            
            // Scrollable list area
            ImGui.BeginChild("##MountList", new Vector2(0, 200), false);
            for (var i = 0; i < mountNames.Length; i++)
            {
                if (!string.IsNullOrEmpty(mountSearch) &&
                    !mountNames[i].Contains(mountSearch, StringComparison.OrdinalIgnoreCase))
                    continue;

                var isSelected = mountNames[i] == currentMount;
                if (UiGui.Selectable(mountNames[i], isSelected))
                {
                    config.FoolFlier = mountNames[i];
                    configManager.SaveCurrentAccount();
                    mountSearch = "";
                }
                if (isSelected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndChild();
            UiGui.EndCombo();
        }

        // Auto Discard
        var autoDiscard = config.EnableAutoDiscard;
        if (UiGui.Checkbox("Auto Discard (/ays discard)", ref autoDiscard))
        {
            config.EnableAutoDiscard = autoDiscard;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Runs /ays discard every 10s only while mounted and in a safe idle window.\nFrenRider defers discard during combat, cutscenes, and area transitions.\nRequires AutoRetainer plugin.");
        DrawDefaultSettingSyncButton("Auto Discard");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Update Interval
        var updateInterval = config.UpdateInterval;
        ImGui.SetNextItemWidth(200);
        if (UiGui.InputFloat("Update Interval (seconds)", ref updateInterval, 0.01f, 0.1f, "%.3f"))
        {
            config.UpdateInterval = Math.Max(0.05f, updateInterval);
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("How often the plugin runs its main logic loop.\nLower values = more responsive but higher CPU usage.\nDefault: 0.3s. WARNING: Values below 0.1 may impact performance.");
        DrawDefaultSettingSyncButton("Update Interval");
        if (updateInterval < 0.1f)
        {
            UiGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1), "WARNING: Very low update interval may impact game performance!");
        }
    }

    private void DrawChocoboTab(CharacterConfig config)
    {
        ImGui.Spacing();
        UiGui.Text("Companion purchasing");
        var foodPreview = ChocoboFoodName(config.ChocoboFoodItemId);
        var foodComboOpen = UiGui.BeginCombo("Companion food", foodPreview);
        if (!foodComboOpen && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            MaterialText.SetTooltip(ChocoboFoodTooltip(config.ChocoboFoodItemId));
        if (foodComboOpen)
        {
            try
            {
                foreach (var food in ChocoboFoods)
                {
                    var selected = config.ChocoboFoodItemId == food.ItemId;
                    if (UiGui.Selectable($"##ChocoboFood{food.ItemId}", selected, UiText.T(food.Name)))
                    {
                        config.ChocoboFoodItemId = food.ItemId;
                        configManager.SaveCurrentAccount();
                    }
                    if (ImGui.IsItemHovered())
                        MaterialText.SetTooltip(ChocoboFoodTooltip(food.ItemId));
                    if (selected) ImGui.SetItemDefaultFocus();
                }
            }
            finally { UiGui.EndCombo(); }
        }
        DrawDefaultSettingSyncButton("Companion food");
        var greensStockTarget = config.ChocoboGreensStockTarget;
        if (UiGui.InputInt("Gysahl Greens stock target", ref greensStockTarget))
        {
            config.ChocoboGreensStockTarget = Math.Max(0, greensStockTarget);
            configManager.SaveCurrentAccount();
        }
        DrawDefaultSettingSyncButton("Gysahl Greens stock target");
        var foodStockTarget = config.ChocoboFoodStockTarget;
        if (UiGui.InputInt("Companion food stock target", ref foodStockTarget))
        {
            config.ChocoboFoodStockTarget = Math.Max(0, foodStockTarget);
            configManager.SaveCurrentAccount();
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            MaterialText.SetTooltip(ChocoboFoodTooltip(config.ChocoboFoodItemId));
        DrawDefaultSettingSyncButton("Companion food stock target");
        UiGui.TextWrapped("Buy travels to the vendor and fills the stock target once. Purchasing starts only when you press Buy. Zero targets disable purchasing.");
        UiGui.TextWrapped("Purchase actions use the active character's profile. Select that character to purchase.");
        var canPurchase = Plugin.ClientState.IsLoggedIn
            && configManager.TryGetLocalActiveConfig(out var activePurchaseConfig)
            && ReferenceEquals(config, activePurchaseConfig);
        var foodCount = canPurchase ? GameHelpers.GetCompanionSupplyStock(config.ChocoboFoodItemId) : -1;
        var selectedFoodStock = foodCount < 0 ? "-" : foodCount.ToString();
        UiGui.Text(UiText.F("Selected food: {0} | NQ stock: {1} | Target: {2}",
            UiText.T(foodPreview), selectedFoodStock, config.ChocoboFoodStockTarget));
        if (ImGui.IsItemHovered())
            MaterialText.SetTooltip(ChocoboFoodTooltip(config.ChocoboFoodItemId));
        ImGui.BeginDisabled(!canPurchase);
        if (UiGui.Button("BUY GREENS")) plugin.PurchaseChocoboGreensNow();
        ImGui.SameLine();
        if (UiGui.Button("BUY FOOD")) plugin.PurchaseChocoboFoodNow();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            MaterialText.SetTooltip(ChocoboFoodTooltip(config.ChocoboFoodItemId));
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (UiGui.Button("Stop companion purchasing")) plugin.StopChocoboPurchasing();
        UiGui.TextWrapped("Buying companion food from Vath requires beast tribe progression through The Naming of Vath. Purchase is unavailable until the vendor is unlocked.");
        UiGui.Text("Purchasing status (active character)");
        UiGui.TextDisabled(plugin.ChocoboPurchaseStatus);

        ImGui.Separator();
        UiGui.Text("Chocobo reload testing");
        var probeAfterReload = configuration.ChocoboProbeAfterReload;
        if (UiGui.Checkbox("Run Companion discovery after reload", ref probeAfterReload))
            plugin.SetChocoboProbeAfterReload(probeAfterReload);
        UiGui.TextWrapped("Runs one read-only Companion probe per plugin load. Changing this switch takes effect on the next reload.");
        if (UiGui.Button("Run Companion discovery now")) plugin.RunChocoboProbe();
        if (UiGui.Button("Stop Companion discovery")) plugin.StopChocoboProbe();
        UiGui.TextDisabled(plugin.ChocoboProbeStatus);
        ImGui.Separator();
        // Summon Chocobo
        var forceGysahl = config.ForceGysahl;
        if (UiGui.Checkbox("Summon Chocobo", ref forceGysahl))
        {
            config.ForceGysahl = forceGysahl;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Auto-summon chocobo companion using Gysahl Greens when timer is low.\nWill not summon in sanctuaries or duties.");

        DrawDefaultSettingSyncButton("Summon Chocobo");

        // Show greens count when enabled
        if (config.ForceGysahl)
        {
            var greensCount = GameHelpers.GetInventoryItemCount(GameHelpers.GysahlGreensItemId);
            var buddyTime = GameHelpers.GetBuddyTimeRemaining();
            var mins = (int)(buddyTime / 60);
            var secs = (int)(buddyTime % 60);
            var timerText = buddyTime > 0 ? UiText.F("{0}m{1:D2}s", mins, secs) : UiText.T("Not summoned");
            var greensColor = greensCount > 0 ? new Vector4(0.3f, 1f, 0.3f, 1) : new Vector4(1f, 0.3f, 0.3f, 1);
            UiGui.Text("      ");
            ImGui.SameLine();
            UiGui.TextColored(greensColor, UiText.F("Gysahl Greens: {0}", greensCount));
            ImGui.SameLine();
            UiGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), " | " + UiText.F("Timer: {0}", timerText));
        }

        // Companion Stance (dropdown)
        var companionIdx = Array.IndexOf(CompanionStances, config.CompanionStrat);
        if (companionIdx < 0) companionIdx = 0;
        ImGui.SetNextItemWidth(200);
        if (UiGui.Combo("Companion Stance", ref companionIdx, CompanionStances, CompanionStances.Length))
        {
            config.CompanionStrat = CompanionStances[companionIdx];
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Chocobo companion battle stance.\nControls how your companion behaves in combat.");

        DrawDefaultSettingSyncButton("Companion Stance");

        ImGui.Separator();
        UiGui.Text("Chocobo food");
        var autoFeed = config.ChocoboAutoFeed;
        if (UiGui.Checkbox("Automatically feed Chocobo", ref autoFeed))
        {
            config.ChocoboAutoFeed = autoFeed;
            configManager.SaveCurrentAccount();
        }
        DrawDefaultSettingSyncButton("Automatically feed Chocobo");
        UiGui.TextWrapped("These settings belong to the profile being edited. Feed and Stop use the active character's runtime profile.");
        ImGui.BeginDisabled(!Plugin.ClientState.IsLoggedIn || !configManager.TryGetLocalActiveConfig(out _));
        if (UiGui.Button("Feed companion now")) plugin.FeedChocoboNow();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)
            && configManager.TryGetActiveConfig(out var feedingConfig) && feedingConfig != null)
            MaterialText.SetTooltip(ChocoboFoodTooltip(feedingConfig.ChocoboFoodItemId));
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (UiGui.Button("Stop companion feeding")) plugin.StopChocoboFeeding();
        UiGui.Text("Feeding status (active character)");
        UiGui.TextDisabled(plugin.ChocoboFoodStatus);

        ImGui.Separator();
        UiGui.Text("Chocobo skills");
        var autoAllocateSkills = config.ChocoboAutoAllocateSkills;
        if (UiGui.Checkbox("Automatically allocate Chocobo skills", ref autoAllocateSkills))
        {
            config.ChocoboAutoAllocateSkills = autoAllocateSkills;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Completes the first chosen tree before the next. Waits when the next skill costs more points than are available.");
        DrawDefaultSettingSyncButton("Automatically allocate Chocobo skills");
        DrawChocoboSkillPriority(config);
        UiGui.TextWrapped("These settings belong to the profile being edited. Allocate and Stop use the active character's runtime profile.");
        ImGui.BeginDisabled(!Plugin.ClientState.IsLoggedIn || !configManager.TryGetLocalActiveConfig(out _));
        if (UiGui.Button("Allocate skills now")) plugin.AllocateChocoboSkills();
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (UiGui.Button("Stop skill allocation")) plugin.StopChocoboSkills();
        UiGui.TextDisabled(plugin.ChocoboSkillStatus);

        ImGui.Separator();
        UiGui.Text("Current companion (active character)");
        UiGui.TextWrapped("These values describe the active character, regardless of the profile being edited.");
        if (GameHelpers.TryReadCompanion(out var companion))
        {
            UiGui.Text(UiText.F("Rank: {0} | Stars: {1}", companion.Rank, companion.Stars));
            UiGui.Text(UiText.F("Experience: {0}", companion.CurrentXp));
            UiGui.Text(UiText.F("Unused skill points: {0}", companion.SkillPoints));
            UiGui.Text(UiText.F("Defender level: {0}", companion.DefenderLevel));
            UiGui.Text(UiText.F("Attacker level: {0}", companion.AttackerLevel));
            UiGui.Text(UiText.F("Healer level: {0}", companion.HealerLevel));
        }
        else
            UiGui.TextDisabled("No companion data available. Log in to inspect it.");
        ImGui.Spacing();
        ImGui.BeginDisabled(Plugin.ObjectTable.LocalPlayer is null);
        if (UiGui.Button("Open game Companion window"))
            companionWindowStatus = GameHelpers.TryOpenCompanionWindow()
                ? "Companion window requested." : "Companion window is unavailable.";
        ImGui.EndDisabled();
        if (companionWindowStatus.Length > 0) UiGui.TextWrapped(companionWindowStatus);
    }

    private void DrawChocoboSkillPriority(CharacterConfig config)
    {
        var saved = config.ChocoboSkillPriority;
        var valid = saved is { Count: >= 1 and <= 3 } &&
                    saved.All(tree => tree is >= 0 and <= 2) && saved.Distinct().Count() == saved.Count;
        var order = new[] { -1, -1, -1 };
        if (valid)
            saved!.CopyTo(order);
        else
            UiGui.TextDisabled("Invalid skill priority in this profile. Choose a first skill tree.");

        for (var index = 0; index < order.Length; index++)
        {
            var choices = new List<int>();
            var labels = new List<string>();
            if (index > 0 || order[index] < 0)
            {
                choices.Add(-1);
                labels.Add(index == 0 ? "Choose a skill tree" : "None");
            }
            for (var tree = 0; tree < ChocoboSkillTrees.Length; tree++)
            {
                if (order[index] < 0 && order.Contains(tree)) continue;
                choices.Add(tree);
                labels.Add(ChocoboSkillTrees[tree]);
            }
            var choice = choices.IndexOf(order[index]);
            ImGui.BeginDisabled(index > 0 && order[index - 1] < 0);
            if (UiGui.Combo(ChocoboSkillPriorityLabels[index], ref choice, labels.ToArray(), labels.Count))
            {
                var selected = choices[choice];
                if (selected >= 0)
                {
                    var previousIndex = Array.IndexOf(order, selected);
                    if (previousIndex >= 0) order[previousIndex] = order[index];
                }
                order[index] = selected;
                config.ChocoboSkillPriority = order.Where(tree => tree >= 0).ToList();
                Array.Fill(order, -1);
                config.ChocoboSkillPriority.CopyTo(order);
                configManager.SaveCurrentAccount();
            }
            ImGui.EndDisabled();
        }
        DrawDefaultSettingSyncButton("Skill priority");
    }

    private void DrawDistanceTab(CharacterConfig config)
    {
        ImGui.Spacing();

        var cling = config.Cling;
        ImGui.SetNextItemWidth(200);
        if (UiGui.InputFloat("Cling Distance", ref cling, 0.5f, 1.0f, "%.3f"))
        {
            config.Cling = cling;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Distance threshold (yalms) to start following fren.\nWhen you are farther than this from fren, navigation begins.");
        DrawDefaultSettingSyncButton("Cling Distance");

        // Cling Type (no CBT)
        var clingType = config.ClingType;
        ImGui.SetNextItemWidth(200);
        if (UiGui.Combo("Cling Type", ref clingType, ClingTypes, ClingTypes.Length))
        {
            config.ClingType = clingType;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Navigation method to reach fren.\nNavMesh: VNavmesh plugin pathfinding (recommended)\nVisland: Alternative navigation\nBossMod Follow: Uses BossMod's follow leader\nVanilla Follow: Game's built-in /follow");
        DrawDefaultSettingSyncButton("Cling Type");

        var clingTypeDuty = config.ClingTypeDuty;
        ImGui.SetNextItemWidth(200);
        if (UiGui.Combo("Cling Type (Duty)", ref clingTypeDuty, ClingTypes, ClingTypes.Length))
        {
            config.ClingTypeDuty = clingTypeDuty;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Navigation method to use inside duties.\nMay need a different method than overworld.");
        DrawDefaultSettingSyncButton("Cling Type (Duty)");

        DrawClingExclusions(config);

        ImGui.Spacing();
        ImGui.Separator();
        UiGui.Text("Social Distancing");
        ImGui.Spacing();

        var sd = config.SocialDistancing;
        ImGui.SetNextItemWidth(200);
        if (UiGui.InputFloat("Social Distance (yalms)", ref sd, 0.5f, 1.0f, "%.3f"))
        {
            config.SocialDistancing = sd;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Minimum distance to maintain from fren in outdoor/foray zones.\nPrevents characters from stacking on top of each other (less bot-like).\nSet to 0 to disable.");
        DrawDefaultSettingSyncButton("Social Distance");

        var sdIndoors = config.SocialDistancingIndoors;
        ImGui.SetNextItemWidth(200);
        if (UiGui.Combo("Social Distance Indoors", ref sdIndoors, OnOff, OnOff.Length))
        {
            config.SocialDistancingIndoors = sdIndoors;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Enable social distancing indoors too.\nOff by default. Turn on if you want spacing in dungeons.");
        DrawDefaultSettingSyncButton("Social Distance Indoors");

        var xw = config.SocialDistanceXWiggle;
        ImGui.SetNextItemWidth(200);
        if (UiGui.InputFloat("X Wiggle (+/- yalms)", ref xw, 0.1f, 0.5f, "%.3f"))
        {
            config.SocialDistanceXWiggle = xw;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Random variance on X axis during social distancing.\nAdds natural-looking movement variance.");
        DrawDefaultSettingSyncButton("X Wiggle");

        var zw = config.SocialDistanceZWiggle;
        ImGui.SetNextItemWidth(200);
        if (UiGui.InputFloat("Z Wiggle (+/- yalms)", ref zw, 0.1f, 0.5f, "%.3f"))
        {
            config.SocialDistanceZWiggle = zw;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Random variance on Z axis during social distancing.");
        DrawDefaultSettingSyncButton("Z Wiggle");

        ImGui.Spacing();
        ImGui.Separator();
        UiGui.Text("Max Distances");
        ImGui.Spacing();

        var maxB = config.MaxBistance;
        ImGui.SetNextItemWidth(200);
        if (UiGui.InputFloat("Max Follow Distance", ref maxB))
        {
            config.MaxBistance = maxB;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Maximum distance (yalms) to chase fren.\nBeyond this, stop following to avoid zone-hopping.");
        DrawDefaultSettingSyncButton("Max Follow Distance");

        var maxBf = config.MaxBistanceForay;
        ImGui.SetNextItemWidth(200);
        if (UiGui.InputFloat("Max Follow Distance (Foray)", ref maxBf))
        {
            config.MaxBistanceForay = maxBf;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Max follow distance in forays (Eureka/Bozja).\nLower value to avoid mini-aetheryte transition issues.");
        DrawDefaultSettingSyncButton("Max Follow Distance (Foray)");

        var dd = config.DDDistance;
        ImGui.SetNextItemWidth(200);
        if (UiGui.InputFloat("DD Extra Distance", ref dd))
        {
            config.DDDistance = dd;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Extra distance added to cling in Deep Dungeons.\nPrevents constant chasing in PotD/HoH.");
        DrawDefaultSettingSyncButton("DD Extra Distance");

        var fd = config.FDistance;
        ImGui.SetNextItemWidth(200);
        if (UiGui.InputFloat("FATE Extra Distance", ref fd))
        {
            config.FDistance = fd;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Reserved for future autosync FATE behavior.\nCurrent follow distance does not change on FATE join or leave.");
        DrawDefaultSettingSyncButton("FATE Extra Distance");

        var autoSyncFate = config.AutoSyncFate;
        if (UiGui.Checkbox("Auto Sync FATE", ref autoSyncFate))
        {
            config.AutoSyncFate = autoSyncFate;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        if (UiGui.SmallButton("DISABLE PANDORA's BOX"))
        {
            GameHelpers.SendChatCommand("/xldisableplugin Pandora's Box", "[FR][FATE-SYNC]");
        }
        ImGui.SameLine();
        HelpMarker("Runs /levelsync on after joining a FATE.\nDefers while mounted or riding pillion.");
        DrawDefaultSettingSyncButton("Auto Sync FATE");

        var pauseClingForFate = config.PauseClingForFate;
        if (UiGui.Checkbox("Pause cling for FATE", ref pauseClingForFate))
        {
            config.PauseClingForFate = pauseClingForFate;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("While driving your own mount with Fly You Fools, safely land and dismount, then pause cling until the FATE and combat end. Pillion passengers are never dismounted.");
        DrawDefaultSettingSyncButton("Pause cling for FATE");

        var ignoreFates = config.IgnoreFates;
        if (UiGui.Checkbox("Ignore FATEs", ref ignoreFates))
        {
            config.IgnoreFates = ignoreFates;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Skip automatic FATE sync and FATE preset overrides, and ignore Pause cling for FATE. Ordinary travel and self-defence continue.");
        DrawDefaultSettingSyncButton("Ignore FATEs");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var formation = config.Formation;
        if (UiGui.Checkbox("Formation Following", ref formation))
        {
            config.Formation = formation;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Follow in a formation pattern (8-person grid).\nPositions based on party slot number.\nDisabled during mounting.");
        DrawDefaultSettingSyncButton("Formation Following");

        var fic = config.FollowInCombat;
        ImGui.SetNextItemWidth(200);
        if (UiGui.Combo("Follow in Combat", ref fic, FollowInCombatOptions, FollowInCombatOptions.Length))
        {
            config.FollowInCombat = fic;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Whether to follow fren during combat.\nAuto: Let the plugin decide based on your job/role.");
        DrawDefaultSettingSyncButton("Follow in Combat");

        var hcr = config.HClingReset;
        ImGui.SetNextItemWidth(200);
        if (UiGui.InputInt("Harmonized Cling Reset Ticks", ref hcr))
        {
            config.HClingReset = hcr;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Number of ticks before harmonized cling resets to 0.\nHandles special logic like DD/FATE force cling.");
        DrawDefaultSettingSyncButton("Harmonized Cling Reset Ticks");
    }

    private void DrawClingExclusions(CharacterConfig config)
    {
        ImGui.Spacing();
        ImGui.Separator();
        UiGui.Text("Cling exclusions");
        UiGui.TextWrapped("Pause cling in these areas. Seek and teleport travel remain available.");
        DrawDefaultSettingSyncButton("Cling exclusions");

        var territories = Plugin.DataManager.GetExcelSheet<TerritoryType>();
        string AreaLabel(uint id)
        {
            var name = territories?.GetRowOrDefault(id)?.PlaceName.ValueNullable?.Name.ToString();
            return string.IsNullOrWhiteSpace(name) ? UiText.F("Area {0}", id) : $"{name} ({id})";
        }

        ImGui.PushID("ClingExclusions");
        try
        {
            if (config.ClingExcludedTerritoryIds.Count == 0)
                UiGui.TextDisabled("No cling exclusions.");

            for (var index = 0; index < config.ClingExcludedTerritoryIds.Count; index++)
            {
                var id = config.ClingExcludedTerritoryIds[index];
                ImGui.PushID(index);
                try
                {
                    MaterialText.Text(AreaLabel(id));
                    ImGui.SameLine();
                    if (UiGui.SmallButton("X##RemoveClingExclusion"))
                    {
                        config.ClingExcludedTerritoryIds.RemoveAt(index);
                        configManager.SaveCurrentAccount();
                        break;
                    }
                }
                finally { ImGui.PopID(); }
            }

            ImGui.SetNextItemWidth(200);
            UiGui.InputText("Search##ClingExclusionSearch", ref clingExclusionSearch, 128);
            ImGui.SetNextItemWidth(200);
            if (UiGui.BeginCombo("Area", clingExclusionAreaToAdd == 0
                    ? "Choose an area" : AreaLabel(clingExclusionAreaToAdd)))
            {
                try
                {
                    if (territories != null)
                    {
                        foreach (var territory in territories)
                        {
                            var name = territory.PlaceName.ValueNullable?.Name.ToString();
                            if (territory.RowId == 0 || string.IsNullOrWhiteSpace(name))
                                continue;
                            var label = AreaLabel(territory.RowId);
                            if (!label.Contains(clingExclusionSearch, StringComparison.OrdinalIgnoreCase))
                                continue;
                            ImGui.PushID((int)territory.RowId);
                            try
                            {
                                if (UiGui.Selectable("##ClingArea", territory.RowId == clingExclusionAreaToAdd, label))
                                    clingExclusionAreaToAdd = territory.RowId;
                            }
                            finally { ImGui.PopID(); }
                        }
                    }
                }
                finally { UiGui.EndCombo(); }
            }

            ImGui.BeginDisabled(clingExclusionAreaToAdd == 0
                || config.ClingExcludedTerritoryIds.Contains(clingExclusionAreaToAdd));
            if (UiGui.SmallButton("Add##ClingExclusion"))
            {
                config.ClingExcludedTerritoryIds.Add(clingExclusionAreaToAdd);
                configManager.SaveCurrentAccount();
            }
            ImGui.EndDisabled();
        }
        finally { ImGui.PopID(); }
    }

    private void DrawBossModPresetSelector(string label, CharacterConfig config, int selector, BossModPresetCatalog catalog)
    {
        var saved = CombatService.ReadManualPresetSelector(config, selector);
        if (plugin.CombatService.IsCurrentManualPresetSelector(config, selector))
        {
            var resolved = AutorotIpcService.ResolvePresetSelection(saved, catalog);
            if (!string.Equals(saved, resolved, StringComparison.Ordinal))
            {
                CombatService.WriteManualPresetSelector(config, selector, resolved);
                configManager.SaveCurrentAccount();
                saved = resolved;
            }
        }
        ImGui.SetNextItemWidth(200);
        if (UiGui.BeginCombo(label, AutorotIpcService.IsNoPreset(saved) ? UiText.T("(none)") : saved, literalPreview: true))
        {
            try
            {
                if (UiGui.Selectable("##NoBossModPreset", AutorotIpcService.IsNoPreset(saved), UiText.T("(none)")))
                {
                    CombatService.WriteManualPresetSelector(config, selector, "none");
                    configManager.SaveCurrentAccount();
                }
                for (var index = 0; index < catalog.DisplayedNames.Count; index++)
                {
                    var name = catalog.DisplayedNames[index];
                    if (UiGui.Selectable($"##BossModPreset{index}", string.Equals(saved, name, StringComparison.Ordinal), name))
                    {
                        CombatService.WriteManualPresetSelector(config, selector, name);
                        configManager.SaveCurrentAccount();
                    }
                }
            }
            finally { UiGui.EndCombo(); }
        }
        ImGui.SameLine();
        HelpMarker("Choose a preset from the selected BossMod provider's complete catalog. None leaves its current preset unchanged.");
        DrawDefaultSettingSyncButton(label);
    }

    private void DrawCombatTab(CharacterConfig config)
    {
        ImGui.Spacing();

        if (configManager.TryGetActiveConfig(out var activeConfig) && activeConfig != null)
        {
            UiGui.Text(UiText.F("Effective rotation: {0} ({1})",
                plugin.CombatService.GetConfiguredRotationProvider(activeConfig),
                UiText.T(plugin.ZoneService.CurrentZone == ZoneType.Foray ? "Foray" : "Normal")));
        }
        if (IsDefaultConfigSelected())
            UiGui.TextWrapped("DEFAULT CONFIG is a template. Combat uses the active character profile.");
        else if (configManager.HasTemporaryProfile &&
                 string.Equals(editingCharacterKey, configManager.ActiveCharacterKey, StringComparison.Ordinal) &&
                 GetEditingRemoteProfile() == null)
            UiGui.TextWrapped("Temporary DAD profile supplies the effective rotation; the controls below edit the saved profile.");

        // Rotation Plugin (dropdown)
        var rotPlugin = Array.IndexOf(RotationPluginIds, config.RotationPlugin);
        ImGui.SetNextItemWidth(200);
        if (UiGui.Combo("Rotation Plugin", ref rotPlugin, RotationPlugins, RotationPlugins.Length))
        {
            config.RotationPlugin = RotationPluginIds[rotPlugin];
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Which rotation automation plugin to use.\nBMR: BossModReborn\nVBM: VanillaBossMod\nRSR: RotationSolver Reborn\nWRATH: Wrath\nDAEDALUS: Daedalus");
        DrawDefaultSettingSyncButton("Rotation Plugin");

        // Rotation Plugin Foray (dropdown)
        var rotPluginForay = Array.IndexOf(RotationPluginIds, config.RotationPluginForay);
        ImGui.SetNextItemWidth(200);
        if (UiGui.Combo("Rotation Plugin (Foray)", ref rotPluginForay, RotationPlugins, RotationPlugins.Length))
        {
            config.RotationPluginForay = RotationPluginIds[rotPluginForay];
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Rotation plugin for foray content (Eureka/Bozja).\nWRATH recommended for phantom job support.");
        DrawDefaultSettingSyncButton("Rotation Plugin (Foray)");

        var isForayContext = plugin.ZoneService.CurrentZone == ZoneType.Foray;
        if (DaedalusTargetModeService.IsEffectiveRotation(
                config.RotationPlugin,
                config.RotationPluginForay,
                isForayContext))
        {
            var daedalusTargetMode = (int)config.DaedalusTargetMode;
            ImGui.SetNextItemWidth(200);
            if (UiGui.Combo(
                    "Daedalus Engage Mode",
                    ref daedalusTargetMode,
                    DaedalusTargetModes,
                    DaedalusTargetModes.Length))
            {
                config.DaedalusTargetMode = Enum.IsDefined(typeof(DaedalusTargetMode), daedalusTargetMode)
                    ? (DaedalusTargetMode)daedalusTargetMode
                    : DaedalusTargetMode.None;
                configManager.SaveCurrentAccount();
                if (ReferenceEquals(config, configManager.GetActiveConfig()))
                    plugin.DaedalusTargetModeService.Apply(config.DaedalusTargetMode, notifyUser: true);
            }
            ImGui.SameLine();
            HelpMarker("Party engage mode sent through Daedalus LAN coordination.\nFocus requires a living enemy hard target.\nNone, Split, and Kill Adds preserve Daedalus's current focus and off-tank assignments.");
            DrawDefaultSettingSyncButton("Daedalus Engage Mode");
        }

        ImGui.Spacing();
        ImGui.Separator();
        UiGui.Text("Presets");
        ImGui.Spacing();

        var manualPresetConfig = config.ConfigureRotationPresetManually;
        if (UiGui.Checkbox("Configure rotation preset manually", ref manualPresetConfig))
        {
            config.ConfigureRotationPresetManually = manualPresetConfig;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Off: FrenRider chooses BossMod presets from your job and rotation provider.\nOn: use the BossMod presets below with any rotation provider. A custom preset may run actions alongside RSR, WRATH or DAEDALUS.");
        DrawDefaultSettingSyncButton("Configure rotation preset manually");

        if (config.ConfigureRotationPresetManually)
        {
            var catalog = plugin.AutorotIpcService.ReadPresetCatalog(plugin.CombatService.GetConfiguredRotationProvider(config));
            DrawBossModPresetSelector("BM Rotation Preset", config, 0, catalog);
            DrawBossModPresetSelector("BM Rotation Preset (DD)", config, 1, catalog);
            DrawBossModPresetSelector("BM Rotation Preset (FATE)", config, 2, catalog);
            if (!catalog.Readable || catalog.DisplayedNames.Count == 0)
                UiGui.TextDisabled("BossMod preset catalog unavailable or empty; saved selections are retained.");
        }
        else
        {
            UiGui.TextDisabled("Managed presets: BMR/VBM use FRENRIDER role presets; RSR/WRATH/DAEDALUS use passive role presets.");
        }

        ImGui.Spacing();
        ImGui.Separator();
        UiGui.Text("Behavior");
        ImGui.Spacing();

        var cleanupMode = config.CleanupMode == FrenRiderCleanupMode.TurnEverythingOff ? 1 : 0;
        ImGui.SetNextItemWidth(200);
        if (UiGui.Combo("Cleanup Mode", ref cleanupMode, CleanupModes, CleanupModes.Length))
        {
            config.CleanupMode = cleanupMode == 1
                ? FrenRiderCleanupMode.TurnEverythingOff
                : FrenRiderCleanupMode.RestoreSnapshot;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Restore snapshot: put captured BMR/VBM AI on/off, follow, movement, CBT fields, and Daedalus enabled state back on /fr off.\nTurn everything off: disable BMR/VBM AI, CBT AutoFollow, RotationSolverReborn, Daedalus, and Wrath auto if FrenRider started it.");
        DrawDefaultSettingSyncButton("Cleanup Mode");

        // RSR Operating Mode (dropdown)
        var rotType = config.RotationType;
        ImGui.SetNextItemWidth(200);
        if (UiGui.Combo("RSR Operating Mode", ref rotType, RsrOperatingModes, RsrOperatingModes.Length))
        {
            config.RotationType = rotType;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("RSR operating mode when RSR is the selected rotation plugin.\nAuto: Full auto.\nManual: Manual targeting mode.\nNone: Don't let FrenRider change the current rotation state.\nSupport: Uses RSR's plugin-managed support mode.");
        DrawDefaultSettingSyncButton("RSR Operating Mode");

        var rsrAggroType = config.RsrAggroType;
        ImGui.SetNextItemWidth(200);
        if (UiGui.Combo("RSR Aggro Type", ref rsrAggroType, RsrAggroTypes, RsrAggroTypes.Length))
        {
            config.RsrAggroType = rsrAggroType;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Hostile target selection sent to RotationSolver Reborn through its typed settings IPC.");
        DrawDefaultSettingSyncButton("RSR Aggro Type");

        // BossMod AI (dropdown)
        var bossModAI = config.BossModAI;
        ImGui.SetNextItemWidth(200);
        if (UiGui.Combo("BossMod AI", ref bossModAI, BossModAIOptions, BossModAIOptions.Length))
        {
            config.BossModAI = bossModAI;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("On: enable the selected BossMod AI implementation for combat.\nOff: send BMR and VBM AI off commands while preserving the rest of FrenRider's combat setup.");
        DrawDefaultSettingSyncButton("BossMod AI");

        var dontMoveWhileCasting = configuration.DontMoveWhileCasting;
        if (UiGui.Checkbox("Don't move while casting", ref dontMoveWhileCasting))
        {
            configuration.DontMoveWhileCasting = dontMoveWhileCasting;
            configuration.Save();
            plugin.BossModActionTweaksService.ApplyDontMoveWhileCasting(dontMoveWhileCasting);
        }
        ImGui.SameLine();
        HelpMarker("Global setting. On plugin load and whenever it changes, applies casting movement lock to currently loaded BMR, VBM, and RSR plugins.");

        var actionTweaks = plugin.BossModActionTweaksService;
        if (actionTweaks.HasResult)
        {
            var statusColor = actionTweaks.HasFailures
                ? UiHelpers.Red
                : actionTweaks.HasNotLoadedTargets
                    ? UiHelpers.Yellow
                    : UiHelpers.Green;
            UiGui.TextColored(statusColor, actionTweaks.StatusText);
        }

        // Positional (dropdown)
        var positional = config.PositionalInCombat;
        ImGui.SetNextItemWidth(200);
        if (UiGui.Combo("Positional", ref positional, Positionals, Positionals.Length))
        {
            config.PositionalInCombat = positional;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Combat positional preference.\nFront: Stay in front of target\nRear: Stay behind target\nAny: No preference\nAuto: Let plugin decide based on job");
        DrawDefaultSettingSyncButton("Positional");

        var maxAIDist = config.MaxAIDistance;
        ImGui.SetNextItemWidth(200);
        if (UiGui.InputFloat("Max AI Distance", ref maxAIDist))
        {
            config.MaxAIDistance = maxAIDist;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Max distance to targets for combat AI.\n424242 = Auto (plugin decides based on job: melee 2.6, caster 10).");
        DrawDefaultSettingSyncButton("Max AI Distance");

        var limitPct = config.LimitPct;
        ImGui.SetNextItemWidth(200);
        if (UiGui.InputFloat("LB Threshold %", ref limitPct))
        {
            config.LimitPct = limitPct;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Target HP percentage to use Limit Break.\n-1 = Disabled.\nAutomatically uses LB3 if available, otherwise LB2.");
        DrawDefaultSettingSyncButton("LB Threshold %");

        ImGui.Spacing();
        ImGui.Separator();
        UiGui.Text("Beastmaster Capture");
        var catchBeasts = config.TryToCatchBeasts;
        if (UiGui.Checkbox("Try to catch beasts", ref catchBeasts))
        {
            config.TryToCatchBeasts = catchBeasts;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("While FrenRider is enabled on Beastmaster, try Capture on eligible living beasts closer than 10 yalms, including passive beasts outside combat.\nPrefers an eligible current target; otherwise automatically targets the nearest eligible beast when Capture is ready.\nSkips owned beasts and enemies above your effective level, and respects both HP thresholds.\nRetries on later cooldowns, including already marked beasts.");
        DrawDefaultSettingSyncButton("Try to catch beasts");

        var farHp = config.CaptureHpFarBelow;
        ImGui.SetNextItemWidth(120);
        if (UiGui.InputInt("Capture HP: more than 5 levels below you", ref farHp))
        {
            config.CaptureHpFarBelow = farHp;
            configManager.SaveCurrentAccount();
        }
        DrawDefaultSettingSyncButton("Capture HP: more than 5 levels below you");

        var nearHp = config.CaptureHpNearOrEqual;
        ImGui.SetNextItemWidth(120);
        if (UiGui.InputInt("Capture HP: within 5 levels below you or equal", ref nearHp))
        {
            config.CaptureHpNearOrEqual = nearHp;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Both HP thresholds accept 1–100%. Exactly five levels below uses this second threshold.\n100% permits Capture on a full-health beast and may initiate combat.");
        DrawDefaultSettingSyncButton("Capture HP: within 5 levels below you or equal");

        if (UiGui.CollapsingHeader("Saved beasts"))
        {
            var account = configManager.GetCurrentAccount();
            if (!string.IsNullOrEmpty(editingRemoteRowId) || string.IsNullOrEmpty(editingCharacterKey) || account == null)
            {
                UiGui.TextWrapped("Select a local character to browse their saved beasts.");
            }
            else
            {
                account.UnlockedBeasts.TryGetValue(editingCharacterKey, out var owned);
                UiGui.TextWrapped(owned == null
                    ? "No confirmed list saved yet. This character's list refreshes while on Beastmaster."
                    : "Saved ownership refreshes while on Beastmaster, including when automatic Capture is off.");
                using var tightRows = plugin.Configuration.UiCompact ? MaterialTable.PushTightRows() : default;
                if (ImGui.BeginTable("SavedBeasts", 2, ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg,
                        new Vector2(0, 220)))
                {
                    ImGui.TableSetupColumn("Beast");
                    ImGui.TableSetupColumn("Saved ownership");
                    UiGui.TableHeadersRow();
                    foreach (var entry in plugin.BeastCaptureService.Roster)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        MaterialText.Text(entry.Name);
                        ImGui.TableNextColumn();
                        UiGui.TextUnformatted(owned == null ? "Unknown" : owned.Contains(entry.Id) ? "Owned" : "Missing");
                    }
                    ImGui.EndTable();
                }
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        UiGui.Text("Advanced");
        ImGui.Spacing();

        var obstacleMapsOn = config.ObstacleMapsOn;
        if (UiGui.Checkbox("Obstacle maps on", ref obstacleMapsOn))
        {
            config.ObstacleMapsOn = obstacleMapsOn;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Controls BossMod Reborn only. Default: off. Applies when FrenRider is enabled, regardless of combat provider.");
        DrawDefaultSettingSyncButton("Obstacle maps on");

        DrawHacksSection(config);
    }

    private void DrawHacksSection(CharacterConfig config)
    {
        ImGui.Spacing();
        ImGui.Separator();
        UiGui.Text("Hacks");
        ImGui.Spacing();

        var reduceRange = config.BmrReduceActivationRangeForOutdoorAreas;
        if (UiGui.Checkbox("BMR reduce activation range for outdoor areas", ref reduceRange))
        {
            config.BmrReduceActivationRangeForOutdoorAreas = reduceRange;
            configManager.SaveCurrentAccount();
            plugin.AdsReflectionIpcService.QueueImmediateUpdate();
        }
        ImGui.SameLine();
        HelpMarker($"When enabled, FrenRider asks ADS to set BMR MaxLoadDistance to {AdsReflectionIpcService.ReducedOutdoorMaxLoadDistance:0}.");
        DrawDefaultSettingSyncButton("BMR reduce activation range for outdoor areas");

        var disableHunts = config.BmrDisableHuntModules;
        if (UiGui.Checkbox("BMR Disable Hunt Modules", ref disableHunts))
        {
            config.BmrDisableHuntModules = disableHunts;
            configManager.SaveCurrentAccount();
            plugin.AdsReflectionIpcService.QueueImmediateUpdate();
        }
        ImGui.SameLine();
        HelpMarker("When enabled, FrenRider asks ADS to disable BMR hunt modules.");
        DrawDefaultSettingSyncButton("BMR Disable Hunt Modules");

        var disableQueen = config.BmrDisableQueenLunatender;
        if (UiGui.Checkbox("BMR Disable Queen Lunatender", ref disableQueen))
        {
            config.BmrDisableQueenLunatender = disableQueen;
            configManager.SaveCurrentAccount();
            plugin.AdsReflectionIpcService.QueueImmediateUpdate();
        }
        ImGui.SameLine();
        HelpMarker("When enabled, FrenRider asks ADS to disable the BMR Queen Lunatender module.");
        DrawDefaultSettingSyncButton("BMR Disable Queen Lunatender");

        var reflection = plugin.AdsReflectionIpcService;
        var statusColor = !reflection.IsAdsAvailable && reflection.HasPendingActions
            ? UiHelpers.Yellow
            : reflection.StatusText.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
                ? UiHelpers.Yellow
                : UiHelpers.Green;
        UiGui.TextColored(statusColor, $"ADS reflection: {reflection.StatusText}");

        if (reflection.NextAttemptAtUtc is { } nextAttempt && nextAttempt > DateTime.UtcNow)
        {
            var seconds = Math.Max(0, (int)Math.Ceiling((nextAttempt - DateTime.UtcNow).TotalSeconds));
            UiGui.TextColored(UiHelpers.Grey, $"  Next retry/reassert in {seconds}s.");
        }
    }

    private void DrawAdsTab(CharacterConfig config)
    {
        ImGui.Spacing();

        UiGui.Text("ADS Duty Handoff");
        ImGui.SameLine();
        HelpMarker("Per-duty-family ADS handoff.\nDuty name, category, support, and clearance come only from ADS CurrentDuty. FrenRider accepts that snapshot only when ADS catalog metadata is complete and its territory/CFC matches live GameMain; otherwise local duty logic stays active.\nThe handoff delay counts only while the character remains continuously ready: logged in, present, alive, conscious, out of area transitions, and out of cutscenes.\nRuntime ADS ownership is authoritative even after manual Start Inside. FrenRider pauses local duty systems while handoff is pending or ADS owns the run; configured exit takeover remains available after duty completion.");

        if (!config.AdsDutyFamilySettingsMigrated)
        {
            UiGui.TextDisabled(UiText.F("Legacy seed active: {0} at threshold {1}.", UiText.T(config.UseAdsIfAvailable ? "global handoff on" : "global handoff off"), Math.Clamp(config.AdsMaturityThreshold, 0, 3)));
            ImGui.SameLine();
            if (UiGui.SmallButton("Seed family rows from legacy values"))
            {
                config.EnsureAdsDutyFamilySettingsInitialized();
                configManager.SaveCurrentAccount();
            }
        }

        ImGui.Spacing();
        UiGui.Text("Duty Families");
        ImGui.SameLine();
        HelpMarker("Each family has its own enable toggle, maturity threshold, and continuous-ready handoff delay.\n0 = not cleared, 1 = unsync cleared, 2 = duty support cleared, 3 = proven sync clear.\nHandoff delay range: 2-300 seconds.");

        foreach (var entry in AdsDutyCategoryCatalog.Entries)
        {
            var controlled = configManager.IsQuestionableDutyFamilyControlled(config, entry.Category);
            var settings = configManager.GetEffectiveAdsDutyFamilySettings(config, entry.Category);
            ImGui.BeginDisabled(controlled);
            var enabled = settings.Enabled;
            if (UiGui.Checkbox($"{entry.Label}##AdsFamily{entry.Category}", ref enabled))
            {
                config.SetAdsDutyFamilySettings(
                    entry.Category,
                    enabled,
                    settings.MaturityThreshold,
                    settings.HandoffDelaySeconds);
                configManager.SaveCurrentAccount();
            }

            ImGui.SameLine();
            var threshold = Math.Clamp(settings.MaturityThreshold, 0, AdsMaturityOptions.Length - 1);
            ImGui.SetNextItemWidth(240);
            if (UiGui.Combo($"##AdsFamilyThreshold{entry.Category}", ref threshold, AdsMaturityOptions, AdsMaturityOptions.Length))
            {
                config.SetAdsDutyFamilySettings(
                    entry.Category,
                    enabled,
                    threshold,
                    settings.HandoffDelaySeconds);
                configManager.SaveCurrentAccount();
            }

            ImGui.SameLine();
            var handoffDelaySeconds = settings.HandoffDelaySeconds;
            ImGui.SetNextItemWidth(90);
            if (UiGui.InputInt($"##AdsFamilyHandoffDelay{entry.Category}", ref handoffDelaySeconds, 1, 10))
            {
                config.SetAdsDutyFamilySettings(entry.Category, enabled, threshold, handoffDelaySeconds);
                configManager.SaveCurrentAccount();
            }
            ImGui.SameLine();
            MaterialText.TextDisabled(UiText.T("sec ready"));
            DrawDefaultSettingSyncButton($"ADS {entry.Label}", $"AdsFamily{entry.Category}");
            ImGui.EndDisabled();
            if (controlled)
            {
                ImGui.SameLine();
                MaterialText.TextDisabled(UiText.T("Temporarily controlled by DAD"));
            }
        }

        if (UiGui.Button("OPEN ADS LOOT OPTIONS"))
            OpenAdsLootOptions();

        if (plugin.AdsIntegrationService is not null)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var adsStatus = plugin.AdsIntegrationService.StatusText;
            var adsColor = plugin.AdsIntegrationService.IsControllingDuty
                ? new Vector4(0.35f, 0.9f, 0.35f, 1f)
                : plugin.AdsIntegrationService.IsHandoffPending
                    ? new Vector4(0.95f, 0.8f, 0.3f, 1f)
                    : new Vector4(0.7f, 0.7f, 0.7f, 1f);
            UiGui.TextColored(adsColor, $"ADS Status: {adsStatus}");
            UiGui.TextDisabled(UiText.F($"Authority source: {plugin.AdsIntegrationService.RuntimeOwnershipSource}; readable={plugin.AdsIntegrationService.RuntimeOwnershipReadable}; exit takeover={plugin.AdsIntegrationService.ExitTakeoverActive}"));
        }
    }

    private static void OpenAdsLootOptions()
    {
        try
        {
            if (Plugin.PluginInterface.GetIpcSubscriber<bool>("ADS.ToggleLootUi").InvokeFunc())
                return;

            Plugin.Log.Warning("[FrenRider][ADS] ADS.ToggleLootUi returned false; not falling back to /ads loot.");
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug($"[FrenRider][ADS] ADS.ToggleLootUi unavailable: {ex.Message}");
            GameHelpers.SendChatCommand("/ads loot", "[FrenRider][ADS]");
        }
    }

    private void DrawDutyAdsExitTab(CharacterConfig config)
    {
        UiHelpers.SectionHeader("ADS Handoff");
        DrawAdsTab(config);

        UiHelpers.SectionHeader("Auto-Yes Dialogs");
        DrawAutoYesSection(config);

        UiHelpers.SectionHeader("Invite Whitelist");
        DrawInviteWhitelistSection(config);

        UiHelpers.SectionHeader("Exit Behavior");
        DrawExitBehaviourSection(config);
    }

    private void DrawAutomationTab(CharacterConfig config)
    {
        UiHelpers.SectionHeader("Loot");
        DrawLootSection(config);

        UiHelpers.SectionHeader("Food");
        DrawFoodSection(config);

        UiHelpers.SectionHeader("Repair");
        DrawRepairSection(config);

        UiHelpers.SectionHeader("Equipment");
        DrawEquipmentSection(config);

        UiHelpers.SectionHeader("Desynthesis");
        DrawDesynthesisSection(config);

        UiHelpers.SectionHeader("Idle Behavior");
        DrawIdleBehaviorSection(config);

        UiHelpers.SectionHeader("Maintenance");
        DrawAutoDiscardSection(config);
        DrawAutorotSection(config);

        UiHelpers.SectionHeader("Debug");
        DrawDebugLoggingSection(config);
    }

    private void DrawEquipmentSection(CharacterConfig config)
    {
        var equipJobStone = config.EquipJobStoneForCurrentClass;
        if (UiGui.Checkbox("Equip job stone for current class", ref equipJobStone))
        {
            config.EquipJobStoneForCurrentClass = equipJobStone;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("While Fren Rider is enabled, equip the first matching armoury-chest soul crystal when the current character is on a level 30+ base class and equipment can be changed safely. The current gearset is updated after the equip is confirmed.");
        DrawDefaultSettingSyncButton("Equip job stone for current class");
    }

    private void DrawUiAboutTab()
    {
        UiHelpers.SectionHeader("UI");
        DrawUiSettingsSection();

        UiHelpers.SectionHeader("About");
        DrawAboutTab();
    }

    private void DrawLootSection(CharacterConfig config)
    {
        var fulfIdx = Array.IndexOf(LootTypes, config.FulfType);
        if (fulfIdx < 0) fulfIdx = 0;
        ImGui.SetNextItemWidth(200);
        if (UiGui.Combo("Loot Type", ref fulfIdx, LootTypes, LootTypes.Length))
        {
            config.FulfType = LootTypes[fulfIdx];
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("How loot is handled if LazyLoot is installed.\n'unchanged' = Don't modify loot settings.");
        DrawDefaultSettingSyncButton("Loot Type");
    }

    private void DrawFoodSection(CharacterConfig config)
    {
        EnsureFoodItemsLoaded();
        BackfillLegacyFoodSelection(config);

        var foodId = config.FeedMeItemId;
        var foodName = config.FeedMeItem;
        if (DrawItemSearchDropdown("Food", ref foodSearch, foodItems, ref foodId, ref foodName))
        {
            config.FeedMeItemId = foodId;
            config.FeedMeItem = foodName;
            plugin.AutomationService.InvalidateFoodCache();
            configManager.SaveCurrentAccount();
        }
        DrawDefaultSettingSyncButton("Food");

        if (config.FeedMeItemId > 0)
        {
            UiGui.Text("  " + UiText.F("Selected: {0} {1} (ID: {2})", config.FeedMeItem, config.FeedMeUseHighQuality ? "[HQ]" : "[NQ]", config.FeedMeItemId));

            var useFoodHq = config.FeedMeUseHighQuality;
            if (UiGui.Checkbox("Use HQ food", ref useFoodHq))
            {
                config.FeedMeUseHighQuality = useFoodHq;
                plugin.AutomationService.InvalidateFoodCache();
                configManager.SaveCurrentAccount();
            }
            DrawDefaultSettingSyncButton("Use HQ food");

            if (UiGui.SmallButton("Clear Food"))
            {
                config.FeedMeItemId = 0;
                config.FeedMeItem = "";
                config.FeedMeUseHighQuality = false;
                foodSearch = "";
                plugin.AutomationService.InvalidateFoodCache();
                configManager.SaveCurrentAccount();
            }
        }
        else
        {
            UiGui.TextDisabled("  " + UiText.T("No food selected. Food is optional."));
        }

        var feedMeSearch = config.FeedMeSearch;
        if (UiGui.Checkbox("Search for Food if Depleted", ref feedMeSearch))
        {
            config.FeedMeSearch = feedMeSearch;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("If configured food runs out, search inventory for any food starting from lowest item ID.");
        DrawDefaultSettingSyncButton("Search for Food if Depleted");
    }

    private void BackfillLegacyFoodSelection(CharacterConfig config)
    {
        if (config.FeedMeItemId > 0)
        {
            if (!string.IsNullOrWhiteSpace(config.FeedMeItem)) return;

            var selected = foodItems.FirstOrDefault(item => item.Id == (uint)config.FeedMeItemId);
            if (selected.Id == 0) return;

            config.FeedMeItem = selected.Name;
            plugin.AutomationService.InvalidateFoodCache();
            configManager.SaveCurrentAccount();
            return;
        }

        if (string.IsNullOrWhiteSpace(config.FeedMeItem)) return;

        var match = foodItems.FirstOrDefault(item =>
            item.Name.Equals(config.FeedMeItem.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match.Id == 0) return;

        config.FeedMeItemId = (int)match.Id;
        config.FeedMeItem = match.Name;
        plugin.AutomationService.InvalidateFoodCache();
        configManager.SaveCurrentAccount();
    }

    private static bool DrawItemSearchDropdown(string label, ref string search, List<(uint Id, string Name)> items, ref int selectedId, ref string selectedName)
    {
        var changed = false;
        var displayText = selectedId > 0 ? $"{selectedName} ({selectedId})" : UiText.F("Select {0}...", UiText.T(label));

        // Item names are game data; keep the native preview outside authored-message matching.
        var minimumWidth = MathF.Ceiling(Math.Max(80 * AethertekUI.MaterialTheme.Metrics.Scale,
            MaterialText.Measure("00000000").X + 2 * ImGui.GetStyle().FramePadding.X));
        ImGui.SetNextItemWidth(AethertekUI.MaterialLayout.FitNextItemWidth(400, minimumWidth));
        if (ImGui.BeginCombo($"##{label}Select", displayText))
        {
            ImGui.SetNextItemWidth(380);
            UiGui.InputText($"Search##{label}", ref search, 128);

            ImGui.Separator();

            var maxResults = 20;
            var shown = 0;

            if (!string.IsNullOrWhiteSpace(search) && search.Length >= 2)
            {
                var searchLower = search.ToLowerInvariant();
                var isNumeric = uint.TryParse(search, out _);

                for (var i = 0; i < items.Count && shown < maxResults; i++)
                {
                    var item = items[i];
                    var match = isNumeric
                        ? item.Id.ToString().Contains(search, StringComparison.Ordinal)
                        : item.Name.ToLowerInvariant().Contains(searchLower);

                    if (!match) continue;
                    shown++;

                    var isSelected = (int)item.Id == selectedId;
                    if (UiGui.Selectable($"{item.Name} ({item.Id})##{label}{i}", isSelected, $"{item.Name} ({item.Id})"))
                    {
                        selectedId = (int)item.Id;
                        selectedName = item.Name;
                        changed = true;
                    }
                }

                if (shown == 0)
                    UiGui.TextDisabled("No results. Try a different search term.");
            }
            else
            {
                UiGui.TextDisabled("Type at least 2 characters to search...");
            }

            ImGui.EndCombo();
        }

        return changed;
    }

    private void DrawRepairSection(CharacterConfig config)
    {
        var repairMode = Math.Clamp(config.Repair, 0, RepairModes.Length - 1);
        ImGui.SetNextItemWidth(200);
        if (UiGui.Combo("Repair Mode", ref repairMode, RepairModes, RepairModes.Length))
        {
            config.Repair = repairMode;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("0 = disabled.\n1 = ADS self repair.\n2 = ADS NPC repair without inn fallback.\n3 = ADS NPC repair only when a mender is within 120y.\n4 = ADS NPC repair near an inn, then enter the inn room.");
        DrawDefaultSettingSyncButton("Repair Mode");

        var tornClothes = Math.Clamp(config.TornClothes, 0, 100);
        ImGui.SetNextItemWidth(200);
        if (UiGui.InputInt("Repair At % Durability", ref tornClothes))
        {
            config.TornClothes = Math.Clamp(tornClothes, 0, 100);
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Trigger repair when gear durability falls below this percentage.");
        DrawDefaultSettingSyncButton("Repair At % Durability");
    }

    private void DrawDesynthesisSection(CharacterConfig config)
    {
        var enabled = config.EnableAutoDesynth;
        if (UiGui.Checkbox("Enable Auto Desynth", ref enabled))
        {
            config.EnableAutoDesynth = enabled;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("After DutyCompleted and duty exit, ask ADS to run configured desynthesis. FrenRider pauses automatic actions until completion or the 180s timeout.");
        DrawDefaultSettingSyncButton("Enable Auto Desynth");

        if (UiGui.Button("OPEN DESYNTH CONFIG"))
        {
            if (!plugin.AdsUtilityIpcService.OpenDesynthConfig(out var failure))
                plugin.ReportToChatAndLog($"Could not open ADS desynthesis config: {failure}", isError: true);
        }

        if (!string.IsNullOrWhiteSpace(plugin.AutomationService.AutoDesynthStatus))
        {
            ImGui.SameLine();
            UiGui.TextWrapped(plugin.AutomationService.AutoDesynthStatus);
        }
    }

    private void DrawIdleBehaviorSection(CharacterConfig config)
    {
        var idleMode = config.IdleActionMode;
        ImGui.SetNextItemWidth(200);
        if (UiGui.Combo("Idle Mode", ref idleMode, IdleActionModes, IdleActionModes.Length))
        {
            config.IdleActionMode = idleMode;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("What to do when idle.\nSpecific Action: execute one command\nAction From List: rotate through a list");
        DrawDefaultSettingSyncButton("Idle Mode");

        if (config.IdleActionMode == 0)
        {
            var idleAction = config.IdleAction;
            ImGui.SetNextItemWidth(300);
            if (UiGui.InputText("Idle Command", ref idleAction, 64))
            {
                config.IdleAction = idleAction;
                configManager.SaveCurrentAccount();
            }
            ImGui.SameLine();
            HelpMarker("Slash command to execute when idle.\nExamples: /tomescroll, /dance, /snd run scriptname");
            DrawDefaultSettingSyncButton("Idle Command");
        }
        else
        {
            var listMode = config.IdleListMode;
            ImGui.SetNextItemWidth(200);
            if (UiGui.Combo("List Source", ref listMode, IdleListModes, IdleListModes.Length))
            {
                config.IdleListMode = listMode;
                configManager.SaveCurrentAccount();
            }
            ImGui.SameLine();
            HelpMarker("Default List: built-in emotes\nCustom List: your own command list");
            DrawDefaultSettingSyncButton("List Source");

            if (config.IdleListMode == 1)
            {
                DrawCustomIdleListEditor(config);
                DrawDefaultSettingSyncButton("Custom Idle List");
            }
        }

        var idleTicks = config.IdleTicksBeforeAction;
        ImGui.SetNextItemWidth(200);
        if (UiGui.InputInt("Idle Ticks Before Action", ref idleTicks))
        {
            config.IdleTicksBeforeAction = idleTicks;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Number of update ticks before idle action triggers.");
        DrawDefaultSettingSyncButton("Idle Ticks Before Action");
    }

    private void DrawAutoDiscardSection(CharacterConfig config)
    {
        var autoDiscard = config.EnableAutoDiscard;
        if (UiGui.Checkbox("Auto Discard (/ays discard)", ref autoDiscard))
        {
            config.EnableAutoDiscard = autoDiscard;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Runs /ays discard while mounted and in a safe idle window.\nRequires AutoRetainer plugin.");
        DrawDefaultSettingSyncButton("Auto Discard");
    }

    private void DrawAutorotSection(CharacterConfig config)
    {
        UiGui.TextDisabled("FrenRider installs BossMod presets whenever it is enabled.");

        if (UiGui.Button("Push Presets Now"))
            plugin.CombatService.ApplyPresetSelection("manual preset push");
        ImGui.SameLine();
        UiGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), plugin.AutorotIpcService.LastStatus);
    }

    private void DrawDebugLoggingSection(CharacterConfig config)
    {
        var spamPrinter = config.SpamPrinter;
        ImGui.SetNextItemWidth(200);
        if (UiGui.Combo("Echo Messages", ref spamPrinter, OnOff, OnOff.Length))
        {
            config.SpamPrinter = spamPrinter;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Print status messages to game chat.\nUseful for debugging but fills chat quickly.");
        DrawDefaultSettingSyncButton("Echo Messages");

        if (config.DebugMode)
        {
            ImGui.Spacing();
            if (UiGui.Button("Test ADS NPC No-Inn Repair"))
                TestAdsNpcNoInnRepair();
        }
    }

    private void TestAdsNpcNoInnRepair()
    {
        if (!plugin.AdsUtilityIpcService.CheckAvailability())
        {
            plugin.ReportToChatAndLog("FrenRider repair test failed: ADS not loaded.", isError: true);
            return;
        }

        if (!GameHelpers.IsInSanctuary())
        {
            plugin.ReportToChatAndLog("FrenRider repair test failed: NPC no-inn repair requires sanctuary.", isError: true);
            return;
        }

        if (plugin.AdsUtilityIpcService.StartRepair("npc-no-inn", out var failure))
        {
            plugin.ReportToChatAndLog("FrenRider repair test accepted: ADS NPC no-inn repair requested.");
            return;
        }

        var detail = string.IsNullOrWhiteSpace(failure)
            ? "ADS did not return a failure reason."
            : failure;
        plugin.ReportToChatAndLog($"FrenRider repair test failed: ADS rejected NPC no-inn repair: {detail}", isError: true);
    }

    private void DrawInviteWhitelistSection(CharacterConfig config)
    {
        UiGui.Text("Trusted inviters");
        HelpMarker("Players in this list will have party invites automatically accepted when you are not in a group.\nNames should omit the @Server part.");
        DrawDefaultSettingSyncButton("Invite Whitelist");
        ImGui.Spacing();

        for (int i = 0; i < config.InviteWhitelist.Count; i++)
        {
            var entry = config.InviteWhitelist[i];
            UiGui.Text(UiText.F($"  {Disp(entry)}"));
            ImGui.SameLine();
            if (UiGui.SmallButton($"X##wlDuty{i}"))
            {
                config.InviteWhitelist.RemoveAt(i);
                configManager.SaveCurrentAccount();
                break;
            }
        }

        ImGui.SetNextItemWidth(220);
        if (UiGui.InputText("##WhitelistAddDuty", ref whitelistInput, 64, ImGuiInputTextFlags.EnterReturnsTrue))
            AddWhitelistEntry(config);

        ImGui.SameLine();
        if (UiGui.SmallButton("Add##WhitelistDuty"))
            AddWhitelistEntry(config);
    }

    private void DrawAutoYesSection(CharacterConfig config)
    {
        UiGui.TextWrapped("Raise offers are accepted automatically while FrenRider is enabled.");

        var teleportOffer = config.TeleportOfferAutoAccept;
        if (UiGui.Checkbox("Teleport offers", ref teleportOffer))
        {
            config.TeleportOfferAutoAccept = teleportOffer;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Automatically accept teleport offers.");
        DrawDefaultSettingSyncButton("Teleport offers");

        var partyInvite = config.PartyInviteAutoAccept;
        if (UiGui.Checkbox("Party invites (backup)", ref partyInvite))
        {
            config.PartyInviteAutoAccept = partyInvite;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Backup auto-accept for party invites. Primary invite handling uses the whitelist.");
        DrawDefaultSettingSyncButton("Party invites");
    }

    private void DrawExitBehaviourSection(CharacterConfig config)
    {
        var controlled = configManager.IsQuestionableDutyFamilyControlled(config, AdsDutyCategory.Solo);
        if (!controlled && config.NormalizeExitMethodSelection())
            configManager.SaveCurrentAccount();
        var exits = configManager.GetEffectiveDutyExitSettings(config);

        if (controlled)
            UiGui.TextDisabled("Temporarily controlled by DAD");
        ImGui.BeginDisabled(controlled);

        if (UiGui.RadioButton("FrenRider Exit method", !exits.UseAdsLeaveAfterAdsDuty))
        {
            config.UseAdsLeaveAfterAdsDuty = false;
            if (!config.ExitAfterDutyEnds && !config.LeaveWhenAllLeft)
                config.ExitAfterDutyEnds = true;
            config.NormalizeExitMethodSelection();
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Use FrenRider's local Leave Duty flow after the configured duty-end condition.");
        DrawDefaultSettingSyncButton("Exit Method", "FrenRiderExitMethod");

        if (!exits.UseAdsLeaveAfterAdsDuty)
        {
            ImGui.Indent();
            DrawFrenRiderExitMethodOptions(config);
            ImGui.Unindent();
        }

        ImGui.Spacing();
        if (UiGui.RadioButton("ADS Exit Method", exits.UseAdsLeaveAfterAdsDuty))
        {
            config.UseAdsLeaveAfterAdsDuty = true;
            config.ExitAfterDutyEnds = false;
            config.LeaveWhenAllLeft = false;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Send /ads leave after the configured duty-end delay. FrenRider does not also run its own Leave Duty flow.");
        DrawDefaultSettingSyncButton("ADS Exit Method", "AdsExitMethod");

        ImGui.Spacing();
        UiGui.Text("Duty-end delay");
        ImGui.SameLine();
        var exitSeconds = exits.ExitAfterDutySeconds;
        ImGui.SetNextItemWidth(70);
        if (UiGui.InputInt("##exitSecondsDuty", ref exitSeconds))
        {
            config.ExitAfterDutySeconds = Math.Max(1, exitSeconds);
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        UiGui.Text("seconds after duty ends");
        DrawDefaultSettingSyncButton("Duty-end delay");
        ImGui.EndDisabled();
    }

    private void DrawFrenRiderExitMethodOptions(CharacterConfig config)
    {
        var method = config.ExitAfterDutyEnds
            ? 0
            : config.LeaveWhenAllLeft
                ? 1
                : 2;

        if (UiGui.RadioButton("Exit after N seconds", method == 0))
        {
            config.UseAdsLeaveAfterAdsDuty = false;
            config.ExitAfterDutyEnds = true;
            config.LeaveWhenAllLeft = false;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Automatically leave the duty N seconds after it completes.");
        DrawDefaultSettingSyncButton("Exit Method", "ExitAfterSeconds");

        if (UiGui.RadioButton("Leave when all others left", method == 1))
        {
            config.UseAdsLeaveAfterAdsDuty = false;
            config.ExitAfterDutyEnds = false;
            config.LeaveWhenAllLeft = true;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Leave the duty if no other party members are visible in the zone.");
        DrawDefaultSettingSyncButton("Exit Method", "LeaveWhenAllLeft");

        if (UiGui.RadioButton("No automatic exit", method == 2))
        {
            config.UseAdsLeaveAfterAdsDuty = false;
            config.ExitAfterDutyEnds = false;
            config.LeaveWhenAllLeft = false;
            configManager.SaveCurrentAccount();
        }
        DrawDefaultSettingSyncButton("Exit Method", "NoAutomaticExit");
    }

    private void DrawUiSettingsSection()
    {
        var videoNotificationsEnabled = configuration.VideoNotificationsEnabled;
        if (UiGui.Checkbox("Video Notifications", ref videoNotificationsEnabled))
        {
            configuration.VideoNotificationsEnabled = videoNotificationsEnabled;
            configuration.Save();
        }
        ImGui.SameLine();
        HelpMarker("Play videos when Fren Rider is enabled or disabled.\nRequires VLC media player.");

        if (!plugin.VideoPlaybackService.IsVLCAvailable())
        {
            UiGui.TextColored(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), "VLC not found");
            ImGui.SameLine();
            if (UiGui.SmallButton("Download VLC"))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.videolan.org/vlc/",
                    UseShellExecute = true
                });
            }
        }

        var movable = configuration.IsConfigWindowMovable;
        if (UiGui.Checkbox("Movable Config Window", ref movable))
        {
            configuration.IsConfigWindowMovable = movable;
            configuration.Save();
        }

        var dtrEnabled = configuration.DtrBarEnabled;
        if (UiGui.Checkbox("DTR Bar Enabled", ref dtrEnabled))
        {
            configuration.DtrBarEnabled = dtrEnabled;
            configuration.Save();
        }
        ImGui.SameLine();
        HelpMarker("Show or hide the DTR bar entry.");

        var dtrMode = configuration.DtrBarMode;
        var dtrModes = new[] { "Text Only", "Icon+Text", "Icon Only" };
        ImGui.SetNextItemWidth(150);
        if (UiGui.Combo("DTR Bar Mode", ref dtrMode, dtrModes, dtrModes.Length))
        {
            configuration.DtrBarMode = dtrMode;
            configuration.Save();
        }

        ImGui.Spacing();
        UiGui.Text("DTR Icons (max 3 characters)");
        ImGui.SameLine();
        if (UiGui.SmallButton("Copy Icon Guide Link"))
        {
            ImGui.SetClipboardText(IconGuideUrl);
            Plugin.Log.Info("Copied icon guide link to clipboard");
        }

        var enabledIcon = configuration.DtrIconEnabled;
        if (DrawIconInputs("Enabled", ref enabledIcon, "\uE03C"))
        {
            configuration.DtrIconEnabled = enabledIcon;
            configuration.Save();
        }

        var disabledIcon = configuration.DtrIconDisabled;
        if (DrawIconInputs("Disabled", ref disabledIcon, "\uE03D"))
        {
            configuration.DtrIconDisabled = disabledIcon;
            configuration.Save();
        }
    }

    private void AddWhitelistEntry(CharacterConfig config)
    {
        var trimmed = whitelistInput.Trim();
        if (!string.IsNullOrEmpty(trimmed) && !config.InviteWhitelist.Contains(trimmed))
        {
            config.InviteWhitelist.Add(ConfigManager.FixNameCapitalization(trimmed));
            configManager.SaveCurrentAccount();
        }
        whitelistInput = "";
    }

    private void DrawMiscTab(CharacterConfig config)
    {
        ImGui.Spacing();

        // --- Loot ---
        UiGui.Text("Loot");
        ImGui.Spacing();

        var fulfIdx = Array.IndexOf(LootTypes, config.FulfType);
        if (fulfIdx < 0) fulfIdx = 0;
        ImGui.SetNextItemWidth(200);
        if (UiGui.Combo("Loot Type", ref fulfIdx, LootTypes, LootTypes.Length))
        {
            config.FulfType = LootTypes[fulfIdx];
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("How loot is handled if LazyLoot is installed.\n'unchanged' = Don't modify loot settings.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // --- Food ---
        UiGui.Text("Food");
        ImGui.Spacing();

        var feedMeItem = config.FeedMeItem;
        ImGui.SetNextItemWidth(300);
        if (UiGui.InputText("Food Item Name", ref feedMeItem, 64))
        {
            config.FeedMeItem = feedMeItem;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Name of food to auto-consume.\nFull item search from game data planned for a future update.");

        var feedMeSearch = config.FeedMeSearch;
        if (UiGui.Checkbox("Search for Food if Depleted", ref feedMeSearch))
        {
            config.FeedMeSearch = feedMeSearch;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("If your configured food runs out, search inventory for any food starting from lowest item ID.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // --- Repair ---
        UiGui.Text("Repair");
        ImGui.Spacing();

        DrawRepairSection(config);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        UiGui.Text("Desynthesis");
        ImGui.Spacing();
        DrawDesynthesisSection(config);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // --- Idle Behavior ---
        UiGui.Text("Idle Behavior");
        ImGui.Spacing();

        var idleMode = config.IdleActionMode;
        ImGui.SetNextItemWidth(200);
        if (UiGui.Combo("Idle Mode", ref idleMode, IdleActionModes, IdleActionModes.Length))
        {
            config.IdleActionMode = idleMode;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("What to do when idle.\nSpecific Action: Execute a single command\nAction From List: Pick randomly from a list");

        if (config.IdleActionMode == 0)
        {
            // Specific action
            var idleAction = config.IdleAction;
            ImGui.SetNextItemWidth(300);
            if (UiGui.InputText("Idle Command", ref idleAction, 64))
            {
                config.IdleAction = idleAction;
                configManager.SaveCurrentAccount();
            }
            ImGui.SameLine();
            HelpMarker("Slash command to execute when idle.\nExamples: /tomescroll, /dance, /snd run scriptname");
        }
        else
        {
            // Action from list
            var listMode = config.IdleListMode;
            ImGui.SetNextItemWidth(200);
            if (UiGui.Combo("List Source", ref listMode, IdleListModes, IdleListModes.Length))
            {
                config.IdleListMode = listMode;
                configManager.SaveCurrentAccount();
            }
            ImGui.SameLine();
            HelpMarker("Default List: Built-in emote list\nCustom List: Your own list of commands");

            if (config.IdleListMode == 1)
            {
                DrawCustomIdleListEditor(config);
            }
        }

        var idleTicks = config.IdleTicksBeforeAction;
        ImGui.SetNextItemWidth(200);
        if (UiGui.InputInt("Idle Ticks Before Action", ref idleTicks))
        {
            config.IdleTicksBeforeAction = idleTicks;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Number of update ticks before idle action triggers.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // --- Debug ---
        UiGui.Text("Debug / Logging");
        ImGui.Spacing();

        DrawDebugLoggingSection(config);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // --- UI Settings ---
        UiGui.Text("UI Settings");
        ImGui.Spacing();

        var videoNotificationsEnabled = configuration.VideoNotificationsEnabled;
        if (UiGui.Checkbox("Video Notifications", ref videoNotificationsEnabled))
        {
            configuration.VideoNotificationsEnabled = videoNotificationsEnabled;
            configuration.Save();
        }
        ImGui.SameLine();
        HelpMarker("Play videos when Fren Rider is enabled/disabled.\nRequires VLC media player to be installed.\nVideos are embedded with the plugin distribution.");
        
        // VLC availability warning
        if (!plugin.VideoPlaybackService.IsVLCAvailable())
        {
            ImGui.Spacing();
            UiGui.TextColored(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), "⚠ VLC Not Found");
            ImGui.SameLine();
            
            // Make VLC text clickable
            var vlcColor = new Vector4(0.4f, 0.8f, 1.0f, 1.0f); // Light blue
            UiGui.TextColored(vlcColor, "VLC");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                UiGui.SetTooltip("Click to download VLC media player");
            }
            if (ImGui.IsItemClicked())
            {
                Process.Start(new ProcessStartInfo 
                { 
                    FileName = "https://www.videolan.org/vlc/", 
                    UseShellExecute = true 
                });
            }
            ImGui.SameLine();
            HelpMarker("VLC media player is required for video notifications.\nClick to download and install VLC.");
        }

        var movable = configuration.IsConfigWindowMovable;
        if (UiGui.Checkbox("Movable Config Window", ref movable))
        {
            configuration.IsConfigWindowMovable = movable;
            configuration.Save();
        }

        var dtrEnabled = configuration.DtrBarEnabled;
        if (UiGui.Checkbox("DTR Bar Enabled", ref dtrEnabled))
        {
            configuration.DtrBarEnabled = dtrEnabled;
            configuration.Save();
        }
        ImGui.SameLine();
        HelpMarker("Show/hide the DTR bar entry (server info bar).");

        var dtrMode = configuration.DtrBarMode;
        var dtrModes = new[] { "Text Only", "Icon+Text", "Icon Only" };
        ImGui.SetNextItemWidth(150);
        if (UiGui.Combo("DTR Bar Mode", ref dtrMode, dtrModes, dtrModes.Length))
        {
            configuration.DtrBarMode = dtrMode;
            configuration.Save();
        }
        ImGui.SameLine();
        HelpMarker("DTR bar display mode:\nText Only: 'FR: On/Off'\nIcon+Text: '⚫ FR'\nIcon Only: '⚫'");

        ImGui.Spacing();
        UiGui.Text("DTR Icons (max 3 characters)");
        ImGui.SameLine();
        HelpMarker("Customize the glyphs used for enabled/disabled icon modes.");
        ImGui.SameLine();
        if (UiGui.SmallButton("Copy Icon Guide Link"))
        {
            ImGui.SetClipboardText(IconGuideUrl);
            Plugin.Log.Info("Copied icon guide link to clipboard");
        }
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Copies the Lodestone blog link with suggested glyphs");

        var enabledIcon = configuration.DtrIconEnabled;
        if (DrawIconInputs("Enabled", ref enabledIcon, "\uE03C"))
        {
            configuration.DtrIconEnabled = enabledIcon;
            configuration.Save();
        }

        var disabledIcon = configuration.DtrIconDisabled;
        if (DrawIconInputs("Disabled", ref disabledIcon, "\uE03D"))
        {
            configuration.DtrIconDisabled = disabledIcon;
            configuration.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // --- Invite Whitelist ---
        UiGui.Text("Invite Whitelist");
        ImGui.SameLine();
        HelpMarker("Players in this list will have their party invites automatically accepted when you're not in a group.\nWhen you join a party via whitelist invite, the inviter will automatically be set as your Fren.\nEnter names without the @Server part.");
        ImGui.Spacing();

        for (int i = 0; i < config.InviteWhitelist.Count; i++)
        {
            var entry = config.InviteWhitelist[i];
            UiGui.Text(UiText.F($"  {Disp(entry)}"));
            ImGui.SameLine();
            if (UiGui.SmallButton($"X##wl{i}"))
            {
                config.InviteWhitelist.RemoveAt(i);
                configManager.SaveCurrentAccount();
                break;
            }
        }

        ImGui.SetNextItemWidth(200);
        if (UiGui.InputText("##WhitelistAdd", ref whitelistInput, 64, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            var trimmed = whitelistInput.Trim();
            if (!string.IsNullOrEmpty(trimmed) && !config.InviteWhitelist.Contains(trimmed))
            {
                config.InviteWhitelist.Add(ConfigManager.FixNameCapitalization(trimmed));
                configManager.SaveCurrentAccount();
            }
            whitelistInput = "";
        }
        ImGui.SameLine();
        if (UiGui.SmallButton("Add"))
        {
            var trimmed = whitelistInput.Trim();
            if (!string.IsNullOrEmpty(trimmed) && !config.InviteWhitelist.Contains(trimmed))
            {
                config.InviteWhitelist.Add(ConfigManager.FixNameCapitalization(trimmed));
                configManager.SaveCurrentAccount();
            }
            whitelistInput = "";
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // --- Auto-Yes Dialogs ---
        UiGui.Text("Auto-Yes Dialogs");
        ImGui.SameLine();
        HelpMarker("Automatically click Yes on specific dialog types when FrenRider is enabled.\nWorks alongside YesAlready - FrenRider pauses YesAlready and handles these dialogs itself.");
        ImGui.Spacing();

        // Teleport offers
        var teleportOffer = config.TeleportOfferAutoAccept;
        if (UiGui.Checkbox("Teleport offers", ref teleportOffer))
        {
            config.TeleportOfferAutoAccept = teleportOffer;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Automatically accept teleport offers (e.g., Return, Teleport to).\nNote: Party invites are handled by the whitelist system below.");

        // Party invites (backup to whitelist)
        var partyInvite = config.PartyInviteAutoAccept;
        if (UiGui.Checkbox("Party invites (backup)", ref partyInvite))
        {
            config.PartyInviteAutoAccept = partyInvite;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Backup auto-accept for party invites.\nPrimary invite handling uses the whitelist system above.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // --- Exit Behaviour ---
        UiGui.Text("Exit Behaviour");
        ImGui.Spacing();
        DrawExitBehaviourSection(config);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // --- Autorot IPC ---
        UiGui.Text("Autorot Presets");
        ImGui.Spacing();

        UiGui.TextDisabled("FrenRider installs BossMod presets whenever it is enabled.");

        if (UiGui.Button("Push Presets Now"))
        {
            plugin.CombatService.ApplyPresetSelection("manual preset push");
        }
        ImGui.SameLine();
        UiGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), plugin.AutorotIpcService.LastStatus);
    }

    private void DrawCustomIdleListEditor(CharacterConfig config)
    {
        if (config.EnsureCustomIdleListSeeded())
            configManager.SaveCurrentAccount();

        ImGui.Spacing();
        if (UiGui.SmallButton("[+]##IdleCustomAdd"))
        {
            var commands = CharacterConfig.CloneCustomIdleList(config.CustomIdleList)
                .Concat(new[] { CharacterConfig.DefaultCustomIdleCommand })
                .ToArray();
            config.CustomIdleList = commands;
            configManager.SaveCurrentAccount();
        }
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Add command");

        var list = CharacterConfig.CloneCustomIdleList(config.CustomIdleList);
        for (var i = 0; i < list.Length; i++)
        {
            ImGui.PushID($"IdleCustom{i}");

            var canRemove = list.Length > 1;
            if (!canRemove)
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);

            var removeClicked = UiGui.SmallButton("[-]") && canRemove;
            if (!canRemove)
                ImGui.PopStyleVar();
            if (ImGui.IsItemHovered())
                UiGui.SetTooltip(canRemove ? "Remove command" : "At least one command required");

            if (removeClicked)
            {
                config.CustomIdleList = list
                    .Where((_, index) => index != i)
                    .DefaultIfEmpty(CharacterConfig.DefaultCustomIdleCommand)
                    .ToArray();
                configManager.SaveCurrentAccount();
                ImGui.PopID();
                break;
            }

            ImGui.SameLine();
            var command = list[i] ?? "";
            ImGui.SetNextItemWidth(Math.Max(200f, ImGui.GetContentRegionAvail().X));
            if (UiGui.InputText("##IdleCustomCommand", ref command, 256))
            {
                list[i] = command;
                config.CustomIdleList = list;
                configManager.SaveCurrentAccount();
            }

            ImGui.PopID();
        }
    }

    private void DrawAboutTab()
    {
        supportLog.Draw(Plugin.PluginInterface, key => UiText.T(key),
            path => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true }), ex => Plugin.Log.Error(ex, "Dalamud log export failed."), Plugin.CommandManager);
        ImGui.Spacing();
        UiGui.TextColored(AethertekUI.MaterialTheme.Current.Colors.Primary, "Fren Rider");
        UiGui.Text("A Dalamud plugin for FFXIV multiplayer follow/combat automation.");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        UiGui.Text("Commands:");
        UiGui.BulletText("/frenrider - Open main window");
        UiGui.BulletText("/fr - Open main window (alias)");
        UiGui.BulletText("/fr on - Enable Fren Rider");
        UiGui.BulletText("/fr off - Disable Fren Rider");
        UiGui.BulletText("/fr settings or /fr s - Toggle settings");
        UiGui.BulletText("/fr mini or /fr m - Toggle MAGIA mini window");
        UiGui.BulletText("/fr debug - Toggle debug controls");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        UiGui.TextColored(new Vector4(1f, 0.8f, 0.4f, 1), "Required Dependencies:");
        UiGui.BulletText("vnavmesh - Navigation and pathfinding");
        ImGui.Spacing();

        UiGui.TextColored(new Vector4(0.6f, 1f, 0.6f, 1), "Optional Plugins:");
        UiGui.BulletText("Visland - Alternative navigation (if vnavmesh unavailable)");
        UiGui.BulletText("BossMod / BossModReborn - Combat AI and following");
        UiGui.BulletText("Rotation Solver Reborn - Combat rotation automation");
        UiGui.BulletText("WRATH - Combat rotation automation");
        UiGui.BulletText("Daedalus - Combat rotation automation");
        ImGui.Indent();
        const string daedalusRepoUrl = "https://raw.githubusercontent.com/ofnature/Daedalus/main/repo.json";
        UiGui.TextColored(new Vector4(0.3f, 0.7f, 1f, 1), daedalusRepoUrl);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            UiGui.SetTooltip("Click to copy Daedalus repository URL");
        }
        if (ImGui.IsItemClicked())
            ImGui.SetClipboardText(daedalusRepoUrl);
        ImGui.Unindent();
        UiGui.BulletText("Questionable - Quest automation integration");
        UiGui.BulletText("Automaton (CBT) by Croizat - Enhanced duty start/end, auto-leave");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        UiGui.Text("Multiplayer Guide:");
        ImGui.Spacing();
        var guideUrl = "https://github.com/McVaxius/dhogsbreakfeast/tree/main/Dungeons%20and%20Multiboxing/Multiplayer%20Guide";
        UiGui.TextColored(new Vector4(0.3f, 0.7f, 1f, 1), guideUrl);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            UiGui.SetTooltip("Click to copy URL to clipboard");
        }
        if (ImGui.IsItemClicked())
        {
            ImGui.SetClipboardText(guideUrl);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        UiGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "Made by McVaxius");
    }

    private void DrawRemoteProfileBanner(RemoteProfileRow remote)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.18f, 0.08f, 0.24f, 0.75f));
        if (ImGui.BeginChild("RemoteProfileIdentity", new Vector2(0, 190f), true))
        {
            UiGui.TextColored(new Vector4(0.95f, 0.65f, 1f, 1f), "REMOTE DAD PROFILE");
            UiGui.TextWrapped("This row is separate from local characters and can never become the active-character profile.");

            var displayLabel = remote.DisplayLabel;
            ImGui.SetNextItemWidth(320f);
            if (UiGui.InputText("Display label", ref displayLabel, 256))
            {
                remote.DisplayLabel = displayLabel;
                configManager.SaveCurrentAccount();
            }

            UiGui.TextDisabled(UiText.F($"Owner: {Disp(remote.OwnerId)}"));
            UiGui.TextDisabled(UiText.F($"Island: {Disp(remote.IslandId)}"));
            UiGui.TextDisabled(UiText.F($"Opaque character: {Disp(remote.CharacterId)}"));
            UiGui.TextDisabled(UiText.F($"Row ID: {remote.RowId}"));

            var enabled = remote.Config.Enabled;
            if (UiGui.Checkbox("Enabled when transferred", ref enabled))
            {
                remote.Config.Enabled = enabled;
                configManager.SaveCurrentAccount();
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    // --- Helpers ---

    /// <summary>Display a name, applying Krangle if enabled.</summary>
    private string Disp(string name)
    {
        return configuration.KrangleEnabled ? KrangleService.KrangleName(name) : name;
    }

    private string GetCurrentCharacterKey()
        => configManager.ActiveCharacterKey;

    private bool DrawIconInputs(string label, ref string value, string fallback)
    {
        var updated = false;
        var glyph = value;
        ImGui.SetNextItemWidth(80);
        if (UiGui.InputText($"{label} Icon", ref glyph, 8))
        {
            value = SanitizeIconInput(glyph, fallback);
            updated = true;
        }
        ImGui.SameLine();
        UiGui.TextDisabled(UiText.F($"Shown when Fren Rider is {label.ToLowerInvariant()}"));

        var code = FormatIconCode(value);
        ImGui.SetNextItemWidth(160);
        if (UiGui.InputText($"{label} Icon Code", ref code, 64))
        {
            var parsed = ParseIconCode(code, value);
            value = SanitizeIconInput(parsed, fallback);
            updated = true;
        }

        return updated;
    }

    private static string SanitizeIconInput(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var trimmed = value.Trim();
        return trimmed.Length > 3 ? trimmed.Substring(0, 3) : trimmed;
    }

    private static string FormatIconCode(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var sb = new StringBuilder();
        foreach (var rune in value.EnumerateRunes())
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append("\\u");
            sb.Append(rune.Value.ToString("X4", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static string ParseIconCode(string input, string fallback)
    {
        if (string.IsNullOrWhiteSpace(input))
            return fallback;

        var parts = input.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            if (sb.Length >= 3) break;

            var token = part.Trim();
            if (token.StartsWith("\\u", StringComparison.OrdinalIgnoreCase))
                token = token[2..];
            else if (token.StartsWith("u", StringComparison.OrdinalIgnoreCase))
                token = token[1..];
            else if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                token = token[2..];

            if (int.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codepoint))
            {
                sb.Append(char.ConvertFromUtf32(codepoint));
            }
        }

        return sb.Length == 0 ? fallback : sb.ToString();
    }

    private const string IconGuideUrl = "https://na.finalfantasyxiv.com/lodestone/character/22423564/blog/4393835";

    private void SyncFrenNameInput()
    {
        var config = GetEditingRemoteProfile()?.Config
                     ?? configManager.GetCurrentCharacterConfig(editingCharacterKey);
        frenNameInput = config.FrenName;
    }

    private RemoteProfileRow? GetEditingRemoteProfile()
        => string.IsNullOrWhiteSpace(editingRemoteRowId)
            ? null
            : configManager.GetRemoteProfile(editingRemoteRowId);

    private static string GetRemoteDisplayLabel(RemoteProfileRow remote)
        => string.IsNullOrWhiteSpace(remote.DisplayLabel)
            ? remote.CharacterId
            : remote.DisplayLabel;

    private void SyncEditingSelectionWithActiveCharacter()
    {
        var activeAccountId = configManager.CurrentAccountId;
        var activeCharacterKey = configManager.ActiveCharacterKey;
        if (string.Equals(observedActiveAccountId, activeAccountId, StringComparison.Ordinal)
            && string.Equals(observedActiveCharacterKey, activeCharacterKey, StringComparison.Ordinal))
        {
            return;
        }

        observedActiveAccountId = activeAccountId;
        observedActiveCharacterKey = activeCharacterKey;
        editingCharacterKey = activeCharacterKey;
        editingRemoteRowId = "";
        SyncFrenNameInput();
    }

    private bool IsDefaultConfigSelected()
        => string.IsNullOrEmpty(editingRemoteRowId) && string.IsNullOrEmpty(editingCharacterKey);

    private string GetCurrentTabDisplayName()
        => currentTab == "Duty" ? "Duty / ADS / Exit" : currentTab;

    private void DrawDefaultSettingSyncButton(string label, string? idSuffix = null)
    {
        if (!IsDefaultConfigSelected())
            return;

        ImGui.SameLine();
        var buttonId = idSuffix ?? label;
        if (UiGui.SmallButton($"Sync all##DefaultSync{buttonId}"))
            ReportDefaultSync(label, configManager.ApplyDefaultSettingToAllCharacters(label));

        if (ImGui.IsItemHovered())
            UiGui.SetTooltip(UiText.F("Copy '{0}' from DEFAULT CONFIG to every character profile in this account.", UiText.T(label)));
    }

    private void DrawAllFrenRiderButton(bool enabled)
    {
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, enabled
            ? new Vector4(0.1f, 0.45f, 0.15f, 1f)
            : new Vector4(0.6f, 0.1f, 0.1f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, enabled
            ? new Vector4(0.15f, 0.6f, 0.2f, 1f)
            : new Vector4(0.8f, 0.15f, 0.15f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, enabled
            ? new Vector4(0.1f, 0.35f, 0.1f, 1f)
            : new Vector4(0.45f, 0.05f, 0.05f, 1f));
        if (UiGui.SmallButton(enabled ? "All FR on" : "All FR off"))
            ReportDefaultSync(enabled ? "All FR on" : "All FR off", configManager.SetAllFrenRiderEnabled(enabled));
        ImGui.PopStyleColor(3);
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Set DEFAULT CONFIG and every local character in this account, including the active temporary profile.");
    }

    private void ReportDefaultSync(string scope, int count)
    {
        if (count <= 0)
        {
            plugin.ReportToChatAndLog($"FrenRider DEFAULT sync found no character profiles for {scope}.");
            return;
        }

        var profileWord = count == 1 ? "profile" : "profiles";
        plugin.ReportToChatAndLog($"FrenRider DEFAULT synced {scope} to {count} character {profileWord}.");
    }

    private static void HelpMarker(string desc)
    {
        ImGui.SameLine();
        UiGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 20.0f);
            UiGui.TextUnformatted(desc);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }
}
