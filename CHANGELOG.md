# Fren Rider - Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Phase 11 - Food Eating & Chocobo Summoning

#### [0.10.0] - 2026-02-28

**Added:**
- **Food eating automation** (AutomationService + GameHelpers):
  - Checks Well Fed buff (status ID 48) remaining time every 10 seconds
  - If buff is below 90 seconds, automatically uses configured food item from inventory
  - Resolves food item name to ID via known food list (fast) or Lumina game data (fallback)
  - Food search: if configured food runs out and `FeedMeSearch` is enabled, scans inventory for best alternative from priority list (Orange Juice → Mate Cookie)
  - Uses `InventoryManager.GetInventoryItemCount()` for inventory checks
  - Uses `ActionManager.UseAction(ActionType.Item, itemId)` for item usage
  - Uses `StatusManager` on player character for buff checking
  - Only eats when alive, not in combat, and not simultaneously in duty + combat
  - Food item ID is cached and re-resolved on zone change or config change
- **Chocobo companion summoning** (AutomationService + GameHelpers):
  - When `ForceGysahl` is enabled, checks every 15 seconds if companion needs summoning
  - Uses Gysahl Greens (item ID 4868) via `ActionManager.UseAction`
  - Checks companion timer via `UIState.Buddy.CompanionInfo.TimeLeft`
  - Only summons when: not mounted, not in duty, not in sanctuary, buddy time < 900s (15 min)
  - Sanctuary detection via `ActionManager.GetActionStatus(GeneralAction, 9)` (Mount action availability)
  - Deferred companion stance setting: sends `/cac "stance"` command 3 seconds after summoning
  - Supports all stances: Free Stance, Defender, Attacker, Healer, Follow
- **GameHelpers.cs** - New static unsafe helper class:
  - `GetInventoryItemCount(uint itemId)` - NQ + HQ inventory count
  - `GetStatusTimeRemaining(uint statusId)` - player buff remaining time
  - `UseItem(uint itemId)` - use item via ActionManager with status check
  - `GetBuddyTimeRemaining()` - companion chocobo timer
  - `IsInSanctuary()` - sanctuary detection
  - `LookupFoodItemId(string name)` - Lumina item name → ID lookup
  - `FindBestAvailableFood()` - scan inventory for best food from priority list
  - `IsPlayerAlive()` - HP > 0 check
- **MainWindow**: Food status and companion status display with color-coded indicators
- **MountService**: Updated `SummonCompanion()` to use `GameHelpers.UseItem()` instead of non-existent `/gysahlgreens` command

**Changed:**
- Food check interval reduced from 60s to 10s for faster buff refresh
- Companion summoning moved from MountService to AutomationService for proper lifecycle management
- AutomationService now exposes `FoodStatus` and `CompanionStatus` properties for UI display
- Added `InvalidateFoodCache()` method for config change handling

### Phase 10.1 - Bug Fixes

#### [0.9.1] - 2026-02-28

**Fixed:**
- Account separation: Enhanced account identification and fallback handling
  - `ConfigManager.EnsureAccountSelected()` uses `PlayerState.ContentId` to uniquely identify accounts
  - Added fallback handling for contentId=0 cases (uses first account or creates new one)
  - Account IDs are hex-formatted content IDs for proper separation
  - Migration logic handles existing single-account configs automatically
  - Added detailed logging for account selection debugging
- Mount logic: Fixed "Fly You Fools" behavior and command syntax
  - **CRITICAL FIX**: Changed to proper `/mount "Mount Name"` syntax (case-sensitive, with quotes)
  - **CRITICAL FIX**: Changed from `ICommandManager.ProcessCommand()` to `UIModule.ProcessChatBoxEntry()` to send commands directly to game
  - Dismount when fren dismounts (if FlyYouFools enabled) using `/mount` toggle
  - Mount when fren mounts (if FlyYouFools enabled and not in combat)
  - **Pillion riding fixed**: Now uses `ITargetManager.Target` to directly set target instead of `/target` command
  - Pillion riding: Finds fren in ObjectTable and sets as target, then sends `/ridepillion <t> 2`
  - Added proper condition checks for combat state
  - Fixed cooldown display to show remaining time
  - Added detailed logging for mount commands and targeting
  - Cooldown no longer blocks state updates (only command execution)
  - Mount Roulette fallback: uses "Company Chocobo" since true roulette requires plugin support
- **Flying follow**: When mounted and fren is flying, sends jump command (`/gaction jump`) to initiate flight
  - **FIXED**: Changed from `/hold SPACE` to `/gaction jump` (proper FFXIV general action command)
  - **FIXED**: Only sends jump when NOT already flying (checks `InFlight` condition) to prevent spam
  - **FIXED**: Increased cooldown from 100ms to 1000ms (1 second) to prevent command spam
  - **FIXED**: Uses `/vnav flyto` when PLAYER is flying (not just when fren is flying)
  - **FIXED**: Increased distance threshold for flying navigation from 1.0 to 5.0 yalms
  - **CRITICAL**: Checks player's `InFlight` condition to determine navigation mode
  - When player is airborne: uses `/vnav flyto` with 5.0 yalm threshold for smooth flying
  - When player is on ground: uses normal navigation with 1.0 yalm threshold
  - Prevents getting stuck in air after fren lands (continues using flyto until player lands)
  - Reduces navigation command spam for smoother flying movement at full speed
  - Jump command only sent when mounted, fren flying, and player not already flying
- DTR bar: Restored toggle behavior (toggles enabled state, not window)
- UI: Mount search field now stays fixed at top while scrolling mount list

**Added:**
- GitHub Actions workflow for automated releases (`.github/workflows/build-release.yml`)
- Plugin repository manifest (`repo.json`) for Dalamud custom repo support
- Comprehensive README.md with installation instructions and feature list

**Changed:**
- `Plugin` - Added `ITargetManager` service for direct targeting
- `Plugin.OnLogin()` - Added detailed logging for ContentId and account selection
- `ConfigManager.EnsureAccountSelected()` - Enhanced with fallback logic and better logging
- `MountService.Update()` - Rewritten mount/dismount logic with better cooldown handling and pillion support
- `MountService.MountSelf()` - **Changed to use `ITargetManager.Target` for pillion riding instead of `/target` command**
- `MountService.MountSelf()` - Uses `/mount "Mount Name"` (proper FFXIV syntax) for Fly You Fools mode
- `MountService.DismountSelf()` - Uses `/mount` to toggle dismount
- `MountService.SendCommand()` - **CRITICAL: Changed from `ICommandManager.ProcessCommand()` to `UIModule.ProcessChatBoxEntry()` to send commands directly to game**
- `FollowService.Update()` - **CRITICAL: Fixed flying follow logic to prevent spam and work correctly**
  - Added `InFlight` condition check to only send jump when player is NOT already flying
  - Increased jump cooldown from 100ms to 1000ms to prevent command spam
  - Jump command only sent when: mounted AND fren flying AND not already flying AND cooldown expired
- `FollowService.NavigateToPosition()` - **CRITICAL: Use /vnav flyto when PLAYER is flying**
  - Checks player's `InFlight` condition flag to determine navigation mode
  - When player is flying: uses `/vnav flyto` with 5.0 yalm distance threshold
  - When player is on ground: uses normal navigation with 1.0 yalm distance threshold
  - Prevents getting stuck in air when fren lands (continues flyto until player lands)
  - Larger threshold for flying reduces command spam and allows smoother movement at full speed
- `FollowService.SendCommand()` - Enhanced to try plugin commands first, then fall back to UIModule for game commands
- `FrenTracker.FrenState` - Added `IsFlying` property to detect when fren is flying
- `FrenTracker.FindFren()` - Added flying detection based on Y position comparison
- `Plugin.SetupDtrBar()` - DTR bar OnClick toggles `cfg.Enabled` state
- `ConfigWindow` - Mount selector now uses BeginChild for scrollable list with fixed search

**Build Results:**
- 0 errors, 0 warnings

**Testing Required:**
1. Check /xllog for mount commands: should see `/mount "Company Chocobo"` or `/mount "Mount Name"`
2. Verify FlyYouFools ON: mount when fren mounts, dismount when fren dismounts
3. **Verify FlyYouFools OFF**: should ride pillion when fren mounts
   - Check logs for "Targeted fren: [Name]" message
   - Should see `/ridepillion <t> 2` command
   - Character should actually target fren and ride pillion
4. **Verify flying follow**: When mounted and fren flies, should see `/gaction jump` in logs and character should take flight
5. Click DTR bar entry → should toggle plugin enabled state (FR: On/Off)
6. Mount search field should stay visible while scrolling mount list
7. Check /xllog for ContentId values when logging in with different accounts

---

### Phase 10 - Polish & Optimization

#### [0.9.0] - 2026-02-28

**Added:**
- `Plugin.SpamLog()` debug helper: verbose logging gated by `SpamPrinter` config (0=off, 1=on)
  - Outputs `[SPAM]` prefixed messages at Debug level only when enabled
- MainWindow enhancements:
  - Idle status display (blue text, shows last idle action performed)
  - Formation slot display (purple text, shows assigned slot number)
  - FATE indicator in zone info line (shows FATE ID when in a FATE)
  - Zone extra info consolidated (indoor + FATE in one line)

**Changed:**
- `Plugin.cs` - Added SpamLog method
- `MainWindow.cs` - Added idle, formation, and FATE display sections

**Build Results:**
- 0 errors, 0 warnings

**Testing Required:**
1. Enable SpamPrinter → verbose debug messages appear in /xllog
2. Disable SpamPrinter → no spam messages
3. MainWindow shows idle status when standing near fren
4. MainWindow shows formation slot when Formation enabled
5. FATE ID appears in zone info when in a FATE

---

### Phase 9 - Formation System

#### [0.8.0] - 2026-02-28

**Added:**
- `FormationService` - 8-slot formation grid system:
  - 8 predefined position offsets (behind fren, fanning out left/right in two rows)
  - Auto-assigns party slot based on party index (excluding fren)
  - Calculates world-space formation target from fren's position
  - Activates when `config.Formation = true`
- FollowService formation integration:
  - When formation active, navigates to assigned formation position instead of fren directly
  - 1.5y threshold for "in position" detection
  - StateDetail shows formation slot number and distance
  - Refactored `NavigateToFren` → `NavigateToPosition` for reuse

**Changed:**
- `FollowService.cs` - Formation target override, extracted `NavigateToPosition` method
- `Plugin.cs` - Creates FormationService, calls Update in framework loop

**Build Results:**
- 0 errors, 0 warnings

**Files Created:**
- `FrenRider/Services/FormationService.cs`

**Files Modified:**
- `FrenRider/Services/FollowService.cs` - Formation integration
- `FrenRider/Plugin.cs` - Service wiring

**Testing Required:**
1. Enable Formation toggle → party members navigate to grid positions
2. Disable Formation → reverts to normal cling following
3. Formation slot assignment changes with party composition
4. StateDetail shows correct slot number

---

### Phase 8 - Automation Features

#### [0.7.0] - 2026-02-28

**Added:**
- `AutomationService` - Idle action and QoL automation:
  - Idle action system: performs emotes/actions when standing idle near fren
  - Tick counter: waits `IdleTicksBeforeAction` ticks before first idle action
  - Two idle modes: specific action (`IdleAction`) or rotating list
  - List modes: default built-in list (8 emotes) or custom user list
  - 30-second minimum between idle actions to prevent spam
  - Food consumption check framework (60s interval, Well Fed buff check stub)
  - Repair trigger method (self-repair via `/generalaction "Repair"`, NPC stub)
  - Zone transition reset clears idle state
  - Skips idle when in combat or mounted
- Default idle emote list: /tomescroll, /doze, /sit, /think, /lookout, /stretch, /box, /pushups

**Changed:**
- `Plugin.cs` - Creates AutomationService, calls Update in framework loop

**Build Results:**
- 0 errors, 0 warnings

**Files Created:**
- `FrenRider/Services/AutomationService.cs`

**Files Modified:**
- `FrenRider/Plugin.cs` - Service wiring

**Testing Required:**
1. Stand idle near fren → idle action triggers after configured tick count
2. IdleActionMode=0 uses specific action, =1 uses list
3. Idle actions don't fire during combat or while mounted
4. Zone change resets idle counter
5. Check /xllog for idle action messages

---

### Phase 7 - Zone-Specific Logic

#### [0.6.0] - 2026-02-28

**Added:**
- FATE detection via FFXIVClientStructs `FateManager`:
  - `ZoneService.InFate` / `CurrentFateId` properties
  - Detects FATE join/leave via `FateManager.FateJoined` and `GetCurrentFateId()`
  - Logs FATE entry/exit events
- `ZoneService.ZoneChanged` flag for territory transition detection
  - Fires once per zone change, resets next frame
  - Logs old → new territory ID
- Zone transition reset in FollowService and CombatService:
  - Stops navigation, deactivates rotation, resets state on zone change
  - Clears social distancing offset and nav target
- FDistance integration: `config.FDistance` added to effective cling distance when in a FATE
- FATE preset selection: CombatService uses `AutoRotationTypeFATE` when `InFate` is true

**Changed:**
- `ZoneService.cs` - Added FATE detection, zone transition tracking, FFXIVClientStructs FateManager import
- `FollowService.cs` - Zone transition reset, FDistance in FATE cling calculation
- `CombatService.cs` - Zone transition reset, FATE preset selection

**Build Results:**
- 0 errors, 0 warnings

**Testing Required:**
1. Enter a FATE → InFate=true, cling distance increases by FDistance
2. Leave a FATE → InFate=false, cling returns to normal
3. Zone change (teleport/duty) → all services reset cleanly
4. Combat in FATE uses AutoRotationTypeFATE preset
5. Check /xllog for zone change and FATE join/leave messages

---

### Phase 6 - Combat System Integration

#### [0.5.0] - 2026-02-28

**Added:**
- `CombatService` - Combat state machine with 4 states (OutOfCombat, EnteringCombat, InCombat, LeavingCombat)
  - Detects combat via `ConditionFlag.InCombat`
  - Auto-activates rotation plugin on combat enter, deactivates on leave
  - Supports 4 rotation plugins: BMR (`/bmrai`), VBM (`/vbmai`), RSR (`/rotation`), WRATH (`/wrath`)
  - Zone-aware preset selection: general vs DD vs FATE presets
  - Foray-specific rotation plugin selection (`RotationPluginForay` config)
  - BossMod AI toggle on combat enter/leave
  - Positional settings (Front/Rear/Any/Auto) sent to RSR/WRATH
  - LB automation stub (threshold checking framework, actual HP check TBD)
  - 2s cooldown between rotation toggle commands
- MainWindow combat state display:
  - Color-coded: red=in combat, orange=entering, grey=leaving
  - Shows active rotation plugin and preset name

**Changed:**
- `Plugin.cs` - Creates CombatService, calls Update in framework loop
- `MainWindow.cs` - Added combat state section

**Build Results:**
- 0 errors, 0 warnings

**Files Created:**
- `FrenRider/Services/CombatService.cs`

**Files Modified:**
- `FrenRider/Plugin.cs` - Service wiring
- `FrenRider/Windows/MainWindow.cs` - Combat display

**Testing Required:**
1. Enter combat → rotation plugin activates with correct preset
2. Leave combat → rotation plugin deactivates
3. BossMod AI toggles on/off with combat
4. Different presets load for DD vs overworld
5. Foray zones use RotationPluginForay setting
6. Check /xllog for rotation commands and any warnings

---

### Phase 5 - Mount System Integration

#### [0.4.0] - 2026-02-28

**Added:**
- `MountService` - Mount state machine with 5 states (Idle, WaitingToMount, Mounting, Mounted, Dismounting)
  - Detects fren mount state via FFXIVClientStructs unsafe `Character.Mount.MountId`
  - Auto-mounts when fren mounts (uses FoolFlier config for mount name)
  - Auto-dismounts when fren dismounts
  - Mount Roulette support via `/mountroulette`
  - Named mount via `/mount "Name"` command
  - 2s mount / 1.5s dismount cooldown to prevent action spam
  - Gysahl Green companion summoning with stance selection (stub for auto-trigger)
- FrenTracker mount data: `IsMounted` and `MountId` on both `FrenState` and `PartyMemberState`
  - Read via unsafe FFXIVClientStructs `Character` struct at runtime
  - Detects mount ID for all visible party members and fren
- MainWindow mount state display:
  - Color-coded: teal=mounted, yellow=mounting, orange=dismounting
  - Shows fren's mount ID when fren is mounted but self is idle

**Changed:**
- `FrenTracker.cs` - Added FFXIVClientStructs import, unsafe mount detection in ScanParty and FindFren
- `Plugin.cs` - Creates MountService, calls Update in framework loop
- `MainWindow.cs` - Added mount state section with color coding

**Technical Notes:**
- `ConditionFlag.InFlight` (not `Flying`) for flight detection
- `ConditionFlag.Mounted` + `ConditionFlag.Mounting71` for self mount state
- `Character.Mount.MountId` (ushort) - non-zero when mounted
- `Character.IsMounted()` helper in FFXIVClientStructs
- `/mount` toggles mount/dismount, `/mount "Name"` mounts specific mount
- Pillion riding (multi-seat mount sharing) requires game interaction system - future enhancement

**Build Results:**
- 0 errors, 0 warnings

**Files Created:**
- `FrenRider/Services/MountService.cs`

**Files Modified:**
- `FrenRider/Services/FrenTracker.cs` - Mount detection
- `FrenRider/Plugin.cs` - Service wiring
- `FrenRider/Windows/MainWindow.cs` - Mount display

**Testing Required:**
1. Fren mounts → plugin auto-mounts configured mount (or roulette)
2. Fren dismounts → plugin auto-dismounts
3. MainWindow shows mount state with correct colors
4. Mount cooldown prevents rapid mount/dismount spam
5. FlyYouFools toggle controls own-mount vs pillion behavior
6. Verify no crashes from unsafe FFXIVClientStructs access

---

### Phase 4 - Basic Following System

#### [0.3.0] - 2026-02-28

**Added:**
- `FollowService` - Core following state machine with 5 states:
  - Idle: disabled / no fren / fren not visible
  - Following: actively navigating to fren via configured nav plugin
  - InRange: within cling distance, navigation stopped
  - TooFar: beyond max distance, navigation stopped
  - InCombat: combat detected, follow paused based on FollowInCombat config
- `ZoneService` - Zone type detection using condition flags + territory ID sets:
  - Overworld, Duty, DeepDungeon, Foray detection
  - Indoor/outdoor classification
  - Deep dungeon IDs: PotD, HoH, Eureka Orthos
  - Foray IDs: Eureka zones, Bozja, Zadnor
- Navigation command dispatch via `ICommandManager.ProcessCommand()`:
  - VNavmesh: `/vnav moveto X Y Z` / `/vnav stop`
  - Visland: `/visland moveto X Y Z` / `/visland stop`
  - BossMod Follow: `/bmr follow`
  - Vanilla Follow: `/follow` (no explicit stop)
- Cling distance logic:
  - Base cling from config + DD extra distance when in deep dungeons
  - Social distancing added to effective cling distance
- Max distance enforcement:
  - Standard max distance for overworld/duty
  - Foray-specific max distance for Eureka/Bozja
- Social distancing:
  - Random X/Z offset regenerated every 5 seconds for natural movement
  - Only active outdoors (or indoors if config allows)
  - Offset persists between ticks to avoid jitter
- Navigation re-issue threshold: only sends new command if target moved >1y
- MainWindow now shows:
  - Follow state with color coding (blue=following, green=in range, orange=too far, red=combat)
  - State detail text (distance, cling, max info)
  - Zone type and territory ID

**Changed:**
- `Plugin.cs` - Creates ZoneService + FollowService, calls Update in framework loop
- `MainWindow.cs` - Added follow state and zone info display

**Build Results:**
- 0 errors, 0 warnings

**Files Created:**
- `FrenRider/Services/ZoneService.cs`
- `FrenRider/Services/FollowService.cs`

**Files Modified:**
- `FrenRider/Plugin.cs` - Service wiring
- `FrenRider/Windows/MainWindow.cs` - Status display

**Testing Required:**
1. Enable plugin with fren in party → Follow state changes from Idle to Following/InRange
2. Walk away from fren → state changes to Following, nav commands sent
3. Walk back close → state changes to InRange, nav stops
4. Walk very far → state changes to TooFar
5. Enter combat → state changes to InCombat (if FollowInCombat=No)
6. Enter a duty → zone type shows Duty, uses ClingTypeDuty
7. Check /xllog for nav commands and any warnings
8. Verify VNavmesh `/vnav moveto` commands work when VNavmesh is installed

---

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

**Known Issues / Future Enhancements:**
- Plugin icon (3 guys on shoulders concept) - **TODO: Requires image file creation**
  - Format: PNG (64x64 or 128x128)
  - Location: `FrenRider/icon.png`
  - Once created, add to `.csproj` as Content and reference in `FrenRider.json`
- Mount selection uses searchable dropdown (✅ IMPLEMENTED via Lumina mount data)
- Food item name is still free-text (full item search from game data planned for future phase)
- Custom idle list editor shows placeholder message (planned for future update)

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
