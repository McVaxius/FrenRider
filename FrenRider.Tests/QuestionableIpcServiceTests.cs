using FrenRider.Services;
using System.Reflection;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace FrenRider.Tests;

public sealed class QuestionableIpcServiceTests
{
    [Theory]
    [InlineData("{\"apiVersion\":2,\"running\":true}", false, true, true)]
    [InlineData("{\"apiVersion\":2,\"running\":false,\"busy\":true}", true, true, true)]
    [InlineData("{\"apiVersion\":2,\"running\":false,\"busy\":false}", true, true, false)]
    [InlineData("{\"apiVersion\":2,\"success\":false,\"code\":\"internal-error\"}", false, false, false)]
    [InlineData("{\"apiVersion\":1,\"running\":false}", false, false, false)]
    [InlineData("{\"apiVersion\":2,\"running\":\"false\"}", false, false, false)]
    [InlineData("{\"apiVersion\":2,\"running\":false}", true, false, false)]
    [InlineData("null", false, false, false)]
    [InlineData("broken", false, false, false)]
    public void CompanionActivityRequiresVersionedBooleanReadback(string json, bool hunt, bool readable, bool running)
        => Assert.Equal((readable, running), QuestionableIpcService.ReadCompanionActivity(json, hunt));

    [Fact]
    public void QuestSafetyLatchRetainsConfirmedActivityUntilEverySourceReadablyStops()
    {
        var q = false;
        var qFails = false;
        var companion = (Readable: true, Running: false);
        using var service = new QuestionableIpcService(() => qFails ? throw new InvalidOperationException() : q,
            () => DateTime.UtcNow, queryCompanionActivity: () => companion);
        service.Refresh(force: true);
        Assert.False(service.QuestAutomationActive);
        companion = (true, true);
        service.Refresh(force: true);
        Assert.True(service.QuestAutomationActive);
        Assert.False(service.WasRunningWithin(QuestionableIpcService.RecentRunningHold));
        companion = (false, false);
        service.Refresh(force: true);
        Assert.False(service.QuestActivityReadable);
        Assert.True(service.QuestAutomationActive);
        companion = (true, false);
        qFails = true;
        service.Refresh(force: true);
        Assert.True(service.QuestAutomationActive);
        qFails = false;
        service.Refresh(force: true);
        Assert.True(service.QuestActivityReadable);
        Assert.False(service.QuestAutomationActive);
        q = true;
        service.Refresh(force: true);
        q = false;
        service.Refresh(force: true);
        Assert.False(service.QuestAutomationActive);
        Assert.True(service.WasRunningWithin(QuestionableIpcService.RecentRunningHold));
    }

    [Theory]
    [InlineData("Rotation")]
    [InlineData("HuntLogs")]
    [InlineData("MassGc")]
    public void CompanionLoadedAloneIsStoppedAndEachActualFeatureCanHoldQuesting(string feature)
    {
        IExposedPlugin[] installed = [Exposed("QSTCompanion", true)];
        var running = false;
        var fail = false;
        var calls = new List<string>();
        var pi = Proxy<IDalamudPluginInterface>((method, args) => method.Name switch
        {
            "get_InstalledPlugins" => installed,
            "GetIpcSubscriber" => Proxy(method.ReturnType, (_, _) =>
            {
                var endpoint = (string)args![0]!;
                calls.Add(endpoint);
                if (fail && endpoint.Contains(feature)) throw new InvalidOperationException();
                return "{\"apiVersion\":2,\"running\":" + (running && endpoint.Contains(feature) ? "true" : "false") + ",\"busy\":false}";
            }),
            _ => null,
        });
        using var service = new QuestionableIpcService(pi, Proxy<IPluginLog>((_, _) => null));
        service.Refresh(force: true);
        Assert.False(service.Current.StatusReadable); // The original Questionable duty contract is unchanged.
        Assert.True(service.QuestActivityReadable);
        Assert.False(service.QuestAutomationActive);
        Assert.Equal(3, calls.Count);
        running = true;
        service.Refresh(force: true);
        Assert.True(service.QuestAutomationActive);
        running = false;
        fail = true;
        service.Refresh(force: true);
        Assert.True(service.QuestAutomationActive);
        fail = false;
        service.Refresh(force: true);
        Assert.False(service.QuestAutomationActive);
        installed = [];
        service.Refresh(force: true);
        Assert.True(service.QuestActivityReadable);
        Assert.False(service.QuestAutomationActive);
    }

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
