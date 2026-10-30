using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Glasswork.Glass;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace FrenRider.Windows;

/// <summary>Composites Background glass from an excluded desktop capture into owned platform viewports.</summary>
internal sealed unsafe class FrenRiderDetachedGlass : IDisposable
{
    internal struct CallbackState
    {
        internal nint Owner;
        internal DrawRequest* Requests;
        internal int Pending;
        internal int Disposed;
    }

    internal struct DrawRequest
    {
        internal CallbackState* State;
        internal DrawRequest* Next;
        internal GlassShape Shape;
        internal GlassMaterial Material;
        internal Vector2 ViewportOrigin;
        internal Vector4 Clip;
        internal nint Hwnd;
        internal long CaptureBoundary;
        internal uint ViewportId;
        internal int Frame;
        internal int Ran;
        internal bool Composite;
        internal bool WindowBackground;
        internal nint ImageSource;
        internal Vector4 ImageRootBounds;
    }

    // These are the pipeline slots changed by desktop rotation normalization and GlassRenderer.
    // The plugin targets x64, so native interface pointers occupy the fixed 64-bit slots.
    private struct PipelineState
    {
        internal ID3D11InputLayout* InputLayout;
        internal D3D_PRIMITIVE_TOPOLOGY Topology;
        internal ID3D11VertexShader* VertexShader;
        internal ID3D11GeometryShader* GeometryShader;
        internal ID3D11HullShader* HullShader;
        internal ID3D11DomainShader* DomainShader;
        internal ID3D11PixelShader* PixelShader;
        internal uint VertexClassCount, GeometryClassCount, HullClassCount, DomainClassCount, PixelClassCount;
        internal fixed long VertexClasses[256];
        internal fixed long GeometryClasses[256];
        internal fixed long HullClasses[256];
        internal fixed long DomainClasses[256];
        internal fixed long PixelClasses[256];
        internal fixed long PixelBuffers[2];
        internal fixed long PixelResources[3];
        internal ID3D11SamplerState* PixelSampler;
        internal ID3D11RasterizerState* Rasterizer;
        internal uint ViewportCount, ScissorCount;
        internal fixed float Viewports[16 * 6];
        internal fixed int Scissors[16 * 4];
        internal fixed long Targets[8];
        internal ID3D11DepthStencilView* DepthView;
        internal ID3D11DepthStencilState* DepthState;
        internal uint StencilReference;
        internal ID3D11BlendState* BlendState;
        internal fixed float BlendFactor[4];
        internal uint SampleMask;
    }

    private readonly IPluginLog log;
    private readonly HashSet<nint> usedWindows = new();
    private readonly Dictionary<nint, long> readyWindows = new();
    private FrenRiderDesktopCapture? capture;
    private FrenRiderCaptureExclusion? exclusion;
    private GlassRenderer? renderer;
    private ID3D11Device* device;
    private ID3D11DeviceContext* context;
    private CallbackState* callbacks;
    private GCHandle ownerHandle;
    private Vector2 pointerPosition;
    private float pointerPresence;
    private int frame;
    private string? lastUnavailableReason;
    private bool enabled;
    private bool faulted;
    private bool disposed;
    private bool? pendingScreenshotsPaused;
    private bool screenshotPauseRequested;
    private bool imageFrame;
    private bool imageModeActive;
    private readonly HashSet<nint> imageWindows = new();
    internal bool ScreenshotsPaused => capture?.IsPaused == true;
    internal bool CanPauseForScreenshots
    {
        get
        {
            if (disposed || faulted || !enabled || exclusion?.Ready != true ||
                capture?.HasRetainedImages != true || usedWindows.Count == 0) return false;
            foreach (var window in usedWindows)
                if (!readyWindows.TryGetValue(window, out var boundary) ||
                    boundary != exclusion.MinimumFrameTimestamp) return false;
            return true;
        }
    }
#if DEBUG
    private uint tracedStages;
#endif

    internal FrenRiderDetachedGlass(nint gameDevice, IPluginLog log)
    {
        this.log = log;
        try
        {
            if (gameDevice == 0) throw new ArgumentException("A Direct3D device is required.", nameof(gameDevice));
            var deviceId = IID.IID_ID3D11Device;
            void* queriedDevice = null;
            var result = ((IUnknown*)gameDevice)->QueryInterface(&deviceId, &queriedDevice);
            if (result.FAILED || queriedDevice == null)
                throw new InvalidOperationException("The host device does not expose Direct3D 11.");
            device = (ID3D11Device*)queriedDevice;
            ID3D11DeviceContext* immediate = null;
            device->GetImmediateContext(&immediate);
            context = immediate;
            if (context == null) throw new InvalidOperationException("The Direct3D device has no immediate context.");

            renderer = new GlassRenderer(device);
            capture = new FrenRiderDesktopCapture((nint)device, log);
            exclusion = new FrenRiderCaptureExclusion();
            ownerHandle = GCHandle.Alloc(this);
            callbacks = (CallbackState*)NativeMemory.AllocZeroed((nuint)sizeof(CallbackState));
            callbacks->Owner = GCHandle.ToIntPtr(ownerHandle);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal void BeginFrame()
    {
        if (disposed) return;
        // Commit UI requests before recording this frame, after all previous native callbacks finished.
        if (pendingScreenshotsPaused is { } paused)
        {
            pendingScreenshotsPaused = null;
            _ = SetScreenshotsPaused(paused);
        }
        else if (screenshotPauseRequested && !ScreenshotsPaused && CanPauseForScreenshots)
            _ = SetScreenshotsPaused(true);
        // The host finishes every platform render before opening its next draw phase.
        ClearRequests(callbacks);
        usedWindows.Clear();
        imageWindows.Clear();
        imageFrame = false;
        frame++;
        capture?.BeginFrame();
        AdvancePointer();
    }

    internal bool TryDraw(ImDrawListPtr drawList, Vector2 min, Vector2 max, float rounding,
        in GlassMaterial material, bool windowBackground, nint imageSource = 0, Vector4 imageRootBounds = default)
    {
        if (disposed || faulted || callbacks == null || capture == null || exclusion == null || renderer == null ||
            drawList.IsNull || !Finite(min) || !Finite(max) || !float.IsFinite(rounding) ||
            max.X <= min.X || max.Y <= min.Y) return false;

        var viewport = ImGui.GetWindowViewport();
        if (viewport.IsNull || imageSource == 0 && viewport.ID == ImGui.GetMainViewport().ID) return false;
        var hwnd = (nint)(viewport.PlatformHandleRaw != null ? viewport.PlatformHandleRaw : viewport.PlatformHandle);
        if (imageSource == 0)
        {
            if (hwnd == 0) { Trace(1, "detached viewport has no native window yet"); return false; }
            usedWindows.Add(hwnd);
        }
        else MarkImageWindow();

        if (windowBackground && (drawList != ImGui.GetWindowDrawList() || min != ImGui.GetWindowPos() ||
            max != min + ImGui.GetWindowSize() || !CanPrepend(drawList)))
        {
            Trace(2, $"window background rejected; splitter={drawList.Handle->Splitter.Count}");
            return false;
        }

        var origin = viewport.Pos;
        var clipMin = windowBackground ? origin : drawList.GetClipRectMin();
        var clipMax = windowBackground ? origin + viewport.Size : drawList.GetClipRectMax();
        if (!Finite(origin) || !Finite(clipMin) || !Finite(clipMax) ||
            clipMax.X <= clipMin.X || clipMax.Y <= clipMin.Y) return false;

        var shape = GlassShape.FromRect(min, max - min, rounding);
        var drawMaterial = material;
        ApplyPointerLight(in shape, ref drawMaterial);
        var boundary = exclusion.MinimumFrameTimestamp;
        var composite = imageSource != 0
            ? readyWindows.TryGetValue(hwnd, out var imageReady) && imageReady == (long)imageSource
            : enabled && (exclusion.Ready || ScreenshotsPaused) &&
                readyWindows.TryGetValue(hwnd, out var readyBoundary) && readyBoundary == boundary;
        var request = (DrawRequest*)NativeMemory.AllocZeroed((nuint)sizeof(DrawRequest));
        request->State = callbacks;
        request->Next = callbacks->Requests;
        request->Shape = shape;
        request->Material = drawMaterial;
        request->ViewportOrigin = origin;
        request->Clip = new Vector4(clipMin.X, clipMin.Y, clipMax.X, clipMax.Y);
        request->Hwnd = hwnd;
        request->ViewportId = viewport.ID;
        request->CaptureBoundary = boundary;
        request->Composite = composite;
        request->WindowBackground = windowBackground;
        request->Frame = frame;
        request->ImageSource = imageSource;
        request->ImageRootBounds = imageRootBounds;
        if (imageSource != 0) ((IUnknown*)imageSource)->AddRef();
        callbacks->Requests = request;
        callbacks->Pending++;

        var first = OpenCallbackBlock(drawList);
        ImGui.AddCallback(drawList,
            (delegate*<ImDrawListPtr, ImDrawCmdPtr, void>)(delegate* unmanaged<ImDrawListPtr, ImDrawCmdPtr, void>)&RenderCallback,
            request);
        ImGui.AddCallback(drawList, ImDrawCallbackEnum.ResetRenderState);
        if (windowBackground) PrependCallbackBlock(drawList, first);
        Trace(windowBackground ? 4u : 8u, windowBackground ? "window callback queued" : "panel callback queued");
        // A probe runs behind the consumer's ordinary paint until a fresh excluded capture has succeeded.
        return composite;
    }

    internal void EndFrame(nint gameWindow, bool enabled)
    {
        if (disposed) return;
        if (enabled && imageFrame)
        {
            if (!imageModeActive)
            {
                capture?.ReleaseResources();
                readyWindows.Clear();
                imageModeActive = true;
            }
            this.enabled = true;
            if (imageWindows.Count == 0)
            {
                if (exclusion?.RestoreAll() == false)
                    ReportUnavailable(exclusion.UnavailableReason ?? "Previous capture affinity could not be restored.");
                return;
            }
            if (imageWindows.Count > 0 && exclusion?.AllowCapture(imageWindows) != true)
                ReportUnavailable(exclusion?.UnavailableReason ?? "Image screenshot visibility could not be verified.");
            return;
        }
        imageModeActive = false;
        if (!enabled || usedWindows.Count == 0)
        {
            Suspend();
            return;
        }
        this.enabled = true;
        if (ScreenshotsPaused)
        {
            if (exclusion?.AllowCapture(usedWindows) == true) return;
            ReportUnavailable(exclusion?.UnavailableReason ?? "Screenshot visibility could not be verified.");
            _ = capture?.TrySetPaused(false);
            readyWindows.Clear();
        }
        if (exclusion == null || !exclusion.Synchronize(usedWindows, gameWindow, true))
        {
            readyWindows.Clear();
            ReportUnavailable(exclusion?.UnavailableReason ?? "Capture exclusion is unavailable.");
            return;
        }
        lastUnavailableReason = null;
        Trace(16, "capture exclusion ready");
        capture?.UpdateExclusionBoundary(usedWindows, exclusion.BoundaryChangedWindows,
            exclusion.MinimumFrameTimestamp);
    }

    internal bool TrySetScreenshotsPaused(bool paused)
    {
        if (disposed || capture == null || exclusion == null || (paused && !ScreenshotsPaused && !CanPauseForScreenshots))
            return false;
        pendingScreenshotsPaused = paused;
        screenshotPauseRequested = paused;
        log.Debug("[FrenRider] Screenshot pause requested: {Paused}", paused);
        return true;
    }

    private bool SetScreenshotsPaused(bool paused)
    {
        if (disposed || capture == null || exclusion == null) return false;
        if (paused == ScreenshotsPaused) return true;
        if (!paused)
        {
            readyWindows.Clear();
            var resumed = capture.TrySetPaused(false);
            if (resumed) log.Information("[FrenRider] Screenshot pause ended; live blur is waiting for a fresh excluded backdrop.");
            return resumed;
        }
        if (!CanPauseForScreenshots || !capture.TrySetPaused(true)) return false;
        // Stop acquisition before making the HWNDs visible to screenshot tools. The owned monitor images survive.
        if (exclusion.AllowCapture(usedWindows))
        {
            log.Information("[FrenRider] Screenshot pause active: WDA_NONE verified for {Count} detached windows; desktop acquisition stopped.", usedWindows.Count);
            return true;
        }
        ReportUnavailable(exclusion.UnavailableReason ?? "Screenshot visibility could not be restored.");
        readyWindows.Clear();
        _ = capture.TrySetPaused(false);
        return false;
    }

    internal void ReleaseResources()
    {
        if (disposed) return;
        Suspend();
        renderer?.ReleasePool();
    }

    private void Suspend()
    {
        pendingScreenshotsPaused = null;
        imageModeActive = false;
        enabled = false;
        readyWindows.Clear();
        usedWindows.Clear();
        // Stop acquisition before putting any owned window back into the desktop capture.
        capture?.ReleaseResources();
        if (exclusion != null && !exclusion.RestoreAll())
            ReportUnavailable(exclusion.UnavailableReason ?? "A detached window's original capture affinity could not be restored.");
    }

    private void ReportUnavailable(string reason)
    {
        if (lastUnavailableReason == reason) return;
        lastUnavailableReason = reason;
        log.Warning("[FrenRider] Detached glass unavailable: {Reason}", reason);
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

    internal void Render(DrawRequest* request)
    {
        Trace(32, "native callback entered");
        if (disposed || faulted || !enabled || request->Frame != frame || capture == null || exclusion == null ||
            renderer == null || context == null)
        {
            Trace(64, $"callback rejected; disposed={disposed}, faulted={faulted}, enabled={enabled}, requestFrame={request->Frame}, frame={frame}");
            return;
        }
        var viewport = ImGui.FindViewportByID(request->ViewportId);
        if (viewport.IsNull || (nint)(viewport.PlatformHandleRaw != null ?
                viewport.PlatformHandleRaw : viewport.PlatformHandle) != request->Hwnd)
        {
            readyWindows.Remove(request->Hwnd);
            Trace(128, "callback viewport/native window mismatch");
            return;
        }
        if (request->ImageSource == 0 && !exclusion.Ready && !ScreenshotsPaused)
        {
            readyWindows.Remove(request->Hwnd);
            Trace(256, "callback capture exclusion is not ready");
            return;
        }
        PipelineState pipeline = default;
        var savedPipeline = &pipeline;
        SavePipeline(context, savedPipeline);
        var targetView = (ID3D11RenderTargetView*)savedPipeline->Targets[0];
        ID3D11Resource* targetResource = null;
        ID3D11Texture2D* targetTexture = null;
        try
        {
            if (targetView == null) { Trace(512, "callback has no render target"); return; }
            targetView->GetResource(&targetResource);
            if (targetResource == null) return;
            var textureId = IID.IID_ID3D11Texture2D;
            void* queriedTexture = null;
            var result = targetResource->QueryInterface(&textureId, &queriedTexture);
            if (result.FAILED || queriedTexture == null) return;
            targetTexture = (ID3D11Texture2D*)queriedTexture;
            D3D11_TEXTURE2D_DESC description;
            targetTexture->GetDesc(&description);
            Trace(1024, $"callback target {description.Width}x{description.Height}; samples={description.SampleDesc.Count}");
            // A clipped panel is a no-op; it must not erase a successful full-window probe.
            if (description.SampleDesc.Count != 1 ||
                !TryTranslateToTarget(in request->Shape, request->ViewportOrigin, request->Clip,
                    description.Width, description.Height, out var shape, out var clip,
                    GlassRenderer.SamplingPad(in request->Material)))
            {
                Trace(2048, "callback shape is clipped or target is multisampled");
                return;
            }

            nint source;
            if (request->ImageSource != 0)
            {
                if (!capture.TryGetImageBackdrop(request->ImageSource, description.Width, description.Height,
                        request->ImageRootBounds, out source)) return;
            }
            else
            {
                var minimumTimestamp = exclusion.MinimumFrameTimestamp;
                if (!capture.TryGetViewportTexture(request->Hwnd, request->ViewportOrigin, description.Width,
                        description.Height, exclusion.Ready, minimumTimestamp, out source) || source == 0)
                {
                    readyWindows.Remove(request->Hwnd);
                    Trace(4096, "callback has no fresh desktop backdrop");
                    return;
                }
                readyWindows[request->Hwnd] = minimumTimestamp;
                Trace(8192, "callback desktop backdrop ready");
                if (!request->Composite || request->CaptureBoundary != minimumTimestamp)
                {
                    Trace(16384, "callback completed initial capture probe");
                    return;
                }
            }
            if (!request->WindowBackground &&
                !capture.TryComposeWindowBackdrop(source, (nint)targetTexture, out source))
            {
                readyWindows.Remove(request->Hwnd);
                return;
            }

            ReadOnlySpan<GlassShape> shapes = new(&shape, 1);
            renderer.Draw(context, (ID3D11Texture2D*)source, targetView, shapes, in request->Material, clip);
            if (request->ImageSource != 0) readyWindows[request->Hwnd] = (long)request->ImageSource;
            Trace(32768, "Glasswork draw completed");
        }
        catch (Exception exception)
        {
            faulted = true;
            readyWindows.Clear();
            log.Warning(exception, "[FrenRider] Detached glass stopped; ordinary Background paint will resume.");
        }
        finally
        {
            if (targetTexture != null) targetTexture->Release();
            if (targetResource != null) targetResource->Release();
            RestorePipeline(context, savedPipeline);
        }
    }

    private static void SavePipeline(ID3D11DeviceContext* context, PipelineState* state)
    {
        context->IAGetInputLayout(&state->InputLayout);
        context->IAGetPrimitiveTopology(&state->Topology);
        state->VertexClassCount = state->GeometryClassCount = state->HullClassCount =
            state->DomainClassCount = state->PixelClassCount = 256;
        context->VSGetShader(&state->VertexShader, (ID3D11ClassInstance**)state->VertexClasses, &state->VertexClassCount);
        context->GSGetShader(&state->GeometryShader, (ID3D11ClassInstance**)state->GeometryClasses, &state->GeometryClassCount);
        context->HSGetShader(&state->HullShader, (ID3D11ClassInstance**)state->HullClasses, &state->HullClassCount);
        context->DSGetShader(&state->DomainShader, (ID3D11ClassInstance**)state->DomainClasses, &state->DomainClassCount);
        context->PSGetShader(&state->PixelShader, (ID3D11ClassInstance**)state->PixelClasses, &state->PixelClassCount);
        context->PSGetConstantBuffers(0, 2, (ID3D11Buffer**)state->PixelBuffers);
        context->PSGetShaderResources(0, 3, (ID3D11ShaderResourceView**)state->PixelResources);
        context->PSGetSamplers(0, 1, &state->PixelSampler);
        context->RSGetState(&state->Rasterizer);
        state->ViewportCount = state->ScissorCount = 16;
        context->RSGetViewports(&state->ViewportCount, (D3D11_VIEWPORT*)state->Viewports);
        context->RSGetScissorRects(&state->ScissorCount, (RECT*)state->Scissors);
        context->OMGetRenderTargets(8, (ID3D11RenderTargetView**)state->Targets, &state->DepthView);
        context->OMGetDepthStencilState(&state->DepthState, &state->StencilReference);
        context->OMGetBlendState(&state->BlendState, state->BlendFactor, &state->SampleMask);
    }

    private static void RestorePipeline(ID3D11DeviceContext* context, PipelineState* state)
    {
        context->OMSetRenderTargets(0, null, null);
        context->OMSetRenderTargets(8, (ID3D11RenderTargetView**)state->Targets, state->DepthView);
        context->OMSetDepthStencilState(state->DepthState, state->StencilReference);
        context->OMSetBlendState(state->BlendState, state->BlendFactor, state->SampleMask);
        context->IASetInputLayout(state->InputLayout);
        context->IASetPrimitiveTopology(state->Topology);
        context->VSSetShader(state->VertexShader, (ID3D11ClassInstance**)state->VertexClasses, state->VertexClassCount);
        context->GSSetShader(state->GeometryShader, (ID3D11ClassInstance**)state->GeometryClasses, state->GeometryClassCount);
        context->HSSetShader(state->HullShader, (ID3D11ClassInstance**)state->HullClasses, state->HullClassCount);
        context->DSSetShader(state->DomainShader, (ID3D11ClassInstance**)state->DomainClasses, state->DomainClassCount);
        context->PSSetShader(state->PixelShader, (ID3D11ClassInstance**)state->PixelClasses, state->PixelClassCount);
        context->PSSetConstantBuffers(0, 2, (ID3D11Buffer**)state->PixelBuffers);
        context->PSSetShaderResources(0, 3, (ID3D11ShaderResourceView**)state->PixelResources);
        context->PSSetSamplers(0, 1, &state->PixelSampler);
        context->RSSetState(state->Rasterizer);
        context->RSSetViewports(state->ViewportCount, (D3D11_VIEWPORT*)state->Viewports);
        context->RSSetScissorRects(state->ScissorCount, (RECT*)state->Scissors);

        if (state->InputLayout != null) state->InputLayout->Release();
        if (state->VertexShader != null) state->VertexShader->Release();
        if (state->GeometryShader != null) state->GeometryShader->Release();
        if (state->HullShader != null) state->HullShader->Release();
        if (state->DomainShader != null) state->DomainShader->Release();
        if (state->PixelShader != null) state->PixelShader->Release();
        if (state->PixelSampler != null) state->PixelSampler->Release();
        if (state->Rasterizer != null) state->Rasterizer->Release();
        if (state->DepthView != null) state->DepthView->Release();
        if (state->DepthState != null) state->DepthState->Release();
        if (state->BlendState != null) state->BlendState->Release();
        ReleaseReferences((void**)state->VertexClasses, state->VertexClassCount);
        ReleaseReferences((void**)state->GeometryClasses, state->GeometryClassCount);
        ReleaseReferences((void**)state->HullClasses, state->HullClassCount);
        ReleaseReferences((void**)state->DomainClasses, state->DomainClassCount);
        ReleaseReferences((void**)state->PixelClasses, state->PixelClassCount);
        ReleaseReferences((void**)state->PixelBuffers, 2);
        ReleaseReferences((void**)state->PixelResources, 3);
        ReleaseReferences((void**)state->Targets, 8);
    }

    private static void ReleaseReferences(void** references, uint count)
    {
        for (uint i = 0; i < count; i++)
            if (references[i] != null) ((IUnknown*)references[i])->Release();
    }

    internal static bool TryTranslateToTarget(in GlassShape screenShape, Vector2 viewportOrigin,
        Vector4 screenClip, uint targetWidth, uint targetHeight, out GlassShape targetShape,
        out RECT targetClip, float samplingReach = 0)
    {
        targetShape = default;
        targetClip = default;
        if (targetWidth == 0 || targetHeight == 0 || targetWidth > int.MaxValue || targetHeight > int.MaxValue ||
            !Finite(viewportOrigin) || !Finite(screenShape.Center) || !Finite(screenShape.HalfSize) ||
            screenShape.HalfSize.X <= 0 || screenShape.HalfSize.Y <= 0 ||
            !float.IsFinite(screenShape.Radius) || !float.IsFinite(screenShape.Thickness) ||
            !float.IsFinite(screenClip.X) || !float.IsFinite(screenClip.Y) ||
            !float.IsFinite(screenClip.Z) || !float.IsFinite(screenClip.W) ||
            screenClip.Z <= screenClip.X || screenClip.W <= screenClip.Y ||
            !float.IsFinite(samplingReach) || samplingReach < 0) return false;

        var shape = screenShape.Offset(-viewportOrigin);
        if (!Finite(shape.Center) || !Finite(shape.Min) || !Finite(shape.Max)) return false;
        var left = (int)Math.Floor(Math.Clamp((double)screenClip.X - viewportOrigin.X, 0, targetWidth));
        var top = (int)Math.Floor(Math.Clamp((double)screenClip.Y - viewportOrigin.Y, 0, targetHeight));
        var right = (int)Math.Ceiling(Math.Clamp((double)screenClip.Z - viewportOrigin.X, 0, targetWidth));
        var bottom = (int)Math.Ceiling(Math.Clamp((double)screenClip.W - viewportOrigin.Y, 0, targetHeight));
        if (right <= left || bottom <= top || (double)shape.Max.X + samplingReach <= left ||
            (double)shape.Max.Y + samplingReach <= top || (double)shape.Min.X - samplingReach >= right ||
            (double)shape.Min.Y - samplingReach >= bottom) return false;
        targetShape = shape;
        targetClip = new RECT(left, top, right, bottom);
        return true;
    }

    [UnmanagedCallersOnly]
    private static void RenderCallback(ImDrawListPtr drawList, ImDrawCmdPtr command)
    {
        var request = (DrawRequest*)command.UserCallbackData;
        if (request == null || request->Ran != 0 || request->State == null) return;
        var state = request->State;
        request->Ran = 1;
        try
        {
            if (state->Disposed == 0 && state->Owner != 0 &&
                GCHandle.FromIntPtr(state->Owner).Target is FrenRiderDetachedGlass owner)
                owner.Render(request);
        }
        catch (Exception)
        {
            // An exception must never cross the host's unmanaged render callback.
        }
        finally
        {
            state->Pending--;
            if (state->Disposed != 0 && state->Pending == 0) FreeState(state);
        }
    }

    private static int OpenCallbackBlock(ImDrawListPtr drawList)
    {
        if (drawList.CmdBuffer.Size == 0 || drawList.CmdBuffer[drawList.CmdBuffer.Size - 1].ElemCount != 0)
            drawList.AddDrawCmd();
        return drawList.CmdBuffer.Size - 1;
    }

    private static bool CanPrepend(ImDrawListPtr drawList)
    {
        var list = drawList.Handle;
        if (list->Splitter.Count > 1) return false;
        for (var i = 0; i + 1 < list->CmdBuffer.Size; i++)
            if (list->CmdBuffer.Data[i].IdxOffset + list->CmdBuffer.Data[i].ElemCount !=
                list->CmdBuffer.Data[i + 1].IdxOffset) return false;
        return true;
    }

    private static void PrependCallbackBlock(ImDrawListPtr drawList, int first)
    {
        var commands = drawList.Handle->CmdBuffer;
        var count = commands.Size - 1;
        if (first == 0 || first >= count) return;
        var offset = commands.Data[0].IdxOffset;
        // The inserted block consists only of zero-element callbacks; existing index order stays unchanged.
        var span = new Span<ImDrawCmd>(commands.Data, count);
        span[..first].Reverse();
        span[first..].Reverse();
        span.Reverse();
        for (var i = 0; i < count; i++)
        {
            commands.Data[i].IdxOffset = offset;
            offset += commands.Data[i].ElemCount;
        }
    }

    private void AdvancePointer()
    {
        var io = ImGui.GetIO();
        var delta = Math.Clamp(io.DeltaTime, 0, .1f);
        var fraction = 1 - MathF.Exp(-MathF.Max(delta, 0) / .12f);
        var valid = ImGui.IsMousePosValid();
        if (valid)
        {
            var target = io.MousePos;
            pointerPosition = pointerPresence <= .001f ? target : pointerPosition + (target - pointerPosition) * fraction;
            if (Vector2.DistanceSquared(pointerPosition, target) < .0001f) pointerPosition = target;
        }
        var presence = valid ? 1f : 0;
        pointerPresence += (presence - pointerPresence) * fraction;
        if (MathF.Abs(presence - pointerPresence) < .001f) pointerPresence = presence;
    }

    private void ApplyPointerLight(in GlassShape shape, ref GlassMaterial material)
    {
        // Match Glasswork.Backend's MIT pointer-light tuning; see Notices/Glasswork.MIT.txt.
        if (pointerPresence <= .001f) return;
        var radius = MathF.Max(0, MathF.Min(shape.Radius, MathF.Min(shape.HalfSize.X, shape.HalfSize.Y)));
        var distance = MathF.Max(Vector2.Max(Vector2.Abs(pointerPosition - shape.Center) -
            (shape.HalfSize - new Vector2(radius)), Vector2.Zero).Length() - radius, 0);
        var weight = 1 - Math.Clamp(distance / 180f, 0, 1);
        weight = weight * weight * (3 - 2 * weight) * pointerPresence;
        var direction = pointerPosition - shape.Center;
        if (weight <= 0 || direction.LengthSquared() < 1e-6f) return;
        direction = Vector2.Lerp(material.LightDirection, Vector2.Normalize(direction), weight * .6f);
        if (direction.LengthSquared() >= 1e-6f) material.LightDirection = Vector2.Normalize(direction);
    }

    private static bool Finite(Vector2 point) => float.IsFinite(point.X) && float.IsFinite(point.Y);

    internal void MarkImageWindow()
    {
        imageFrame = true;
        var viewport = ImGui.GetWindowViewport();
        if (viewport.IsNull || viewport.ID == ImGui.GetMainViewport().ID) return;
        var hwnd = (nint)(viewport.PlatformHandleRaw != null ? viewport.PlatformHandleRaw : viewport.PlatformHandle);
        if (hwnd != 0) imageWindows.Add(hwnd);
    }

    internal void ReleaseImageSource()
    {
        capture?.ReleaseImageSource();
        readyWindows.Clear();
    }

    private static void ClearRequests(CallbackState* state)
    {
        if (state == null) return;
        var request = state->Requests;
        while (request != null)
        {
            var next = request->Next;
            if (request->ImageSource != 0) ((IUnknown*)request->ImageSource)->Release();
            NativeMemory.Free(request);
            request = next;
        }
        state->Requests = null;
        state->Pending = 0;
    }

    private static void FreeState(CallbackState* state)
    {
        ClearRequests(state);
        if (state->Owner != 0)
        {
            GCHandle.FromIntPtr(state->Owner).Free();
            state->Owner = 0;
        }
        NativeMemory.Free(state);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        enabled = false;
        if (callbacks != null) callbacks->Disposed = 1;
        // Pending callbacks retain both their records and this assembly's managed owner until their final return.
        if (callbacks == null && ownerHandle.IsAllocated) ownerHandle.Free();
        capture?.Dispose();
        capture = null;
        exclusion?.Dispose();
        exclusion = null;
        renderer?.Dispose();
        renderer = null;
        if (context != null) context->Release();
        context = null;
        if (device != null) device->Release();
        device = null;
        if (callbacks != null && callbacks->Pending == 0) FreeState(callbacks);
        callbacks = null;
        ownerHandle = default;
        usedWindows.Clear();
        readyWindows.Clear();
    }
}
