using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using FrenRider.Windows;
using Glasswork.Glass;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using Xunit;
using Win32 = TerraFX.Interop.Windows.Windows;

namespace FrenRider.Tests;

[Collection("Activation mode static services")]
public sealed class DesktopGlassTests
{
    [Fact]
    public unsafe void BackgroundTintChangesActualGlassPixelsIndependentlyOfFrost()
    {
        ID3D11Device* device = null;
        ID3D11DeviceContext* context = null;
        ID3D11Texture2D* source = null;
        ID3D11Texture2D* target = null;
        ID3D11Texture2D* staging = null;
        ID3D11RenderTargetView* view = null;
        GlassRenderer? renderer = null;
        try
        {
            Assert.True(DirectX.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_WARP,
                default, 0, null, 0, 7, &device, null, &context).SUCCEEDED);
            source = CreateTestTexture(device, Enumerable.Repeat(0xFF808080u, 128 * 128).ToArray(), 128);
            var description = new D3D11_TEXTURE2D_DESC
            {
                Width = 128, Height = 128, MipLevels = 1, ArraySize = 1,
                Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
                Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT, BindFlags = 0x20,
            };
            Assert.True(device->CreateTexture2D(&description, null, &target).SUCCEEDED);
            Assert.True(device->CreateRenderTargetView((ID3D11Resource*)target, null, &view).SUCCEEDED);
            description.Usage = D3D11_USAGE.D3D11_USAGE_STAGING;
            description.BindFlags = 0; description.CPUAccessFlags = 0x20000;
            Assert.True(device->CreateTexture2D(&description, null, &staging).SUCCEEDED);
            renderer = new GlassRenderer(device);
            float* clear = stackalloc float[4] { 0, 0, 0, 0 };
            var clip = Rect(0, 0, 128, 128);
            GlassShape[] shapes = [GlassShape.FromRect(new Vector2(8), new Vector2(112), 12)];
            var drawContext = context;
            var drawView = view;
            var drawTarget = target;
            var readback = staging;
            foreach (var frost in new[] { 0f, .75f, 1f })
            {
                uint Sample(Vector4 tint)
                {
                    drawContext->ClearRenderTargetView(drawView, clear);
                    var material = FrenRiderGlass.CreateMaterial(tint, frost, 1, 1);
                    renderer.Draw(drawContext, source, drawView, shapes, in material, clip);
                    drawContext->CopyResource((ID3D11Resource*)readback, (ID3D11Resource*)drawTarget);
                    D3D11_MAPPED_SUBRESOURCE mapped;
                    Assert.True(drawContext->Map((ID3D11Resource*)readback, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped).SUCCEEDED);
                    try { return ((uint*)((byte*)mapped.pData + 64 * mapped.RowPitch))[64]; }
                    finally { drawContext->Unmap((ID3D11Resource*)readback, 0); }
                }
                var red = Sample(new Vector4(1, 0, 0, .42f));
                var blue = Sample(new Vector4(0, 0, 1, .42f));
                Assert.True(((red >> 16) & 255) > (red & 255) + 25, $"Red tint was not visible at Frost {frost}.");
                Assert.True((blue & 255) > ((blue >> 16) & 255) + 25, $"Blue tint was not visible at Frost {frost}.");
            }
        }
        finally
        {
            renderer?.Dispose();
            if (context != null) context->ClearState();
            if (view != null) view->Release();
            if (staging != null) staging->Release();
            if (target != null) target->Release();
            if (source != null) source->Release();
            if (context != null) context->Release();
            if (device != null) device->Release();
        }
    }

    private unsafe struct MaskedDuplication
    {
        internal void** Vtable;
        internal ID3D11Texture2D* Image;
        internal int FrameReleases;
        internal int Acquires;
        internal long PresentTimestamp;
    }

    [UnmanagedCallersOnly]
    private static unsafe int AcquireMaskedFrame(MaskedDuplication* self, uint timeout,
        DXGI_OUTDUPL_FRAME_INFO* info, IDXGIResource** resource)
    {
        *info = default;
        self->Acquires++;
        info->LastPresentTime.QuadPart = self->PresentTimestamp == 0 ? 42 : self->PresentTimestamp;
        info->ProtectedContentMaskedOut = true;
        self->Image->AddRef();
        *resource = (IDXGIResource*)self->Image;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static unsafe int ReleaseMaskedFrame(MaskedDuplication* self)
    {
        self->FrameReleases++;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static unsafe uint ReleaseMaskedDuplication(MaskedDuplication* self) => 1;

    private unsafe struct RetainedOutput
    {
        internal void** Vtable;
        internal DXGI_OUTPUT_DESC Description;
        internal int DescriptionReads;
    }

    [UnmanagedCallersOnly]
    private static unsafe int ReadRetainedOutput(RetainedOutput* self, DXGI_OUTPUT_DESC* description)
    {
        self->DescriptionReads++;
        *description = self->Description;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static unsafe uint ReleaseRetainedOutput(RetainedOutput* self) => 1;

    [Fact]
    public unsafe void WindowsMaskedFramesRemainUsableAndKeepTheirMaskedPixels()
    {
        ID3D11Device* gameDevice = null;
        ID3D11DeviceContext* gameContext = null;
        ID3D11Device* captureDevice = null;
        ID3D11DeviceContext* captureContext = null;
        ID3D11Texture2D* image = null;
        FrenRiderDesktopCapture? capture = null;
        var duplication = (MaskedDuplication*)NativeMemory.AllocZeroed((nuint)sizeof(MaskedDuplication));
        duplication->Vtable = (void**)NativeMemory.AllocZeroed((nuint)(15 * sizeof(void*)));
        try
        {
            Assert.True(DirectX.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_WARP,
                default, 0, null, 0, 7, &gameDevice, null, &gameContext).SUCCEEDED);
            Assert.True(DirectX.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_WARP,
                default, 0, null, 0, 7, &captureDevice, null, &captureContext).SUCCEEDED);
            uint[] expected = [0xFF000000, 0xFF204060, 0xFF000000, 0xFF10C020];
            image = CreateTestTexture(captureDevice, expected);
            duplication->Image = image;
            duplication->Vtable[2] = (void*)(delegate* unmanaged<MaskedDuplication*, uint>)&ReleaseMaskedDuplication;
            duplication->Vtable[8] = (void*)(delegate* unmanaged<MaskedDuplication*, uint,
                DXGI_OUTDUPL_FRAME_INFO*, IDXGIResource**, int>)&AcquireMaskedFrame;
            duplication->Vtable[14] = (void*)(delegate* unmanaged<MaskedDuplication*, int>)&ReleaseMaskedFrame;
            capture = new((nint)gameDevice, ActivationModeTests.Proxy<IPluginLog>((_, _) => null));
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var adapterType = typeof(FrenRiderDesktopCapture).GetNestedType("AdapterCapture", BindingFlags.NonPublic)!;
            var outputType = typeof(FrenRiderDesktopCapture).GetNestedType("OutputCapture", BindingFlags.NonPublic)!;
            var adapter = Activator.CreateInstance(adapterType, true)!;
            var output = Activator.CreateInstance(outputType, true)!;
            void SetPointer(object owner, string name, void* pointer)
            {
                var field = owner.GetType().GetField(name, flags)!;
                field.SetValue(owner, Pointer.Box(pointer, field.FieldType));
            }
            captureDevice->AddRef(); captureContext->AddRef();
            SetPointer(adapter, "Device", captureDevice);
            SetPointer(adapter, "Context", captureContext);
            outputType.GetField("Adapter", flags)!.SetValue(output, adapter);
            outputType.GetField("Description", flags)!.SetValue(output, new DXGI_OUTPUT_DESC
            {
                DesktopCoordinates = Rect(0, 0, 2, 2), AttachedToDesktop = true,
                Rotation = DXGI_MODE_ROTATION.DXGI_MODE_ROTATION_IDENTITY,
            });
            SetPointer(output, "Duplication", duplication);
            ((System.Collections.IList)typeof(FrenRiderDesktopCapture).GetField("adapters", flags)!.GetValue(capture)!).Add(adapter);
            ((System.Collections.IList)typeof(FrenRiderDesktopCapture).GetField("outputs", flags)!.GetValue(capture)!).Add(output);
            var acquired = (bool)typeof(FrenRiderDesktopCapture).GetMethod("CaptureOutput", flags)!.Invoke(capture, [output])!;
            Assert.True(acquired, "Windows already masked the pixels; its supplied frame must remain usable.");
            Assert.True((bool)outputType.GetField("HasImage", flags)!.GetValue(output)!);
            Assert.Equal(1, duplication->FrameReleases);
            var retained = (ID3D11Texture2D*)Pointer.Unbox(outputType.GetField("CaptureImage", flags)!.GetValue(output)!);
            var staging = (ID3D11Texture2D*)Pointer.Unbox(outputType.GetField("StagingImage", flags)!.GetValue(output)!);
            captureContext->CopyResource((ID3D11Resource*)staging, (ID3D11Resource*)retained);
            D3D11_MAPPED_SUBRESOURCE mapped;
            Assert.True(captureContext->Map((ID3D11Resource*)staging, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped).SUCCEEDED);
            try
            {
                for (var y = 0; y < 2; y++)
                    for (var x = 0; x < 2; x++)
                        Assert.Equal(expected[y * 2 + x], ((uint*)((byte*)mapped.pData + y * mapped.RowPitch))[x]);
            }
            finally { captureContext->Unmap((ID3D11Resource*)staging, 0); }
        }
        finally
        {
            capture?.Dispose();
            if (image != null) image->Release();
            if (captureContext != null) captureContext->Release();
            if (captureDevice != null) captureDevice->Release();
            if (gameContext != null) gameContext->Release();
            if (gameDevice != null) gameDevice->Release();
            NativeMemory.Free(duplication->Vtable); NativeMemory.Free(duplication);
        }
    }

    [Fact]
    public unsafe void NativeLegacyFactoryCaptureProbe()
    {
        IDXGIFactory* factory = null;
        IDXGIAdapter* adapter = null;
        IDXGIOutput* output = null;
        ID3D11Device* device = null;
        ID3D11DeviceContext* context = null;
        HWND window = default;
        var warnings = new List<string>();
        try
        {
            Assert.True(DirectX.CreateDXGIFactory(Win32.__uuidof<IDXGIFactory>(), (void**)&factory).SUCCEEDED);
            Assert.True(factory->EnumAdapters(0, &adapter).SUCCEEDED);
            Assert.True(adapter->EnumOutputs(0, &output).SUCCEEDED);
            DXGI_OUTPUT_DESC description;
            Assert.True(output->GetDesc(&description).SUCCEEDED);
            Assert.True(DirectX.D3D11CreateDevice(adapter, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_UNKNOWN,
                default, 0x20, null, 0, 7, &device, null, &context).SUCCEEDED);
            fixed (char* className = "STATIC")
                window = Win32.CreateWindowExW(0x08200080, className, null, 0x80000000,
                    description.DesktopCoordinates.left + 32, description.DesktopCoordinates.top + 32,
                    128, 128, default, default, default, null);
            Assert.NotEqual(0, (nint)window);
            using var capture = new FrenRiderDesktopCapture((nint)device,
                ActivationModeTests.Proxy<IPluginLog>((method, args) =>
                {
                    if (method.Name == "Warning" && args != null)
                        warnings.Add(string.Join(" ", args.Select(value => value is object[] values ? string.Join(" ", values) : value)));
                    return null;
                }));
            using var exclusion = new FrenRiderCaptureExclusion();
            Assert.True(exclusion.Synchronize([(nint)window], (nint)1, true), exclusion.UnavailableReason);
            capture.UpdateExclusionBoundary([(nint)window], exclusion.BoundaryChangedWindows, exclusion.MinimumFrameTimestamp);
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            nint texture = 0;
            while (texture == 0 && elapsed.Elapsed < TimeSpan.FromSeconds(3))
            {
                capture.BeginFrame();
                _ = capture.TryGetViewportTexture((nint)window, Vector2.Zero, 128, 128,
                    exclusion.Ready, exclusion.MinimumFrameTimestamp, out texture);
                if (texture == 0) Thread.Sleep(16);
            }
            Assert.True(texture != 0, "Legacy-device capture failed: " + string.Join(" | ", warnings));
        }
        finally
        {
            if ((nint)window != 0) _ = Win32.DestroyWindow(window);
            if (context != null) context->Release();
            if (device != null) device->Release();
            if (output != null) output->Release();
            if (adapter != null) adapter->Release();
            if (factory != null) factory->Release();
        }
    }

    [Fact]
    public unsafe void ClippedPanelRenderKeepsSuccessfulWindowCaptureReadiness()
    {
        ID3D11Device* device = null;
        ID3D11DeviceContext* context = null;
        ID3D11Texture2D* target = null;
        ID3D11RenderTargetView* targetView = null;
        var previousContext = ImGui.GetCurrentContext();
        var uiContext = ImGui.CreateContext();
        FrenRiderDetachedGlass? glass = null;
        using var scene = new ExclusionScene();
        try
        {
            Assert.True(DirectX.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_WARP,
                default, 0, null, 0, 7, &device, null, &context).SUCCEEDED);
            var description = new D3D11_TEXTURE2D_DESC
            {
                Width = 128, Height = 128, MipLevels = 1, ArraySize = 1,
                Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
                Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT, BindFlags = 0x20,
            };
            Assert.True(device->CreateTexture2D(&description, null, &target).SUCCEEDED);
            Assert.True(device->CreateRenderTargetView((ID3D11Resource*)target, null, &targetView).SUCCEEDED);
            context->OMSetRenderTargets(1, &targetView, null);
            glass = new((nint)device, ActivationModeTests.Proxy<IPluginLog>((_, _) => null));
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(FrenRiderDetachedGlass).GetField("exclusion", flags)!.SetValue(glass, scene.Helper);
            typeof(FrenRiderDetachedGlass).GetField("enabled", flags)!.SetValue(glass, true);
            Assert.True(scene.Helper.Synchronize([(nint)11], (nint)99, true));
            var ready = (Dictionary<nint, long>)typeof(FrenRiderDetachedGlass)
                .GetField("readyWindows", flags)!.GetValue(glass)!;
            ready[11] = scene.Helper.MinimumFrameTimestamp;
            var viewport = ImGui.GetMainViewport();
            viewport.PlatformHandleRaw = (void*)11;
            var request = new FrenRiderDetachedGlass.DrawRequest
            {
                Hwnd = 11, ViewportId = viewport.ID, Frame = 0,
                Shape = GlassShape.FromRect(new Vector2(10, 300), new Vector2(80, 40), 4),
                Clip = new Vector4(0, 0, 128, 128), Material = GlassMaterial.Regular,
                CaptureBoundary = scene.Helper.MinimumFrameTimestamp, Composite = true,
            };
            // The real production render reaches the target/clip check, with no desktop acquisition.
            glass.Render(&request);
            Assert.True(ready.TryGetValue(11, out var boundary),
                "An offscreen panel erased the full window's successful capture readiness.");
            Assert.Equal(scene.Helper.MinimumFrameTimestamp, boundary);

            // Losing the native window still invalidates readiness rather than retaining stale capture.
            viewport.PlatformHandleRaw = (void*)12;
            glass.Render(&request);
            Assert.False(ready.ContainsKey(11));
        }
        finally
        {
            glass?.Dispose();
            if (context != null) context->ClearState();
            if (targetView != null) targetView->Release();
            if (target != null) target->Release();
            if (context != null) context->Release();
            if (device != null) device->Release();
            ImGui.DestroyContext(uiContext);
            ImGui.SetCurrentContext(previousContext);
        }
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(17u)]
    public unsafe void ScreenshotPauseKeepsRealGlassPixelsVisibleWithoutAcquiringAndRequiresFreshResume(uint originalAffinity)
    {
        using (var scene = new ScreenshotPauseScene(originalAffinity))
        {
            Assert.False(scene.Capture.HasRetainedImages);
            Assert.False(scene.Capture.TrySetPaused(true));
            Assert.False(scene.Glass.CanPauseForScreenshots);
            Assert.False(scene.Glass.TrySetScreenshotsPaused(true));

            scene.Duplication->PresentTimestamp = scene.Exclusion.MinimumFrameTimestamp + 1;
            Assert.All(scene.Render(composite: false), pixel => Assert.Equal(0u, pixel));
            var live = scene.Render(composite: true);
            scene.AssertNonFlatGlass(live);
            Assert.True(scene.Glass.CanPauseForScreenshots);
            Assert.Equal(FrenRiderCaptureExclusion.ExcludeFromCapture, scene.ReadAffinity());
            var acquisitions = scene.Duplication->Acquires;
            var descriptorReads = scene.Output->DescriptionReads;

            Assert.True(scene.Glass.TrySetScreenshotsPaused(true));
            Assert.False(scene.Glass.ScreenshotsPaused); // UI requests commit before the next frame's recording.
            scene.CheckPauseRestoreOrder = true;
            scene.AdvanceFrame();
            scene.CheckPauseRestoreOrder = false;
            Assert.True(scene.Glass.ScreenshotsPaused);
            Assert.True(scene.Capture.IsPaused);
            Assert.Equal(0u, scene.ReadAffinity());
            Assert.False(scene.Exclusion.Ready);

            scene.ChangeFutureInput();
            for (var index = 0; index < 2; index++)
            {
                scene.AdvanceFrame();
                var frozen = scene.Render(composite: true);
                scene.AssertNonFlatGlass(frozen);
                Assert.Equal(live, frozen);
                Assert.Equal(acquisitions, scene.Duplication->Acquires);
                Assert.Equal(descriptorReads, scene.Output->DescriptionReads);
                Assert.Equal(0u, scene.ReadAffinity());
            }

            var oldBoundary = scene.Exclusion.MinimumFrameTimestamp;
            Assert.True(scene.Glass.TrySetScreenshotsPaused(false));
            Assert.True(scene.Glass.ScreenshotsPaused);
            scene.AdvanceFrame();
            Assert.False(scene.Glass.ScreenshotsPaused);
            Assert.False(scene.Capture.IsPaused);
            Assert.False(scene.Glass.CanPauseForScreenshots);
            Assert.Equal(FrenRiderCaptureExclusion.ExcludeFromCapture, scene.ReadAffinity());
            Assert.True(scene.Exclusion.MinimumFrameTimestamp > oldBoundary);
            Assert.False(scene.Capture.TryGetViewportTexture(scene.Window, scene.Origin, 128, 128,
                scene.Exclusion.Ready, scene.Exclusion.MinimumFrameTimestamp, out var stale));
            Assert.Equal(0, stale);
            Assert.All(scene.Render(composite: true), pixel => Assert.Equal(0u, pixel));
            Assert.False(scene.Ready.ContainsKey(scene.Window));

            scene.Duplication->PresentTimestamp = scene.Exclusion.MinimumFrameTimestamp + 1;
            scene.AdvanceFrame();
            Assert.All(scene.Render(composite: false), pixel => Assert.Equal(0u, pixel));
            Assert.Equal(scene.Exclusion.MinimumFrameTimestamp, scene.Ready[scene.Window]);
            Assert.Contains(scene.Render(composite: true).Zip(live), pair => pair.First != pair.Second);
            Assert.True(scene.Glass.CanPauseForScreenshots);

            Assert.True(scene.Glass.TrySetScreenshotsPaused(true));
            scene.AdvanceFrame();
            Assert.True(scene.Glass.ScreenshotsPaused);
            scene.Glass.EndFrame(1, false);
            Assert.False(scene.Glass.ScreenshotsPaused);
            Assert.False(scene.Capture.IsPaused);
            Assert.False(scene.Capture.HasRetainedImages);
            Assert.Equal(scene.OriginalAffinity, scene.ReadAffinity());
            Assert.Empty(scene.Ready);
            Assert.False(scene.Glass.TrySetScreenshotsPaused(true));
            Assert.Empty(scene.Warnings);
        }

        using (var scene = new ScreenshotPauseScene(originalAffinity))
        {
            scene.Duplication->PresentTimestamp = scene.Exclusion.MinimumFrameTimestamp + 1;
            _ = scene.Render(composite: false);
            _ = scene.Render(composite: true);
            Assert.True(scene.Glass.TrySetScreenshotsPaused(true));
            scene.AdvanceFrame();
            Assert.True(scene.Glass.ScreenshotsPaused);
            scene.Glass.Dispose();
            Assert.False(scene.Glass.ScreenshotsPaused);
            Assert.False(scene.Capture.IsPaused);
            Assert.False(scene.Capture.HasRetainedImages);
            Assert.Equal(scene.OriginalAffinity, scene.ReadAffinity());
            Assert.False(scene.Glass.TrySetScreenshotsPaused(true));
            Assert.Empty(scene.Warnings);
        }
    }

    [Fact]
    public unsafe void NativeExclusionRestoresAnOwnedHiddenDirectCompositionStyleWindow()
    {
        Assert.True(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041));
        HWND window = default;
        try
        {
            fixed (char* className = "STATIC")
                window = Win32.CreateWindowExW(0x08200080, className, null, 0x80000000,
                    -32000, -32000, 32, 32, default, default, default, null);
            Assert.NotEqual(0, (nint)window);
            uint original = 0;
            Assert.True(Win32.GetWindowDisplayAffinity(window, &original).Value != 0,
                $"Original hidden-window affinity is unreadable: {Win32.GetLastError()}");
            using var exclusion = new FrenRiderCaptureExclusion();
            Assert.True(exclusion.Synchronize([(nint)window], (nint)1, true), exclusion.UnavailableReason);
            uint excluded = 0;
            Assert.True(Win32.GetWindowDisplayAffinity(window, &excluded).Value != 0);
            Assert.Equal(FrenRiderCaptureExclusion.ExcludeFromCapture, excluded);
            Assert.True(exclusion.RestoreAll(), exclusion.UnavailableReason);
            uint restored = 0;
            Assert.True(Win32.GetWindowDisplayAffinity(window, &restored).Value != 0);
            Assert.Equal(original, restored);
        }
        finally { if ((nint)window != 0) _ = Win32.DestroyWindow(window); }
    }

    [Fact]
    public unsafe void NativeBackdropCompositionRetainsPremultipliedWavesOverCapturedPixels()
    {
        ID3D11Device* device = null;
        ID3D11DeviceContext* context = null;
        ID3D11Texture2D* desktop = null;
        ID3D11Texture2D* window = null;
        ID3D11Texture2D* readback = null;
        FrenRiderDesktopCapture? capture = null;
        try
        {
            var created = DirectX.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_WARP,
                default, 0, null, 0, 7, &device, null, &context);
            Assert.True(created.SUCCEEDED, $"Software Direct3D device failed: 0x{created.Value:X8}");
            desktop = CreateTestTexture(device, [0xFF204060, 0xFF204060, 0xFF204060, 0xFF204060]);
            window = CreateTestTexture(device, [0, 0x80800000, 0xFF10C020, 0x40000040]);
            capture = new FrenRiderDesktopCapture((nint)device, ActivationModeTests.Proxy<IPluginLog>((_, _) => null));
            Assert.True(capture.TryComposeWindowBackdrop((nint)desktop, (nint)window, out var result));
            Assert.NotEqual(0, result);
            var description = new D3D11_TEXTURE2D_DESC
            {
                Width = 2, Height = 2, MipLevels = 1, ArraySize = 1,
                Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
                Usage = D3D11_USAGE.D3D11_USAGE_STAGING, CPUAccessFlags = 0x20000,
            };
            Assert.True(device->CreateTexture2D(&description, null, &readback).SUCCEEDED);
            context->CopyResource((ID3D11Resource*)readback, (ID3D11Resource*)result);
            D3D11_MAPPED_SUBRESOURCE mapped;
            Assert.True(context->Map((ID3D11Resource*)readback, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped).SUCCEEDED);
            try
            {
                uint[] expected = [0xFF204060, 0xFF902030, 0xFF10C020, 0xFF183088];
                for (var y = 0; y < 2; y++)
                    for (var x = 0; x < 2; x++)
                    {
                        var actual = ((uint*)((byte*)mapped.pData + y * mapped.RowPitch))[x];
                        for (var shift = 0; shift < 32; shift += 8)
                            Assert.InRange((int)((actual >> shift) & 255),
                                Math.Max(0, (int)((expected[y * 2 + x] >> shift) & 255) - 1),
                                Math.Min(255, (int)((expected[y * 2 + x] >> shift) & 255) + 1));
                    }
            }
            finally { context->Unmap((ID3D11Resource*)readback, 0); }
        }
        finally
        {
            capture?.Dispose();
            if (context != null) context->ClearState();
            if (readback != null) readback->Release();
            if (window != null) window->Release();
            if (desktop != null) desktop->Release();
            if (context != null) context->Release();
            if (device != null) device->Release();
        }
    }

    private static unsafe ID3D11Texture2D* CreateTestTexture(ID3D11Device* device, uint[] pixels, uint side = 2)
    {
        var description = new D3D11_TEXTURE2D_DESC
        {
            Width = side, Height = side, MipLevels = 1, ArraySize = 1,
            Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT, BindFlags = 8,
        };
        ID3D11Texture2D* texture = null;
        fixed (uint* data = pixels)
        {
            var initial = new D3D11_SUBRESOURCE_DATA { pSysMem = data, SysMemPitch = side * 4 };
            var created = device->CreateTexture2D(&description, &initial, &texture);
            Assert.True(created.SUCCEEDED, $"Synthetic texture creation failed: 0x{created.Value:X8}");
        }
        return texture;
    }

    [Fact]
    public void PhysicalMonitorRegionsMapIntoTheActualTargetWithoutScalingScreenOrigins()
    {
        var physicalClient = Rect(-3840, -2160, 0, 0);
        Assert.Equal(new Vector4(0, 0, 960, 1080), FrenRiderDesktopCapture.MapAtlasDestination(
            Rect(0, 0, 1920, 2160), physicalClient, 1920, 1080));
        Assert.Equal(new Vector4(960, 0, 1920, 1080), FrenRiderDesktopCapture.MapAtlasDestination(
            Rect(1920, 0, 3840, 2160), physicalClient, 1920, 1080));
        Assert.Equal(new Vector4(7.5f, 15, 75, 90), FrenRiderDesktopCapture.MapAtlasDestination(
            Rect(10, 20, 100, 120), Rect(-200, -300, 200, 100), 300, 300));
    }

    [Theory]
    [InlineData(DXGI_MODE_ROTATION.DXGI_MODE_ROTATION_UNSPECIFIED, 0, 0, 1, 0, 0, 1)]
    [InlineData(DXGI_MODE_ROTATION.DXGI_MODE_ROTATION_IDENTITY, 0, 0, 1, 0, 0, 1)]
    [InlineData(DXGI_MODE_ROTATION.DXGI_MODE_ROTATION_ROTATE90, 0, 1, 0, 0, 1, 1)]
    [InlineData(DXGI_MODE_ROTATION.DXGI_MODE_ROTATION_ROTATE180, 1, 1, 0, 1, 1, 0)]
    [InlineData(DXGI_MODE_ROTATION.DXGI_MODE_ROTATION_ROTATE270, 1, 0, 1, 1, 0, 0)]
    public void OutputRotationMapsDisplayedCornersIntoTheAcquiredTexture(
        DXGI_MODE_ROTATION rotation, float leftU, float leftV, float rightU, float rightV, float bottomU, float bottomV)
    {
        var monitor = Rect(-1080, -1920, 0, 0);
        Assert.True(FrenRiderDesktopCapture.TryMapOutput(monitor, monitor, rotation, out var destination, out var uv));
        Assert.Equal(Rect(0, 0, 1080, 1920), destination);
        Point(new(leftU, leftV), Vector2.Transform(Vector2.Zero, uv));
        Point(new(rightU, rightV), Vector2.Transform(Vector2.UnitX, uv));
        Point(new(bottomU, bottomV), Vector2.Transform(Vector2.UnitY, uv));
    }

    [Fact]
    public void NegativeMonitorOriginAndClippedEdgesUsePhysicalAtlasCoordinates()
    {
        var monitor = Rect(-1920, 0, 0, 1080);
        Assert.True(FrenRiderDesktopCapture.TryMapOutput(Rect(-1536, 270, -768, 810), monitor,
            DXGI_MODE_ROTATION.DXGI_MODE_ROTATION_IDENTITY, out var destination, out var uv));
        Assert.Equal(Rect(0, 0, 768, 540), destination);
        Point(new(.2f, .25f), Vector2.Transform(Vector2.Zero, uv));
        Point(new(.6f, .75f), Vector2.Transform(Vector2.One, uv));

        Assert.True(FrenRiderDesktopCapture.TryMapOutput(Rect(-2000, -100, -1600, 300), monitor,
            DXGI_MODE_ROTATION.DXGI_MODE_ROTATION_IDENTITY, out destination, out uv));
        Assert.Equal(Rect(80, 100, 400, 400), destination);
        Point(Vector2.Zero, Vector2.Transform(Vector2.Zero, uv));
        Point(new(1f / 6, 5f / 18), Vector2.Transform(Vector2.One, uv));
    }

    [Fact]
    public void SpanningViewportCopiesBothOutputsIntoAdjacentAtlasRegions()
    {
        var viewport = Rect(-480, 270, 480, 810);
        Assert.True(FrenRiderDesktopCapture.TryMapOutput(viewport, Rect(-1920, 0, 0, 1080),
            DXGI_MODE_ROTATION.DXGI_MODE_ROTATION_IDENTITY, out var left, out var leftUv));
        Assert.True(FrenRiderDesktopCapture.TryMapOutput(viewport, Rect(0, 0, 1920, 1080),
            DXGI_MODE_ROTATION.DXGI_MODE_ROTATION_IDENTITY, out var right, out var rightUv));
        Assert.Equal(Rect(0, 0, 480, 540), left);
        Assert.Equal(Rect(480, 0, 960, 540), right);
        Assert.Equal(left.right, right.left);
        Point(new(.75f, .25f), Vector2.Transform(Vector2.Zero, leftUv));
        Point(new(1, .75f), Vector2.Transform(Vector2.One, leftUv));
        Point(new(0, .25f), Vector2.Transform(Vector2.Zero, rightUv));
        Point(new(.25f, .75f), Vector2.Transform(Vector2.One, rightUv));
    }

    [Fact]
    public void EmptyOrNonIntersectingOutputBoundsCannotSupplyCapturePixels()
    {
        foreach (var (viewport, output) in new[]
        {
            (Rect(0, 0, 0, 100), Rect(0, 0, 1920, 1080)),
            (Rect(0, 0, 100, 100), Rect(0, 0, 1920, 0)),
            (Rect(-100, -100, 0, 0), Rect(0, 0, 1920, 1080)),
        })
            Assert.False(FrenRiderDesktopCapture.TryMapOutput(viewport, output,
                DXGI_MODE_ROTATION.DXGI_MODE_ROTATION_IDENTITY, out _, out _));
    }

    [Fact]
    public void DetachedCallbackTranslatesScreenGeometryOnceAndRoundsItsActualTargetClipOutwards()
    {
        var shape = GlassShape.FromRect(new(-1800, -1000), new(400, 300), 16);
        Assert.True(FrenRiderDetachedGlass.TryTranslateToTarget(in shape, new(-1920, -1080),
            new(-1800.25f, -1000.75f, -1400.25f, -700.25f), 800, 600, out var target, out var clip));
        Assert.Equal(new Vector2(120, 80), target.Min);
        Assert.Equal(new Vector2(520, 380), target.Max);
        Assert.Equal(16, target.Radius);
        Assert.Equal(Rect(119, 79, 520, 380), clip);

        Assert.True(FrenRiderDetachedGlass.TryTranslateToTarget(in shape, new(-1920, -1080),
            new(-3000, -2000, 3000, 2000), 800, 600, out target, out clip));
        Assert.Equal(new Vector2(120, 80), target.Min);
        Assert.Equal(Rect(0, 0, 800, 600), clip);
    }

    [Fact]
    public void DetachedCallbackRejectsInvalidOrClippedGeometryButRetainsVisibleSamplingReach()
    {
        var shape = GlassShape.FromRect(new(10, 10), new(40, 30), 4);
        Assert.False(FrenRiderDetachedGlass.TryTranslateToTarget(in shape, Vector2.Zero,
            new(0, 0, 800, 600), 0, 600, out _, out _));
        Assert.False(FrenRiderDetachedGlass.TryTranslateToTarget(in shape, new(float.NaN, 0),
            new(0, 0, 800, 600), 800, 600, out _, out _));
        Assert.False(FrenRiderDetachedGlass.TryTranslateToTarget(in shape, Vector2.Zero,
            new(0, 0, float.PositiveInfinity, 600), 800, 600, out _, out _));
        Assert.False(FrenRiderDetachedGlass.TryTranslateToTarget(in shape, Vector2.Zero,
            new(100, 100, 200, 200), 800, 600, out _, out _));
        var outside = GlassShape.FromRect(new(-1940, 100), new(10, 20), 3);
        Assert.False(FrenRiderDetachedGlass.TryTranslateToTarget(in outside, new(-1920, 0),
            new(-1920, 0, -1120, 600), 800, 600, out _, out _));
        Assert.True(FrenRiderDetachedGlass.TryTranslateToTarget(in outside, new(-1920, 0),
            new(-1920, 0, -1120, 600), 800, 600, out var target, out _, samplingReach: 15));
        Assert.Equal(new Vector2(-20, 100), target.Min);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(.5f)]
    [InlineData(1f)]
    public void CommonMaterialKeepsContinuousFrostTintAndOpacityAndScalesLengthsOnce(float frost)
    {
        var material = FrenRiderGlass.CreateMaterial(new(.2f, .3f, .4f, .18f), frost, .5f, 4);
        Assert.Equal(frost, material.Frost);
        Assert.Equal(new Glasswork.Color(.2f, .3f, .4f), material.Tint);
        Assert.Equal(.18f, material.TintAmount);
        Assert.Equal(.5f, material.Opacity);
        Assert.Equal(112f, material.Bezel);
        Assert.Equal(160f, material.Depth);
        Assert.Equal(4.8f, material.BlurSpread, .0001f);
        Assert.Equal(3, material.BlurLevels);
    }

    [Fact]
    public void CommonMaterialClampsUserFractionsAndKeepsNonfiniteOpacityVisible()
    {
        var minimum = FrenRiderGlass.CreateMaterial(new(.2f, .3f, .4f, -1), -1, -1, 1);
        Assert.Equal(0, minimum.Frost);
        Assert.Equal(0, minimum.TintAmount);
        Assert.Equal(0, minimum.Opacity);
        var maximum = FrenRiderGlass.CreateMaterial(new(.2f, .3f, .4f, 2), 2, 2, 1);
        Assert.Equal(1, maximum.Frost);
        Assert.Equal(1, maximum.TintAmount);
        Assert.Equal(1, maximum.Opacity);
        Assert.Equal(1, FrenRiderGlass.CreateMaterial(Vector4.Zero, .5f, float.NaN, 1).Opacity);
    }

    [Fact]
    public void ExclusionReadsOriginalAndVerifiesTheWriteBeforePublishingAFreshCaptureBoundary()
    {
        using var scene = new ExclusionScene();
        scene.Windows[11].Affinity = 1;
        Assert.True(scene.Helper.Synchronize([11], 99, true));
        Assert.True(scene.Helper.Ready);
        Assert.Equal(1000, scene.Helper.MinimumFrameTimestamp);
        Assert.True(scene.Calls.IndexOf("read:11") < scene.Calls.IndexOf("write:11:17"));
        Assert.True(scene.Calls.IndexOf("claim:11") < scene.Calls.IndexOf("write:11:17"));
        Assert.Contains(scene.Calls.Skip(scene.Calls.IndexOf("write:11:17") + 1), call => call == "read:11");
        Assert.True(scene.Calls.IndexOf("time") > scene.Calls.IndexOf("write:11:17"));
        Assert.NotEqual(0, scene.Windows[11].Marker);
        scene.Clock = 2000;
        Assert.True(scene.Helper.Synchronize([11], 99, true));
        Assert.Equal(1000, scene.Helper.MinimumFrameTimestamp);
        Assert.Single(scene.Writes);

        Assert.True(scene.Helper.Synchronize([], 99, false));
        Assert.False(scene.Helper.Ready);
        Assert.Equal(1u, scene.Windows[11].Affinity);
        Assert.Equal(0, scene.Windows[11].Marker);
        Assert.Equal(new[] { ((nint)11, 17u), ((nint)11, 1u) }, scene.Writes);
        scene.Helper.Dispose();
        Assert.Equal(2, scene.Writes.Count);
    }

    [Fact]
    public void UnreadableOrUnsupportedCaptureExclusionFailsClosedWithoutChangingWindows()
    {
        using (var unsupported = new ExclusionScene(supported: false))
        {
            Assert.False(unsupported.Helper.Synchronize([11], 99, true));
            Assert.False(unsupported.Helper.Ready);
            Assert.False(string.IsNullOrWhiteSpace(unsupported.Helper.UnavailableReason));
            Assert.Empty(unsupported.Writes);
        }
        using var unreadable = new ExclusionScene();
        unreadable.Windows[11].Readable = false;
        Assert.False(unreadable.Helper.Synchronize([11], 99, true));
        Assert.False(unreadable.Helper.Ready);
        Assert.False(string.IsNullOrWhiteSpace(unreadable.Helper.UnavailableReason));
        Assert.Empty(unreadable.Writes);
        Assert.Equal(0, unreadable.Windows[11].Marker);
    }

    [Fact]
    public void ExclusionNeverTouchesTheGameForeignOrInvalidWindows()
    {
        using var scene = new ExclusionScene();
        scene.Windows[33] = new() { CurrentProcessTopLevel = false };
        Assert.False(scene.Helper.Synchronize([0, 99, 33], 99, true));
        Assert.False(scene.Helper.Ready);
        Assert.Empty(scene.Writes);
        Assert.Equal(0, scene.Windows[99].Marker);
        Assert.Equal(0, scene.Windows[33].Marker);
    }

    [Fact]
    public void ExternalAffinityChangesAreYieldedAndNeverOverwrittenDuringCleanup()
    {
        using var scene = new ExclusionScene();
        Assert.True(scene.Helper.Synchronize([11], 99, true));
        scene.Windows[11].Affinity = 1;
        Assert.False(scene.Helper.Synchronize([11], 99, true));
        Assert.False(scene.Helper.Ready);
        Assert.True(scene.Helper.RestoreAll());
        Assert.Equal(1u, scene.Windows[11].Affinity);
        Assert.Single(scene.Writes);
    }

    [Fact]
    public void RecycledOrDestroyedHandlesCannotBeRestoredAsThePreviousWindow()
    {
        using (var recycled = new ExclusionScene())
        {
            Assert.True(recycled.Helper.Synchronize([11], 99, true));
            recycled.Windows[11].Marker = 1234;
            recycled.Windows[11].Affinity = 1;
            recycled.Helper.Dispose();
            Assert.Equal(1u, recycled.Windows[11].Affinity);
            Assert.Equal((nint)1234, recycled.Windows[11].Marker);
            Assert.Single(recycled.Writes);
        }
        using var destroyed = new ExclusionScene();
        Assert.True(destroyed.Helper.Synchronize([11], 99, true));
        destroyed.Windows[11].Alive = false;
        destroyed.Windows[11].Marker = 0;
        destroyed.Helper.Dispose();
        Assert.Single(destroyed.Writes);
    }

    [Fact]
    public void PartialAdmissionFailureStillRestoresPreviouslyOwnedExclusionOnDispose()
    {
        using var scene = new ExclusionScene();
        scene.Windows[11].Affinity = 1;
        scene.Windows[22] = new() { Readable = false };
        Assert.True(scene.Helper.Synchronize([11], 99, true));
        Assert.False(scene.Helper.Synchronize([11, 22], 99, true));
        Assert.False(scene.Helper.Ready);
        scene.Helper.Dispose();
        Assert.Equal(1u, scene.Windows[11].Affinity);
        Assert.Equal(0, scene.Windows[11].Marker);
        Assert.DoesNotContain(scene.Writes, write => write.Window == 22);
    }

    [Fact]
    public void FailedAffinityRestoreRetainsOwnershipForAnExplicitCleanupRetry()
    {
        using var scene = new ExclusionScene();
        scene.Windows[11].Affinity = 1;
        Assert.True(scene.Helper.Synchronize([11], 99, true));
        scene.Windows[11].RejectedAffinities.Add(1);
        Assert.False(scene.Helper.RestoreAll());
        Assert.False(scene.Helper.Ready);
        Assert.False(string.IsNullOrWhiteSpace(scene.Helper.UnavailableReason));
        Assert.NotEqual(0, scene.Windows[11].Marker);
        scene.Windows[11].RejectedAffinities.Clear();
        Assert.True(scene.Helper.RestoreAll());
        Assert.Equal(1u, scene.Windows[11].Affinity);
        Assert.Equal(0, scene.Windows[11].Marker);
    }

    [Fact]
    public void ChangedExclusionOwnershipRequiresANewBoundaryWithoutClearingAPreexistingExclusion()
    {
        using var scene = new ExclusionScene();
        scene.Windows[11].Affinity = 17;
        scene.Windows[22] = new();
        Assert.True(scene.Helper.Synchronize([11], 99, true));
        Assert.Empty(scene.Writes);
        Assert.Equal(1000, scene.Helper.MinimumFrameTimestamp);
        scene.Clock = 2000;
        Assert.True(scene.Helper.Synchronize([11, 22], 99, true));
        Assert.Equal(2000, scene.Helper.MinimumFrameTimestamp);
        scene.Clock = 3000;
        Assert.True(scene.Helper.Synchronize([22], 99, true));
        Assert.Equal(3000, scene.Helper.MinimumFrameTimestamp);
        Assert.Equal(17u, scene.Windows[11].Affinity);
        Assert.Equal(0, scene.Windows[11].Marker);
        Assert.DoesNotContain(scene.Writes, write => write.Window == 11);
        scene.Helper.Dispose();
        Assert.Equal(0u, scene.Windows[22].Affinity);
        Assert.Equal(0, scene.Windows[22].Marker);
    }

    [Theory]
    [InlineData("marker", 0u)]
    [InlineData("write", 0u)]
    [InlineData("readback", 0u)]
    [InlineData("changed readback", 1u)]
    public void FailedExclusionAdmissionNeverReportsReadyAndCleansUpOnlyItsAppliedState(string failure, uint finalAffinity)
    {
        using var scene = new ExclusionScene();
        var window = scene.Windows[11];
        if (failure == "marker") window.ClaimSucceeds = false;
        if (failure == "write") window.RejectedAffinities.Add(17);
        if (failure == "readback") window.FailReadAt = 3;
        if (failure == "changed readback") window.RetainedAffinity = 1;
        Assert.False(scene.Helper.Synchronize([11], 99, true));
        Assert.False(scene.Helper.Ready);
        Assert.False(string.IsNullOrWhiteSpace(scene.Helper.UnavailableReason));
        scene.Helper.Dispose();
        Assert.Equal(finalAffinity, window.Affinity);
        Assert.Equal(0, window.Marker);
        if (failure == "marker") Assert.Empty(scene.Writes);
        if (failure == "write") Assert.Single(scene.Writes);
        if (failure == "changed readback") Assert.Single(scene.Writes);
    }

    private sealed unsafe class ScreenshotPauseScene : IDisposable
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly ImGuiContextPtr previousContext = ImGui.GetCurrentContext();
        private ImGuiContextPtr uiContext;
        private DPI_AWARENESS_CONTEXT previousDpi;
        private ID3D11Device* device;
        private ID3D11DeviceContext* context;
        private ID3D11Texture2D* source;
        private ID3D11Texture2D* target;
        private ID3D11Texture2D* readback;
        private ID3D11RenderTargetView* targetView;
        private bool disposed;
        internal readonly Vector2 Origin = new(-32000);
        internal readonly List<string> Warnings = [];
        internal MaskedDuplication* Duplication;
        internal RetainedOutput* Output;
        internal FrenRiderDetachedGlass Glass = null!;
        internal FrenRiderDesktopCapture Capture = null!;
        internal FrenRiderCaptureExclusion Exclusion = null!;
        internal nint Window;
        internal uint OriginalAffinity;
        internal bool CheckPauseRestoreOrder;
        internal Dictionary<nint, long> Ready => (Dictionary<nint, long>)Get(Glass, "readyWindows");
        private HashSet<nint> Used => (HashSet<nint>)Get(Glass, "usedWindows");

        internal ScreenshotPauseScene(uint initialAffinity = 0)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                throw new PlatformNotSupportedException("Screenshot capture affinity requires Windows 10 version 2004 or later.");
            try
            {
                previousDpi = Win32.SetThreadDpiAwarenessContext((DPI_AWARENESS_CONTEXT)(nint)(-4));
                Assert.NotEqual(0, (nint)previousDpi);
                uiContext = ImGui.CreateContext();
                ID3D11Device* createdDevice = null;
                ID3D11DeviceContext* createdContext = null;
                Assert.True(DirectX.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_WARP,
                    default, 0, null, 0, 7, &createdDevice, null, &createdContext).SUCCEEDED);
                device = createdDevice; context = createdContext;
                fixed (char* className = "STATIC")
                    Window = (nint)Win32.CreateWindowExW(0x08200080, className, null, 0x80000000,
                        -32000, -32000, 128, 128, default, default, default, null);
                Assert.NotEqual(0, Window);
                Assert.True(Win32.SetWindowDisplayAffinity((HWND)Window, initialAffinity).Value != 0);
                OriginalAffinity = ReadAffinity();
                var viewport = ImGui.GetMainViewport();
                viewport.Pos = Origin;
                viewport.Size = new Vector2(128);
                viewport.PlatformHandleRaw = (void*)Window;
                ImGui.GetIO().DeltaTime = 1f / 60;

                var description = new D3D11_TEXTURE2D_DESC
                {
                    Width = 128, Height = 128, MipLevels = 1, ArraySize = 1,
                    Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                    SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
                    Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT, BindFlags = 0x20,
                };
                ID3D11Texture2D* createdTexture = null;
                Assert.True(device->CreateTexture2D(&description, null, &createdTexture).SUCCEEDED);
                target = createdTexture;
                ID3D11RenderTargetView* createdView = null;
                Assert.True(device->CreateRenderTargetView((ID3D11Resource*)target, null, &createdView).SUCCEEDED);
                targetView = createdView;
                description.Usage = D3D11_USAGE.D3D11_USAGE_STAGING;
                description.BindFlags = 0; description.CPUAccessFlags = 0x20000;
                createdTexture = null;
                Assert.True(device->CreateTexture2D(&description, null, &createdTexture).SUCCEEDED);
                readback = createdTexture;
                var pixels = new uint[128 * 128];
                for (var y = 0; y < 128; y++)
                    for (var x = 0; x < 128; x++)
                    {
                        var shade = (uint)(((x / 2 + y / 2) & 1) * 24);
                        pixels[y * 128 + x] = (x < 64 ? 0xFF204080u : 0xFF804020u) + shade * 0x010101u;
                    }
                source = CreateTestTexture(device, pixels, 128);
                var log = ActivationModeTests.Proxy<IPluginLog>((method, args) =>
                {
                    if (method.Name == "Warning" && args != null)
                        Warnings.Add(string.Join(" ", args.Select(value => value is object[] values ? string.Join(" ", values) : value)));
                    return null;
                });
                Glass = new FrenRiderDetachedGlass((nint)device, log);
                Capture = (FrenRiderDesktopCapture)Get(Glass, "capture");
                ((FrenRiderCaptureExclusion)Get(Glass, "exclusion")).Dispose();
                var native = (FrenRiderCaptureExclusion.Native)typeof(FrenRiderCaptureExclusion)
                    .GetMethod("CreateNative", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
                Exclusion = new FrenRiderCaptureExclusion(native with
                {
                    SetAffinity = (window, affinity) =>
                    {
                        if (CheckPauseRestoreOrder && window == Window && affinity == 0)
                            Assert.True(Capture.IsPaused, "Affinity was restored before live acquisition stopped.");
                        return native.SetAffinity(window, affinity);
                    },
                });
                Set(Glass, "exclusion", Exclusion);

                Duplication = (MaskedDuplication*)NativeMemory.AllocZeroed((nuint)sizeof(MaskedDuplication));
                Duplication->Vtable = (void**)NativeMemory.AllocZeroed((nuint)(15 * sizeof(void*)));
                Duplication->Image = source;
                Duplication->Vtable[2] = (void*)(delegate* unmanaged<MaskedDuplication*, uint>)&ReleaseMaskedDuplication;
                Duplication->Vtable[8] = (void*)(delegate* unmanaged<MaskedDuplication*, uint,
                    DXGI_OUTDUPL_FRAME_INFO*, IDXGIResource**, int>)&AcquireMaskedFrame;
                Duplication->Vtable[14] = (void*)(delegate* unmanaged<MaskedDuplication*, int>)&ReleaseMaskedFrame;
                Output = (RetainedOutput*)NativeMemory.AllocZeroed((nuint)sizeof(RetainedOutput));
                Output->Vtable = (void**)NativeMemory.AllocZeroed((nuint)(8 * sizeof(void*)));
                Output->Vtable[2] = (void*)(delegate* unmanaged<RetainedOutput*, uint>)&ReleaseRetainedOutput;
                Output->Vtable[7] = (void*)(delegate* unmanaged<RetainedOutput*, DXGI_OUTPUT_DESC*, int>)&ReadRetainedOutput;
                Output->Description = new DXGI_OUTPUT_DESC
                {
                    DesktopCoordinates = Rect(-32000, -32000, -31872, -31872), AttachedToDesktop = true,
                    Rotation = DXGI_MODE_ROTATION.DXGI_MODE_ROTATION_IDENTITY, Monitor = (HMONITOR)(nint)7,
                };
                var adapterType = typeof(FrenRiderDesktopCapture).GetNestedType("AdapterCapture", BindingFlags.NonPublic)!;
                var outputType = typeof(FrenRiderDesktopCapture).GetNestedType("OutputCapture", BindingFlags.NonPublic)!;
                var adapter = Activator.CreateInstance(adapterType, true)!;
                var output = Activator.CreateInstance(outputType, true)!;
                device->AddRef(); context->AddRef();
                SetPointer(adapter, "Device", device);
                SetPointer(adapter, "Context", context);
                Set(output, "Adapter", adapter);
                Set(output, "Description", Output->Description);
                SetPointer(output, "Output", Output);
                SetPointer(output, "Duplication", Duplication);
                ((System.Collections.IList)Get(Capture, "adapters")).Add(adapter);
                ((System.Collections.IList)Get(Capture, "outputs")).Add(output);
                Set(Capture, "topologyNeeded", false); // This fixture supplies its monitor; never enumerate the real desktop.
                Used.Add(Window);
                Glass.EndFrame(1, true);
                Assert.True(Exclusion.Ready, Exclusion.UnavailableReason);
            }
            catch { Dispose(); throw; }
        }

        internal void AdvanceFrame()
        {
            Set(Capture, "topologyNeeded", true); // Avoid probing an adapter owned by this synthetic output.
            Glass.BeginFrame();
            Set(Capture, "topologyNeeded", false);
            Used.Add(Window);
            Glass.EndFrame(1, true);
        }

        internal uint ReadAffinity()
        {
            uint affinity = 0;
            Assert.True(Win32.GetWindowDisplayAffinity((HWND)Window, &affinity).Value != 0);
            return affinity;
        }

        internal uint[] Render(bool composite)
        {
            float* clear = stackalloc float[4] { 0, 0, 0, 0 };
            context->ClearRenderTargetView(targetView, clear);
            var view = targetView;
            context->OMSetRenderTargets(1, &view, null);
            var request = new FrenRiderDetachedGlass.DrawRequest
            {
                Hwnd = Window, ViewportId = ImGui.GetMainViewport().ID,
                Frame = (int)Get(Glass, "frame"), ViewportOrigin = Origin,
                Shape = GlassShape.FromRect(Origin + new Vector2(8), new Vector2(112), 12),
                Clip = new Vector4(Origin.X, Origin.Y, Origin.X + 128, Origin.Y + 128),
                Material = FrenRiderGlass.CreateMaterial(new Vector4(.2f, .3f, .4f, .42f), 1, 1, 1),
                CaptureBoundary = Exclusion.MinimumFrameTimestamp, Composite = composite, WindowBackground = true,
            };
            Glass.Render(&request);
            context->CopyResource((ID3D11Resource*)readback, (ID3D11Resource*)target);
            D3D11_MAPPED_SUBRESOURCE mapped;
            Assert.True(context->Map((ID3D11Resource*)readback, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped).SUCCEEDED);
            try
            {
                var pixels = new uint[128 * 128];
                for (var y = 0; y < 128; y++)
                    new ReadOnlySpan<uint>((byte*)mapped.pData + y * mapped.RowPitch, 128).CopyTo(pixels.AsSpan(y * 128));
                return pixels;
            }
            finally { context->Unmap((ID3D11Resource*)readback, 0); }
        }

        internal void AssertNonFlatGlass(uint[] pixels)
        {
            var left = pixels[64 * 128 + 40];
            var right = pixels[64 * 128 + 88];
            Assert.Equal(255u, left >> 24);
            Assert.Equal(255u, right >> 24);
            Assert.True(Enumerable.Range(0, 3).Any(channel =>
                Math.Abs((int)((left >> (channel * 8)) & 255) - (int)((right >> (channel * 8)) & 255)) > 10),
                "The actual Glasswork target lost the retained backdrop's spatial detail.");
        }

        internal void ChangeFutureInput()
        {
            var pixels = Enumerable.Repeat(0xFF30D060u, 128 * 128).ToArray();
            fixed (uint* data = pixels)
                context->UpdateSubresource((ID3D11Resource*)source, 0, null, data, 128 * 4, 0);
        }

        private static object Get(object owner, string name) => owner.GetType().GetField(name, Fields)!.GetValue(owner)!;
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Fields)!.SetValue(owner, value);
        private static void SetPointer(object owner, string name, void* value)
        {
            var field = owner.GetType().GetField(name, Fields)!;
            field.SetValue(owner, Pointer.Box(value, field.FieldType));
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Glass?.Dispose();
            if (context != null) context->ClearState();
            if (targetView != null) targetView->Release();
            if (readback != null) readback->Release();
            if (target != null) target->Release();
            if (source != null) source->Release();
            if (context != null) context->Release();
            if (device != null) device->Release();
            if (Duplication != null) { NativeMemory.Free(Duplication->Vtable); NativeMemory.Free(Duplication); }
            if (Output != null) { NativeMemory.Free(Output->Vtable); NativeMemory.Free(Output); }
            if (Window != 0) _ = Win32.DestroyWindow((HWND)Window);
            if (!uiContext.IsNull) ImGui.DestroyContext(uiContext);
            ImGui.SetCurrentContext(previousContext);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393) && (nint)previousDpi != 0)
                _ = Win32.SetThreadDpiAwarenessContext(previousDpi);
        }
    }

    private sealed class ExclusionScene : IDisposable
    {
        internal sealed class WindowState
        {
            internal bool Alive = true, CurrentProcessTopLevel = true, Readable = true, ClaimSucceeds = true;
            internal uint Affinity;
            internal uint? RetainedAffinity;
            internal nint Marker;
            internal int AffinityReads, FailReadAt;
            internal readonly HashSet<uint> RejectedAffinities = [];
        }
        internal readonly Dictionary<nint, WindowState> Windows = new() { [11] = new(), [99] = new() };
        internal readonly List<(nint Window, uint Affinity)> Writes = [];
        internal readonly List<string> Calls = [];
        internal long Clock = 1000;
        internal readonly FrenRiderCaptureExclusion Helper;
        internal ExclusionScene(bool supported = true)
        {
            Helper = new(new FrenRiderCaptureExclusion.Native(supported,
                hwnd => Windows.TryGetValue(hwnd, out var state) && state.Alive,
                hwnd => Windows.TryGetValue(hwnd, out var state) && state.Alive && state.CurrentProcessTopLevel,
                hwnd =>
                {
                    Calls.Add($"read:{hwnd}"); var state = Windows[hwnd]; state.AffinityReads++;
                    return (state.Readable && state.AffinityReads != state.FailReadAt, state.Affinity);
                },
                (hwnd, affinity) =>
                {
                    Calls.Add($"write:{hwnd}:{affinity}"); Writes.Add((hwnd, affinity));
                    var state = Windows[hwnd];
                    if (state.RejectedAffinities.Contains(affinity)) return false;
                    state.Affinity = state.RetainedAffinity ?? affinity; return true;
                },
                hwnd => Windows[hwnd].Marker,
                (hwnd, marker) =>
                {
                    Calls.Add($"claim:{hwnd}"); var state = Windows[hwnd];
                    if (!state.ClaimSucceeds) return false;
                    state.Marker = marker; return true;
                },
                hwnd => { var marker = Windows[hwnd].Marker; Windows[hwnd].Marker = 0; return marker; },
                () => { Calls.Add("time"); return Clock; }));
        }
        public void Dispose() => Helper.Dispose();
    }

    private static RECT Rect(int left, int top, int right, int bottom)
        => new() { left = left, top = top, right = right, bottom = bottom };
    private static void Point(Vector2 expected, Vector2 actual)
    {
        Assert.InRange(MathF.Abs(expected.X - actual.X), 0, .00001f);
        Assert.InRange(MathF.Abs(expected.Y - actual.Y), 0, .00001f);
    }
}
