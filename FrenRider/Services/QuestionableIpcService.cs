using System;
using System.Linq;
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
    }

    public QuestionableIpcService(
        Func<bool> queryIsRunning,
        Func<DateTime> utcNow,
        Action<string>? logTransition = null,
        Action<string>? logDebug = null)
        : this(() => (IpcIsRunning, queryIsRunning()), utcNow, logTransition, logDebug)
    {
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
    }

    public QuestionableRunningSnapshot Current { get; private set; } = QuestionableRunningSnapshot.Empty;

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
        var signature = $"{snapshot.StatusReadable}|{snapshot.IsRunning}|{snapshot.Source}|{snapshot.Detail}";
        if (string.Equals(signature, lastTransitionSignature, StringComparison.Ordinal))
            return Current;

        lastTransitionSignature = signature;
        logTransition(
            $"[FrenRider][Questionable] IsRunning transition: readable={snapshot.StatusReadable}, running={snapshot.IsRunning}, source={snapshot.Source}. {snapshot.Detail}");
        return Current;
    }
}
