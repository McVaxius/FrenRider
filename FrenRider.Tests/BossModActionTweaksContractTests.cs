using System.Reflection;
using System.Reflection.Emit;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class BossModActionTweaksContractTests
{
    [Theory]
    [InlineData("BossMod.ActionTweaksConfig")]
    [InlineData("BossMod.ActionManagerConfig")]
    public void CastingConfigUsesTheNativeGetterRatherThanTreatingNodeTypesAsObjects(string typeName)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("SyntheticCastingConfig"), AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("Provider");
        var nodeType = module.DefineType(typeName, TypeAttributes.Public).CreateType()!;
        var rootBuilder = module.DefineType("BossMod.ConfigRoot", TypeAttributes.Public);
        var nodeField = rootBuilder.DefineField("Node", typeof(object), FieldAttributes.Public);
        var get = rootBuilder.DefineMethod("Get", MethodAttributes.Public);
        var argument = get.DefineGenericParameters("T")[0];
        get.SetReturnType(argument);
        var il = get.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, nodeField);
        il.Emit(OpCodes.Unbox_Any, argument);
        il.Emit(OpCodes.Ret);
        var rootType = rootBuilder.CreateType()!;
        var root = Activator.CreateInstance(rootType)!;
        var node = Activator.CreateInstance(nodeType)!;
        rootType.GetField("Node")!.SetValue(root, node);

        Assert.Same(node, BossModActionTweaksService.FindConfigNode(root, rootType.Assembly));
        Assert.Null(BossModActionTweaksService.FindConfigNode(root, typeof(BossModActionTweaksContractTests).Assembly));
        rootType.GetField("Node")!.SetValue(root, null);
        Assert.Null(BossModActionTweaksService.FindConfigNode(root, rootType.Assembly));
    }

    [Theory]
    [InlineData("BossMod", true)]
    [InlineData("BossModReborn", false)]
    public void PermanentCastingPreferenceUsesTheSelectedProviderConfigIpc(string provider, bool enabled)
    {
        var f = new IpcFixture(provider);

        Assert.True(BossModActionTweaksService.ApplyConfigViaIpc(f.Interface, provider, "ActionTweaksConfig", enabled));
        Assert.Equal(provider == "BossMod" ? typeof(IReadOnlyList<string>) : typeof(List<string>), f.ArgumentType);
        Assert.Equal(new[] { "ActionTweaksConfig", "PreventMovingWhileCasting", enabled ? "true" : "false" }, f.Arguments);
        Assert.True(f.Save);
        Assert.Equal(1, f.Writes);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("ambiguous")]
    [InlineData("different-provider")]
    public void UnavailableOrAmbiguousIpcDoesNotDispatch(string shape)
    {
        var f = new IpcFixture("BossMod") { Available = shape != "missing" };
        if (shape == "ambiguous") f.Plugins.Add(Plugin("BossModReborn"));
        Assert.Null(BossModActionTweaksService.ApplyConfigViaIpc(f.Interface,
            shape == "different-provider" ? "BossModReborn" : "BossMod", "ActionTweaksConfig", true));
        Assert.Equal(0, f.Writes);
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("unreadable")]
    [InlineData("throws")]
    public void AttemptedIpcFailureRemainsFailureAndNeverBecomesUnavailable(string failure)
    {
        var f = new IpcFixture("BossMod") { Failure = failure };
        if (failure == "throws")
            Assert.Throws<InvalidOperationException>(() => BossModActionTweaksService.ApplyConfigViaIpc(f.Interface, "BossMod", "ActionTweaksConfig", true));
        else
            Assert.False(BossModActionTweaksService.ApplyConfigViaIpc(f.Interface, "BossMod", "ActionTweaksConfig", true));
        Assert.Equal(1, f.Writes);
    }

    private sealed class IpcFixture
    {
        internal readonly IDalamudPluginInterface Interface;
        internal readonly List<IExposedPlugin> Plugins = new();
        internal bool Available = true;
        internal string? Failure;
        internal int Writes;
        internal Type? ArgumentType;
        internal string[]? Arguments;
        internal bool Save;

        internal IpcFixture(string provider)
        {
            Plugins.Add(Plugin(provider));
            Interface = (IDalamudPluginInterface)Proxy(typeof(IDalamudPluginInterface), (method, args) => method.Name switch
            {
                "get_InstalledPlugins" => Plugins,
                "GetIpcSubscriber" => Subscriber(method, args),
                _ => throw new InvalidOperationException(method.Name),
            });
        }

        private object Subscriber(MethodInfo method, object?[]? args)
        {
            Assert.Equal("BossMod.Configuration", args![0]);
            ArgumentType = method.GetGenericArguments()[0];
            return Proxy(method.ReturnType, (call, values) =>
            {
                if (call.Name == "get_HasFunction") return Available;
                Assert.Equal("InvokeFunc", call.Name);
                Writes++;
                Arguments = ((IEnumerable<string>)values![0]!).ToArray();
                Save = (bool)values[1]!;
                if (Failure == "throws") throw new InvalidOperationException("Synthetic uncertain write.");
                return Failure == "unreadable" ? null : Failure == "rejected" ? new List<string> { "Synthetic rejection" } : new List<string>();
            });
        }
    }

    private static IExposedPlugin Plugin(string name) => (IExposedPlugin)Proxy(typeof(IExposedPlugin), (method, _) => method.Name switch
    {
        "get_InternalName" => name,
        "get_IsLoaded" => true,
        _ => throw new InvalidOperationException(method.Name),
    });

    private static object Proxy(Type type, Func<MethodInfo, object?[]?, object?> invoke)
    {
        var proxy = DispatchProxy.Create(type, typeof(ContractProxy));
        ((ContractProxy)proxy).Handler = invoke;
        return proxy;
    }

    public class ContractProxy : DispatchProxy
    {
        internal Func<MethodInfo, object?[]?, object?> Handler = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
    }
}
