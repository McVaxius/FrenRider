using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace FrenRider.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;

    public ConfigWindow(Plugin plugin) : base("Fren Rider Settings###FrenRiderConfig")
    {
        Flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(500, 600);
        SizeCondition = ImGuiCond.FirstUseEver;

        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        if (configuration.IsConfigWindowMovable)
        {
            Flags &= ~ImGuiWindowFlags.NoMove;
        }
        else
        {
            Flags |= ImGuiWindowFlags.NoMove;
        }
    }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("FrenRiderTabs"))
        {
            DrawPartyTab();
            DrawDistanceTab();
            DrawCombatTab();
            DrawAutomationTab();
            DrawMiscTab();
            ImGui.EndTabBar();
        }
    }

    private void DrawPartyTab()
    {
        if (ImGui.BeginTabItem("Party / Friend"))
        {
            ImGui.Spacing();

            var frenName = configuration.FrenName;
            if (ImGui.InputText("Fren Name", ref frenName, 64))
            {
                configuration.FrenName = frenName;
                configuration.Save();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Partial name OK if unique. Do not include @Server.");
            }

            var flyYouFools = configuration.FlyYouFools;
            if (ImGui.Checkbox("Fly You Fools (fly alongside instead of pillion)", ref flyYouFools))
            {
                configuration.FlyYouFools = flyYouFools;
                configuration.Save();
            }

            var foolFlier = configuration.FoolFlier;
            if (ImGui.InputText("Mount Name (if flying solo)", ref foolFlier, 64))
            {
                configuration.FoolFlier = foolFlier;
                configuration.Save();
            }

            var forceGysahl = configuration.ForceGysahl;
            if (ImGui.Checkbox("Force Gysahl Green Usage", ref forceGysahl))
            {
                configuration.ForceGysahl = forceGysahl;
                configuration.Save();
            }

            var companionStrat = configuration.CompanionStrat;
            if (ImGui.InputText("Companion Stance", ref companionStrat, 32))
            {
                configuration.CompanionStrat = companionStrat;
                configuration.Save();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Follow, Free Stance, Defender Stance, Healer Stance, Attacker Stance");
            }

            var timeFriction = configuration.TimeFriction;
            if (ImGui.SliderFloat("Tick Rate (seconds)", ref timeFriction, 0.1f, 2.0f))
            {
                configuration.TimeFriction = timeFriction;
                configuration.Save();
            }

            ImGui.EndTabItem();
        }
    }

    private void DrawDistanceTab()
    {
        if (ImGui.BeginTabItem("Distance / Following"))
        {
            ImGui.Spacing();

            var cling = configuration.Cling;
            if (ImGui.SliderFloat("Cling Distance", ref cling, 0.5f, 30.0f))
            {
                configuration.Cling = cling;
                configuration.Save();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Distance threshold to start following fren.");
            }

            var clingType = configuration.ClingType;
            if (ImGui.Combo("Cling Type", ref clingType, "NavMesh\0Visland\0BossMod Follow\0CBT Autofollow\0Vanilla Follow\0"))
            {
                configuration.ClingType = clingType;
                configuration.Save();
            }

            var clingTypeDuty = configuration.ClingTypeDuty;
            if (ImGui.Combo("Cling Type (Duty)", ref clingTypeDuty, "NavMesh\0Visland\0BossMod Follow\0CBT Autofollow\0Vanilla Follow\0"))
            {
                configuration.ClingTypeDuty = clingTypeDuty;
                configuration.Save();
            }

            ImGui.Separator();
            ImGui.Text("Social Distancing");

            var socialDistancing = configuration.SocialDistancing;
            if (ImGui.SliderFloat("Social Distance (yalms)", ref socialDistancing, 0.0f, 30.0f))
            {
                configuration.SocialDistancing = socialDistancing;
                configuration.Save();
            }

            var sdIndoors = configuration.SocialDistancingIndoors;
            if (ImGui.Combo("Social Distance Indoors", ref sdIndoors, "Off\0On\0"))
            {
                configuration.SocialDistancingIndoors = sdIndoors;
                configuration.Save();
            }

            var xWiggle = configuration.SocialDistanceXWiggle;
            if (ImGui.SliderFloat("X Wiggle (+/- yalms)", ref xWiggle, 0.0f, 5.0f))
            {
                configuration.SocialDistanceXWiggle = xWiggle;
                configuration.Save();
            }

            var zWiggle = configuration.SocialDistanceZWiggle;
            if (ImGui.SliderFloat("Z Wiggle (+/- yalms)", ref zWiggle, 0.0f, 5.0f))
            {
                configuration.SocialDistanceZWiggle = zWiggle;
                configuration.Save();
            }

            ImGui.Separator();
            ImGui.Text("Max Distances");

            var maxBistance = configuration.MaxBistance;
            if (ImGui.InputFloat("Max Follow Distance", ref maxBistance))
            {
                configuration.MaxBistance = maxBistance;
                configuration.Save();
            }

            var maxBistanceForay = configuration.MaxBistanceForay;
            if (ImGui.InputFloat("Max Follow Distance (Foray)", ref maxBistanceForay))
            {
                configuration.MaxBistanceForay = maxBistanceForay;
                configuration.Save();
            }

            var ddDist = configuration.DDDistance;
            if (ImGui.InputFloat("DD Extra Distance", ref ddDist))
            {
                configuration.DDDistance = ddDist;
                configuration.Save();
            }

            var fDist = configuration.FDistance;
            if (ImGui.InputFloat("FATE Extra Distance", ref fDist))
            {
                configuration.FDistance = fDist;
                configuration.Save();
            }

            var formation = configuration.Formation;
            if (ImGui.Checkbox("Formation Following", ref formation))
            {
                configuration.Formation = formation;
                configuration.Save();
            }

            var followInCombat = configuration.FollowInCombat;
            if (ImGui.InputInt("Follow in Combat (0=No, 1=Yes, 42=Auto)", ref followInCombat))
            {
                configuration.FollowInCombat = followInCombat;
                configuration.Save();
            }

            ImGui.EndTabItem();
        }
    }

    private void DrawCombatTab()
    {
        if (ImGui.BeginTabItem("Combat / AI"))
        {
            ImGui.Spacing();

            var rotPlugin = configuration.RotationPlugin;
            if (ImGui.InputText("Rotation Plugin", ref rotPlugin, 16))
            {
                configuration.RotationPlugin = rotPlugin;
                configuration.Save();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("BMR, VBM, RSR, or WRATH");
            }

            var rotPluginForay = configuration.RotationPluginForay;
            if (ImGui.InputText("Rotation Plugin (Foray)", ref rotPluginForay, 16))
            {
                configuration.RotationPluginForay = rotPluginForay;
                configuration.Save();
            }

            var autoRot = configuration.AutoRotationType;
            if (ImGui.InputText("Auto Rotation Preset", ref autoRot, 32))
            {
                configuration.AutoRotationType = autoRot;
                configuration.Save();
            }

            var autoRotDD = configuration.AutoRotationTypeDD;
            if (ImGui.InputText("Auto Rotation Preset (DD)", ref autoRotDD, 32))
            {
                configuration.AutoRotationTypeDD = autoRotDD;
                configuration.Save();
            }

            var autoRotFATE = configuration.AutoRotationTypeFATE;
            if (ImGui.InputText("Auto Rotation Preset (FATE)", ref autoRotFATE, 32))
            {
                configuration.AutoRotationTypeFATE = autoRotFATE;
                configuration.Save();
            }

            var rotType = configuration.RotationType;
            if (ImGui.InputText("Rotation Type (Auto/Manual)", ref rotType, 16))
            {
                configuration.RotationType = rotType;
                configuration.Save();
            }

            var bossModAI = configuration.BossModAI;
            if (ImGui.InputText("BossMod AI (on/off)", ref bossModAI, 8))
            {
                configuration.BossModAI = bossModAI;
                configuration.Save();
            }

            var positional = configuration.PositionalInCombat;
            if (ImGui.InputInt("Positional (0=Front, 1=Rear, 2=Any, 42=Auto)", ref positional))
            {
                configuration.PositionalInCombat = positional;
                configuration.Save();
            }

            var maxAIDist = configuration.MaxAIDistance;
            if (ImGui.InputFloat("Max AI Distance (424242=Auto)", ref maxAIDist))
            {
                configuration.MaxAIDistance = maxAIDist;
                configuration.Save();
            }

            var limitPct = configuration.LimitPct;
            if (ImGui.InputFloat("LB Threshold % (-1=Off)", ref limitPct))
            {
                configuration.LimitPct = limitPct;
                configuration.Save();
            }

            ImGui.EndTabItem();
        }
    }

    private void DrawAutomationTab()
    {
        if (ImGui.BeginTabItem("Automation"))
        {
            ImGui.Spacing();

            ImGui.Text("Food");
            var feedMe = configuration.FeedMe;
            if (ImGui.InputInt("Food Item ID (0=Off)", ref feedMe))
            {
                configuration.FeedMe = feedMe;
                configuration.Save();
            }

            var feedMeItem = configuration.FeedMeItem;
            if (ImGui.InputText("Food Item Name", ref feedMeItem, 64))
            {
                configuration.FeedMeItem = feedMeItem;
                configuration.Save();
            }

            var feedMeSearch = configuration.FeedMeSearch;
            if (ImGui.Checkbox("Search for Food if Depleted", ref feedMeSearch))
            {
                configuration.FeedMeSearch = feedMeSearch;
                configuration.Save();
            }

            ImGui.Separator();
            ImGui.Text("XP / Repair");

            var xpItem = configuration.XpItem;
            if (ImGui.InputInt("XP Item ID (0=Off)", ref xpItem))
            {
                configuration.XpItem = xpItem;
                configuration.Save();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Azyma Earring = 41081. Use SimpleTweaks to see item IDs.");
            }

            var repair = configuration.Repair;
            if (ImGui.InputInt("Repair (0=No, 1=Self, 2=Inn NPC)", ref repair))
            {
                configuration.Repair = repair;
                configuration.Save();
            }

            var tornClothes = configuration.TornClothes;
            if (ImGui.InputInt("Repair At % Durability", ref tornClothes))
            {
                configuration.TornClothes = tornClothes;
                configuration.Save();
            }

            ImGui.Separator();
            ImGui.Text("Idle Behavior");

            var idleShitter = configuration.IdleShitter;
            if (ImGui.InputText("Idle Action", ref idleShitter, 64))
            {
                configuration.IdleShitter = idleShitter;
                configuration.Save();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Options: list, hfh, nothing, or any /slash command");
            }

            var idleShitterTic = configuration.IdleShitterTic;
            if (ImGui.InputInt("Idle Ticks Before Action", ref idleShitterTic))
            {
                configuration.IdleShitterTic = idleShitterTic;
                configuration.Save();
            }

            ImGui.EndTabItem();
        }
    }

    private void DrawMiscTab()
    {
        if (ImGui.BeginTabItem("Misc"))
        {
            ImGui.Spacing();

            var fulfType = configuration.FulfType;
            if (ImGui.InputText("Loot Type (unchanged/need/greed/pass)", ref fulfType, 16))
            {
                configuration.FulfType = fulfType;
                configuration.Save();
            }

            var cbtEdse = configuration.CbtEdse;
            if (ImGui.InputInt("Enhanced Duty Start/End (0=Off, 1=On)", ref cbtEdse))
            {
                configuration.CbtEdse = cbtEdse;
                configuration.Save();
            }

            var spamPrinter = configuration.SpamPrinter;
            if (ImGui.InputInt("Spam Printer (0=Off, 1=On)", ref spamPrinter))
            {
                configuration.SpamPrinter = spamPrinter;
                configuration.Save();
            }

            ImGui.Separator();
            ImGui.Spacing();

            var movable = configuration.IsConfigWindowMovable;
            if (ImGui.Checkbox("Movable Config Window", ref movable))
            {
                configuration.IsConfigWindowMovable = movable;
                configuration.Save();
            }

            ImGui.EndTabItem();
        }
    }
}
