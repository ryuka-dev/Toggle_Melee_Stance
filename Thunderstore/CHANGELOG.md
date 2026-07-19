# Changelog

## 1.3.0

- Fixed item pickup and interaction being blocked while melee stance is toggled on. The game only allows interaction when the basic melee slot is not active, and the persistent stance held that slot indefinitely, which hid item name prompts and prevented picking anything up. Interaction now works while melee stance is active.
- Interaction lockouts that the game applies for other reasons (such as the straitjacket status) are still respected.
- Verified compatibility with SULFUR v0.18.5. No patch targets changed.

## 1.2.2

- Added SULFUR Config localization files for the in-game configuration page (14 languages). No gameplay changes.

## 1.2.0

- Changed this mod into the shared prerequisite melee stance module for The Dragonblade.
- Added public API for other mods to read melee stance state.
- Stopped auto-disabling when `kumo.sulfur.melee_expansion` is installed.
- Changed default `MeleeToggleTapThreshold` from `0.22` to `0.13` for faster hold-melee passthrough.
- Kept external weapon switch compatibility so guns can fire normally after another mod switches the current weapon.

## 1.1.0

- Added tap / hold melee key behavior.
- Tap melee key toggles melee stance.
- Hold melee key uses the original melee behavior.
- Added external weapon switch compatibility.

## 1.0.0

- Initial release.
- Added toggle melee stance.
- Added Fire action melee attack while toggled.
- Preserved vanilla Aim / alternative stance / block behavior.
- Added safe sheathe behavior so pressing melee again does not trigger an extra attack.
- Added melee Animator safe-state reset to prevent repeated draw animation flashes.
- Added compatibility auto-disable for future Melee Expansion mod.
