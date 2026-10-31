using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Dalamud.Plugin.Services;
using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class ImmediateRaiseTests
{
    [Fact]
    public void RaidAcceptRezControlsTheEarlyRaiseBranchAndReturnRemainsManual()
    {
        var plugin = (Plugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
        var config = new CharacterConfig { Enabled = true, RaidAcceptRez = false };
        var frame = new RaidRuntimeFrame { LoggedIn = true, CharacterId = 701, TerritoryId = 100, DutyId = 1, InDuty = true, Ready = true };
        using var raid = new RaidModeService(() => config, () => { }, () => frame, () => false, () => false);
        plugin.GetType().GetProperty(nameof(Plugin.RaidModeService))!.SetValue(plugin, raid);
        raid.Enable();
        var condition = DispatchProxy.Create<ICondition, LogProxy>();
        var log = DispatchProxy.Create<IPluginLog, LogProxy>();
        string? prompt = "Accept Raise?";
        var clicks = 0;
        using var autoYes = new AutoYesService(plugin, condition, log, () => config, () => prompt,
            () => { clicks++; return true; });
        autoYes.Update();
        Assert.Equal(0, clicks);
        Assert.True(autoYes.RaiseOfferActive);
        raid.AcceptRez = true;
        autoYes.Update();
        Assert.Equal(1, clicks);
        prompt = "Return to the starting point?";
        using var returnCheck = new AutoYesService(plugin, condition, log, () => config, () => prompt,
            () => { clicks++; return true; });
        returnCheck.Update();
        Assert.Equal(1, clicks);
        var respawn = new RespawnService(plugin);
        respawn.Update();
        Assert.Equal(RespawnState.Off, respawn.State);
        Assert.False(respawn.ShouldOwnCurrentUnconsciousReviveFlow(config));
        Assert.Empty(((LogProxy)(object)condition).Messages);
    }

    [Fact]
    public void RaisePrecedesDutyUtilityCombatAndRespawnGatesAndOnlySuccessfulClicksAreDeduplicated()
    {
        var plugin = (Plugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
        var config = JsonSerializer.Deserialize<CharacterConfig>(
            """{"Enabled":true,"RaiseOfferAutoAccept":false}""")!;
        Assert.True(config.Enabled);
        Assert.DoesNotContain("RaiseOfferAutoAccept", JsonSerializer.Serialize(config));
        var condition = DispatchProxy.Create<ICondition, LogProxy>();
        var log = DispatchProxy.Create<IPluginLog, LogProxy>();
        var messages = ((LogProxy)(object)log).Messages;
        string? prompt = "Accept Raise?";
        var clicks = 0;
        var success = false;
        using var autoYes = new AutoYesService(plugin, condition, log, () => config, () => prompt,
            () => { ++clicks; return success; });
        plugin.GetType().GetProperty(nameof(Plugin.AutoYesService))!.SetValue(plugin, autoYes);
        var respawn = new RespawnService(plugin);
        // Start in the existing unconscious wait; no global plugin logger is needed for a state transition.
        typeof(RespawnService).GetProperty(nameof(RespawnService.State))!.SetValue(respawn, RespawnState.Waiting);

        // ADS, utility and respawn dependencies are deliberately unavailable. Raise must
        // finish before querying any of them or the combat condition/generic dialog timer.
        autoYes.Update();
        Assert.True(autoYes.RaiseOfferActive);
        Assert.Equal(1, clicks);
        Assert.Empty(messages);
        respawn.Update();
        Assert.Equal(RespawnState.Waiting, respawn.State);

        success = true;
        autoYes.Update(); // Failed click did not record a cooldown or success.
        Assert.Equal(2, clicks);
        Assert.Single(messages);
        autoYes.Update();
        respawn.Update();
        Assert.Equal(2, clicks);
        Assert.Single(messages);
        Assert.Empty(((LogProxy)(object)condition).Messages);

        prompt = null; // Closing the offer cannot open Return while revival begins.
        autoYes.Update();
        Assert.True(autoYes.RaiseOfferActive);
        respawn.Update();
        Assert.Equal(RespawnState.Waiting, respawn.State);
        config.Enabled = false;
        prompt = "Accept Raise?";
        autoYes.Update();
        Assert.False(autoYes.RaiseOfferActive);
        Assert.Equal(2, clicks);
    }

    public class LogProxy : DispatchProxy
    {
        public readonly List<string> Messages = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "get_Item")
                throw new InvalidOperationException("Raise reached a combat/condition gate.");
            if (method.Name == "Information")
                Messages.Add((string)args![0]!);
            return method.ReturnType == typeof(void) || !method.ReturnType.IsValueType
                ? null : Activator.CreateInstance(method.ReturnType);
        }
    }
}
