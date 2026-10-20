using System.Numerics;
using System.Reflection;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using FrenRider.Models;
using FrenRider.Windows;
using Xunit;

namespace FrenRider.Tests;

[Collection("Activation mode static services")]
public sealed class ActivationAndLimitBreakUiTests
{
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
