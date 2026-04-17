using System;
using System.Collections.Generic;
using System.Linq;

namespace FrenRider.Models;

[Serializable]
public class CharacterConfig
{
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
    public string[] CustomIdleList { get; set; } = Array.Empty<string>();
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
    public float FDistance { get; set; } = 0f;
    public bool Formation { get; set; } = false;
    public int HClingReset { get; set; } = 10;

    // --- Combat / AI ---
    public string AutoRotationType { get; set; } = "FRENRIDER";
    public string AutoRotationTypeDD { get; set; } = "DD";
    public string AutoRotationTypeFATE { get; set; } = "FATE";
    public int RotationPlugin { get; set; } = 2; // 0=BMR, 1=VBM, 2=RSR, 3=WRATH
    public int RotationPluginForay { get; set; } = 3; // 0=BMR, 1=VBM, 2=RSR, 3=WRATH
    public int BossModAI { get; set; } = 0; // 0=on, 1=off
    public int PositionalInCombat { get; set; } = 3; // 0=Front, 1=Rear, 2=Any, 3=Auto
    public float MaxAIDistance { get; set; } = 424242f;
    public float LimitPct { get; set; } = -1f;
    public int RotationType { get; set; } = 0; // 0=Auto, 1=Manual, 2=none, 3=Auto (Support), 4=Previously Engaged Targets
    public bool UseAdsIfAvailable { get; set; } = false;
    public int AdsMaturityThreshold { get; set; } = 3; // 0=Not Cleared, 1=1P Unsync, 2=1P Duty Support, 3=4P Sync
    public bool AdsEnableChestOpening { get; set; } = true;
    public int AdsPresetSelection { get; set; } = 0; // Local stub only until FrenRider can push richer ADS config

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
            CustomIdleList = (string[])CustomIdleList.Clone(),
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
            Formation = Formation,
            HClingReset = HClingReset,
            AutoRotationType = AutoRotationType,
            AutoRotationTypeDD = AutoRotationTypeDD,
            AutoRotationTypeFATE = AutoRotationTypeFATE,
            RotationPlugin = RotationPlugin,
            RotationPluginForay = RotationPluginForay,
            BossModAI = BossModAI,
            PositionalInCombat = PositionalInCombat,
            MaxAIDistance = MaxAIDistance,
            LimitPct = LimitPct,
            RotationType = RotationType,
            UseAdsIfAvailable = UseAdsIfAvailable,
            AdsMaturityThreshold = AdsMaturityThreshold,
            AdsEnableChestOpening = AdsEnableChestOpening,
            AdsPresetSelection = AdsPresetSelection,
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
}
