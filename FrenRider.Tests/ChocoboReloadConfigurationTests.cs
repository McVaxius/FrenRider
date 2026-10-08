using Newtonsoft.Json;

namespace FrenRider.Tests;

public sealed class ChocoboReloadConfigurationTests
{
    [Fact]
    public void ReloadSelectionDefaultsOffForNewAndLegacyConfiguration()
    {
        Assert.False(new Configuration().ChocoboProbeAfterReload);
        var legacy = JsonConvert.DeserializeObject<Configuration>(
            """{"Version":2,"UiLanguage":"ja","DontMoveWhileCasting":false,"VideoMuteAudio":false,"DtrBarEnabled":false}""")!;

        Assert.False(legacy.ChocoboProbeAfterReload);
        Assert.Equal(2, legacy.Version);
        Assert.Equal("ja", legacy.UiLanguage);
        Assert.False(legacy.DontMoveWhileCasting);
        Assert.False(legacy.VideoMuteAudio);
        Assert.False(legacy.DtrBarEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReloadSelectionRoundTripPreservesExistingConfiguration(bool selected)
    {
        var configuration = new Configuration
        {
            ChocoboProbeAfterReload = selected,
            UiLanguage = "ja",
            UiAccentRgb = 0x123456,
            UiCompact = true,
            DontMoveWhileCasting = false,
            DtrBarEnabled = false,
            VideoMuteAudio = false,
            VideoWindowWidth = 640,
        };
        var json = JsonConvert.SerializeObject(configuration);
        Assert.Contains($"\"{nameof(Configuration.ChocoboProbeAfterReload)}\":", json);
        var restored = JsonConvert.DeserializeObject<Configuration>(json)!;

        Assert.Equal(selected, restored.ChocoboProbeAfterReload);
        Assert.Equal(configuration.Version, restored.Version);
        Assert.Equal(configuration.UiLanguage, restored.UiLanguage);
        Assert.Equal(configuration.UiAccentRgb, restored.UiAccentRgb);
        Assert.Equal(configuration.UiCompact, restored.UiCompact);
        Assert.Equal(configuration.DontMoveWhileCasting, restored.DontMoveWhileCasting);
        Assert.Equal(configuration.DtrBarEnabled, restored.DtrBarEnabled);
        Assert.Equal(configuration.VideoMuteAudio, restored.VideoMuteAudio);
        Assert.Equal(configuration.VideoWindowWidth, restored.VideoWindowWidth);
    }
}
