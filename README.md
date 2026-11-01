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
  through `ToggleOff` and let the original method perform the switch. A selection made during
  an attack is stored and replayed from `ReportMeleeDonePostfix` once the stance has sheathed.
  A keyboard consumable in a gadget slot is used through `InventoryItem.Use(true)` without
  leaving the stance, mirroring the check `ChangeWeapon` makes before it uses one. Each entry
  point is patched only when its signature matches (`ChangeWeapon(InventorySlot, bool forced,
  bool navigatedDirectlyTo)` and `InventoryItem.Use(bool)` are 1.0.2 signatures).
- Target game version: SULFUR v0.18.5 (verified). All patched members are unchanged since
  v0.17.4.
