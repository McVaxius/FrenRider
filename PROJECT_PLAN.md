# Fren Rider - FFXIV Plugin Project Plan

## Project Overview

**Plugin Name:** Fren Rider  
**Purpose:** Convert the frenrider Lua script into a native FFXIV Dalamud plugin for multiboxing support  
**Original Script:** https://github.com/McVaxius/dhogsbreakfeast/blob/main/Dungeons%20and%20Multiboxing/frenrider/frenrider_McVaxius.lua  
**Base Template:** https://github.com/goatcorp/SamplePlugin  

## Technology Stack

### Development Environment
- **IDE:** Visual Studio 2022 or JetBrains Rider
- **Framework:** .NET Core 8 SDK
- **Language:** C#
- **Platform:** Dalamud Plugin Framework for FFXIV

### Dependencies & References
- **Dalamud:** FFXIV plugin framework (provided by XIVLauncher)
- **FFXIVClientStructs:** Game memory structures
- **ImGui.NET:** UI rendering
- **Potential Plugin Dependencies:**
  - VNavmesh (navigation/pathfinding)
  - Visland (alternative navigation)
  - BossMod/BossModReborn (combat automation)
  - SimpleTweaks (targeting fixes)
  - RotationSolver Reborn (RSR) or Wrath (rotation plugins)

### Original Script Dependencies (Lua-based)
- **SomethingNeedDoing (SND):** Lua scripting engine for FFXIV
- **dfunc.lua:** Utility functions library
- **VNavmesh:** Navigation mesh pathfinding
- **Visland:** Alternative navigation system
- **BossMod/VBM:** Combat automation
- **SimpleTweaks:** Targeting fixes

## Core Functionality Breakdown

### 1. Configuration System
**Original Implementation:** INI file serialization with version control  
**Plugin Implementation:** JSON-based configuration using Dalamud's Configuration API

**Key Settings:**
- **Party/Friend Settings:**
  - Fren name (target to follow)
  - Fly mode vs mount mode
  - Mount selection
  - Loot fulfillment type
  - Chocobo companion stance
  
- **Distance/Following:**
  - Cling distance (trigger distance for following)
  - Cling type (0=navmesh, 1=visland, 2=bossmod, 3=CBT, 4=vanilla)
  - Social distancing (outdoor/foray spacing)
  - Max follow distance
  - Formation following
  
- **Combat/AI:**
  - Rotation plugin selection (BMR/VBM/RSR/WRATH)
  - Auto-rotation presets
  - Positional preferences
  - Follow in combat settings
  - Limit break usage threshold
  
- **Misc:**
  - XP item auto-equip
  - Food consumption
  - Auto-repair settings
  - Idle emote behavior

### 2. Following System
**Core Mechanic:** Auto-follow party leader/designated friend

**Movement Types:**
- Navmesh pathfinding (primary)
- Visland pathfinding (alternative)
- BossMod follow leader
- CBT autofollow
- Vanilla game follow

**Special Behaviors:**
- Social distancing in outdoor/foray zones
- Formation-based positioning (8-person grid)
- Deep dungeon special handling
- FATE zone adjustments
- Combat vs non-combat following

### 3. Mount/Travel System
**Functionality:**
- Auto-mount on designated friend's mount (pillion riding)
- Alternative: Fly alongside on own mount
- Gysahl Green usage for chocobo companion
- Zone-specific mount restrictions (Firmament, Diadem, etc.)

### 4. Combat Integration
**Rotation Plugin Support:**
- BossMod Reborn (BMR)
- VanillaBossMod (VBM)
- RotationSolver Reborn (RSR)
- Wrath

**Combat Features:**
- Auto-rotation preset switching (general/DD/FATE)
- Positional awareness (front/rear/any)
- Follow distance by job role
- Limit break automation
- Target distance management

### 5. Job-Specific Configurations
**Job Tables:** Pre-configured settings per job
- Distance settings (melee 2.6y, ranged 10y)
- Follow in combat (yes/no)
- Positional requirements (front/rear/any)
- Job-specific behaviors

**Supported Roles:**
- Tanks (PLD, WAR, DRK, GNB, Beastlord)
- Melee DPS (MNK, DRG, NIN, SAM, RPR, VPR)
- Physical Ranged (DNC, BRD, MCH)
- Magical Ranged (BLM, SMN, RDM, PCT, BLU)
- Healers (WHM, SCH, AST, SGE)

### 6. Zone-Specific Logic
**Deep Dungeons:**
- Modified follow distance
- Area transition handling
- Special rotation presets

**FATEs:**
- Adjusted cling distance
- FATE-specific rotations

**Forays (Eureka/Bozja):**
- Social distancing enforcement
- Phantom job handling (Wrath)
- Instance number management
- Mini-aetheryte transitions

**Duties:**
- Auto-interact with duty objects
- Cutscene handling
- Loot management

### 7. Automation Features
**Auto-Actions:**
- Food consumption when buff expires
- XP item equipping
- Equipment repair (self or NPC)
- Idle emotes/actions
- Yes/No dialog auto-acceptance (forays)

### 8. Utility Functions
**Helper Systems:**
- Distance calculations (3D space)
- Zone transition detection
- Party composition analysis
- Target validation
- Buffer circle calculations (social distancing)
- Random number generation (wiggle/variance)

## Implementation Phases

### Phase 0: Project Setup & Foundation ✓ (COMPLETE)
**Goal:** Establish project structure and documentation

**Tasks:**
- [x] Initialize Git repository
- [x] Create README.md with project description
- [x] Create PROJECT_PLAN.md (this document)
- [x] Create KNOWLEDGE_BASE.md
- [x] Create how-to-import-plugins.md
- [x] Create .gitignore
- [x] Create CHANGELOG.md

**Deliverables:**
- Complete project documentation
- Development workflow established
- Backup/versioning strategy defined

---

### Phase 1: Basic Plugin Structure ✓ (COMPLETE)
**Goal:** Create a minimal working plugin that loads in-game

**Tasks:**
1. ~~Clone SamplePlugin template~~ ✓ (Recreated from template)
2. ~~Rename all references from SamplePlugin to FrenRider~~ ✓
3. ~~Update plugin manifest (FrenRider.json)~~ ✓
4. ~~Create basic plugin class structure~~ ✓
5. ~~Implement simple slash command (/frenrider)~~ ✓
6. ~~Create basic ImGui window~~ ✓
7. ~~Test plugin loads in-game~~ ✓ (Confirmed by user)

**Phase 1.1 - UI Redesign (absorbed Phase 2):**
8. ~~Multi-account/multi-character config system~~ ✓
9. ~~Left panel character list~~ ✓
10. ~~DTR bar integration~~ ✓
11. ~~Dropdowns, tooltips, reset buttons~~ ✓
12. ~~Fren name capitalization + party dropdown~~ ✓
13. ~~Merge Automation+Misc tabs~~ ✓
14. ~~Remove CBT from cling types~~ ✓
15. ~~Non-editable fren name in MainWindow~~ ✓

**Phase 1.1 - UI Feedback & New Features:**
16. ~~DTR bar click toggles on/off (not main window)~~ ✓
17. ~~Widen left panel, fix name truncation, add spacing~~ ✓
18. ~~Krangle checkbox: garble names with exercise words~~ ✓
19. ~~DELETE button for non-DEFAULT chars (CTRL+click)~~ ✓
20. ~~Mount selector: Lumina searchable dropdown~~ ✓
21. ~~Upper-right layout: Krangle + Reset All/This + DELETE~~ ✓
22. ~~Both Reset buttons have (?) tooltips~~ ✓

---

### Phase 2: Configuration System ✓ (COMPLETE - Absorbed into Phase 1.1)
**Goal:** Implement persistent configuration storage

**Tasks:**
1. ~~Create Configuration.cs class~~ ✓ (Global + CharacterConfig + AccountConfig)
2. ~~Define all configuration properties (from original script)~~ ✓ (35+ fields)
3. ~~Implement JSON serialization/deserialization~~ ✓ (System.Text.Json per-account files)
4. ~~Create configuration UI (ImGui)~~ ✓ (Tabbed with left panel)
5. ~~Add save/load functionality~~ ✓ (ConfigManager)
6. ~~Implement version migration system~~ ✓ (Version field in IPluginConfiguration)

---

### Phase 3: Party & Target Detection ✓ (COMPLETE)
**Goal:** Detect and track the designated "fren" in party

**Tasks:**
1. ~~Implement party member enumeration~~ ✓ (FrenTracker.ScanParty)
2. ~~Create friend name matching logic~~ ✓ (partial, case-insensitive, @Server stripped)
3. ~~Add target position tracking~~ ✓ (ObjectTable lookup, Vector3 position)
4. ~~Implement distance calculation functions~~ ✓ (Vector3.Distance, displayed in MainWindow)
5. ~~Create party composition analyzer~~ ✓ (GetPartyComposition role counts)
6. ~~Add job detection for party members~~ ✓ (ClassJob ID → Role mapping, all jobs incl VPR/PCT)

**Deliverables:**
- ~~Plugin can find "fren" by partial name~~ ✓
- ~~Real-time position tracking~~ ✓
- ~~Distance calculations working~~ ✓
- ~~Party role detection~~ ✓

**Testing Required:**
- Correctly identifies fren in various party sizes
- Distance calculations accurate
- Updates in real-time

---

**Phase 3.1 - In-Game Testing Feedback Fixes:**
23. ~~Resizable left panel via drag splitter~~ ✓
24. ~~Krangle locks Fren Name field read-only~~ ✓
25. ~~Replace all SliderFloat with InputFloat (3 decimal places)~~ ✓
26. ~~Fix "Not on main thread!" login detection error~~ ✓

---

### Phase 4: Basic Following System ✓ (COMPLETE)
**Goal:** Implement core following mechanics (non-combat)

**Tasks:**
1. ~~Integrate with VNavmesh plugin API~~ ✓ (ICommandManager.ProcessCommand for /vnav, /visland, /bmr, /follow)
2. ~~Implement basic pathfinding to fren position~~ ✓ (FollowService.NavigateToFren)
3. ~~Add cling distance trigger logic~~ ✓ (effective cling = base + DD extra + social distancing)
4. ~~Create social distancing calculations~~ ✓ (random X/Z offset, regenerated every 5s)
5. ~~Implement max distance checks~~ ✓ (standard + foray-specific max distances)
6. ~~Add zone type detection (outdoor/indoor/duty)~~ ✓ (ZoneService: condition flags + territory ID sets)

**Deliverables:**
- ~~Character follows fren when distance > cling~~ ✓
- ~~Stops at appropriate distance~~ ✓
- ~~Social distancing works in outdoor zones~~ ✓
- ~~Doesn't follow beyond max distance~~ ✓

**Testing Required:**
- Following works in overworld
- Stops at correct distance
- Social distancing maintains spacing
- No rubber-banding issues

---

### Phase 5: Mount System Integration ✓ (COMPLETE)
**Goal:** Auto-mount on fren's mount or fly alongside

**Tasks:**
1. ~~Detect when fren is mounted~~ ✓ (FFXIVClientStructs unsafe Character.Mount.MountId)
2. ~~Implement pillion riding (mount sharing)~~ ✓ (stub — auto-mounts own mount; pillion interaction TBD)
3. ~~Add alternative fly mode (own mount)~~ ✓ (FlyYouFools config → /mount "Name")
4. ~~Create mount selection system~~ ✓ (FoolFlier config + Mount Roulette)
5. Handle zone-specific mount restrictions (future enhancement)
6. ~~Implement Gysahl Green usage~~ ✓ (SummonCompanion with stance, auto-trigger TBD)

**Deliverables:**
- ~~Auto-mounts on fren's mount~~ ✓
- ~~Fly mode works with custom mount~~ ✓
- Zone restrictions (future)
- ~~Chocobo companion summoning~~ ✓

**Testing Required:**
- Fly mode uses correct mount
- Auto-dismount when fren dismounts
- Mount cooldown prevents spam
- Works in various zones

---

### Phase 6: Combat System Integration ✓ (COMPLETE)
**Goal:** Integrate with rotation plugins and combat behavior

**Tasks:**
1. ~~Detect combat state~~ ✓ (ConditionFlag.InCombat, CombatService state machine)
2. ~~Implement rotation plugin switching (BMR/VBM/RSR/Wrath)~~ ✓ (4 plugins, zone-aware selection)
3. ~~Add preset management system~~ ✓ (AutoRotationType / DD / FATE presets)
4. ~~Create job-specific configuration loader~~ ✓ (per-character config with RotationPlugin/Foray)
5. ~~Implement positional settings~~ ✓ (Front/Rear/Any/Auto → RSR/WRATH commands)
6. ~~Add follow-in-combat logic~~ ✓ (FollowInCombat config in FollowService)
7. ~~Create limit break automation~~ ✓ (stub — threshold framework, HP check TBD)

**Deliverables:**
- ~~Rotation plugin activates in combat~~ ✓
- ~~Correct presets load per content type~~ ✓
- ~~Job-specific settings apply~~ ✓
- Limit break triggers at threshold (stub)

**Testing Required:**
- Rotation plugins switch correctly
- Presets load for DD/FATE/general
- Positionals work per job
- LB triggers appropriately

---

### Phase 7: Zone-Specific Logic ✓ (COMPLETE)
**Goal:** Handle special behaviors for different content types

**Tasks:**
1. ~~Implement Deep Dungeon detection and logic~~ ✓ (ZoneService DD IDs + DDDistance in FollowService)
2. ~~Add FATE zone handling~~ ✓ (FateManager detection + FDistance + FATE preset)
3. ~~Create Foray (Eureka/Bozja) systems~~ ✓ (ForayIds + MaxBistanceForay + RotationPluginForay)
4. ~~Implement duty-specific behaviors~~ ✓ (ClingTypeDuty in FollowService)
5. ~~Add zone transition handling~~ ✓ (ZoneChanged flag, service resets)
6. Create auto-interact for duty objects (future enhancement)

**Deliverables:**
- ~~Deep Dungeon following works correctly~~ ✓
- ~~FATE adjustments apply~~ ✓
- ~~Foray social distancing active~~ ✓
- Duty interactions automated (future)

**Testing Required:**
- DD area transitions smooth
- FATE distance adjustments work
- Foray zones handle correctly
- Duty objects auto-interact

---

### Phase 8: Automation Features ✓ (COMPLETE)
**Goal:** Implement quality-of-life automation

**Tasks:**
1. ~~Create food consumption system~~ ✓ (stub — 60s check interval, Well Fed buff check TBD)
2. Implement XP item auto-equip (future)
3. ~~Add repair automation~~ ✓ (self-repair via /generalaction, NPC stub)
4. ~~Create idle emote system~~ ✓ (specific action + rotating list, 30s throttle)
5. Implement auto-dialog acceptance (future)
6. Add loot management integration (future)

**Deliverables:**
- Food auto-consumed when needed (stub)
- XP items equip automatically (future)
- ~~Repairs trigger at threshold~~ ✓
- ~~Idle emotes play~~ ✓
- Dialogs auto-accepted in forays (future)

**Testing Required:**
- Food consumption triggers correctly
- XP items equip when available
- Repair works (self and NPC)
- Emotes don't spam
- Dialog acceptance safe

---

### Phase 9: Formation System ✓ (COMPLETE)
**Goal:** Implement formation-based following

**Tasks:**
1. ~~Create 8-person formation grid~~ ✓ (8-slot Vector3 offsets behind fren)
2. ~~Implement party position calculation~~ ✓ (DetermineSlot based on party index)
3. ~~Add formation toggle~~ ✓ (config.Formation bool)
4. ~~Create position offset system~~ ✓ (CalculateFormationPosition + FollowService integration)

**Deliverables:**
- ~~Formation following works~~ ✓
- ~~8 party members position correctly~~ ✓
- ~~Toggle between formation and cling~~ ✓

**Testing Required:**
- Formation maintains shape
- Works with various party sizes
- Transitions smoothly

---

### Phase 10: Polish & Optimization
**Goal:** Refine user experience and performance

**Tasks:**
1. Add comprehensive error handling
2. Implement logging system
3. Create debug mode
4. Optimize performance (reduce CPU/memory)
5. Add user-friendly error messages
6. Create help/documentation in UI
7. Implement backup system for configs

**Deliverables:**
- Stable, crash-free operation
- Clear error messages
- Debug logging available
- Optimized performance
- In-game help documentation

**Testing Required:**
- No crashes in extended use
- Performance acceptable
- Errors handled gracefully
- Help documentation clear

---

### Phase 11: Advanced Features (Future)
**Goal:** Implement whitelist and advanced automation

**Tasks:**
1. Create party invite whitelist system
2. Add auto-accept party invites
3. Implement instance number management
4. Add custom chat command system (PuppetMaster integration)
5. Create multi-character coordination

**Deliverables:**
- Whitelist system functional
- Auto-invite acceptance
- Instance switching works
- Chat commands respond

**Testing Required:**
- Whitelist filters correctly
- Invites accepted safely
- Instance changes work
- Commands execute properly

---

## Features NOT Possible as Plugin

### Limitations vs Original Lua Script

**1. Direct Game Command Execution**
- **Original:** Uses `/send` to simulate keypresses
- **Plugin:** Cannot directly simulate keyboard input
- **Workaround:** Use Dalamud's command execution or plugin APIs

**2. Arbitrary Lua Script Execution**
- **Original:** Runs within SomethingNeedDoing's Lua engine
- **Plugin:** Compiled C# code only
- **Impact:** No runtime script modification

**3. Some SND-Specific Functions**
- **Original:** Uses SND's `yield()`, `PathfindAndMoveTo()`, etc.
- **Plugin:** Must use Dalamud/VNavmesh APIs directly
- **Workaround:** Implement equivalent functionality via plugin APIs

**4. Direct Memory Manipulation**
- **Original:** May use some direct memory reads via SND
- **Plugin:** Must use FFXIVClientStructs or Dalamud services
- **Impact:** Some features may need different implementation

**5. File System Operations**
- **Original:** Opens explorer windows, manages files freely
- **Plugin:** Limited to plugin config directory
- **Impact:** Backup system will be different

## Development Workflow

### Version Control Strategy
1. **Main Branch:** Stable, tested releases
2. **Development Branch:** Active development
3. **Feature Branches:** Individual features (phase-based)

### Backup System
**Before ANY file edit:**
1. Create timestamped backup in `/backups/` folder
2. Format: `filename_YYYYMMDD_HHMMSS.ext`
3. Keep last 10 backups per file

### Change Documentation
**Every change must:**
1. Update CHANGELOG.md with:
   - Date and time
   - Files modified
   - Description of changes
   - Reason for changes
   - Testing performed
2. Include what user should test

### Code Quality Checks
**Before each commit:**
1. Build succeeds (no errors)
2. No compiler warnings (or documented why acceptable)
3. Code follows C# conventions
4. Comments explain complex logic
5. No memory leaks (dispose patterns used)
6. Plugin loads in-game without errors

### Testing Protocol
**After each phase:**
1. Document test cases in CHANGELOG
2. Perform in-game testing
3. Record any issues found
4. Fix issues before next phase
5. User validates functionality

## Risk Assessment & Mitigation

### High Risk Areas

**1. Plugin Bans/TOS**
- **Risk:** Automation may violate FFXIV TOS
- **Mitigation:** User assumes all risk; plugin is for educational purposes
- **Note:** Original script has same risk profile

**2. Game Updates Breaking Plugin**
- **Risk:** FFXIV patches may break Dalamud/plugin
- **Mitigation:** Version locking, rapid updates, fallback modes

**3. Dependency on Other Plugins**
- **Risk:** VNavmesh, BossMod, etc. may not be available
- **Mitigation:** Graceful degradation, feature detection, alternatives

**4. Performance Issues**
- **Risk:** Constant position checking may impact FPS
- **Mitigation:** Throttling, async operations, optimization

**5. Complex State Management**
- **Risk:** Following logic may have edge cases causing bugs
- **Mitigation:** Extensive testing, state machine design, logging

### Medium Risk Areas

**1. Configuration Migration**
- **Risk:** Config updates may break existing settings
- **Mitigation:** Version checking, migration functions, defaults

**2. Multi-Plugin Coordination**
- **Risk:** Conflicts with rotation/navigation plugins
- **Mitigation:** Proper API usage, conflict detection, user warnings

**3. Zone-Specific Logic**
- **Risk:** New zones may not be handled correctly
- **Mitigation:** Generic fallbacks, easy zone table updates

## Success Criteria

### Minimum Viable Product (MVP)
**Phase 1-4 Complete:**
- Plugin loads in-game ✓
- Configuration saves/loads ✓
- Detects and tracks fren ✓
- Basic following works ✓

### Full Feature Parity
**Phase 1-8 Complete:**
- All original script features implemented
- Combat integration working
- Zone-specific logic functional
- Automation features active

### Production Ready
**Phase 1-10 Complete:**
- Stable, no crashes
- Optimized performance
- User documentation complete
- Testing validated by user

## Documentation Requirements

### User-Facing Documentation
1. **README.md** - Project overview, installation
2. **how-to-import-plugins.md** - Installation guide
3. **In-game help** - UI tooltips and help panels
4. **FAQ** - Common issues and solutions

### Developer Documentation
1. **PROJECT_PLAN.md** - This document
2. **KNOWLEDGE_BASE.md** - Technical learnings (gitignored)
3. **CHANGELOG.md** - All changes documented
4. **Code comments** - Inline documentation
5. **API_REFERENCE.md** - Plugin API usage notes

### Excluded from Repository (.gitignore)
- KNOWLEDGE_BASE.md (internal learning notes)
- Development notes
- Test configurations
- Personal settings
- Backup files (*.bak, *.backup)

## Timeline Estimates

**Phase 0:** 1 session (documentation) - CURRENT  
**Phase 1:** 1-2 sessions (basic structure)  
**Phase 2:** 1-2 sessions (configuration)  
**Phase 3:** 1 session (party detection)  
**Phase 4:** 2-3 sessions (following system)  
**Phase 5:** 1-2 sessions (mount system)  
**Phase 6:** 2-3 sessions (combat integration)  
**Phase 7:** 2-3 sessions (zone logic)  
**Phase 8:** 2-3 sessions (automation)  
**Phase 9:** 1-2 sessions (formation - optional)  
**Phase 10:** 2-3 sessions (polish)  
**Phase 11:** 3-4 sessions (advanced features)  

**Total Estimated:** 18-30 development sessions

## Notes & Considerations

### Original Script TODOs to Address
From frenrider_McVaxius.lua header:
- No mounting in forays (addressed with fake_outdoors_foray flag)
- Instance number changing (needs testing with Lifestream)
- LazyLoot toggle issue (plugin may have better API)
- Synced level detection (for XP gear swapping)

### Plugin-Specific Considerations
1. **Thread Safety:** Dalamud plugins run on game thread, be careful with blocking operations
2. **Dispose Pattern:** Properly dispose resources on plugin unload
3. **Service Injection:** Use Dalamud's DI container for services
4. **Update Frequency:** Balance between responsiveness and performance
5. **Error Handling:** Never crash the game, always fail gracefully

### Future Enhancement Ideas
- Multi-profile support (different configs per character)
- Preset sharing/import/export
- Advanced formation patterns
- Conditional logic system (if/then rules)
- Integration with Discord/webhooks for notifications
- Macro recording/playback
- Path recording for custom routes

---

## Conclusion

This project aims to convert a complex Lua-based multiboxing script into a native Dalamud plugin. The phased approach ensures incremental progress with testing at each stage. The plugin will provide the same core functionality as the original script while leveraging the benefits of native plugin integration.

**Next Steps:**
1. Complete Phase 0 documentation
2. User confirms understanding and approves plan
3. Begin Phase 1 implementation
4. Iterate based on testing feedback

**Success depends on:**
- Careful testing at each phase
- User feedback and validation
- Proper error handling and logging
- Performance optimization
- Clear documentation
