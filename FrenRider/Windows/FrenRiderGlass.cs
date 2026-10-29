using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Glasswork.Backend;
using Glasswork.Glass;

namespace FrenRider.Windows;

/// <summary>Records Glasswork only at the consumer's explicit Background surfaces.</summary>
internal sealed class FrenRiderGlass : IDisposable
{
    private readonly Func<nint>? deviceHandle;
    private readonly Action<string, Exception?>? log;
    private readonly IPluginLog? pluginLog;
    private readonly Func<ImDrawListPtr, Vector2, Vector2, float, Vector4, float, bool, bool>? record;
    private GlassLayer? layer;
    private FrenRiderDetachedGlass? detached;
    private nint currentDevice;
    private bool disposed;
#if DEBUG
    private uint tracedRoutes;
#endif

    internal float Opacity { get; set; } = 1;

    internal FrenRiderGlass(IDalamudPluginInterface pluginInterface, IPluginLog pluginLog)
    {
        this.pluginLog = pluginLog;
        deviceHandle = () => pluginInterface.UiBuilder.DeviceHandle;
        log = (message, exception) =>
        {
            if (exception == null) pluginLog.Debug("[FrenRider] {Message}", message);
            else pluginLog.Warning(exception, "[FrenRider] {Message}", message);
        };
    }

    // Records the native consumer contract in the focused CPU drawing test.
    internal FrenRiderGlass(Func<ImDrawListPtr, Vector2, Vector2, float, Vector4, float, bool, bool> record)
        => this.record = record ?? throw new ArgumentNullException(nameof(record));

    internal void BeginFrame()
    {
        if (disposed || deviceHandle == null) return;
        var device = deviceHandle();
        if (device != currentDevice)
        {
            // A resized back buffer keeps its device; a replaced/lost device needs a new layer.
            layer?.Dispose();
            layer = null;
            detached?.Dispose();
            detached = null;
            currentDevice = device;
            if (device != 0)
            {
                layer = new GlassLayer(() => currentDevice, log)
                {
                    MaxQuality = GlassQuality.Full,
                    // Pin the material and continuous Frost percentage; Reduced quantizes Frost.
                    BudgetMilliseconds = double.PositiveInfinity,
                };
                detached = new FrenRiderDetachedGlass(device, pluginLog!);
                pluginLog?.Debug("[FrenRider] Glasswork pilot: native renderers initialized.");
            }
        }
        layer?.BeginFrame();
        detached?.BeginFrame();
    }

    internal unsafe void EndFrame(bool enabled)
    {
        if (detached == null) return;
        var viewport = ImGui.GetMainViewport();
        var gameWindow = viewport.IsNull ? 0 : (nint)viewport.PlatformHandleRaw;
        if (gameWindow == 0 && !viewport.IsNull) gameWindow = (nint)viewport.PlatformHandle;
        detached.EndFrame(gameWindow, enabled);
    }

    internal bool DrawBackground(ImDrawListPtr drawList, Vector2 min, Vector2 max, float rounding,
        Vector4 tint, float frost, bool windowBackground = false)
    {
        if (disposed) return false;
        if (record != null) return record(drawList, min, max, rounding, tint, frost, windowBackground);
        var size = max - min;
        if (drawList.IsNull || !float.IsFinite(min.X) || !float.IsFinite(min.Y) ||
            !float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X <= 0 || size.Y <= 0 ||
            !float.IsFinite(rounding) || !float.IsFinite(frost)) return false;

        var material = CreateMaterial(tint, frost, Opacity, ImGuiHelpers.GlobalScale);
        var viewport = ImGui.GetWindowViewport();
        if (!viewport.IsNull && viewport.ID != ImGui.GetMainViewport().ID)
        {
            TraceRoute(1, "detached viewport selected");
            return detached?.TryDraw(drawList, min, max, rounding, in material, windowBackground) == true;
        }
        TraceRoute(2, "main viewport selected");
        if (layer == null || !layer.CanRecord()) return false;

        if (windowBackground)
        {
            if (drawList != ImGui.GetWindowDrawList() || min != ImGui.GetWindowPos() ||
                max != min + ImGui.GetWindowSize()) return false;
            var callbacks = CountCallbacks(drawList);
            // This API prepends behind Begin's native title, buttons, borders and scrollbars.
            layer.DrawWindowBackground(in material, rounding);
            // The package's void window API can paint a fill on failure; only callbacks count as glass.
            return !layer.Faulted && CountCallbacks(drawList) > callbacks;
        }

        var shape = GlassShape.FromRect(min, size, rounding);
        return layer.TryDraw(drawList, in shape, in material);
    }

    internal static GlassMaterial CreateMaterial(Vector4 tint, float frost, float opacity, float scale)
    {
        var material = GlassMaterial.Tinted(new Glasswork.Color(tint.X, tint.Y, tint.Z), Math.Clamp(tint.W, 0, 1));
        material.Frost = Math.Clamp(frost, 0, 1);
        material.Opacity *= float.IsFinite(opacity) ? Math.Clamp(opacity, 0, 1) : 1;
        return material.Scaled(scale);
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private void TraceRoute(uint stage, string detail)
    {
#if DEBUG
        if ((tracedRoutes & stage) != 0) return;
        tracedRoutes |= stage;
        pluginLog?.Debug("[FrenRider] Glasswork pilot: {Detail}", detail);
#endif
    }

    private static unsafe int CountCallbacks(ImDrawListPtr drawList)
    {
        var count = 0;
        for (var i = 0; i < drawList.CmdBuffer.Size; i++)
        {
            var command = drawList.CmdBuffer[i];
            if (command.UserCallback != null && command.UserCallbackData != null) count++;
        }
        return count;
    }

    internal void ReleaseResources()
    {
        layer?.ReleaseResources();
        detached?.ReleaseResources();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        layer?.Dispose();
        layer = null;
        detached?.Dispose();
        detached = null;
    }
}
