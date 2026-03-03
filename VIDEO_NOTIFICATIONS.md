# Video Notifications Feature

## Overview
Fren Rider now supports video notifications that play when the plugin is enabled or disabled. This feature uses VLC media player to display borderless video notifications.

## Integration Steps for Developers

### 1. Add Configuration Settings
Add these properties to `Configuration.cs`:
```csharp
// --- Video Notifications ---
public bool VideoNotificationsEnabled { get; set; } = false;
public int VideoWindowX { get; set; } = 100;
public int VideoWindowY { get; set; } = 100;
public int VideoWindowWidth { get; set; } = 640;
public int VideoWindowHeight { get; set; } = 480;
public bool VideoMuteAudio { get; set; } = true;
public string EmbeddedVideosFolder { get; set; } = "videos";
```

### 2. Create VideoPlaybackService
Create `Services/VideoPlaybackService.cs` based on the ExperimentalDumpsterResearch implementation:
- VLC borderless playback using `-I dummy --no-video-deco --no-embedded-video --play-and-exit`
- Embedded video detection in plugin directory
- Process management and cleanup

### 3. Update Project File (.csproj)
Add videos to build output and plugin packaging:
```xml
<ItemGroup>
  <None Include="images\**" CopyToOutputDirectory="PreserveNewest" />
  <Content Include="videos\**">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>

<Target Name="CopyPluginArtifacts" AfterTargets="Build">
  <!-- ... existing config ... -->
  <ItemGroup>
    <PluginFiles Include="$(TargetPath)" />
    <PluginFiles Include="$(ProjectDir)$(AssemblyName).json" />
    <PluginFiles Include="videos\**" />
  </ItemGroup>
  <!-- ... rest of target ... -->
</Target>
```

### 4. Add UI Controls
Add checkbox to ConfigWindow Misc tab:
```csharp
var videoNotificationsEnabled = configuration.VideoNotificationsEnabled;
if (ImGui.Checkbox("Video Notifications", ref videoNotificationsEnabled))
{
    configuration.VideoNotificationsEnabled = videoNotificationsEnabled;
    configuration.Save();
}
ImGui.SameLine();
HelpMarker("Play videos when Fren Rider is enabled/disabled.\nRequires VLC media player to be installed.\nVideos are embedded with the plugin distribution.");
```

### 5. Integrate with Plugin State Changes
Add video playback logic to `Plugin.cs` OnFrameworkUpdate:
```csharp
// Check for plugin enable/disable state changes for video notifications
var config = ConfigManager.GetActiveConfig();
if (config != null)
{
    if (Configuration.VideoNotificationsEnabled && config.Enabled != wasPluginEnabled)
    {
        if (config.Enabled)
        {
            // Plugin was just enabled - play enable video
            var enableVideoPath = VideoPlaybackService.GetEmbeddedVideoPath("1.mp4");
            if (!string.IsNullOrEmpty(enableVideoPath))
            {
                _ = VideoPlaybackService.PlayVideo(enableVideoPath);
            }
        }
        else
        {
            // Plugin was just disabled - play disable video
            var disableVideoPath = VideoPlaybackService.GetEmbeddedVideoPath("2.mp4");
            if (!string.IsNullOrEmpty(disableVideoPath))
            {
                _ = VideoPlaybackService.PlayVideo(disableVideoPath);
            }
        }
        wasPluginEnabled = config.Enabled;
    }
}
```

### 6. Add Videos to Project
Create `videos/` folder in source and add video files:
- `1.mp4` - Played when plugin is **enabled**
- `2.mp4` - Played when plugin is **disabled**

**Video Packaging**: Videos are copied to the same directory as the plugin DLL during build:
```xml
<Content Include="videos\*.mp4">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</Content>
```

**Runtime Location**: Videos are located in the same directory as `FrenRider.dll`:
```
PluginDirectory/
├── FrenRider.dll
├── FrenRider.json
├── 1.mp4 (enable video)
└── 2.mp4 (disable video)
```

**No hardcoded paths** - videos are always found relative to the DLL location using `PluginInterface.AssemblyLocation.DirectoryName`.

## User Setup

### 1. Install VLC Media Player
Download and install VLC from: https://www.videolan.org/vlc/

### 2. Enable Video Notifications
1. Open Fren Rider settings (`/fr` or `/frenrider`)
2. Go to the **Misc** tab
3. Check **Video Notifications**

### 3. Test the Feature
Use the test command: `/fr testvideo` to verify video availability and playback.

## Video Requirements
- **Format**: MP4
- **Codec**: H.264 recommended
- **Resolution**: 640x480 (default, configurable)
- **Duration**: 5-10 seconds recommended
- **Audio**: Muted by default (configurable)

## Configuration Settings
- **Video Notifications**: Enable/disable the feature
- **Video Window X/Y**: Position of video window
- **Video Window Width/Height**: Size of video window
- **Video Mute Audio**: Control audio playback

## How It Works
1. When you enable Fren Rider (via `/fr on` or DTR bar), the plugin checks if video notifications are enabled
2. If enabled, it looks for `1.mp4` in the videos folder next to the plugin DLL
3. If found, it plays the video using VLC in borderless mode
4. The same process happens when disabling the plugin (plays `2.mp4`)

## Troubleshooting

### VLC Not Found
- Install VLC media player
- Ensure it's in the default installation location or in your PATH

### Videos Not Playing
- Check that video files exist in the correct location (`pluginDir/videos/`)
- Verify video format is MP4
- Check Dalamud log for error messages
- Use `/fr testvideo` command to debug

### Video Position Issues
- Adjust Video Window X/Y settings in the configuration
- Default position is (100, 100) from top-left corner

## Debug Commands
- `/fr testvideo` - Test video availability and playback
- Check Dalamud log for `[FR]` prefixed debug messages

## Technical Details
- Uses VLC command-line arguments for borderless playback
- Videos are embedded with plugin distribution
- Automatic cleanup when VLC process ends
- No UI decorations or borders
- Async video playback to prevent blocking

## Integration Notes
This feature integrates seamlessly with existing Fren Rider functionality and does not interfere with normal plugin operations. The video playback runs asynchronously and includes proper error handling and cleanup.
