using System;
using System.Collections.Generic;
using System.Linq;

namespace FrenRider.Models;

public enum FrenRiderCleanupMode
{
    RestoreSnapshot,
    TurnEverythingOff,
}

public enum DaedalusTargetMode
{
    None = 0,
    Focus = 1,
    Split = 2,
    KillAdds = 3,
}

public enum FrenRiderProfileAcceptancePolicy
{
    Temporary = 0,
    Off = 1,
    Permanent = 2,
}

[Serializable]
public class CharacterConfig
{
    public const string DefaultCustomIdleCommand = "/smile motion";
    internal const int LegacyPreviouslyEngagedRotationType = 4;
    internal const int PreviouslyEngagedRsrAggroType = 1;

    private int respawnOutsideDutiesDelaySeconds = 60;
    private int respawnInsideDutiesDelaySeconds = 60;
    private float mountUpToChaseFrenDistance = 100f;
    private int mountUpToChaseFrenDelaySeconds = 30;
    private int adsSoloHandoffDelaySeconds = 10;
    private int adsFourManHandoffDelaySeconds = 2;
    private int adsEightManHandoffDelaySeconds = 2;
    private int adsAllianceHandoffDelaySeconds = 2;
    private int adsGuildHestHandoffDelaySeconds = 2;
    private int adsDeepDungeonHandoffDelaySeconds = 2;
    private int adsTreasureDungeonHandoffDelaySeconds = 2;
    private int adsOtherHandoffDelaySeconds = 2;

    // --- Party / Friend ---
    public string FrenName { get; set; } = "";
    public bool FlyYouFools { get; set; } = false;
    public bool TryTeleportToFrenWhenOutOfZone { get; set; } = false;
    public int TeleportToFrenDelaySeconds { get; set; } = 30;
    public bool NudgeInDutyWhenFrenNotNearbyOrInZone { get; set; } = false;
    public bool RespawnOutsideDuties { get; set; } = false;
    public int RespawnOutsideDutiesDelaySeconds
    {
        get => respawnOutsideDutiesDelaySeconds;
        set => respawnOutsideDutiesDelaySeconds = Math.Max(1, value);
    }
    public bool RespawnInsideDuties { get; set; } = false;
    public int RespawnInsideDutiesDelaySeconds
    {
        get => respawnInsideDutiesDelaySeconds;
        set => respawnInsideDutiesDelaySeconds = Math.Max(1, value);
    }
    public bool MountUpToChaseFren { get; set; } = false;
    public float MountUpToChaseFrenDistance
    {
        get => mountUpToChaseFrenDistance;
        set => mountUpToChaseFrenDistance = float.IsFinite(value) ? Math.Max(1f, value) : 1f;
    }
    public int MountUpToChaseFrenDelaySeconds
    {
        get => mountUpToChaseFrenDelaySeconds;
        set => mountUpToChaseFrenDelaySeconds = Math.Clamp(value, 0, 300);
    }
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
    public int FollowInCombat { get; set; } = 0; // 0=No, 1=Yes, 2=Auto
    public float FDistance { get; set; } = 0f; // Reserved for future autosync FATE; not applied to follow distance.
    public bool AutoSyncFate { get; set; } = true;
    public bool Formation { get; set; } = false;
    public int HClingReset { get; set; } = 10;

    // --- Combat / AI ---
    public bool ConfigureRotationPresetManually { get; set; } = false;
    public string AutoRotationType { get; set; } = "FRENRIDER";
    public string AutoRotationTypeDD { get; set; } = "DD";
    public string AutoRotationTypeFATE { get; set; } = "FATE";
    public int RotationPlugin { get; set; } = 2; // 0=BMR, 1=VBM, 2=RSR, 3=WRATH, 4=DAEDALUS
    public int RotationPluginForay { get; set; } = 3; // 0=BMR, 1=VBM, 2=RSR, 3=WRATH, 4=DAEDALUS
    public DaedalusTargetMode DaedalusTargetMode { get; set; } =
        global::FrenRider.Models.DaedalusTargetMode.None;
    public bool ForceBossModPresetRegardlessOfRotation { get; set; } = false;
    public int BossModAI { get; set; } = 0; // 0=on, 1=off
    public int PositionalInCombat { get; set; } = 3; // 0=Front, 1=Rear, 2=Any, 3=Auto
    public float MaxAIDistance { get; set; } = 424242f;
    public float LimitPct { get; set; } = -1f;
    public int RotationType { get; set; } = 0; // 0=Auto, 1=Manual, 2=None, 3=Support
    public int RsrAggroType { get; set; } = 0; // Matches RSR TargetHostileType ordering
    public bool BmrReduceActivationRangeForOutdoorAreas { get; set; } = false;
    public bool BmrDisableHuntModules { get; set; } = false;
    public bool BmrDisableQueenLunatender { get; set; } = false;
    public FrenRiderCleanupMode CleanupMode { get; set; } = FrenRiderCleanupMode.RestoreSnapshot;
    public bool UseAdsIfAvailable { get; set; } = false;
    public int AdsMaturityThreshold { get; set; } = 3; // 0=Not Cleared, 1=1P Unsync, 2=1P Duty Support, 3=4P Sync
    public bool AdsDutyFamilySettingsMigrated { get; set; } = false;
    public bool AdsSoloEnabled { get; set; } = false;
    public int AdsSoloMaturityThreshold { get; set; } = 3;
    public int AdsSoloHandoffDelaySeconds
    {
        get => adsSoloHandoffDelaySeconds;
        set => adsSoloHandoffDelaySeconds = Math.Clamp(value, 2, 300);
    }
    public bool AdsFourManEnabled { get; set; } = false;
    public int AdsFourManMaturityThreshold { get; set; } = 3;
    public int AdsFourManHandoffDelaySeconds
    {
        get => adsFourManHandoffDelaySeconds;
        set => adsFourManHandoffDelaySeconds = Math.Clamp(value, 2, 300);
    }
    public bool AdsEightManEnabled { get; set; } = false;
    public int AdsEightManMaturityThreshold { get; set; } = 3;
    public int AdsEightManHandoffDelaySeconds
    {
        get => adsEightManHandoffDelaySeconds;
        set => adsEightManHandoffDelaySeconds = Math.Clamp(value, 2, 300);
    }
    public bool AdsAllianceEnabled { get; set; } = false;
    public int AdsAllianceMaturityThreshold { get; set; } = 3;
    public int AdsAllianceHandoffDelaySeconds
    {
        get => adsAllianceHandoffDelaySeconds;
        set => adsAllianceHandoffDelaySeconds = Math.Clamp(value, 2, 300);
    }
    public bool AdsGuildHestEnabled { get; set; } = false;
    public int AdsGuildHestMaturityThreshold { get; set; } = 3;
    public int AdsGuildHestHandoffDelaySeconds
    {
        get => adsGuildHestHandoffDelaySeconds;
        set => adsGuildHestHandoffDelaySeconds = Math.Clamp(value, 2, 300);
    }
    public bool AdsDeepDungeonEnabled { get; set; } = false;
    public int AdsDeepDungeonMaturityThreshold { get; set; } = 3;
    public int AdsDeepDungeonHandoffDelaySeconds
    {
        get => adsDeepDungeonHandoffDelaySeconds;
        set => adsDeepDungeonHandoffDelaySeconds = Math.Clamp(value, 2, 300);
    }
    public bool AdsTreasureDungeonEnabled { get; set; } = false;
    public int AdsTreasureDungeonMaturityThreshold { get; set; } = 3;
    public int AdsTreasureDungeonHandoffDelaySeconds
    {
        get => adsTreasureDungeonHandoffDelaySeconds;
        set => adsTreasureDungeonHandoffDelaySeconds = Math.Clamp(value, 2, 300);
    }
    public bool AdsOtherEnabled { get; set; } = false;
    public int AdsOtherMaturityThreshold { get; set; } = 3;
    public int AdsOtherHandoffDelaySeconds
    {
        get => adsOtherHandoffDelaySeconds;
        set => adsOtherHandoffDelaySeconds = Math.Clamp(value, 2, 300);
    }
    public bool AdsEnableChestOpening { get; set; } = true;
    public int AdsPresetSelection { get; set; } = 0; // Local stub only until FrenRider can push richer ADS config
    public bool UseAdsLeaveAfterAdsDuty { get; set; } = false;

    // --- Automation / Misc ---
    public bool EnableAutoDiscard { get; set; } = false;
    public bool EquipJobStoneForCurrentClass { get; set; } = false;
    public int FeedMeItemId { get; set; } = 0;
    public string FeedMeItem { get; set; } = "Boiled Egg";
    public bool FeedMeUseHighQuality { get; set; } = false;
    public bool FeedMeSearch { get; set; } = true;
    public int XpItem { get; set; } = 0;
    public int Repair { get; set; } = 0; // 0=Disabled, 1=Self, 2=NPC no-inn
    public int TornClothes { get; set; } = 75;
    public bool EnableAutoDesynth { get; set; } = false;
    public int SpamPrinter { get; set; } = 0; // 0=off, 1=on
    public bool DebugMode { get; set; } = false;

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
    public FrenRiderProfileAcceptancePolicy ProfileAcceptancePolicy { get; set; } =
        FrenRiderProfileAcceptancePolicy.Temporary;
    public bool Enabled { get; set; } = false;

    public CharacterConfig Clone()
    {
        return new CharacterConfig
        {
            FrenName = FrenName,
            FlyYouFools = FlyYouFools,
            TryTeleportToFrenWhenOutOfZone = TryTeleportToFrenWhenOutOfZone,
            TeleportToFrenDelaySeconds = TeleportToFrenDelaySeconds,
            NudgeInDutyWhenFrenNotNearbyOrInZone = NudgeInDutyWhenFrenNotNearbyOrInZone,
            RespawnOutsideDuties = RespawnOutsideDuties,
            RespawnOutsideDutiesDelaySeconds = RespawnOutsideDutiesDelaySeconds,
            RespawnInsideDuties = RespawnInsideDuties,
            RespawnInsideDutiesDelaySeconds = RespawnInsideDutiesDelaySeconds,
            MountUpToChaseFren = MountUpToChaseFren,
            MountUpToChaseFrenDistance = MountUpToChaseFrenDistance,
            MountUpToChaseFrenDelaySeconds = MountUpToChaseFrenDelaySeconds,
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
            DaedalusTargetMode = DaedalusTargetMode,
            ForceBossModPresetRegardlessOfRotation = ForceBossModPresetRegardlessOfRotation,
            BossModAI = BossModAI,
            PositionalInCombat = PositionalInCombat,
            MaxAIDistance = MaxAIDistance,
            LimitPct = LimitPct,
            RotationType = RotationType,
            RsrAggroType = RsrAggroType,
            BmrReduceActivationRangeForOutdoorAreas = BmrReduceActivationRangeForOutdoorAreas,
            BmrDisableHuntModules = BmrDisableHuntModules,
            BmrDisableQueenLunatender = BmrDisableQueenLunatender,
            CleanupMode = CleanupMode,
            UseAdsIfAvailable = UseAdsIfAvailable,
            AdsMaturityThreshold = AdsMaturityThreshold,
            AdsDutyFamilySettingsMigrated = AdsDutyFamilySettingsMigrated,
            AdsSoloEnabled = AdsSoloEnabled,
            AdsSoloMaturityThreshold = AdsSoloMaturityThreshold,
            AdsSoloHandoffDelaySeconds = AdsSoloHandoffDelaySeconds,
            AdsFourManEnabled = AdsFourManEnabled,
            AdsFourManMaturityThreshold = AdsFourManMaturityThreshold,
            AdsFourManHandoffDelaySeconds = AdsFourManHandoffDelaySeconds,
            AdsEightManEnabled = AdsEightManEnabled,
            AdsEightManMaturityThreshold = AdsEightManMaturityThreshold,
            AdsEightManHandoffDelaySeconds = AdsEightManHandoffDelaySeconds,
            AdsAllianceEnabled = AdsAllianceEnabled,
            AdsAllianceMaturityThreshold = AdsAllianceMaturityThreshold,
            AdsAllianceHandoffDelaySeconds = AdsAllianceHandoffDelaySeconds,
            AdsGuildHestEnabled = AdsGuildHestEnabled,
            AdsGuildHestMaturityThreshold = AdsGuildHestMaturityThreshold,
            AdsGuildHestHandoffDelaySeconds = AdsGuildHestHandoffDelaySeconds,
            AdsDeepDungeonEnabled = AdsDeepDungeonEnabled,
            AdsDeepDungeonMaturityThreshold = AdsDeepDungeonMaturityThreshold,
            AdsDeepDungeonHandoffDelaySeconds = AdsDeepDungeonHandoffDelaySeconds,
            AdsTreasureDungeonEnabled = AdsTreasureDungeonEnabled,
            AdsTreasureDungeonMaturityThreshold = AdsTreasureDungeonMaturityThreshold,
            AdsTreasureDungeonHandoffDelaySeconds = AdsTreasureDungeonHandoffDelaySeconds,
            AdsOtherEnabled = AdsOtherEnabled,
            AdsOtherMaturityThreshold = AdsOtherMaturityThreshold,
            AdsOtherHandoffDelaySeconds = AdsOtherHandoffDelaySeconds,
            AdsEnableChestOpening = AdsEnableChestOpening,
            AdsPresetSelection = AdsPresetSelection,
            UseAdsLeaveAfterAdsDuty = UseAdsLeaveAfterAdsDuty,
            EnableAutoDiscard = EnableAutoDiscard,
            EquipJobStoneForCurrentClass = EquipJobStoneForCurrentClass,
            FeedMeItemId = FeedMeItemId,
            FeedMeItem = FeedMeItem,
            FeedMeUseHighQuality = FeedMeUseHighQuality,
            FeedMeSearch = FeedMeSearch,
            XpItem = XpItem,
            Repair = Repair,
            TornClothes = TornClothes,
            EnableAutoDesynth = EnableAutoDesynth,
            SpamPrinter = SpamPrinter,
            DebugMode = DebugMode,
            InviteWhitelist = new List<string>(InviteWhitelist),
            RaiseOfferAutoAccept = RaiseOfferAutoAccept,
            TeleportOfferAutoAccept = TeleportOfferAutoAccept,
            PartyInviteAutoAccept = PartyInviteAutoAccept,
            ExitAfterDutyEnds = ExitAfterDutyEnds,
            ExitAfterDutySeconds = ExitAfterDutySeconds,
            LeaveWhenAllLeft = LeaveWhenAllLeft,
            AutorotPushOnEnable = AutorotPushOnEnable,
            ProfileAcceptancePolicy = ProfileAcceptancePolicy,
            Enabled = Enabled,
        };
    }

    internal bool MigrateLegacyRsrOperatingMode()
    {
        if (RotationType != LegacyPreviouslyEngagedRotationType)
            return false;

        RotationType = 0;
        RsrAggroType = PreviouslyEngagedRsrAggroType;
        return true;
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
        {
            return new AdsDutyFamilySettings(
                UseAdsIfAvailable,
                Math.Clamp(AdsMaturityThreshold, 0, 3),
                GetDefaultAdsHandoffDelaySeconds(category));
        }

        return category switch
        {
            AdsDutyCategory.Solo => new AdsDutyFamilySettings(AdsSoloEnabled, Math.Clamp(AdsSoloMaturityThreshold, 0, 3), AdsSoloHandoffDelaySeconds),
            AdsDutyCategory.FourMan => new AdsDutyFamilySettings(AdsFourManEnabled, Math.Clamp(AdsFourManMaturityThreshold, 0, 3), AdsFourManHandoffDelaySeconds),
            AdsDutyCategory.EightMan => new AdsDutyFamilySettings(AdsEightManEnabled, Math.Clamp(AdsEightManMaturityThreshold, 0, 3), AdsEightManHandoffDelaySeconds),
            AdsDutyCategory.Alliance => new AdsDutyFamilySettings(AdsAllianceEnabled, Math.Clamp(AdsAllianceMaturityThreshold, 0, 3), AdsAllianceHandoffDelaySeconds),
            AdsDutyCategory.GuildHest => new AdsDutyFamilySettings(AdsGuildHestEnabled, Math.Clamp(AdsGuildHestMaturityThreshold, 0, 3), AdsGuildHestHandoffDelaySeconds),
            AdsDutyCategory.DeepDungeon => new AdsDutyFamilySettings(AdsDeepDungeonEnabled, Math.Clamp(AdsDeepDungeonMaturityThreshold, 0, 3), AdsDeepDungeonHandoffDelaySeconds),
            AdsDutyCategory.TreasureDungeon => new AdsDutyFamilySettings(AdsTreasureDungeonEnabled, Math.Clamp(AdsTreasureDungeonMaturityThreshold, 0, 3), AdsTreasureDungeonHandoffDelaySeconds),
            _ => new AdsDutyFamilySettings(AdsOtherEnabled, Math.Clamp(AdsOtherMaturityThreshold, 0, 3), AdsOtherHandoffDelaySeconds),
        };
    }

    public void SetAdsDutyFamilySettings(
        AdsDutyCategory category,
        bool enabled,
        int maturityThreshold,
        int handoffDelaySeconds)
    {
        EnsureAdsDutyFamilySettingsInitialized();
        var clampedThreshold = Math.Clamp(maturityThreshold, 0, 3);
        var clampedDelay = Math.Clamp(handoffDelaySeconds, 2, 300);
        switch (category)
        {
            case AdsDutyCategory.Solo:
                AdsSoloEnabled = enabled;
                AdsSoloMaturityThreshold = clampedThreshold;
                AdsSoloHandoffDelaySeconds = clampedDelay;
                break;
            case AdsDutyCategory.FourMan:
                AdsFourManEnabled = enabled;
                AdsFourManMaturityThreshold = clampedThreshold;
                AdsFourManHandoffDelaySeconds = clampedDelay;
                break;
            case AdsDutyCategory.EightMan:
                AdsEightManEnabled = enabled;
                AdsEightManMaturityThreshold = clampedThreshold;
                AdsEightManHandoffDelaySeconds = clampedDelay;
                break;
            case AdsDutyCategory.Alliance:
                AdsAllianceEnabled = enabled;
                AdsAllianceMaturityThreshold = clampedThreshold;
                AdsAllianceHandoffDelaySeconds = clampedDelay;
                break;
            case AdsDutyCategory.GuildHest:
                AdsGuildHestEnabled = enabled;
                AdsGuildHestMaturityThreshold = clampedThreshold;
                AdsGuildHestHandoffDelaySeconds = clampedDelay;
                break;
            case AdsDutyCategory.DeepDungeon:
                AdsDeepDungeonEnabled = enabled;
                AdsDeepDungeonMaturityThreshold = clampedThreshold;
                AdsDeepDungeonHandoffDelaySeconds = clampedDelay;
                break;
            case AdsDutyCategory.TreasureDungeon:
                AdsTreasureDungeonEnabled = enabled;
                AdsTreasureDungeonMaturityThreshold = clampedThreshold;
                AdsTreasureDungeonHandoffDelaySeconds = clampedDelay;
                break;
            default:
                AdsOtherEnabled = enabled;
                AdsOtherMaturityThreshold = clampedThreshold;
                AdsOtherHandoffDelaySeconds = clampedDelay;
                break;
        }
    }

    private static int GetDefaultAdsHandoffDelaySeconds(AdsDutyCategory category)
        => category == AdsDutyCategory.Solo ? 10 : 2;

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
