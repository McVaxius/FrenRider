using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using FrenRider.Windows;
using Newtonsoft.Json;
using Xunit;

namespace FrenRider.Tests;

[Collection("Activation mode static services")]
public sealed unsafe class ColorAppearanceTests
{
    [Fact]
    public void BackgroundGlassWindowScopePreservesDetachedPlacementAndRestoresOriginalFlagsAfterDrawFailure()
    {
        var windows = new WindowSystem("Glasswork viewport scope fixture");
        var ordinary = new BackgroundProbeWindow("Ordinary###glasswork-scope-ordinary")
        {
            Flags = ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse,
            ForceMainWindow = false,
        };
        var alreadyForced = new BackgroundProbeWindow("Forced###glasswork-scope-forced")
        {
            Flags = ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar,
            ForceMainWindow = true,
        };
        windows.AddWindow(ordinary);
        windows.AddWindow(alreadyForced);
        var ordinaryFlags = ordinary.Flags;
        var forcedFlags = alreadyForced.Flags;
        var failure = new InvalidOperationException("Synthetic window draw failure.");
        var draws = 0;

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            Plugin.DrawAppearanceWindows(windows, true, () =>
            {
                draws++;
                Assert.False(ordinary.ForceMainWindow);
                Assert.True(alreadyForced.ForceMainWindow);
                Assert.Equal(ordinaryFlags | ImGuiWindowFlags.NoBackground, ordinary.Flags);
                Assert.Equal(forcedFlags, alreadyForced.Flags);
                throw failure;
            })));
        Assert.Equal(1, draws);
        Assert.False(ordinary.ForceMainWindow);
        Assert.True(alreadyForced.ForceMainWindow);
        Assert.Equal(ordinaryFlags, ordinary.Flags);
        Assert.Equal(forcedFlags, alreadyForced.Flags);

        Plugin.DrawAppearanceWindows(windows, false, () =>
        {
            draws++;
            Assert.False(ordinary.ForceMainWindow);
            Assert.True(alreadyForced.ForceMainWindow);
            Assert.Equal(ordinaryFlags, ordinary.Flags);
            Assert.Equal(forcedFlags, alreadyForced.Flags);
        });
        Assert.Equal(2, draws);
        Assert.False(ordinary.ForceMainWindow);
        Assert.True(alreadyForced.ForceMainWindow);
        Assert.Equal(ordinaryFlags, ordinary.Flags);
        Assert.Equal(forcedFlags, alreadyForced.Flags);
    }

    [Fact]
    public void WindowCommandsChangeOnlyTheirIndependentGlobalPreferenceAndKeepStoredStyle()
    {
        var config = new Configuration { UiStyle = FrenRiderUiStyle.Glassmorphism, UiAccentRgb = 0x123456 };
        Assert.True(Plugin.TryApplyWindowCommand(config, " lock "));
        Assert.True(config.UiWindowsLocked);
        Assert.True(Plugin.TryApplyWindowCommand(config, "LOCK"));
        Assert.True(config.UiWindowsLocked);
        Assert.True(Plugin.TryApplyWindowCommand(config, "ui off"));
        Assert.False(config.UiStylingEnabled);
        Assert.Equal(FrenRiderUiStyle.Glassmorphism, config.UiStyle);
        Assert.True(config.UiWindowsLocked);
        Assert.False(Plugin.TryApplyWindowCommand(config, "off"));
        Assert.True(Plugin.TryApplyWindowCommand(config, "unlock"));
        Assert.False(config.UiWindowsLocked);
        Assert.False(config.UiStylingEnabled);
        Assert.True(Plugin.TryApplyWindowCommand(config, "ui on"));
        Assert.True(config.UiStylingEnabled);
        Assert.Equal(0x123456u, config.UiAccentRgb);
    }

    [Fact]
    public void GlassworkFrostRetainsPerStyleChoicesAndUsesTheExistingGlobalConfiguration()
    {
        var config = JsonConvert.DeserializeObject<Configuration>("""{"UiStyle":1}""")!;
        Assert.True(config.UiStylingEnabled);
        Assert.False(config.UiWindowsLocked);
        Assert.Equal(75, config.UiColors.BackgroundBlurPercent);
        Assert.True(FrenRiderPresentation.BackgroundBlurEnabled(config));
        Assert.Equal(.75f, FrenRiderPresentation.BackgroundFrost(config));
        config.UiStyle = FrenRiderUiStyle.Spatial;
        Assert.False(FrenRiderPresentation.BackgroundBlurEnabled(config));
        Assert.Equal(0, FrenRiderPresentation.BackgroundFrost(config));
        config.UiColors = config.UiColors.WithBackgroundBlur(MaterialColorMode.Spatial, true)
            .WithBackgroundBlur(MaterialColorMode.Glassmorphism, false) with { BackgroundBlurPercent = 60 };
        var restored = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(config))!;
        Assert.True(FrenRiderPresentation.BackgroundBlurEnabled(restored));
        Assert.Equal(.6f, FrenRiderPresentation.BackgroundFrost(restored), .001f);
        restored.UiStyle = FrenRiderUiStyle.Glassmorphism;
        Assert.False(FrenRiderPresentation.BackgroundBlurEnabled(restored));
        Assert.Equal(0, FrenRiderPresentation.BackgroundFrost(restored));
        restored.UiStyle = FrenRiderUiStyle.Spatial;
        restored.UiStylingEnabled = false;
        Assert.False(FrenRiderPresentation.BackgroundBlurEnabled(restored));
        Assert.Equal(0, FrenRiderPresentation.BackgroundFrost(restored));
        using var scope = FrenRiderPresentation.Push(restored);
        Assert.False(FrenRiderPresentation.Decorated);
        Assert.False(FrenRiderPresentation.IsLcars);
    }

    [Fact]
    public void GlassworkSubmitsOnlyFlaggedBackgroundsWithFrostAndRetainsForegroundAndUnavailablePaint()
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            var config = new Configuration { UiStyle = FrenRiderUiStyle.Cyberpunk };
            config.UiColors = config.UiColors.WithBackgroundBlur(MaterialColorMode.Cyberpunk, true);
            var submissions = new List<(bool Window, float Frost, Vector4 Tint)>();
            var available = true;
            using var renderer = new FrenRiderGlass((list, min, max, rounding, tint, frost, window) =>
            {
                submissions.Add((window, frost, tint));
                return available;
            });
            var io = ImGui.GetIO();
            io.IniFilename = null; io.LogFilename = null; io.DisplaySize = new(1000, 800); io.DeltaTime = 1f / 60;
            io.Fonts.AddFontDefault();
            byte* pixels = null; int width = 0, height = 0;
            io.Fonts.GetTexDataAsRGBA32(0, &pixels, &width, &height);
            io.Fonts.SetTexID(0, new ImTextureID((nint)1));

            ((Vector2 Position, uint Colour)[] Foreground, uint[] PanelColours, int ForegroundSubmissions,
                float Alpha, Vector4 Header, Vector4 Fill, Vector4 Field) Frame()
            {
                submissions.Clear();
                ImGui.NewFrame();
                using var appearance = FrenRiderPresentation.Push(config, renderer);
                using var theme = MaterialTheme.Push(FrenRiderPresentation.Theme(config), 1, MaterialStyleMode.ColorsOnly);
                ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(800, 600));
                ImGui.Begin("Glasswork background contract", ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBackground);
                FrenRiderPresentation.DrawBackdrop();
                using var backgrounds = FrenRiderPresentation.GlassBackgroundStyle();
                var list = ImGui.GetWindowDrawList();
                var first = list.VtxBuffer.Size;
                FrenRiderPresentation.DrawPanel(new(50, 90), new(330, 230), background: true);
                var panelColours = Enumerable.Range(first, list.VtxBuffer.Size - first)
                    .Select(index => list.VtxBuffer[index].Col).ToArray();
                var beforeForeground = submissions.Count;
                first = list.VtxBuffer.Size;
                FrenRiderPresentation.DrawPanel(new(370, 90), new(580, 135), inset: true);
                using (FrenRiderPresentation.NativeFieldStyle()) ImGui.Button("Foreground control");
                FrenRiderPresentation.PaintToggle(new(370, 155), new(46, 24), true, MaterialTheme.Current.Colors.Primary);
                FrenRiderPresentation.PaintChoice(new(440, 155), 24, true, false);
                var foreground = Enumerable.Range(first, list.VtxBuffer.Size - first)
                    .Select(index => (list.VtxBuffer[index].Pos, list.VtxBuffer[index].Col)).ToArray();
                var foregroundSubmissions = submissions.Count - beforeForeground;
                var alpha = ImGui.GetStyle().Alpha;
                var header = ImGui.GetStyle().Colors[(int)ImGuiCol.TableHeaderBg];
                var fill = FrenRiderPresentation.BackgroundFill(MaterialTheme.Current.Colors.SurfaceContainerLow);
                var field = ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg];
                ImGui.End(); ImGui.Render();
                return (foreground, panelColours, foregroundSubmissions, alpha, header, fill, field);
            }

            Frame(); Frame();
            var frosted = Frame();
            Assert.Equal(new[] { (true, .75f), (false, .75f) }, submissions.Select(value => (value.Window, value.Frost)));
            Assert.Equal(0, frosted.ForegroundSubmissions);
            Assert.Equal(1, frosted.Alpha);
            Assert.Equal(Vector4.Zero, frosted.Header);
            Assert.Equal(Vector4.Zero, frosted.Fill);
            Assert.NotEqual(Vector4.Zero, frosted.Field);
            var tint = submissions.Select(value => value.Tint).ToArray();
            var solidPanel = MaterialCanvas.Color(FrenRiderPresentation.Theme(config).Colors.SurfaceContainerLow);
            Assert.DoesNotContain(solidPanel, frosted.PanelColours);

            config.UiColors = config.UiColors with { BackgroundBlurPercent = 25 };
            var lessFrost = Frame();
            Assert.All(submissions, value => Assert.Equal(.25f, value.Frost));
            Assert.Equal(tint, submissions.Select(value => value.Tint));
            Assert.True(frosted.Foreground.SequenceEqual(lessFrost.Foreground));
            Assert.Equal(0, lessFrost.ForegroundSubmissions);
            var originalPalette = config.UiCyberpunkColors;
            config.UiCyberpunkColors = originalPalette with { BackgroundRgb = 0xD02080 };
            Frame();
            Assert.All(submissions, value => Assert.Equal(
                FrenRiderPresentation.Rgb(0xD02080) with { W = .42f }, value.Tint));
            config.UiCyberpunkColors = originalPalette;
            config.UiColors = config.UiColors with { BackgroundBlurPercent = 0 };
            Frame();
            Assert.True(FrenRiderPresentation.BackgroundBlurEnabled(config));
            Assert.Equal(2, submissions.Count);
            Assert.All(submissions, value => Assert.Equal(0, value.Frost));

            config.UiColors = config.UiColors.WithBackgroundBlur(MaterialColorMode.Cyberpunk, false);
            var disabled = Frame();
            Assert.Empty(submissions);
            Assert.Contains(solidPanel, disabled.PanelColours);
            Assert.NotEqual(Vector4.Zero, disabled.Header);
            Assert.NotEqual(Vector4.Zero, disabled.Fill);
            Assert.Equal(frosted.Field, disabled.Field);
            Assert.True(frosted.Foreground.SequenceEqual(disabled.Foreground));
            available = false;
            config.UiColors = config.UiColors.WithBackgroundBlur(MaterialColorMode.Cyberpunk, true);
            var unavailable = Frame();
            Assert.Equal(2, submissions.Count);
            Assert.Equal(disabled.PanelColours, unavailable.PanelColours);
            Assert.Equal(disabled.Header, unavailable.Header);
            Assert.Equal(disabled.Fill, unavailable.Fill);
            Assert.True(disabled.Foreground.SequenceEqual(unavailable.Foreground));
        }
        finally { ImGui.DestroyContext(context); ImGui.SetCurrentContext(previous); }
    }

    [Fact]
    public void SuccessfulGlassmorphismGlassRetainsTranslucentWaveMeshesAnimationAndForeground()
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            var config = new Configuration { UiStyle = FrenRiderUiStyle.Glassmorphism, UiGlassAnimation = false };
            var submissions = new List<(bool Window, float Frost)>();
            using var renderer = new FrenRiderGlass((_, _, _, _, _, frost, window) =>
            {
                submissions.Add((window, frost));
                return true;
            });
            var io = ImGui.GetIO();
            io.IniFilename = null; io.LogFilename = null; io.DisplaySize = new(1000, 800); io.DeltaTime = .1f;
            io.Fonts.AddFontDefault();
            byte* pixels = null; int width = 0, height = 0;
            io.Fonts.GetTexDataAsRGBA32(0, &pixels, &width, &height);
            io.Fonts.SetTexID(0, new ImTextureID((nint)1));

            ((Vector2 Position, uint Colour)[] Waves, int[] Indices, (Vector2 Position, uint Colour)[] Foreground) Frame(float frost = .75f)
            {
                submissions.Clear();
                ImGui.NewFrame();
                using var appearance = FrenRiderPresentation.Push(config, renderer);
                using var theme = MaterialTheme.Push(FrenRiderPresentation.Theme(config), 1, MaterialStyleMode.ColorsOnly);
                ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(800, 600));
                ImGui.Begin("Glasswork waves contract", ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBackground);
                var list = ImGui.GetWindowDrawList();
                var first = list.VtxBuffer.Size;
                var firstIndex = list.IdxBuffer.Size;
                FrenRiderPresentation.DrawBackdrop();
                var waves = Enumerable.Range(first, list.VtxBuffer.Size - first)
                    .Select(index => (list.VtxBuffer[index].Pos, list.VtxBuffer[index].Col)).ToArray();
                var indices = Enumerable.Range(firstIndex, list.IdxBuffer.Size - firstIndex)
                    .Select(index => (int)list.IdxBuffer[index] - first).ToArray();
                first = list.VtxBuffer.Size;
                ImGui.SetCursorScreenPos(new(80, 100));
                using (FrenRiderPresentation.NativeFieldStyle()) ImGui.Button("Foreground control", new(180, 32));
                FrenRiderPresentation.PaintToggle(new(300, 100), new(46, 24), true, MaterialTheme.Current.Colors.Primary);
                FrenRiderPresentation.PaintChoice(new(370, 100), 24, true, false);
                var foreground = Enumerable.Range(first, list.VtxBuffer.Size - first)
                    .Select(index => (list.VtxBuffer[index].Pos, list.VtxBuffer[index].Col)).ToArray();
                ImGui.End(); ImGui.Render();
                Assert.Equal(new[] { (true, frost) }, submissions);
                return (waves, indices, foreground);
            }

            Frame(); Frame();
            var still = Frame();
            var repeated = Frame();
            Assert.NotEmpty(still.Waves);
            Assert.NotEmpty(still.Foreground);
            Assert.Equal(still.Waves, repeated.Waves);
            Assert.Equal(still.Indices, repeated.Indices);
            Assert.All(still.Indices, index => Assert.InRange(index, 0, still.Waves.Length - 1));
            Assert.Contains(Enumerable.Range(0, still.Indices.Length / 3), triangle =>
                (still.Waves[still.Indices[triangle * 3]].Colour & 0xFFFFFF) !=
                    (still.Waves[still.Indices[triangle * 3 + 1]].Colour & 0xFFFFFF)
                || (still.Waves[still.Indices[triangle * 3]].Colour & 0xFFFFFF) !=
                    (still.Waves[still.Indices[triangle * 3 + 2]].Colour & 0xFFFFFF));
            Assert.Contains(still.Waves, vertex => (vertex.Colour >> 24) > 0);
            Assert.DoesNotContain(still.Waves, vertex => (vertex.Colour >> 24) == 255);

            config.UiGlassWave1Rgb = 0xE34A62;
            var recoloured = Frame();
            Assert.Equal(still.Waves.Select(vertex => vertex.Position), recoloured.Waves.Select(vertex => vertex.Position));
            Assert.Equal(still.Indices, recoloured.Indices);
            Assert.False(still.Waves.Select(vertex => vertex.Colour).SequenceEqual(recoloured.Waves.Select(vertex => vertex.Colour)));
            Assert.Equal(still.Foreground, recoloured.Foreground);
            config.UiGlassAnimation = true;
            var moving = Frame();
            var later = Frame();
            Assert.False(moving.Waves.SequenceEqual(later.Waves));
            Assert.Equal(recoloured.Foreground, moving.Foreground);
            Assert.Equal(moving.Foreground, later.Foreground);
            config.UiGlassAnimation = false;
            Assert.Equal(recoloured.Waves, Frame().Waves);

            config.UiColors = config.UiColors with { BackgroundBlurPercent = 0 };
            // Clear glass must still be submitted, without replacing it with an opaque painted backdrop.
            var clear = Frame(0);
            Assert.Equal(recoloured.Waves, clear.Waves);
            Assert.Equal(recoloured.Indices, clear.Indices);
            Assert.DoesNotContain(clear.Waves, vertex => (vertex.Colour >> 24) == 255);
            Assert.Equal(recoloured.Foreground, clear.Foreground);
        }
        finally { ImGui.DestroyContext(context); ImGui.SetCurrentContext(previous); }
    }

    [Fact]
    public void LegacyAppearanceRemainsAutomaticAndManualChoicesRoundTripThroughTheExistingGlobalConfiguration()
    {
        var legacy = JsonConvert.DeserializeObject<Configuration>(
            """{"Version":2,"UiAccentRgb":1193046,"UiLanguage":"ja","DtrBarEnabled":false,"LastAccountId":"fixture-account","UnrelatedFixture":17}""")!;
        Assert.Equal(MaterialColorMode.Auto, legacy.UiColors.Mode);
        Assert.Equal(FrenRiderUiStyle.Classic, legacy.UiStyle);
        Assert.True(legacy.UiGlassAnimation);
        Assert.Equal(1f, legacy.UiGlassAnimationSpeed);
        Assert.Equal(Configuration.DefaultGlassWave1Rgb, legacy.UiGlassWave1Rgb);
        Assert.Equal(Configuration.DefaultGlassWave2Rgb, legacy.UiGlassWave2Rgb);
        Assert.Equal(Configuration.DefaultGlassWave3Rgb, legacy.UiGlassWave3Rgb);
        Assert.True(legacy.UiLcarsSoundsMuted);
        Assert.Equal(0x123456u, legacy.UiAccentRgb);
        Assert.Equal("ja", legacy.UiLanguage);
        Assert.False(legacy.DtrBarEnabled);
        legacy.UiColors = new() { Mode = MaterialColorMode.Picker,
            BackgroundRgb = 0x010203, ControlsRgb = 0x040506, TextRgb = 0x070809 };
        var restored = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(legacy))!;
        Assert.Equal(legacy.UiColors, restored.UiColors);
        Assert.Equal(legacy.Version, restored.Version);
        Assert.Equal(legacy.LastAccountId, restored.LastAccountId);
        Assert.Equal(17, (int)restored.AdditionalSettings!["UnrelatedFixture"]);
        Assert.Equal(legacy.UiAccentRgb, restored.UiAccentRgb);
        Assert.Equal(legacy.UiLanguage, restored.UiLanguage);
        var automatic = FrenRiderPresentation.Theme(FrenRiderPresentation.ReferenceAccent).Colors;
        Assert.Equal(FrenRiderPresentation.Rgb(0x102024), automatic.Background);
        Assert.Equal(FrenRiderPresentation.Rgb(0xE8EEF2), automatic.OnSurface);
        Assert.Equal(FrenRiderPresentation.Rgb(0x70C677), automatic.Primary);
        Assert.Equal(FrenRiderPresentation.Rgb(0x20383D), automatic.SecondaryContainer);
        var manual = FrenRiderPresentation.Theme(legacy.UiAccentRgb, restored.UiColors).Colors;
        Assert.Equal(new Vector4(MaterialColorSettings.Rgb(0x010203), 1), manual.Background);
        Assert.Equal(new Vector4(MaterialColorSettings.Rgb(0x040506), 1), manual.Primary);
        Assert.Equal(new Vector4(MaterialColorSettings.Rgb(0x070809), 1), manual.OnSurface);
    }

    [Fact]
    public void EveryAdvertisedLocaleProvidesAllColourEditorLabels()
    {
        string[] keys = ["Auto", "Picker", "Default", "Background, controls and text follow your theme.", "Theme colour",
            "Choose each colour independently.", "Background", "Controls", "Text", "Aa  Preview text", "Control",
            "Switch to Picker to set each colour yourself.", "Switch back to Auto to use the automatic theme.",
            "LCARS", "LCARS panels, bars and controls", "LCARS UI sounds", "UI sounds", "Preview sound",
            "Custom UI tones. The titlebar speaker toggles the same setting.", "Select LCARS to use UI sounds.",
            "Turn on UI sounds to preview.", "Audio output is unavailable.", "UI sounds muted", "UI sounds on",
            "Glassmorphism", "Spatial", "Cyberpunk", "Animation", "Bar colour 1", "Bar colour 2", "Bar colour 3",
            "Reset", "Reset colours", "Movement speed (x)", "Wave colour 1", "Wave colour 2", "Wave colour 3",
            "Reset wave colours", "Background blur", "Blur (%)", "UI styling",
            "Lock window movement and resizing", "Use native UI", "Use styled UI", "Lock windows", "Unlock windows"];
        foreach (var language in UiText.Languages)
        {
            using var text = new UiText(language.Code, _ => new NoFont());
            foreach (var key in keys) Assert.False(string.IsNullOrWhiteSpace(text.Resources.GetString(key, false)), language.Code + ": " + key);
        }
    }

    [Theory]
    [InlineData(false, 1f, 600f)]
    [InlineData(true, 1.5f, 600f)]
    [InlineData(true, 1f, 320f)]
    public void RealTitlebarCallbackSavesTheGlobalModeAndKeepsManualChoicesWhenReturningToAuto(bool compact, float scale, float windowWidth)
    {
        var interfaceProperty = typeof(Plugin).GetProperty("PluginInterface", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousInterface = interfaceProperty.GetValue(null);
        var previousContext = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        var saves = new List<string>();
        try
        {
            interfaceProperty.SetValue(null, ActivationModeTests.Proxy<IDalamudPluginInterface>((method, args) =>
            {
                if (method.Name == "SavePluginConfig") saves.Add(JsonConvert.SerializeObject(args![0]));
                return null;
            }));
            using var text = new UiText("en", _ => new NoFont());
            using var textScope = text.Enter();
            var config = new Configuration { UiCompact = compact, LastAccountId = "fixture-account" };
            var plugin = (Plugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
            typeof(Plugin).GetProperty(nameof(Plugin.Configuration))!.SetValue(plugin, config);
            typeof(Plugin).GetField("colorDraft", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(plugin, config.UiColors);
            typeof(Plugin).GetField("accentDraft", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(plugin, MaterialColorSettings.Rgb(config.UiAccentRgb));
            var draw = typeof(Plugin).GetMethod("DrawTitleColour", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var apply = typeof(Plugin).GetMethod("ApplyAppearance", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var themeField = typeof(Plugin).GetField("uiTheme", BindingFlags.Instance | BindingFlags.NonPublic)!;
            typeof(Plugin).GetField("appliedLanguage", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(plugin, "en");
            var glyphColors = new List<uint>();
            var io = ImGui.GetIO(); io.IniFilename = null; io.LogFilename = null;
            io.DisplaySize = new(1200 * scale, 1500 * scale); io.DeltaTime = 1f / 60; io.FontGlobalScale = scale;
            io.Fonts.AddFontDefault();
            byte* pixels = null; int width = 0, height = 0;
            io.Fonts.GetTexDataAsRGBA32(0, &pixels, &width, &height);
            io.Fonts.SetTexID(0, new ImTextureID((nint)1));
            ImDrawListPtr list = default;
            var popup = false;
            var openPopup = false;
            var popupVisible = false;
            void Frame()
            {
                ImGui.NewFrame();
                apply.Invoke(plugin, null);
                using var appearance = FrenRiderPresentation.Push(config);
                using var theme = MaterialTheme.Push((MaterialTheme)themeField.GetValue(plugin)!, scale, MaterialStyleMode.ColorsOnly);
                ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(windowWidth * scale, 1350 * scale));
                ImGui.Begin("FrenRider Colour callback", ImGuiWindowFlags.NoSavedSettings);
                if (popup)
                {
                    draw.Invoke(plugin, null);
                    if (openPopup) { ImGui.OpenPopup("Native colour popup"); openPopup = false; }
                    ImGui.SetNextWindowPos(new(80 * scale, 80 * scale));
                    ImGui.SetNextWindowSize(new(Math.Min(480, windowWidth) * scale, 1280 * scale));
                    popupVisible = ImGui.BeginPopup("Native colour popup");
                }
                if (!popup || popupVisible)
                {
                    draw.Invoke(plugin, null);
                    list = ImGui.GetWindowDrawList();
                    var firstGlyph = list.VtxBuffer.Size;
                    UiGui.TextUnformatted("ColourProbe");
                    glyphColors.Clear();
                    for (var vertex = firstGlyph; vertex < list.VtxBuffer.Size; vertex++) glyphColors.Add(list.VtxBuffer[vertex].Col);
                }
                if (popupVisible) ImGui.EndPopup();
                ImGui.End();
                ImGui.Render();
            }
            void Click(string caption, int occurrence = 0)
            {
                var point = Center(caption, occurrence);
                io.AddMousePosEvent(point.X, point.Y); Frame();
                io.AddMouseButtonEvent(0, true); Frame();
                io.AddMouseButtonEvent(0, false); Frame();
            }
            Vector2 Center(string caption, int occurrence = 0)
            {
                var ink = caption.Replace(" ", "");
                for (var index = 0; index + ink.Length * 4 <= list.VtxBuffer.Size; index++)
                {
                    var matched = true;
                    for (var character = 0; character < ink.Length; character++)
                    {
                        var glyph = ImGui.FindGlyphNoFallback(ImGui.GetFont(), ink[character]);
                        if (list.VtxBuffer[index + character * 4].Uv != new Vector2(glyph.Handle->U0, glyph.Handle->V0)
                            || list.VtxBuffer[index + character * 4 + 2].Uv != new Vector2(glyph.Handle->U1, glyph.Handle->V1))
                        { matched = false; break; }
                    }
                    if (matched && occurrence-- == 0) return (list.VtxBuffer[index].Pos + list.VtxBuffer[index + ink.Length * 4 - 2].Pos) / 2;
                }
                throw new InvalidOperationException("Real colour callback did not show " + caption);
            }
            Frame(); Frame(); Click("Picker");
            Assert.Single(saves);
            var restored = JsonConvert.DeserializeObject<Configuration>(saves[0])!;
            Assert.Equal(MaterialColorMode.Picker, restored.UiColors.Mode);
            Assert.Equal(config.UiColors, restored.UiColors);
            Assert.Equal("fixture-account", restored.LastAccountId);
            var originalColors = new[] { config.UiColors.BackgroundRgb, config.UiColors.ControlsRgb, config.UiColors.TextRgb };
            var replacements = new[] { "#193149", "#3579BD", "#E51A73" };
            for (var field = 0; field < 3; field++)
            {
                var before = config.UiColors;
                Click("#" + originalColors[field].ToString("X6"));
                io.AddKeyEvent(ImGuiKey.ModCtrl, true); io.AddKeyEvent(ImGuiKey.A, true); Frame();
                io.AddKeyEvent(ImGuiKey.A, false); io.AddKeyEvent(ImGuiKey.ModCtrl, false); Frame();
                foreach (var character in replacements[field]) io.AddInputCharacter(character);
                Frame();
                Assert.Equal(1 + field, saves.Count);
                io.AddKeyEvent(ImGuiKey.Enter, true); Frame();
                io.AddKeyEvent(ImGuiKey.Enter, false); Frame();
                Assert.Equal(2 + field, saves.Count);
                restored = JsonConvert.DeserializeObject<Configuration>(saves[^1])!;
                var expected = Convert.ToUInt32(replacements[field][1..], 16);
                Assert.Equal(field == 0 ? expected : before.BackgroundRgb, restored.UiColors.BackgroundRgb);
                Assert.Equal(field == 1 ? expected : before.ControlsRgb, restored.UiColors.ControlsRgb);
                Assert.Equal(field == 2 ? expected : before.TextRgb, restored.UiColors.TextRgb);
                Assert.Equal(config.UiColors, restored.UiColors);
                Assert.NotEmpty(glyphColors);
                var textInk = ImGui.ColorConvertFloat4ToU32(new Vector4(MaterialColorSettings.Rgb(restored.UiColors.TextRgb), 1));
                Assert.All(glyphColors, actualInk => Assert.Equal(textInk, actualInk));
                Assert.Equal("fixture-account", restored.LastAccountId);
            }
            var manual = config.UiColors;
            Click("Auto");
            Assert.Equal(5, saves.Count);
            restored = JsonConvert.DeserializeObject<Configuration>(saves[^1])!;
            Assert.Equal(config.UiColors, restored.UiColors);
            Assert.Equal(manual with { Mode = MaterialColorMode.Auto }, restored.UiColors);
            Click("LCARS"); Frame(); Frame();
            restored = JsonConvert.DeserializeObject<Configuration>(saves[^1])!;
            Assert.Equal(manual with { Mode = MaterialColorMode.Lcars }, restored.UiColors);
            Assert.True(plugin.LcarsActive);
            Assert.True(restored.UiLcarsSoundsMuted);
            Click("Picker"); Frame();
            Assert.Equal(manual, config.UiColors);

            void EditColour(uint original, string replacement, int occurrence = 0)
            {
                var before = saves.Count;
                Click("#" + original.ToString("X6"), occurrence);
                io.AddKeyEvent(ImGuiKey.ModCtrl, true); io.AddKeyEvent(ImGuiKey.A, true); Frame();
                io.AddKeyEvent(ImGuiKey.A, false); io.AddKeyEvent(ImGuiKey.ModCtrl, false); Frame();
                foreach (var character in replacement) io.AddInputCharacter(character);
                Frame();
                Assert.Equal(before, saves.Count);
                io.AddKeyEvent(ImGuiKey.Enter, true); Frame();
                io.AddKeyEvent(ImGuiKey.Enter, false); Frame();
                Assert.Equal(before + 1, saves.Count);
            }
            uint[] WaveColours(Configuration current) => [current.UiGlassWave1Rgb, current.UiGlassWave2Rgb, current.UiGlassWave3Rgb];
            void ExerciseWaveEditor()
            {
                var glass = config.UiGlassColors;
                var picker = config.UiColors;
                var spatial = config.UiSpatialColors;
                var cyberpunk = config.UiCyberpunkColors;
                var lcars = (config.UiLcarsBackgroundRgb, config.UiLcarsBar1Rgb, config.UiLcarsBar2Rgb, config.UiLcarsBar3Rgb);
                var animation = (config.UiGlassAnimation, config.UiGlassAnimationSpeed);
                uint[] defaults = [Configuration.DefaultGlassWave1Rgb, Configuration.DefaultGlassWave2Rgb, Configuration.DefaultGlassWave3Rgb];
                uint[] custom = [0xE34A62, 0x76C349, 0xA66CE8];
                void AssertIsolated(Configuration current)
                {
                    Assert.Equal(glass, current.UiGlassColors);
                    Assert.Equal(picker, current.UiColors);
                    Assert.Equal(spatial, current.UiSpatialColors);
                    Assert.Equal(cyberpunk, current.UiCyberpunkColors);
                    Assert.Equal(lcars, (current.UiLcarsBackgroundRgb, current.UiLcarsBar1Rgb, current.UiLcarsBar2Rgb, current.UiLcarsBar3Rgb));
                    Assert.Equal(animation, (current.UiGlassAnimation, current.UiGlassAnimationSpeed));
                    Assert.Equal(FrenRiderUiStyle.Glassmorphism, current.UiStyle);
                }
                void EditWave(int wave, uint replacement)
                {
                    var before = WaveColours(config);
                    var original = before[wave];
                    var occurrence = new[] { glass.BackgroundRgb, glass.ControlsRgb, glass.TextRgb }.Count(rgb => rgb == original)
                        + before.Take(wave).Count(rgb => rgb == original);
                    EditColour(original, "#" + replacement.ToString("X6"), occurrence);
                    before[wave] = replacement;
                    Assert.Equal(before, WaveColours(config));
                    var saved = JsonConvert.DeserializeObject<Configuration>(saves[^1])!;
                    Assert.Equal(before, WaveColours(saved));
                    AssertIsolated(saved);
                }
                for (var wave = 0; wave < 3; wave++) EditWave(wave, custom[wave]);
                for (var wave = 0; wave < 3; wave++)
                {
                    var before = saves.Count;
                    // Background, Controls, Text, Reset colours and speed retain their existing order.
                    Click("Reset", 5 + wave); Frame();
                    Assert.Equal(before + 1, saves.Count);
                    var expected = custom.ToArray(); expected[wave] = defaults[wave];
                    Assert.Equal(expected, WaveColours(config));
                    var saved = JsonConvert.DeserializeObject<Configuration>(saves[^1])!;
                    Assert.Equal(expected, WaveColours(saved));
                    AssertIsolated(saved);
                    EditWave(wave, custom[wave]);
                }
                var beforeResetAll = saves.Count;
                Click("Reset wave colours"); Frame();
                Assert.Equal(beforeResetAll + 1, saves.Count);
                Assert.Equal(defaults, WaveColours(config));
                var resetSaved = JsonConvert.DeserializeObject<Configuration>(saves[^1])!;
                Assert.Equal(defaults, WaveColours(resetSaved));
                AssertIsolated(resetSaved);
                var atDefaultSaves = saves.Count;
                Click("Reset wave colours");
                for (var wave = 0; wave < 3; wave++) Click("Reset", 5 + wave);
                Assert.Equal(atDefaultSaves, saves.Count);
                AssertIsolated(config);
            }
            foreach (var style in new[] { FrenRiderUiStyle.Glassmorphism, FrenRiderUiStyle.Spatial, FrenRiderUiStyle.Cyberpunk })
            {
                Click(style.ToString()); Frame();
                Assert.Equal(style, config.UiStyle);
                var before = FrenRiderPresentation.Palette(config);
                var picker = config.UiColors;
                EditColour(before.BackgroundRgb, "#162534");
                var after = FrenRiderPresentation.Palette(config);
                Assert.Equal(0x162534u, after.BackgroundRgb);
                Assert.Equal(before.ControlsRgb, after.ControlsRgb);
                Assert.Equal(before.TextRgb, after.TextRgb);
                Assert.Equal(picker, config.UiColors);
                var saved = JsonConvert.DeserializeObject<Configuration>(saves[^1])!;
                Assert.Equal(style, saved.UiStyle);
                Assert.Equal(after, FrenRiderPresentation.Palette(saved));
            }
            Click("Glassmorphism"); Frame();
            Assert.True(config.UiGlassAnimation);
            Click("Animation"); Frame();
            Assert.False(config.UiGlassAnimation);
            Assert.False(JsonConvert.DeserializeObject<Configuration>(saves[^1])!.UiGlassAnimation);
            Click("LCARS"); Frame();
            EditColour(config.UiLcarsBackgroundRgb, "#111820");
            EditColour(config.UiLcarsBar1Rgb, "#55D6BE");
            EditColour(config.UiLcarsBar2Rgb, "#F2A7C6");
            EditColour(config.UiLcarsBar3Rgb, "#86B8FF");
            Assert.Equal(0x111820u, config.UiLcarsBackgroundRgb);
            Assert.Equal(0x55D6BEu, config.UiLcarsBar1Rgb);
            Assert.Equal(0xF2A7C6u, config.UiLcarsBar2Rgb);
            Assert.Equal(0x86B8FFu, config.UiLcarsBar3Rgb);
            var lcarsColors = FrenRiderPresentation.Theme(config).Colors;
            Assert.True(MaterialColor.Contrast(lcarsColors.Background, lcarsColors.OnSurface) >= 4.5f);
            Click("Picker"); Frame();
            Assert.Equal(manual, config.UiColors);
            Assert.Equal(FrenRiderUiStyle.Classic, config.UiStyle);
            Assert.Equal("fixture-account", config.LastAccountId);

            Click("Glassmorphism");
            var beforePopup = config.UiGlassColors;
            var pickerBeforePopup = config.UiColors;
            popup = openPopup = true;
            Frame(); Frame();
            Assert.True(popupVisible);
            var beforePopupSave = saves.Count;
            Click("#" + beforePopup.BackgroundRgb.ToString("X6"));
            io.AddKeyEvent(ImGuiKey.ModCtrl, true); io.AddKeyEvent(ImGuiKey.A, true); Frame();
            io.AddKeyEvent(ImGuiKey.A, false); io.AddKeyEvent(ImGuiKey.ModCtrl, false); Frame();
            foreach (var character in "#28465A") io.AddInputCharacter(character);
            Frame();
            Assert.Equal(beforePopupSave, saves.Count);
            Assert.Equal(beforePopup, config.UiGlassColors);
            io.AddMousePosEvent(30 * scale, 730 * scale); Frame();
            io.AddMouseButtonEvent(0, true); Frame();
            io.AddMouseButtonEvent(0, false); Frame();
            Assert.False(popupVisible);
            Assert.Equal(beforePopupSave + 1, saves.Count);
            restored = JsonConvert.DeserializeObject<Configuration>(saves[^1])!;
            Assert.Equal(beforePopup with { BackgroundRgb = 0x28465A }, restored.UiGlassColors);
            Assert.Equal(pickerBeforePopup, restored.UiColors);
            Frame();
            Assert.Equal(beforePopupSave + 1, saves.Count);

            openPopup = true;
            Frame(); Frame();
            Assert.True(popupVisible);
            ExerciseWaveEditor();
            io.AddMousePosEvent(30 * scale, 730 * scale); Frame();
            io.AddMouseButtonEvent(0, true); Frame();
            io.AddMouseButtonEvent(0, false); Frame();
            Assert.False(popupVisible);

            // The same visible controls serve a titlebar popup and a narrow Settings pane.
            popup = false;
            Frame(); Frame();
            ExerciseWaveEditor();
            var spatialBeforeReset = config.UiSpatialColors;
            var cyberBeforeReset = config.UiCyberpunkColors;
            var glassBeforeReset = config.UiGlassColors;
            EditColour(glassBeforeReset.ControlsRgb, "#D070AA");
            Click("Reset", 1); Frame();
            Assert.Equal(Configuration.DefaultPalette(FrenRiderUiStyle.Glassmorphism).ControlsRgb, config.UiGlassColors.ControlsRgb);
            Assert.Equal(glassBeforeReset.BackgroundRgb, config.UiGlassColors.BackgroundRgb);
            Assert.Equal(glassBeforeReset.TextRgb, config.UiGlassColors.TextRgb);
            Assert.Equal(spatialBeforeReset, config.UiSpatialColors);
            Assert.Equal(cyberBeforeReset, config.UiCyberpunkColors);

            Click("Animation"); Frame();
            Assert.True(config.UiGlassAnimation);
            Click("1.00");
            io.AddKeyEvent(ImGuiKey.ModCtrl, true); io.AddKeyEvent(ImGuiKey.A, true); Frame();
            io.AddKeyEvent(ImGuiKey.A, false); io.AddKeyEvent(ImGuiKey.ModCtrl, false); Frame();
            foreach (var character in "2.50") io.AddInputCharacter(character);
            Frame();
            io.AddKeyEvent(ImGuiKey.Enter, true); Frame();
            io.AddKeyEvent(ImGuiKey.Enter, false); Frame();
            Assert.Equal(2.5f, config.UiGlassAnimationSpeed);
            Assert.Equal(2.5f, JsonConvert.DeserializeObject<Configuration>(saves[^1])!.UiGlassAnimationSpeed);
            Click("Reset colours"); Frame();
            Assert.Equal(Configuration.DefaultPalette(FrenRiderUiStyle.Glassmorphism), config.UiGlassColors);
            Assert.Equal(2.5f, config.UiGlassAnimationSpeed);
            Assert.True(config.UiGlassAnimation);
            Click("Reset", 4); Frame();
            Assert.Equal(1f, config.UiGlassAnimationSpeed);
            var atDefaultSaves = saves.Count;
            Click("Reset"); Click("Reset colours");
            Click("Glassmorphism");
            Assert.Equal(atDefaultSaves, saves.Count);
            Assert.Equal(spatialBeforeReset, config.UiSpatialColors);
            Assert.Equal(cyberBeforeReset, config.UiCyberpunkColors);
            Assert.Equal(pickerBeforePopup, config.UiColors);

            Click("LCARS"); Frame();
            var lcarsBackground = config.UiLcarsBackgroundRgb;
            var lcarsFirst = config.UiLcarsBar1Rgb;
            var lcarsThird = config.UiLcarsBar3Rgb;
            Click("Reset", 2); Frame();
            Assert.Equal(Configuration.DefaultLcarsBar2Rgb, config.UiLcarsBar2Rgb);
            Assert.Equal(lcarsBackground, config.UiLcarsBackgroundRgb);
            Assert.Equal(lcarsFirst, config.UiLcarsBar1Rgb);
            Assert.Equal(lcarsThird, config.UiLcarsBar3Rgb);
            Click("Reset colours"); Frame();
            Assert.Equal(Configuration.DefaultLcarsBackgroundRgb, config.UiLcarsBackgroundRgb);
            Assert.Equal(Configuration.DefaultLcarsBar1Rgb, config.UiLcarsBar1Rgb);
            Assert.Equal(Configuration.DefaultLcarsBar2Rgb, config.UiLcarsBar2Rgb);
            Assert.Equal(Configuration.DefaultLcarsBar3Rgb, config.UiLcarsBar3Rgb);

            foreach (var style in new[] { FrenRiderUiStyle.Spatial, FrenRiderUiStyle.Cyberpunk })
            {
                Click(style.ToString()); Frame();
                Click("Reset colours"); Frame();
                Assert.Equal(style, config.UiStyle);
                Assert.Equal(Configuration.DefaultPalette(style), FrenRiderPresentation.Palette(config));
                Assert.Equal(Configuration.DefaultPalette(FrenRiderUiStyle.Glassmorphism), config.UiGlassColors);
            }
            Click("Picker"); Frame();
            var pickerBeforeReset = config.UiColors;
            Click("Reset", 2); Frame();
            Assert.Equal(new MaterialColorSettings().TextRgb, config.UiColors.TextRgb);
            Assert.Equal(pickerBeforeReset.BackgroundRgb, config.UiColors.BackgroundRgb);
            Assert.Equal(pickerBeforeReset.ControlsRgb, config.UiColors.ControlsRgb);
            Click("Reset colours"); Frame();
            Assert.Equal(Configuration.DefaultPalette(FrenRiderUiStyle.Classic) with { Mode = MaterialColorMode.Picker }, config.UiColors);
            var pickerDefaults = config.UiColors;
            config.UiAccentRgb = 0x3579BD;
            Click("Auto"); Frame();
            Click("Reset colours"); Frame();
            Assert.Equal(FrenRiderPresentation.ReferenceAccent, config.UiAccentRgb);
            Assert.Equal(MaterialColorMode.Auto, config.UiColors.Mode);
            Assert.Equal(pickerDefaults with { Mode = MaterialColorMode.Auto }, config.UiColors);
            Assert.Equal("fixture-account", config.LastAccountId);
            Assert.Equal("en", config.UiLanguage);
        }
        finally
        {
            interfaceProperty.SetValue(null, previousInterface);
            ImGui.DestroyContext(context); ImGui.SetCurrentContext(previousContext);
        }
    }

    [Fact]
    public void NewPalettesGlassWavesAndFourLcarsRolesRoundTripIndependentlyWithoutReplacingLegacySettings()
    {
        var config = JsonConvert.DeserializeObject<Configuration>("""{"UiColors":{"Mode":1,"BackgroundRgb":66051,"ControlsRgb":263430,"TextRgb":460809},"UiGlassColors":{"Mode":1,"BackgroundRgb":1122867,"ControlsRgb":4478310,"TextRgb":7833753},"UiLanguage":"ja","LastAccountId":"fixture-account","OtherValue":42}""")!;
        Assert.Equal(Configuration.DefaultGlassWave1Rgb, config.UiGlassWave1Rgb);
        Assert.Equal(Configuration.DefaultGlassWave2Rgb, config.UiGlassWave2Rgb);
        Assert.Equal(Configuration.DefaultGlassWave3Rgb, config.UiGlassWave3Rgb);
        var picker = config.UiColors;
        var spatial = config.UiSpatialColors;
        var cyberpunk = config.UiCyberpunkColors;
        config.UiStyle = FrenRiderUiStyle.Glassmorphism;
        config.UiGlassColors = config.UiGlassColors with { BackgroundRgb = 0x27182E, ControlsRgb = 0xEFA8C4, TextRgb = 0xFFF4E8 };
        config.UiLcarsBar2Rgb = 0x123456;
        config.UiGlassAnimation = false;
        config.UiGlassAnimationSpeed = 2.5f;
        config.UiGlassWave1Rgb = 0xF06182;
        config.UiGlassWave2Rgb = 0x83D453;
        config.UiGlassWave3Rgb = 0x9C71E8;
        var restored = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(config))!;
        Assert.Equal(picker, restored.UiColors);
        Assert.Equal(spatial, restored.UiSpatialColors);
        Assert.Equal(cyberpunk, restored.UiCyberpunkColors);
        Assert.Equal(config.UiGlassColors, restored.UiGlassColors);
        Assert.Equal(0x123456u, restored.UiLcarsBar2Rgb);
        Assert.Equal(0xFFAA66u, restored.UiLcarsBar1Rgb);
        Assert.Equal(0x9999FFu, restored.UiLcarsBar3Rgb);
        Assert.False(restored.UiGlassAnimation);
        Assert.Equal(2.5f, restored.UiGlassAnimationSpeed);
        Assert.Equal(config.UiGlassWave1Rgb, restored.UiGlassWave1Rgb);
        Assert.Equal(config.UiGlassWave2Rgb, restored.UiGlassWave2Rgb);
        Assert.Equal(config.UiGlassWave3Rgb, restored.UiGlassWave3Rgb);
        restored.UiGlassAnimationSpeed = float.NaN;
        Assert.Equal(1f, restored.UiGlassAnimationSpeed);
        restored.UiGlassAnimationSpeed = 100;
        Assert.Equal(10f, restored.UiGlassAnimationSpeed);
        restored.UiGlassAnimationSpeed = -1;
        Assert.Equal(.1f, restored.UiGlassAnimationSpeed);
        Assert.Equal("ja", restored.UiLanguage);
        Assert.Equal("fixture-account", restored.LastAccountId);
        Assert.Equal(42, (int)restored.AdditionalSettings!["OtherValue"]);
    }

    [Theory]
    [InlineData(FrenRiderUiStyle.Glassmorphism, 0x10232Du, 0x65C7D2u, 0xEDF7FAu)]
    [InlineData(FrenRiderUiStyle.Spatial, 0xE8EDF4u, 0x6678A8u, 0x1C2538u)]
    [InlineData(FrenRiderUiStyle.Cyberpunk, 0x0B0D13u, 0x19DCE8u, 0xEAF8FFu)]
    public void NewStylesUseTheirApprovedBaseColours(FrenRiderUiStyle style, uint background, uint controls, uint text)
    {
        var config = new Configuration { UiStyle = style };
        var theme = FrenRiderPresentation.Theme(config);
        Assert.Equal(FrenRiderPresentation.Rgb(background), theme.Colors.Background);
        Assert.Equal(FrenRiderPresentation.Rgb(controls), theme.Colors.Primary);
        Assert.Equal(FrenRiderPresentation.Rgb(text), theme.Colors.OnSurface);
    }

    [Fact]
    public void GlassAnimationOffKeepsBackdropStaticAndOnChangesItsActualMeshes()
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            var config = new Configuration { UiStyle = FrenRiderUiStyle.Glassmorphism, UiGlassAnimation = false };
            var theme = FrenRiderPresentation.Theme(config);
            var io = ImGui.GetIO();
            io.IniFilename = null; io.LogFilename = null; io.DisplaySize = new(1000, 800); io.DeltaTime = .1f;
            io.Fonts.AddFontDefault();
            byte* pixels = null; int width = 0, height = 0;
            io.Fonts.GetTexDataAsRGBA32(0, &pixels, &width, &height);
            io.Fonts.SetTexID(0, new ImTextureID((nint)1));
            int[] indices = [];
            (Vector2 Position, uint Colour)[] Frame(Vector2? size = null)
            {
                ImGui.NewFrame();
                using var appearance = FrenRiderPresentation.Push(config);
                using var themed = MaterialTheme.Push(theme, 1, MaterialStyleMode.ColorsOnly);
                ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(size ?? new(800, 600));
                ImGui.Begin("Glass motion", ImGuiWindowFlags.NoSavedSettings);
                var list = ImGui.GetWindowDrawList();
                var first = list.VtxBuffer.Size;
                var firstIndex = list.IdxBuffer.Size;
                FrenRiderPresentation.DrawBackdrop();
                var vertices = Enumerable.Range(first, list.VtxBuffer.Size - first)
                    .Select(index => (list.VtxBuffer[index].Pos, list.VtxBuffer[index].Col)).ToArray();
                indices = Enumerable.Range(firstIndex, list.IdxBuffer.Size - firstIndex)
                    .Select(index => (int)list.IdxBuffer[index] - first).ToArray();
                ImGui.End(); ImGui.Render();
                return vertices;
            }
            Frame(); Frame();
            var still = Frame();
            Assert.NotEmpty(still);
            Assert.Equal(still, Frame());
            Assert.All(indices, index => Assert.InRange(index, 0, still.Length - 1));
            Assert.Contains(Enumerable.Range(0, indices.Length / 3), triangle =>
                (still[indices[triangle * 3]].Colour & 0xFFFFFF) != (still[indices[triangle * 3 + 1]].Colour & 0xFFFFFF)
                || (still[indices[triangle * 3]].Colour & 0xFFFFFF) != (still[indices[triangle * 3 + 2]].Colour & 0xFFFFFF));
            config.UiGlassAnimation = true;
            var moving = Frame();
            Assert.False(moving.SequenceEqual(Frame()));
            Assert.True(config.UiGlassAnimation);

            static long MotionDifference((Vector2 Position, uint Colour)[] before, (Vector2 Position, uint Colour)[] after)
            {
                long difference = 0;
                for (var vertex = 0; vertex < before.Length; vertex++)
                    for (var shift = 0; shift < 24; shift += 8)
                        difference += Math.Abs((int)((before[vertex].Colour >> shift) & 255) - (int)((after[vertex].Colour >> shift) & 255));
                return difference;
            }
            config.UiGlassAnimationSpeed = .1f;
            var slow = MotionDifference(Frame(), Frame());
            config.UiGlassAnimationSpeed = 10;
            var fast = MotionDifference(Frame(), Frame());
            Assert.True(fast > slow, $"Expected faster wave movement: slow={slow}, fast={fast}");
            config.UiGlassAnimation = false;
            var frozen = Frame();
            config.UiGlassAnimationSpeed = .1f;
            Assert.Equal(frozen, Frame());
            config.UiGlassColors = config.UiGlassColors with { ControlsRgb = 0xE95061 };
            theme = FrenRiderPresentation.Theme(config);
            Assert.Equal(frozen, Frame());
            Assert.Equal(FrenRiderPresentation.Rgb(config.UiGlassColors.TextRgb), theme.Colors.OnSurface);
            var glass = config.UiGlassColors;
            var picker = config.UiColors;
            var spatial = config.UiSpatialColors;
            var cyberpunk = config.UiCyberpunkColors;
            var lcars = (config.UiLcarsBackgroundRgb, config.UiLcarsBar1Rgb, config.UiLcarsBar2Rgb, config.UiLcarsBar3Rgb);
            uint[] replacements = [0xE34A62, 0x76C349, 0xA66CE8];
            for (var wave = 0; wave < 3; wave++)
            {
                var beforeMesh = Frame();
                var beforeTheme = theme.Colors;
                var beforeWaves = new[] { config.UiGlassWave1Rgb, config.UiGlassWave2Rgb, config.UiGlassWave3Rgb };
                switch (wave)
                {
                    case 0: config.UiGlassWave1Rgb = replacements[wave]; break;
                    case 1: config.UiGlassWave2Rgb = replacements[wave]; break;
                    case 2: config.UiGlassWave3Rgb = replacements[wave]; break;
                }
                theme = FrenRiderPresentation.Theme(config);
                var recoloured = Frame();
                Assert.False(beforeMesh.Select(vertex => vertex.Colour).SequenceEqual(recoloured.Select(vertex => vertex.Colour)));
                Assert.Equal(beforeMesh.Select(vertex => vertex.Position), recoloured.Select(vertex => vertex.Position));
                beforeWaves[wave] = replacements[wave];
                Assert.Equal(beforeWaves, new[] { config.UiGlassWave1Rgb, config.UiGlassWave2Rgb, config.UiGlassWave3Rgb });
                Assert.Equal(glass, config.UiGlassColors);
                Assert.Equal(picker, config.UiColors);
                Assert.Equal(spatial, config.UiSpatialColors);
                Assert.Equal(cyberpunk, config.UiCyberpunkColors);
                Assert.Equal(lcars, (config.UiLcarsBackgroundRgb, config.UiLcarsBar1Rgb, config.UiLcarsBar2Rgb, config.UiLcarsBar3Rgb));
                Assert.Equal(beforeTheme.Background, theme.Colors.Background);
                Assert.Equal(beforeTheme.Primary, theme.Colors.Primary);
                Assert.Equal(beforeTheme.Secondary, theme.Colors.Secondary);
                Assert.Equal(beforeTheme.Tertiary, theme.Colors.Tertiary);
                Assert.Equal(beforeTheme.OnSurface, theme.Colors.OnSurface);
            }
            io.DisplaySize = new(4096, 2400);
            var large = Frame(new(3840, 2160));
            Assert.InRange(large.Length, 1, 32768);
            Assert.All(indices, index => Assert.InRange(index, 0, large.Length - 1));
        }
        finally { ImGui.DestroyContext(context); ImGui.SetCurrentContext(previous); }
    }

    private sealed class BackgroundProbeWindow(string name) : Window(name)
    {
        public override void Draw() { }
    }

    private sealed class NoFont : IDisposable { public void Dispose() { } }
}
