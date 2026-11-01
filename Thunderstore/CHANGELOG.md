# Changelog

## 1.6.0

- New `MeleeInputMode` setting. If telling a tap from a hold feels finicky, try `QuickMeleeChordToggle`: the melee key always does the vanilla quick melee, and holding it while pressing Fire or the last used weapon key enters melee stance, so there's no timing to get right. Thanks to the player who suggested it on GitHub.
- The last used weapon key now treats melee stance like a weapon. However you leave the stance, pressing it takes you back in, so pressing it repeatedly switches between your weapon and the stance.
- Fixed the last used weapon key and the scroll wheel leaving melee stance onto the wrong weapon. They now return to the weapon you had out before the stance.
- `EnableTapHoldMeleeKey` and `SwapQuickMeleeAndEquipMelee` are replaced by `MeleeInputMode`. Your old setting is carried over on the first start.

## 1.5.0

- Selecting a weapon now leaves melee stance and switches straight to it, so you no longer have to press the melee key first. This works with the weapon slot keys (1-5), the scroll wheel or gamepad d-pad, and the last used weapon key. If you pick a weapon in the middle of a swing, the switch happens when the swing ends. Consumables in slots 3-5 are used without leaving the stance. Set `WeaponSwitchExitsMeleeStance` to false to keep the old behavior.
- Fixed the fire button getting stuck after an Endless mode level-up. If the card selection opened during a stance attack (picking a card with Fire starts one), the stance didn't come back, and after leaving it automatic weapons kept firing on their own while other weapons couldn't fire.
- Holding Fire while entering melee stance no longer leaves the gun's trigger held after you leave the stance.
- Verified on SULFUR 1.0.2.

## 1.4.0

- Added `SwapQuickMeleeAndEquipMelee` option. When enabled (and tap/hold input is on), tap the melee key to perform the original quick melee attack and hold the melee key to toggle melee stance. Default keeps the existing mapping: tap toggles stance, hold performs the quick melee attack.

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
