using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FrenRider.Services;

/// <summary>Own Companion discovery or a held Skills window on the framework thread.</summary>
internal sealed class ChocoboExplorationService
{
    internal const string BuildMarker = "chocobo-discovery-025";
    private const long TimeoutMs = 15_000;
    private const long SettleMs = 300;
    internal readonly record struct WindowSnapshot(nint Address, uint Id, int Tab, bool Ready, int SelectedTab,
        uint AgentAddonId = 0);

    private readonly Func<bool> canRun;
    private readonly Func<ulong> identity;
    private readonly Func<WindowSnapshot?> readWindow;
    private readonly Func<bool> open;
    private readonly Action<int> selectTab;
    private readonly Action capture;
    private readonly Action close;
    private readonly Action<string> log;
    private readonly Func<long> clock;
    private readonly Func<bool> skillsChildReady;
    private ulong owner;
    private WindowSnapshot? original;
    private WindowSnapshot? ownedWindow;
    private long deadline;
    private long settleUntil;
    private int expectedTab;
    private int nextTab;
    private bool awaitingWindow;
    private bool active;
    private bool disposed;
    private bool reloadInitialized;
    private bool pendingReload;
    private ulong reloadOwner;
    private bool preparingSkills;
    private bool skillsReady;

    internal bool IsActive => active;
    internal bool PendingReload => pendingReload;
    internal bool IsSkillPreparation => active && preparingSkills;
    internal bool SkillsReady => IsSkillPreparation && skillsReady;
    internal bool OwnsReadySkillsWindow
    {
        get
        {
            if (!SkillsReady || !ownedWindow.HasValue || !canRun() || identity() != owner) return false;
            var current = readWindow();
            return current.HasValue && current.Value.Ready && SameWindow(current.Value, ownedWindow.Value)
                && current.Value.Tab == 1 && current.Value.SelectedTab == 1 && skillsChildReady();
        }
    }
    internal bool PrepareSkills() => Start(forSkills: true);

    public ChocoboExplorationService() : this(
        () => Plugin.ClientState.IsLoggedIn && Plugin.ObjectTable.LocalPlayer != null
            && !Plugin.Condition[ConditionFlag.InCombat]
            && !Plugin.Condition[ConditionFlag.LoggingOut]
            && !Plugin.Condition[ConditionFlag.BetweenAreas]
            && !Plugin.Condition[ConditionFlag.BetweenAreas51],
        () => Plugin.PlayerState.ContentId, ReadWindow, GameHelpers.TryOpenCompanionWindow,
        SelectTab, Capture, CloseWindow,
        message => Plugin.Log.Information($"[FrenRider][ChocoboProbe] {message}"),
        () => Environment.TickCount64)
    { }

    internal ChocoboExplorationService(Func<bool> canRun, Func<ulong> identity,
        Func<WindowSnapshot?> readWindow, Func<bool> open, Action<int> selectTab,
        Action capture, Action close, Action<string> log, Func<long> clock, Func<bool>? skillsChildReady = null)
    {
        this.canRun = canRun;
        this.identity = identity;
        this.readWindow = readWindow;
        this.open = open;
        this.selectTab = selectTab;
        this.capture = capture;
        this.close = close;
        this.log = log;
        this.clock = clock;
        this.skillsChildReady = skillsChildReady ?? HasReadySkillsChild;
    }

    internal void InitializeReload(bool selectedAtLoad, ulong ownerAtLoad)
    {
        if (disposed || reloadInitialized) return;
        reloadInitialized = true;
        pendingReload = selectedAtLoad;
        reloadOwner = ownerAtLoad;
        log($"reload selection build={BuildMarker}; selected={selectedAtLoad}");
    }

    internal void SelectionChanged(bool selected)
    {
        if (disposed) return;
        if (!selected) Stop("reload selection disabled");
        // New selections belong to the next plugin instance, never this load.
    }

    internal void TickReload(bool characterRegistered)
    {
        if (disposed || !pendingReload) return;
        try
        {
            var currentOwner = identity();
            if (reloadOwner != 0 && currentOwner != 0 && currentOwner != reloadOwner)
            {
                pendingReload = false;
                log("reload cancelled: original character departed");
                return;
            }
            if (!characterRegistered || currentOwner == 0) return;
            pendingReload = false; // Consume before the ordinary manual preflight and native dispatch.
            log($"reload consumed build={BuildMarker}");
            Start();
        }
        catch (Exception ex)
        {
            pendingReload = false;
            Fail(ex);
        }
    }

    internal bool Start(bool forSkills = false)
    {
        if (disposed) return false;
        pendingReload = false; // An explicit manual attempt supersedes pending reload discovery.
        if (active) { log("already active; duplicate start ignored"); return false; }
        try
        {
            if (!canRun() || identity() == 0) { log("blocked: current character is not ready or context is unsafe"); return false; }
            original = readWindow();
            log(original.HasValue
                ? $"preflight: Buddy ready={original.Value.Ready}; native-tab={original.Value.Tab}; selected-radio={original.Value.SelectedTab}; addon-id={original.Value.Id}; agent-addon-id={original.Value.AgentAddonId}"
                : "preflight: Buddy is not visible");
            if (original.HasValue && (!original.Value.Ready || original.Value.Tab is < 0 or > 2
                || original.Value.SelectedTab is < 0 or > 2))
            { log("blocked: existing Companion window is not ready"); return false; }
            owner = identity();
            ownedWindow = null;
            nextTab = 0;
            preparingSkills = forSkills;
            skillsReady = false;
            awaitingWindow = true;
            deadline = clock() + TimeoutMs;
            active = true; // Consume the attempt before native dispatch; Tick never reopens it.
            log($"start build={BuildMarker}; original-visible={original.HasValue}");
            if (!original.HasValue)
            {
                log("dispatch: open Companion window");
                if (!open()) { Stop("open request unavailable", restore: false); return false; }
            }
            return active;
        }
        catch (Exception ex) { Fail(ex); return false; }
    }

    internal void Tick()
    {
        if (!active) return;
        try
        {
            if (!canRun() || identity() != owner) { Stop("context/character departed", restore: false); return; }
            if (!skillsReady && clock() >= deadline) { Stop("timeout"); return; }
            var current = readWindow();
            if (awaitingWindow)
            {
                if (!current.HasValue || !current.Value.Ready) return;
                if (original.HasValue && (!SameWindow(current.Value, original.Value)
                    || current.Value.Tab != original.Value.Tab || current.Value.SelectedTab != original.Value.SelectedTab))
                { Stop("existing window/tab changed externally", restore: false); return; }
                if (current.Value.Tab is < 0 or > 2 || current.Value.SelectedTab is < 0 or > 2
                    || (!original.HasValue && current.Value.SelectedTab != current.Value.Tab))
                { Stop("native tab and selected radio disagree", restore: false); return; }
                ownedWindow = current;
                expectedTab = current.Value.Tab;
                awaitingWindow = false;
                log($"accepted: Companion visible and ready; initial-tab={expectedTab}");
                if (!preparingSkills)
                {
                    if (current.Value.SelectedTab == current.Value.Tab) capture();
                    else log("initial native/radio mismatch: capture deferred until the first selected tab is accepted");
                }
                if (active) DispatchNextTab();
                return;
            }
            if (!current.HasValue || !current.Value.Ready || !ownedWindow.HasValue
                || !SameWindow(current.Value, ownedWindow.Value))
            { Stop("window closed, replaced or no longer ready", restore: false); return; }
            if (clock() < settleUntil) return;
            if (current.Value.Tab != expectedTab || current.Value.SelectedTab != expectedTab)
            {
                log($"tab readback: expected={expectedTab}; native-tab={current.Value.Tab}; selected-radio={current.Value.SelectedTab}; addon-id={current.Value.Id}; agent-addon-id={current.Value.AgentAddonId}");
                Stop("native tab changed externally or dispatch was rejected", restore: false);
                return;
            }
            if (preparingSkills)
            {
                if (!skillsChildReady()) return;
                if (!skillsReady) log("Skills window prepared: native/radio tab1 and linked BuddySkill ready");
                skillsReady = true;
                return; // The allocator owns spending and releases this window when it stops.
            }
            log($"progress: inspecting tab={expectedTab}");
            capture();
            if (active) DispatchNextTab();
        }
        catch (Exception ex) { Fail(ex); }
    }

    private void DispatchNextTab()
    {
        if (!canRun() || identity() != owner) { Stop("context/character departed", restore: false); return; }
        if (preparingSkills)
        {
            expectedTab = 1;
            settleUntil = clock() + SettleMs;
            log("dispatch: select Skills tab=1");
            selectTab(1);
            return;
        }
        if (nextTab == 3) { Stop("completed: all three tab selections observed; content captured for review"); return; }
        expectedTab = nextTab++;
        settleUntil = clock() + SettleMs;
        log($"dispatch: select tab={expectedTab}");
        selectTab(expectedTab); // The step is consumed before invoking native code.
    }

    internal void Stop(string reason, bool restore = true)
    {
        preparingSkills = false;
        skillsReady = false;
        if (pendingReload)
        {
            pendingReload = false;
            log($"reload cancelled: {reason}");
        }
        if (!active) return;
        active = false; // Cleanup cannot rearm or repeat a native action.
        log(reason);
        try
        {
            if (!restore || !canRun() || identity() != owner || !ownedWindow.HasValue) return;
            var current = readWindow();
            if (!current.HasValue || !current.Value.Ready
                || !SameWindow(current.Value, ownedWindow.Value) || current.Value.Tab != expectedTab
                || current.Value.SelectedTab != expectedTab) return;
            if (original.HasValue)
            {
                if (current.Value.Tab != original.Value.Tab)
                {
                    selectTab(original.Value.Tab);
                    log("cleanup: requested original tab; restoration not yet observed");
                }
                else log("cleanup: original tab remains selected");
            }
            else
            {
                close();
                log("cleanup: requested Buddy agent hide; completion not yet observed");
            }
        }
        catch (Exception ex) { log($"cleanup failed: {ex.GetType().Name}"); }
        finally { ownedWindow = null; original = null; }
    }

    private void Fail(Exception ex)
    {
        if (active) Stop($"failed: {ex.GetType().Name}");
        else log($"failed: {ex.GetType().Name}");
    }
    internal void Dispose()
    {
        disposed = true;
        Stop("plugin unloading", restore: false);
    }

    private static bool SameWindow(WindowSnapshot a, WindowSnapshot b)
        => a.Address == b.Address && a.Id == b.Id && a.AgentAddonId == b.AgentAddonId;

    private static unsafe AddonBuddy* GetWindow()
    {
        var manager = RaptureAtkUnitManager.Instance();
        return manager == null ? null : (AddonBuddy*)manager->GetAddonByName("Buddy");
    }

    private static unsafe WindowSnapshot? ReadWindow()
    {
        var addon = GetWindow();
        if (addon == null || !addon->IsVisible) return null;
        var agent = GameHelpers.GetCompanionAgent();
        var agentAddonId = agent == null ? 0u : agent->GetAddonId();
        var ready = addon->IsReady && agent != null && agent->IsAgentActive() && agent->IsAddonReady()
            && agentAddonId != 0 && agentAddonId == addon->Id;
        return new WindowSnapshot((nint)addon, addon->Id, addon->TabIndex, ready,
            ready ? ReadSelectedTab(addon) : -1, agentAddonId);
    }

    private static unsafe int ReadSelectedTab(AddonBuddy* addon)
    {
        var selected = -1;
        for (var i = 0; i < addon->RadioButtons.Length; i++)
        {
            var button = addon->RadioButtons[i].Value;
            if (button == null || !button->IsChecked) continue;
            if (selected >= 0) return -1; // Multiple selected controls cannot establish acceptance.
            selected = i;
        }
        return selected;
    }

    private static unsafe void SelectTab(int tab)
    {
        var addon = GetWindow();
        if (addon == null || !addon->IsVisible || !addon->IsReady) throw new InvalidOperationException();
        if (tab < 0 || tab >= addon->RadioButtons.Length) throw new InvalidOperationException();
        if (addon->TabIndex == tab && ReadSelectedTab(addon) == tab) return;
        var button = addon->RadioButtons[tab].Value;
        if (button == null || (!button->IsChecked && !button->SetActive())) throw new InvalidOperationException();
        addon->SetTab(tab);
        // Radio activation alone changed only the highlight in marker006. Native content and
        // radio/index agreement must be observed after the paired typed calls, not inferred here.
    }

    private static unsafe void CloseWindow()
    {
        var addon = GetWindow();
        if (addon == null || !GameHelpers.TryHideCompanionWindow(addon->Id))
            throw new InvalidOperationException();
    }

    private static unsafe bool HasReadySkillsChild() => GetReadySkillsChild() != null;

    internal static unsafe AtkUnitBase* GetReadySkillsChild()
    {
        var buddy = GetWindow();
        if (buddy == null || !buddy->IsVisible || !buddy->IsReady
            || buddy->TabIndex != 1 || ReadSelectedTab(buddy) != 1) return null;
        ref var control = ref buddy->AddonControl;
        if (control.ParentAddon != (AtkUnitBase*)buddy || control.ChildAddons.WithOps.Head == null) return null;
        var head = control.ChildAddons.WithOps.Head;
        var entry = head->Next;
        for (var i = 0; entry != null && entry != head && i < 8; i++, entry = entry->Next)
        {
            var info = entry->Value.Value;
            var child = info == null ? null : info->AtkUnitBase;
            if (child == null || !child->IsVisible || !child->IsReady || child->ParentId != buddy->Id) continue;
            var bytes = child->Name;
            var end = bytes.IndexOf((byte)0);
            if (System.Text.Encoding.UTF8.GetString(end < 0 ? bytes : bytes[..end]) == "BuddySkill") return child;
        }
        return null;
    }

    private static unsafe void Capture()
    {
        var addon = GetWindow();
        if (addon == null || !addon->IsVisible || !addon->IsReady) throw new InvalidOperationException();
        if (ReadSelectedTab(addon) != addon->TabIndex) throw new InvalidOperationException();
        void Write(string value) => Plugin.Log.Information($"[FrenRider][ChocoboProbe] {value}");
        if (GameHelpers.TryReadCompanion(out var info))
            Write($"progression rank={info.Rank}; stars={info.Stars}; xp={info.CurrentXp}; points={info.SkillPoints}; defender={info.DefenderLevel}; attacker={info.AttackerLevel}; healer={info.HealerLevel}");
        var stage = AtkStage.Instance();
        if (stage != null && stage->AtkArrayDataHolder != null
            && stage->AtkArrayDataHolder->NumberArrays != null
            && stage->AtkArrayDataHolder->NumberArrayCount > (int)NumberArrayType.Buddy)
        {
            var numbers = stage->GetNumberArrayData(NumberArrayType.Buddy);
            if (numbers != null && numbers->IntArray != null && numbers->Size >= 10)
                Write($"numbers xp={numbers->IntArray[0]}/{numbers->IntArray[1]}; hp={numbers->IntArray[2]}/{numbers->IntArray[3]}; summon={numbers->IntArray[4]}/{numbers->IntArray[5]}; raw-slot6={numbers->IntArray[6]}; rank={numbers->IntArray[8]}");
        }
        var state = UIState.Instance();
        if (state == null) throw new InvalidOperationException();
        var companionMember = state->Buddy.CompanionInfo.Companion;
        Write($"companion-field summoned-member={companionMember != null}; timer={state->Buddy.CompanionInfo.TimeLeft}; mounted={state->Buddy.CompanionInfo.Mounted}");
        var statuses = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>();
        for (uint statusId = 536; statusId <= 545; statusId++)
        {
            var status = statuses?.GetRowOrDefault(statusId);
            Write($"feed-status row={statusId}; label={(status.HasValue ? Sanitize(status.Value.Name.ToString(), Array.Empty<string>()) : "")}; active={companionMember != null && companionMember->StatusManager.HasStatus(statusId)}");
        }
        var rank = state->Buddy.CompanionInfo.Rank;
        var rankRow = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.BuddyRank>()?.GetRowOrDefault(rank);
        if (rankRow.HasValue)
            Write($"rank-catalog rank={rank}; xp-required={rankRow.Value.ExpRequired}; raw-xp={state->Buddy.CompanionInfo.CurrentXP}; attained-cap not inferred from stars");
        var vathVendorPresent = false;
        foreach (var obj in Plugin.ObjectTable)
            if (obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc
                && obj.BaseId == 1016804 && obj.IsTargetable) { vathVendorPresent = true; break; }
        var game = FFXIVClientStructs.FFXIV.Client.Game.GameMain.Instance();
        Write($"vath-vendor quest-complete={FFXIVClientStructs.FFXIV.Client.Game.QuestManager.IsQuestComplete(67791u)}; in-territory={Plugin.ClientState.TerritoryType == 398}; native-territory-agrees={game != null && game->CurrentTerritoryTypeId == Plugin.ClientState.TerritoryType}; loaded-targetable-vendor={vathVendorPresent}; read-only=True");
        var names = new List<string>();
        names.Add(state->Buddy.CompanionInfo.NameString);
        var playerName = Plugin.ObjectTable.LocalPlayer?.Name.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(playerName)) throw new InvalidOperationException();
        names.Add(playerName);
        names.AddRange(playerName.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var redactions = names.ToArray();
        var confirmationTemplate = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Addon>(Plugin.ClientState.ClientLanguage)?.GetRowOrDefault(4974);
        if (confirmationTemplate.HasValue)
            Write($"skill-confirmation-template macro={Sanitize(confirmationTemplate.Value.Text.ToMacroString(), redactions)}");
        if (GameHelpers.TryGetCompanionSkillPrompt(new(ChocoboSkillTree.Healer, 9, 9, 26), out var renderedPrompt))
            Write($"skill-confirmation-rendered={Sanitize(renderedPrompt, redactions)}");
        Write(GameHelpers.TryReadSelectYesnoPrompt(out var prompt)
            ? $"pending-confirmation visible=True; prompt={Sanitize(prompt, redactions)}"
            : "pending-confirmation visible=False");
        var skills = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.BuddySkill>();
        if (skills != null)
        {
            var rows = 0;
            foreach (var skill in skills)
            {
                if (++rows > 40) break;
                Write($"skill-catalog row={skill.RowId}; level={skill.BuddyLevel}; active={skill.IsActive}; defender-ref={skill.Defender.RowId}; attacker-ref={skill.Attacker.RowId}; healer-ref={skill.Healer.RowId}; point cost not supplied by sheet");
            }
        }
        var foods = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.BuddyItem>();
        if (foods != null)
        {
            var rows = 0;
            foreach (var food in foods)
            {
                if (++rows > 40) break;
                var item = food.Item.ValueNullable;
                var label = item.HasValue ? Sanitize(item.Value.Name.ToString(), redactions) : "";
                Write($"food-catalog row={food.RowId}; item={food.Item.RowId}; name={label}; raw-status={food.Status}; use-field={food.UseField}; use-training={food.UseTraining}; raw-unknown0={food.Unknown0}");
            }
        }
        var player = Plugin.ObjectTable.LocalPlayer;
        var actions = FFXIVClientStructs.FFXIV.Client.Game.ActionManager.Instance();
        foreach (var itemId in new uint[] { 4868, 7894, 7895, 7897, 7898, 7900, 8166 })
        {
            var stock = GameHelpers.GetInventoryItemCount(itemId, highQuality: false);
            var actionStatus = player == null || actions == null ? uint.MaxValue
                : actions->GetActionStatus(FFXIVClientStructs.FFXIV.Client.Game.ActionType.Item, itemId, player.GameObjectId);
            Write($"field-item item={itemId}; nq-stock={stock}; self-action-status={actionStatus}; read-only=True");
            if (GameHelpers.TryReadCompanionFood(itemId, out var field))
                Write($"field-food item={itemId}; owned-summoned={field.BuddyEntityId != 0}; nq-stock={field.Stock}; effect-holders={field.EffectHolders}; read-only=True");
        }
        for (var i = 0; i < addon->RadioButtons.Length; i++)
        {
            var button = addon->RadioButtons[i].Value;
            if (button == null) continue;
            Write($"tab-button index={i}; checked={button->IsChecked}; group={button->GroupId}; label={ReadText(button->ButtonTextNode, redactions)}");
        }
        var visited = new HashSet<nint>();
        var remaining = 512;
        CaptureNodes(&addon->UldManager, "Buddy", 0, visited, ref remaining, redactions, Write);
        CaptureChildAddons(addon, visited, ref remaining, redactions, Write);
        Write($"snapshot tab={addon->TabIndex}; checked-tab={ReadSelectedTab(addon)}; nodes={visited.Count}; bounded={remaining == 0}; event metadata is observational, not a verified mutation payload");
    }

    private static unsafe void CaptureChildAddons(AddonBuddy* addon, HashSet<nint> visited,
        ref int remaining, string[] redactions, Action<string> write)
    {
        ref var control = ref addon->AddonControl;
        var parentMatches = control.ParentAddon == (AtkUnitBase*)addon;
        write($"child-control parent-matches={parentMatches}; linked={control.IsParentAddonLinked}; setup-complete={control.IsChildSetupComplete}; count={control.ChildAddons.WithOps.Size}");
        if (!parentMatches || control.ChildAddons.WithOps.Head == null) return;
        var head = control.ChildAddons.WithOps.Head;
        var entry = head->Next;
        var addons = new HashSet<nint>();
        for (var i = 0; entry != null && entry != head && i < 8; i++, entry = entry->Next)
        {
            var info = entry->Value.Value;
            if (info == null || info->AtkUnitBase == null) continue;
            var child = info->AtkUnitBase;
            if (!addons.Add((nint)child)) continue;
            var nameBytes = child->Name;
            var terminator = nameBytes.IndexOf((byte)0);
            var name = Sanitize(System.Text.Encoding.UTF8.GetString(
                terminator < 0 ? nameBytes : nameBytes[..terminator]), redactions);
            write($"child-addon name={name}; id={child->Id}; parent-id={child->ParentId}; host-id={child->HostId}; control-tab={info->TabIndex}; visible={child->IsVisible}; ready={child->IsReady}");
            if (!child->IsVisible || !child->IsReady || remaining == 0) continue;
            CaptureNodes(&child->UldManager, $"Buddy/{name}", 0, visited, ref remaining, redactions, write);
        }
        write($"child-capture addons={addons.Count}; bounded={entry != null && entry != head}");
    }

    private static unsafe void CaptureNodes(AtkUldManager* manager, string path, int depth,
        HashSet<nint> visited, ref int remaining, string[] redactions, Action<string> write)
    {
        if (manager == null || manager->NodeList == null || depth > 6) return;
        var count = Math.Min((int)manager->NodeListCount, 256);
        for (var i = 0; i < count && remaining > 0; i++)
        {
            var node = manager->NodeList[i];
            if (node == null || !visited.Add((nint)node)) continue;
            remaining--;
            var nodePath = $"{path}/{node->NodeId}";
            var text = node->GetAsAtkTextNode();
            write($"node path={nodePath}; type={node->Type}; visible={node->IsVisible()}; enabled={(node->NodeFlags & NodeFlags.Enabled) != 0}; children={node->ChildCount}"
                + (text == null ? "" : $"; text={ReadText(text, redactions)}"));
            var nativeEvent = node->AtkEventManager.Event;
            for (var eventIndex = 0; nativeEvent != null && eventIndex < 8; eventIndex++)
            {
                write($"event path={nodePath}; type={nativeEvent->State.EventType}; param={nativeEvent->Param}; flags={nativeEvent->State.StateFlags}/{nativeEvent->State.ReturnFlags}; listener-present={nativeEvent->Listener != null}; listener-addon={nativeEvent->Listener == (AtkEventListener*)GetWindow()}; target-self={nativeEvent->Target == (AtkEventTarget*)node}; node-self={nativeEvent->Node == node}");
                nativeEvent = nativeEvent->NextEvent;
            }
            var component = node->GetAsAtkComponentNode();
            if (component != null && component->Component != null)
            {
                write($"component path={nodePath}; type={component->Component->GetComponentType()}");
                CaptureNodes(&component->Component->UldManager, nodePath, depth + 1, visited, ref remaining, redactions, write);
            }
        }
    }

    private static unsafe string ReadText(AtkTextNode* node, string[] redactions)
    {
        if (node == null || !node->NodeText.StringPtr.HasValue) return "";
        if (node->NodeText.BufUsed is < 1 or > 4097 || node->NodeText.BufSize < node->NodeText.BufUsed)
            return "[text outside capture bound]";
        var text = SeString.Parse(node->NodeText.AsSpan()).TextValue;
        return Sanitize(text, redactions);
    }

    internal static string Sanitize(string text, string[] names)
    {
        foreach (var name in names)
            if (!string.IsNullOrWhiteSpace(name)) text = text.Replace(name, "[redacted]", StringComparison.OrdinalIgnoreCase);
        text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Replace('\0', ' ');
        return text.Length > 240 ? text[..240] + "…" : text;
    }
}
