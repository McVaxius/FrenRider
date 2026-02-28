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
- .NET Core 8 Runtime (usually installed with XIVLauncher/Dalamud)

### 3. Recommended Plugins
For full functionality, install these plugins from the Dalamud Plugin Installer:

**Required:**
- **VNavmesh** - For navigation and pathfinding
- **SimpleTweaks** - Enable "Targeting Fix" in its settings

**Highly Recommended:**
- **BossMod** or **BossModReborn** - For combat automation
- **RotationSolver Reborn** or **Wrath** - For combat rotations
- **Visland** - Alternative navigation system

**Optional:**
- **LazyLoot** - Automated looting
- **Discard Helper** - Inventory management
- **Cutscene Skipper** - Skip MSQ cutscenes

---

## Installation Methods

### Method 1: Install from Dev Plugin (During Development)

1. **Build the Plugin:**
   - Open `FrenRider.sln` in Visual Studio 2022
   - Build the solution (Build → Build Solution or Ctrl+Shift+B)
   - The plugin DLL will be in: `FrenRider/bin/x64/Debug/FrenRider.dll`

2. **Add to Dalamud Dev Plugins:**
   - Launch FFXIV with XIVLauncher
   - In-game, type `/xlsettings` in chat (or use the Dalamud console)
   - Go to the **Experimental** tab
   - Under "Dev Plugin Locations", click the **+** button
   - Paste the full path to `FrenRider.dll`
     - Example: `D:\temp\FrenRider\bin\x64\Debug\FrenRider.dll`
   - Click **Save and Close**

3. **Enable the Plugin:**
   - Type `/xlplugins` in chat
   - Go to **Dev Tools → Installed Dev Plugins**
   - Find **Fren Rider** in the list
   - Click the checkbox to enable it

4. **Verify Installation:**
   - Type `/frenrider` in chat
   - The Fren Rider configuration window should open

---

### Method 2: Install from Custom Repository (Future)

> **Note:** This method will be available once the plugin is published to a custom repository.

1. **Add Custom Repository:**
   - Type `/xlsettings` in-game
   - Go to **Experimental** tab
   - Under "Custom Plugin Repositories", add the repository URL
   - Click **Save and Close**

2. **Install from Plugin Installer:**
   - Type `/xlplugins` in-game
   - Search for "Fren Rider"
   - Click **Install**

3. **Enable the Plugin:**
   - The plugin should auto-enable after installation
   - If not, check the box next to "Fren Rider" in the plugin list

---

## First-Time Setup

### 1. Open Configuration
- Type `/frenrider` in chat
- The configuration window will open

### 2. Configure Basic Settings

**Party/Friend Settings:**
- **Fren Name:** Enter the first and last name of the person you want to follow
  - Example: `John Smith` (do NOT include @Server)
  - Can be partial as long as it's unique (e.g., `John` if only one John in party)
  
**Following Settings:**
- **Cling Distance:** How close to get before stopping (default: 2.6 yalms)
- **Max Distance:** Maximum distance to follow (default: 500 yalms)
- **Cling Type:** Choose navigation method
  - 0 = VNavmesh (recommended)
  - 1 = Visland
  - 2 = BossMod Follow Leader
  - 3 = CBT Autofollow
  - 4 = Vanilla Game Follow

**Mount Settings:**
- **Fly You Fools:** If enabled, flies on own mount instead of riding fren's mount
- **Fool Flier:** If flying solo, which mount to use (e.g., "Company Chocobo")

### 3. Configure Combat Settings (Optional)

**Rotation Plugin:**
- Choose which rotation plugin to use: BMR, VBM, RSR, or WRATH
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
- 1 = Self-repair always
- 2 = Repair at inn NPC

### 5. Save Configuration
- Click **Save** or close the window (auto-saves)

---

## Using Fren Rider

### Basic Usage

1. **Form a Party:**
   - Be in the same party as your "fren"
   - Make sure the fren name is configured correctly

2. **Start Following:**
   - The plugin automatically follows when distance exceeds cling threshold
   - No manual command needed once configured

3. **Mounting:**
   - When fren mounts, you'll automatically:
     - Mount on their multi-seat mount (pillion), OR
     - Mount your own mount if "Fly You Fools" is enabled

4. **Combat:**
   - Plugin will use configured rotation plugin
   - Follows combat behavior settings

### Slash Commands

- `/frenrider` - Open configuration window
- `/frenrider toggle` - Enable/disable following (if implemented)
- `/frenrider reload` - Reload configuration (if implemented)

### Monitoring

**Check Plugin Status:**
- Look for echo messages in chat (if spam_printer enabled)
- Check Dalamud plugin list (`/xlplugins`)

**Troubleshooting:**
- If not following, check:
  - Fren is in party
  - Fren name is correct
  - Distance > cling threshold
  - Plugin is enabled
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
2. Rebuild solution in Visual Studio
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
- **Check:** .NET Core 8 SDK installed
- **Check:** Dalamud is up to date
- **Check:** Plugin DLL path is correct
- **Check:** No compile errors in Visual Studio

### Not Following Fren
- **Check:** Fren is in party
- **Check:** Fren name matches configuration (case-sensitive)
- **Check:** Distance > cling threshold
- **Check:** VNavmesh plugin installed and enabled
- **Check:** Not in a zone that restricts movement

### Mount Not Working
- **Check:** Fren has multi-seat mount (for pillion)
- **Check:** Zone allows mounts
- **Check:** Not in combat
- **Check:** "Fly You Fools" setting matches intent

### Combat Not Working
- **Check:** Rotation plugin (BMR/VBM/RSR/Wrath) installed
- **Check:** Rotation preset exists and is named correctly
- **Check:** Rotation plugin is enabled

### Performance Issues
- **Reduce:** Update frequency in configuration
- **Disable:** Spam printer (echo messages)
- **Check:** Other plugins causing conflicts

### Configuration Not Saving
- **Check:** File permissions on config directory
- **Try:** Manually save in UI
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
- Review PROJECT_PLAN.md for feature status
- Check CHANGELOG.md for recent changes

### Reporting Issues
- Provide clear description of problem
- Include steps to reproduce
- Note your game version and Dalamud version
- List installed plugins

### Feature Requests
- Check PROJECT_PLAN.md for planned features
- Suggest new features via GitHub issues (when available)

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
A: Yes, through the rotation plugin (BMR/VBM/RSR/Wrath) settings.

**Q: Does it work in all zones?**  
A: Most zones, with special handling for dungeons, forays, deep dungeons, etc.

**Q: Can I turn it off temporarily?**  
A: Yes, disable the plugin in `/xlplugins` or use toggle command (if implemented).

**Q: How do I update my settings?**  
A: `/frenrider` to open config, make changes, save.

**Q: What if my fren changes?**  
A: Update the "Fren Name" in configuration.

---

## Credits

**Original Script:** frenrider_McVaxius.lua by McVaxius  
**Plugin Development:** Based on Dalamud SamplePlugin template  
**Framework:** Dalamud by goatcorp  

---

*Last Updated: Phase 0 - Initial Documentation*
