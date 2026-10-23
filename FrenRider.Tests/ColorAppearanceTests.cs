using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;
using FrenRider.Windows;
using Newtonsoft.Json;
using Xunit;

namespace FrenRider.Tests;

[Collection("Activation mode static services")]
public sealed unsafe class ColorAppearanceTests
{
    [Fact]
    public void LegacyAppearanceRemainsAutomaticAndManualChoicesRoundTripThroughTheExistingGlobalConfiguration()
    {
        var legacy = JsonConvert.DeserializeObject<Configuration>(
            """{"Version":2,"UiAccentRgb":1193046,"UiLanguage":"ja","DtrBarEnabled":false,"LastAccountId":"fixture-account","UnrelatedFixture":17}""")!;
        Assert.Equal(MaterialColorMode.Auto, legacy.UiColors.Mode);
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
            "Turn on UI sounds to preview.", "Audio output is unavailable.", "UI sounds muted", "UI sounds on"];
        foreach (var language in UiText.Languages)
        {
            using var text = new UiText(language.Code, _ => new NoFont());
            foreach (var key in keys) Assert.False(string.IsNullOrWhiteSpace(text.Resources.GetString(key, false)), language.Code + ": " + key);
        }
    }

    [Theory]
    [InlineData(false, 1f)]
    [InlineData(true, 1.5f)]
    public void RealTitlebarCallbackSavesTheGlobalModeAndKeepsManualChoicesWhenReturningToAuto(bool compact, float scale)
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
            io.DisplaySize = new(1200 * scale, 900 * scale); io.DeltaTime = 1f / 60; io.FontGlobalScale = scale;
            io.Fonts.AddFontDefault();
            byte* pixels = null; int width = 0, height = 0;
            io.Fonts.GetTexDataAsRGBA32(0, &pixels, &width, &height);
            io.Fonts.SetTexID(0, new ImTextureID((nint)1));
            ImDrawListPtr list = default;
            void Frame()
            {
                ImGui.NewFrame();
                apply.Invoke(plugin, null);
                using var theme = MaterialTheme.Push((MaterialTheme)themeField.GetValue(plugin)!, scale, MaterialStyleMode.ColorsOnly);
                ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(600 * scale, 800 * scale));
                ImGui.Begin("FrenRider Colour callback", ImGuiWindowFlags.NoSavedSettings);
                draw.Invoke(plugin, null);
                list = ImGui.GetWindowDrawList();
                var firstGlyph = list.VtxBuffer.Size;
                UiGui.TextUnformatted("ColourProbe");
                glyphColors.Clear();
                for (var vertex = firstGlyph; vertex < list.VtxBuffer.Size; vertex++) glyphColors.Add(list.VtxBuffer[vertex].Col);
                ImGui.End(); ImGui.Render();
            }
            void Click(string caption)
            {
                var point = Center(caption);
                io.AddMousePosEvent(point.X, point.Y); Frame();
                io.AddMouseButtonEvent(0, true); Frame();
                io.AddMouseButtonEvent(0, false); Frame();
            }
            Vector2 Center(string caption)
            {
                for (var index = 0; index + caption.Length * 4 <= list.VtxBuffer.Size; index++)
                {
                    var matched = true;
                    for (var character = 0; character < caption.Length; character++)
                    {
                        var glyph = ImGui.FindGlyphNoFallback(ImGui.GetFont(), caption[character]);
                        if (list.VtxBuffer[index + character * 4].Uv != new Vector2(glyph.Handle->U0, glyph.Handle->V0)
                            || list.VtxBuffer[index + character * 4 + 2].Uv != new Vector2(glyph.Handle->U1, glyph.Handle->V1))
                        { matched = false; break; }
                    }
                    if (matched) return (list.VtxBuffer[index].Pos + list.VtxBuffer[index + caption.Length * 4 - 2].Pos) / 2;
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
            Assert.True(((MaterialTheme)themeField.GetValue(plugin)!).IsLcars);
            Assert.True(restored.UiLcarsSoundsMuted);
            Click("Picker"); Frame();
            Assert.Equal(manual, config.UiColors);
        }
        finally
        {
            interfaceProperty.SetValue(null, previousInterface);
            ImGui.DestroyContext(context); ImGui.SetCurrentContext(previousContext);
        }
    }

    private sealed class NoFont : IDisposable { public void Dispose() { } }
}
