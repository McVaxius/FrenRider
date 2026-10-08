using System;
using System.Linq;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace FrenRider.Services;

public enum QuestionableRunningSource
{
    None,
    Ipc,
    Unreadable,
}

public sealed record QuestionableRunningSnapshot(
    bool StatusReadable,
    bool IsRunning,
    QuestionableRunningSource Source,
    DateTime CapturedAtUtc,
    DateTime LastRunningUtc,
    string Detail)
{
    public static QuestionableRunningSnapshot Empty { get; } = new(
        false,
        false,
        QuestionableRunningSource.None,
        DateTime.MinValue,
        DateTime.MinValue,
        "Not polled.");
}

public sealed class QuestionableIpcService : IDisposable
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan RecentRunningHold = TimeSpan.FromSeconds(15);

    private const string IpcIsRunning = "Questionable.IsRunning";

    private readonly Func<(string Endpoint, bool Running)> queryIsRunning;
    private readonly Func<DateTime> utcNow;
    private readonly Action<string> logTransition;
    private readonly Action<string> logDebug;
    private readonly Func<bool> questionableLoaded;
    private readonly Func<(bool Readable, bool Running)> queryCompanionActivity;

    private DateTime lastPollUtc = DateTime.MinValue;
    private DateTime lastRunningUtc = DateTime.MinValue;
    private string lastTransitionSignature = string.Empty;

    public QuestionableIpcService(IDalamudPluginInterface pluginInterface, IPluginLog log)
        : this(
            () => QuerySelectedInstallation(pluginInterface),
            () => DateTime.UtcNow,
            message => log.Information(message),
            message => log.Debug(message))
    {
        questionableLoaded = () => pluginInterface.InstalledPlugins.Any(plugin => plugin.IsLoaded
            && plugin.InternalName is "Questionable" or "WigglyQuest");
        queryCompanionActivity = () => QueryCompanionActivity(pluginInterface);
    }

    public QuestionableIpcService(
        Func<bool> queryIsRunning,
        Func<DateTime> utcNow,
        Action<string>? logTransition = null,
        Action<string>? logDebug = null,
        Func<(bool Readable, bool Running)>? queryCompanionActivity = null)
        : this(() => (IpcIsRunning, queryIsRunning()), utcNow, logTransition, logDebug)
    {
        this.queryCompanionActivity = queryCompanionActivity ?? (() => (true, false));
    }

    private QuestionableIpcService(
        Func<(string Endpoint, bool Running)> queryIsRunning,
        Func<DateTime> utcNow,
        Action<string>? logTransition,
        Action<string>? logDebug)
    {
        this.queryIsRunning = queryIsRunning;
        this.utcNow = utcNow;
        this.logTransition = logTransition ?? (_ => { });
        this.logDebug = logDebug ?? (_ => { });
        questionableLoaded = () => true;
        queryCompanionActivity = () => (true, false);
    }

    public QuestionableRunningSnapshot Current { get; private set; } = QuestionableRunningSnapshot.Empty;
    public bool QuestActivityReadable { get; private set; }
    public bool QuestAutomationActive { get; private set; }

    private static (bool Readable, bool Running) QueryCompanionActivity(IDalamudPluginInterface pluginInterface)
    {
        var loaded = pluginInterface.InstalledPlugins.Count(plugin => plugin.IsLoaded && plugin.InternalName == "QSTCompanion");
        if (loaded == 0) return (true, false);
        if (loaded != 1) return (false, false);
        var readable = true;
        var running = false;
        foreach (var feature in new[] { "Rotation", "HuntLogs", "MassGc" })
        {
            try
            {
                var json = pluginInterface.GetIpcSubscriber<string>($"QSTCompanion.{feature}.GetState").InvokeFunc();
                var activity = ReadCompanionActivity(json, feature == "HuntLogs");
                readable &= activity.Readable;
                running |= activity.Running;
            }
            catch { readable = false; }
        }
        return (readable, running);
    }

    internal static (bool Readable, bool Running) ReadCompanionActivity(string json, bool huntLogs)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var state = document.RootElement;
            if (!state.TryGetProperty("apiVersion", out var version) || !version.TryGetInt32(out var api) || api != 2
                || !state.TryGetProperty("running", out var running) || running.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return (false, false);
            if (!huntLogs) return (true, running.GetBoolean());
            if (!state.TryGetProperty("busy", out var busy) || busy.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return (false, false);
            return (true, running.GetBoolean() || busy.GetBoolean());
        }
        catch { return (false, false); }
    }

    private static (string Endpoint, bool Running) QuerySelectedInstallation(IDalamudPluginInterface pluginInterface)
    {
        var loaded = pluginInterface.InstalledPlugins.Where(plugin => plugin.IsLoaded &&
            plugin.InternalName is "Questionable" or "WigglyQuest").ToArray();
        if (loaded.Length != 1)
            throw new InvalidOperationException(loaded.Length == 0
                ? "No Questionable / WigglyQuest installation is loaded."
                : "Multiple Questionable installations are loaded; disable all but one of Questionable / WigglyQuest.");
        var endpoint = $"{loaded[0].InternalName}.IsRunning";
        return (endpoint, pluginInterface.GetIpcSubscriber<bool>(endpoint).InvokeFunc());
    }

    public void Dispose()
    {
    }

    public QuestionableRunningSnapshot Refresh(bool force = false)
    {
        var now = utcNow();
        if (!force && now - lastPollUtc < PollInterval)
            return Current;

        lastPollUtc = now;

        try
        {
            var (endpoint, running) = queryIsRunning();
            if (running)
                lastRunningUtc = now;

            return Apply(new QuestionableRunningSnapshot(
                true,
                running,
                QuestionableRunningSource.Ipc,
                now,
                lastRunningUtc,
                endpoint));
        }
        catch (Exception ex)
        {
            logDebug($"[FrenRider][Questionable] IsRunning unreadable: {ex.Message}");
            return Apply(new QuestionableRunningSnapshot(
                false,
                false,
                QuestionableRunningSource.Unreadable,
                now,
                lastRunningUtc,
                ex.Message));
        }
    }

    public bool WasRunningWithin(TimeSpan window)
    {
        if (lastRunningUtc == DateTime.MinValue)
            return false;

        var elapsed = utcNow() - lastRunningUtc;
        return elapsed >= TimeSpan.Zero && elapsed <= window;
    }

    private QuestionableRunningSnapshot Apply(QuestionableRunningSnapshot snapshot)
    {
        Current = snapshot;
        try
        {
            var companion = queryCompanionActivity();
            var hasQuestionable = questionableLoaded();
            QuestActivityReadable = (!hasQuestionable || snapshot.StatusReadable) && companion.Readable;
            // A readable stop clears this separate safety latch; duty authority keeps its own timed hold.
            QuestAutomationActive = hasQuestionable && snapshot.IsRunning || companion.Running
                || !QuestActivityReadable && QuestAutomationActive;
        }
        catch { QuestActivityReadable = false; }
        var signature = $"{snapshot.StatusReadable}|{snapshot.IsRunning}|{snapshot.Source}|{snapshot.Detail}";
        if (string.Equals(signature, lastTransitionSignature, StringComparison.Ordinal))
            return Current;

        lastTransitionSignature = signature;
        logTransition(
            $"[FrenRider][Questionable] IsRunning transition: readable={snapshot.StatusReadable}, running={snapshot.IsRunning}, source={snapshot.Source}. {snapshot.Detail}");
        return Current;
    }
}
