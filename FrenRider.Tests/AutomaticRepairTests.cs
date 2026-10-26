using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

[Collection("Activation mode static services")]
public sealed unsafe class AutomaticRepairTests
{
    public static IEnumerable<object[]> EnabledModes()
    {
        foreach (var activation in new[] { FrenRiderActivationMode.AlwaysOn, FrenRiderActivationMode.DutyOnly })
        foreach (var mode in new[] { 1, 2, 3, 4 })
            yield return [activation, mode];
    }

    public static IEnumerable<object[]> ObservedReplacementUtilities()
    {
        foreach (var activation in new[] { FrenRiderActivationMode.AlwaysOn, FrenRiderActivationMode.DutyOnly })
        foreach (var replacement in new[]
                 {
                     ("shop-purchase", "shop purchasing"), ("auto-equip", "recommended equipment"),
                     ("self", "self-repair"), ("npc-no-inn", "NPC repair"), ("npc-no-teleport-no-inn", "NPC repair"),
                 })
            yield return [activation, replacement.Item1, replacement.Item2];
    }

    [Theory]
    [MemberData(nameof(EnabledModes))]
    public void AutomaticRepairRequestsEachModeOutsideDutyAndWaitsForProviderCompletion(
        FrenRiderActivationMode activation, int mode)
    {
        WithScenario(activation, mode, scenario =>
        {
            scenario.Tick();
            Assert.True(scenario.Requests.Count == 1, scenario.Service.RepairStatus);
            Assert.Equal(new[] { Mode(mode) }, scenario.Requests);
            Assert.True(scenario.Service.IsRepairFlowActive);
            Assert.DoesNotContain("complete", scenario.Service.RepairStatus, StringComparison.OrdinalIgnoreCase);

            scenario.Running = true;
            scenario.Tick();
            Assert.True(scenario.Service.IsRepairFlowActive);

            // Crossing FrenRider's threshold is not an ADS completion receipt.
            scenario.Condition = 30000;
            scenario.Tick();
            Assert.True(scenario.Service.IsRepairFlowActive);
            Assert.DoesNotContain("complete", scenario.Service.RepairStatus, StringComparison.OrdinalIgnoreCase);

            scenario.Running = false;
            scenario.CompletedAt = DateTime.UtcNow.AddSeconds(1);
            scenario.Success = "Repair finished";
            scenario.Tick();
            Assert.False(scenario.Service.IsRepairFlowActive);
            Assert.Contains("Repair complete", scenario.Service.RepairStatus);
            Assert.True(scenario.Requests.Count == 1, scenario.Service.RepairStatus);
        });
    }

    [Theory]
    [MemberData(nameof(EnabledModes))]
    public void UnavailableEquippedInventoryCannotCompleteOrReleaseAcceptedRepair(
        FrenRiderActivationMode activation, int mode)
    {
        WithScenario(activation, mode, scenario =>
        {
            scenario.Tick();
            Assert.Single(scenario.Requests);
            scenario.InventoryAvailable = false;
            scenario.Tick();
            Assert.True(scenario.Service.IsRepairFlowActive);
            Assert.DoesNotContain("complete", scenario.Service.RepairStatus, StringComparison.OrdinalIgnoreCase);
            Assert.Single(scenario.Requests);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NpcNoInnRepairDelegatesLocationAdmissionToAds(bool manual)
        => WithScenario(FrenRiderActivationMode.AlwaysOn, 2, scenario =>
        {
            var priorInstance = ActionManager.Addresses.Instance.Value;
            var priorStatus = ActionManager.Addresses.GetActionStatus.Value;
            var manager = new ActionManager();
            try
            {
                ActionManager.Addresses.Instance.Value = (nint)(&manager);
                ActionManager.Addresses.GetActionStatus.Value =
                    (nint)(delegate* unmanaged<ActionManager*, ActionType, uint, ulong, byte, byte, uint*, uint>)&MountAvailable;
                Assert.Equal(0u, ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 9));
                Assert.False(GameHelpers.IsInSanctuary()); // Real mount-available native result; no nearby crystal.
                if (manual) scenario.Service.TriggerRepair(scenario.Config);
                else scenario.Tick();
                Assert.Equal(new[] { "npc-no-inn" }, scenario.Requests);
            }
            finally
            {
                ActionManager.Addresses.Instance.Value = priorInstance;
                ActionManager.Addresses.GetActionStatus.Value = priorStatus;
            }
        });

    [UnmanagedCallersOnly]
    private static uint MountAvailable(ActionManager* manager, ActionType type, uint id, ulong target,
        byte recast, byte casting, uint* extra) => 0;

    [Theory]
    [InlineData(FrenRiderActivationMode.AlwaysOn)]
    [InlineData(FrenRiderActivationMode.DutyOnly)]
    public void DisabledModeAndMasterOffNeverRequestRepair(FrenRiderActivationMode activation)
    {
        WithScenario(activation, 0, scenario =>
        {
            scenario.Tick();
            Assert.Empty(scenario.Requests);
            scenario.Config.Repair = 1;
            scenario.Config.Enabled = false;
            scenario.Tick();
            Assert.Empty(scenario.Requests);
        });
    }

    [Theory]
    [InlineData(ConditionFlag.InCombat)]
    [InlineData(ConditionFlag.BoundByDuty)]
    [InlineData(ConditionFlag.BoundByDuty56)]
    [InlineData(ConditionFlag.BoundByDuty95)]
    [InlineData(ConditionFlag.BetweenAreas)]
    [InlineData(ConditionFlag.BetweenAreas51)]
    [InlineData(ConditionFlag.RidingPillion)]
    [InlineData(ConditionFlag.Mounting71)]
    [InlineData(ConditionFlag.OccupiedInQuestEvent)]
    [InlineData(ConditionFlag.OccupiedInCutSceneEvent)]
    [InlineData(ConditionFlag.Occupied33)]
    [InlineData(ConditionFlag.Occupied39)]
    [InlineData(ConditionFlag.WatchingCutscene)]
    public void AutomaticRepairRetainsConditionSafetyHolds(ConditionFlag flag)
        => WithScenario(FrenRiderActivationMode.DutyOnly, 1, scenario =>
        {
            scenario.Flags.Add(flag);
            scenario.Tick();
            Assert.Empty(scenario.Requests);
        });

    [Theory]
    [InlineData("ads-unavailable")]
    [InlineData("ads-unreadable")]
    [InlineData("utility-busy")]
    [InlineData("inventory-unavailable")]
    [InlineData("threshold-zero")]
    [InlineData("threshold-met")]
    [InlineData("dead")]
    [InlineData("casting")]
    public void AutomaticRepairRequiresReadableReadyProviderAndGear(string blocker)
        => WithScenario(FrenRiderActivationMode.AlwaysOn, 1, scenario =>
        {
            switch (blocker)
            {
                case "ads-unavailable": scenario.Available = false; break;
                case "ads-unreadable": scenario.Readable = false; break;
                case "utility-busy": scenario.Running = true; break;
                case "inventory-unavailable": scenario.InventoryAvailable = false; break;
                case "threshold-zero": scenario.Config.TornClothes = 0; break;
                case "threshold-met": scenario.Condition = 22500; break;
                case "dead": scenario.Hp = 0; break;
                case "casting": scenario.Casting = true; break;
            }
            scenario.Tick();
            Assert.Empty(scenario.Requests);
        });

    [Theory]
    [InlineData("unreadable")]
    [InlineData("stale-success")]
    [InlineData("accepted-only")]
    [InlineData("unloaded-inventory")]
    [InlineData("still-damaged")]
    public void AcceptedRequestRequiresFreshSuccessAndReadableRecoveredDurability(string state)
        => WithScenario(FrenRiderActivationMode.AlwaysOn, 4, scenario =>
        {
            scenario.Tick();
            Assert.Single(scenario.Requests);
            scenario.Condition = 30000;
            scenario.CompletedAt = DateTime.UtcNow.AddSeconds(1);
            scenario.Success = "Inn return finished";
            switch (state)
            {
                case "unreadable": scenario.Readable = false; break;
                case "stale-success": scenario.CompletedAt = DateTime.UtcNow.AddMinutes(-1); break;
                case "accepted-only": scenario.CompletedAt = null; scenario.Success = ""; break;
                case "unloaded-inventory": scenario.InventoryLoaded = false; break;
                case "still-damaged": scenario.Condition = 0; break;
            }
            scenario.Tick();
            Assert.True(scenario.Service.IsRepairFlowActive);
            Assert.DoesNotContain("Repair complete", scenario.Service.RepairStatus);
            Assert.Single(scenario.Requests);
        });

    [Fact]
    public void ProviderFailureIsReportedAndAnInFlightModeIsNotRetargeted()
        => WithScenario(FrenRiderActivationMode.AlwaysOn, 1, scenario =>
        {
            scenario.Tick();
            scenario.Config.Repair = 4;
            scenario.Running = true;
            scenario.Tick();
            Assert.StartsWith("Self repair running", scenario.Service.RepairStatus);
            scenario.Running = false;
            scenario.CompletedAt = DateTime.UtcNow.AddSeconds(1);
            scenario.Failure = "Missing repair supplies";
            scenario.Tick();
            Assert.False(scenario.Service.IsRepairFlowActive);
            Assert.Contains(scenario.Failure, scenario.Service.RepairStatus);
            Assert.Equal(new[] { "self" }, scenario.Requests);
        });

    [Theory]
    [MemberData(nameof(ObservedReplacementUtilities))]
    public void ObservedReplacementCannotCompleteTheOriginalInnRepair(
        FrenRiderActivationMode activation, string replacementMode, string replacementTask)
        => WithScenario(activation, 4, scenario =>
        {
            scenario.Tick();
            Assert.Equal(new[] { "npc-yes-inn" }, scenario.Requests);
            scenario.Running = true;
            scenario.Tick();
            scenario.Condition = 30000;
            scenario.UtilityMode = replacementMode;
            scenario.UtilityTask = replacementTask;
            scenario.Tick();
            var releasedOriginalTracking = !scenario.Service.IsRepairFlowActive;
            var replacementStatus = scenario.Service.RepairStatus;
            Assert.True(scenario.Service.IsUtilityGateActive);

            scenario.Running = false;
            scenario.CompletedAt = DateTime.UtcNow.AddSeconds(1);
            scenario.Success = "Replacement utility finished";
            scenario.Tick();
            Assert.DoesNotContain("Repair complete", scenario.Service.RepairStatus);
            Assert.True(releasedOriginalTracking);
            Assert.Contains("interrupted", replacementStatus, StringComparison.OrdinalIgnoreCase);
            Assert.Single(scenario.Requests);
        });

    private static string Mode(int mode) => mode switch
    {
        1 => "self", 2 => "npc-no-inn", 3 => "npc-no-teleport-no-inn", 4 => "npc-yes-inn", _ => "",
    };

    private static void WithScenario(FrenRiderActivationMode activation, int mode, Action<Scenario> verify)
        => ActivationModeTests.WithManager((manager, config) =>
        {
            using var scenario = new Scenario(manager, config, activation, mode);
            verify(scenario);
        });

    private sealed class Scenario : IDisposable
    {
        private readonly Dictionary<PropertyInfo, object?> previousServices = [];
        private readonly nint priorInventoryInstance = InventoryManager.Addresses.Instance.Value;
        private readonly nint priorGetContainer = InventoryManager.Addresses.GetInventoryContainer.Value;
        private readonly nint memory;
        private readonly InventoryItem* items;
        public readonly AutomationService Service;
        public readonly CharacterConfig Config;
        public readonly List<string> Requests = [];
        public readonly HashSet<ConditionFlag> Flags = [];
        public bool Available = true;
        public bool Readable = true;
        public uint Hp = 100;
        public bool Casting;
        public bool Running;
        public DateTime? CompletedAt;
        public string Success = "";
        public string Failure = "";
        public string? UtilityMode;
        public string? UtilityTask;
        public ushort Condition { set => items[0].Condition = value; }
        public bool InventoryAvailable { set => inventoryAvailable = value; }
        public bool InventoryLoaded { set => container->IsLoaded = value; }

        public Scenario(ConfigManager manager, CharacterConfig config, FrenRiderActivationMode activation, int mode)
        {
            Config = config = manager.GetActiveConfig();
            config.Enabled = true;
            config.ActivationMode = activation;
            config.Repair = mode;
            config.TornClothes = 75;
            memory = (nint)NativeMemory.AllocZeroed((nuint)(sizeof(InventoryManager) + sizeof(InventoryContainer)
                + sizeof(InventoryItem) * 14 + sizeof(InventoryContainer.InventoryContainerVirtualTable) + sizeof(nint)));
            var instance = (InventoryManager*)memory;
            container = (InventoryContainer*)(instance + 1);
            items = (InventoryItem*)(container + 1);
            container->Items = items;
            container->Size = 14;
            container->IsLoaded = true;
            items[0].ItemId = 2;
            items[0].Condition = 0;
            var vtable = (InventoryContainer.InventoryContainerVirtualTable*)(items + 14);
            vtable->GetInventorySlot = &GetSlot;
            container->VirtualTable = vtable;
            var pointer = (nint*)(vtable + 1);
            *pointer = memory;
            inventoryAvailable = true;
            InventoryManager.Addresses.Instance.Value = (nint)pointer;
            InventoryManager.Addresses.GetInventoryContainer.Value = (nint)(delegate* unmanaged<InventoryManager*, InventoryType, InventoryContainer*>)&GetContainer;

            var log = Proxy<IPluginLog>((_, _) => null);
            var ads = Proxy<IExposedPlugin>((method, _) => method.Name switch
            {
                "get_InternalName" => "ADS", "get_Name" => "AI Duty Solver", "get_IsLoaded" => Available, _ => null,
            });
            var pi = Proxy<IDalamudPluginInterface>((method, args) => method.Name switch
            {
                "get_InstalledPlugins" => new[] { ads },
                "GetIpcSubscriber" => Proxy(method.ReturnType, (call, values) =>
                {
                    if (call.Name != "InvokeFunc") return null;
                    if ((string)args![0]! == "ADS.GetStatusJson")
                        return Readable ? JsonSerializer.Serialize(new { utilityRunning = Running,
                            utilityMode = Running ? UtilityMode ?? Mode(mode) : "",
                            utilityTask = Running ? UtilityTask ?? "Repair" : "",
                            utilityLastSuccess = Success, utilityLastFailure = Failure,
                            utilityCompletedAtUtc = CompletedAt }) : "";
                    Assert.Equal("ADS.StartRepair", args[0]);
                    Requests.Add((string)values![0]!);
                    return true;
                }),
                _ => null,
            });
            SetStatic("Log", log);
            SetStatic("PluginInterface", pi);
            SetStatic("Condition", Proxy<ICondition>((method, args) => method.Name == "get_Item"
                ? Flags.Contains((ConditionFlag)args![0]!) : Default(method.ReturnType)));
            var player = Proxy<IPlayerCharacter>((method, _) => method.Name switch
            {
                "get_CurrentHp" => Hp, "get_IsCasting" => Casting, _ => Default(method.ReturnType),
            });
            SetStatic("ObjectTable", Proxy<IObjectTable>((method, _) => method.Name == "get_LocalPlayer" ? player : Default(method.ReturnType)));
            var plugin = (Plugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
            Set(plugin, "<ConfigManager>k__BackingField", manager);
            Set(plugin, "<AdsUtilityIpcService>k__BackingField", new AdsUtilityIpcService(pi, log));
            Set(plugin, "<AdsIntegrationService>k__BackingField", new AdsIntegrationService(plugin, new AdsDutyIpcService(pi, log)));
            Service = (AutomationService)RuntimeHelpers.GetUninitializedObject(typeof(AutomationService));
            Set(Service, "plugin", plugin);
            Set(Service, "zoneService", new ZoneService());
        }

        public void Tick()
        {
            Set(Service, "lastRepairCheckMs", 0L);
            Service.UpdateUtilityGate();
        }

        private void SetStatic(string name, object value)
        {
            var property = typeof(Plugin).GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic)!;
            previousServices[property] = property.GetValue(null);
            property.SetValue(null, value);
        }

        public void Dispose()
        {
            foreach (var entry in previousServices) entry.Key.SetValue(null, entry.Value);
            InventoryManager.Addresses.Instance.Value = priorInventoryInstance;
            InventoryManager.Addresses.GetInventoryContainer.Value = priorGetContainer;
            container = null;
            NativeMemory.Free((void*)memory);
        }
    }

    private static InventoryContainer* container;
    private static bool inventoryAvailable;
    [UnmanagedCallersOnly]
    private static InventoryContainer* GetContainer(InventoryManager* _, InventoryType type)
        => inventoryAvailable && type == InventoryType.EquippedItems ? container : null;
    [UnmanagedCallersOnly]
    private static InventoryItem* GetSlot(InventoryContainer* value, int index)
        => index >= 0 && index < value->Size ? value->Items + index : null;
    private static void Set(object target, string name, object value)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static object? Default(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;
    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class => (T)Proxy(typeof(T), handler);
    private static object Proxy(Type type, Func<MethodInfo, object?[]?, object?> handler)
    {
        var result = DispatchProxy.Create(type, typeof(QuestionableTestProxy));
        ((QuestionableTestProxy)result).Handler = handler;
        return result;
    }
}
