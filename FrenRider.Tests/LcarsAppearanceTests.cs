using System.Reflection;
using System.Runtime.CompilerServices;
using System.Numerics;
using AethertekUI;
using AethertekUI.Dalamud;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using FrenRider.Windows;
using FrenRider.Services;
using Xunit;

namespace FrenRider.Tests;

[Collection("Activation mode static services")]
public sealed class LcarsAppearanceTests
{
    [Theory]
    [InlineData(false, false, 1f)]
    [InlineData(true, false, 1.5f)]
    [InlineData(false, true, 1.5f)]
    [InlineData(true, true, 1f)]
    public unsafe void ActualMiniAndWarningRetainNativeActionsWithinTheirAutomaticWindows(bool compact, bool eureka, float scale)
    {
        var clientProperty = typeof(Plugin).GetProperty("ClientState", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousClient = clientProperty.GetValue(null);
        var commandProperty = typeof(Plugin).GetProperty("CommandManager", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousCommands = commandProperty.GetValue(null);
        var commands = new List<string>();
        var previousContext = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            clientProperty.SetValue(null, ActivationModeTests.Proxy<IClientState>((method, _) => method.Name == "get_IsLoggedIn" ? false : null));
            commandProperty.SetValue(null, ActivationModeTests.Proxy<ICommandManager>((method, args) =>
            {
                if (method.Name != "ProcessCommand") return null;
                commands.Add((string)args![0]!);
                return true;
            }));
            using var text = new UiText("en", _ => new NoFont());
            using var textScope = text.Enter();
            var io = ImGui.GetIO();
            io.IniFilename = null; io.LogFilename = null;
            io.DisplaySize = new(1600, 1400); io.DeltaTime = 1f / 60; io.FontGlobalScale = scale;
            io.Fonts.AddFontDefault();
            byte* pixels = null; int width = 0, height = 0;
            io.Fonts.GetTexDataAsRGBA32(0, &pixels, &width, &height);
            io.Fonts.SetTexID(0, new ImTextureID((nint)1));
            ActivationModeTests.WithManager((manager, config) =>
            {
                var settings = new Configuration { UiCompact = compact, UiColors = new() { Mode = MaterialColorMode.Lcars } };
                var plugin = (Plugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
                typeof(Plugin).GetProperty(nameof(Plugin.Configuration))!.SetValue(plugin, settings);
                typeof(Plugin).GetProperty(nameof(Plugin.ConfigManager))!.SetValue(plugin, manager);
                var zone = new ZoneService();
                typeof(ZoneService).GetProperty(nameof(ZoneService.TerritoryId))!.SetValue(zone, eureka ? 732u : 0u);
                typeof(Plugin).GetProperty(nameof(Plugin.ZoneService))!.SetValue(plugin, zone);
                using var owner = new MagiaMiniWindow(plugin);
                var motion = (MaterialWindowMotion)typeof(MagiaMiniWindow).GetField("motion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
                var warningOwner = new AutoDutyWarningWindow(plugin, ActivationModeTests.Proxy<IChatGui>((_, _) => null),
                    ActivationModeTests.Proxy<IPluginLog>((_, _) => null));
                var warningMotion = (MaterialWindowMotion)typeof(AutoDutyWarningWindow).GetField("motion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(warningOwner)!;
                var theme = FrenRiderPresentation.Theme(FrenRiderPresentation.ReferenceAccent, settings.UiColors);
                theme.Density = compact ? MaterialDensity.Compact : MaterialDensity.Standard;
                ImDrawListPtr list = default;
                void Frame(bool warning = false)
                {
                    ImGui.NewFrame();
                    using (MaterialTheme.Push(theme, scale, MaterialStyleMode.ColorsOnly))
                    {
                        Window active = warning ? warningOwner : owner;
                        active.PreDraw();
                        ImGui.Begin(active.WindowName, active.Flags);
                        active.Draw();
                        list = ImGui.GetWindowDrawList();
                        ImGui.End();
                        (warning ? warningMotion : motion).Restore(active);
                        if (!warning) ImGui.PopStyleVar();
                    }
                    ImGui.Render();
                }
                Vector2 Caption(string caption)
                {
                    // Native text advances over spaces without emitting glyph quads.
                    var ink = caption.Replace(" ", "");
                    for (var vertex = 0; vertex + ink.Length * 4 <= list.VtxBuffer.Size; vertex++)
                    {
                        if ((list.VtxBuffer[vertex].Col >> 24) == 0) continue;
                        var matches = true;
                        for (var character = 0; character < ink.Length; character++)
                        {
                            var glyph = ImGui.FindGlyphNoFallback(ImGui.GetFont(), ink[character]);
                            if (list.VtxBuffer[vertex + character * 4].Uv != new Vector2(glyph.Handle->U0, glyph.Handle->V0)
                                || list.VtxBuffer[vertex + character * 4 + 2].Uv != new Vector2(glyph.Handle->U1, glyph.Handle->V1))
                            { matches = false; break; }
                        }
                        if (matches) return (list.VtxBuffer[vertex].Pos + list.VtxBuffer[vertex + ink.Length * 4 - 2].Pos) * .5f;
                    }
                    throw new InvalidOperationException("Actual Mini did not render " + caption);
                }
                for (var frame = 0; frame < 6; frame++) Frame();
                var run = Caption("Run");
                Assert.NotEqual(Vector2.Zero, Caption("Fren"));
                Assert.NotEqual(Vector2.Zero, Caption("BUY GREENS"));
                Assert.NotEqual(Vector2.Zero, Caption("BUY FOOD"));
                if (eureka)
                    foreach (var caption in new[] { "MAGIA", "Attack", "Defense", "Off" }) Assert.NotEqual(Vector2.Zero, Caption(caption));
                io.AddMousePosEvent(run.X, run.Y); Frame();
                io.AddMouseButtonEvent(0, true); Frame();
                io.AddMouseButtonEvent(0, false); Frame();
                Assert.True(config.Enabled);
                Assert.Same(config, manager.GetActiveConfig());
                Assert.Empty(commands);
                warningOwner.IsOpen = false;
                warningOwner.OnClose();
                Assert.True(warningOwner.IsOpen);
                for (var frame = 0; frame < 6; frame++) Frame(warning: true);
                var disable = Caption("Disable AutoDuty");
                io.AddMousePosEvent(disable.X, disable.Y); Frame(warning: true);
                io.AddMouseButtonEvent(0, true); Frame(warning: true);
                io.AddMouseButtonEvent(0, false); Frame(warning: true);
                Assert.Equal("/xldisableplugin AutoDuty", Assert.Single(commands));
                Assert.False(warningOwner.IsOpen);
                warningOwner.OnClose();
                Assert.False(warningOwner.IsOpen);
            });
        }
        finally
        {
            clientProperty.SetValue(null, previousClient);
            commandProperty.SetValue(null, previousCommands);
            ImGui.DestroyContext(context); ImGui.SetCurrentContext(previousContext);
        }
    }

    [Theory]
    [InlineData(false, 1f, true, FrenRiderUiStyle.Classic)]
    [InlineData(true, 1f, true, FrenRiderUiStyle.Classic)]
    [InlineData(false, 1.5f, true, FrenRiderUiStyle.Classic)]
    [InlineData(true, 1.5f, true, FrenRiderUiStyle.Classic)]
    [InlineData(false, 1.5f, false, FrenRiderUiStyle.Classic)]
    [InlineData(true, 1f, false, FrenRiderUiStyle.Classic)]
    [InlineData(false, 1f, false, FrenRiderUiStyle.Glassmorphism)]
    [InlineData(true, 1.5f, false, FrenRiderUiStyle.Glassmorphism)]
    [InlineData(false, 1.5f, false, FrenRiderUiStyle.Spatial)]
    [InlineData(true, 1f, false, FrenRiderUiStyle.Spatial)]
    [InlineData(false, 1f, false, FrenRiderUiStyle.Cyberpunk)]
    [InlineData(true, 1.5f, false, FrenRiderUiStyle.Cyberpunk)]
    public unsafe void MiniAndWarningFramingKeepsNativeActionsClickableWithoutChangingTheirIds(bool compact, float scale, bool lcars, FrenRiderUiStyle surface = FrenRiderUiStyle.Classic)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            using var text = new UiText("en", _ => new NoFont());
            using var textScope = text.Enter();
            var config = new Configuration { UiStyle = surface, UiColors = new() { Mode = lcars ? MaterialColorMode.Lcars : MaterialColorMode.Auto } };
            var theme = FrenRiderPresentation.Theme(config);
            using var appearance = FrenRiderPresentation.Push(config);
            theme.Density = compact ? MaterialDensity.Compact : MaterialDensity.Standard;
            var io = ImGui.GetIO();
            io.IniFilename = null; io.LogFilename = null;
            io.DisplaySize = new(1200, 1000); io.DeltaTime = 1f / 60; io.FontGlobalScale = scale;
            io.Fonts.AddFontDefault();
            byte* pixels = null; int width = 0, height = 0;
            io.Fonts.GetTexDataAsRGBA32(0, &pixels, &width, &height);
            io.Fonts.SetTexID(0, new ImTextureID((nint)1));
            var center = Vector2.Zero;
            var clicks = 0;
            void Frame()
            {
                ImGui.NewFrame();
                using (MaterialTheme.Push(theme, scale, MaterialStyleMode.ColorsOnly))
                {
                    ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(500 * scale, 400 * scale));
                    ImGui.Begin("Mini frame native action", ImGuiWindowFlags.NoSavedSettings);
                    var originalId = ImGui.GetID("Run fixture");
                    var originalPadding = ImGui.GetStyle().CellPadding;
                    var afterBody = 0;
                    UiGui.LcarsPanel("##mini-frame-fixture", () =>
                    {
                        Assert.Equal(originalId, ImGui.GetID("Run fixture"));
                        if (ImGui.Button("Run fixture")) clicks++;
                        center = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) * .5f;
                        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
                        Assert.True(ImGui.GetItemRectMax().X <= right);
                        afterBody = ImGui.GetWindowDrawList().VtxBuffer.Size;
                    });
                    Assert.Equal(originalPadding, ImGui.GetStyle().CellPadding);
                    Assert.Equal(originalId, ImGui.GetID("Run fixture"));
                    if (lcars || surface != FrenRiderUiStyle.Classic) Assert.True(ImGui.GetWindowDrawList().VtxBuffer.Size > afterBody);
                    ImGui.End();
                }
                ImGui.Render();
            }
            Frame(); Frame();
            io.AddMousePosEvent(center.X, center.Y); Frame();
            io.AddMouseButtonEvent(0, true); Frame();
            io.AddMouseButtonEvent(0, false); Frame();
            Assert.Equal(1, clicks);
        }
        finally { ImGui.DestroyContext(context); ImGui.SetCurrentContext(previous); }
    }

    [Theory]
    [InlineData(false, 1f, 580, false)]
    [InlineData(true, 1f, 580, false)]
    [InlineData(false, 1.5f, 580, false)]
    [InlineData(true, 1.5f, 580, false)]
    [InlineData(false, 1f, 280, true)]
    [InlineData(true, 1f, 280, true)]
    [InlineData(false, 1.5f, 280, true)]
    [InlineData(true, 1.5f, 280, true)]
    public unsafe void CyberpunkTabsAndRaisedActionsRetainTheirNativeBoundsSemanticsAndStyleScopes(bool compact, float scale, int logicalWidth, bool custom)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            using var text = new UiText("en", _ => new NoFont());
            using var textScope = text.Enter();
            var config = new Configuration { UiStyle = FrenRiderUiStyle.Cyberpunk, UiCompact = compact };
            if (custom) config.UiCyberpunkColors = config.UiCyberpunkColors with { BackgroundRgb = 0x141A26, ControlsRgb = 0xF1B25B, TextRgb = 0xDBF0DF };
            var theme = FrenRiderPresentation.Theme(config);
            theme.Density = compact ? MaterialDensity.Compact : MaterialDensity.Standard;
            var io = ImGui.GetIO();
            io.IniFilename = null; io.LogFilename = null;
            io.DisplaySize = new(2200, 2200); io.DeltaTime = 1f / 60; io.FontGlobalScale = scale;
            io.Fonts.AddFontDefault();
            byte* pixels = null; int width = 0, height = 0;
            io.Fonts.GetTexDataAsRGBA32(0, &pixels, &width, &height);
            io.Fonts.SetTexID(0, new ImTextureID((nint)1));
            var centers = new Dictionary<string, Vector2>();
            var clicks = 0; var radioClicks = 0; var disabledClicks = 0;
            var selected = false; var checkedValue = false;
            var semantic = new Vector4(.55f, .12f, .16f, 1);
            var status = new Vector4(.18f, .84f, .52f, 1);
            var plugin = (Plugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
            typeof(Plugin).GetProperty(nameof(Plugin.Configuration))!.SetValue(plugin, config);
            using var main = new MainWindow(plugin);
            var panel = typeof(MainWindow).GetMethod("Panel", BindingFlags.Instance | BindingFlags.NonPublic)!;

            void CaptionWithin(ImDrawListPtr list, int first, string caption, Vector2 min, Vector2 max, uint? color = null)
            {
                var ink = caption.Replace(" ", "");
                for (var vertex = first; vertex + ink.Length * 4 <= list.VtxBuffer.Size; vertex++)
                {
                    var matches = true;
                    for (var character = 0; character < ink.Length; character++)
                    {
                        var glyph = ImGui.FindGlyphNoFallback(ImGui.GetFont(), ink[character]);
                        if (list.VtxBuffer[vertex + character * 4].Uv != new Vector2(glyph.Handle->U0, glyph.Handle->V0)
                            || list.VtxBuffer[vertex + character * 4 + 2].Uv != new Vector2(glyph.Handle->U1, glyph.Handle->V1))
                        { matches = false; break; }
                    }
                    if (!matches) continue;
                    for (var offset = 0; offset < ink.Length * 4; offset++)
                    {
                        var glyphVertex = list.VtxBuffer[vertex + offset];
                        Assert.InRange(glyphVertex.Pos.X, min.X, max.X);
                        Assert.InRange(glyphVertex.Pos.Y, min.Y, max.Y);
                        Assert.True((glyphVertex.Col >> 24) > 0);
                        if (color.HasValue) Assert.Equal(color.Value, glyphVertex.Col);
                    }
                    return;
                }
                Assert.Fail("The actual mesh did not retain the complete caption: " + caption);
            }
            bool SolidTriangle(ImDrawListPtr list, int firstIndex, uint color, float minimumArea, int firstVertex = 0)
            {
                for (var index = firstIndex; index + 2 < list.IdxBuffer.Size; index += 3)
                {
                    if (list.IdxBuffer[index] < firstVertex || list.IdxBuffer[index + 1] < firstVertex || list.IdxBuffer[index + 2] < firstVertex) continue;
                    var a = list.VtxBuffer[list.IdxBuffer[index]];
                    var b = list.VtxBuffer[list.IdxBuffer[index + 1]];
                    var c = list.VtxBuffer[list.IdxBuffer[index + 2]];
                    if (a.Col != color || b.Col != color || c.Col != color) continue;
                    var ab = b.Pos - a.Pos; var ac = c.Pos - a.Pos;
                    if (MathF.Abs(ab.X * ac.Y - ab.Y * ac.X) * .5f >= minimumArea * scale * scale) return true;
                }
                return false;
            }
            void Remember(string name) => centers[name] = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) * .5f;
            void Frame()
            {
                ImGui.NewFrame();
                using (FrenRiderPresentation.Push(config))
                using (MaterialTheme.Push(theme, scale, MaterialStyleMode.ColorsOnly))
                {
                    ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(logicalWidth * scale, 1100 * scale));
                    ImGui.Begin("Cyberpunk native appearance", ImGuiWindowFlags.NoSavedSettings);
                    var list = ImGui.GetWindowDrawList();
                    var style = ImGui.GetStyle();
                    var originalColors = Enumerable.Range(0, (int)ImGuiCol.Count).Select(index => style.Colors[index]).ToArray();
                    var originalMetrics = (style.FramePadding, style.CellPadding, style.ItemInnerSpacing, style.ItemSpacing, style.FrameRounding, style.FrameBorderSize);
                    var originalId = ImGui.GetID("Run fixture");
                    UiGui.LcarsPanel("##cyberpunk-regression", () =>
                    {
                        Assert.Equal(originalId, ImGui.GetID("Run fixture"));
                        var plate = ImGui.GetCursorScreenPos();
                        var plateSize = new Vector2(ImGui.GetContentRegionAvail().X, 36 * scale);
                        var plateIndex = list.IdxBuffer.Size;
                        FrenRiderPresentation.DrawPanel(plate, plate + plateSize);
                        Assert.True(SolidTriangle(list, plateIndex, MaterialCanvas.Color(theme.Colors.SurfaceContainerLow), 64), "Panel lost its flat opaque plate.");
                        ImGui.Dummy(plateSize);
                        foreach (var (title, accent) in new[] { ("Activation mode", 0), ("Operator", 1), ("Duty / ADS / Exit", 2), ("Party", 0), ("Automation", 1) })
                        {
                            var first = list.VtxBuffer.Size;
                            panel.Invoke(main, [title, MaterialIcon.Table, (Action)(() =>
                            {
                                Assert.Equal(originalId, ImGui.GetID("Run fixture"));
                                var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
                                var available = ImGui.GetContentRegionAvail().X;
                                Assert.InRange(max.X - min.X, available - .1f, available + .1f);
                                var tabColor = accent == 0 ? theme.Colors.Primary : Vector4.Lerp(theme.Colors.OnSurface, theme.Colors.Primary, .10f);
                                CaptionWithin(list, first, title.ToUpperInvariant(), min, max, MaterialCanvas.Color(FrenRiderPresentation.ReadableInk(tabColor)));
                                // A nested native table has its own index channel; restrict this scan to this Panel's vertices.
                                Assert.True(SolidTriangle(list, 0, MaterialCanvas.Color(tabColor), 64, first), "Main Panel bypassed its solid primary or pale heading tab.");
                                if (accent != 0) Assert.True(SolidTriangle(list, 0, MaterialCanvas.Color(theme.Colors.Secondary), 12, first), "Main Panel lost its secondary heading wedge.");
                                UiHelpers.ReadOnlyField("Panel fixture", status, localize: false);
                            }), title != "Activation mode"]);
                            Assert.Equal(originalId, ImGui.GetID("Run fixture"));
                        }
                        const string wrappedCaption = "AUTOMATION AND COMPANION PURCHASING STATUS";
                        var wrappedFirst = list.VtxBuffer.Size;
                        var wrappedOrigin = ImGui.GetCursorScreenPos();
                        var wrappedWidth = ImGui.GetContentRegionAvail().X;
                        FrenRiderPresentation.LcarsHeading(wrappedCaption, 1);
                        Assert.Equal(wrappedOrigin, ImGui.GetItemRectMin());
                        Assert.InRange(ImGui.GetItemRectMax().X - wrappedOrigin.X, wrappedWidth - .1f, wrappedWidth + .1f);
                        CaptionWithin(list, wrappedFirst, wrappedCaption, wrappedOrigin, ImGui.GetItemRectMax());
                        if (logicalWidth == 280) Assert.True(ImGui.GetItemRectMax().Y - wrappedOrigin.Y > ImGui.GetTextLineHeight() * 2);
                        var buttonFirst = list.VtxBuffer.Size;
                        using (var semanticStyle = new MaterialStyleScope())
                        {
                            semanticStyle.Color(ImGuiCol.Button, semantic);
                            semanticStyle.Color(ImGuiCol.ButtonHovered, semantic);
                            semanticStyle.Color(ImGuiCol.ButtonActive, semantic);
                            if (UiGui.Button("Run fixture", new Vector2(-1, 0))) clicks++;
                            Remember("Run");
                            var bottom = ImGui.GetItemRectMax().Y;
                            var edges = Enumerable.Range(buttonFirst, list.VtxBuffer.Size - buttonFirst)
                                .Where(index => list.VtxBuffer[index].Col == MaterialCanvas.Color(theme.Colors.Primary) && list.VtxBuffer[index].Pos.Y >= bottom - 3 * scale)
                                .Select(index => list.VtxBuffer[index].Pos.X).ToArray();
                            Assert.NotEmpty(edges);
                            Assert.True(edges.Max() - edges.Min() > (ImGui.GetItemRectMax().X - ImGui.GetItemRectMin().X) * .5f);
                            Assert.Contains(Enumerable.Range(buttonFirst, list.VtxBuffer.Size - buttonFirst), index => list.VtxBuffer[index].Col == MaterialCanvas.Color(semantic));
                            Assert.Equal(semantic, style.Colors[(int)ImGuiCol.Button]);
                        }
                        if (UiGui.RadioButton("Always on fixture", selected)) { selected = true; radioClicks++; }
                        Remember("Radio");
                        UiGui.Checkbox("Check fixture", ref checkedValue);
                        Remember("Check");
                        ImGui.BeginDisabled();
                        if (UiGui.Button("Disabled fixture")) disabledClicks++;
                        Remember("Disabled");
                        ImGui.EndDisabled();
                        var readOnlyFirst = list.VtxBuffer.Size;
                        UiHelpers.ReadOnlyField("Ready fixture", status, localize: false);
                        Remember("ReadOnly");
                        CaptionWithin(list, readOnlyFirst, "Ready fixture", ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), MaterialCanvas.Color(status));
                    });
                    Assert.Equal(originalId, ImGui.GetID("Run fixture"));
                    Assert.Equal(originalMetrics, (style.FramePadding, style.CellPadding, style.ItemInnerSpacing, style.ItemSpacing, style.FrameRounding, style.FrameBorderSize));
                    Assert.Equal(originalColors, Enumerable.Range(0, (int)ImGuiCol.Count).Select(index => style.Colors[index]).ToArray());
                    ImGui.End();
                }
                ImGui.Render();
            }
            void Click(string name)
            {
                var center = centers[name];
                io.AddMousePosEvent(center.X, center.Y); Frame();
                io.AddMouseButtonEvent(0, true); Frame();
                io.AddMouseButtonEvent(0, false); Frame();
            }
            Frame(); Frame();
            Click("Run"); Click("Radio"); Click("Check"); Click("Disabled"); Click("ReadOnly");
            Assert.Equal(1, clicks); Assert.Equal(1, radioClicks); Assert.Equal(0, disabledClicks);
            Assert.True(selected); Assert.True(checkedValue);
            Assert.False(ImGui.IsAnyItemActive());
        }
        finally { ImGui.DestroyContext(context); ImGui.SetCurrentContext(previous); }
    }

    [Fact]
    public void ActualTitlebarRegistrationBindsKrangleAndRestoresConventionalIconsOutsideLcars()
    {
        var interfaceProperty = typeof(Plugin).GetProperty("PluginInterface", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousInterface = interfaceProperty.GetValue(null);
        var saves = new List<Configuration>();
        try
        {
            interfaceProperty.SetValue(null, ActivationModeTests.Proxy<IDalamudPluginInterface>((method, args) =>
            {
                if (method.Name == "SavePluginConfig") saves.Add(JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(args![0]))!);
                return null;
            }));
            var config = new Configuration { UiColors = new() { Mode = MaterialColorMode.Lcars }, LastAccountId = "fixture-account" };
            var plugin = (Plugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
            typeof(Plugin).GetProperty(nameof(Plugin.Configuration))!.SetValue(plugin, config);
            var windows = new WindowSystem("LCARS privacy fixture");
            typeof(Plugin).GetField("WindowSystem")!.SetValue(plugin, windows);
            var adapters = new Dictionary<Window, MaterialAppearanceTitleBar>();
            typeof(Plugin).GetField("appearanceTitleBars", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(plugin, adapters);
            var owner = new ProbeWindow();
            var original = new TitleBarButton { Icon = FontAwesomeIcon.Cog, Priority = 10 };
            owner.TitleBarButtons.Add(original);
            windows.AddWindow(owner);
            var register = typeof(Plugin).GetMethod("PrepareAppearanceTitleBars", BindingFlags.Instance | BindingFlags.NonPublic)!;
            register.Invoke(plugin, null); register.Invoke(plugin, null);
            Assert.Equal(6, owner.TitleBarButtons.Count);
            Assert.Same(original, owner.TitleBarButtons[0]);
            adapters[owner].SetLcarsIcons(true);
            Assert.Equal(FontAwesomeIcon.Globe, owner.TitleBarButtons[1].Icon);
            Assert.Equal(FontAwesomeIcon.Desktop, owner.TitleBarButtons[2].Icon);
            Assert.Equal(FontAwesomeIcon.Tint, owner.TitleBarButtons[3].Icon);
            var privacy = Assert.Single(owner.TitleBarButtons, button => button.Icon == FontAwesomeIcon.UserSecret);
            privacy.Click!(ImGuiMouseButton.Right);
            Assert.False(config.KrangleEnabled); Assert.Empty(saves);
            privacy.Click!(ImGuiMouseButton.Left);
            Assert.True(config.KrangleEnabled); Assert.True(Assert.Single(saves).KrangleEnabled);
            Assert.Equal("fixture-account", saves[0].LastAccountId);
            privacy.Click!(ImGuiMouseButton.Left);
            Assert.False(config.KrangleEnabled); Assert.False(saves[^1].KrangleEnabled);
            adapters[owner].SetLcarsIcons(false);
            Assert.Equal(FontAwesomeIcon.Language, owner.TitleBarButtons[1].Icon);
            Assert.Equal(FontAwesomeIcon.CompressAlt, owner.TitleBarButtons[2].Icon);
            Assert.Equal(FontAwesomeIcon.Adjust, owner.TitleBarButtons[3].Icon);
            Assert.Same(privacy, owner.TitleBarButtons[^1]);
        }
        finally { interfaceProperty.SetValue(null, previousInterface); }
    }

    [Fact]
    public void SoundTitlebarAndSettingsUseOneGlobalDefaultMutedPreferenceAndRetainOtherActions()
    {
        var interfaceProperty = typeof(Plugin).GetProperty("PluginInterface", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousInterface = interfaceProperty.GetValue(null);
        var saves = new List<Configuration>();
        try
        {
            interfaceProperty.SetValue(null, ActivationModeTests.Proxy<IDalamudPluginInterface>((method, args) =>
            {
                if (method.Name == "SavePluginConfig") saves.Add(JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(args![0]))!);
                return null;
            }));
            var config = new Configuration { UiColors = new() { BackgroundRgb = 0x123456 }, LastAccountId = "fixture-account" };
            var plugin = (Plugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
            typeof(Plugin).GetProperty(nameof(Plugin.Configuration))!.SetValue(plugin, config);
            var windows = new WindowSystem("LCARS sound fixture");
            typeof(Plugin).GetField("WindowSystem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(plugin, windows);
            typeof(Plugin).GetField("lcarsSoundButtons", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(plugin, new Dictionary<Window, TitleBarButton>());
            var owner = new ProbeWindow();
            owner.TitleBarButtons.Add(new() { Icon = FontAwesomeIcon.Cog, Priority = 10 });
            owner.TitleBarButtons.Add(new() { Icon = FontAwesomeIcon.PowerOff, Priority = 5 });
            var original = owner.TitleBarButtons.ToArray();
            windows.AddWindow(owner);
            var refresh = typeof(Plugin).GetMethod("RefreshLcarsSoundButtons", BindingFlags.Instance | BindingFlags.NonPublic)!;
            refresh.Invoke(plugin, null);
            Assert.Equal(original, owner.TitleBarButtons);
            config.UiColors = config.UiColors with { Mode = MaterialColorMode.Lcars };
            refresh.Invoke(plugin, null);
            Assert.Equal(3, owner.TitleBarButtons.Count);
            var sound = owner.TitleBarButtons[^1];
            Assert.Equal(FontAwesomeIcon.VolumeMute, sound.Icon);
            Assert.True(sound.Priority < original.Min(button => button.Priority));
            sound.Click!(ImGuiMouseButton.Right);
            Assert.Empty(saves);
            sound.Click!(ImGuiMouseButton.Left);
            Assert.False(config.UiLcarsSoundsMuted);
            Assert.False(Assert.Single(saves).UiLcarsSoundsMuted);
            refresh.Invoke(plugin, null);
            Assert.Equal(FontAwesomeIcon.VolumeUp, sound.Icon);
            // The actual Settings action writes exactly the same global preference.
            plugin.SetLcarsSounds(false);
            Assert.True(config.UiLcarsSoundsMuted);
            Assert.True(saves[^1].UiLcarsSoundsMuted);
            Assert.Equal(2, saves.Count);
            Assert.Equal(0x123456u, saves[^1].UiColors.BackgroundRgb);
            Assert.Equal("fixture-account", saves[^1].LastAccountId);
            Assert.Null(typeof(Plugin).GetField("uiSoundPlayer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plugin));
            config.UiColors = config.UiColors with { Mode = MaterialColorMode.Picker };
            refresh.Invoke(plugin, null);
            Assert.Equal(original, owner.TitleBarButtons);
            sound.Click!(ImGuiMouseButton.Left);
            Assert.Equal(2, saves.Count);
        }
        finally { interfaceProperty.SetValue(null, previousInterface); }
    }

    private sealed class ProbeWindow() : Window("LCARS sound###lcars-sound")
    {
        public override void Draw() { }
    }
    private sealed class NoFont : IDisposable { public void Dispose() { } }
}
