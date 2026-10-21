using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FrenRider.Models;
using FrenRider.Services;
using FrenRider.Windows;
using Xunit;

namespace FrenRider.Tests;

[Collection("Activation mode static services")]
public sealed class ActivationAndLimitBreakUiTests
{
    [Fact]
    public unsafe void TitleScreenRoleEditsSaveToSelectedAccountWithoutChangingRuntimeOwner()
    {
        using var text = new UiText("en", _ => new NoFont());
        using var textScope = text.Enter();
        ActivationModeTests.WithManager((manager, local) =>
        {
            const string key = "Example Member@TestWorld";
            var other = new AccountConfig { AccountId = "other", AccountAlias = "Other" };
            other.Characters[key] = new();
            var remote = new RemoteProfileRow { DisplayLabel = "Remote fixture" };
            other.RemoteProfiles.Add(remote);
            ((Dictionary<string, AccountConfig>)manager.Accounts).Add(other.AccountId, other);
            var identity = new DadProfileIdentity("owner", "island", "opaque", "proposal");
            Assert.True(manager.TryInstallTemporaryProfile(identity, new CharacterConfig { Enabled = true }));
            var overlay = manager.GetActiveConfig();
            var plugin = (Plugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
            typeof(Plugin).GetField("<ConfigManager>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(plugin, manager);
            var window = new ConfigWindow(plugin);
            var type = typeof(ConfigWindow);
            void Select(string account, string character, string remoteId = "")
            {
                type.GetField("editingAccountId", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, account);
                type.GetField("editingCharacterKey", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, character);
                type.GetField("editingRemoteRowId", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, remoteId);
            }
            CharacterConfig Resolve() => (CharacterConfig)type.GetMethod("GetEditingConfig", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
            void Save() => type.GetMethod("SaveEditingAccount", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            Select(other.AccountId, key);
            Assert.Same(other.Characters[key], Resolve());
            Assert.Same(overlay, manager.GetActiveConfig());
            Assert.False(overlay.LimitBreak.Melee.Enabled);
            manager.ClearActiveCharacter(); // Existing logout lifecycle.
            manager.CurrentAccountId = ""; // Also cover a reload with no active account.
            type.GetMethod("SyncEditingSelectionWithActiveCharacter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            Assert.Same(other.Characters[key], Resolve());

            var previous = ImGui.GetCurrentContext();
            var context = ImGui.CreateContext();
            try
            {
                var io = ImGui.GetIO();
                io.IniFilename = null; io.LogFilename = null;
                io.DisplaySize = new(1200, 1000); io.DeltaTime = 1f / 60;
                io.Fonts.AddFontDefault();
                byte* pixels = null; int width = 0, height = 0;
                io.Fonts.GetTexDataAsRGBA32(0, &pixels, &width, &height);
                io.Fonts.SetTexID(0, new ImTextureID((nint)1));
                using var theme = MaterialTheme.Push(FrenRiderPresentation.Theme(FrenRiderPresentation.ReferenceAccent), 1, MaterialStyleMode.ColorsOnly);
                var before = Vector2.Zero;
                var draw = type.GetMethod("DamageControls", BindingFlags.Static | BindingFlags.NonPublic)!;
                foreach (var role in new[] { LimitBreakRole.Melee, LimitBreakRole.PhysicalRanged, LimitBreakRole.MagicalRanged })
                {
                    for (var frame = 0; frame < 5; frame++)
                    {
                        if (frame == 2) { io.AddMousePosEvent(before.X + 8, before.Y + 8); io.AddMouseButtonEvent(0, true); }
                        if (frame == 3) io.AddMouseButtonEvent(0, false);
                        ImGui.NewFrame();
                        ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(500, 800));
                        ImGui.Begin("Saved profile LB", ImGuiWindowFlags.NoSavedSettings);
                        before = ImGui.GetCursorScreenPos();
                        draw.Invoke(null, [Resolve().LimitBreak.Damage(role)!, role, (Action)Save, (Action<string>)(_ => { })]);
                        ImGui.End(); ImGui.Render();
                    }
                    Assert.True(Resolve().LimitBreak.Damage(role)!.Enabled);
                }
            }
            finally { ImGui.DestroyContext(context); ImGui.SetCurrentContext(previous); }
            Assert.False(Resolve().LimitBreak.Tank.Enabled);
            Assert.False(Resolve().LimitBreak.Healer.Enabled);
            Select(other.AccountId, "");
            Resolve().LimitBreak.Tank.Enabled = true;
            Save();
            Assert.Equal(1, manager.ApplyDefaultSettingToAllCharacters("Tank Limit Break", accountId: other.AccountId));
            Select(other.AccountId, key, remote.RowId);
            Assert.Same(remote.Config, Resolve());
            Resolve().LimitBreak.Healer.Enabled = true;
            Save();

            var directory = (string)typeof(ConfigManager).GetField("configDir", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
            var reloaded = new ConfigManager(ActivationModeTests.Proxy<IDalamudPluginInterface>((m, _) => m.Name == "GetPluginConfigDirectory" ? directory : null),
                ActivationModeTests.Proxy<IPluginLog>((_, _) => null));
            var saved = reloaded.Accounts[other.AccountId];
            Assert.True(saved.Characters[key].LimitBreak.Melee.Enabled);
            Assert.True(saved.Characters[key].LimitBreak.PhysicalRanged.Enabled);
            Assert.True(saved.Characters[key].LimitBreak.MagicalRanged.Enabled);
            Assert.True(saved.DefaultConfig.LimitBreak.Tank.Enabled);
            Assert.True(saved.Characters[key].LimitBreak.Tank.Enabled);
            Assert.False(saved.Characters[key].LimitBreak.Healer.Enabled);
            Assert.True(saved.RemoteProfiles.Single().Config.LimitBreak.Healer.Enabled);
            Assert.False(local.LimitBreak.Melee.Enabled);
            Assert.False(overlay.LimitBreak.Melee.Enabled);
            Assert.Empty(reloaded.CurrentAccountId);
            Assert.False(reloaded.GetActiveConfig().Enabled);
            reloaded.EnsureAccountSelected(other.AccountId, key);
            reloaded.EnsureCharacterExists("Example Member", "TestWorld");
            Assert.True(reloaded.GetActiveConfig().LimitBreak.Melee.Enabled);
            reloaded.ClearActiveCharacter();
            Assert.True(reloaded.GetCurrentCharacterConfig(key, other.AccountId).LimitBreak.Melee.Enabled);
        });
    }

    [Theory]
    [InlineData(false, "en", 1)]
    [InlineData(true, "en", 1)]
    [InlineData(true, "de", 1.5f)]
    public unsafe void ActualRadioAndRoleControlsRenderAndSaveNativeEdits(bool compact, string language, float scale)
    {
        var previous = ImGui.GetCurrentContext();
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.DisplaySize = new(1920, 1400);
            io.DeltaTime = 1f / 60;
            io.Fonts.AddFontDefault();
            byte* pixels = null;
            int width = 0, height = 0;
            io.Fonts.GetTexDataAsRGBA32(0, &pixels, &width, &height);
            io.Fonts.SetTexID(0, new ImTextureID((nint)1));
            var theme = FrenRiderPresentation.Theme(FrenRiderPresentation.ReferenceAccent);
            theme.Density = compact ? MaterialDensity.Compact : MaterialDensity.Standard;
            using var themeScope = MaterialTheme.Push(theme, scale, MaterialStyleMode.ColorsOnly);
            using var text = new UiText(language, _ => new NoFont());
            using var textScope = text.Enter();
            var config = new CharacterConfig { Enabled = true, ActivationMode = FrenRiderActivationMode.DutyOnly };
            var before = Vector2.Zero;
            var changes = 0;
            for (var frame = 0; frame < 5; frame++)
            {
                if (frame == 2) { io.AddMousePosEvent(before.X + 8, before.Y + 8); io.AddMouseButtonEvent(0, true); }
                if (frame == 3) io.AddMouseButtonEvent(0, false);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new(20, 20));
                ImGui.SetNextWindowSize(new(500 * scale, 400 * scale));
                ImGui.Begin("Activation controls", ImGuiWindowFlags.NoSavedSettings);
                before = ImGui.GetCursorScreenPos();
                if (UiGui.ActivationModeControls(config)) changes++;
                ImGui.End();
                ImGui.Render();
            }
            Assert.Equal(FrenRiderActivationMode.AlwaysOn, config.ActivationMode);
            Assert.True(config.Enabled);
            Assert.Equal(1, changes);

            foreach (var role in Enum.GetValues<LimitBreakRole>().Where(r => r != LimitBreakRole.None))
            {
                for (var frame = 0; frame < 2; frame++)
                {
                    ImGui.NewFrame();
                    ImGui.SetNextWindowPos(new(20, 20));
                    ImGui.SetNextWindowSize(new(compact ? 500 * scale : 1100 * scale, 1100));
                    ImGui.Begin("Limit Break controls", ImGuiWindowFlags.NoSavedSettings);
                    ConfigWindow.DrawLimitBreakSettings(config.LimitBreak, role, _ => { },
                        () => Assert.Fail("Unexpected edit"), _ => { });
                    ImGui.End();
                    ImGui.Render();
                    Assert.True(ImGui.GetDrawData().CmdListsCount > 0);
                }
            }

            // Exercise the actual damage editor's first checkbox through native mouse input.
            var drawDamage = typeof(ConfigWindow).GetMethod("DamageControls", BindingFlags.NonPublic | BindingFlags.Static)!;
            var saves = 0;
            for (var frame = 0; frame < 5; frame++)
            {
                if (frame == 2) { io.AddMousePosEvent(before.X + 8, before.Y + 8); io.AddMouseButtonEvent(0, true); }
                if (frame == 3) io.AddMouseButtonEvent(0, false);
                ImGui.NewFrame();
                ImGui.SetNextWindowPos(new(20, 20));
                ImGui.SetNextWindowSize(new(500 * scale, 800));
                ImGui.Begin("Edit damage policy", ImGuiWindowFlags.NoSavedSettings);
                before = ImGui.GetCursorScreenPos();
                drawDamage.Invoke(null, [config.LimitBreak.Melee, LimitBreakRole.Melee, (Action)(() => saves++), (Action<string>)(_ => { })]);
                ImGui.End();
                ImGui.Render();
            }
            Assert.True(config.LimitBreak.Melee.Enabled);
            Assert.False(config.LimitBreak.PhysicalRanged.Enabled);
            Assert.False(config.LimitBreak.Tank.Enabled);
            Assert.Equal(1, saves);
        }
        finally
        {
            ImGui.DestroyContext(context);
            ImGui.SetCurrentContext(previous);
        }
    }

    private sealed class NoFont : IDisposable { public void Dispose() { } }
}
