using FrenRider.Services;
using System.Reflection;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace FrenRider.Tests;

public sealed class QuestionableIpcServiceTests
{
    [Theory]
    [InlineData("Questionable", "WigglyQuest")]
    [InlineData("WigglyQuest", "Questionable")]
    public void SelectsOneLoadedAliasAndReevaluatesAfterUnload(string name, string otherName)
    {
        var active = Exposed(name, true);
        IExposedPlugin[] installed = [Exposed(otherName, false), active];
        var calls = new List<string>();
        var running = false;
        var fail = false;
        var pi = Proxy<IDalamudPluginInterface>((method, args) => method.Name switch
        {
            "get_InstalledPlugins" => installed,
            "GetIpcSubscriber" => Proxy(method.ReturnType, (call, _) =>
            {
                Assert.Equal("InvokeFunc", call.Name);
                calls.Add((string)args![0]!);
                if (fail) throw new InvalidOperationException("missing IPC");
                return running;
            }),
            _ => null,
        });
        using var service = new QuestionableIpcService(pi, Proxy<IPluginLog>((_, _) => null));
        var stopped = service.Refresh(force: true);
        Assert.True(stopped.StatusReadable);
        Assert.False(stopped.IsRunning);
        Assert.Equal(name + ".IsRunning", stopped.Detail);
        Assert.Equal(new[] { name + ".IsRunning" }, calls);

        running = true;
        Assert.True(service.Refresh(force: true).IsRunning);
        calls.Clear();
        installed = [active, Exposed(otherName, true)];
        var ambiguous = service.Refresh(force: true);
        Assert.False(ambiguous.StatusReadable);
        Assert.Contains("Multiple", ambiguous.Detail);
        Assert.Empty(calls);
        Assert.True(service.WasRunningWithin(QuestionableIpcService.RecentRunningHold));

        installed = [Exposed(name, false)];
        Assert.False(service.Refresh(force: true).StatusReadable);
        installed = [];
        Assert.False(service.Refresh(force: true).StatusReadable);
        Assert.Empty(calls);
        installed = [Exposed(otherName, true)];
        running = false;
        var reloaded = service.Refresh(force: true);
        Assert.True(reloaded.StatusReadable);
        Assert.False(reloaded.IsRunning);
        Assert.Equal(new[] { otherName + ".IsRunning" }, calls);
        calls.Clear();
        fail = true;
        Assert.False(service.Refresh(force: true).StatusReadable);
        Assert.Equal(new[] { otherName + ".IsRunning" }, calls);
    }

    [Fact]
    public void PollIntervalIsUnchanged()
    {
        var now = DateTime.UtcNow;
        var calls = 0;
        var service = new QuestionableIpcService(() => { calls++; return false; }, () => now);
        service.Refresh();
        now = now.AddMilliseconds(249);
        service.Refresh();
        Assert.Equal(1, calls);
        now = now.AddMilliseconds(1);
        service.Refresh();
        Assert.Equal(2, calls);
    }

    private static IExposedPlugin Exposed(string name, bool loaded)
        => Proxy<IExposedPlugin>((method, _) => method.Name switch
        {
            "get_InternalName" => name,
            "get_IsLoaded" => loaded,
            _ => null,
        });

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
        => (T)Proxy(typeof(T), handler);

    private static object Proxy(Type type, Func<MethodInfo, object?[]?, object?> handler)
    {
        var proxy = DispatchProxy.Create(type, typeof(QuestionableTestProxy));
        ((QuestionableTestProxy)proxy).Handler = handler;
        return proxy;
    }

    [Fact]
    public void UnreadableIpcFallsBackToNotRunning()
    {
        var now = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
        var service = new QuestionableIpcService(
            queryIsRunning: () => throw new InvalidOperationException("missing IPC"),
            utcNow: () => now);

        var snapshot = service.Refresh(force: true);

        Assert.False(snapshot.IsRunning);
        Assert.False(snapshot.StatusReadable);
        Assert.Equal(QuestionableRunningSource.Unreadable, snapshot.Source);
        Assert.False(service.WasRunningWithin(QuestionableIpcService.RecentRunningHold));
    }

    [Fact]
    public void RecentRunningLatchSurvivesTransitionBoundary()
    {
        var now = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
        var running = true;
        var service = new QuestionableIpcService(queryIsRunning: () => running, utcNow: () => now);

        var runningSnapshot = service.Refresh(force: true);
        Assert.True(runningSnapshot.IsRunning);

        running = false;
        now = now.AddSeconds(10);
        var stoppedSnapshot = service.Refresh(force: true);

        Assert.False(stoppedSnapshot.IsRunning);
        Assert.True(service.WasRunningWithin(QuestionableIpcService.RecentRunningHold));

        now = now.AddSeconds(6);
        Assert.False(service.WasRunningWithin(QuestionableIpcService.RecentRunningHold));
    }
}

public class QuestionableTestProxy : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?> Handler = null!;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
}
