using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private readonly ConfigManager configManager;

    private string currentTab = "Party";
    private string accountAliasEdit = "";
    private string frenNameInput = "";
    private bool frenNameFocused = false;
    private string mountSearch = "";
    private bool isDraggingSplitter = false;
    private string whitelistInput = "";

    private static readonly string[] CompanionStances = { "Free Stance", "Defender Stance", "Attacker Stance", "Healer Stance", "Follow" };
    private static readonly string[] ClingTypes = { "NavMesh", "Visland", "BossMod Follow", "Vanilla Follow" };
    private static readonly string[] RotationPlugins = { "BMR", "VBM", "RSR", "WRATH" };
    private static readonly string[] RotationTypes = { "Auto", "Manual", "none" };
    private static readonly string[] BossModAIOptions = { "on", "off" };
    private static readonly string[] Positionals = { "Front", "Rear", "Any", "Auto" };
    private static readonly string[] FollowInCombatOptions = { "No", "Yes", "Auto" };
    private static readonly string[] LootTypes = { "unchanged", "need", "greed", "pass" };
    private static readonly string[] OnOff = { "Off", "On" };
    private static readonly string[] RepairOptions = { "No", "Self Repair", "Inn NPC" };
    private static readonly string[] IdleActionModes = { "Specific Action", "Action From List" };
    private static readonly string[] IdleListModes = { "Default List", "Custom List" };

    public ConfigWindow(Plugin plugin) : base("Fren Rider Settings###FrenRiderConfig")
    {
        Flags = ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(900, 550);
        SizeCondition = ImGuiCond.FirstUseEver;

        this.plugin = plugin;
        this.configuration = plugin.Configuration;
        this.configManager = plugin.ConfigManager;
    }

    public void Dispose() { }

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
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##AccountAlias", ref accountAliasEdit, 64))
        {
            configManager.UpdateAccountAlias(accountAliasEdit);
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
            if (ImGui.BeginTabItem("Party / Friend"))
            {
                currentTab = "Party";
                DrawPartyTab(config);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Distance / Following"))
            {
                currentTab = "Distance";
                DrawDistanceTab(config);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Combat / AI"))
            {
                currentTab = "Combat";
                DrawCombatTab(config);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Misc"))
            {
                currentTab = "Misc";
                DrawMiscTab(config);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("About"))
            {
                currentTab = "About";
                DrawAboutTab();
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

        // Force Gysahl
        var forceGysahl = config.ForceGysahl;
        if (ImGui.Checkbox("Force Gysahl Green Usage", ref forceGysahl))
        {
            config.ForceGysahl = forceGysahl;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Force use of Gysahl Greens to summon your chocobo companion.\nMay cause issues in towns.");

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
        HelpMarker("Extra distance padding during FATEs.\nAllows more spread-out positioning.");

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

        var autoRot = config.AutoRotationType;
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputText("Auto Rotation Preset", ref autoRot, 32))
        {
            config.AutoRotationType = autoRot;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Name of the auto-rotation preset for general content.\nMust match a preset name in your rotation plugin.\nUse 'none' to not change the preset.");

        var autoRotDD = config.AutoRotationTypeDD;
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputText("Auto Rotation Preset (DD)", ref autoRotDD, 32))
        {
            config.AutoRotationTypeDD = autoRotDD;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Preset name for Deep Dungeon content.");

        var autoRotFATE = config.AutoRotationTypeFATE;
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputText("Auto Rotation Preset (FATE)", ref autoRotFATE, 32))
        {
            config.AutoRotationTypeFATE = autoRotFATE;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Preset name for FATE content.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Behavior");
        ImGui.Spacing();

        // Rotation Type (dropdown)
        var rotType = config.RotationType;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Rotation Type", ref rotType, RotationTypes, RotationTypes.Length))
        {
            config.RotationType = rotType;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("RSR rotation mode.\nAuto: Fully automated rotation\nManual: Manual trigger\nnone: Don't change setting");

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

        // --- XP / Repair ---
        ImGui.Text("XP / Repair");
        ImGui.Spacing();

        var xpItem = config.XpItem;
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputInt("XP Item ID (0=Off)", ref xpItem))
        {
            config.XpItem = xpItem;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Item ID to auto-equip for XP bonus.\nAzyma Earring = 41081.\nUse SimpleTweaks to see item IDs.\n0 = Disabled.");

        var repair = config.Repair;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Repair", ref repair, RepairOptions, RepairOptions.Length))
        {
            config.Repair = repair;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Auto-repair method.\nNo: Don't auto-repair\nSelf Repair: Use dark matter\nInn NPC: Use inn repair NPC (only if parked at inn)");

        var tornClothes = config.TornClothes;
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputInt("Repair At % Durability", ref tornClothes))
        {
            config.TornClothes = tornClothes;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Trigger repair when gear durability falls below this percentage.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // --- Duty ---
        ImGui.Text("Duty");
        ImGui.Spacing();

        var cbtEdse = config.CbtEdse;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("Enhanced Duty Start/End", ref cbtEdse, OnOff, OnOff.Length))
        {
            config.CbtEdse = cbtEdse;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("CBT enhanced duty start/end.\nEnables special settings when entering/leaving duties.");

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
                ImGui.TextColored(new Vector4(1, 1, 0.4f, 1), "Custom list editor planned for future update.");
                ImGui.TextWrapped("Tip: You can use commands like /snd run scriptname or /simulationf motion");
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
        ImGui.Separator();
        ImGui.Spacing();

        // --- Invite Whitelist ---
        ImGui.Text("Invite Whitelist");
        ImGui.SameLine();
        HelpMarker("Players in this list will have their party invites automatically accepted.\nEnter names without the @Server part.");
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

        // --- Auto Leave Duty ---
        ImGui.Text("Auto Leave Duty");
        ImGui.Spacing();

        var autoLeave = config.AutoLeaveDutyEnabled;
        if (ImGui.Checkbox("Enable Auto Leave", ref autoLeave))
        {
            config.AutoLeaveDutyEnabled = autoLeave;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Automatically leave a duty when certain conditions are met.");

        if (config.AutoLeaveDutyEnabled)
        {
            var allLeft = config.AutoLeaveWhenAllLeft;
            if (ImGui.Checkbox("Leave when all others left", ref allLeft))
            {
                config.AutoLeaveWhenAllLeft = allLeft;
                configManager.SaveCurrentAccount();
            }

            var dutyEnded = config.AutoLeaveWhenDutyEnded;
            if (ImGui.Checkbox("Leave when duty ended", ref dutyEnded))
            {
                config.AutoLeaveWhenDutyEnded = dutyEnded;
                configManager.SaveCurrentAccount();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // --- Autorot IPC ---
        ImGui.Text("Autorot Presets");
        ImGui.Spacing();

        var pushOnEnable = config.AutorotPushOnEnable;
        if (ImGui.Checkbox("Push presets on enable", ref pushOnEnable))
        {
            config.AutorotPushOnEnable = pushOnEnable;
            configManager.SaveCurrentAccount();
        }
        ImGui.SameLine();
        HelpMarker("Automatically push FRENRIDER and DD presets\ninto BMR/VBM via IPC when the plugin is enabled.");

        if (ImGui.Button("Push Presets Now"))
        {
            plugin.AutorotIpcService.CreatePresets();
        }
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), plugin.AutorotIpcService.LastStatus);
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
