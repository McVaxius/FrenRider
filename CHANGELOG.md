# Fren Rider - Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Phase 3.1 - In-Game Testing Feedback Fixes

#### [0.2.1] - 2026-02-28

**Fixed:**
- Left panel now user-resizable via drag splitter (120px–500px range, persisted in config)
  - Visual splitter line highlights on hover, cursor changes to resize arrow
  - Width saved to `Configuration.LeftPanelWidth` on release
- Krangle now locks Fren Name field to read-only when enabled, showing garbled text
  - Party member dropdown hidden when Krangle is on (can't edit anyway)
  - Tooltip: "Disable Krangle to edit fren name."
- All `SliderFloat` controls replaced with `InputFloat` (editable fields, up to 3 decimal places)
  - Affected: Update Interval, Cling Distance, Social Distance, X Wiggle, Z Wiggle
- Login detection "Not on main thread!" error fixed
  - `ClientState.Login` event now defers to framework update via 3-frame delay
  - Plugin load with already-logged-in character also deferred
  - `OnLoginEvent()` sets delay flag; `OnLogin()` only runs from framework update

**Changed:**
- `Configuration.cs` - Added `LeftPanelWidth` property (default 240f)
- `Plugin.cs` - Split login handling into `OnLoginEvent` (event handler) and `OnLogin` (deferred)

**Build Results:**
- 0 errors, 0 warnings

**Files Modified:**
- `FrenRider/Configuration.cs` - LeftPanelWidth
- `FrenRider/Plugin.cs` - Deferred login detection
- `FrenRider/Windows/ConfigWindow.cs` - Resizable splitter, Krangle fren lock, InputFloat

**Testing Required:**
1. Drag the splitter between left and right panels → resizes, cursor changes
2. Close and reopen settings → panel width persisted
3. Enable Krangle → Fren Name becomes read-only with garbled text, dropdown hidden
4. Disable Krangle → Fren Name editable again with real text
5. All numeric fields are now text inputs with +/- buttons (no sliders)
6. Character swap / logout-login → no "Not on main thread!" error in /xllog

---

### Phase 3 - Party & Target Detection

#### [0.2.0] - 2026-02-28

**Added:**
- `FrenTracker` service: real-time party member enumeration and fren tracking
  - Party member scanning with position, distance, ClassJob, and role detection
  - Fren name matching: partial, case-insensitive, @Server stripped for search
  - ObjectTable scanning for nearby non-party fren fallback
  - Distance calculation (3D Euclidean via Vector3.Distance)
  - Party composition analyzer (role counts: Tank/Healer/Melee/Ranged/Caster)
  - ClassJob → Role mapping for all combat jobs including VPR (41), PCT (42)
  - Update throttled by config's UpdateInterval setting
- MainWindow now shows real-time fren tracking info:
  - Fren name, job, distance, position coordinates
  - "in party" vs "not in party" indicator
  - Party composition summary (e.g. "1 Tank, 1 Healer, 2 Melee")
  - "Tracking inactive" when disabled, "Fren not found" when missing
- FrenTracker integrated into Plugin.cs framework update loop

**Changed:**
- MainWindow status display replaced manual party scanning with FrenTracker data

**Build Results:**
- 0 errors, 0 warnings

**Files Created:**
- `FrenRider/Services/FrenTracker.cs`

**Files Modified:**
- `FrenRider/Plugin.cs` - FrenTracker creation and Update() call
- `FrenRider/Windows/MainWindow.cs` - FrenTracker-based status display

**Backups:**
- `backups/Plugin_*.cs`
- `backups/MainWindow_*.cs`

**Testing Required:**
1. Enable plugin and join a party → party members listed with count
2. Set fren name → "Fren found" with job, distance, position shown
3. Walk away from fren → distance updates in real-time
4. Leave party → fren detected via ObjectTable if nearby
5. Fren not nearby → "Fren not found" displayed
6. Disable plugin → "Tracking inactive"
7. No crashes in /xllog

---

### Phase 1.1 - UI Feedback & New Features

#### [0.1.1] - 2026-02-28

**Fixed:**
- DTR bar click now toggles Fren Rider on/off instead of opening main window
- Left panel widened from 200px to 240px with spacing between entries to fix name truncation
- "Reset This Page" renamed to "Reset This" for shorter label; both Reset buttons now have (?) tooltips
- Window default size bumped from 850 to 900px wide
- Fren Name tooltip clarifies @Server is cosmetic only

**Added:**
- `[DELETE]` button for non-DEFAULT character configs (requires CTRL+click, dimmed when CTRL not held)
- `[ ] Krangle` checkbox: garbles all identifying text (names, servers) with military/exercise words
  - Deterministic per-name (same input → same output)
  - Respects FF14 naming conventions (max 14 per part, max 22 total, server max 25)
  - Applies to ConfigWindow title, left panel, and MainWindow fren display
  - Tooltip explains purpose (screenshots for issue reporting)
- Mount selector: searchable dropdown populated from Lumina Mount sheet (game data)
  - IDataManager service added to Plugin.cs
  - "Mount Roulette" always first, rest sorted alphabetically
  - Filter-as-you-type search box inside dropdown
- `KrangleService.cs` - static service for deterministic name garbling
- `DeleteCharacter()` method in ConfigManager
- `KrangleEnabled` property in Configuration.cs

**Changed:**
- Mount Name input replaced with searchable combo box (was plain InputText)
- Upper-right layout: `[ ] Krangle ... [Reset All] (?) [Reset This] (?) [DELETE]`

**Build Results:**
- 0 errors, 0 warnings

**Files Created:**
- `FrenRider/Services/KrangleService.cs`

**Files Modified:**
- `FrenRider/Plugin.cs` - IDataManager, mount loading, DTR toggle fix
- `FrenRider/Configuration.cs` - KrangleEnabled
- `FrenRider/Services/ConfigManager.cs` - DeleteCharacter
- `FrenRider/Windows/ConfigWindow.cs` - All UI changes
- `FrenRider/Windows/MainWindow.cs` - Krangle display support

**Backups:**
- `backups/Plugin_*.cs`
- `backups/ConfigWindow_*.cs`
- `backups/MainWindow_*.cs`

**Testing Required:**
1. DTR bar click toggles FR: On/Off (not main window)
2. Left panel names not truncated, spacing between entries
3. Krangle checkbox garbles names in config window title, left panel, main window
4. Reset All (?) and Reset This (?) both show tooltips
5. DELETE button appears only for non-DEFAULT entries, dimmed without CTRL, works with CTRL held
6. Mount dropdown populated with game mounts, searchable
7. No crashes in /xllog

---

### Phase 0 - Project Initialization (Current)

#### [0.0.1] - 2026-02-28 @ 03:19 AM EST

**Added:**
- Initial project documentation structure
- README.md with project description and goals
- PROJECT_PLAN.md with comprehensive development roadmap
- KNOWLEDGE_BASE.md with technical reference (gitignored)
- how-to-import-plugins.md with user installation guide
- .gitignore with appropriate exclusions
- CHANGELOG.md (this file)
- Git repository initialized

**Files Created:**
- `d:\temp\FrenRider\README.MD`
- `d:\temp\FrenRider\PROJECT_PLAN.md`
- `d:\temp\FrenRider\KNOWLEDGE_BASE.md`
- `d:\temp\FrenRider\how-to-import-plugins.md`
- `d:\temp\FrenRider\.gitignore`
- `d:\temp\FrenRider\CHANGELOG.md`

**Research Completed:**
- Analyzed SamplePlugin template structure
- Reviewed frenrider_McVaxius.lua script (v5)
- Studied dfunc.lua utility library
- Examined SomethingNeedDoing plugin architecture
- Documented 50+ configuration variables
- Identified 11 development phases
- Mapped Lua-to-C# translation requirements

**Technical Decisions:**
- Framework: .NET Core 8 with Dalamud
- Language: C#
- UI: ImGui.NET
- Config: JSON-based (IPluginConfiguration)
- Navigation: VNavmesh primary, Visland secondary
- Combat: Multi-plugin support (BMR/VBM/RSR/Wrath)

**Next Steps:**
- User review and approval of project plan
- Begin Phase 1: Basic Plugin Structure
- Clone SamplePlugin template
- Rename to FrenRider
- Test initial plugin load

**Testing Required:**
- None yet (documentation phase)

**Notes:**
- Project scope: Convert Lua script to native Dalamud plugin
- Target: Multiboxing support for 2+ characters
- Original script: ~1000+ lines of Lua
- Estimated timeline: 18-30 development sessions

---

### Phase 1 - Basic Plugin Structure (Complete)

#### [0.1.0] - 2026-02-28 @ 03:32 AM EST

**Added:**
- Complete plugin project structure based on SamplePlugin template
- `FrenRider.sln` - Visual Studio solution file
- `FrenRider/FrenRider.csproj` - Project file using Dalamud.NET.Sdk/14.0.2
- `FrenRider/FrenRider.json` - Plugin manifest with metadata
- `FrenRider/Plugin.cs` - Main plugin class with:
  - Dalamud service injection (ClientState, Framework, PartyList, Condition, ChatGui, ObjectTable, etc.)
  - `/frenrider` slash command registration
  - WindowSystem with MainWindow and ConfigWindow
  - Proper Dispose pattern for cleanup
- `FrenRider/Configuration.cs` - Full configuration class with all 35+ settings from original Lua script:
  - Party/Friend settings (FrenName, FlyYouFools, FoolFlier, CompanionStrat, etc.)
  - Distance/Following settings (Cling, ClingType, SocialDistancing, MaxBistance, etc.)
  - Combat/AI settings (RotationPlugin, AutoRotationType, Positional, etc.)
  - Automation settings (FeedMe, XpItem, Repair, IdleShitter, etc.)
  - Misc settings (FulfType, SpamPrinter, CbtEdse)
- `FrenRider/Windows/MainWindow.cs` - Main UI window with:
  - Enable/Disable toggle
  - Fren Name input
  - Live status display (logged in, party count, fren detection, zone ID)
  - Settings button
- `FrenRider/Windows/ConfigWindow.cs` - Configuration UI with tabbed layout:
  - Party/Friend tab
  - Distance/Following tab
  - Combat/AI tab
  - Automation tab
  - Misc tab
  - All settings save immediately on change

**Implementation Details:**
- Used Dalamud.NET.Sdk v14.0.2 (matches current SamplePlugin template)
- All SamplePlugin references renamed to FrenRider
- Namespace: `FrenRider` with `FrenRider.Windows` sub-namespace
- Configuration uses JSON serialization via `IPluginConfiguration`
- All original Lua config variables mapped to C# properties with matching defaults
- ImGui UI uses tabbed interface for organized settings
- MainWindow shows real-time party detection and fren name matching

**Build Results:**
- `dotnet restore` - SUCCESS
- `dotnet build --configuration Debug` - SUCCESS
- Output: `FrenRider/bin/x64/Debug/FrenRider.dll` (37,376 bytes)
- Output: `FrenRider/bin/x64/Debug/FrenRider.json` (674 bytes)
- 0 errors, 0 warnings

**Testing Performed:**
- Compilation verified - no errors or warnings
- Output DLL produced correctly
- JSON manifest included in output

**Testing Required (User):**
1. Open FFXIV with XIVLauncher/Dalamud
2. Go to `/xlsettings` → Experimental → Dev Plugin Locations
3. Add the full path: `D:\temp\FrenRider\FrenRider\bin\x64\Debug`
4. Go to `/xlplugins` → Dev Tools → Installed Dev Plugins
5. Enable "Fren Rider"
6. Type `/frenrider` in chat
7. **Verify:** Main window opens showing status info
8. **Verify:** Click "Open Settings" → Config window opens with 5 tabs
9. **Verify:** Change Fren Name → close and reopen → value persists
10. **Verify:** No crashes or errors in `/xllog`

**Known Issues:**
- Plugin is UI-only at this point, no following/combat logic yet
- Configuration values are saved but not yet used by any system
- Party detection shown in UI but no action taken on it

**Files Created:**
- `FrenRider.sln`
- `FrenRider/FrenRider.csproj`
- `FrenRider/FrenRider.json`
- `FrenRider/Plugin.cs`
- `FrenRider/Configuration.cs`
- `FrenRider/Windows/MainWindow.cs`
- `FrenRider/Windows/ConfigWindow.cs`
- `backups/` (empty directory for future backups)

---

### Phase 1.1 - UI Redesign & Multi-Account Config (Complete)

#### [0.1.1] - 2026-02-28 @ 10:35 AM EST

**Added:**
- Multi-account/multi-character configuration system
  - `FrenRider/Models/CharacterConfig.cs` - Per-character settings (35+ fields with Clone())
  - `FrenRider/Models/AccountConfig.cs` - Account container (alias, default config, character dictionary)
  - `FrenRider/Services/ConfigManager.cs` - File I/O for `<accountId>_FrenRider.json` files
  - Account auto-detection: characters auto-register to accounts on login
  - Per-account JSON files stored in plugin config directory
- Left panel in ConfigWindow with character list
  - Editable account alias at top
  - DEFAULT CONFIG selectable
  - Current character highlighted in green
  - Other characters sorted alphabetically
  - Clicking any entry populates settings on right side
- DTR bar integration (`IDtrBar` service)
  - Shows "FR: On" / "FR: Off" in server info bar
  - Click DTR entry to toggle main window
  - Tooltip shows active fren name or disabled status
  - Checkbox in MainWindow to enable/disable DTR bar
- (?) help markers next to every setting with detailed tooltips
- Reset buttons: "Reset All" (all tabs) and "Reset This Page" (current tab only)
  - Resets character to default config; if default, resets to plugin defaults
- Fren name auto-capitalization (e.g., "gabe newell@pcmr" -> "Gabe Newell@Pcmr")
- Party member quick-select dropdown for fren name field
- Window title updates to show selected character name

**Changed:**
- Configuration.cs simplified to global settings only (DtrBarEnabled, IsConfigWindowMovable, LastAccountId)
- All per-character settings moved to CharacterConfig.cs
- Plugin.cs rewritten with:
  - ConfigManager integration
  - DTR bar setup/update
  - Login event detection with Framework.Update fallback
  - IObjectTable.LocalPlayer (replaces deprecated IClientState.LocalPlayer)
- ConfigWindow.cs completely rewritten:
  - Left panel + right panel layout (200px / rest)
  - Companion Stance: now dropdown (Free Stance, Defender Stance, Attacker Stance, Healer Stance, Follow)
  - Cling Type: CBT Autofollow removed, now 4 options (NavMesh, Visland, BossMod Follow, Vanilla Follow)
  - Combat tab: Rotation Plugin, Rotation Plugin Foray, Rotation Type, BossMod AI, Positional, Follow in Combat all converted to dropdowns
  - Loot Type: now dropdown (unchanged, need, greed, pass)
  - Repair: now dropdown (No, Self Repair, Inn NPC)
  - Enhanced Duty Start/End, Echo Messages: now On/Off dropdowns
  - Automation and Misc tabs merged into single "Misc" tab
  - Idle behavior: now 2-tier dropdown (Specific Action / Action From List -> Default List / Custom List)
  - Tick Rate renamed to "Update Interval" with 0.05-5.0s range and performance warning
  - Food Item ID removed (plugin version doesn't need it, just name)
- MainWindow.cs rewritten:
  - Fren Name now read-only (displays from config, editable only in Settings)
  - DTR Bar checkbox added next to Enabled
  - Shows account alias in status section
  - Fren detection uses name part before @ for partial matching

**Build Results:**
- `dotnet build --configuration Debug` - SUCCESS
- 0 errors, 0 warnings
- Output: `FrenRider/bin/x64/Debug/FrenRider.dll`

**Files Created:**
- `FrenRider/Models/CharacterConfig.cs`
- `FrenRider/Models/AccountConfig.cs`
- `FrenRider/Services/ConfigManager.cs`

**Files Modified:**
- `FrenRider/Configuration.cs` (simplified to global only)
- `FrenRider/Plugin.cs` (DTR bar, ConfigManager, login detection)
- `FrenRider/Windows/ConfigWindow.cs` (complete rewrite)
- `FrenRider/Windows/MainWindow.cs` (read-only fren, DTR toggle)

**Backups Created:**
- `backups/Plugin_20260228_102120.cs`
- `backups/Configuration_20260228_102120.cs`
- `backups/MainWindow_20260228_102120.cs`
- `backups/ConfigWindow_20260228_102120.cs`

**Testing Required (User):**
1. Rebuild or reload plugin in Dalamud
2. Type `/frenrider` - main window should open
3. **Verify:** Fren Name is read-only (just text, not editable)
4. **Verify:** DTR Bar checkbox toggles "FR: Off" in server info bar
5. **Verify:** Click DTR bar entry toggles main window
6. **Verify:** Click "Open Settings" - config window has left panel with character list
7. **Verify:** Your current character appears in green in the left panel
8. **Verify:** DEFAULT CONFIG is selectable; switching characters changes right panel
9. **Verify:** Window title shows "Fren Rider Settings - CharName@Server"
10. **Verify:** (?) icons show tooltips on hover for all settings
11. **Verify:** "Reset All" / "Reset This Page" buttons visible (red/orange)
12. **Verify:** Companion Stance is a dropdown (5 options)
13. **Verify:** Cling Type has 4 options (no CBT)
14. **Verify:** Combat tab settings are mostly dropdowns
15. **Verify:** Only 4 tabs: Party/Friend, Distance/Following, Combat/AI, Misc
16. **Verify:** Fren Name has party member dropdown button next to it
17. **Verify:** Account alias editable at top of left panel
18. **Verify:** No crashes or errors in `/xllog`
19. **Verify:** Config persists after close/reopen (check plugin config directory for `*_FrenRider.json`)

**Known Issues:**
- Mount selection is still free-text (full mount list from game data planned for future phase)
- Food item name is still free-text (full item search from game data planned for future phase)
- Custom idle list editor shows placeholder message (planned for future update)
- Plugin icon (3 guys on shoulders) not yet created
- Old Dalamud IPluginConfiguration (FrenRider.json) may conflict with new system on first run - delete old config if issues arise

**Research Notes:**
- VBM/BMR autorotation presets can be loaded via slash commands:
  - `/vbm ar set <preset>` for VanillaBossMod
  - `/bmr ar set <preset>` for BossModReborn
  - `/bmrai setpresetname <preset>` for BMR AI
  - This means we CAN inject presets from the plugin via CommandManager
- IDtrBarEntry is in `Dalamud.Game.Gui.Dtr` namespace
- OnClick delegate takes `DtrInteractionEvent` parameter (not parameterless)
- IClientState.LocalPlayer is deprecated in favor of IObjectTable.LocalPlayer or IPlayerState

---

## Changelog Format

Each entry should include:

### [Version] - YYYY-MM-DD @ HH:MM AM/PM TZ

**Added:**
- New features or files

**Changed:**
- Modifications to existing functionality

**Fixed:**
- Bug fixes

**Removed:**
- Removed features or files

**Deprecated:**
- Soon-to-be removed features

**Security:**
- Security-related changes

**Files Modified:**
- List of files changed with brief description

**Implementation Details:**
- Technical details of what was implemented
- Why certain approaches were chosen
- Any deviations from plan

**Testing Performed:**
- What was tested
- Results of testing
- Any issues found

**Testing Required (User):**
- Specific things user should test
- Expected behavior
- How to verify functionality

**Known Issues:**
- Any bugs or limitations discovered
- Workarounds if available

**Performance Impact:**
- FPS impact (if measurable)
- Memory usage changes
- CPU usage notes

---

## Version Numbering

**Format:** MAJOR.MINOR.PATCH

- **MAJOR:** Incompatible API changes or complete rewrites
- **MINOR:** New functionality in backwards-compatible manner
- **PATCH:** Backwards-compatible bug fixes

**Development Phases:**
- 0.0.x - Phase 0 (Documentation)
- 0.1.x - Phase 1 (Basic Structure)
- 0.2.x - Phase 2 (Configuration)
- 0.3.x - Phase 3 (Party Detection)
- 0.4.x - Phase 4 (Following System)
- 0.5.x - Phase 5 (Mount System)
- 0.6.x - Phase 6 (Combat Integration)
- 0.7.x - Phase 7 (Zone Logic)
- 0.8.x - Phase 8 (Automation)
- 0.9.x - Phase 9 (Formation - Optional)
- 0.10.x - Phase 10 (Polish)
- 1.0.0 - First stable release
- 1.x.x - Phase 11+ (Advanced Features)

---

## Backup Policy

Before editing any file:
1. Create timestamped backup in `/backups/` folder
2. Format: `filename_YYYYMMDD_HHMMSS.ext`
3. Keep last 10 backups per file
4. Backups are gitignored

Example:
- Original: `Plugin.cs`
- Backup: `backups/Plugin_20260228_031900.cs`

---

*This changelog will be updated with every change to the project.*
