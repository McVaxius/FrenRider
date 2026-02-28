using Dalamud.Configuration;
using System;

namespace FrenRider;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;

    // --- Party / Friend ---
    public string FrenName { get; set; } = "Fren Name";
    public bool FlyYouFools { get; set; } = false;
    public string FoolFlier { get; set; } = "Company Chocobo";
    public string FulfType { get; set; } = "unchanged";
    public bool ForceGysahl { get; set; } = false;
    public string CompanionStrat { get; set; } = "Free Stance";
    public float TimeFriction { get; set; } = 0.3f;
    public string IdleShitter { get; set; } = "/tomescroll";
    public int IdleShitterTic { get; set; } = 10;

    // --- Distance / Following ---
    public float Cling { get; set; } = 2.6f;
    public int ClingType { get; set; } = 0;
    public int ClingTypeDuty { get; set; } = 0;
    public float SocialDistancing { get; set; } = 5f;
    public int SocialDistancingIndoors { get; set; } = 0;
    public float SocialDistanceXWiggle { get; set; } = 1f;
    public float SocialDistanceZWiggle { get; set; } = 1f;
    public float MaxBistance { get; set; } = 500f;
    public float MaxBistanceForay { get; set; } = 100f;
    public float DDDistance { get; set; } = 100f;
    public int FollowInCombat { get; set; } = 42;
    public float FDistance { get; set; } = 0f;
    public bool Formation { get; set; } = false;
    public int HClingReset { get; set; } = 10;

    // --- Combat / AI ---
    public string AutoRotationType { get; set; } = "FRENRIDER";
    public string AutoRotationTypeDD { get; set; } = "DD";
    public string AutoRotationTypeFATE { get; set; } = "FATE";
    public string RotationType { get; set; } = "Auto";
    public string BossModAI { get; set; } = "on";
    public int PositionalInCombat { get; set; } = 42;
    public float MaxAIDistance { get; set; } = 424242f;
    public float LimitPct { get; set; } = -1f;
    public string RotationPlugin { get; set; } = "RSR";
    public string RotationPluginForay { get; set; } = "WRATH";

    // --- Exp / Food / Repair ---
    public int XpItem { get; set; } = 0;
    public int Repair { get; set; } = 0;
    public int TornClothes { get; set; } = 0;
    public int FeedMe { get; set; } = 4650;
    public string FeedMeItem { get; set; } = "Boiled Egg";
    public bool FeedMeSearch { get; set; } = true;

    // --- Misc ---
    public int CbtEdse { get; set; } = 0;
    public int SpamPrinter { get; set; } = 1;

    // --- Plugin State (not from original script) ---
    public bool Enabled { get; set; } = false;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
