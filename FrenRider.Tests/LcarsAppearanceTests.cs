using System.Reflection;
using System.Runtime.CompilerServices;
using AethertekUI;
using AethertekUI.Dalamud;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Newtonsoft.Json;
using Xunit;

namespace FrenRider.Tests;

[Collection("Activation mode static services")]
public sealed class LcarsAppearanceTests
{
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
}
