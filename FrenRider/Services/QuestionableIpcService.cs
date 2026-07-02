using System;
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

    private readonly Func<bool> queryIsRunning;
    private readonly Func<DateTime> utcNow;
    private readonly Action<string> logTransition;
    private readonly Action<string> logDebug;

    private DateTime lastPollUtc = DateTime.MinValue;
    private DateTime lastRunningUtc = DateTime.MinValue;
    private string lastTransitionSignature = string.Empty;

    public QuestionableIpcService(IDalamudPluginInterface pluginInterface, IPluginLog log)
        : this(
            () => pluginInterface.GetIpcSubscriber<bool>(IpcIsRunning).InvokeFunc(),
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
    {
        this.queryIsRunning = queryIsRunning;
        this.utcNow = utcNow;
        this.logTransition = logTransition ?? (_ => { });
        this.logDebug = logDebug ?? (_ => { });
    }

    public QuestionableRunningSnapshot Current { get; private set; } = QuestionableRunningSnapshot.Empty;

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
            var running = queryIsRunning();
            if (running)
                lastRunningUtc = now;

            return Apply(new QuestionableRunningSnapshot(
                true,
                running,
                QuestionableRunningSource.Ipc,
                now,
                lastRunningUtc,
                IpcIsRunning));
        }
        catch (Exception ex)
        {
            logDebug($"[FrenRider][Questionable] {IpcIsRunning} unreadable: {ex.Message}");
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
        var signature = $"{snapshot.StatusReadable}|{snapshot.IsRunning}|{snapshot.Source}";
        if (string.Equals(signature, lastTransitionSignature, StringComparison.Ordinal))
            return Current;

        lastTransitionSignature = signature;
        logTransition(
            $"[FrenRider][Questionable] IsRunning transition: readable={snapshot.StatusReadable}, running={snapshot.IsRunning}, source={snapshot.Source}.");
        return Current;
    }
}
