# Fren Rider

A native FFXIV Dalamud plugin for multiboxing support, converting the original [frenrider Lua script](https://github.com/McVaxius/dhogsbreakfeast/blob/main/Dungeons%20and%20Multiboxing/frenrider/frenrider_McVaxius.lua) into a fully-featured plugin.

- Some things not done
- NPC repair Via the Inn
- searching for items to eat
- leaving a duty when its over

## Features

**Phases 1-10 Complete:**
- ✅ Multi-account/character configuration system
- ✅ Party member following with VNavmesh/Visland integration
- ✅ Auto-mount/dismount with "Fly You Fools" mode
- ✅ Flying follow with proper navigation
- ✅ Combat integration (RSR/WRATH/BMR/VBM rotation plugins)
- ✅ Zone-specific logic (Deep Dungeons, FATEs, Forays)
- ✅ Formation-based positioning (8-person grid)
- ✅ Social distancing for outdoor zones
- ✅ Automation features (food, repair, idle actions)
- ✅ DTR bar integration
- ✅ Comprehensive UI with status display

## Installation

### Via Custom Repository (Recommended)

1. Open Dalamud Settings (`/xlsettings`)
2. Go to Experimental tab
3. Add custom plugin repository:
   ```
   https://raw.githubusercontent.com/McVaxius/TheDumpsterFire/refs/heads/master/repo.json
   ```
4. Open Plugin Installer (`/xlplugins`)
5. Search for "Fren Rider" and click Install

### Manual Installation

1. Download `latest.zip` from [Releases](https://github.com/McVaxius/FrenRider/releases/latest)
2. Extract to `%AppData%\XIVLauncher\installedPlugins\FrenRider\`
3. Restart game or reload plugins

## Usage

- `/frenrider` - Open main status window
- `/frenrider config` - Open settings window
- Click DTR bar entry (server info bar) to toggle plugin on/off

## Configuration

The plugin uses per-account configuration files stored in:
```
%AppData%\XIVLauncher\pluginConfigs\
```

Each account gets a separate config file: `{ContentId}_FrenRider.json`

## Dependencies

**Required Plugins:**
- VNavmesh (for navigation) OR Visland (alternative)

**Optional Plugins:**
- RotationSolver Reborn (RSR) or Wrath (combat automation)
- BossModReborn (BMR) or VanillaBossMod (VBM) (alternative combat)

## Development Status

**Current Version:** 0.0.0.1 (Phases 1-10 Complete)

**Completed:**
- All core functionality from original Lua script
- Multi-account support with character-specific configs
- Flying follow with smooth navigation
- Combat integration with rotation plugins
- Zone-specific behaviors

**Future (Phase 11+):**
- Plugin icon (3 guys on shoulders concept) - **TODO: Create icon.png**
- Party invite whitelist system
- Auto-accept party invites
- Instance number management
- Custom chat command system

## Icon Status

**TODO:** Plugin icon needs to be created
- Concept: "3 guys on shoulders" (multiboxing theme)
- Format: PNG (64x64 or 128x128 recommended)
- Location: `FrenRider/icon.png`
- Once created, add to `.csproj` as Content and reference in `FrenRider.json`

## Building from Source

```bash
git clone https://github.com/McVaxius/FrenRider.git
cd FrenRider
dotnet build --configuration Release
```

Output: `FrenRider/bin/x64/Release/FrenRider/`

## License

AGPL-3.0-or-later

## Credits

- Original Lua script: [frenrider_McVaxius.lua](https://github.com/McVaxius/dhogsbreakfeast/blob/main/Dungeons%20and%20Multiboxing/frenrider/frenrider_McVaxius.lua)
- Dalamud Plugin Framework: [goatcorp/Dalamud](https://github.com/goatcorp/Dalamud)