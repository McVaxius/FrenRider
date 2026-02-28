using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;

namespace FrenRider.Services;

public enum ZoneType
{
    Overworld,
    Town,
    Duty,
    DeepDungeon,
    Foray,
    Other,
}

public class ZoneService
{
    // Known deep dungeon territory IDs (PotD, HoH, EO)
    private static readonly HashSet<uint> DeepDungeonIds = new()
    {
        561, 562, 563, 564, 565, // Palace of the Dead
        770, 771, 772, 773,     // Heaven-on-High
        1099, 1100, 1101, 1102, // Eureka Orthos
    };

    // Known foray territory IDs (Eureka, Bozja, Zadnor)
    private static readonly HashSet<uint> ForayIds = new()
    {
        732, 763, 795, 827, // Eureka zones
        920, 975,           // Bozja Southern Front, Zadnor
    };

    public ZoneType CurrentZone { get; private set; }
    public bool IsIndoors { get; private set; }
    public uint TerritoryId { get; private set; }

    public void Update()
    {
        TerritoryId = Plugin.ClientState.TerritoryType;

        if (DeepDungeonIds.Contains(TerritoryId))
        {
            CurrentZone = ZoneType.DeepDungeon;
            IsIndoors = true;
        }
        else if (ForayIds.Contains(TerritoryId))
        {
            CurrentZone = ZoneType.Foray;
            IsIndoors = false;
        }
        else if (Plugin.Condition[ConditionFlag.BoundByDuty] || Plugin.Condition[ConditionFlag.BoundByDuty56])
        {
            CurrentZone = ZoneType.Duty;
            IsIndoors = true;
        }
        else
        {
            CurrentZone = ZoneType.Overworld;
            IsIndoors = false;
        }
    }
}
