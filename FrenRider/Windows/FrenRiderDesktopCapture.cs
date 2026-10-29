using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Plugin.Services;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using Win32 = TerraFX.Interop.Windows.Windows;

namespace FrenRider.Windows;

/// <summary>Draw-thread desktop duplication, assembled in the detached target's physical pixel coordinates.</summary>
internal sealed unsafe class FrenRiderDesktopCapture : IDisposable
{
    private const int NotFound = unchecked((int)0x887A0002);
    private const int DeviceRemoved = unchecked((int)0x887A0005);
    private const int DeviceReset = unchecked((int)0x887A0007);
    private const int AccessLost = unchecked((int)0x887A0026);
    private const int WaitTimeout = unchecked((int)0x887A0027);
    private const int SessionDisconnected = unchecked((int)0x887A0028);
    private const int TemporarilyUnavailable = unchecked((int)0x887A0022);
    private const int AccessDenied = unchecked((int)0x80070005);
    private const int DxgiAccessDenied = unchecked((int)0x887A002B);
    private readonly IPluginLog log;
    private readonly List<AdapterCapture> adapters = new();
    private readonly List<OutputCapture> outputs = new();
    private readonly Dictionary<nint, RECT> excludedWindowBounds = new();
    private readonly Dictionary<nint, long> outputBoundaries = new();
    private ID3D11Device* gameDevice;
    private ID3D11DeviceContext* gameContext;
    private IDXGIFactory1* factory;
    private ID3D11Texture2D* atlas;
    private ID3D11RenderTargetView* atlasView;
    private ID3D11Texture2D* windowCopy;
    private ID3D11ShaderResourceView* windowCopyView;
    private ID3D11Texture2D* windowBackdrop;
    private ID3D11RenderTargetView* windowBackdropView;
    private ID3D11ShaderResourceView* desktopView;
    private nint desktopViewTexture;
    private uint windowWidth;
    private uint windowHeight;
    private DXGI_FORMAT windowFormat;
    private ID3D11VertexShader* vertexShader;
    private ID3D11PixelShader* pixelShader;
    private ID3D11Buffer* transformBuffer;
    private ID3D11SamplerState* sampler;
    private ID3D11RasterizerState* rasterizer;
    private uint atlasWidth;
    private uint atlasHeight;
    private int frame;
    private int atlasFrame = -1;
    private RECT atlasBounds;
    private long atlasMinimumTimestamp;
    private long synchronizedTimestamp = -1;
    private bool topologyNeeded = true;
    private bool pipelineFailed;
    private bool disposed;
    private string? lastFailure;
    internal bool IsPaused { get; private set; }
    internal bool HasRetainedImages => !disposed && synchronizedTimestamp >= 0 &&
        outputs.Exists(output => output.HasImage && output.PresentTimestamp > output.MinimumPresentTimestamp);
#if DEBUG
    private uint tracedStages;
#endif

    internal FrenRiderDesktopCapture(nint gameDevice, IPluginLog log)
    {
        if (gameDevice == 0) throw new ArgumentNullException(nameof(gameDevice));
        this.log = log;
        this.gameDevice = (ID3D11Device*)gameDevice;
        this.gameDevice->AddRef();
        ID3D11DeviceContext* context = null;
        this.gameDevice->GetImmediateContext(&context);
        gameContext = context;
    }

    internal void BeginFrame()
    {
        if (disposed) return;
        frame++;
        atlasFrame = -1;
        if (IsPaused) return;
        if (factory != null && !factory->IsCurrent())
        {
            ReleaseOutputs();
            topologyNeeded = true;
        }
        if (!topologyNeeded && OutputTopologyChanged())
        {
            ReleaseOutputs();
            topologyNeeded = true;
        }
        // A monitor can move or rotate without changing the adapter list.
        foreach (var output in outputs)
        {
            DXGI_OUTPUT_DESC description;
            if (!TryReadOutputDescription((IDXGIOutput*)output.Output, out description)) continue;
            if (!SameRect(description.DesktopCoordinates, output.Description.DesktopCoordinates) ||
                description.Rotation != output.Description.Rotation ||
                description.AttachedToDesktop != output.Description.AttachedToDesktop)
            {
                output.ReleaseImage();
                output.Description = description;
            }
        }
    }

    private bool OutputTopologyChanged()
    {
        // IsCurrent tracks adapters; connectors on an existing adapter also need checking.
        foreach (var adapter in adapters)
        {
            for (uint index = 0; ; index++)
            {
                IDXGIOutput* candidate = null;
                var result = adapter.Adapter->EnumOutputs(index, &candidate);
                if (result.Value == NotFound) break;
                if (result.FAILED) { Release(ref candidate); return true; }
                try
                {
                    DXGI_OUTPUT_DESC description;
                    if (!TryReadOutputDescription(candidate, out description)) return true;
                    if (!description.AttachedToDesktop) continue;
                    OutputCapture? known = null;
                    foreach (var output in outputs)
                        if (output.Adapter == adapter && (output.Description.Monitor == description.Monitor ||
                            SameRect(output.Description.DesktopCoordinates, description.DesktopCoordinates)))
                        { known = output; break; }
                    if (known == null) return true;
                    known.TopologyFrame = frame;
                }
                finally { Release(ref candidate); }
            }
        }
        foreach (var output in outputs)
            if (output.TopologyFrame != frame) return true;
        return false;
    }

    internal void UpdateExclusionBoundary(IReadOnlyCollection<nint> usedOwnedHwnds,
        IReadOnlyCollection<nint> affinityChangedHwnds, long globalTimestamp)
    {
        if (disposed || IsPaused) return;
        try
        {
            if (topologyNeeded) EnumerateOutputs();
            var currentBounds = new Dictionary<nint, RECT>(usedOwnedHwnds.Count);
            foreach (var hwnd in usedOwnedHwnds)
            {
                if (hwnd != 0 && TryGetPhysicalWindowBounds(hwnd, out var bounds)) currentBounds[hwnd] = bounds;
            }
            foreach (var hwnd in affinityChangedHwnds)
            {
                if (excludedWindowBounds.TryGetValue(hwnd, out var previous)) InvalidateOutputs(previous, globalTimestamp);
                if (currentBounds.TryGetValue(hwnd, out var current)) InvalidateOutputs(current, globalTimestamp);
            }
            foreach (var previous in excludedWindowBounds)
                if (!currentBounds.ContainsKey(previous.Key)) InvalidateOutputs(previous.Value, globalTimestamp);
            foreach (var current in currentBounds)
                if (!excludedWindowBounds.ContainsKey(current.Key)) InvalidateOutputs(current.Value, globalTimestamp);
            excludedWindowBounds.Clear();
            foreach (var current in currentBounds) excludedWindowBounds.Add(current.Key, current.Value);
            if (synchronizedTimestamp != globalTimestamp || affinityChangedHwnds.Count != 0) atlasFrame = -1;
            synchronizedTimestamp = globalTimestamp;
        }
        catch (Exception exception)
        {
            synchronizedTimestamp = -1;
            ReportFailure(exception.Message);
        }
    }

    private void InvalidateOutputs(RECT affectedBounds, long timestamp)
    {
        foreach (var output in outputs)
        {
            if (!Intersects(affectedBounds, output.Description.DesktopCoordinates)) continue;
            output.MinimumPresentTimestamp = Math.Max(output.MinimumPresentTimestamp, timestamp);
            outputBoundaries[(nint)output.Description.Monitor] = output.MinimumPresentTimestamp;
        }
    }

    /// <summary>The borrowed texture is valid until the next capture, release or disposal. Call inside the render callback.</summary>
    internal bool TryGetViewportTexture(nint hwnd, Vector2 savedViewportOrigin, uint targetWidth, uint targetHeight,
        bool exclusionReady, long minimumFrameTimestamp, out nint texture)
    {
        texture = 0;
        if (disposed || (!exclusionReady && !IsPaused) || minimumFrameTimestamp != synchronizedTimestamp || hwnd == 0 || gameContext == null ||
            !float.IsFinite(savedViewportOrigin.X) || !float.IsFinite(savedViewportOrigin.Y) ||
            targetWidth == 0 || targetHeight == 0 || targetWidth > 16384 || targetHeight > 16384)
        {
            Trace(1, $"capture prerequisites rejected; disposed={disposed}, exclusion={exclusionReady}, boundaryMatch={minimumFrameTimestamp == synchronizedTimestamp}, window={hwnd != 0}, context={gameContext != null}");
            return false;
        }
        if (!TryGetPhysicalClientBounds(hwnd, out var bounds))
        {
            Trace(2, "physical client bounds unavailable");
            return false;
        }
        // ImGui's saved origin freezes shape coordinates. The HWND supplies the current physical desktop origin.
        var physicalWidth = (long)bounds.right - bounds.left;
        var physicalHeight = (long)bounds.bottom - bounds.top;
        var targetScale = new Vector2(targetWidth / (float)physicalWidth, targetHeight / (float)physicalHeight);
        if (atlasFrame == frame && SameRect(bounds, atlasBounds) && atlasWidth == targetWidth && atlasHeight == targetHeight &&
            atlasMinimumTimestamp == minimumFrameTimestamp)
        {
            texture = (nint)atlas;
            return true;
        }
        try
        {
            if (topologyNeeded) EnumerateOutputs();
            EnsureAtlas(targetWidth, targetHeight);
            var anyOutput = false;
            // Validate every visible source before changing the atlas, including exclusion freshness.
            foreach (var output in outputs)
            {
                if (!output.Description.AttachedToDesktop ||
                    !TryMapOutput(bounds, output.Description.DesktopCoordinates, output.Description.Rotation, out _, out _))
                    continue;
                anyOutput = true;
                if (!CaptureOutput(output) || !output.HasImage)
                {
                    Trace(4, "desktop output has no acquired image");
                    return false;
                }
                if (output.PresentTimestamp <= output.MinimumPresentTimestamp)
                {
                    Trace(8, "desktop output is awaiting a present after exclusion");
                    return false;
                }
            }
            if (!anyOutput) { Trace(16, "window does not intersect a captured output"); return false; }
            foreach (var output in outputs)
            {
                if (!output.Description.AttachedToDesktop ||
                    !TryMapOutput(bounds, output.Description.DesktopCoordinates, output.Description.Rotation,
                        out _, out var sourceUv)) continue;
                UploadRegion(output, sourceUv);
            }
            float* clear = stackalloc float[4] { 0, 0, 0, 1 };
            gameContext->ClearRenderTargetView(atlasView, clear);
            foreach (var output in outputs)
            {
                if (!output.Description.AttachedToDesktop ||
                    !TryMapOutput(bounds, output.Description.DesktopCoordinates, output.Description.Rotation,
                        out var destination, out var sourceUv)) continue;
                if (targetScale == Vector2.One && output.Description.Rotation is
                    (DXGI_MODE_ROTATION.DXGI_MODE_ROTATION_IDENTITY or DXGI_MODE_ROTATION.DXGI_MODE_ROTATION_UNSPECIFIED))
                {
                    var source = new D3D11_BOX
                    {
                        left = (uint)(bounds.left + destination.left - output.Description.DesktopCoordinates.left),
                        top = (uint)(bounds.top + destination.top - output.Description.DesktopCoordinates.top),
                        front = 0,
                        right = (uint)(bounds.left + destination.right - output.Description.DesktopCoordinates.left),
                        bottom = (uint)(bounds.top + destination.bottom - output.Description.DesktopCoordinates.top),
                        back = 1,
                    };
                    gameContext->CopySubresourceRegion((ID3D11Resource*)atlas, 0, (uint)destination.left,
                        (uint)destination.top, 0, (ID3D11Resource*)output.GameImage, 0, &source);
                }
                else DrawSampled(output, sourceUv, MapAtlasDestination(destination, bounds, targetWidth, targetHeight));
            }
            atlasBounds = bounds;
            atlasMinimumTimestamp = minimumFrameTimestamp;
            atlasFrame = frame;
            lastFailure = null;
            texture = (nint)atlas;
            Trace(32, "desktop atlas composed");
            return true;
        }
        catch (Exception exception)
        {
            ReportFailure(exception.Message);
            return false;
        }
    }

    internal static Vector4 MapAtlasDestination(RECT physicalDestination, RECT physicalClientBounds, uint targetWidth, uint targetHeight)
    {
        var width = (long)physicalClientBounds.right - physicalClientBounds.left;
        var height = (long)physicalClientBounds.bottom - physicalClientBounds.top;
        if (width <= 0 || height <= 0 || targetWidth == 0 || targetHeight == 0) return default;
        var scale = new Vector2(targetWidth / (float)width, targetHeight / (float)height);
        return new Vector4(physicalDestination.left * scale.X, physicalDestination.top * scale.Y,
            physicalDestination.right * scale.X, physicalDestination.bottom * scale.Y);
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private void Trace(uint stage, string detail)
    {
#if DEBUG
        if ((tracedStages & stage) != 0) return;
        tracedStages |= stage;
        log.Debug("[FrenRider] Glasswork pilot: {Detail}", detail);
#endif
    }

    /// <summary>Reconstructs the opaque backdrop under a nested pane, retaining already-drawn waves and glass.</summary>
    internal bool TryComposeWindowBackdrop(nint desktopTexture, nint windowTexture, out nint texture)
    {
        texture = 0;
        if (disposed || gameContext == null || desktopTexture == 0 || windowTexture == 0) return false;
        try
        {
            var desktop = (ID3D11Texture2D*)desktopTexture;
            var window = (ID3D11Texture2D*)windowTexture;
            D3D11_TEXTURE2D_DESC desktopDescription, description;
            desktop->GetDesc(&desktopDescription);
            window->GetDesc(&description);
            if (description.Width == 0 || description.Height == 0 || description.SampleDesc.Count != 1 ||
                desktopDescription.SampleDesc.Count != 1 || desktopDescription.Width != description.Width ||
                desktopDescription.Height != description.Height) return false;
            if (windowCopy == null || windowWidth != description.Width || windowHeight != description.Height || windowFormat != description.Format)
            {
                ReleaseWindowComposition();
                description.MipLevels = 1; description.ArraySize = 1;
                description.Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT;
                description.BindFlags = 8; description.CPUAccessFlags = 0; description.MiscFlags = 0;
                ID3D11Texture2D* copy = null;
                Check(gameDevice->CreateTexture2D(&description, null, &copy), "Create existing-window backdrop copy");
                windowCopy = copy;
                ID3D11ShaderResourceView* copyView = null;
                Check(gameDevice->CreateShaderResourceView((ID3D11Resource*)copy, null, &copyView), "Read existing-window backdrop");
                windowCopyView = copyView;
                windowFormat = description.Format;
                description.Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM;
                description.BindFlags = 0x20;
                ID3D11Texture2D* combined = null;
                Check(gameDevice->CreateTexture2D(&description, null, &combined), "Create composed window backdrop");
                windowBackdrop = combined;
                ID3D11RenderTargetView* combinedView = null;
                Check(gameDevice->CreateRenderTargetView((ID3D11Resource*)combined, null, &combinedView), "Target composed window backdrop");
                windowBackdropView = combinedView;
                windowWidth = description.Width; windowHeight = description.Height;
            }
            if (desktopView == null || desktopViewTexture != desktopTexture)
            {
                Release(ref desktopView);
                ID3D11ShaderResourceView* sourceView = null;
                Check(gameDevice->CreateShaderResourceView((ID3D11Resource*)desktop, null, &sourceView), "Read captured desktop backdrop");
                desktopView = sourceView;
                desktopViewTexture = desktopTexture;
            }
            gameContext->CopyResource((ID3D11Resource*)windowCopy, (ID3D11Resource*)window);
            DrawMapped(windowBackdropView, desktopView, windowCopyView, Matrix3x2.Identity,
                new Vector4(0, 0, windowWidth, windowHeight));
            texture = (nint)windowBackdrop;
            return true;
        }
        catch (Exception exception)
        {
            ReportFailure(exception.Message);
            return false;
        }
    }

    private static bool TryGetPhysicalClientBounds(nint hwnd, out RECT bounds)
    {
        bounds = default;
        using var dpi = new PhysicalDpiScope();
        if (!dpi.Ready) return false;
        RECT client;
        if (!Win32.GetClientRect((HWND)hwnd, &client)) return false;
        var first = new POINT(client.left, client.top);
        var last = new POINT(client.right, client.bottom);
        if (!Win32.ClientToScreen((HWND)hwnd, &first) || !Win32.ClientToScreen((HWND)hwnd, &last) ||
            last.x <= first.x || last.y <= first.y) return false;
        bounds = new RECT(first.x, first.y, last.x, last.y);
        return true;
    }

    private static bool TryGetPhysicalWindowBounds(nint hwnd, out RECT bounds)
    {
        bounds = default;
        using var dpi = new PhysicalDpiScope();
        if (!dpi.Ready) return false;
        RECT result;
        if (!Win32.GetWindowRect((HWND)hwnd, &result)) return false;
        bounds = result;
        return true;
    }

    private static bool TryReadOutputDescription(IDXGIOutput* output, out DXGI_OUTPUT_DESC description)
    {
        description = default;
        using var dpi = new PhysicalDpiScope();
        if (!dpi.Ready) return false;
        DXGI_OUTPUT_DESC result;
        if (output->GetDesc(&result).FAILED) return false;
        if (result.AttachedToDesktop)
        {
            var settings = new DEVMODEW { dmSize = (ushort)sizeof(DEVMODEW) };
            var name = (char*)Unsafe.AsPointer(ref result.DeviceName);
            if (!Win32.EnumDisplaySettingsExW(name, uint.MaxValue, &settings, 0) ||
                (settings.dmFields & 0x180020) != 0x180020 || settings.dmPelsWidth == 0 || settings.dmPelsHeight == 0)
                return false;
            result.DesktopCoordinates = new RECT(settings.dmPosition.x, settings.dmPosition.y,
                checked(settings.dmPosition.x + (int)settings.dmPelsWidth), checked(settings.dmPosition.y + (int)settings.dmPelsHeight));
        }
        description = result;
        return true;
    }

    private readonly ref struct PhysicalDpiScope
    {
        private readonly DPI_AWARENESS_CONTEXT previous;
        public PhysicalDpiScope() => previous = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)
            ? Win32.SetThreadDpiAwarenessContext((DPI_AWARENESS_CONTEXT)(nint)(-4)) : default;
        internal bool Ready => (nint)previous != 0;
        public void Dispose()
        {
            if (Ready && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                _ = Win32.SetThreadDpiAwarenessContext(previous);
        }
    }

    /// <summary>Maps displayed screen pixels to atlas pixels and the unrotated duplication surface's UVs.</summary>
    internal static bool TryMapOutput(RECT viewportScreenBounds, RECT outputScreenBounds, DXGI_MODE_ROTATION rotation,
        out RECT atlasDestination, out Matrix3x2 sourceUvTransform)
    {
        atlasDestination = default;
        sourceUvTransform = default;
        if (viewportScreenBounds.right <= viewportScreenBounds.left || viewportScreenBounds.bottom <= viewportScreenBounds.top ||
            outputScreenBounds.right <= outputScreenBounds.left || outputScreenBounds.bottom <= outputScreenBounds.top)
            return false;
        var left = Math.Max(viewportScreenBounds.left, outputScreenBounds.left);
        var top = Math.Max(viewportScreenBounds.top, outputScreenBounds.top);
        var right = Math.Min(viewportScreenBounds.right, outputScreenBounds.right);
        var bottom = Math.Min(viewportScreenBounds.bottom, outputScreenBounds.bottom);
        if (right <= left || bottom <= top) return false;
        var width = (float)((long)outputScreenBounds.right - outputScreenBounds.left);
        var height = (float)((long)outputScreenBounds.bottom - outputScreenBounds.top);
        var scaleX = ((long)right - left) / width;
        var scaleY = ((long)bottom - top) / height;
        var offsetX = ((long)left - outputScreenBounds.left) / width;
        var offsetY = ((long)top - outputScreenBounds.top) / height;
        sourceUvTransform = rotation switch
        {
            DXGI_MODE_ROTATION.DXGI_MODE_ROTATION_UNSPECIFIED or DXGI_MODE_ROTATION.DXGI_MODE_ROTATION_IDENTITY
                => new(scaleX, 0, 0, scaleY, offsetX, offsetY),
            DXGI_MODE_ROTATION.DXGI_MODE_ROTATION_ROTATE90 => new(0, -scaleX, scaleY, 0, offsetY, 1 - offsetX),
            DXGI_MODE_ROTATION.DXGI_MODE_ROTATION_ROTATE180 => new(-scaleX, 0, 0, -scaleY, 1 - offsetX, 1 - offsetY),
            DXGI_MODE_ROTATION.DXGI_MODE_ROTATION_ROTATE270 => new(0, scaleX, -scaleY, 0, 1 - offsetY, offsetX),
            _ => default,
        };
        if (sourceUvTransform == default) return false;
        atlasDestination = new RECT(left - viewportScreenBounds.left, top - viewportScreenBounds.top,
            right - viewportScreenBounds.left, bottom - viewportScreenBounds.top);
        return true;
    }

    private void EnumerateOutputs()
    {
        ReleaseOutputs();
        topologyNeeded = false;
        IDXGIFactory1* newFactory = null;
        try
        {
            Check(DirectX.CreateDXGIFactory1(Win32.__uuidof<IDXGIFactory1>(), (void**)&newFactory), "Enumerate desktop adapters");
            factory = newFactory;
            newFactory = null;
            for (uint adapterIndex = 0; ; adapterIndex++)
            {
                IDXGIAdapter1* adapter = null;
                var result = factory->EnumAdapters1(adapterIndex, &adapter);
                if (result.Value == NotFound) break;
                Check(result, "Enumerate desktop adapter");
                var group = new AdapterCapture { Adapter = adapter };
                adapters.Add(group);
                // DuplicateOutput requires a device created from IDXGIFactory1 or later.
                // The game's same-adapter device can originate from a legacy factory and be rejected.
                for (uint outputIndex = 0; ; outputIndex++)
                {
                    IDXGIOutput* nativeOutput = null;
                    result = adapter->EnumOutputs(outputIndex, &nativeOutput);
                    if (result.Value == NotFound) break;
                    Check(result, "Enumerate desktop output");
                    IDXGIOutput1* output1 = null;
                    try
                    {
                        DXGI_OUTPUT_DESC outputDescription;
                        if (!TryReadOutputDescription(nativeOutput, out outputDescription))
                            throw new InvalidOperationException("Physical desktop output geometry is unavailable.");
                        if (!outputDescription.AttachedToDesktop) continue;
                        // Mirrored outputs can describe the same physical desktop rectangle.
                        var outputBounds = outputDescription.DesktopCoordinates;
                        if (outputs.Exists(existing => SameRect(existing.Description.DesktopCoordinates, outputBounds))) continue;
                        Check(nativeOutput->QueryInterface(Win32.__uuidof<IDXGIOutput1>(), (void**)&output1), "Query desktop duplication");
                        var cutoff = outputBoundaries.GetValueOrDefault((nint)outputDescription.Monitor);
                        if (cutoff == 0)
                            foreach (var excluded in excludedWindowBounds.Values)
                                if (Intersects(excluded, outputBounds)) cutoff = Math.Max(cutoff, synchronizedTimestamp);
                        outputs.Add(new OutputCapture
                        {
                            Adapter = group, Output = output1, Description = outputDescription,
                            MinimumPresentTimestamp = cutoff,
                        });
                        output1 = null;
                    }
                    finally { Release(ref output1); Release(ref nativeOutput); }
                }
            }
        }
        catch
        {
            ReleaseOutputs();
            topologyNeeded = true;
            throw;
        }
        finally { Release(ref newFactory); }
    }

    private bool CaptureOutput(OutputCapture output)
    {
        // Screenshot pause keeps the already-owned clean image; never acquire our now-visible windows.
        if (IsPaused) return output.HasImage;
        if (output.CapturedFrame == frame) return output.HasImage;
        output.CapturedFrame = frame;
        if (output.Failed) return false;
        var group = output.Adapter;
        if (group.Failed) return false;
        if (group.Device == null)
        {
            ID3D11Device* device = null;
            ID3D11DeviceContext* context = null;
            var result = DirectX.D3D11CreateDevice((IDXGIAdapter*)group.Adapter, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_UNKNOWN,
                default, 0x20, null, 0, 7, &device, null, &context);
            if (result.FAILED)
            {
                Release(ref context); Release(ref device);
                group.Failed = true;
                ReportFailure($"Creating the monitor's capture device failed (0x{result.Value:X8}).");
                return false;
            }
            group.Device = device;
            group.Context = context;
        }
        if (output.Duplication == null)
        {
            IDXGIOutputDuplication* duplication = null;
            var result = output.Output->DuplicateOutput((IUnknown*)group.Device, &duplication);
            if (result.FAILED)
            {
                Release(ref duplication);
                if (result.Value is DeviceRemoved or DeviceReset) ResetAdapter(group);
                else if (result.Value is not (AccessLost or SessionDisconnected or TemporarilyUnavailable or AccessDenied or DxgiAccessDenied))
                    output.Failed = true;
                ReportFailure($"Opening desktop duplication failed (0x{result.Value:X8}).");
                return false;
            }
            output.Duplication = duplication;
        }
        DXGI_OUTDUPL_FRAME_INFO info;
        IDXGIResource* resource = null;
        var acquire = output.Duplication->AcquireNextFrame(0, &info, &resource);
        if (acquire.Value == WaitTimeout) { Release(ref resource); return output.HasImage; }
        if (acquire.FAILED)
        {
            Release(ref resource);
            if (acquire.Value is AccessLost or SessionDisconnected or DeviceRemoved or DeviceReset or
                TemporarilyUnavailable or AccessDenied or DxgiAccessDenied)
            {
                output.ReleaseImage();
                output.CapturedFrame = frame;
                if (acquire.Value is DeviceRemoved or DeviceReset) ResetAdapter(group);
            }
            else output.Failed = true;
            ReportFailure($"Acquiring the desktop frame failed (0x{acquire.Value:X8}).");
            return false;
        }
        ID3D11Texture2D* captured = null;
        try
        {
            if (info.ProtectedContentMaskedOut)
                // Windows has already blacked out protected pixels; the rest of the supplied frame is usable.
                Trace(64, "using desktop frame with Windows-masked pixels");
            // A pointer-only frame has no new desktop present and cannot cross a new exclusion boundary.
            if (info.LastPresentTime.QuadPart == 0) return output.HasImage;
            Check(resource->QueryInterface(Win32.__uuidof<ID3D11Texture2D>(), (void**)&captured), "Read desktop frame texture");
            D3D11_TEXTURE2D_DESC description;
            captured->GetDesc(&description);
            EnsureOutputImage(output, description);
            // Keep the latest complete frame on the owned capture device. Upload only the requested region.
            group.Context->CopyResource((ID3D11Resource*)output.CaptureImage, (ID3D11Resource*)captured);
            output.PresentTimestamp = info.LastPresentTime.QuadPart;
            output.HasImage = true;
            return true;
        }
        catch
        {
            output.Failed = true;
            output.HasImage = false;
            throw;
        }
        finally
        {
            Release(ref captured);
            Release(ref resource);
            var released = output.Duplication->ReleaseFrame();
            if (released.FAILED) output.ReleaseImage();
        }
    }

    private void EnsureOutputImage(OutputCapture output, D3D11_TEXTURE2D_DESC description)
    {
        if (description.SampleDesc.Count != 1 || description.Format != DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM)
            throw new InvalidOperationException("Desktop duplication did not return a single-sample BGRA frame.");
        var rotated = output.Description.Rotation is DXGI_MODE_ROTATION.DXGI_MODE_ROTATION_ROTATE90 or DXGI_MODE_ROTATION.DXGI_MODE_ROTATION_ROTATE270;
        var displayedWidth = (uint)(output.Description.DesktopCoordinates.right - output.Description.DesktopCoordinates.left);
        var displayedHeight = (uint)(output.Description.DesktopCoordinates.bottom - output.Description.DesktopCoordinates.top);
        if (description.Width != (rotated ? displayedHeight : displayedWidth) ||
            description.Height != (rotated ? displayedWidth : displayedHeight))
            throw new InvalidOperationException("Desktop output dimensions changed during capture.");
        if (output.GameImage != null && output.Width == description.Width && output.Height == description.Height) return;
        output.ReleaseTextures();
        description.MipLevels = 1;
        description.ArraySize = 1;
        description.Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT;
        description.BindFlags = 8;
        description.CPUAccessFlags = 0;
        description.MiscFlags = 0;
        ID3D11Texture2D* image = null;
        Check(gameDevice->CreateTexture2D(&description, null, &image), "Create desktop image");
        output.GameImage = image;
        ID3D11ShaderResourceView* view = null;
        Check(gameDevice->CreateShaderResourceView((ID3D11Resource*)image, null, &view), "Create desktop image view");
        output.GameView = view;
        description.BindFlags = 0;
        ID3D11Texture2D* retained = null;
        Check(output.Adapter.Device->CreateTexture2D(&description, null, &retained), "Retain desktop frame on capture device");
        output.CaptureImage = retained;
        description.Usage = D3D11_USAGE.D3D11_USAGE_STAGING;
        description.CPUAccessFlags = 0x20000;
        ID3D11Texture2D* staging = null;
        Check(output.Adapter.Device->CreateTexture2D(&description, null, &staging), "Create desktop region readback");
        output.StagingImage = staging;
        output.Width = description.Width;
        output.Height = description.Height;
    }

    private void UploadRegion(OutputCapture output, Matrix3x2 sourceUv)
    {
        var a = Vector2.Transform(Vector2.Zero, sourceUv);
        var b = Vector2.Transform(Vector2.UnitX, sourceUv);
        var c = Vector2.Transform(Vector2.UnitY, sourceUv);
        var d = Vector2.Transform(Vector2.One, sourceUv);
        var min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
        var max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
        // One texel absorbs affine rounding at a rotated crop edge; CPU traffic still follows the requested ROI.
        var region = new D3D11_BOX
        {
            left = (uint)Math.Clamp((int)MathF.Floor(min.X * output.Width) - 1, 0, (int)output.Width),
            top = (uint)Math.Clamp((int)MathF.Floor(min.Y * output.Height) - 1, 0, (int)output.Height),
            right = (uint)Math.Clamp((int)MathF.Ceiling(max.X * output.Width) + 1, 0, (int)output.Width),
            bottom = (uint)Math.Clamp((int)MathF.Ceiling(max.Y * output.Height) + 1, 0, (int)output.Height),
            front = 0, back = 1,
        };
        var context = output.Adapter.Context;
        context->CopySubresourceRegion((ID3D11Resource*)output.StagingImage, 0, 0, 0, 0,
            (ID3D11Resource*)output.CaptureImage, 0, &region);
        D3D11_MAPPED_SUBRESOURCE mapped;
        Check(context->Map((ID3D11Resource*)output.StagingImage, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped), "Read desktop region from capture device");
        try
        {
#if DEBUG
            if ((tracedStages & 128) == 0)
            {
                var width = (int)(region.right - region.left);
                var height = (int)(region.bottom - region.top);
                var samples = 0;
                var nonblack = 0;
                for (var y = 0; y < height; y += Math.Max(1, height / 8))
                    for (var x = 0; x < width; x += Math.Max(1, width / 8))
                    {
                        samples++;
                        if ((((uint*)((byte*)mapped.pData + y * mapped.RowPitch))[x] & 0xFFFFFF) != 0) nonblack++;
                    }
                Trace(128, $"desktop region has {nonblack}/{samples} nonblack samples");
            }
#endif
            gameContext->UpdateSubresource((ID3D11Resource*)output.GameImage, 0, &region, mapped.pData, mapped.RowPitch, 0);
        }
        finally { context->Unmap((ID3D11Resource*)output.StagingImage, 0); }
    }

    private void EnsureAtlas(uint width, uint height)
    {
        if (atlas != null && atlasView != null && width == atlasWidth && height == atlasHeight) return;
        Release(ref atlasView);
        Release(ref atlas);
        atlasFrame = -1;
        var description = new D3D11_TEXTURE2D_DESC
        {
            Width = width, Height = height, MipLevels = 1, ArraySize = 1,
            Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT, BindFlags = 0x28,
        };
        ID3D11Texture2D* image = null;
        Check(gameDevice->CreateTexture2D(&description, null, &image), "Create desktop viewport atlas");
        atlas = image;
        ID3D11RenderTargetView* view = null;
        Check(gameDevice->CreateRenderTargetView((ID3D11Resource*)image, null, &view), "Create desktop atlas target");
        atlasView = view;
        atlasWidth = width;
        atlasHeight = height;
    }

    private void DrawSampled(OutputCapture output, Matrix3x2 sourceUv, Vector4 targetBounds)
        => DrawMapped(atlasView, output.GameView, null, sourceUv, targetBounds);

    private void DrawMapped(ID3D11RenderTargetView* target, ID3D11ShaderResourceView* source,
        ID3D11ShaderResourceView* foreground, Matrix3x2 sourceUv, Vector4 targetBounds)
    {
        EnsureRotationPipeline();
        var transform = stackalloc Vector4[2]
        {
            new(sourceUv.M11, sourceUv.M21, sourceUv.M31, foreground != null ? 1 : 0),
            new(sourceUv.M12, sourceUv.M22, sourceUv.M32, 0),
        };
        gameContext->UpdateSubresource((ID3D11Resource*)transformBuffer, 0, null, transform, 0, 0);
        var sources = stackalloc ID3D11ShaderResourceView*[2] { source, foreground };
        var constants = transformBuffer;
        var pointSampler = sampler;
        var viewport = new D3D11_VIEWPORT
        {
            TopLeftX = targetBounds.X, TopLeftY = targetBounds.Y,
            Width = targetBounds.Z - targetBounds.X, Height = targetBounds.W - targetBounds.Y,
            MinDepth = 0, MaxDepth = 1,
        };
        var clip = new RECT((int)MathF.Floor(viewport.TopLeftX), (int)MathF.Floor(viewport.TopLeftY),
            (int)MathF.Ceiling(viewport.TopLeftX + viewport.Width), (int)MathF.Ceiling(viewport.TopLeftY + viewport.Height));
        float* blend = stackalloc float[4] { 0, 0, 0, 0 };
        gameContext->OMSetRenderTargets(1, &target, null);
        gameContext->OMSetBlendState(null, blend, uint.MaxValue);
        gameContext->OMSetDepthStencilState(null, 0);
        gameContext->RSSetState(rasterizer);
        gameContext->RSSetViewports(1, &viewport);
        gameContext->RSSetScissorRects(1, &clip);
        gameContext->IASetInputLayout(null);
        gameContext->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        gameContext->VSSetShader(vertexShader, null, 0);
        gameContext->GSSetShader(null, null, 0);
        gameContext->HSSetShader(null, null, 0);
        gameContext->DSSetShader(null, null, 0);
        gameContext->PSSetShader(pixelShader, null, 0);
        gameContext->PSSetShaderResources(0, 2, sources);
        gameContext->PSSetSamplers(0, 1, &pointSampler);
        gameContext->PSSetConstantBuffers(0, 1, &constants);
        gameContext->Draw(3, 0);
        sources[0] = sources[1] = null;
        gameContext->PSSetShaderResources(0, 2, sources);
        gameContext->OMSetRenderTargets(0, null, null);
    }

    private void EnsureRotationPipeline()
    {
        if (pixelShader != null) return;
        if (pipelineFailed) throw new InvalidOperationException("Desktop rotation normalization is unavailable.");
        const string source = """
            Texture2D image : register(t0);
            Texture2D foreground : register(t1);
            SamplerState pointSampler : register(s0);
            cbuffer Mapping : register(b0) { float4 rowX; float4 rowY; };
            struct Vertex { float4 position : SV_Position; float2 uv : TEXCOORD0; };
            Vertex VS(uint id : SV_VertexID) {
                Vertex v;
                v.uv = float2((id << 1) & 2, id & 2);
                v.position = float4(v.uv * float2(2, -2) + float2(-1, 1), 0, 1);
                return v;
            }
            float4 PS(Vertex v) : SV_Target {
                float3 uv = float3(v.uv, 1);
                float4 background = image.SampleLevel(pointSampler, float2(dot(rowX.xyz, uv), dot(rowY.xyz, uv)), 0);
                if (rowX.w > 0.5) {
                    float4 over = foreground.SampleLevel(pointSampler, v.uv, 0);
                    return float4(over.rgb + background.rgb * (1 - over.a), 1);
                }
                return background;
            }
            """;
        ID3DBlob* vertex = null;
        ID3DBlob* pixel = null;
        try
        {
            vertex = Compile(source, "VS", "vs_5_0");
            pixel = Compile(source, "PS", "ps_5_0");
            ID3D11VertexShader* vs = null;
            Check(gameDevice->CreateVertexShader(vertex->GetBufferPointer(), vertex->GetBufferSize(), null, &vs), "Create desktop rotation vertex shader");
            vertexShader = vs;
            ID3D11PixelShader* ps = null;
            Check(gameDevice->CreatePixelShader(pixel->GetBufferPointer(), pixel->GetBufferSize(), null, &ps), "Create desktop rotation pixel shader");
            pixelShader = ps;
            var bufferDescription = new D3D11_BUFFER_DESC { ByteWidth = 32, BindFlags = 4, Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT };
            ID3D11Buffer* buffer = null;
            Check(gameDevice->CreateBuffer(&bufferDescription, null, &buffer), "Create desktop rotation mapping");
            transformBuffer = buffer;
            var samplerDescription = new D3D11_SAMPLER_DESC
            {
                Filter = D3D11_FILTER.D3D11_FILTER_MIN_MAG_MIP_LINEAR,
                AddressU = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP,
                AddressV = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP,
                AddressW = D3D11_TEXTURE_ADDRESS_MODE.D3D11_TEXTURE_ADDRESS_CLAMP,
                ComparisonFunc = D3D11_COMPARISON_FUNC.D3D11_COMPARISON_NEVER,
                MaxAnisotropy = 1, MaxLOD = float.MaxValue,
            };
            ID3D11SamplerState* createdSampler = null;
            Check(gameDevice->CreateSamplerState(&samplerDescription, &createdSampler), "Create desktop rotation sampler");
            sampler = createdSampler;
            var rasterDescription = new D3D11_RASTERIZER_DESC
            {
                FillMode = D3D11_FILL_MODE.D3D11_FILL_SOLID, CullMode = D3D11_CULL_MODE.D3D11_CULL_NONE,
                DepthClipEnable = true, ScissorEnable = true,
            };
            ID3D11RasterizerState* createdRasterizer = null;
            Check(gameDevice->CreateRasterizerState(&rasterDescription, &createdRasterizer), "Create desktop rotation rasterizer");
            rasterizer = createdRasterizer;
        }
        catch
        {
            ReleasePipeline();
            pipelineFailed = true;
            throw;
        }
        finally { Release(ref vertex); Release(ref pixel); }
    }

    private static ID3DBlob* Compile(string source, string entry, string profile)
    {
        var text = Encoding.UTF8.GetBytes(source);
        var entryBytes = Encoding.ASCII.GetBytes(entry + '\0');
        var profileBytes = Encoding.ASCII.GetBytes(profile + '\0');
        ID3DBlob* shader = null;
        ID3DBlob* errors = null;
        try
        {
            fixed (byte* code = text, entryPoint = entryBytes, target = profileBytes)
            {
                var result = DirectX.D3DCompile(code, (nuint)text.Length, null, null, null,
                    (sbyte*)entryPoint, (sbyte*)target, 0, 0, &shader, &errors);
                if (result.FAILED)
                {
                    var message = errors == null ? "" : Marshal.PtrToStringAnsi((nint)errors->GetBufferPointer(), checked((int)errors->GetBufferSize()));
                    Release(ref shader);
                    throw new InvalidOperationException($"Compiling desktop rotation failed (0x{result.Value:X8}): {message}");
                }
            }
            return shader;
        }
        finally { Release(ref errors); }
    }

    private void ResetAdapter(AdapterCapture adapter)
    {
        foreach (var output in outputs)
            if (output.Adapter == adapter) { output.ReleaseImage(); output.CapturedFrame = frame; }
        Release(ref adapter.Context); Release(ref adapter.Device);
        adapter.Failed = false;
    }

    private void ReportFailure(string message)
    {
        if (lastFailure == message) return;
        lastFailure = message;
        log.Warning("[FrenRider] Desktop glass unavailable: {Message}", message);
    }

    private static void Check(HRESULT result, string operation)
    {
        if (result.FAILED) throw new InvalidOperationException($"{operation} failed (0x{result.Value:X8}).");
    }

    private static bool SameRect(RECT a, RECT b)
        => a.left == b.left && a.top == b.top && a.right == b.right && a.bottom == b.bottom;

    private static bool Intersects(RECT a, RECT b)
        => a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom;

    private static void Release<T>(ref T* pointer) where T : unmanaged
    {
        if (pointer == null) return;
        ((IUnknown*)pointer)->Release();
        pointer = null;
    }

    private void ReleaseOutputs()
    {
        foreach (var output in outputs) { output.ReleaseImage(); Release(ref output.Output); }
        outputs.Clear();
        foreach (var adapter in adapters)
        {
            Release(ref adapter.Context); Release(ref adapter.Device);
            Release(ref adapter.Adapter);
        }
        adapters.Clear();
        Release(ref factory);
    }

    private void ReleasePipeline()
    {
        Release(ref rasterizer); Release(ref sampler); Release(ref transformBuffer);
        Release(ref pixelShader); Release(ref vertexShader);
    }

    private void ReleaseWindowComposition()
    {
        Release(ref desktopView); desktopViewTexture = 0;
        Release(ref windowCopyView); Release(ref windowCopy);
        Release(ref windowBackdropView); Release(ref windowBackdrop);
        windowWidth = windowHeight = 0;
    }

    internal void ReleaseResources()
    {
        if (disposed) return;
        IsPaused = false;
        ReleaseOutputs();
        ReleaseWindowComposition();
        Release(ref atlasView); Release(ref atlas);
        atlasFrame = -1;
        excludedWindowBounds.Clear();
        outputBoundaries.Clear();
        synchronizedTimestamp = -1;
        topologyNeeded = true;
        pipelineFailed = false;
    }

    internal bool TrySetPaused(bool paused)
    {
        if (disposed) return false;
        if (paused == IsPaused) return true;
        if (paused && !HasRetainedImages) return false;
        IsPaused = paused;
        atlasFrame = -1;
        if (!paused) synchronizedTimestamp = -1;
        return true;
    }

    public void Dispose()
    {
        if (disposed) return;
        ReleaseResources();
        ReleasePipeline();
        Release(ref gameContext); Release(ref gameDevice);
        disposed = true;
    }

    private sealed class AdapterCapture
    {
        internal IDXGIAdapter1* Adapter;
        internal ID3D11Device* Device;
        internal ID3D11DeviceContext* Context;
        internal bool Failed;
    }

    private sealed class OutputCapture
    {
        internal AdapterCapture Adapter = null!;
        internal IDXGIOutput1* Output;
        internal DXGI_OUTPUT_DESC Description;
        internal IDXGIOutputDuplication* Duplication;
        internal ID3D11Texture2D* GameImage;
        internal ID3D11ShaderResourceView* GameView;
        internal ID3D11Texture2D* CaptureImage;
        internal ID3D11Texture2D* StagingImage;
        internal uint Width;
        internal uint Height;
        internal long PresentTimestamp;
        internal long MinimumPresentTimestamp;
        internal int CapturedFrame = -1;
        internal int TopologyFrame = -1;
        internal bool HasImage;
        internal bool Failed;

        internal void ReleaseTextures()
        {
            Release(ref StagingImage); Release(ref CaptureImage); Release(ref GameView); Release(ref GameImage);
            HasImage = false;
            PresentTimestamp = 0;
            Width = Height = 0;
        }

        internal void ReleaseImage()
        {
            ReleaseTextures();
            Release(ref Duplication);
            CapturedFrame = -1;
            Failed = false;
        }
    }
}
