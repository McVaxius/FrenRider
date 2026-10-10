2026-10-10 - Grouped settings layouts (I523)

- Organize all eight settings tabs into compact semantic groups, with two columns when they fit and whole groups stacked at narrower widths. Keep each ADS family on one aligned row and Chocobo supply targets in a stock/target/Buy grid. Measure profile actions against the visible pane and keep subsection alignment independent between tabs.
- Use the shared AethertekUI Appearance standard with inline colour choices and separate main-window, opacity and focus groups. Preserve every setting, conditional action, profile/sync/save behavior, native identity and release version; translate new captions across all existing catalogs.

2026-10-10 - Restore window scopes after drawing errors

- Always close Main's scrolling child, nested tables and party row IDs when drawing fails. Restore Main/Mini PreDraw styles even if title painting or opacity fails, preventing leaked scopes from causing a secondary titlebar error. Keep native Retry/error handling, titlebar controls, saved settings and release version unchanged; the supplied screenshot does not identify the original Draw error.

2026-10-10 - Compact defaults and main appearance controls (I521)

- Apply Compact on once, with main Compact and Transparency controls hidden. Keep density, transparency and independent visibility choices in Appearance settings; preserve later choices, opacity, automation and unknown saved fields. Advance the release from 2.0.0.6 to 2.0.0.7.

2026-10-09 - Tight compact list grids (I503/I509)


- Use adjacent compact rows in saved beasts and party status. Size party rows to their retained job icons and text; keep every stock/purchase/feed action.

2026-10-09 - Separate XA Slave log-tools shortcut (I512)

- Add Open XA Slave log tools beside the existing manual support exporter when XA Slave is loaded. The new action opens Utility > XA Mods only; preserve the Copy / ZIP button, its handler and cap warning. No automatic cleanup, provider loading or settings changes.

2026-10-09 - Companion food effect tooltips (I510)

- Explain each field food's effect and favorite-food distinction on hover in the selector, its choices and stock summary, purchase actions on Settings/Main/Mini, and the active-profile Feed action. Preserve existing stock targets, native IDs and manual purchase/feed behavior.

2026-10-09 - Manual Dalamud support log export (I506)

- Add Copy / ZIP Dalamud log and Open Export Folder to the existing settings/support interface. At 100 MiB or above, warn that logging may have stopped and recent activity may be missing; require another explicit click to export. Exports stay local and can be shared or removed manually. Preserve saved settings and release versions.

2026-10-08 - Dedicated Window appearance settings (I505)

- Move colour, language, compact mode and transparency controls into their own settings tab or sidebar page. Retain the existing controls, native IDs, saved preferences and actions; keep normal settings visible without an appearance block above them. Versions and client configuration are unchanged. Local build checks and game visual acceptance are recorded separately in the selected task checkpoint.

2026-10-08 - Effective rotation in Combat settings (I490)

- Show the active character's effective rotation for the current normal/Foray context. Clearly label DEFAULT CONFIG as a template and distinguish a full temporary DAD profile from the saved profile being edited. Keep existing rotation controls and combat behavior unchanged; the Questionable ADS/exit override does not select a rotation provider.

2026-10-08 - Manual purchase controls on Main and Mini (I496)

- Show Buy Greens, Buy Food and Stop purchasing on Main, with cart/food/stop icons on Mini inside and outside Eureka. Both windows show current NQ stock beside the corresponding action; tooltips identify the selected food, saved targets, Vath unlock requirement and current purchase status. Unknown stock displays a dash.
- Share the existing manual actions without automatic purchase triggers or additional settings. Preserve profile targets, window identities and localized captions. Native Account 1 retesting remains pending.

2026-10-08 - Manual companion purchasing without a gil-cap setting (I496)

- Remove the saved gil cap, travel checkbox and automatic restocking. Only BUY FOOD and BUY GREENS start one vendor trip and purchase the affordable deficit to the active profile's stock target; legacy zero caps no longer block a manual purchase. Authorize the exact expected cost through the existing ADS request while retaining price, stock, capacity, cancellation and uncertain-outcome checks.
- Move BUY FOOD, BUY GREENS and Stop purchasing to the top of Chocobo settings. Warn beside the food controls that the Vath vendor requires beast tribe progression through The Naming of Vath. Translate the warning and manual-purchase guidance in all fifteen existing catalogs. Native Account 1 purchasing remains pending verification.

2026-10-08 - ADS-owned Chocobo purchasing and travel (I496)

- Add separate per-profile automatic-restock and purchase-travel switches, both off by default, with existing clone/sync/reset controls. Allow zero stock targets and a zero gil cap to disable their purchase intent. Share one gil cap across greens and selected food in a captured restock cycle.
- Expose BUY FOOD and BUY GREENS in Chocobo settings and display the selected food's current NQ stock and target. Delegate vendor travel, shop opening, purchasing and cancellation to ADS IPC; FrenRider retains the profile policy and displayed purchase status.
- Require ADS's advertised purchase-travel capability and match the request identity, quantity, verified offer and exact stock before accepting completion. Keep local movement and companion-action holds after unreadable cancellation until the matching ADS operation is terminal with navigation released, without replaying the request.
- Use only verified city merchants or the unlocked Vath gil shop, with travel off unless explicitly enabled. Translate the reached controls and delegation statuses in the existing fifteen catalogs. ADS purchasing and travel still require runtime acceptance; no live purchase or deployment is claimed.

2026-10-08 - Bounded Chocobo gil-shop purchasing (I496)

- Add manual greens/selected-food stock purchasing from an already open supported city or unlocked Vath shop, with per-profile stock targets and a gil cap that default unset. Retain profile clone/sync/reset ownership and translate the new settings and statuses in the existing fifteen catalogs.
- Bind the loaded shop lifecycle to the active character/profile, territory, merchant and typed handler. Recheck the exact NQ offer, price, capacity and budget before dispatch; consume the request and its matching localized confirmation before native input. Accept only exact item/gil readback and a settled owned transaction. Cancel changed context and block an unresolved request in the same shop session without retry; the guard is instance-local.
- No automatic purchase, vendor opening/travel, food test trigger, onion use or client-file mutation is added. Local isolated compilation, 1,252 tests and the unchanged Debug/x64 batch pass without warnings/errors; live purchasing remains untested.

2026-10-08 - Guarded Chocobo field feeding (I496)

- Add a per-profile field-food selector and optional automatic feeding to the existing fifteen-second companion cadence. Feeding defaults off; Mimett Gourd is the selected default, preserving explicit saved choices. Retain manual Feed/Stop, current runtime-profile ownership, cloning and default-sync behavior; translate the new controls and displayed statuses in all fifteen existing languages.
- Require the summoned battle buddy to match the active character's native companion, with no current selected-food effect. Recheck exact NQ stock and ownership before self-targeted item use; accept feeding only after one item is deducted and its normal or favorite effect is observed on the owned native state. Cancel on context/profile/companion changes and stop uncertain or rejected attempts without automatic replay in the current plugin load.
- Preserve the separate read-only discovery selector and block simultaneous skill allocation/feeding. Field feeding is locally verified and has not been live-tested; onions, verified purchasing and optional travel remain unfinished.

2026-10-08 - Guarded Chocobo skill allocation (I496)

- Add manual skill allocation and optional automatic allocation in the existing fifteen-second companion check. Automatic allocation defaults off; priority defaults to Healer, Attacker, Defender, preserving saved custom choices. Wait for the first unfinished tree when its next skill is unaffordable and never respec learned skills.
- Use the current runtime profile and own a settled native Skills window. Invoke the enabled skill button's registered click listener and match the exact native-language skill/cost confirmation. Observe the selected learned level and exact skill-point deduction before advancing. Restore the original window/tab on release; cancel on unsafe context/profile changes and stop on rejection, partial results or missing acknowledgement without automatic replay until an explicit new selection or manual attempt.
- Preserve nullable/malformed priorities so they block spending instead of silently choosing another tree. Deep-copy the choices in profile cloning, default sync and reset. Add Chocobo settings access to Main and Main/Mini titlebars while retaining existing controls.
- Keep the saved reload-discovery selector read-only. One controlled FTP5 Healer9 acquisition is verified with the exact nine-point debit; generalized allocation and other native clients retain separate runtime acceptance. Feeding, onion use and purchasing remain unfinished.
- Reject an owned skill prompt during cleanup only while fresh character, safe context, progression and Skills-window ownership still match; logout, transitions and replaced windows receive no cleanup input.

2026-10-08 - GitHub Actions shared-library repair

- Build against published AethertekUI main so current shared APIs are available. Retain repository-specific read-only SSH deploy keys, which do not expire, and disabled credential persistence. Publish library APIs before consumer changes.

2026-10-08 - Automatic Companion discovery (I496)

- /fr testchocobo now opens the existing Chocobo tab with saved opt-in reload-test controls. Run one selected probe per plugin load after character registration; turning it off, Stop or unloading cancels without replay. Manual Run/Stop remain available, and the new global preference is independent of character/DAD profiles.
- Pair native Buddy agent Show/Hide for probe-owned windows and log bounded agent/window lifecycle state to diagnose missing-window requests without claiming unobserved acceptance.
- Add /fr testchocobo: one bounded automatic exploration of the native Companion window and all three tabs, logging progression, redacted text and structural control/event metadata through the existing plugin log. /fr testchocobo stop cancels the attempt.
- Preserve an existing window/tab when still owned, cancel on unsafe context or native-window changes, and never replay an open request. Discovery does not spend points, use food/onions or purchase items.
- The first live probe exposed unchanged tab selection despite changing TabIndex. Activate the native radio button with SetActive and require its observed selection to agree with the native tab before recording a tab or completing discovery.
- A later probe showed radio activation alone changes the highlight without changing the native tab. Pair the typed radio and Buddy SetTab calls, retaining rejected-dispatch cancellation and bounded content capture; native acceptance remains an observed result.
- For an existing ready Companion window with valid but differing tab/radio indexes, defer capture until the probe's first tab dispatch is accepted and restore its original native tab. Keep identity/window binding, invalid-selection and newly opened-window guards unchanged.
- Include visible, ready child addons owned by the Buddy addon control in bounded read-only discovery. Distinguish confirmed tab selection from complete content verification and leave unidentified number-array values labelled as raw slots.
- Bound child-name decoding to the native fixed field and capture control enabled flags plus field/training food metadata without dispatching skill, item or purchase actions.
- Include bounded read-only NQ stock and native self-target item availability for companion greens, field foods and onions. Availability is observational; it does not dispatch or prove consumption.

2026-10-07 - Chocobo inspection preparation (I496)

- Move the existing summon and stance controls into Settings → Chocobo, retaining saved fields and actions with matching tab sync/reset ownership.
- Show read-only native companion rank, stars, experience, unused skill points and Defender/Attacker/Healer levels for the active character. Add an explicit manual Companion-window request through the current Buddy agent API; request dispatch is separate from in-game acceptance.
- Skill spending, feeding, onion use and vendor purchases remain pending native-window verification. No new automatic action or purchasing travel is enabled.

2026-10-07 - Packaged image branding and operator guidance (I500/I497/I499)

- Use the existing packaged icon in Main and MAGIA Mini branding/titlebars with aspect-ratio fitting and a stable reserved box. Preserve native titlebar controls, saved geometry, motion and complete-window opacity. Copy the original icon beside direct and packaged DLLs as icon.png while retaining the nested images copy.
- Refresh concise README guidance for appearance, focus fade, titlebar shortcuts and this plugin's existing setup/automation controls.
- Probe the optional Hindi menu caption once per existing font generation. Disable only that choice with an ASCII caption when unavailable; retain selected-catalog checks and show explicit ASCII Hindi failure status with a saved Use English action (I499).

2026-10-07 - Questing combat and targeting guard (I498)

- While Questionable/WigglyQuest or Questionable Companion rotation, Hunt Logs, or Mass GC is active, pause FrenRider-owned VBM AI and runtime movement presets outside duties and combat. Resume eligible configured VBM on combat/duty entry or a readable automation stop, preserving explicit AI-off, existing safety holds, and newer external settings. BMR behavior is unchanged.
- Selected RSR uses Previously Engaged Targets outdoors and All Attackable Targets in duties during quest automation, including target-only QuestionableSolo handling. Preserve operating mode, saved profiles, matching DAD targeting priority, native overrides, and conditional lifecycle restoration. A confirmed run becoming unreadable retains the safe policy until a readable stop.

2026-10-07 - Button sizing (I491)

- Use font-aware Toolbar sizing for ordinary buttons and reduce the local-account All FR on/off action heights. Preserve complete labels and icons, native IDs/actions, full-width slots and small/dense controls.
- Current Debug/x64 compilation passes. Final actual-product native checks pass 9714 assertions across 32 focused scenes and 112 pointer activations, with integer exit 0 in all 2 routes. Coverage uses English/Hindi captions, original exercised font roles, both densities, 100/150 percent scale and enlarged text; game/GPU acceptance remains separate.

2026-10-07 - CJK atlas construction

- Request a 4096 x 4096 managed atlas and merge one bundled Noto CJK face per font role for the selected language, including Simplified and Traditional Chinese aliases. Preserve existing font sizes, glyph ranges, Windows and symbol fonts, and font lifecycle.

2026-10-07 - Native Main and Mini shortcuts (I489)

- Add fixed Settings, Mini, active-profile Run and local-account All FR on/off titlebar actions to Main, plus Main, Settings and active-profile Run on Mini. Recheck the effective profile/account when clicked and retain all body controls, the Fren selector and Eureka Magia actions.
- Reserve actual native-button and measured title widths before motion preparation; keep title painting clear of controls and available while collapsed without changing saved window identities.

2026-10-07 - DAD dungeon RSR targeting ownership (I465)

- Add matching-run acquire/release IPC for confirmed four-player dungeons. Keep All Attackable Targets effective through eligible FrenRider RSR activation and refresh without editing saved or temporary profiles or changing operating mode during acquisition.
- Capture the loaded provider, configuration and exact job's native targeting before a write. Require configured/effective readback before normal RSR activation, preserve active external overrides and newer intentional changes, and report unsupported or unconfirmed conditional restoration.

2026-10-07 - Manual BossMod selection and owned live settings (I470/I485)

- Populate general, deep-dungeon and FATE manual selectors from the selected live provider's complete preset catalog, retaining exact names, None and dormant profile fields. Replace a deleted active choice only after a readable catalog confirms it is missing, and preserve selections when no valid catalog is available.
- Honor manual BossMod choices with every rotation provider while preserving the saved force field. Remove unsupported RSR preset-setting forwards and use literal BossMod preset IPC with exact readback.
- Capture the original provider, character/profile, runtime presets, BMR saved AI selector and supported preferred distance before owned writes. Restore matching fields at session cleanup, preserving later external values and distinguishing none, force-disabled and ordered VBM selections; report unavailable or unconfirmed restoration.
- Read the native AI config through the provider's zero-argument generic config accessor. Support an originally unset BMR selector with a catalog-checked clear argument and confirmed null restoration. Confirm selector/runtime effects even when dispatch throws, preserve independent runtime or distance changes, and retain failed cleanup against the same provider until explicit recovery. Never send the departed provider's cleanup command to its replacement.
- Current focused ownership, interaction, combat-authority and duty lifecycle checks pass 359/359 with no failures or skips. The unchanged FrenRider launcher builds Debug/x64 with zero warnings/errors. Installed-provider and game acceptance remain user-controlled.
- Continue debounced combat-setting application while ADS owns navigation. Apply preset, AI, aggro, positional and target-mode edits independently, preserving combat suppression and saved profiles.
- Preserve startup holds and reapply eligible configured combat after an actual suppression ends. Read VBM automatic AI from its live Enabled setting and report departed-provider restoration failures without writing into a replacement instance.

2026-10-06 - Solo duty Return recovery (I483)

- Reuse the inside-duty Return opt-in with a five-second continuous delay only for a confirmed solo duty and complete native/HUD roster evidence. Grouped and outside-duty cases retain their saved delays.
- Recheck scope, identity and roster before Return, preserving Raise, Phoenix Down, utility and transition holds.

2026-10-06 - Own-mount FATE pause and Ignore FATEs (I469)

- Add optional FATE cling pause and Ignore FATEs settings, both off by default. Pause only begins from a verified own mount with Fly You Fools, safely lands/dismounts and holds normal follow/mount correction through the FATE and remaining combat; pillion always retires the hold without a passenger dismount.
- Ignore FATEs preserves ordinary travel and self-defence while skipping automatic sync and FATE preset overrides. Preserve saved sync, distance and preset choices, default/profile compatibility, and clear transient FATE ownership on character/profile/fren changes, disable, death, logout and transitions.

2026-10-06 - City and hub cling exclusions (I467)

- Add an editable territory exclusion list to Follow settings, seeded with current cities and secondary hubs. Preserve custom and empty lists through character/default saves, independent cloning, default synchronization and profile transfer; legacy profiles missing this field receive the seeded defaults.
- Pause owned cling, formation and same-territory chase in excluded areas without changing saved enablement, combat or mount policy. Keep Revenant's Toll and housing allowed by default, and keep seek, teleport and local aethernet travel available by releasing owned cling before travel starts.

2026-10-06 - Default-branch CI

- Run the existing artifact-only CI on master as well as the retained main branch, matching this repository's default branch. Preserve the fork-PR guard, build steps, dependency pin and version; Build and Release remains the sole publisher.

2026-10-06 - Actions dependency revision

- Pin both existing Actions library checkouts to published AethertekUI revision `6c193cf06ac67f954c549cafc2033ac0efdd630a`, which contains the Hindi text host used by the current source. Preserve the existing build/release routes; the last successful remote run predates this UI adoption.

2026-10-06 - Hindi text shaping

- Append हिन्दी after the fourteen existing language choices and embed all 904 Hindi catalog entries, including displayed service diagnostics. Use the shared Windows shaping renderer for retained text, captions, measurement, tooltips and editors, reserving natural line height for native controls and the Settings tab owner. Retain original control identities, font roles, field formats and steps, configuration and automation. Game-rendered DTR text remains English for Hindi; all other DTR locales and configured values retain their existing behavior. Debug x64 builds with zero warnings/errors; the focused native probe passes 4,833 assertions against identical product/checker core bytes, including catalog structure, placeholders, whitespace, newlines, original glyph-role sizes, controls, editors and scope restoration. Game, GPU, managed-font and IME acceptance remain separate.

2026-10-06 - Window appearance and transparency

- Move colour, compact and language controls into Window appearance in the existing UI Settings section, with independent main-header compact/language visibility and a transparency toggle. Remember normal opacity (100%) and automatic unfocused fade (50% after 10 seconds) through the existing configuration, applying complete-window opacity once after native motion restore on Main, Mini, Settings, warning and font status. Preserve active/editing account selection, Mini data, automation, fonts and native actions. Translate the eight new labels in all fourteen catalogs. The unchanged local launcher builds successfully with zero warnings and errors. Native appearance/persistence checks and game acceptance remain pending.

# Fren Rider - Changelog

## 2026-10-05 - Rounded outer window chrome

- Apply the shared theme's rounded outer corners around the existing native window lifecycle, retaining control identities, layout, saved geometry and actions.

## 2026-10-05 - Main reference spacing

- Align regular and compact Main card padding, section gaps, table columns and field text insets with their approved references. Keep translated content and retained extra rows accessible through native scrolling.
- Verify fourteen locales at 100%/150% with the original font roles, stable control IDs and unchanged Mini lifecycle. Preserve the existing first-use window size, automation, Phoenix Down recovery and version 1.4.0.1. Complete reference and in-game visual acceptance remain open.

## 2026-10-04 - Food and Settings text readability

- Translate the food-selection hint and empty placeholder while retaining raw game item names and the original selection IDs.
- Reserve translated Settings tab widths through native tab sizing and scroll overflowing tabs. Keep ADS family ready-delay and temporary-owner annotations on one translated line while paragraph diagnostics retain wrapping. Retain native window and column text clips for selectable rows so fractional font heights and glyph bearings do not cut off ink; native hit areas and actions remain unchanged.

## 2026-10-04 - AutoDuty warning sizing

- Establish the warning's scaled, measured width before native window sizing, and center its actual wrapped content when first shown or reopened after a density change.
- Verify 336 Mini lifecycle cases, 112 warning cases, 336 Settings tab cases, Main scrolling and appearance popups across fourteen locales, both densities and scales 1/1.5, using the original six font roles within the existing native process bounds. Local reference and text readability, managed-host and in-game acceptance remain separate.

## 2026-10-04 - Five additional UI languages

- Add complete Vietnamese, Brazilian Portuguese, Indonesian, Polish, and Turkish embedded catalogs, including Phoenix Down recovery, ADS handoff, settings help, and diagnostics. Append the five native language choices after the original nine without changing existing selection order, configuration version, or plugin version.
- Verify the Release build, 49 focused configuration/profile checks, all 895 compiled phrases in each of fourteen catalogs, and bounded native selector interactions with the original six font roles for every added language. Whole-window, managed-host, reference, and in-game visual acceptance remain separate.

## 2026-10-03 - Main and Mini display completion

- Restore the tracked fren's job/name details, party totals and role composition, retaining the original tracking colours and collapsed debug ID. Localize the restored party summaries across all nine languages.
- Match the reference's status badge, empty party slots and primary Mini Attack action. Place the colour swatch before Settings/Ko-fi and the language field afterward, retaining the appearance IDs. Retain the original minimum window size, measure translated party columns and Duty labels, and keep long rows reachable through native scrolling. Paint table-header translations once over transparent surfaces. Preserve all existing commands, saves and control IDs.

## 2026-10-03 - Mini auto-resize repair

- Establish Mini's required width before native window sizing to prevent empty narrow columns. Keep the toggle, saved fren and Eureka actions visible, and measure translated MAGIA labels at the active UI scale so all three actions stay on one row.

## 2026-10-03 - Readable settings fields

- Keep settings editors wide enough for their values and both native step buttons, including Duty/ADS/Exit seconds. Translate labels above fields without adding invisible English width; retain native IDs, values and increment actions.
- Fit every settings input, slider and combo through the shared measured-width helper, and retain combo popup scopes. Show the current assembly version in the main title without changing the saved window ID.

## 2026-10-03 - UI service-value preservation

- Keep already-formatted service values, IDs, leading zeroes, clock text, empty arguments and numbered placeholders intact during localization. Typed UI numbers/dates and authored status labels retain their selected-culture formatting and translations.


## 2026-10-02 - Build and release repair

- Pin GitHub builds to SDK 10.0.201 and pass the downloaded Dalamud library path. Restore and build plugin projects with matching configuration, platform and runtime; stop on restore failure.
- Keep build tokens read-only and release writes in a separate job. Use packaged manifest versions for untagged releases.
- Consolidate tag releases in build-release.yml and retain the read-only CI build.
- Local launchers build the plugin directly in the pinned environment and return its exit status.

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Added
- Adopted the approved regular and compact AethertekUI main and mini designs with plugin-owned vector branding, read-only Operator and Automation fields, native party/status tables, responsive groups, and collapsed diagnostics. Settings and the blocking AutoDuty alert retain their layouts with shared typography, theme, and necessary scrolling/wrapping.
- Added consumer-owned `UiLanguage`, `UiAccentRgb`, and `UiCompact` preferences without changing the configuration version. All fourteen languages use embedded keyed resources, selected-locale formatting, and stable native control/window IDs. Relative OKLCH colours update the whole decorative theme while preserving semantic status colours.
- Added managed Segoe UI weight roles with host-managed CJK and Windows symbol merges, explicit font readiness/glyph checks, and disposal. The mini retains saved fren identities outside the current party and shows MAGIA actions only in Eureka. Local build/source validation is separate from pending game visual acceptance.
- Retained the main Close action and companion timer/inventory fallback; narrow party tables scroll horizontally. Translated tabs, radios, and disclosure arrows respect disabled appearance.
- Added per-character Phoenix Down recovery (on), nearby outdoor stranger revival (on), and combat item use (off), including legacy defaults and all profile operations. Regular four-player dungeons and outdoor areas use item 4570 against confirmed corpses only when no living healer is within 20 yalms; party members and dead healers take priority.
- Party recovery holds progression and movement, approaches visible party corpses through vnavmesh to 14.5 yalms, checks inventory/native readiness/shared medicine cooldown/range/line of sight, and staggers clients by sorted party Content IDs. Combat-off recovery waits before approaching or using an item; other survivors retain current combat actions. Track actual casts separately from revival, defer Return while a living party rescuer remains, and release only recovery-owned controls during cleanup.
- Added `FrenRider.PhoenixDown.ShouldPauseDutyProgression` and the ADS recovery acknowledgement handshake. Missing ADS support blocks recovery approach/item use in owned dungeons. Offline verification covers recovery and profile behavior; live cast, revival, and ADS resumption remain pending mcvaxius-controlled testing.
- Added Beastmaster automatic Capture for eligible Combatant beasts closer than 10 yalms while FrenRider is enabled, including outside combat. **Try to catch beasts** defaults to on, with independent 1–100% HP thresholds of 90% for enemies more than five levels below and 40% for enemies within five levels below or equal. A threshold of 100% permits Capture on a full-health beast and may initiate combat. More distant targets are skipped before beast resolution. Higher-level and unresolved beasts, and models without any confirmed unowned candidate, are skipped; attempts respect native availability, cooldowns, combat authority, utility holds, and leases without changing another rotation plugin.
- Added a collapsed **Saved beasts** list in Combat settings. Confirmed native XBMPet unlocks refresh on Beastmaster after login/job changes, area changes, and combat exit, even with automatic Capture disabled. Catches stay in local character account data through settings copies, resets, and profile transfers, and remain browsable on other jobs.

### Changed
- Build only the plugin project in GitHub Actions so test and regression projects do not block production artifacts.

- Capture prefers an eligible current target, then scans nearby living Combatant beasts nearest first. It automatically selects a beast whose model has a confirmed unowned candidate only after HP, level, range, native action availability, cooldown, and existing hold checks pass, so manual targeting is no longer required.
- Capture resolves beasts by model alone, using BNpcBase for the target model and capturable flag. Shared models retain every distinct nonzero beast ID and permit attempts while any candidate is confirmed unowned; this can attempt an already-owned variety until all matching entries are confirmed owned. Ownership is recorded only after native unlock confirmation.
- Capture now accepts passive Combatant beasts without requiring hostile status, allowing Capture to open combat when the configured HP threshold permits it.
- Capture attempt spacing now uses the native calculated cooldown rather than the current recast timer's total, allowing a first attempt before that timer is initialized. Native availability and active-recast checks still gate each attempt.
- Manual `Z:\FrenRider.bat` Debug builds now target the plugin project directly, skipping the test project.
- Capture now writes one log line when it attempts the action, including the native wrapper's true/false return value. Attempt logging is always on; skipped checks remain quiet.
- Alphabetized the normal and Foray combat-plugin dropdowns while preserving saved selections.
- Accept actionable Raise offers immediately while FrenRider is enabled, ahead of ADS, combat, utility, respawn and generic dialog gates. Remove the unused Raise controls and setting; old configurations still load and drop the obsolete key on the next normal save. Suppress duplicate responses and defer Return while revival begins; record handling only after a successful click.

### Added
- Added owned DAD IPC for temporary Questionable/WigglyQuest Solo and 4-man ADS handoff settings: enabled at maturity 0 with 10s/2s continuous-ready delays, plus ADS Exit Method with a 20-second duty-end delay and both local exit choices disabled. The active character's two family rows and exit section display "Temporarily controlled by DAD"; saved profiles and other families remain unchanged. Owner release, character change/logout, DAD unload and FrenRider unload clear the in-memory override.
- Appended NPC repair + inn room as repair mode 4, preserving modes 0–3 and the Disabled default. FrenRider waits for ADS to finish the inn return even after durability recovers, and displays failed-trip status.
- Optional local aetheryte-network following under teleport settings detects a fren's jump and requests the matching Lifestream destination in connected normal, custom (including Bozja/Eureka), and residential networks when both players were at the origin.

### Fixed
- Send `/bmrai forbidactions off` immediately before enabling BMR AI so the selected preset can move into attack range, preserving Foray rotation selection, passive presets, and dodge clearance.
- Apply DDUCK's 1.5-yalm dodge clearance through Medium cushioning in all six packaged presets and before BMR AI activation.
- BST (43) now selects the melee BMR preset, including through DAD's existing FrenRider integration. Manual preset overrides remain in control.
- Configured ADS duty exit now waits from duty completion, bypasses the entry grace, and sends `/ads leave` once when loading, combat, and utility guards clear. Completed duties cannot restart automatic ADS progression after ownership release, cutscenes, missing duty identity, or re-enable; actual exit, logout, or a new duty start resets the session.
- Automatic ADS solo-duty handoffs now suppress active combat providers until the full configured char-safe delay and readable ADS ownership confirmation. Loading, all three cutscene flags, death, duty re-entry, and re-enable restart readiness; rejected or timed-out attempts receive a fresh countdown. Enable-time presets and Hyper Focus cannot bypass the hold, and the configured rotation bootstraps once afterward, respecting disabled rotation and leases.
- Questionable running-state checks support both Questionable and WigglyQuest, using the single loaded installation's
  IPC endpoint. Disabled duplicates do not override it, and multiple loaded copies report an unreadable state without
  querying either endpoint. The 250 ms polling interval and 15-second recent-running combat hold are unchanged.
- `FrenRider.Dad.ApplyProfile` accepts optional `forceTemporary` (default false). Companion HealRider can install the existing in-memory overlay under Off, Temporary, or Permanent acceptance without saving or changing that setting. Exact-owner conflicts, release, full-profile enable/disable effects, and callers that omit the field keep their existing behavior.

### Added
- Added **Obstacle maps on** under Combat > Advanced, defaulting to off. The setting is saved per profile, included in profile copying and default synchronization, and sent to BossMod Reborn at the end of each enable-time setup regardless of combat provider. VBM is unaffected; disabling FrenRider does not restore the setting.
- The main window now has large, equal-width red **All FR off** and green **All FR on** buttons above the scrolling content, also available as compact buttons in DEFAULT CONFIG, to immediately set the current account's default and local profiles, including an active temporary profile; main-window controls are disabled only when no account is loaded
- Repeated target range/line-of-sight errors can temporarily release confirmed casting movement locks while stationary outside combat, restoring on combat, a fixed five-second timeout, disable, transitions, unload, or control transfer; ADS, Questionable, and Coppelia ownership block recovery
- DEFAULT CONFIG now exposes whether Fren Rider is enabled by default and can sync that state to local character profiles
- ADS can acquire a five-second token-owned Hyper Focus lease through JSON IPC to run RSR Manual as the sole temporary combat provider in validated ADS-owned solo duties
- DAD can now resolve and export exact remote FrenRider profiles, apply them under each local character's Off/Temporary/Permanent acceptance policy, and release proposal-owned temporary overlays through bounded JSON IPC
- Settings now keep visually distinct editable remote DAD profile rows separate from local characters, keyed by exact owner, sender-island, and opaque-character identity
- `/fr settings` and `/fr s` now toggle settings, while `/fr mini` and `/fr m` toggle a separate auto-sized MAGIA window with Attack, Defense, and Off command buttons
- RSR combat configuration now separates the preserved Auto, Manual, None, and Support operating modes from all five upstream hostile-target choices, with per-character/default sync and typed IPC application
- A disabled-by-default per-character Automation option can equip the first matching Soul Crystal armoury item for a safe level 30+ base class, confirm it, and update the valid current gearset once
- Respawn settings now have independent outside-duty and inside-duty toggles and delays, each disabled by default with a 60-second delay and synchronized from DEFAULT CONFIG; notification, Return, and recovery prompt handling honor the selected delay
- Per-character Daedalus engage modes now expose stable None, Focus, Split, and Kill Adds choices when Daedalus is the effective normal/Foray rotation, with immediate save/default sync and fail-safe LAN coordination reflection
- Fren Rider now warns immediately and every five seconds while enabled if loaded internal plugins BossModReborn (BMR) and BossMod (VBM) are both detected, without disabling either plugin or blocking combat
- About → Optional Plugins now shows a click-to-copy Daedalus repository URL: `https://raw.githubusercontent.com/ofnature/Daedalus/main/repo.json`
- Daedalus is available as a normal and Foray combat-rotation choice, managed through typed enabled-state IPC with mounted/automation suppression and cleanup snapshot restoration
- Combat settings now include a global **Don't move while casting** toggle that applies the stored value on plugin load and on change to currently loaded BMR/VBM Action Tweaks plus RSR `PoslockCasting`
- ADS runtime duty ownership polling now uses authoritative `ADS.IsDutyOwned()` IPC with JSON fallback, a 250ms poll interval, and a bounded 5-second stale hold for transient IPC failures
- Duty combat now uses latched `QuestionableSolo` versus `FrenRider` authority, classifying true solo duties only from the validated ADS `CurrentDuty` category while unknown or invalid duty context stays FrenRider-owned
- Non-solo duties now receive one FrenRider combat bootstrap per enabled duty session, including when ADS already owns navigation and progression

### Fixed
- Ending or exhausting ADS Hyper Focus now re-arms FrenRider's normal one-time duty combat bootstrap, restoring the configured rotation and BossMod AI state only while FrenRider remains enabled in the ADS-owned duty
- ADS handoff now waits for each duty family's configurable continuous-ready delay before calling ADS (Solo defaults to 10 seconds; all other families to 2), restarting after player, death/unconscious, transition, cutscene, or duty-identity interruptions while preserving Praetorium readiness and the five-second ownership retry
- Newly surfaced characters in an existing XIVLauncher account group now inherit that group's `DEFAULT CONFIG` instead of stale legacy character settings
- ADS handoff from latched Questionable solo authority now reactivates the configured combat provider once even when an earlier duty bootstrap was already consumed
- Solo duties handed to ADS now retain FrenRider's one-time configured combat-rotation bootstrap instead of suppressing it under `QuestionableSolo` authority
- Packaged BossMod presets are now force-refreshed whenever Fren Rider is enabled, including when duty authority skips combat setup
- Fren Name edits and party-dropdown selections in Settings no longer disappear while interacting with the input
- Character lists are now grouped by XIVLauncher's raw `CurrentAccountId` from Windows or XIVLauncher.Core, with fail-closed launcher-config reads, character-proven migration of legacy account files, and a persistent Collector Account for genuinely unlinked characters
- Followed mounted party members now use their movement state for flight detection instead of their height relative to the local player
- Configured BMR/VBM AI state is now applied after BossMod preset setup so zone loading cannot override the selected on/off state
- Whitelisted party invite acceptance now closes the accepted invite dialog correctly
- Automatic and manual BossMod preset application now targets only the selected provider, preventing non-VBM passive presets from being sent to VBM and interrupting Questionable progress
- The row-level **Sync all** beside **ADS Exit Method** now copies the complete exit selection and clears conflicting local exit flags
- Cross-world BossMod follow now removes a matching concatenated home-world suffix before sending `/bmrai follow`, while preserving legitimate surnames that match a world name
- Ground stuck-jump recovery now tracks only while vnav reports a running path with no path calculation in progress; idle, calculating, or unknown vnav state resets the jump timer
- Settings character browsing now uses an editing-only selection, so viewing or changing `DEFAULT CONFIG` and other profiles cannot replace the logged-in character's runtime configuration or DAD target
- Manual ADS Start Inside/Resume now immediately pauses FrenRider follow, navigation, mounts, formation, combat-state changes, duty interaction, maintenance, and automatic dialogs
- Questionable no longer forces FrenRider combat engines off in 4-player, 8-player, alliance, deep-dungeon, treasure, or unknown duties; true solo ownership remains latched until duty exit
- Loading or re-enabling FrenRider inside an ADS-owned non-solo duty now activates the configured rotation exactly once before the ADS duty-system pause takes effect
- ADS handoff now waits for authoritative ownership confirmation, retries after a 5-second timeout/backoff, and falls back to `/ads inside` only when typed IPC is unavailable
- Configured FrenRider duty exit can take over after duty completion while all non-exit FrenRider duty systems remain paused
- AutoDuty warning lifecycle now clears correctly when FrenRider is disabled instead of re-forcing the popup path
- FATE join/leave no longer changes follow cling distance, so FATE entry cannot stop follow by itself
- Flying follow can now escape rare vnavmesh wall/object stalls by releasing vnav, ascending, automoving forward, and resuming normal pathing
- BossMod follow now uses the reviewed `/bmrai follow <name>` command flow instead of the older mismatched follow syntax
- BossMod AI cleanup snapshots now preserve and restore BMR/VBM AI on/off state instead of only follow and movement fields
- BossMod AI set to `off` now sends BMR/VBM AI off commands instead of re-enabling BossModReborn AI
- Main-window AutoDuty warning is only shown while FrenRider is enabled and the blocking warning window is not already open
- Duty area-transition recovery now resets zone-sensitive services after `BetweenAreas` loads, improving level-70 treasure dungeon waterfall recovery

### Changed
- Legacy RSR operating value 4 now migrates once to Auto plus Previously Engaged aggro instead of remaining a combined operating-mode choice
- Respawn now selects its inside-duty or outside-duty setting only from `BoundByDuty`, resets its unconscious timer and notification recovery on duty transitions, and no longer uses `BoundByDuty56` for this service
- FrenRider no longer disables AutoDuty while applying BossMod/rotation defaults; only the explicit **Disable AutoDuty** action in its warning window can send the disable command
- ADS duty identity, category, support, and clearance now come only from the validated `ADS.GetStatusJson` `CurrentDuty` projection; live GameMain territory/CFC mismatches or incomplete/unknown rows fail closed, and the former FrenRider maturity table, pilot promotions, treasure overrides, Lumina catalog, and category fallback are removed
- Global configuration schema v2 defaults **Don't move while casting** on, migrates v1 configurations on once, and preserves later v2 opt-outs
- FATE transitions now log follow/combat decision context for pathing diagnostics
- Mounted rotation suppression now targets the selected combat engine while mounted outside duty, then restores it on dismount or duty entry
- BossMod follow is now treated as a stateful mode and only reissued when target, territory, or follow-combat settings change
- AutoDuty warning window no longer emits per-frame position debug logging

### Fixed
- Fly You Fools now uses ground `/vnav moveto` navigation in every no-flight Foray while retaining normal takeoff and `/vnav flyto` behavior in the Diadem (territory 939)
- Nearby Fly You Fools followers now honor `ConditionFlag.Mounted` alongside the local character's ClientStructs mount state while excluding pillion; normal own-mount correction applies in Forays and every duty except The Praetorium (territory 1044)

### Phase 11 - Food Eating & Chocobo Summoning

#### [0.10.1] - 2026-02-28

**Fixed:**
- **CRITICAL: Item usage now works properly**:
  - Added `extraParam: 65535` to `ActionManager.UseAction()` for items (required parameter from AutoDuty)
  - Added proper casting check (`player.IsCasting`) before item usage
  - Added occupied state checks (cutscene, quest event, etc.) before item usage
  - Fixed deprecated `IClientState.LocalPlayer` warnings by using `IObjectTable.LocalPlayer`
- **Throttling to prevent action spam**:
  - Food eating: 5-second cooldown between attempts, 20-second delay after success
  - Companion summoning: 5-second cooldown between attempts, 20-second delay after success
  - Prevents rapid-fire attempts that cause action cancellation
  - Status display shows cooldown timer when throttled
- **Self-repair implementation** (from AutoDuty):
  - Added `GameHelpers.NeedsRepair(conditionPercent)` - checks equipped gear durability
  - Added `GameHelpers.UseRepairAction()` - uses General Action 6 (Repair with dark matter)
  - Updated `AutomationService.TriggerRepair()` to use new repair methods
  - Respects `TornClothes` config for repair threshold
  - NPC repair remains unimplemented (requires navigation + ATK interaction)

**Changed:**
- `GameHelpers.UseItem()` now checks player state before attempting item usage
- Food/companion status messages now show throttle cooldown timers
- Improved logging: changed failed attempts from `Information` to `Debug` level

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
