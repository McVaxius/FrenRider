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
/fr debug
```

`/frenrider` always opens the main window. `/fr` opens the main window when no recognized argument is supplied. `/fr debug` toggles debug controls for the active character config.

The main window shows run state, configured fren, party status, follow/mount/idle/teleport/respawn state, ADS state, exit mode, and debug details when enabled.

## Core Features

- Party/fren tracking from the current party list, with quick-select in settings.
- Invite whitelist. Whitelisted invites can be accepted when not already in a party, and the inviter can become the configured fren.
- Follow modes: NavMesh, Visland, BossMod Follow, and Vanilla Follow.
- Separate overworld and duty follow modes, cling distance, max distance, foray distance, deep-dungeon distance, and combat-follow behavior.
- Social spacing with configurable X/Z wiggle and indoor behavior.
- Formation following for party-slot based positioning.
- Mount and flying behavior, including Fly You Fools, own-mount chase, far-chase recovery, and vnavmesh flying-stuck recovery through ECommons keypress support.
- Combat integration for BMR, VBM, RotationSolver Reborn, and Wrath, with packaged BossMod presets.
- FATE sync, food use/search, companion summon/stance, idle commands, auto-discard, repair, and desynthesis helpers.
- DTR controls with text/icon modes.
- Optional video notifications when enabling or disabling Fren Rider.
- Optional Krangle mode to obfuscate character names in UI.

## Duty And ADS Automation

Fren Rider can hand duties to ADS when ADS is loaded, the current duty is in the mirrored ADS catalog, the duty family is enabled, and the duty maturity meets the configured threshold.

Duty families are Solo, 4-Man, 8-Man, Alliance, Guild Hest, Deep Dungeon, Treasure Dungeon, and Other. Each family has its own enable flag and maturity threshold.

When ADS handoff is active, Fren Rider pauses local follow, combat, duty interaction, maintenance, generic dialog handling, and exit behavior until ADS releases or the configured exit mode takes over.

Repair and desynthesis are delegated to ADS utility IPC. Repair modes are disabled, self, NPC no-inn, and NPC no-inn/no-teleport. Auto desynthesis waits for duty completion and duty exit, requests ADS configured desynthesis, retries rejected starts every 30 seconds, and times out after 180 seconds.

Exit behavior supports:

- FrenRider exit after N seconds from duty completion.
- Leave when all other party members have left the duty zone.
- ADS exit method, which sends `/ads leave` after the configured delay.
- No auto-exit, which prevents Fren Rider from initiating a duty leave.

## AutoYes And Respawn

AutoYes handles selected dialogs while Fren Rider is enabled. It can accept raise offers, teleport offers, and party invites. Party invites are primarily handled by the whitelist system, with AutoYes as backup.

Respawn is separate from AutoYes. The respawn feature is named **Respawn after death outside duties after** in settings. When enabled, Fren Rider waits until the character has been continuously `Unconscious` outside duties for the configured delay, opens the Return prompt, and accepts it.

Respawn blocks while ADS utility is active, while area transition is active, and while `BoundByDuty` or `BoundByDuty56` is true. Treasure-map deaths can be timing-sensitive because game duty state may appear or disappear around death, Return, and portal/duty transitions.

## Normal Game Behavior

FFXIV treats death Return differently depending on context. A normal overworld Return should not leave a party by itself.

If the character is dead while `BoundByDuty`, `BoundByDuty56`, or treasure-map duty state is still active, accepting the Return prompt can remove the character from the party without a separate "Leave party?" confirmation. This is game behavior. Plugins or external tools can make it happen by accepting the Return prompt, even when they did not explicitly choose a leave-party option.

When investigating unexpected party drops, also check external auto-Return tools such as Automaton InstantReturn.

## Configuration And Storage

Global UI settings are stored by Dalamud in the normal Fren Rider plugin config.

Per-account Fren Rider config files are stored in the plugin config directory as:

```text
{AccountId}_FrenRider.json
```

`AccountId` normally comes from the FFXIV content ID. Each account file contains a default config plus per-character configs keyed as `Character Name@World`.

## Integrations

Common integrations used by code paths:

- **vnavmesh**: NavMesh pathing, flying, and path-state checks.
- **Visland**: alternative movement backend.
- **BossMod/BossMod Reborn**: BossMod Follow and packaged autorotation presets.
- **RotationSolver Reborn** and **Wrath**: combat automation modes.
- **ADS**: duty handoff, repair, desynthesis, and ADS exit method.
- **Lifestream**: optional teleport-to-fren support when the fren is out of zone.
- **AutoRetainer**: auto-discard support through `/ays discard`.
- **YesAlready**: paused while Fren Rider is enabled so Fren Rider can own its selected dialogs.
- **AutoDuty**: detected and warned about; Fren Rider can send `/xldisableplugin AutoDuty` from its warning/defaults flow.

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
