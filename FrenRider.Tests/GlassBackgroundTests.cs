using System.Numerics;
using System.Reflection;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using FrenRider.Windows;
using Glasswork.Glass;
using Newtonsoft.Json;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using Xunit;

namespace FrenRider.Tests;

[Collection("Activation mode static services")]
public sealed unsafe class GlassBackgroundTests
{
    [Fact]
    public void SavedBackgroundChoicesKeepLegacyWavesAndIndependentPalettes()
    {
        var legacy = JsonConvert.DeserializeObject<Configuration>("{}")!;
        Assert.Equal(FrenRiderGlassBackground.Waves, legacy.UiGlassBackground);
        Assert.Equal(FrenRiderGlassLights.AmbientGlows, legacy.UiGlassLights);
        Assert.Equal("", legacy.UiGlassImagePath);
        legacy.UiGlassBackground = FrenRiderGlassBackground.Image;
        legacy.UiGlassLights = FrenRiderGlassLights.SweepingBeams;
        legacy.UiGlassImagePath = @"C:\fixture\glass.png";
        legacy.UiGlassWave1Rgb = 0x102030;
        legacy.UiGlassLight1Rgb = 0x405060;
        var restored = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(legacy))!;
        Assert.Equal(FrenRiderGlassBackground.Image, restored.UiGlassBackground);
        Assert.Equal(FrenRiderGlassLights.SweepingBeams, restored.UiGlassLights);
        Assert.Equal(legacy.UiGlassImagePath, restored.UiGlassImagePath);
        Assert.Equal(0x102030u, restored.UiGlassWave1Rgb);
        Assert.Equal(0x405060u, restored.UiGlassLight1Rgb);
        restored.UiGlassBackground = (FrenRiderGlassBackground)99;
        restored.UiGlassLights = (FrenRiderGlassLights)(-1);
        Assert.Equal(FrenRiderGlassBackground.Waves, restored.UiGlassBackground);
        Assert.Equal(FrenRiderGlassLights.AmbientGlows, restored.UiGlassLights);
    }

    [Theory]
    [InlineData(FrenRiderGlassLights.AmbientGlows)]
    [InlineData(FrenRiderGlassLights.SweepingBeams)]
    [InlineData(FrenRiderGlassLights.PointerGlow)]
    public void LightsRespectMotionColoursAndKeepForegroundOutsideGlass(FrenRiderGlassLights effect)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            var config = new Configuration { UiStyle = FrenRiderUiStyle.Glassmorphism,
                UiGlassBackground = FrenRiderGlassBackground.Lights, UiGlassLights = effect, UiGlassAnimation = false };
            using var renderer = new FrenRiderGlass((_, _, _, _, _, _, _) => true);
            var io = ImGui.GetIO();
            io.IniFilename = null; io.LogFilename = null; io.DisplaySize = new(1000, 800); io.DeltaTime = .1f;
            io.Fonts.AddFontDefault();
            byte* pixels = null; int width = 0, height = 0;
            io.Fonts.GetTexDataAsRGBA32(0, &pixels, &width, &height);
            io.Fonts.SetTexID(0, new ImTextureID((nint)1));
            (uint[] Background, uint[] Foreground) Frame(Vector2 pointer)
            {
                ImGui.GetIO().MousePos = pointer;
                ImGui.NewFrame();
                using var appearance = FrenRiderPresentation.Push(config, renderer);
                using var theme = MaterialTheme.Push(FrenRiderPresentation.Theme(config), 1, MaterialStyleMode.ColorsOnly);
                ImGui.SetNextWindowPos(new(20)); ImGui.SetNextWindowSize(new(800, 600));
                ImGui.Begin("Light backdrop fixture", ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBackground);
                var list = ImGui.GetWindowDrawList();
                var start = list.VtxBuffer.Size;
                FrenRiderPresentation.DrawBackdrop();
                var background = Enumerable.Range(start, list.VtxBuffer.Size - start).Select(i => list.VtxBuffer[i].Col).ToArray();
                start = list.VtxBuffer.Size;
                ImGui.SetCursorScreenPos(new(80, 100));
                using (FrenRiderPresentation.NativeFieldStyle()) ImGui.Button("Foreground", new(180, 32));
                var foreground = Enumerable.Range(start, list.VtxBuffer.Size - start).Select(i => list.VtxBuffer[i].Col).ToArray();
                ImGui.End(); ImGui.Render();
                return (background, foreground);
            }
            Frame(new(250)); Frame(new(250));
            var still = Frame(new(250));
            Assert.NotEmpty(still.Background);
            Assert.True(still.Background.SequenceEqual(Frame(new(600, 350)).Background));
            Assert.Contains(still.Background, color => color >> 24 > 0);
            Assert.DoesNotContain(still.Background, color => color >> 24 == 255);
            config.UiGlassLight1Rgb = 0xF03060;
            var recolored = Frame(new(250));
            Assert.False(still.Background.SequenceEqual(recolored.Background));
            Assert.Equal(still.Foreground, recolored.Foreground);
            Assert.Equal(Configuration.DefaultGlassWave1Rgb, config.UiGlassWave1Rgb);
            config.UiGlassAnimation = true;
            var moving = Frame(new(200, 200));
            var later = Frame(effect == FrenRiderGlassLights.PointerGlow ? new(600, 400) : new(200, 200));
            Assert.False(moving.Background.SequenceEqual(later.Background));
            Assert.Equal(moving.Foreground, later.Foreground);
        }
        finally { ImGui.DestroyContext(context); ImGui.SetCurrentContext(previous); }
    }

    [Fact]
    public void ImageBackdropIsCroppedAndActuallyBlurredWithoutDesktopCapture()
    {
        ID3D11Device* device = null;
        ID3D11DeviceContext* context = null;
        ID3D11Texture2D* image = null;
        ID3D11Texture2D* target = null;
        ID3D11Texture2D* staging = null;
        ID3D11RenderTargetView* view = null;
        var previous = ImGui.GetCurrentContext();
        var ui = ImGui.CreateContext();
        FrenRiderDetachedGlass? glass = null;
        try
        {
            Assert.True(DirectX.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_WARP,
                default, 0, null, 0, 7, &device, null, &context).SUCCEEDED);
            var imagePixels = new uint[384 * 128];
            for (var y = 0; y < 128; y++)
                for (var x = 0; x < 384; x++)
                    imagePixels[y * 384 + x] = x < 128 ? 0xFFCC1010u : x >= 256 ? 0xFF1010CCu
                        : ((x / 4 + y / 4) & 1) == 0 ? 0xFF104010u : 0xFF10C010u;
            var description = new D3D11_TEXTURE2D_DESC { Width = 384, Height = 128, MipLevels = 1, ArraySize = 1,
                Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, SampleDesc = new() { Count = 1 },
                Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT, BindFlags = 8 };
            fixed (uint* data = imagePixels)
            {
                var initial = new D3D11_SUBRESOURCE_DATA { pSysMem = data, SysMemPitch = 384 * 4 };
                Assert.True(device->CreateTexture2D(&description, &initial, &image).SUCCEEDED);
            }
            description.Width = 128; description.BindFlags = 0x28;
            Assert.True(device->CreateTexture2D(&description, null, &target).SUCCEEDED);
            Assert.True(device->CreateRenderTargetView((ID3D11Resource*)target, null, &view).SUCCEEDED);
            description.Usage = D3D11_USAGE.D3D11_USAGE_STAGING; description.BindFlags = 0; description.CPUAccessFlags = 0x20000;
            Assert.True(device->CreateTexture2D(&description, null, &staging).SUCCEEDED);
            glass = new((nint)device, ActivationModeTests.Proxy<IPluginLog>((_, _) => null));
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(FrenRiderDetachedGlass).GetField("enabled", fields)!.SetValue(glass, true);
            var capture = (FrenRiderDesktopCapture)typeof(FrenRiderDetachedGlass).GetField("capture", fields)!.GetValue(glass)!;
            var exclusion = (FrenRiderCaptureExclusion)typeof(FrenRiderDetachedGlass).GetField("exclusion", fields)!.GetValue(glass)!;
            Assert.False(exclusion.Ready);
            var viewport = ImGui.GetMainViewport();
            viewport.PlatformHandleRaw = (void*)11;
            var drawContext = context; var drawView = view; var readback = staging; var drawTarget = target; var drawImage = image;
            uint[] Sample(float frost)
            {
                float* clear = stackalloc float[4] { 0, 0, 0, 0 };
                drawContext->ClearRenderTargetView(drawView, clear);
                var renderView = drawView;
                drawContext->OMSetRenderTargets(1, &renderView, null);
                var request = new FrenRiderDetachedGlass.DrawRequest { Hwnd = 11,
                    ViewportId = ImGui.GetMainViewport().ID, Frame = 0,
                    Shape = GlassShape.FromRect(new Vector2(8), new Vector2(112), 8), Clip = new(0, 0, 128, 128),
                    Material = FrenRiderGlass.CreateMaterial(new(.2f, .3f, .4f, .42f), frost, 1, 1),
                    ImageSource = (nint)drawImage, ImageRootBounds = new(0, 0, 128, 128), WindowBackground = true, Composite = true };
                glass.Render(&request);
                drawContext->CopyResource((ID3D11Resource*)readback, (ID3D11Resource*)drawTarget);
                D3D11_MAPPED_SUBRESOURCE mapped;
                Assert.True(drawContext->Map((ID3D11Resource*)readback, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped).SUCCEEDED);
                try
                {
                    var result = new uint[128 * 128];
                    for (var y = 0; y < 128; y++)
                        new ReadOnlySpan<uint>((byte*)mapped.pData + y * mapped.RowPitch, 128).CopyTo(result.AsSpan(y * 128));
                    return result;
                }
                finally { drawContext->Unmap((ID3D11Resource*)readback, 0); }
            }
            var sharp = Sample(0);
            var blurred = Sample(1);
            foreach (var x in new[] { 20, 64, 108 })
            {
                var pixel = sharp[64 * 128 + x];
                Assert.True(((pixel >> 8) & 255) > ((pixel >> 16) & 255), "The cover crop included the red side band.");
                Assert.True(((pixel >> 8) & 255) > (pixel & 255), "The cover crop included the blue side band.");
                Assert.Equal(255u, pixel >> 24);
            }
            static double Detail(uint[] pixels) => Enumerable.Range(32, 64)
                .Average(x => Math.Abs((int)((pixels[64 * 128 + x] >> 8) & 255) - (int)((pixels[64 * 128 + x + 1] >> 8) & 255)));
            Assert.True(Detail(sharp) > 5, "The sharp image lost its patterned detail.");
            Assert.True(Detail(blurred) < Detail(sharp) * .5, "Frost did not blur the supplied image.");
            Assert.Empty((System.Collections.IList)typeof(FrenRiderDesktopCapture).GetField("outputs", fields)!.GetValue(capture)!);
            Assert.False(exclusion.Ready);
        }
        finally
        {
            glass?.Dispose();
            if (context != null) context->ClearState();
            if (view != null) view->Release();
            if (staging != null) staging->Release();
            if (target != null) target->Release();
            if (image != null) image->Release();
            if (context != null) context->Release();
            if (device != null) device->Release();
            ImGui.DestroyContext(ui); ImGui.SetCurrentContext(previous);
        }
    }
}
