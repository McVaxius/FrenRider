# How to Import and Use Fren Rider Plugin

This guide will walk you through installing and using the Fren Rider plugin for FFXIV.

---

## Prerequisites

Before you can use Fren Rider, you need:

### 1. XIVLauncher & Dalamud
- **XIVLauncher** must be installed and configured
- **Dalamud** must be enabled in XIVLauncher settings
- You must have launched FFXIV through XIVLauncher at least once

**Download XIVLauncher:** https://github.com/goatcorp/FFXIVQuickLauncher/releases

### 2. .NET Runtime
- Use an up-to-date Dalamud installation; v2.0.0.1 targets .NET 10.
- Building from source requires the .NET 10 SDK (the release workflow uses 10.0.201).

### 3. Recommended Plugins
For full functionality, install these plugins from the Dalamud Plugin Installer:

**Required:**
- **VNavmesh** - For navigation and pathfinding
- **SimpleTweaks** - Enable "Targeting Fix" in its settings

**Highly Recommended:**
- **BossMod** or **BossModReborn** - For combat automation
- **RotationSolver Reborn**, **Wrath**, or **Daedalus** - For combat rotations
- **Visland** - Alternative navigation system

**Optional:**
- **LazyLoot** - Automated looting
- **Discard Helper** - Inventory management
- **Cutscene Skipper** - Skip MSQ cutscenes

---

## Installation Methods

### Method 1: Install from Dev Plugin (For Source Development)

For normal installation, use the Aethertek custom repository in Method 2.

1. **Build the Plugin:**
   - Place a checkout of `McVaxius/aethertekUI` in a sibling `aethertekUI` directory so the project's shared UI references resolve
   - From the FrenRider repository root, build with the .NET 10 SDK:
     ```powershell
     dotnet build FrenRider/FrenRider.csproj --configuration Debug -p:Platform=x64 -p:DalamudLibPath="$env:APPDATA/XIVLauncher/addon/Hooks/dev"
     ```
   - `DalamudLibPath` must point to your current Dalamud assemblies; the path above is the one used by the release workflow
   - The plugin DLL will be in: `FrenRider/bin/x64/Debug/FrenRider.dll`

2. **Add to Dalamud Dev Plugins:**
   - Launch FFXIV with XIVLauncher
   - In-game, type `/xlsettings` in chat (or use the Dalamud console)
   - Go to the **Experimental** tab
   - Under "Dev Plugin Locations", click the **+** button
   - Paste the full path to `FrenRider.dll`
     - Example: `D:\temp\FrenRider\FrenRider\bin\x64\Debug\FrenRider.dll`
   - Click **Save and Close**

3. **Enable the Plugin:**
   - Type `/xlplugins` in chat
   - Go to **Dev Tools → Installed Dev Plugins**
   - Find **Fren Rider** in the list
   - Click the checkbox to enable it

4. **Verify Installation:**
   - Type `/frenrider` or `/fr` in chat
   - The Fren Rider main window should open
   - Use `/fr settings` or `/fr s` to open settings

---

### Method 2: Install from Custom Repository (Recommended)

Fren Rider is published through the Aethertek custom repository. [v2.0.0.1](https://github.com/McVaxius/FrenRider/releases/tag/v2.0.0.1) was released on October 7, 2026.

1. **Add Custom Repository:**
   - Type `/xlsettings` in-game
   - Go to **Experimental** tab
   - Under "Custom Plugin Repositories", add `https://aethertek.io/x.json`
   - Click **Save and Close**

2. **Install from Plugin Installer:**
   - Type `/xlplugins` in-game
   - Search for "Fren Rider"
   - Click **Install**

3. **Enable the Plugin:**
   - The plugin should auto-enable after installation
   - If not, check the box next to "Fren Rider" in the plugin list
   - Use `/fr` to verify the main window opens; configure settings before starting automation with `/fr on`

---

## First-Time Setup

### 1. Open Configuration
- Type `/fr settings` or `/fr s` in chat
- The settings window will open; `/frenrider` and `/fr` open the main window

### 2. Configure Basic Settings

**Party/Friend Settings:**
- **Fren Name:** Enter the first and last name of the person you want to follow
  - Example: `John Smith` or `John Smith@World`; the `@World` part is cosmetic for targeting
  - You can also select the fren from the party dropdown
  - Can be partial as long as it's unique (e.g., `John` if only one John in party)
  
**Following Settings:**
- **Cling Distance:** How close to get before stopping (default: 2.6 yalms)
- **Max Distance:** Maximum distance to follow (default: 500 yalms)
- **Cling Type:** Choose navigation method
  - 0 = VNavmesh (recommended)
  - 1 = Visland
  - 2 = BossMod Follow Leader
  - 3 = Vanilla Game Follow

**Mount Settings:**
- **Fly You Fools:** If enabled, flies on own mount instead of riding fren's mount
- **Fool Flier:** If flying solo, which mount to use (e.g., "Company Chocobo")

### 3. Configure Combat Settings (Optional)

**Rotation Plugin:**
- Choose which rotation plugin to use: BMR, VBM, RSR, WRATH, or DAEDALUS
- Set auto-rotation preset names for different content types

**Combat Behavior:**
- **Follow in Combat:** Whether to follow during combat (42 = auto-decide by job)
- **Positional:** Front/Rear/Any (42 = auto-decide by job)

### 4. Configure Automation (Optional)

**Food:**
- **Feed Me:** Item ID of food to consume (default: 4650 = Boiled Egg)
- Enable food search if you want it to find alternatives

**XP Item:**
- **XP Item:** Item ID to auto-equip (e.g., 41081 = Azyma Earring)

**Repair:**
- 0 = No auto-repair
- 1 = ADS self-repair
- 2 = ADS NPC repair without inn fallback
- 3 = ADS NPC repair without teleport or inn fallback
- 4 = ADS NPC repair followed by entering an inn room
- Requires ADS; repair triggers below the configured durability threshold

### 5. Save Configuration
- Settings save when changed; finish editing a text field so its value is committed

---

## Using Fren Rider

### Basic Usage

1. **Form a Party:**
   - Be in the same party as your "fren"
   - Make sure the fren name is configured correctly

2. **Start Following:**
   - Run `/fr on` to enable Fren Rider for the active character (new profiles are disabled by default)
   - While enabled, the plugin follows according to the configured movement mode and distance settings
   - Run `/fr off` to stop Fren Rider for the active character

3. **Mounting:**
   - When fren mounts, you'll automatically:
     - Mount on their multi-seat mount (pillion), OR
     - Mount your own mount if "Fly You Fools" is enabled

4. **Combat:**
   - Plugin will use configured rotation plugin
   - Follows combat behavior settings

### Slash Commands

| Command | Behavior |
| --- | --- |
| `/frenrider` or `/fr` | Toggle the main window |
| `/fr on` | Enable Fren Rider for the active character |
| `/fr off` | Disable Fren Rider for the active character |
| `/fr settings` or `/fr s` | Toggle the settings window |
| `/fr mini` or `/fr m` | Toggle the MAGIA mini window |
| `/fr debug` | Toggle and save debug controls for the active character config |

`/frenrider` always toggles the main window, regardless of arguments. It has no `toggle` or `reload` subcommands. Use `/fr` for the commands above.

Opening the mini window does not enable automation. Its MAGIA Attack, Defense, and Off buttons appear only in Eureka and send `/magiaauto attack`, `/magiaauto defense`, and `/magiaauto off`.

### Monitoring

**Check Plugin Status:**
- Open `/fr` to check run state, party status, follow/mount state, and ADS status
- Look for echo messages in chat (if spam_printer enabled)
- Check Dalamud plugin list (`/xlplugins`)

**Troubleshooting:**
- If not following, check:
  - Fren is in party
  - Fren name is correct
  - Distance > cling threshold
  - Plugin is enabled
  - Fren Rider is running for the active character (`/fr on`)
  - Required plugins (VNavmesh) are installed

---

## Advanced Configuration

### Social Distancing
- **Social Distancing:** Minimum distance to maintain in outdoor/foray zones
- **Wiggle:** Random variance to avoid bot-like behavior
- Prevents characters from stacking on top of each other

### Formation Following
- **Formation:** Enable to follow in 8-person grid pattern
- Positions based on party slot number
- Disabled during mounting

### Zone-Specific Settings
- **DD Distance:** Additional distance padding in Deep Dungeons
- **FATE Distance:** Additional distance padding in FATEs
- **Max Distance Foray:** Reduced max distance in forays

### Job-Specific Settings
- Plugin auto-detects your job
- Applies appropriate distance, positional, and follow settings
- Can be overridden in configuration

---

## Content-Specific Tips

### Overworld / FATEs
- Social distancing active by default
- Follows at configured cling distance
- Auto-mounts when fren mounts

### Dungeons / Trials / Raids
- Tighter following (social distancing off by default)
- Combat rotation active
- Auto-interact with duty objects (if configured)

### Deep Dungeons (PotD/HoH)
- Increased follow distance
- Special area transition handling
- DD-specific rotation preset

### Forays (Eureka/Bozja)
- Social distancing enforced
- Auto-accept Yes/No dialogs
- Wrath rotation recommended (phantom jobs)
- Mini-aetheryte transition support

### Treasure Maps
- Standard following behavior
- Loot management (if LazyLoot installed)

---

## Updating the Plugin

### Dev Plugin Updates
1. Pull latest code from repository
2. Rebuild with the .NET 10 SDK using the source-build instructions above
3. Restart FFXIV or reload plugin
   - `/xlplugins` → Disable → Enable

### Repository Plugin Updates
- Updates will appear in `/xlplugins` automatically
- Click **Update** when available

---

## Uninstalling

### Remove Dev Plugin
1. `/xlsettings` → Experimental
2. Remove the DLL path from "Dev Plugin Locations"
3. `/xlplugins` → Disable Fren Rider

### Remove Repository Plugin
1. `/xlplugins`
2. Find Fren Rider
3. Click **Delete**

### Clean Configuration
- Configuration files stored in:
  - `%APPDATA%\XIVLauncher\pluginConfigs\FrenRider\`
- Delete folder to remove all settings

---

## Troubleshooting

### Plugin Won't Load
- **Check:** Dalamud supports the release's API 15 / .NET 10 target
- **Check:** Dalamud is up to date
- **For source builds:** Check the .NET 10 SDK, shared UI checkout, Dalamud assembly path, and build errors
- **For dev plugins:** Check the plugin DLL path is correct

### Not Following Fren
- **Check:** Fren is in party
- **Check:** Fren name matches configuration (matching is case-insensitive; avoid ambiguous partial names)
- **Check:** Fren Rider is running for the active character (`/fr on`)
- **Check:** Distance > cling threshold
- **Check:** VNavmesh plugin installed and enabled
- **Check:** Not in a zone that restricts movement

### Mount Not Working
- **Check:** Fren has multi-seat mount (for pillion)
- **Check:** Zone allows mounts
- **Check:** Not in combat
- **Check:** "Fly You Fools" setting matches intent

### Combat Not Working
- **Check:** Rotation plugin (BMR/VBM/RSR/Wrath/Daedalus) installed
- **Check:** Rotation preset exists and is named correctly
- **Check:** Rotation plugin is enabled

### Performance Issues
- **Reduce:** Update frequency in configuration
- **Disable:** Spam printer (echo messages)
- **Check:** Other plugins causing conflicts

### Configuration Not Saving
- **Check:** File permissions on config directory
- **Try:** Finish editing the field; settings save when changed
- **Check:** No errors in Dalamud log (`/xllog`)

---

## Safety & Disclaimers

### Terms of Service
⚠️ **WARNING:** Using automation plugins may violate FFXIV's Terms of Service.
- Use at your own risk
- Account bans are possible
- Plugin is for educational purposes

### Responsible Use
- Don't use in competitive content (Savage, Ultimate, PvP)
- Be respectful of other players
- Don't advertise plugin use in-game
- Monitor your character, don't AFK bot

### Data Privacy
- Plugin does not collect or transmit data
- Configuration stored locally only
- No telemetry or analytics

---

## Support & Feedback

### Getting Help
- Check this guide first
- Review README.MD for current features and commands
- Check CHANGELOG.md for recent changes

### Reporting Issues
- Provide clear description of problem
- Include steps to reproduce
- Note your game version and Dalamud version
- List installed plugins

### Feature Requests
- Check PROJECT_PLAN.md for planned features
- Suggest new features via GitHub issues

---

## FAQ

**Q: Can I use this solo?**  
A: No, you need to be in a party with the person you're following.

**Q: Does this work in PvP?**  
A: Not recommended and likely won't work correctly.

**Q: Can I follow multiple people?**  
A: No, only one "fren" at a time.

**Q: Will this get me banned?**  
A: Possibly. Use at your own risk. Automation plugins violate TOS.

**Q: Does it work with all jobs?**  
A: Yes, with job-specific optimizations for each role.

**Q: Can I customize the rotation?**  
A: Yes, through the rotation plugin (BMR/VBM/RSR/Wrath/Daedalus) settings.

**Q: Does it work in all zones?**  
A: Most zones, with special handling for dungeons, forays, deep dungeons, etc.

**Q: Can I turn it off temporarily?**  
A: Yes, use `/fr off` to stop Fren Rider for the active character and `/fr on` to resume. You can also disable the plugin in `/xlplugins`.

**Q: How do I update my settings?**  
A: Use `/fr settings` or `/fr s`, then make changes; settings save when changed.

**Q: What if my fren changes?**  
A: Update the "Fren Name" in configuration.

---

## Credits

**Original Script:** frenrider_McVaxius.lua by McVaxius  
**Plugin Development:** Based on Dalamud SamplePlugin template  
**Framework:** Dalamud by goatcorp  

---

*Last Updated: October 7, 2026 — verified against README.MD and v2.0.0.1 source at a7ddfe6d3788fa9d5eef8af4c417929a6505cc05.*
