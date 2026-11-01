# Toggle Melee Stance — Developer Notes

BepInEx plugin for **SULFUR**. Turns the melee action into a toggle/tap-hold stance and
serves as the shared melee stance module for The Dragonblade.

The player-facing store description lives in [`Thunderstore/README.md`](Thunderstore/README.md).

## Layout

```text
Class1.cs                 Plugin implementation (single file, string-reflection patches)
Properties/AssemblyInfo.cs
Directory.Build.props     Machine-specific paths (override on the command line)
Toggle Melee Stance.csproj
Thunderstore/             Package contents + repo-tracked store files
```

## Building

All game / BepInEx paths come from `Directory.Build.props`. Override any of them on the
command line if your install differs, e.g. `-p:SulfurGameDir=...`.

```powershell
dotnet build "Toggle Melee Stance.csproj" -c Release
```

To also copy the built DLL into the Gale profile and the `Thunderstore/` folder:

```powershell
dotnet build "Toggle Melee Stance.csproj" -c Release -p:DeployToSulfurProfile=true
```

## Packaging

Use the `sulfur-thunderstore` skill's `pack` action, which validates the manifest, icon,
version consistency (manifest / CHANGELOG / `PluginVersion`) and builds the release zip.

## Implementation notes

- The plugin resolves all game members by name through Harmony `AccessTools`; there is no
  compile-time dependency on the Sulfur assemblies. A missing member logs an error and
  disables only the affected hook.
- Weapon selection while the stance is on (`WeaponSwitchExitsMeleeStance`): the game refuses
  every non-forced `ChangeWeapon` while `weaponSwitchingPrevented` is set, and `NextWeapon` /
  `PreviousWeapon` return early while the basic melee slot is selected. Prefixes on
  `ChangeWeapon` (slot keys only, recognised by `navigatedDirectlyTo`), `NextWeapon`,
  `PreviousWeapon` (only without a starting slot) and `SelectLastUsedWeapon` end the stance
  through `ToggleOff`. A slot key then lets the original method perform the switch. The other
  three skip the original (bool prefix) once the stance has left the melee slot: `OnMeleeDone`
  has already restored the pre-stance weapon, and the original would move one weapon further,
  because `lastSelectedSlot` and `slotOrder` both ignore the basic melee slot. A slot key pressed
  during an attack is stored and replayed from `ReportMeleeDonePostfix` once the stance has
  sheathed; the other requests need no replay, since the sheathe restores the pre-stance weapon.
  The stance also counts as a last used item: every place where it ends (`ToggleOff`, both
  sheathe completions, the retry-window exit, the external-switch exit) calls
  `RememberStanceAsLastUsed` with the current slot, and a `ChangeWeapon` in the same frame (a slot
  key, a replayed selection) moves that slot along. `SelectLastUsedWeaponPrefix` then re-enters the
  stance through `ToggleOn` (`TryReturnToStance`) while the player is still on that slot. Any later
  switch to another slot clears it in `ChangeWeaponPostfix`, after which the game's own
  `lastSelectedSlot` applies again.
  A keyboard consumable in a gadget slot is used through `InventoryItem.Use(true)` without
  leaving the stance, mirroring the check `ChangeWeapon` makes before it uses one. Each entry
  point is patched only when its signature matches (`ChangeWeapon(InventorySlot, bool forced,
  bool navigatedDirectlyTo)` and `InventoryItem.Use(bool)` are 1.0.2 signatures).
- Melee key behavior (`MeleeInputMode`): `GetMeleeKeyMode` is the one place that reads it.
  `HandleMeleeInputPrefix` dispatches on it, and clears the tap / hold tracking when the value
  changes at runtime. In `QuickMeleeChordToggle` the prefix returns `true` while the stance is
  off, so the game charges on press and swings on release. `PullTriggerPrefix` and
  `SelectLastUsedWeaponPrefix` call `TryEnterStanceFromChord`, which promotes a held quick melee
  charge into the stance through `ToggleOn`, as the swapped tap / hold mode does at its
  threshold. It requires `meleePressed`, `isInMeleeCharge`, the basic melee slot and a charging
  melee weapon; `isInMeleeCharge` is set one frame after the press, so Fire on the press frame
  (which the game reads before the melee key) never counts. `EnableTapHoldMeleeKey` and
  `SwapQuickMeleeAndEquipMelee` are read once in `Awake` by `MigrateLegacyMeleeKeySettings`
  and then reset to their defaults.
- Target game version: SULFUR v0.18.5 (verified). All patched members are unchanged since
  v0.17.4.
