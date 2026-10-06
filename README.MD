# Fren Rider

Fren Rider is a Dalamud plugin for multibox/follow support in FFXIV. It tracks a configured party member, follows with multiple movement backends, manages mount/flying behavior, coordinates combat automation, handles selected dialogs, and can delegate duty work to ADS.

## Install

Add the Aethertek custom repository in Dalamud:

```text
https://aethertek.io/x.json
```

Then open the plugin installer and install **Fren Rider**.

Support development: https://ko-fi.com/mcvaxius  
Plugins and guides: https://aethertek.io/

## Quick Start

Open the main window:

```text
/frenrider
/fr
```

Common commands:

```text
/fr on
/fr off
/fr settings
/fr s
/fr mini
/fr m
/fr debug
```

`/frenrider` always opens the main window. `/fr` opens the main window when no recognized argument is supplied. `/fr settings` and `/fr s` toggle the settings window. `/fr mini` and `/fr m` toggle a separate auto-sized **MAGIA** window whose Attack, Defense, and Off buttons send `/magiaauto attack`, `/magiaauto defense`, and `/magiaauto off`. The mini window does not enable anything automatically. `/fr debug` toggles debug controls for the active character config.

The main window shows run state, a read-only operator profile, party status, follow/mount/idle/teleport/respawn/Phoenix Down state, ADS state, exit mode, and collapsed debug details. Account and character selection stays in Settings; browsing another profile does not change the active automation profile.

The header colour swatch and language selector apply to every Fren Rider window and popup. English, German, French, Spanish, Italian, Russian, Japanese, Korean, Simplified Chinese, Vietnamese, Brazilian Portuguese, Indonesian, Polish, and Turkish are included. Colours are calculated relative to the approved mint theme across panels, fields, borders, and foregrounds; warning and automation status colours keep their meaning. The native **C** checkbox selects compact spacing across the main, mini, Settings, and AutoDuty alert windows. Appearance preferences are also available in Settings under UI/About and save through the existing configuration.

The mini keeps the enable switch and exact saved fren identity available outside Eureka; its MAGIA Attack, Defense, and Off actions appear only in Eureka. Segoe UI and host-managed CJK/symbol fonts load through Dalamud's managed atlas and are never distributed with the plugin. Font readiness and missing glyphs are reported explicitly. The approved regular and compact layouts still require mcvaxius's in-game screenshot acceptance.

## Core Features

- Party/fren tracking from the current party list, with quick-select in settings.
- Invite whitelist. Whitelisted invites can be accepted when not already in a party, and the inviter can become the configured fren.
- Follow modes: NavMesh, Visland, BossMod Follow, and Vanilla Follow.
- Separate overworld and duty follow modes, cling distance, max distance, foray distance, deep-dungeon distance, and combat-follow behavior.
- Social spacing with configurable X/Z wiggle and indoor behavior.
- Formation following for party-slot based positioning.
- Mount and flying behavior, including Fly You Fools, own-mount chase, far-chase recovery, and vnavmesh flying-stuck recovery through ECommons keypress support. Fly You Fools uses ground navigation in no-flight Forays but remains flight-enabled in the Diadem; nearby own mounts use the local ClientStructs mount state (excluding pillion) for dismount correction.
- Combat integration for BMR, VBM, RotationSolver Reborn, Wrath, and Daedalus, with packaged BossMod presets.
- Separate RotationSolver Reborn operating-mode and hostile-target choices, synchronized per character.
- FATE sync, food use/search, companion summon/stance, idle commands, auto-discard, repair, and desynthesis helpers.
- Optional per-character job-stone equip for level 30+ base classes, disabled by default.
- Optional MAGIA mini controls for attack, defense, and off commands.
- DTR controls with text/icon modes.
- Optional video notifications when enabling or disabling Fren Rider.
- Optional Krangle mode to obfuscate character names in UI.

## Duty And ADS Automation

Fren Rider can hand duties to ADS when ADS is loaded, ADS reports a complete `CurrentDuty` catalog row whose territory and Content Finder Condition match live GameMain, the ADS-reported duty family is enabled, and the ADS clearance meets the configured threshold. Missing, malformed, unknown, or mismatched duty metadata fails closed to Fren Rider's local duty logic; Fren Rider has no separate per-duty maturity table or pilot promotions.

Duty families are Solo, 4-Man, 8-Man, Alliance, Guild Hest, Deep Dungeon, Treasure Dungeon, and Other. Each family has its own enable flag, maturity threshold, and 2-300 second ADS handoff delay. Solo defaults to 10 seconds; every other family defaults to 2 seconds.

The handoff delay advances only while readiness remains continuous: the client is logged in, the local player is present and alive, the character is not `Unconscious`, no area transition is active, and neither `WatchingCutscene` nor `OccupiedInCutSceneEvent` is active. Losing readiness or changing duty identity restarts the full family delay. Combat does not block this countdown because some solo-duty openings enter combat before handoff. The ADS status shows the current blocker or remaining continuous-ready time without per-frame logging. The Praetorium must also satisfy its existing duty-timer/fallback start seam before Fren Rider asks ADS to take ownership.

ADS runtime duty ownership is authoritative, including manual ADS **Start Inside** and **Resume**. While handoff is pending, ADS owns/leaves the live duty, or configured exit takeover is active, Fren Rider pauses follow/navigation, far chase, formation, mount actions, combat-state changes, duty interaction, maintenance, and automatic dialogs. Duty completion can enable the configured FrenRider exit path without resuming those other systems. ADS **Stop** releases runtime ownership and allows FrenRider local duty logic to resume.

Repair and desynthesis are delegated to ADS utility IPC. Repair modes are disabled, self, NPC no-inn, and NPC no-inn/no-teleport. Auto desynthesis waits for duty completion and duty exit, requests ADS configured desynthesis, retries rejected starts every 30 seconds, and times out after 180 seconds.

Exit behavior supports:

- FrenRider exit after N seconds from duty completion.
- Leave when all other party members have left the duty zone.
- ADS exit method, which sends `/ads leave` after the configured delay.
- No auto-exit, which prevents Fren Rider from initiating a duty leave.

## Phoenix Down Recovery

The Profile tab has three per-character settings: **Use Phoenix Downs for recovery** (on), **Revive anyone within range outdoors** (on), and **Allow Phoenix Down use during combat** (off). Existing profiles missing these fields receive the same defaults; profile copies, synchronization, resets, and transfers include all three.

Recovery supports regular four-player dungeons identified from live duty metadata and outdoor areas. Other instances are excluded. Party members take priority, with dead healers first. Outdoor strangers are eligible only when the outdoor setting is on and they are within 15 yalms; Fren Rider never approaches them. A living healer, including a non-party healer, at or within 20 yalms of the corpse blocks Phoenix Down use. Dead healers do not block it.

Party recovery pauses follow, formation, far chase, mounting, duty objectives, and competing automatic movement. A rescuer with an item can approach a visible party corpse through vnavmesh and stop within 14.5 yalms. Grounded rescuers dismount; flying rescuers wait to land. With combat use off, rescuers wait to leave combat before approaching or attempting an item. Survivors can finish the current fight, while new pulls stay blocked. Inventory, native availability, shared medicine cooldown, range, line of sight, pending Raise, and observed casts are checked before each attempt. Party clients stagger attempts by 500 milliseconds per rank in sorted party Content IDs; an empty inventory on one survivor does not block another.

The main window's **Phoenix Down** row reports waiting, approach, cast, and compatibility blockers. Actual casting and revival are tracked separately. Rejected or interrupted attempts wait at least one second before rechecking. Return is deferred for a dead party member while a living party rescuer remains, including while items are missing or on cooldown. Full party wipes retain the existing respawn behavior. Recovery releases its temporary controls after revival, disabling the feature, logout, character changes, transitions, and unload.

ADS must support `ADS.IsPhoenixDownRecoveryHoldActive` before Fren Rider approaches or uses an item in an ADS-owned dungeon. Fren Rider publishes `FrenRider.PhoenixDown.ShouldPauseDutyProgression`; ADS stops its movement once, retains ownership, and replans from fresh state after recovery. Missing acknowledgement support appears as a compatibility blocker. No automatic Stop/Resume cycle is required; explicit ADS Stop or Leave remains authoritative.

Offline checks cannot confirm native item execution. Live acceptance requires an actual Phoenix Down cast, revival, and ADS resumption; mcvaxius controls live testing and deployment.

## AutoYes And Respawn

AutoYes handles selected dialogs while Fren Rider is enabled. It can accept raise offers, teleport offers, and party invites. Party invites are primarily handled by the whitelist system, with AutoYes as backup.

Respawn is separate from AutoYes. Settings provide independent **Respawn after death outside duties after** and **Respawn after death inside duties after** toggles and delays. When the selected setting is enabled, Fren Rider waits until the character has been continuously `Unconscious` in that scope for its configured delay, opens the Return prompt, and accepts it. Both settings are disabled by default.

Respawn blocks while ADS utility is active or while area transition is active. The service selects inside-duty versus outside-duty scope only from `BoundByDuty` (condition 34); `BoundByDuty56` is not used for this respawn decision. Duty transitions reset the unconscious timer and notification recovery. Treasure-map deaths can be timing-sensitive because game duty state may appear or disappear around death, Return, and portal/duty transitions.

## Normal Game Behavior

FFXIV treats death Return differently depending on context. A normal overworld Return should not leave a party by itself.

If the character is dead while `BoundByDuty`, `BoundByDuty56`, or treasure-map duty state is still active, accepting the Return prompt can remove the character from the party without a separate "Leave party?" confirmation. This is game behavior. Plugins or external tools can make it happen by accepting the Return prompt, even when they did not explicitly choose a leave-party option.

When investigating unexpected party drops, also check external auto-Return tools such as Automaton InstantReturn.

## Configuration And Storage

Global UI settings are stored by Dalamud in the normal Fren Rider plugin config.

Companion HealRider requires no FrenRider profile setup. At grounded pickup readiness, Companion temporarily enables a clone of the active profile, targets the assigned Helper, and uses pillion riding with close following. Combat settings and normal enable/disable effects remain active. The optional `forceTemporary: true` field on `FrenRider.Dad.ApplyProfile` installs the existing in-memory overlay regardless of the saved Off/Temporary/Permanent acceptance setting, without saving or changing that setting. Companion retains the exact owner tuple and calls `FrenRider.Dad.ReleaseTemporaryProfile` on arrival, cancellation, timeout, stop, session loss, duty handoff, or disposal to reveal the saved profile. Other owners cannot replace or release it. Existing callers that omit `forceTemporary` retain acceptance-policy behavior.

**Don't move while casting** is a global setting that defaults on. Fren Rider applies the stored value when the plugin loads and whenever the checkbox changes: BMR and VBM receive their existing Action Tweaks setting, while RotationSolver Reborn receives `PoslockCasting true|false` through typed IPC. Existing global v1 configurations enable the setting during their one-time v2 migration; a later v2 opt-out remains off.

While Fren Rider has control outside combat, three localized target range/line-of-sight errors within five seconds against the same living hostile target, with movement limited to 0.5 yalms, temporarily release casting movement locks confirmed on in BMR, VBM, and RSR. Only those settings are restored when combat begins or five seconds pass; further errors do not extend the deadline. Disable, character/zone transitions, unload, and control transfer also restore them. The casting checkbox overrides pending recovery. Recovery does not start during ADS ownership, pending handoff, utility activity, Hyper Focus, Questionable or Coppelia control, or unreadable ADS ownership in a duty.

The main window has large red **All FR off** and green **All FR on** buttons beneath the top bar, above the scrolling content. They remain available while FrenRider is disabled and are disabled only when no account is loaded. These controls, also available as compact buttons beside the enabled checkbox in **DEFAULT CONFIG**, immediately set the default and all local characters in the current account, including the active temporary profile. The main window's **Run** checkbox still controls only the current character. Other settings and remote profiles are preserved.

RotationSolver Reborn configuration keeps Auto, Manual, None, and Support under **RSR Operating Mode**. **RSR Aggro Type** independently exposes all five upstream hostile-target choices and applies the selected value through RSR settings IPC. Existing per-character value `4` from the former combined operating-mode list migrates once to Auto plus Previously Engaged Targets.

**Equip job stone for current class** is a per-character Automation setting and defaults off. While Fren Rider is enabled, it acts only on a level 30+ base class when equipment can be changed safely and the character has a valid current gearset. Fren Rider checks the Soul Crystal armoury chest in slot order, equips the first stone whose job belongs to the current class, confirms the equipped slot, and updates that gearset once. A character already on a job is never scanned; Arcanist uses whichever matching Summoner or Scholar stone is encountered first.

BossMod preset activation is provider-specific: VBM selections receive only the VBM preset command, while BMR and non-VBM rotation selections use BMR for BossMod AI and receive only the BMR preset command. **Push Presets Now** installs the packaged presets and applies the current selection through the same routing.

Daedalus is rotation option 4 and is controlled through its typed `Daedalus.IsEnabled` and `Daedalus.SetEnabled` IPC. When Daedalus is the effective normal or Foray rotation, **Daedalus Engage Mode** can send None, Focus, Split, or Kill Adds through Daedalus LAN coordination. Focus requires a living enemy hard target; failures leave Daedalus unchanged. Fren Rider disables Daedalus during conflicting rotation, mounted, Questionable, and Coppelia ownership. **Restore snapshot** returns Daedalus to the enabled state captured before Fren Rider changed it; **Turn everything off** disables it.

Daedalus custom repository URL:

```text
https://raw.githubusercontent.com/ofnature/Daedalus/main/repo.json
```

While Fren Rider is enabled, it warns immediately and every five seconds if both loaded internal plugins `BossModReborn` (BMR) and `BossMod` (VBM) are detected. The warning is informational: Fren Rider does not disable either plugin or block combat.

Per-account Fren Rider config files are stored in the plugin config directory as:

```text
{AccountId}_FrenRider.json
```

`AccountId` is XIVLauncher's raw `launcherConfigV3.json` `CurrentAccountId`. Each launcher account receives its own file, containing a default config plus per-character configs keyed as `Character Name@World`; the FFXIV content ID remains character/session identity only.

## Integrations

Common integrations used by code paths:

- **vnavmesh**: NavMesh pathing, flying, and path-state checks.
- **Visland**: alternative movement backend.
- **BossMod/BossMod Reborn**: BossMod Follow and packaged autorotation presets.
- **RotationSolver Reborn**, **Wrath**, and **Daedalus**: combat automation modes.
- **ADS**: duty handoff, repair, desynthesis, and ADS exit method.
- **Lifestream**: optional teleport-to-fren support when the fren is out of zone.
- **AutoRetainer**: auto-discard support through `/ays discard`.
- **YesAlready**: paused while Fren Rider is enabled so Fren Rider can own its selected dialogs.
- **AutoDuty**: detected and warned about; only the explicit **Disable AutoDuty** action in Fren Rider's warning window can send `/xldisableplugin AutoDuty`.

## Troubleshooting

Use Dalamud logs. Useful search strings:

```text
[Respawn]
[AutoYes]
[ExitBehaviour]
Return to
BoundByDuty
BoundByDuty56
You leave ... party
Unconscious
[YES/NO] Clicked Yes
Automatically clicked Yes
InstantReturn
```

For party-drop investigations, line up the death/respawn line, Return prompt, `BoundByDuty` state, any `[Respawn]` or `[AutoYes]` acceptance line, external auto-Return logs, and the system party-leave message.

## Build

```bash
git clone https://github.com/McVaxius/FrenRider.git
cd FrenRider
dotnet build --configuration Release
```

## License

AGPL-3.0-or-later

## Credits

Original Lua script: https://github.com/McVaxius/dhogsbreakfeast/blob/main/Dungeons%20and%20Multiboxing/frenrider/frenrider_McVaxius.lua

Dalamud Plugin Framework: https://github.com/goatcorp/Dalamud
