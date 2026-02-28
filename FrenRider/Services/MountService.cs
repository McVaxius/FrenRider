using System;
using Dalamud.Game.ClientState.Conditions;
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
        var now = Environment.TickCount64;

        // Cooldown between mount/dismount actions to avoid spam
        if (now < mountCooldownMs)
        {
            StateDetail = "Cooldown...";
            return;
        }

        // Fren just mounted
        if (fren.IsMounted && !wasFrenMounted)
        {
            wasFrenMounted = true;
            Plugin.Log.Information($"Fren mounted (MountId={fren.MountId})");

            if (!selfMounted)
            {
                if (config.FlyYouFools)
                {
                    // Fly alongside: mount our own mount
                    State = MountState.WaitingToMount;
                    StateDetail = "Fren mounted, preparing to mount...";
                    MountSelf(config);
                }
                else
                {
                    // Pillion: would need to interact with fren's mount
                    // For now, just mount own mount as fallback
                    State = MountState.WaitingToMount;
                    StateDetail = "Fren mounted, mounting up...";
                    MountSelf(config);
                }
            }
        }
        // Fren just dismounted
        else if (!fren.IsMounted && wasFrenMounted)
        {
            wasFrenMounted = false;
            Plugin.Log.Information("Fren dismounted");

            if (selfMounted)
            {
                State = MountState.Dismounting;
                StateDetail = "Fren dismounted, dismounting...";
                DismountSelf();
            }
        }
        // Update ongoing state
        else if (fren.IsMounted && selfMounted)
        {
            State = MountState.Mounted;
            StateDetail = selfFlying ? "Flying alongside fren" : "Mounted alongside fren";
        }
        else if (fren.IsMounted && !selfMounted)
        {
            // We should be mounted but aren't - retry
            if (State == MountState.WaitingToMount && now - mountCooldownMs > 3000)
            {
                MountSelf(config);
            }
        }
        else
        {
            State = MountState.Idle;
            StateDetail = "";
        }

        // Gysahl Green: summon companion chocobo if configured and not in combat/mounted
        if (config.ForceGysahl && !selfMounted && !Plugin.Condition[ConditionFlag.InCombat]
            && !fren.IsMounted && fren.IsVisible)
        {
            // Companion summoning handled via separate cooldown/check in future
        }
    }

    private void MountSelf(CharacterConfig config)
    {
        var mountName = config.FoolFlier;
        mountCooldownMs = Environment.TickCount64 + 2000; // 2s cooldown

        if (string.IsNullOrEmpty(mountName) || mountName == "Mount Roulette")
        {
            SendCommand("/mountroulette");
        }
        else
        {
            // Use /mount "Name" command
            SendCommand($"/mount \"{mountName}\"");
        }

        State = MountState.Mounting;
        StateDetail = $"Mounting: {mountName}";
    }

    private void DismountSelf()
    {
        mountCooldownMs = Environment.TickCount64 + 1500; // 1.5s cooldown
        SendCommand("/mount");
        State = MountState.Dismounting;
        StateDetail = "Dismounting...";
    }

    /// <summary>
    /// Summon chocobo companion via Gysahl Greens.
    /// </summary>
    public void SummonCompanion(CharacterConfig config)
    {
        var stanceCmd = config.CompanionStrat switch
        {
            "Defender Stance" => "/cstance defender",
            "Attacker Stance" => "/cstance attacker",
            "Healer Stance" => "/cstance healer",
            "Follow" => "/cstance follow",
            _ => "/cstance free",
        };

        SendCommand("/gysahlgreens");
        // Set stance after a delay (companion needs time to spawn)
        // For now, just send stance command — the game queues it
        SendCommand(stanceCmd);
    }

    private static void SendCommand(string command)
    {
        try
        {
            if (!Plugin.CommandManager.ProcessCommand(command))
                Plugin.Log.Warning($"Mount command not handled: {command}");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Mount command failed [{command}]: {ex.Message}");
        }
    }
}
