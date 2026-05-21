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
    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private readonly ConfigManager configManager;

    private string currentTab = "Profile";
    private string accountAliasEdit = "";
    private string frenNameInput = "";
    private bool frenNameFocused = false;
    private string mountSearch = "";
    private string foodSearch = "";
    private bool isDraggingSplitter = false;
    private string whitelistInput = "";
    private readonly List<(uint Id, string Name)> foodItems = new();
    private bool foodItemsLoaded = false;

    private static readonly string[] CompanionStances = { "Free Stance", "Defender Stance", "Attacker Stance", "Healer Stance", "Follow" };
    private static readonly string[] ClingTypes = { "NavMesh", "Visland", "BossMod Follow", "Vanilla Follow" };
    private static readonly string[] RotationPlugins = { "BMR", "VBM", "RSR", "WRATH" };
    private static readonly string[] RotationTypes = { "Auto", "Manual", "none", "Auto (Support)", "Previously Engaged Targets" };
    private static readonly string[] BossModAIOptions = { "on", "off" };
    private static readonly string[] Positionals = { "Front", "Rear", "Any", "Auto" };
    private static readonly string[] FollowInCombatOptions = { "No", "Yes", "Auto" };
    private static readonly string[] AdsMaturityOptions = { "0 - Not Cleared", "1 - 1P Unsync Cleared", "2 - 1P Duty Support", "3 - 4P Sync Cleared" };
    private static readonly string[] AdsPresetOptions = { "No preset push (stub)", "Pilot Default (stub)", "Treasure (stub)" };
    private static readonly string[] LootTypes = { "unchanged", "need", "greed", "pass" };
    private static readonly string[] OnOff = { "Off", "On" };
    private static readonly string[] IdleActionModes = { "Specific Action", "Action From List" };
    private static readonly string[] IdleListModes = { "Default List", "Custom List" };

    public ConfigWindow(Plugin plugin) : base("Fren Rider Settings###FrenRiderConfig")
    {
        Flags = ImGuiWindowFlags.None;
        Size = new Vector2(900, 550);
        SizeCondition = ImGuiCond.FirstUseEver;

        this.plugin = plugin;
        this.configuration = plugin.Configuration;
        this.configManager = plugin.ConfigManager;
    }

    public void Dispose() { }

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
        if (configuration.IsConfigWindowMovable)
            Flags &= ~ImGuiWindowFlags.NoMove;
        else
            Flags |= ImGuiWindowFlags.NoMove;

        // Update window title based on selected character (krangled if enabled)
        var sel = configManager.SelectedCharacterKey;
        var displaySel = string.IsNullOrEmpty(sel) ? "DEFAULT CONFIG" : Disp(sel);
        WindowName = $"Fren Rider Settings - {displaySel}###FrenRiderConfig";
    }

    public override void Draw()
    {
        var config = configManager.GetActiveConfig();
        if (config == null) return;

        var panelWidth = configuration.LeftPanelWidth;

        // Left panel (user-resizable)
        ImGui.BeginChild("LeftPanel", new Vector2(panelWidth, 0), true);
        DrawLeftPanel();
        ImGui.EndChild();

        ImGui.SameLine();

        // Splitter handle (vertical drag bar)
        var cursorPos = ImGui.GetCursorScreenPos();
        var splitterHeight = ImGui.GetContentRegionAvail().Y;
        ImGui.InvisibleButton("##Splitter", new Vector2(6, splitterHeight));
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
            ? ImGui.GetColorU32(new Vector4(0.6f, 0.6f, 0.9f, 1f))
            : ImGui.GetColorU32(new Vector4(0.4f, 0.4f, 0.4f, 1f));
        drawList.AddLine(new Vector2(cursorPos.X + 2, cursorPos.Y), new Vector2(cursorPos.X + 2, cursorPos.Y + splitterHeight), lineColor, 2f);

        ImGui.SameLine();

        // Right panel
        ImGui.BeginChild("RightPanel", Vector2.Zero, false);
        DrawRightPanel(config);
        ImGui.EndChild();
    }

    private void DrawLeftPanel()
    {
        var account = configManager.GetCurrentAccount();
        if (account == null)
        {
            ImGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1), "No account loaded.");
            ImGui.TextWrapped("Log in to a character to create one.");
            return;
        }

        // Account alias (editable)
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 1f, 1), "ACCOUNT");
        if (accountAliasEdit != account.AccountAlias)
            accountAliasEdit = account.AccountAlias;

        if (configuration.KrangleEnabled)
        {
            var krangledAlias = Disp(accountAliasEdit);
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##AccountAliasKrangled", ref krangledAlias, 64, ImGuiInputTextFlags.ReadOnly);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Disable Krangle to edit the account alias.");
        }
        else
        {
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##AccountAlias", ref accountAliasEdit, 64))
            {
                configManager.UpdateAccountAlias(accountAliasEdit);
            }
        }
        HelpMarker("Human-readable alias for this account group. Linked to account ID internally.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // DEFAULT CONFIG
        var isDefault = string.IsNullOrEmpty(configManager.SelectedCharacterKey);
        if (ImGui.Selectable("DEFAULT CONFIG", isDefault))
        {
            configManager.SelectedCharacterKey = "";
            SyncFrenNameInput();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Current character (green highlight, with spacing)
        var currentCharKey = GetCurrentCharacterKey();
        if (!string.IsNullOrEmpty(currentCharKey))
        {
            var isCurrent = configManager.SelectedCharacterKey == currentCharKey;
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 1f, 0.4f, 1));
            if (ImGui.Selectable(Disp(currentCharKey), isCurrent))
            {
                configManager.SelectedCharacterKey = currentCharKey;
                SyncFrenNameInput();
            }
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

        // Other characters sorted alphabetically (with spacing between)
        foreach (var charKey in configManager.GetSortedCharacterKeys())
        {
            if (charKey == currentCharKey) continue;
            var isSelected = configManager.SelectedCharacterKey == charKey;
            if (ImGui.Selectable(Disp(charKey), isSelected))
            {
                configManager.SelectedCharacterKey = charKey;
                SyncFrenNameInput();
            }
            ImGui.Spacing();
        }
    }

    private void DrawRightPanel(CharacterConfig config)
    {
        // --- Top bar: Krangle | Reset All (?) | Reset This (?) ---
        var krangleEnabled = configuration.KrangleEnabled;
        if (ImGui.Checkbox("Krangle", ref krangleEnabled))
        {
            configuration.KrangleEnabled = krangleEnabled;
            configuration.Save();
            KrangleService.ClearCache();
        }
        HelpMarker("Garble all identifying text (character names, fren names, servers)\nwith military/exercise words. Useful for taking screenshots\nto report issues without revealing personal info.");

        // Right-align the buttons
        var avail = ImGui.GetContentRegionAvail().X;
        var buttonGroupWidth = 340f;
        ImGui.SameLine(ImGui.GetCursorPosX() + avail - buttonGroupWidth);

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 1));
        if (ImGui.Button("Reset All"))
        {
            configManager.ResetCharacterToDefault(configManager.SelectedCharacterKey);
            SyncFrenNameInput();
        }
        ImGui.PopStyleColor();
        HelpMarker("Reset ALL tabs for this character to default values.\nIf editing DEFAULT CONFIG, resets to plugin defaults.");

        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.4f, 0.2f, 1));
        if (ImGui.Button("Reset This"))
        {
            configManager.ResetCharacterTabToDefault(configManager.SelectedCharacterKey, currentTab);
            SyncFrenNameInput();
        }
        ImGui.PopStyleColor();
        HelpMarker("Reset only the current tab for this character to default values.");

        // DELETE button (only for non-default characters, requires CTRL)
        if (!string.IsNullOrEmpty(configManager.SelectedCharacterKey))
        {
            ImGui.SameLine();
            var io = ImGui.GetIO();
            var ctrlHeld = io.KeyCtrl;
            if (!ctrlHeld) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.7f, 0.1f, 0.1f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.2f, 0.2f, 1));
            if (ImGui.Button("DELETE") && ctrlHeld)
            {
                configManager.DeleteCharacter(configManager.SelectedCharacterKey);
            }
            ImGui.PopStyleColor(2);
            if (!ctrlHeld) ImGui.PopStyleVar();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Hold CTRL and click to delete this character's config.\nThis cannot be undone.");
        }

        ImGui.Spacing();

        if (ImGui.BeginTabBar("FrenRiderTabs"))
        {
            if (ImGui.BeginTabItem("Profile"))
            {
                currentTab = "Profile";
                DrawPartyTab(config);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Follow"))
            {
                currentTab = "Follow";
                DrawDistanceTab(config);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Combat"))
            {
                currentTab = "Combat";
                DrawCombatTab(config);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Duty / ADS / Exit"))
            {
                currentTab = "Duty";
                DrawDutyAdsExitTab(config);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Automation"))
            {
                currentTab = "Automation";
                DrawAutomationTab(config);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("UI / About"))
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

        // Fren Name with party dropdown and capitalization fix
        ImGui.Text("Fren Name");
        ImGui.SameLine();
        HelpMarker("Name of the party member to follow. Can be partial if unique.\nThe @Server part is cosmetic for display; targeting uses the name before @.\nNames are auto-capitalized. Select from party or type manually.");

        if (configuration.KrangleEnabled)
        {
            // Krangled: show read-only garbled name
            var krangled = Disp(config.FrenName);
            ImGui.SetNextItemWidth(300);
            ImGui.InputText("##FrenNameKrangled", ref krangled, 64, ImGuiInputTextFlags.ReadOnly);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Disable Krangle to edit fren name.");
        }
        else
        {
            if (frenNameInput != config.FrenName && !frenNameFocused)
                frenNameInput = config.FrenName;
            ImGui.SetNextItemWidth(300);
            frenNameFocused = false;
            if (ImGui.InputText("##FrenName", ref frenNameInput, 64))
            {
                frenNameFocused = true;
            }
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                config.FrenName = ConfigManager.FixNameCapitalization(frenNameInput);
                frenNameInput = config.FrenName;
                configManager.SaveCurrentAccount();
            }

            // Party member quick-select dropdown
            ImGui.SameLine();
            if (ImGui.BeginCombo("##PartySelect", "", ImGuiComboFlags.NoPreview | ImGuiComboFlags.PopupAlignLeft))
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
                        if (ImGui.Selectable(display))
                        {
                            config.FrenName = display;
                            frenNameInput = display;
                            configManager.SaveCurrentAccount();
                        }
                    }
                }
                else
                {
                    ImGui.TextDisabled("Not in a party");
                }
                ImGui.EndCombo();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Select from current party members");

            // Add to Whitelist button
            ImGui.SameLine();
            var currentFren = config.FrenName;
            var frenBase = currentFren.Split('@')[0].Trim();
            var canAddWl = !string.IsNullOrEmpty(frenBase) && !config.InviteWhitelist.Contains(frenBase);
            if (!canAddWl) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
            if (ImGui.SmallButton("WL+"))
            {
                if (canAddWl)
                {
                    config.InviteWhitelist.Add(ConfigManager.FixNameCapitalization(frenBase));
                    configManager.SaveCurrentAccount();
                }
            }
            if (!canAddWl) ImGui.PopStyleVar();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(canAddWl
                    ? $"Add '{frenBase}' to Invite Whitelist"
                    : string.IsNullOrEmpty(frenBase) ? "No fren name set" : $"'{frenBase}' already in whitelist");
        }

        ImGui.Spacing();

        // Fly You Fools
        var flyYouFools = config.FlyYouFools;
        if (ImGui.Checkbox("Fly You Fools (fly alongside instead of pillion)", ref flyYouFools))
        {
            config.FlyYouFools = flyYouFools;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("If enabled, you will summon your own mount instead of pillion riding.\nUseful for flying zones.\n\n⚠️ IMPORTANT: This feature requires you to be grouped with your fren.\nIt will not work properly if ungrouped (won't jump into air to follow).");

        // Mount Name (searchable dropdown from game data)
        ImGui.Text("Mount Name (if flying solo)");
        ImGui.SameLine();
        HelpMarker("Select the mount to use when flying solo.\n'Mount Roulette' picks a random mount.\nType to search the list.");

        var mountNames = plugin.MountNames;
        var currentMount = config.FoolFlier;
        ImGui.SetNextItemWidth(300);
        if (ImGui.BeginCombo("##MountSelect", string.IsNullOrEmpty(currentMount) ? "(none)" : currentMount))
        {
            // Search field - fixed at top
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##MountSearch", ref mountSearch, 64);
            ImGui.Separator();
            
            // Scrollable list area
            ImGui.BeginChild("##MountList", new Vector2(0, 200), false);
            for (var i = 0; i < mountNames.Length; i++)
            {
                if (!string.IsNullOrEmpty(mountSearch) &&
                    !mountNames[i].Contains(mountSearch, StringComparison.OrdinalIgnoreCase))
                    continue;

                var isSelected = mountNames[i] == currentMount;
                if (ImGui.Selectable(mountNames[i], isSelected))
                {
                    config.FoolFlier = mountNames[i];
                    configManager.SaveCurrentAccount();
                    mountSearch = "";
                }
                if (isSelected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndChild();
            ImGui.EndCombo();
        }

        // Summon Chocobo
        var forceGysahl = config.ForceGysahl;
        if (ImGui.Checkbox("Summon Chocobo", ref forceGysahl))
        {
            config.ForceGysahl = forceGysahl;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Auto-summon chocobo companion using Gysahl Greens when timer is low.\nWill not summon in sanctuaries or duties.");

        // Show greens count when enabled
        if (config.ForceGysahl)
        {
            var greensCount = GameHelpers.GetInventoryItemCount(GameHelpers.GysahlGreensItemId);
            var buddyTime = GameHelpers.GetBuddyTimeRemaining();
            var mins = (int)(buddyTime / 60);
            var secs = (int)(buddyTime % 60);
            var timerText = buddyTime > 0 ? $"{mins}m{secs:D2}s" : "Not summoned";
            var greensColor = greensCount > 0 ? new Vector4(0.3f, 1f, 0.3f, 1) : new Vector4(1f, 0.3f, 0.3f, 1);
            ImGui.Text("      ");
            ImGui.SameLine();
            ImGui.TextColored(greensColor, $"Gysahl Greens: {greensCount}");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), $" | Timer: {timerText}");
        }

        // Companion Stance (dropdown)
        var companionIdx = Array.IndexOf(CompanionStances, config.CompanionStrat);
        if (companionIdx < 0) companionIdx = 0;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Companion Stance", ref companionIdx, CompanionStances, CompanionStances.Length))
        {
            config.CompanionStrat = CompanionStances[companionIdx];
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Chocobo companion battle stance.\nControls how your companion behaves in combat.");

        // Auto Discard
        var autoDiscard = config.EnableAutoDiscard;
        if (ImGui.Checkbox("Auto Discard (/ays discard)", ref autoDiscard))
        {
            config.EnableAutoDiscard = autoDiscard;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Runs /ays discard every 10s only while mounted and in a safe idle window.\nFrenRider defers discard during combat, cutscenes, and area transitions.\nRequires AutoRetainer plugin.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Update Interval
        var updateInterval = config.UpdateInterval;
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputFloat("Update Interval (seconds)", ref updateInterval, 0.01f, 0.1f, "%.3f"))
        {
            config.UpdateInterval = Math.Max(0.05f, updateInterval);
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("How often the plugin runs its main logic loop.\nLower values = more responsive but higher CPU usage.\nDefault: 0.3s. WARNING: Values below 0.1 may impact performance.");
        if (updateInterval < 0.1f)
        {
            ImGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1), "WARNING: Very low update interval may impact game performance!");
        }
    }

    private void DrawDistanceTab(CharacterConfig config)
    {
        ImGui.Spacing();

        var cling = config.Cling;
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputFloat("Cling Distance", ref cling, 0.5f, 1.0f, "%.3f"))
        {
            config.Cling = cling;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Distance threshold (yalms) to start following fren.\nWhen you are farther than this from fren, navigation begins.");

        // Cling Type (no CBT)
        var clingType = config.ClingType;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Cling Type", ref clingType, ClingTypes, ClingTypes.Length))
        {
            config.ClingType = clingType;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Navigation method to reach fren.\nNavMesh: VNavmesh plugin pathfinding (recommended)\nVisland: Alternative navigation\nBossMod Follow: Uses BossMod's follow leader\nVanilla Follow: Game's built-in /follow");

        var clingTypeDuty = config.ClingTypeDuty;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Cling Type (Duty)", ref clingTypeDuty, ClingTypes, ClingTypes.Length))
        {
            config.ClingTypeDuty = clingTypeDuty;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Navigation method to use inside duties.\nMay need a different method than overworld.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Social Distancing");
        ImGui.Spacing();

        var sd = config.SocialDistancing;
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputFloat("Social Distance (yalms)", ref sd, 0.5f, 1.0f, "%.3f"))
        {
            config.SocialDistancing = sd;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Minimum distance to maintain from fren in outdoor/foray zones.\nPrevents characters from stacking on top of each other (less bot-like).\nSet to 0 to disable.");

        var sdIndoors = config.SocialDistancingIndoors;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Social Distance Indoors", ref sdIndoors, OnOff, OnOff.Length))
        {
            config.SocialDistancingIndoors = sdIndoors;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Enable social distancing indoors too.\nOff by default. Turn on if you want spacing in dungeons.");

        var xw = config.SocialDistanceXWiggle;
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputFloat("X Wiggle (+/- yalms)", ref xw, 0.1f, 0.5f, "%.3f"))
        {
            config.SocialDistanceXWiggle = xw;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Random variance on X axis during social distancing.\nAdds natural-looking movement variance.");

        var zw = config.SocialDistanceZWiggle;
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputFloat("Z Wiggle (+/- yalms)", ref zw, 0.1f, 0.5f, "%.3f"))
        {
            config.SocialDistanceZWiggle = zw;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Random variance on Z axis during social distancing.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Max Distances");
        ImGui.Spacing();

        var maxB = config.MaxBistance;
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputFloat("Max Follow Distance", ref maxB))
        {
            config.MaxBistance = maxB;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Maximum distance (yalms) to chase fren.\nBeyond this, stop following to avoid zone-hopping.");

        var maxBf = config.MaxBistanceForay;
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputFloat("Max Follow Distance (Foray)", ref maxBf))
        {
            config.MaxBistanceForay = maxBf;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Max follow distance in forays (Eureka/Bozja).\nLower value to avoid mini-aetheryte transition issues.");

        var dd = config.DDDistance;
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputFloat("DD Extra Distance", ref dd))
        {
            config.DDDistance = dd;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Extra distance added to cling in Deep Dungeons.\nPrevents constant chasing in PotD/HoH.");

        var fd = config.FDistance;
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputFloat("FATE Extra Distance", ref fd))
        {
            config.FDistance = fd;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Reserved for future autosync FATE behavior.\nCurrent follow distance does not change on FATE join or leave.");

        var autoSyncFate = config.AutoSyncFate;
        if (ImGui.Checkbox("Auto Sync FATE", ref autoSyncFate))
        {
            config.AutoSyncFate = autoSyncFate;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("DISABLE PANDORA's BOX"))
        {
            GameHelpers.SendChatCommand("/xldisableplugin Pandora's Box", "[FR][FATE-SYNC]");
        }
        ImGui.SameLine();
        HelpMarker("Runs /levelsync on after joining a FATE.\nDefers while mounted or riding pillion.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var formation = config.Formation;
        if (ImGui.Checkbox("Formation Following", ref formation))
        {
            config.Formation = formation;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Follow in a formation pattern (8-person grid).\nPositions based on party slot number.\nDisabled during mounting.");

        var fic = config.FollowInCombat;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Follow in Combat", ref fic, FollowInCombatOptions, FollowInCombatOptions.Length))
        {
            config.FollowInCombat = fic;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Whether to follow fren during combat.\nAuto: Let the plugin decide based on your job/role.");

        var hcr = config.HClingReset;
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputInt("Harmonized Cling Reset Ticks", ref hcr))
        {
            config.HClingReset = hcr;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Number of ticks before harmonized cling resets to 0.\nHandles special logic like DD/FATE force cling.");
    }

    private void DrawCombatTab(CharacterConfig config)
    {
        ImGui.Spacing();

        // Rotation Plugin (dropdown)
        var rotPlugin = config.RotationPlugin;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Rotation Plugin", ref rotPlugin, RotationPlugins, RotationPlugins.Length))
        {
            config.RotationPlugin = rotPlugin;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Which rotation automation plugin to use.\nBMR: BossModReborn\nVBM: VanillaBossMod\nRSR: RotationSolver Reborn\nWRATH: Wrath");

        // Rotation Plugin Foray (dropdown)
        var rotPluginForay = config.RotationPluginForay;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Rotation Plugin (Foray)", ref rotPluginForay, RotationPlugins, RotationPlugins.Length))
        {
            config.RotationPluginForay = rotPluginForay;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Rotation plugin for foray content (Eureka/Bozja).\nWRATH recommended for phantom job support.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Presets");
        ImGui.Spacing();

        var manualPresetConfig = config.ConfigureRotationPresetManually;
        if (ImGui.Checkbox("Configure rotation preset manually", ref manualPresetConfig))
        {
            config.ConfigureRotationPresetManually = manualPresetConfig;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Off: FrenRider chooses the BossMod preset from current job and selected rotation plugin.\nOn: use the preset name fields below.");

        if (config.ConfigureRotationPresetManually)
        {
            var autoRot = config.AutoRotationType;
            ImGui.SetNextItemWidth(200);
            if (ImGui.InputText("BM Rotation Preset", ref autoRot, 32))
            {
                config.AutoRotationType = autoRot;
                configManager.SaveCurrentAccount();
            }
            ImGui.SameLine();
            HelpMarker("Name of the auto-rotation preset for general content.\nMust match a preset name in your rotation plugin.\nUse 'none' to not change the preset.");

            var autoRotDD = config.AutoRotationTypeDD;
            ImGui.SetNextItemWidth(200);
            if (ImGui.InputText("BM Rotation Preset (DD)", ref autoRotDD, 32))
            {
                config.AutoRotationTypeDD = autoRotDD;
                configManager.SaveCurrentAccount();
            }
            ImGui.SameLine();
            HelpMarker("Preset name for Deep Dungeon content.\nUse 'none' to not change the preset.");

            var autoRotFATE = config.AutoRotationTypeFATE;
            ImGui.SetNextItemWidth(200);
            if (ImGui.InputText("BM Rotation Preset (FATE)", ref autoRotFATE, 32))
            {
                config.AutoRotationTypeFATE = autoRotFATE;
                configManager.SaveCurrentAccount();
            }
            ImGui.SameLine();
            HelpMarker("Preset name for FATE content.\nUse 'none' to not change the preset.");

            var forceBossModPreset = config.ForceBossModPresetRegardlessOfRotation;
            if (ImGui.Checkbox("Force BossMod preset regardless of rotation", ref forceBossModPreset))
            {
                config.ForceBossModPresetRegardlessOfRotation = forceBossModPreset;
                configManager.SaveCurrentAccount();
            }
            ImGui.SameLine();
            HelpMarker("When RSR or WRATH is selected, also force BMR/VBM to the configured zone preset.");
        }
        else
        {
            ImGui.TextDisabled("Managed presets: BMR/VBM use FRENRIDER role presets; RSR/WRATH use passive role presets.");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Behavior");
        ImGui.Spacing();

        // RSR Rotation Type (dropdown)
        var rotType = config.RotationType;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("RSR Rotation Type", ref rotType, RotationTypes, RotationTypes.Length))
        {
            config.RotationType = rotType;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("RSR operating mode when RSR is the selected rotation plugin.\nAuto: Full auto with standard hostile targeting.\nManual: Manual targeting mode.\nnone: Don't let FrenRider change the current rotation state.\nAuto (Support): Uses RSR's plugin-managed support mode and support-oriented targeting settings.\nPreviously Engaged Targets: Auto with RSR hostile targeting forced to previously engaged targets.");

        // BossMod AI (dropdown)
        var bossModAI = config.BossModAI;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("BossMod AI", ref bossModAI, BossModAIOptions, BossModAIOptions.Length))
        {
            config.BossModAI = bossModAI;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Enable or disable BossMod AI module.");

        // Positional (dropdown)
        var positional = config.PositionalInCombat;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Positional", ref positional, Positionals, Positionals.Length))
        {
            config.PositionalInCombat = positional;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Combat positional preference.\nFront: Stay in front of target\nRear: Stay behind target\nAny: No preference\nAuto: Let plugin decide based on job");

        var maxAIDist = config.MaxAIDistance;
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputFloat("Max AI Distance", ref maxAIDist))
        {
            config.MaxAIDistance = maxAIDist;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Max distance to targets for combat AI.\n424242 = Auto (plugin decides based on job: melee 2.6, caster 10).");

        var limitPct = config.LimitPct;
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputFloat("LB Threshold %", ref limitPct))
        {
            config.LimitPct = limitPct;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Target HP percentage to use Limit Break.\n-1 = Disabled.\nAutomatically uses LB3 if available, otherwise LB2.");

        DrawHacksSection(config);
    }

    private void DrawHacksSection(CharacterConfig config)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Hacks");
        ImGui.Spacing();

        var reduceRange = config.BmrReduceActivationRangeForOutdoorAreas;
        if (ImGui.Checkbox("BMR reduce activation range for outdoor areas", ref reduceRange))
        {
            config.BmrReduceActivationRangeForOutdoorAreas = reduceRange;
            configManager.SaveCurrentAccount();
            plugin.AdsReflectionIpcService.QueueImmediateUpdate();
        }
        ImGui.SameLine();
        HelpMarker($"When enabled, FrenRider asks ADS to set BMR MaxLoadDistance to {AdsReflectionIpcService.ReducedOutdoorMaxLoadDistance:0}.");

        var disableHunts = config.BmrDisableHuntModules;
        if (ImGui.Checkbox("BMR Disable Hunt Modules", ref disableHunts))
        {
            config.BmrDisableHuntModules = disableHunts;
            configManager.SaveCurrentAccount();
            plugin.AdsReflectionIpcService.QueueImmediateUpdate();
        }
        ImGui.SameLine();
        HelpMarker("When enabled, FrenRider asks ADS to disable BMR hunt modules.");

        var disableQueen = config.BmrDisableQueenLunatender;
        if (ImGui.Checkbox("BMR Disable Queen Lunatender", ref disableQueen))
        {
            config.BmrDisableQueenLunatender = disableQueen;
            configManager.SaveCurrentAccount();
            plugin.AdsReflectionIpcService.QueueImmediateUpdate();
        }
        ImGui.SameLine();
        HelpMarker("When enabled, FrenRider asks ADS to disable the BMR Queen Lunatender module.");

        var reflection = plugin.AdsReflectionIpcService;
        var statusColor = !reflection.IsAdsAvailable && reflection.HasPendingActions
            ? UiHelpers.Yellow
            : reflection.StatusText.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
                ? UiHelpers.Yellow
                : UiHelpers.Green;
        ImGui.TextColored(statusColor, $"ADS reflection: {reflection.StatusText}");

        if (reflection.NextAttemptAtUtc is { } nextAttempt && nextAttempt > DateTime.UtcNow)
        {
            var seconds = Math.Max(0, (int)Math.Ceiling((nextAttempt - DateTime.UtcNow).TotalSeconds));
            ImGui.TextColored(UiHelpers.Grey, $"  Next retry/reassert in {seconds}s.");
        }
    }

    private void DrawAdsTab(CharacterConfig config)
    {
        ImGui.Spacing();

        ImGui.Text("ADS Duty Handoff");
        ImGui.SameLine();
        HelpMarker("Per-duty-family ADS handoff.\nFrenRider keeps local duty logic running until /ads inside succeeds, then pauses only while ADS truly owns the run.");

        if (!config.AdsDutyFamilySettingsMigrated)
        {
            ImGui.TextDisabled($"Legacy seed active: {(config.UseAdsIfAvailable ? "global handoff on" : "global handoff off")} at threshold {Math.Clamp(config.AdsMaturityThreshold, 0, 3)}.");
            ImGui.SameLine();
            if (ImGui.SmallButton("Seed family rows from legacy values"))
            {
                config.EnsureAdsDutyFamilySettingsInitialized();
                configManager.SaveCurrentAccount();
            }
        }

        ImGui.Spacing();
        ImGui.Text("Duty Families");
        ImGui.SameLine();
        HelpMarker("Each family has its own enable toggle and maturity threshold.\n0 = not cleared, 1 = unsync cleared, 2 = duty support cleared, 3 = proven sync clear.");

        foreach (var entry in AdsDutyCategoryCatalog.Entries)
        {
            var settings = config.GetAdsDutyFamilySettings(entry.Category);
            var enabled = settings.Enabled;
            if (ImGui.Checkbox($"{entry.Label}##AdsFamily{entry.Category}", ref enabled))
            {
                config.SetAdsDutyFamilySettings(entry.Category, enabled, settings.MaturityThreshold);
                configManager.SaveCurrentAccount();
            }

            ImGui.SameLine();
            var threshold = Math.Clamp(settings.MaturityThreshold, 0, AdsMaturityOptions.Length - 1);
            ImGui.SetNextItemWidth(240);
            if (ImGui.Combo($"##AdsFamilyThreshold{entry.Category}", ref threshold, AdsMaturityOptions, AdsMaturityOptions.Length))
            {
                config.SetAdsDutyFamilySettings(entry.Category, enabled, threshold);
                configManager.SaveCurrentAccount();
            }
        }

        var adsEnableChestOpening = config.AdsEnableChestOpening;
        if (ImGui.Checkbox("Ask ADS to open chests (stub)", ref adsEnableChestOpening))
        {
            config.AdsEnableChestOpening = adsEnableChestOpening;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Recorded for the ADS handoff profile, but FrenRider does not push this setting into ADS yet.");

        var adsPresetSelection = Math.Clamp(config.AdsPresetSelection, 0, AdsPresetOptions.Length - 1);
        ImGui.SetNextItemWidth(240);
        if (ImGui.Combo("ADS Preset", ref adsPresetSelection, AdsPresetOptions, AdsPresetOptions.Length))
        {
            config.AdsPresetSelection = adsPresetSelection;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Placeholder for future ADS preset coordination. Stored now so the config shape is stable.");

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
            ImGui.TextColored(adsColor, $"ADS Status: {adsStatus}");
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

        UiHelpers.SectionHeader("Idle Behavior");
        DrawIdleBehaviorSection(config);

        UiHelpers.SectionHeader("Maintenance");
        DrawAutoDiscardSection(config);
        DrawAutorotSection(config);

        UiHelpers.SectionHeader("Debug");
        DrawDebugLoggingSection(config);
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
        if (ImGui.Combo("Loot Type", ref fulfIdx, LootTypes, LootTypes.Length))
        {
            config.FulfType = LootTypes[fulfIdx];
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("How loot is handled if LazyLoot is installed.\n'unchanged' = Don't modify loot settings.");
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

        if (config.FeedMeItemId > 0)
        {
            ImGui.Text($"  Selected: {config.FeedMeItem} {(config.FeedMeUseHighQuality ? "[HQ]" : "[NQ]")} (ID: {config.FeedMeItemId})");

            var useFoodHq = config.FeedMeUseHighQuality;
            if (ImGui.Checkbox("Use HQ food", ref useFoodHq))
            {
                config.FeedMeUseHighQuality = useFoodHq;
                plugin.AutomationService.InvalidateFoodCache();
                configManager.SaveCurrentAccount();
            }

            if (ImGui.SmallButton("Clear Food"))
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
            ImGui.TextDisabled("  No food selected. Food is optional.");
        }

        var feedMeSearch = config.FeedMeSearch;
        if (ImGui.Checkbox("Search for Food if Depleted", ref feedMeSearch))
        {
            config.FeedMeSearch = feedMeSearch;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("If configured food runs out, search inventory for any food starting from lowest item ID.");
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
        var displayText = selectedId > 0 ? $"{selectedName} ({selectedId})" : $"Select {label}...";

        ImGui.SetNextItemWidth(400);
        if (ImGui.BeginCombo($"##{label}Select", displayText))
        {
            ImGui.SetNextItemWidth(380);
            ImGui.InputText($"Search##{label}", ref search, 128);

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
                    if (ImGui.Selectable($"{item.Name} ({item.Id})##{label}{i}", isSelected))
                    {
                        selectedId = (int)item.Id;
                        selectedName = item.Name;
                        changed = true;
                    }
                }

                if (shown == 0)
                    ImGui.TextDisabled("No results. Try a different search term.");
            }
            else
            {
                ImGui.TextDisabled("Type at least 2 characters to search...");
            }

            ImGui.EndCombo();
        }

        return changed;
    }

    private void DrawRepairSection(CharacterConfig config)
    {
        if (config.Repair == 2)
        {
            config.Repair = 0;
            configManager.SaveCurrentAccount();
        }

        var selfRepair = config.Repair == 1;
        if (ImGui.Checkbox("Self Repair", ref selfRepair))
        {
            config.Repair = selfRepair ? 1 : 0;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("When enabled, FrenRider checks equipped gear durability and sends /ads selfrepair when any equipped item is below the threshold. ADS owns the actual repair window automation.");

        var tornClothes = Math.Clamp(config.TornClothes, 0, 100);
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputInt("Repair At % Durability", ref tornClothes))
        {
            config.TornClothes = Math.Clamp(tornClothes, 0, 100);
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Trigger repair when gear durability falls below this percentage.");
    }

    private void DrawIdleBehaviorSection(CharacterConfig config)
    {
        var idleMode = config.IdleActionMode;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Idle Mode", ref idleMode, IdleActionModes, IdleActionModes.Length))
        {
            config.IdleActionMode = idleMode;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("What to do when idle.\nSpecific Action: execute one command\nAction From List: rotate through a list");

        if (config.IdleActionMode == 0)
        {
            var idleAction = config.IdleAction;
            ImGui.SetNextItemWidth(300);
            if (ImGui.InputText("Idle Command", ref idleAction, 64))
            {
                config.IdleAction = idleAction;
                configManager.SaveCurrentAccount();
            }
            ImGui.SameLine();
            HelpMarker("Slash command to execute when idle.\nExamples: /tomescroll, /dance, /snd run scriptname");
        }
        else
        {
            var listMode = config.IdleListMode;
            ImGui.SetNextItemWidth(200);
            if (ImGui.Combo("List Source", ref listMode, IdleListModes, IdleListModes.Length))
            {
                config.IdleListMode = listMode;
                configManager.SaveCurrentAccount();
            }
            ImGui.SameLine();
            HelpMarker("Default List: built-in emotes\nCustom List: your own command list");

            if (config.IdleListMode == 1)
                DrawCustomIdleListEditor(config);
        }

        var idleTicks = config.IdleTicksBeforeAction;
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputInt("Idle Ticks Before Action", ref idleTicks))
        {
            config.IdleTicksBeforeAction = idleTicks;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Number of update ticks before idle action triggers.");
    }

    private void DrawAutoDiscardSection(CharacterConfig config)
    {
        var autoDiscard = config.EnableAutoDiscard;
        if (ImGui.Checkbox("Auto Discard (/ays discard)", ref autoDiscard))
        {
            config.EnableAutoDiscard = autoDiscard;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Runs /ays discard while mounted and in a safe idle window.\nRequires AutoRetainer plugin.");
    }

    private void DrawAutorotSection(CharacterConfig config)
    {
        ImGui.TextDisabled("FrenRider installs BossMod presets whenever it is enabled.");

        if (ImGui.Button("Push Presets Now"))
            plugin.AutorotIpcService.CreatePresets(force: true);
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), plugin.AutorotIpcService.LastStatus);
    }

    private void DrawDebugLoggingSection(CharacterConfig config)
    {
        var spamPrinter = config.SpamPrinter;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Echo Messages", ref spamPrinter, OnOff, OnOff.Length))
        {
            config.SpamPrinter = spamPrinter;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Print status messages to game chat.\nUseful for debugging but fills chat quickly.");
    }

    private void DrawInviteWhitelistSection(CharacterConfig config)
    {
        ImGui.Text("Trusted inviters");
        HelpMarker("Players in this list will have party invites automatically accepted when you are not in a group.\nNames should omit the @Server part.");
        ImGui.Spacing();

        for (int i = 0; i < config.InviteWhitelist.Count; i++)
        {
            var entry = config.InviteWhitelist[i];
            ImGui.Text($"  {Disp(entry)}");
            ImGui.SameLine();
            if (ImGui.SmallButton($"X##wlDuty{i}"))
            {
                config.InviteWhitelist.RemoveAt(i);
                configManager.SaveCurrentAccount();
                break;
            }
        }

        ImGui.SetNextItemWidth(220);
        if (ImGui.InputText("##WhitelistAddDuty", ref whitelistInput, 64, ImGuiInputTextFlags.EnterReturnsTrue))
            AddWhitelistEntry(config);

        ImGui.SameLine();
        if (ImGui.SmallButton("Add##WhitelistDuty"))
            AddWhitelistEntry(config);
    }

    private void DrawAutoYesSection(CharacterConfig config)
    {
        var raiseOffer = config.RaiseOfferAutoAccept;
        if (ImGui.Checkbox("Raise offers", ref raiseOffer))
        {
            config.RaiseOfferAutoAccept = raiseOffer;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Automatically accept raise offers from other players.");

        var teleportOffer = config.TeleportOfferAutoAccept;
        if (ImGui.Checkbox("Teleport offers", ref teleportOffer))
        {
            config.TeleportOfferAutoAccept = teleportOffer;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Automatically accept teleport offers.");

        var partyInvite = config.PartyInviteAutoAccept;
        if (ImGui.Checkbox("Party invites (backup)", ref partyInvite))
        {
            config.PartyInviteAutoAccept = partyInvite;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Backup auto-accept for party invites. Primary invite handling uses the whitelist.");
    }

    private void DrawExitBehaviourSection(CharacterConfig config)
    {
        if (config.NormalizeExitMethodSelection())
            configManager.SaveCurrentAccount();

        if (ImGui.RadioButton("FrenRider Exit method", !config.UseAdsLeaveAfterAdsDuty))
        {
            config.UseAdsLeaveAfterAdsDuty = false;
            if (!config.ExitAfterDutyEnds && !config.LeaveWhenAllLeft)
                config.ExitAfterDutyEnds = true;
            config.NormalizeExitMethodSelection();
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Use FrenRider's local Leave Duty flow after the configured duty-end condition.");

        if (!config.UseAdsLeaveAfterAdsDuty)
        {
            ImGui.Indent();
            DrawFrenRiderExitMethodOptions(config);
            ImGui.Unindent();
        }

        ImGui.Spacing();
        if (ImGui.RadioButton("ADS Exit Method", config.UseAdsLeaveAfterAdsDuty))
        {
            config.UseAdsLeaveAfterAdsDuty = true;
            config.ExitAfterDutyEnds = false;
            config.LeaveWhenAllLeft = false;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Send /ads leave after the configured duty-end delay. FrenRider does not also run its own Leave Duty flow.");

        ImGui.Spacing();
        ImGui.Text("Duty-end delay");
        ImGui.SameLine();
        var exitSeconds = config.ExitAfterDutySeconds;
        ImGui.SetNextItemWidth(70);
        if (ImGui.InputInt("##exitSecondsDuty", ref exitSeconds))
        {
            config.ExitAfterDutySeconds = Math.Max(1, exitSeconds);
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        ImGui.Text("seconds after duty ends");
    }

    private void DrawFrenRiderExitMethodOptions(CharacterConfig config)
    {
        var method = config.ExitAfterDutyEnds
            ? 0
            : config.LeaveWhenAllLeft
                ? 1
                : 2;

        if (ImGui.RadioButton("Exit after N seconds", method == 0))
        {
            config.UseAdsLeaveAfterAdsDuty = false;
            config.ExitAfterDutyEnds = true;
            config.LeaveWhenAllLeft = false;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Automatically leave the duty N seconds after it completes.");

        if (ImGui.RadioButton("Leave when all others left", method == 1))
        {
            config.UseAdsLeaveAfterAdsDuty = false;
            config.ExitAfterDutyEnds = false;
            config.LeaveWhenAllLeft = true;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Leave the duty if no other party members are visible in the zone.");

        if (ImGui.RadioButton("No automatic exit", method == 2))
        {
            config.UseAdsLeaveAfterAdsDuty = false;
            config.ExitAfterDutyEnds = false;
            config.LeaveWhenAllLeft = false;
            configManager.SaveCurrentAccount();
        }
    }

    private void DrawUiSettingsSection()
    {
        var videoNotificationsEnabled = configuration.VideoNotificationsEnabled;
        if (ImGui.Checkbox("Video Notifications", ref videoNotificationsEnabled))
        {
            configuration.VideoNotificationsEnabled = videoNotificationsEnabled;
            configuration.Save();
        }
        ImGui.SameLine();
        HelpMarker("Play videos when Fren Rider is enabled or disabled.\nRequires VLC media player.");

        if (!plugin.VideoPlaybackService.IsVLCAvailable())
        {
            ImGui.TextColored(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), "VLC not found");
            ImGui.SameLine();
            if (ImGui.SmallButton("Download VLC"))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.videolan.org/vlc/",
                    UseShellExecute = true
                });
            }
        }

        var movable = configuration.IsConfigWindowMovable;
        if (ImGui.Checkbox("Movable Config Window", ref movable))
        {
            configuration.IsConfigWindowMovable = movable;
            configuration.Save();
        }

        var dtrEnabled = configuration.DtrBarEnabled;
        if (ImGui.Checkbox("DTR Bar Enabled", ref dtrEnabled))
        {
            configuration.DtrBarEnabled = dtrEnabled;
            configuration.Save();
        }
        ImGui.SameLine();
        HelpMarker("Show or hide the DTR bar entry.");

        var dtrMode = configuration.DtrBarMode;
        var dtrModes = new[] { "Text Only", "Icon+Text", "Icon Only" };
        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo("DTR Bar Mode", ref dtrMode, dtrModes, dtrModes.Length))
        {
            configuration.DtrBarMode = dtrMode;
            configuration.Save();
        }

        ImGui.Spacing();
        ImGui.Text("DTR Icons (max 3 characters)");
        ImGui.SameLine();
        if (ImGui.SmallButton("Copy Icon Guide Link"))
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
        ImGui.Text("Loot");
        ImGui.Spacing();

        var fulfIdx = Array.IndexOf(LootTypes, config.FulfType);
        if (fulfIdx < 0) fulfIdx = 0;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Loot Type", ref fulfIdx, LootTypes, LootTypes.Length))
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
        ImGui.Text("Food");
        ImGui.Spacing();

        var feedMeItem = config.FeedMeItem;
        ImGui.SetNextItemWidth(300);
        if (ImGui.InputText("Food Item Name", ref feedMeItem, 64))
        {
            config.FeedMeItem = feedMeItem;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Name of food to auto-consume.\nFull item search from game data planned for a future update.");

        var feedMeSearch = config.FeedMeSearch;
        if (ImGui.Checkbox("Search for Food if Depleted", ref feedMeSearch))
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
        ImGui.Text("Repair");
        ImGui.Spacing();

        DrawRepairSection(config);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // --- Idle Behavior ---
        ImGui.Text("Idle Behavior");
        ImGui.Spacing();

        var idleMode = config.IdleActionMode;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Idle Mode", ref idleMode, IdleActionModes, IdleActionModes.Length))
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
            if (ImGui.InputText("Idle Command", ref idleAction, 64))
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
            if (ImGui.Combo("List Source", ref listMode, IdleListModes, IdleListModes.Length))
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
        if (ImGui.InputInt("Idle Ticks Before Action", ref idleTicks))
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
        ImGui.Text("Debug / Logging");
        ImGui.Spacing();

        var spamPrinter = config.SpamPrinter;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Echo Messages", ref spamPrinter, OnOff, OnOff.Length))
        {
            config.SpamPrinter = spamPrinter;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Print status messages to game chat.\nUseful for debugging but fills chat quickly.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // --- UI Settings ---
        ImGui.Text("UI Settings");
        ImGui.Spacing();

        var videoNotificationsEnabled = configuration.VideoNotificationsEnabled;
        if (ImGui.Checkbox("Video Notifications", ref videoNotificationsEnabled))
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
            ImGui.TextColored(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), "⚠ VLC Not Found");
            ImGui.SameLine();
            
            // Make VLC text clickable
            var vlcColor = new Vector4(0.4f, 0.8f, 1.0f, 1.0f); // Light blue
            ImGui.TextColored(vlcColor, "VLC");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                ImGui.SetTooltip("Click to download VLC media player");
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
        if (ImGui.Checkbox("Movable Config Window", ref movable))
        {
            configuration.IsConfigWindowMovable = movable;
            configuration.Save();
        }

        var dtrEnabled = configuration.DtrBarEnabled;
        if (ImGui.Checkbox("DTR Bar Enabled", ref dtrEnabled))
        {
            configuration.DtrBarEnabled = dtrEnabled;
            configuration.Save();
        }
        ImGui.SameLine();
        HelpMarker("Show/hide the DTR bar entry (server info bar).");

        var dtrMode = configuration.DtrBarMode;
        var dtrModes = new[] { "Text Only", "Icon+Text", "Icon Only" };
        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo("DTR Bar Mode", ref dtrMode, dtrModes, dtrModes.Length))
        {
            configuration.DtrBarMode = dtrMode;
            configuration.Save();
        }
        ImGui.SameLine();
        HelpMarker("DTR bar display mode:\nText Only: 'FR: On/Off'\nIcon+Text: '⚫ FR'\nIcon Only: '⚫'");

        ImGui.Spacing();
        ImGui.Text("DTR Icons (max 3 characters)");
        ImGui.SameLine();
        HelpMarker("Customize the glyphs used for enabled/disabled icon modes.");
        ImGui.SameLine();
        if (ImGui.SmallButton("Copy Icon Guide Link"))
        {
            ImGui.SetClipboardText(IconGuideUrl);
            Plugin.Log.Info("Copied icon guide link to clipboard");
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Copies the Lodestone blog link with suggested glyphs");

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
        ImGui.Text("Invite Whitelist");
        ImGui.SameLine();
        HelpMarker("Players in this list will have their party invites automatically accepted when you're not in a group.\nWhen you join a party via whitelist invite, the inviter will automatically be set as your Fren.\nEnter names without the @Server part.");
        ImGui.Spacing();

        for (int i = 0; i < config.InviteWhitelist.Count; i++)
        {
            var entry = config.InviteWhitelist[i];
            ImGui.Text($"  {Disp(entry)}");
            ImGui.SameLine();
            if (ImGui.SmallButton($"X##wl{i}"))
            {
                config.InviteWhitelist.RemoveAt(i);
                configManager.SaveCurrentAccount();
                break;
            }
        }

        ImGui.SetNextItemWidth(200);
        if (ImGui.InputText("##WhitelistAdd", ref whitelistInput, 64, ImGuiInputTextFlags.EnterReturnsTrue))
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
        if (ImGui.SmallButton("Add"))
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
        ImGui.Text("Auto-Yes Dialogs");
        ImGui.SameLine();
        HelpMarker("Automatically click Yes on specific dialog types when FrenRider is enabled.\nWorks alongside YesAlready - FrenRider pauses YesAlready and handles these dialogs itself.");
        ImGui.Spacing();

        // Raise offers
        var raiseOffer = config.RaiseOfferAutoAccept;
        if (ImGui.Checkbox("Raise offers", ref raiseOffer))
        {
            config.RaiseOfferAutoAccept = raiseOffer;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Automatically accept raise offers from other players.");

        // Teleport offers
        var teleportOffer = config.TeleportOfferAutoAccept;
        if (ImGui.Checkbox("Teleport offers", ref teleportOffer))
        {
            config.TeleportOfferAutoAccept = teleportOffer;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Automatically accept teleport offers (e.g., Return, Teleport to).\nNote: Party invites are handled by the whitelist system below.");

        // Party invites (backup to whitelist)
        var partyInvite = config.PartyInviteAutoAccept;
        if (ImGui.Checkbox("Party invites (backup)", ref partyInvite))
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
        ImGui.Text("Exit Behaviour");
        ImGui.Spacing();
        DrawExitBehaviourSection(config);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // --- Autorot IPC ---
        ImGui.Text("Autorot Presets");
        ImGui.Spacing();

        ImGui.TextDisabled("FrenRider installs BossMod presets whenever it is enabled.");

        if (ImGui.Button("Push Presets Now"))
        {
            plugin.AutorotIpcService.CreatePresets(force: true);
        }
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), plugin.AutorotIpcService.LastStatus);
    }

    private void DrawCustomIdleListEditor(CharacterConfig config)
    {
        if (config.EnsureCustomIdleListSeeded())
            configManager.SaveCurrentAccount();

        ImGui.Spacing();
        if (ImGui.SmallButton("[+]##IdleCustomAdd"))
        {
            var commands = CharacterConfig.CloneCustomIdleList(config.CustomIdleList)
                .Concat(new[] { CharacterConfig.DefaultCustomIdleCommand })
                .ToArray();
            config.CustomIdleList = commands;
            configManager.SaveCurrentAccount();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Add command");

        var list = CharacterConfig.CloneCustomIdleList(config.CustomIdleList);
        for (var i = 0; i < list.Length; i++)
        {
            ImGui.PushID($"IdleCustom{i}");

            var canRemove = list.Length > 1;
            if (!canRemove)
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);

            var removeClicked = ImGui.SmallButton("[-]") && canRemove;
            if (!canRemove)
                ImGui.PopStyleVar();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(canRemove ? "Remove command" : "At least one command required");

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
            if (ImGui.InputText("##IdleCustomCommand", ref command, 256))
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
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1), "Fren Rider");
        ImGui.Text("A Dalamud plugin for FFXIV multiplayer follow/combat automation.");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Commands:");
        ImGui.BulletText("/frenrider - Open main window");
        ImGui.BulletText("/fr - Open main window (alias)");
        ImGui.BulletText("/fr on - Enable Fren Rider");
        ImGui.BulletText("/fr off - Disable Fren Rider");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(1f, 0.8f, 0.4f, 1), "Required Dependencies:");
        ImGui.BulletText("vnavmesh - Navigation and pathfinding");
        ImGui.Spacing();

        ImGui.TextColored(new Vector4(0.6f, 1f, 0.6f, 1), "Optional Plugins:");
        ImGui.BulletText("Visland - Alternative navigation (if vnavmesh unavailable)");
        ImGui.BulletText("BossMod / BossModReborn - Combat AI and following");
        ImGui.BulletText("Rotation Solver Reborn - Combat rotation automation");
        ImGui.BulletText("WRATH - Combat rotation automation");
        ImGui.BulletText("Questionable - Quest automation integration");
        ImGui.BulletText("Automaton (CBT) by Croizat - Enhanced duty start/end, auto-leave");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Multiplayer Guide:");
        ImGui.Spacing();
        var guideUrl = "https://github.com/McVaxius/dhogsbreakfeast/tree/main/Dungeons%20and%20Multiboxing/Multiplayer%20Guide";
        ImGui.TextColored(new Vector4(0.3f, 0.7f, 1f, 1), guideUrl);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip("Click to copy URL to clipboard");
        }
        if (ImGui.IsItemClicked())
        {
            ImGui.SetClipboardText(guideUrl);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "Made by McVaxius");
    }

    // --- Helpers ---

    /// <summary>Display a name, applying Krangle if enabled.</summary>
    private string Disp(string name)
    {
        return configuration.KrangleEnabled ? KrangleService.KrangleName(name) : name;
    }

    private string GetCurrentCharacterKey()
    {
        if (!Plugin.ClientState.IsLoggedIn) return "";
        var charName = Plugin.ObjectTable.LocalPlayer?.Name.ToString() ?? "";
        var worldName = Plugin.ObjectTable.LocalPlayer?.HomeWorld.Value.Name.ToString() ?? "";
        return !string.IsNullOrEmpty(charName) && !string.IsNullOrEmpty(worldName)
            ? $"{charName}@{worldName}"
            : "";
    }

    private bool DrawIconInputs(string label, ref string value, string fallback)
    {
        var updated = false;
        var glyph = value;
        ImGui.SetNextItemWidth(80);
        if (ImGui.InputText($"{label} Icon", ref glyph, 8))
        {
            value = SanitizeIconInput(glyph, fallback);
            updated = true;
        }
        ImGui.SameLine();
        ImGui.TextDisabled($"Shown when Fren Rider is {label.ToLowerInvariant()}");

        var code = FormatIconCode(value);
        ImGui.SetNextItemWidth(160);
        if (ImGui.InputText($"{label} Icon Code", ref code, 64))
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
        var config = configManager.GetActiveConfig();
        frenNameInput = config?.FrenName ?? "";
    }

    private static void HelpMarker(string desc)
    {
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 20.0f);
            ImGui.TextUnformatted(desc);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }
}
