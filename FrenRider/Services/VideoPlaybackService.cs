using Dalamud.Plugin.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FrenRider.Services;

public class VideoPlaybackService : IDisposable
{
    private readonly Configuration config;
    private readonly IPluginLog log;
    private readonly IChatGui chat;

    private Process? vlcProcess;
    private bool isPlaying = false;
    private string currentVideoPath = "";

    public bool IsPlaying => isPlaying;
    public string CurrentVideo => currentVideoPath;

    public VideoPlaybackService(Configuration config, IPluginLog log, IChatGui chat)
    {
        this.config = config;
        this.log = log;
        this.chat = chat;
    }

    /// <summary>
    /// Play an MP4 video file using VLC media player in borderless mode
    /// </summary>
    public async Task<bool> PlayVideo(string videoPath)
    {
        log.Debug($"[FR] PlayVideo called with: {videoPath}");
        
        if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
        {
            log.Warning($"[FR] Video file not found: {videoPath}");
            log.Debug($"[FR] File exists check: {!string.IsNullOrEmpty(videoPath)}, {File.Exists(videoPath)}");
            return false;
        }

        try
        {
            // Stop any currently playing video
            StopVideo();

            currentVideoPath = videoPath;
            log.Info($"[FR] Starting video playback: {videoPath}");

            // Start VLC process for video playback
            await StartVLCPlayback(videoPath);

            isPlaying = true;
            log.Information("[FR] Video playback started");
            return true;
        }
        catch (Exception ex)
        {
            log.Error(ex, "[FR] Failed to start video playback");
            return false;
        }
    }

    /// <summary>
    /// Stop video playback
    /// </summary>
    public void StopVideo()
    {
        try
        {
            if (vlcProcess != null && !vlcProcess.HasExited)
            {
                vlcProcess.Kill();
                vlcProcess.Dispose();
                vlcProcess = null;
            }

            isPlaying = false;
            currentVideoPath = "";
            log.Debug("[FR] Video playback stopped");
        }
        catch (Exception ex)
        {
            log.Error(ex, "[FR] Error stopping video playback");
        }
    }

    /// <summary>
    /// Start VLC process for video playback
    /// </summary>
    private async Task StartVLCPlayback(string videoPath)
    {
        await Task.Run(() =>
        {
            try
            {
                var vlcPath = GetVLCPath();
                if (string.IsNullOrEmpty(vlcPath))
                {
                    log.Warning("[FR] VLC not found. Video notifications require VLC media player.");
                    log.Warning("[FR] Download from: https://www.videolan.org/vlc/");
                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = vlcPath,
                    Arguments = BuildVLCArguments(videoPath),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                vlcProcess = new Process { StartInfo = startInfo };
                vlcProcess.Start();
                
                log.Information("[FR] VLC process started successfully");
                
                // Start monitoring playback
                StartPlaybackMonitoring();
            }
            catch (Exception ex)
            {
                log.Error(ex, "[FR] Failed to start VLC process");
            }
        });
    }

    /// <summary>
    /// Build VLC command line arguments for borderless playback
    /// </summary>
    private string BuildVLCArguments(string videoPath)
    {
        // VLC arguments - completely borderless, positioned, play once and close
        var args = "-I dummy --no-video-deco --no-embedded-video --play-and-exit";
        
        // Position window
        args += $" --video-x={config.VideoWindowX} --video-y={config.VideoWindowY}";
        
        // Size window to video dimensions
        args += $" --width={config.VideoWindowWidth} --height={config.VideoWindowHeight}";
        
        // Add audio settings
        if (config.VideoMuteAudio)
        {
            args += " --no-audio";
        }

        // Properly escape special characters in filename
        var escapedPath = videoPath.Replace("[", "\\[").Replace("]", "\\]");
        args += $" \"{escapedPath}\"";
        
        return args;
    }

    /// <summary>
    /// Get VLC executable path
    /// </summary>
    private string GetVLCPath()
    {
        var possiblePaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VideoLAN", "VLC", "vlc.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VideoLAN", "VLC", "vlc.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "VLC", "vlc.exe"),
            "vlc.exe" // Assume it's in PATH
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
                return path;
        }

        return "";
    }

    /// <summary>
    /// Start monitoring playback status
    /// </summary>
    private void StartPlaybackMonitoring()
    {
        // Use a simple timer to monitor when VLC exits
        Task.Run(async () =>
        {
            while (vlcProcess != null && !vlcProcess.HasExited)
            {
                await Task.Delay(1000);
            }
            
            isPlaying = false;
            currentVideoPath = "";
            log.Debug("[FR] VLC process ended");
        });
    }

    /// <summary>
    /// Check if VLC is available
    /// </summary>
    public bool IsVLCAvailable()
    {
        return !string.IsNullOrEmpty(GetVLCPath());
    }

    /// <summary>
    /// Get embedded video path by name
    /// </summary>
    public string GetEmbeddedVideoPath(string videoName)
    {
        try
        {
            // Always look in the same directory as the plugin DLL
            var pluginDir = Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "";
            var videoPath = Path.Combine(pluginDir, videoName);
            
            log.Debug($"[FR] Looking for video: {videoName}");
            log.Debug($"[FR] Plugin directory: {pluginDir}");
            log.Debug($"[FR] Full video path: {videoPath}");
            log.Debug($"[FR] Video file exists: {File.Exists(videoPath)}");
            
            return File.Exists(videoPath) ? videoPath : string.Empty;
        }
        catch (Exception ex)
        {
            log.Error(ex, "[FR] Failed to get embedded video path");
            return string.Empty;
        }
    }

    /// <summary>
    /// Check if both enable and disable videos are available
    /// </summary>
    public bool CheckVideoAvailability()
    {
        var enablePath = GetEmbeddedVideoPath("1.mp4");
        var disablePath = GetEmbeddedVideoPath("2.mp4");
        
        log.Debug($"[FR] Enable video available: {!string.IsNullOrEmpty(enablePath)}");
        log.Debug($"[FR] Disable video available: {!string.IsNullOrEmpty(disablePath)}");
        
        return !string.IsNullOrEmpty(enablePath) && !string.IsNullOrEmpty(disablePath);
    }

    /// <summary>
    /// Get list of available embedded videos
    /// </summary>
    public string[] GetAvailableEmbeddedVideos()
    {
        try
        {
            var pluginDir = Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "";
            var videosDir = Path.Combine(pluginDir, config.EmbeddedVideosFolder);
            
            if (!Directory.Exists(videosDir))
                return Array.Empty<string>();

            return Directory.GetFiles(videosDir, "*.mp4")
                           .Select(Path.GetFileName)
                           .Where(name => !string.IsNullOrEmpty(name))
                           .Cast<string>()
                           .ToArray() ?? Array.Empty<string>();
        }
        catch (Exception ex)
        {
            log.Error(ex, "[FR] Failed to get available embedded videos");
            return Array.Empty<string>();
        }
    }

    public void Dispose()
    {
        StopVideo();
    }
}
