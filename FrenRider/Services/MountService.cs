using System;
using System.Linq;
using System.Text;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FrenRider.Models;

namespace FrenRider.Services;

public enum MountState
{
    Idle,           // Not doing anything mount-related
    WaitingToMount, // Fren mounted, waiting for proximity before mounting
    Mounting,       // In the process of mounting
    Mounted,        // On own mount (FlyYouFools mode)
    Dismounting,    // Fren dismounted, dismounting self
}

public class MountService
{
    private readonly Plugin plugin;
    private readonly FrenTracker tracker;
    private readonly ZoneService zoneService;

    private bool wasFrenMounted;
    private long mountCooldownMs;

    public MountState State { get; private set; } = MountState.Idle;
    public string StateDetail { get; private set; } = "";

    public MountService(Plugin plugin, FrenTracker tracker, ZoneService zoneService)
    {
        this.plugin = plugin;
        this.tracker = tracker;
        this.zoneService = zoneService;
    }

    public void Update()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        if (!config.Enabled)
        {
            State = MountState.Idle;
            StateDetail = "Disabled";
            wasFrenMounted = false;
            return;
        }

        var fren = tracker.Fren;
        if (fren == null || !fren.IsFound || !fren.IsVisible)
        {
            State = MountState.Idle;
            StateDetail = "No fren";
            wasFrenMounted = false;
            return;
        }

        var selfMounted = Plugin.Condition[ConditionFlag.Mounted] || Plugin.Condition[ConditionFlag.Mounting71];
        var selfFlying = Plugin.Condition[ConditionFlag.InFlight];
        var inCombat = Plugin.Condition[ConditionFlag.InCombat];
        var now = Environment.TickCount64;

        // DISMOUNT LOGIC: If fren is not mounted and FlyYouFools is enabled, dismount
        // This matches SND: "if IsPartyMemberMounted(fren) == false and fly_you_fools == true"
        if (!fren.IsMounted && config.FlyYouFools && selfMounted)
        {
            if (now >= mountCooldownMs)
            {
                State = MountState.Dismounting;
                StateDetail = "Fren dismounted (FlyYouFools), dismounting...";
                DismountSelf();
            }
            else
            {
                StateDetail = $"Dismount cooldown ({(mountCooldownMs - now) / 1000.0:F1}s)";
            }
            wasFrenMounted = false;
            return;
        }

        // MOUNT LOGIC: If fren is mounted, mount up or ride pillion
        if (fren.IsMounted && !selfMounted && !inCombat)
        {
            if (!wasFrenMounted)
            {
                wasFrenMounted = true;
                Plugin.Log.Information($"Fren mounted (MountId={fren.MountId}), will {(config.FlyYouFools ? "mount self" : "ride pillion")}");
            }
            
            if (now >= mountCooldownMs)
            {
                State = MountState.Mounting;
                StateDetail = config.FlyYouFools ? "Fren mounted (FlyYouFools), mounting..." : "Fren mounted, riding pillion...";
                MountSelf(config);
            }
            else
            {
                State = MountState.WaitingToMount;
                StateDetail = $"Mount cooldown ({(mountCooldownMs - now) / 1000.0:F1}s)";
            }
            return;
        }

        // Track fren mount state
        if (fren.IsMounted && !wasFrenMounted)
        {
            wasFrenMounted = true;
            Plugin.Log.Information($"Fren mounted (MountId={fren.MountId})");
        }
        else if (!fren.IsMounted && wasFrenMounted)
        {
            wasFrenMounted = false;
            Plugin.Log.Information("Fren dismounted");
        }

        // Update ongoing state
        if (fren.IsMounted && selfMounted)
        {
            State = MountState.Mounted;
            StateDetail = selfFlying ? "Flying alongside fren" : "Mounted alongside fren";
        }
        else if (fren.IsMounted && !selfMounted && config.FlyYouFools)
        {
            // We should be mounted but aren't - waiting for cooldown
            State = MountState.WaitingToMount;
            StateDetail = "Waiting to mount...";
        }
        else
        {
            State = MountState.Idle;
            StateDetail = "";
        }

        // Companion summoning is handled by AutomationService.CheckCompanion()
    }

    private void MountSelf(CharacterConfig config)
    {
        var mountName = config.FoolFlier;
        mountCooldownMs = Environment.TickCount64 + 2000; // 2s cooldown

        if (config.FlyYouFools)
        {
            // Fly You Fools: mount own mount
            if (string.IsNullOrEmpty(mountName) || mountName == "Mount Roulette")
            {
                // Mount Roulette - use Company Chocobo as fallback
                SendCommand("/mount \"Company Chocobo\"");
            }
            else
            {
                // Use /mount "Mount Name" with proper case sensitivity
                SendCommand($"/mount \"{mountName}\"");
            }
            State = MountState.Mounting;
            StateDetail = $"Mounting: {mountName}";
        }
        else
        {
            // Pillion riding: target fren and ride pillion
            var fren = tracker.Fren;
            if (fren != null && fren.IsFound)
            {
                // Find fren in ObjectTable and set as target
                var frenObj = Plugin.ObjectTable.FirstOrDefault(obj => 
                    obj != null && obj.Name.ToString() == fren.Name);
                
                if (frenObj != null)
                {
                    Plugin.TargetManager.Target = frenObj;
                    Plugin.Log.Information($"Targeted fren: {fren.Name}");
                    
                    // Send pillion command
                    SendCommand("/ridepillion <t> 2");
                    State = MountState.Mounting;
                    StateDetail = "Riding pillion on fren's mount";
                }
                else
                {
                    State = MountState.Idle;
                    StateDetail = "Can't pillion: fren not in ObjectTable";
                    Plugin.Log.Warning($"Fren {fren.Name} not found in ObjectTable for targeting");
                }
            }
            else
            {
                State = MountState.Idle;
                StateDetail = "Can't pillion: fren not found";
            }
        }
    }

    private void DismountSelf()
    {
        mountCooldownMs = Environment.TickCount64 + 1500; // 1.5s cooldown
        // /mount toggles mount on/off - when mounted, it dismounts
        SendCommand("/mount");
        State = MountState.Dismounting;
        StateDetail = "Dismounting...";
    }

    /// <summary>
    /// Summon chocobo companion via Gysahl Greens (manual trigger).
    /// Auto-summoning is handled by AutomationService.CheckCompanion().
    /// </summary>
    public void SummonCompanion(CharacterConfig config)
    {
        if (GameHelpers.GetInventoryItemCount(GameHelpers.GysahlGreensItemId) <= 0)
        {
            Plugin.Log.Warning("SummonCompanion: No Gysahl Greens in inventory");
            return;
        }

        Plugin.Log.Information("SummonCompanion: Using Gysahl Greens");
        GameHelpers.UseItem(GameHelpers.GysahlGreensItemId);
    }

    private static unsafe void SendCommand(string command)
    {
        try
        {
            Plugin.Log.Information($"MountService sending command: {command}");
            
            // Use UIModule to send command directly to game
            var uiModule = UIModule.Instance();
            if (uiModule == null)
            {
                Plugin.Log.Error("UIModule is null, cannot send command");
                return;
            }

            // Create Utf8String for the command
            var bytes = Encoding.UTF8.GetBytes(command);
            var utf8String = Utf8String.FromSequence(bytes);
            
            // Send command through ProcessChatBoxEntry
            uiModule->ProcessChatBoxEntry(utf8String, nint.Zero);
            
            Plugin.Log.Information($"Mount command sent to game: {command}");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Mount command failed [{command}]: {ex.Message}");
        }
    }
}
