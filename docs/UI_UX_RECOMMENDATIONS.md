# Fren Rider UI/UX Recommendations

**Review date:** 2026-08-18  
**Scope:** UI code review only; no runtime behaviour or implementation changes are included in this document.

## Product goal

Help a multibox operator choose a Fren, understand follow/automation ownership, and recover quickly when party, mount, combat, ADS, or exit state is not ready.

## Reviewed surfaces

- `FrenRider/Windows/MainWindow.cs`
- `FrenRider/Windows/ConfigWindow.cs`
- `FrenRider/Windows/AutoDutyWarningWindow.cs`
- `FrenRider/Windows/MagiaMiniWindow.cs`

## What is already working

- The main window exposes party, follow, mount, combat, cleanup, food, repair, companion, duty, and exit state in one place.
- Status colours, warning strips, account/character profiles, and screenshot-safe Krangle controls are already established.
- The settings window has meaningful task groupings and a reusable setup model rather than a flat configuration dump.

## Prioritized recommendations

| Priority | Recommendation | Rationale and completion signal |
| --- | --- | --- |
| P0 | Turn the top bar into one clear run-state control. | Replace the small `Run` checkbox plus status pill with a prominent Enable/Disable action, current state, and a short reason when it cannot run. |
| P0 | Give an unconfigured Fren a direct empty-state action. | When no Fren is selected, show `Choose from party` beside the warning and carry the operator directly to the relevant profile control. |
| P0 | Resolve the AutoDuty/ADS warning language. | The UI currently mixes an AutoDuty conflict warning with ADS and AutoDuty backend concepts. State exactly which plugin owns the duty, why a conflict exists, and what `Disable AutoDuty` will change. |
| P1 | Collapse the operational status stack by urgency. | Keep Fren, follow, combat, and the active blocker visible; move healthy food, repair, desynth, companion, and raw authority fields into `More details`. |
| P1 | Make profile scope and bulk actions unmistakable. | Keep `Default config` versus active character in a sticky scope header. Rename `Everything Sync` and `Full Tab Sync` to describe source and destination, then preview affected profiles before applying. |
| P1 | Separate everyday settings from expert controls. | Within each tab, show recommended controls first and collapse hacks, debug logging, raw ADS authority, icon codes, and test actions under an explicit Advanced section. |
| P2 | Add feedback to MAGIA commands. | Show the last command sent and a brief success/pending state so Attack, Defense, and Off do not feel like fire-and-forget buttons. |

## Suggested information hierarchy

1. Header: run state, selected Fren, Settings
2. Blockers and next action
3. Live follow/party summary
4. Collapsed automation details
5. Advanced diagnostics

## Validation checklist

- A new user can identify the primary action and current blocker within five seconds.
- Every disabled control has a nearby plain-language reason and, when possible, a direct corrective action.
- Healthy, warning, error, running, and disabled states remain distinguishable without colour.
- The UI remains usable at narrow window widths and common Dalamud UI scales without clipped labels or unreachable controls.
- Destructive, global, or high-impact actions identify their scope and require confirmation or provide a safe undo.
- Empty, loading, stale-data, success, partial-success, and failure states each provide an appropriate next action.
- Settings clearly identify whether they apply globally, per account, per character, per preset, or only for the current session.
- Advanced diagnostics are still reachable but do not compete with the everyday workflow.

## Recommended implementation order

1. Implement P0 items and validate the primary workflow plus blocker recovery.
2. Implement P1 information-architecture and configuration improvements.
3. Apply P2 polish, then test at multiple UI scales with both fresh and mature configurations.
