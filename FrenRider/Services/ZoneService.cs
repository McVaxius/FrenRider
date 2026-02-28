using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;

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

    private uint lastTerritoryId;

    public ZoneType CurrentZone { get; private set; }
    public bool IsIndoors { get; private set; }
    public uint TerritoryId { get; private set; }
    public bool InFate { get; private set; }
    public ushort CurrentFateId { get; private set; }
    public bool ZoneChanged { get; private set; }

    public void Update()
    {
        TerritoryId = Plugin.ClientState.TerritoryType;

        // Detect zone transitions
        ZoneChanged = TerritoryId != lastTerritoryId && lastTerritoryId != 0;
        if (ZoneChanged)
            Plugin.Log.Information($"Zone changed: {lastTerritoryId} → {TerritoryId}");
        lastTerritoryId = TerritoryId;

        // Zone type detection
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

        // FATE detection via FFXIVClientStructs
        UpdateFateStatus();
    }

    private void UpdateFateStatus()
    {
        try
        {
            unsafe
            {
                var fm = FateManager.Instance();
                if (fm != null && fm->FateJoined != 0 && fm->CurrentFate != null)
                {
                    var fateId = fm->GetCurrentFateId();
                    if (fateId != 0)
                    {
                        if (!InFate)
                            Plugin.Log.Information($"Joined FATE (ID {fateId})");
                        InFate = true;
                        CurrentFateId = fateId;
                        return;
                    }
                }
            }
        }
        catch { /* FateManager access failed */ }

        if (InFate)
            Plugin.Log.Information("Left FATE");
        InFate = false;
        CurrentFateId = 0;
    }
}
