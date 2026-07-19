# Toggle Melee Stance

A shared melee stance module and quality-of-life mod for **SULFUR**.

It changes melee from hold-to-use into a toggle stance, while keeping the original hold melee behavior available through tap / hold input.

This mod can be used by itself, and it is also used as the shared prerequisite melee stance module for **The Dragonblade**.

## What This Mod Does

- Press the game's melee action once to keep your melee weapon drawn.
- Press the melee action again to sheathe it and return to your previous weapon.
- Tap the melee key to toggle melee stance.
- Hold the melee key to use the original melee behavior.
- While melee stance is toggled on, press the normal Fire action to perform one melee attack.
- While melee stance is toggled on, hold Aim / Alt Fire to use the vanilla alternative melee stance or block.
- Lets you pick up items and use interactables while melee stance is active. The base game blocks all interaction while the basic melee is out, which the persistent stance would otherwise leave active the whole time.
- Uses SULFUR's own input actions instead of hardcoded mouse buttons, so it should work with rebinding, controller, and Steam Deck layouts.
- Keeps the original melee attack, parry, alternative stance, and weapon-specific behavior.
- Fixes a visual issue where some melee weapons could briefly flash an attack animation on repeated draw by resetting the melee Animator to a safe cached equip state before sheathing.
- Handles external weapon switches safely, so a weapon-randomizing mod switching you to a gun should not leave melee stance blocking Fire input.
- Exposes a small public API that other mods can use to read the current toggle melee stance state.

## What This Mod Does NOT Do

- Does not change melee damage.
- Does not change weapon durability.
- Does not change enemy AI.
- Does not edit save files.
- Does not replace or rebalance melee weapons.
- Does not add new melee moves such as dashes or thrust attacks.

## Basic Controls

```text
Tap Melee key
→ Toggle melee stance on / off

Hold Melee key
→ Use the original melee behavior
→ Release to perform the original melee attack
→ Return to the previous weapon

Fire
→ Perform one melee attack while melee stance is toggled on

Aim / Alt Fire
→ Keep vanilla alternative melee stance / block behavior
```

The mod does not hardcode `F`, mouse buttons, or controller buttons.

It uses the game's own input actions, so it should respect key rebinding and controller input better than raw key checks.

## Shared Module Behavior

Starting from `1.2.0`, Toggle Melee Stance is intended to be the shared stance foundation for other melee-focused mods.

The Dragonblade should no longer duplicate the same toggle melee patches internally. Instead, it should depend on this mod and read the stance state through the public API.

This avoids double-patching the same melee input methods and reduces the chance of both mods fighting over the same internal melee state.

## Public API For Other Mods

The public API is exposed through:

```text
ToggleMeleeStance.Plugin
```

Available methods:

```text
IsRuntimeReady
IsMeleeStanceActive(object equipmentManager)
IsAttackInProgress(object equipmentManager)
IsSheatheAfterAttack(object equipmentManager)
IsMeleeChargeActive(object equipmentManager)
GetCurrentHoldableForExternal(object equipmentManager)
IsCurrentHoldableMeleeForExternal(object equipmentManager)
```

These are intended for mods such as The Dragonblade to check whether melee stance is active without patching the same melee input methods again.

## Configuration

Config file:

```text
BepInEx/config/kumo.sulfur.toggle_melee_stance.cfg
```

Default options:

```ini
[General]
EnableMod = true

[Melee]
FirePerformsMeleeAttack = true
ReChargeRetryInterval = 0.08
EnableTapHoldMeleeKey = true
MeleeToggleTapThreshold = 0.13

[Visual]
ResetMeleeAnimatorBeforeSheathe = true

[Compatibility]
DisableWhenMeleeExpansionDetected = false
KeepMeleeStanceAfterExternalWeaponSwitch = false

[Debug]
LogStateChanges = false
```

## Option Details

`EnableMod`

Enables or disables the mod.

`FirePerformsMeleeAttack`

When enabled, pressing the normal Fire action while melee is toggled on performs one melee attack.

`ReChargeRetryInterval`

Controls how often the mod retries re-entering melee stance after the game clears the melee charge state.

`EnableTapHoldMeleeKey`

When enabled, tapping the melee key toggles melee stance, while holding the melee key passes through to the original melee behavior.

`MeleeToggleTapThreshold`

Maximum press duration treated as a tap.

Default:

```ini
MeleeToggleTapThreshold = 0.13
```

Lower values make hold behavior activate faster.

Higher values make tap behavior easier.

`ResetMeleeAnimatorBeforeSheathe`

Keeps repeated melee draws visually stable by resetting the melee Animator to a cached safe equip state before sheathing.

`DisableWhenMeleeExpansionDetected`

Deprecated.

Older versions used this to auto-disable Toggle Melee Stance when `kumo.sulfur.melee_expansion` was installed.

Starting from this version, The Dragonblade is expected to use Toggle Melee Stance as a prerequisite, so this option defaults to `false` and is no longer recommended.

`KeepMeleeStanceAfterExternalWeaponSwitch`

Controls behavior when another mod changes the current weapon while melee stance is toggled on.

```text
false
→ Recommended default.
→ If another mod switches your weapon while melee stance is active, this mod exits stance when you try to fire.
→ This allows guns to shoot normally after an external weapon switch.

true
→ Tries to keep melee stance even after another mod switches your weapon.
→ This may conflict with weapon-randomizing mods.
```

`LogStateChanges`

Enables debug logging. Keep this disabled for normal gameplay.

## Installation

### With a Mod Manager

Install through Thunderstore / r2modman if available.

### Manual Installation

1. Install BepInEx for SULFUR.
2. Extract this package into your SULFUR game folder.
3. Make sure the DLL ends up here:

```text
SULFUR/BepInEx/plugins/Toggle Melee Stance.dll
```

4. Start the game once to generate the config file.

## Compatibility

This mod does not edit original game files.

It should be compatible with most mods. Compatibility issues are most likely with mods that also patch:

- `EquipmentManager.HandleMeleeInput`
- `EquipmentManager.HandleAimInput`
- `EquipmentManager.PullTrigger`
- `Weapon.ReportMeleeDone`
- `Interactable.CanInteract`
- melee weapon Animator behavior

The Dragonblade `0.3.0+` is expected to use this mod as a prerequisite instead of duplicating the same melee stance patches.

## Uninstallation

Remove the DLL from:

```text
BepInEx/plugins/
```

Optional: remove the config file:

```text
BepInEx/config/kumo.sulfur.toggle_melee_stance.cfg
```

## SULFUR Config Localization Support

This mod includes localization files for SULFUR Config.

This localization support is only for the in-game configuration page provided by SULFUR Config. It localizes the mod name, config sections, setting names, and setting descriptions shown in the config UI.

It does not change the game's own text, item names, dialogue, or gameplay content.

Supported SULFUR Config languages:

- English
- Simplified Chinese
- Swedish
- French
- Italian
- German
- Spanish
- Portuguese
- Russian
- Polish
- Japanese
- Korean
- Turkish
- Arabic
