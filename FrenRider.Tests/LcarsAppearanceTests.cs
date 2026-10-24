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
    [InlineData(false, 1f, true)]
    [InlineData(true, 1f, true)]
    [InlineData(false, 1.5f, true)]
    [InlineData(true, 1.5f, true)]
    [InlineData(false, 1.5f, false)]
    [InlineData(true, 1f, false)]
    public unsafe void MiniAndWarningFramingKeepsNativeActionsClickableWithoutChangingTheirIds(bool compact, float scale, bool lcars)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            using var text = new UiText("en", _ => new NoFont());
            using var textScope = text.Enter();
            var theme = FrenRiderPresentation.Theme(FrenRiderPresentation.ReferenceAccent,
                new() { Mode = lcars ? MaterialColorMode.Lcars : MaterialColorMode.Auto });
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
                    if (lcars) Assert.True(ImGui.GetWindowDrawList().VtxBuffer.Size > afterBody);
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
