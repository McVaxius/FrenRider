using System;
using System.Collections.Generic;
using System.Linq;

namespace FrenRider.Models;

[Serializable]
public class CharacterConfig
{
    public const string DefaultCustomIdleCommand = "/smile motion";

    // --- Party / Friend ---
    public string FrenName { get; set; } = "";
    public bool FlyYouFools { get; set; } = false;
    public string FoolFlier { get; set; } = "Company Chocobo";
    public string FulfType { get; set; } = "unchanged";
    public bool ForceGysahl { get; set; } = false;
    public string CompanionStrat { get; set; } = "Free Stance";
    public float UpdateInterval { get; set; } = 0.3f;
    public string IdleAction { get; set; } = "/tomescroll";
    public int IdleActionMode { get; set; } = 0; // 0 = specific action, 1 = action from list
    public int IdleListMode { get; set; } = 0; // 0 = default list, 1 = custom list
    public string[] CustomIdleList { get; set; } = new[] { DefaultCustomIdleCommand };
    public int IdleTicksBeforeAction { get; set; } = 10;

    // --- Distance / Following ---
    public float Cling { get; set; } = 2.6f;
    public int ClingType { get; set; } = 0; // 0=NavMesh, 1=Visland, 2=BossMod, 3=Vanilla
    public int ClingTypeDuty { get; set; } = 0;
    public float SocialDistancing { get; set; } = 5f;
    public int SocialDistancingIndoors { get; set; } = 0;
    public float SocialDistanceXWiggle { get; set; } = 1f;
    public float SocialDistanceZWiggle { get; set; } = 1f;
    public float MaxBistance { get; set; } = 500f;
    public float MaxBistanceForay { get; set; } = 100f;
    public float DDDistance { get; set; } = 100f;
    public int FollowInCombat { get; set; } = 2; // 0=No, 1=Yes, 2=Auto
    public float FDistance { get; set; } = 0f; // Reserved for future autosync FATE; not applied to follow distance.
    public bool AutoSyncFate { get; set; } = true;
    public bool Formation { get; set; } = false;
    public int HClingReset { get; set; } = 10;

    // --- Combat / AI ---
    public bool ConfigureRotationPresetManually { get; set; } = false;
    public string AutoRotationType { get; set; } = "FRENRIDER";
    public string AutoRotationTypeDD { get; set; } = "DD";
    public string AutoRotationTypeFATE { get; set; } = "FATE";
    public int RotationPlugin { get; set; } = 2; // 0=BMR, 1=VBM, 2=RSR, 3=WRATH
    public int RotationPluginForay { get; set; } = 3; // 0=BMR, 1=VBM, 2=RSR, 3=WRATH
    public bool ForceBossModPresetRegardlessOfRotation { get; set; } = false;
    public int BossModAI { get; set; } = 0; // 0=on, 1=off
    public int PositionalInCombat { get; set; } = 3; // 0=Front, 1=Rear, 2=Any, 3=Auto
    public float MaxAIDistance { get; set; } = 424242f;
    public float LimitPct { get; set; } = -1f;
    public int RotationType { get; set; } = 0; // 0=Auto, 1=Manual, 2=none, 3=Auto (Support), 4=Previously Engaged Targets
    public bool UseAdsIfAvailable { get; set; } = false;
    public int AdsMaturityThreshold { get; set; } = 3; // 0=Not Cleared, 1=1P Unsync, 2=1P Duty Support, 3=4P Sync
    public bool AdsDutyFamilySettingsMigrated { get; set; } = false;
    public bool AdsSoloEnabled { get; set; } = false;
    public int AdsSoloMaturityThreshold { get; set; } = 3;
    public bool AdsFourManEnabled { get; set; } = false;
    public int AdsFourManMaturityThreshold { get; set; } = 3;
    public bool AdsEightManEnabled { get; set; } = false;
    public int AdsEightManMaturityThreshold { get; set; } = 3;
    public bool AdsAllianceEnabled { get; set; } = false;
    public int AdsAllianceMaturityThreshold { get; set; } = 3;
    public bool AdsGuildHestEnabled { get; set; } = false;
    public int AdsGuildHestMaturityThreshold { get; set; } = 3;
    public bool AdsDeepDungeonEnabled { get; set; } = false;
    public int AdsDeepDungeonMaturityThreshold { get; set; } = 3;
    public bool AdsTreasureDungeonEnabled { get; set; } = false;
    public int AdsTreasureDungeonMaturityThreshold { get; set; } = 3;
    public bool AdsOtherEnabled { get; set; } = false;
    public int AdsOtherMaturityThreshold { get; set; } = 3;
    public bool AdsEnableChestOpening { get; set; } = true;
    public int AdsPresetSelection { get; set; } = 0; // Local stub only until FrenRider can push richer ADS config
    public bool UseAdsLeaveAfterAdsDuty { get; set; } = false;

    // --- Automation / Misc ---
    public bool EnableAutoDiscard { get; set; } = false;
    public string FeedMeItem { get; set; } = "Boiled Egg";
    public bool FeedMeSearch { get; set; } = true;
    public int XpItem { get; set; } = 0;
    public int Repair { get; set; } = 0; // 0=No, 1=Self, 2=Inn NPC
    public int TornClothes { get; set; } = 0;
    public int SpamPrinter { get; set; } = 0; // 0=off, 1=on

    // --- Invite Whitelist ---
    public List<string> InviteWhitelist { get; set; } = new();

    // --- Auto-Yes Dialogs ---
    public bool RaiseOfferAutoAccept { get; set; } = true;
    public bool TeleportOfferAutoAccept { get; set; } = true;
    public bool PartyInviteAutoAccept { get; set; } = true;

    // --- Exit Behaviour ---
    public bool ExitAfterDutyEnds { get; set; } = true;
    public int ExitAfterDutySeconds { get; set; } = 20;
    public bool LeaveWhenAllLeft { get; set; } = false;

    // --- Autorot IPC ---
    public bool AutorotPushOnEnable { get; set; } = true;

    // --- Plugin State ---
    public bool Enabled { get; set; } = false;

    public CharacterConfig Clone()
    {
        return new CharacterConfig
        {
            FrenName = FrenName,
            FlyYouFools = FlyYouFools,
            FoolFlier = FoolFlier,
            FulfType = FulfType,
            ForceGysahl = ForceGysahl,
            CompanionStrat = CompanionStrat,
            UpdateInterval = UpdateInterval,
            IdleAction = IdleAction,
            IdleActionMode = IdleActionMode,
            IdleListMode = IdleListMode,
            CustomIdleList = CloneCustomIdleList(CustomIdleList),
            IdleTicksBeforeAction = IdleTicksBeforeAction,
            Cling = Cling,
            ClingType = ClingType,
            ClingTypeDuty = ClingTypeDuty,
            SocialDistancing = SocialDistancing,
            SocialDistancingIndoors = SocialDistancingIndoors,
            SocialDistanceXWiggle = SocialDistanceXWiggle,
            SocialDistanceZWiggle = SocialDistanceZWiggle,
            MaxBistance = MaxBistance,
            MaxBistanceForay = MaxBistanceForay,
            DDDistance = DDDistance,
            FollowInCombat = FollowInCombat,
            FDistance = FDistance,
            AutoSyncFate = AutoSyncFate,
            Formation = Formation,
            HClingReset = HClingReset,
            ConfigureRotationPresetManually = ConfigureRotationPresetManually,
            AutoRotationType = AutoRotationType,
            AutoRotationTypeDD = AutoRotationTypeDD,
            AutoRotationTypeFATE = AutoRotationTypeFATE,
            RotationPlugin = RotationPlugin,
            RotationPluginForay = RotationPluginForay,
            ForceBossModPresetRegardlessOfRotation = ForceBossModPresetRegardlessOfRotation,
            BossModAI = BossModAI,
            PositionalInCombat = PositionalInCombat,
            MaxAIDistance = MaxAIDistance,
            LimitPct = LimitPct,
            RotationType = RotationType,
            UseAdsIfAvailable = UseAdsIfAvailable,
            AdsMaturityThreshold = AdsMaturityThreshold,
            AdsDutyFamilySettingsMigrated = AdsDutyFamilySettingsMigrated,
            AdsSoloEnabled = AdsSoloEnabled,
            AdsSoloMaturityThreshold = AdsSoloMaturityThreshold,
            AdsFourManEnabled = AdsFourManEnabled,
            AdsFourManMaturityThreshold = AdsFourManMaturityThreshold,
            AdsEightManEnabled = AdsEightManEnabled,
            AdsEightManMaturityThreshold = AdsEightManMaturityThreshold,
            AdsAllianceEnabled = AdsAllianceEnabled,
            AdsAllianceMaturityThreshold = AdsAllianceMaturityThreshold,
            AdsGuildHestEnabled = AdsGuildHestEnabled,
            AdsGuildHestMaturityThreshold = AdsGuildHestMaturityThreshold,
            AdsDeepDungeonEnabled = AdsDeepDungeonEnabled,
            AdsDeepDungeonMaturityThreshold = AdsDeepDungeonMaturityThreshold,
            AdsTreasureDungeonEnabled = AdsTreasureDungeonEnabled,
            AdsTreasureDungeonMaturityThreshold = AdsTreasureDungeonMaturityThreshold,
            AdsOtherEnabled = AdsOtherEnabled,
            AdsOtherMaturityThreshold = AdsOtherMaturityThreshold,
            AdsEnableChestOpening = AdsEnableChestOpening,
            AdsPresetSelection = AdsPresetSelection,
            UseAdsLeaveAfterAdsDuty = UseAdsLeaveAfterAdsDuty,
            EnableAutoDiscard = EnableAutoDiscard,
            FeedMeItem = FeedMeItem,
            FeedMeSearch = FeedMeSearch,
            XpItem = XpItem,
            Repair = Repair,
            TornClothes = TornClothes,
            SpamPrinter = SpamPrinter,
            InviteWhitelist = new List<string>(InviteWhitelist),
            RaiseOfferAutoAccept = RaiseOfferAutoAccept,
            TeleportOfferAutoAccept = TeleportOfferAutoAccept,
            PartyInviteAutoAccept = PartyInviteAutoAccept,
            ExitAfterDutyEnds = ExitAfterDutyEnds,
            ExitAfterDutySeconds = ExitAfterDutySeconds,
            LeaveWhenAllLeft = LeaveWhenAllLeft,
            AutorotPushOnEnable = AutorotPushOnEnable,
            Enabled = Enabled,
        };
    }

    public bool EnsureCustomIdleListSeeded()
    {
        if (CustomIdleList != null && CustomIdleList.Length > 0)
            return false;

        CustomIdleList = new[] { DefaultCustomIdleCommand };
        return true;
    }

    public static string[] CloneCustomIdleList(string[]? customIdleList)
    {
        return customIdleList?.ToArray() ?? new[] { DefaultCustomIdleCommand };
    }

    public static string[] GetExecutableCustomIdleCommands(string[]? customIdleList)
    {
        var commands = customIdleList?
            .Where(command => !string.IsNullOrWhiteSpace(command))
            .Select(command => command.Trim())
            .ToArray() ?? Array.Empty<string>();

        return commands.Length > 0
            ? commands
            : new[] { DefaultCustomIdleCommand };
    }

    public void EnsureAdsDutyFamilySettingsInitialized()
    {
        if (AdsDutyFamilySettingsMigrated)
            return;

        AdsSoloEnabled = UseAdsIfAvailable;
        AdsSoloMaturityThreshold = AdsMaturityThreshold;
        AdsFourManEnabled = UseAdsIfAvailable;
        AdsFourManMaturityThreshold = AdsMaturityThreshold;
        AdsEightManEnabled = UseAdsIfAvailable;
        AdsEightManMaturityThreshold = AdsMaturityThreshold;
        AdsAllianceEnabled = UseAdsIfAvailable;
        AdsAllianceMaturityThreshold = AdsMaturityThreshold;
        AdsGuildHestEnabled = UseAdsIfAvailable;
        AdsGuildHestMaturityThreshold = AdsMaturityThreshold;
        AdsDeepDungeonEnabled = UseAdsIfAvailable;
        AdsDeepDungeonMaturityThreshold = AdsMaturityThreshold;
        AdsTreasureDungeonEnabled = UseAdsIfAvailable;
        AdsTreasureDungeonMaturityThreshold = AdsMaturityThreshold;
        AdsOtherEnabled = UseAdsIfAvailable;
        AdsOtherMaturityThreshold = AdsMaturityThreshold;
        AdsDutyFamilySettingsMigrated = true;
    }

    public AdsDutyFamilySettings GetAdsDutyFamilySettings(AdsDutyCategory category)
    {
        if (!AdsDutyFamilySettingsMigrated)
            return new AdsDutyFamilySettings(UseAdsIfAvailable, Math.Clamp(AdsMaturityThreshold, 0, 3));

        return category switch
        {
            AdsDutyCategory.Solo => new AdsDutyFamilySettings(AdsSoloEnabled, Math.Clamp(AdsSoloMaturityThreshold, 0, 3)),
            AdsDutyCategory.FourMan => new AdsDutyFamilySettings(AdsFourManEnabled, Math.Clamp(AdsFourManMaturityThreshold, 0, 3)),
            AdsDutyCategory.EightMan => new AdsDutyFamilySettings(AdsEightManEnabled, Math.Clamp(AdsEightManMaturityThreshold, 0, 3)),
            AdsDutyCategory.Alliance => new AdsDutyFamilySettings(AdsAllianceEnabled, Math.Clamp(AdsAllianceMaturityThreshold, 0, 3)),
            AdsDutyCategory.GuildHest => new AdsDutyFamilySettings(AdsGuildHestEnabled, Math.Clamp(AdsGuildHestMaturityThreshold, 0, 3)),
            AdsDutyCategory.DeepDungeon => new AdsDutyFamilySettings(AdsDeepDungeonEnabled, Math.Clamp(AdsDeepDungeonMaturityThreshold, 0, 3)),
            AdsDutyCategory.TreasureDungeon => new AdsDutyFamilySettings(AdsTreasureDungeonEnabled, Math.Clamp(AdsTreasureDungeonMaturityThreshold, 0, 3)),
            _ => new AdsDutyFamilySettings(AdsOtherEnabled, Math.Clamp(AdsOtherMaturityThreshold, 0, 3)),
        };
    }

    public void SetAdsDutyFamilySettings(AdsDutyCategory category, bool enabled, int maturityThreshold)
    {
        EnsureAdsDutyFamilySettingsInitialized();
        var clampedThreshold = Math.Clamp(maturityThreshold, 0, 3);
        switch (category)
        {
            case AdsDutyCategory.Solo:
                AdsSoloEnabled = enabled;
                AdsSoloMaturityThreshold = clampedThreshold;
                break;
            case AdsDutyCategory.FourMan:
                AdsFourManEnabled = enabled;
                AdsFourManMaturityThreshold = clampedThreshold;
                break;
            case AdsDutyCategory.EightMan:
                AdsEightManEnabled = enabled;
                AdsEightManMaturityThreshold = clampedThreshold;
                break;
            case AdsDutyCategory.Alliance:
                AdsAllianceEnabled = enabled;
                AdsAllianceMaturityThreshold = clampedThreshold;
                break;
            case AdsDutyCategory.GuildHest:
                AdsGuildHestEnabled = enabled;
                AdsGuildHestMaturityThreshold = clampedThreshold;
                break;
            case AdsDutyCategory.DeepDungeon:
                AdsDeepDungeonEnabled = enabled;
                AdsDeepDungeonMaturityThreshold = clampedThreshold;
                break;
            case AdsDutyCategory.TreasureDungeon:
                AdsTreasureDungeonEnabled = enabled;
                AdsTreasureDungeonMaturityThreshold = clampedThreshold;
                break;
            default:
                AdsOtherEnabled = enabled;
                AdsOtherMaturityThreshold = clampedThreshold;
                break;
        }
    }

    public bool NormalizeExitMethodSelection()
    {
        var changed = false;

        if (UseAdsLeaveAfterAdsDuty)
        {
            if (ExitAfterDutyEnds)
            {
                ExitAfterDutyEnds = false;
                changed = true;
            }

            if (LeaveWhenAllLeft)
            {
                LeaveWhenAllLeft = false;
                changed = true;
            }

            return changed;
        }

        if (ExitAfterDutyEnds && LeaveWhenAllLeft)
        {
            LeaveWhenAllLeft = false;
            changed = true;
        }

        return changed;
    }
}
