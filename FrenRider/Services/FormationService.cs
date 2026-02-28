using System;
using System.Numerics;
using FrenRider.Models;

namespace FrenRider.Services;

public class FormationService
{
    private readonly Plugin plugin;
    private readonly FrenTracker tracker;

    // 8-slot formation grid offsets (relative to fren, facing fren's direction)
    // Slot 0 = directly behind, then fan out left/right
    private static readonly Vector3[] FormationOffsets = new Vector3[]
    {
        new( 0.0f, 0, -2.0f),  // Slot 0: directly behind
        new(-2.0f, 0, -2.0f),  // Slot 1: behind-left
        new( 2.0f, 0, -2.0f),  // Slot 2: behind-right
        new(-4.0f, 0, -2.0f),  // Slot 3: far behind-left
        new( 4.0f, 0, -2.0f),  // Slot 4: far behind-right
        new(-1.0f, 0, -4.0f),  // Slot 5: second row left
        new( 1.0f, 0, -4.0f),  // Slot 6: second row right
        new( 0.0f, 0, -4.0f),  // Slot 7: second row center
    };

    public bool IsActive { get; private set; }
    public Vector3 FormationTarget { get; private set; }
    public int AssignedSlot { get; private set; } = -1;

    public FormationService(Plugin plugin, FrenTracker tracker)
    {
        this.plugin = plugin;
        this.tracker = tracker;
    }

    public void Update()
    {
        var config = plugin.ConfigManager.GetActiveConfig();

        if (!config.Enabled || !config.Formation)
        {
            IsActive = false;
            AssignedSlot = -1;
            return;
        }

        var fren = tracker.Fren;
        if (fren == null || !fren.IsFound || !fren.IsVisible)
        {
            IsActive = false;
            return;
        }

        // Determine our slot in the party
        AssignedSlot = DetermineSlot();
        if (AssignedSlot < 0 || AssignedSlot >= FormationOffsets.Length)
        {
            IsActive = false;
            return;
        }

        IsActive = true;
        FormationTarget = CalculateFormationPosition(fren.Position, AssignedSlot);
    }

    /// <summary>
    /// Determines the local player's slot in the formation based on party index.
    /// Returns -1 if not in party or fren not found.
    /// </summary>
    private int DetermineSlot()
    {
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null) return -1;

        var localName = localPlayer.Name.ToString();
        var slot = 0;

        foreach (var member in tracker.Party)
        {
            // Skip fren — they're the anchor
            if (tracker.Fren != null && member.Name == tracker.Fren.Name)
                continue;

            if (member.Name == localName)
                return slot;

            slot++;
        }

        return -1;
    }

    /// <summary>
    /// Calculates the world-space formation position for a given slot,
    /// offset from the fren's position.
    /// </summary>
    private static Vector3 CalculateFormationPosition(Vector3 frenPos, int slot)
    {
        if (slot < 0 || slot >= FormationOffsets.Length)
            return frenPos;

        var offset = FormationOffsets[slot];

        // Simple offset (no rotation — formation is axis-aligned)
        // Future: rotate offsets based on fren's facing direction
        return new Vector3(
            frenPos.X + offset.X,
            frenPos.Y + offset.Y,
            frenPos.Z + offset.Z
        );
    }

    /// <summary>
    /// Gets the formation target position. Used by FollowService when formation mode is on.
    /// Returns null if formation is not active.
    /// </summary>
    public Vector3? GetFormationTarget()
    {
        return IsActive ? FormationTarget : null;
    }
}
