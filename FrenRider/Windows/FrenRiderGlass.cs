using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Textures.TextureWraps;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
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
    private IDalamudTextureWrap? imageWrap;
    private string? imagePath;
    internal bool ImageAvailable => imageWrap is { Width: > 0, Height: > 0 };
    internal bool ImageMode { get; private set; }
    internal string? ImageProblem { get; private set; }
#if DEBUG
    private uint tracedRoutes;
#endif

    internal float Opacity { get; set; } = 1;
    internal bool ScreenshotsPaused => detached?.ScreenshotsPaused == true;
    internal bool CanPauseForScreenshots => detached?.CanPauseForScreenshots == true;
    internal bool TrySetScreenshotsPaused(bool paused) => detached?.TrySetScreenshotsPaused(paused) == true;

    internal void ConfigureImage(ITextureProvider textures, bool enabled, string? path)
    {
        if (ImageMode != enabled || imagePath != path) detached?.ReleaseImageSource();
        imagePath = path;
        ImageMode = enabled;
        imageWrap = null;
        ImageProblem = null;
        if (!enabled) return;
        if (string.IsNullOrWhiteSpace(path)) { ImageProblem = "Choose an image file."; return; }
        try
        {
            if (!textures.GetFromFile(path).TryGetWrap(out imageWrap, out var error))
                ImageProblem = error == null ? "Loading image..." : "Image could not be loaded.";
        }
        catch { ImageProblem = "Image could not be loaded."; }
    }

    internal bool DrawPlainImage(ImDrawListPtr drawList, Vector2 min, Vector2 max)
    {
        if (!ImageMode) return false;
        detached?.MarkImageWindow();
        if (!ImageAvailable || imageWrap == null || drawList.IsNull) return false;
        var root = ImGuiP.GetCurrentWindowRead().RootWindow;
        var uv = CoverUv(new(imageWrap.Width, imageWrap.Height), root.Size);
        var range = new Vector2(uv.Z - uv.X, uv.W - uv.Y);
        var first = new Vector2(uv.X, uv.Y) + (min - root.Pos) / root.Size * range;
        var last = new Vector2(uv.X, uv.Y) + (max - root.Pos) / root.Size * range;
        drawList.AddImage(imageWrap.Handle, min, max, first, last, ImGui.GetColorU32(Vector4.One));
        return true;
    }

    internal static Vector4 CoverUv(Vector2 imageSize, Vector2 areaSize)
    {
        if (!float.IsFinite(imageSize.X) || !float.IsFinite(imageSize.Y) ||
            !float.IsFinite(areaSize.X) || !float.IsFinite(areaSize.Y) ||
            imageSize.X <= 0 || imageSize.Y <= 0 || areaSize.X <= 0 || areaSize.Y <= 0)
            return new(0, 0, 1, 1);
        var scale = MathF.Max(areaSize.X / imageSize.X, areaSize.Y / imageSize.Y);
        var extent = areaSize / (imageSize * scale);
        var start = (Vector2.One - extent) * .5f;
        return new(start.X, start.Y, start.X + extent.X, start.Y + extent.Y);
    }

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
        if (ImageMode) return DrawImageBackground(drawList, min, max, rounding, in material, windowBackground);
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

    private unsafe bool DrawImageBackground(ImDrawListPtr drawList, Vector2 min, Vector2 max, float rounding,
        in GlassMaterial material, bool windowBackground)
    {
        detached?.MarkImageWindow();
        if (detached == null || imageWrap == null) return false;
        var view = (ID3D11ShaderResourceView*)(nint)imageWrap.Handle.Handle;
        if (view == null) return false;
        ID3D11Resource* resource = null;
        ID3D11Texture2D* texture = null;
        try
        {
            view->GetResource(&resource);
            if (resource == null) return false;
            var id = IID.IID_ID3D11Texture2D;
            void* queried = null;
            if (resource->QueryInterface(&id, &queried).FAILED || queried == null) return false;
            texture = (ID3D11Texture2D*)queried;
            var viewport = ImGui.GetWindowViewport();
            var root = ImGuiP.GetCurrentWindowRead().RootWindow;
            var origin = root.Pos - viewport.Pos;
            var bounds = new Vector4(origin.X, origin.Y, origin.X + root.Size.X, origin.Y + root.Size.Y);
            return detached.TryDraw(drawList, min, max, rounding, in material, windowBackground, (nint)texture, bounds);
        }
        finally
        {
            if (texture != null) texture->Release();
            if (resource != null) resource->Release();
        }
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
        imageWrap = null;
        layer?.Dispose();
        layer = null;
        detached?.Dispose();
        detached = null;
    }
}
