using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ToggleMeleeStance
{
    [BepInPlugin(
        PluginGuid,
        PluginName,
        PluginVersion
    )]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "kumo.sulfur.toggle_melee_stance";
        public const string PluginName = "Toggle Melee Stance";
        public const string PluginVersion = "1.5.0";

        public const string MeleeExpansionGuid = "kumo.sulfur.melee_expansion";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> EnableMod;
        internal static ConfigEntry<bool> FirePerformsMeleeAttack;
        internal static ConfigEntry<bool> ResetMeleeAnimatorBeforeSheathe;
        internal static ConfigEntry<bool> DisableWhenMeleeExpansionDetected;
        internal static ConfigEntry<bool> KeepMeleeStanceAfterExternalWeaponSwitch;
        internal static ConfigEntry<MeleeKeyMode> MeleeInputMode;
        internal static ConfigEntry<bool> EnableTapHoldMeleeKey;
        internal static ConfigEntry<bool> SwapQuickMeleeAndEquipMelee;
        internal static ConfigEntry<float> MeleeToggleTapThreshold;
        internal static ConfigEntry<bool> WeaponSwitchExitsMeleeStance;
        internal static ConfigEntry<float> ReChargeRetryInterval;
        internal static ConfigEntry<bool> LogStateChanges;

        private Harmony harmony;

        private static Type equipmentManagerType;
        private static Type weaponType;
        private static Type interactableType;
        private static Type gameManagerType;

        // When our toggle stance is active, allow interaction that vanilla blocks purely
        // because the current slot is BasicMelee. Only override while the stance is being
        // actively maintained (see LastMaintainTime), so game-halted states such as the
        // straitjacket status, flashbacks, and pause keep their vanilla interaction lockouts.
        private const float StanceLivenessWindow = 0.5f;

        private static MethodInfo mHandleAimInput;
        private static MethodInfo mHandleMeleeInput;
        private static MethodInfo mPullTrigger;
        private static MethodInfo mReleaseTrigger;
        private static MethodInfo mChargeBasicMelee;
        private static MethodInfo mUseBasicMelee;
        private static MethodInfo mOnMeleeDone;
        private static MethodInfo mReportMeleeDone;
        private static MethodInfo mIsMeleeCharging;
        private static MethodInfo mChargeMelee;
        private static MethodInfo mSetAlternativeState;
        private static MethodInfo mCanInteract;
        private static MethodInfo mChangeWeapon;

        private static PropertyInfo pWeaponCurrentSlot;
        private static object basicMeleeSlotValue;

        private static PropertyInfo pIsMelee;
        private static PropertyInfo pAnimator;
        private static PropertyInfo pGameManagerInstance;
        private static PropertyInfo pGameManagerEquipmentManager;

        private static FieldInfo fAltFireAction;
        private static FieldInfo fMeleeFireAction;
        private static FieldInfo fMeleeFireActionAlternative;
        private static FieldInfo fSelectLastUsedAction;

        private static FieldInfo fCurrentHoldable;
        private static FieldInfo fEquipmentManager;

        private static FieldInfo fIsInMeleeCharge;
        private static FieldInfo fMeleePressed;
        private static FieldInfo fAlternativeMeleePressed;
        private static FieldInfo fAimingInputHeld;
        private static FieldInfo fMeleeInputCooldown;
        private static FieldInfo fCurrentParries;

        // Optional members for leaving the stance when the player selects a weapon. A missing or
        // changed member disables only the entry point that needs it.
        private static MethodInfo mNextWeapon;
        private static MethodInfo mPreviousWeapon;
        private static MethodInfo mSelectLastUsedWeapon;
        private static MethodInfo mInventoryItemUse;
        private static PropertyInfo pWeaponSwitchingPrevented;
        private static PropertyInfo pGamepadUsed;
        private static PropertyInfo pInventoryItemDefinition;
        private static PropertyInfo pInventoryItemUseType;
        private static FieldInfo fItemDefinitionPrefab;
        private static FieldInfo fEquippedItems;
        private static object inventorySlotNoneValue;
        private static object consumableUseTypeValue;
        private static bool changeWeaponSignatureMatches;

        private static readonly ConditionalWeakTable<object, ToggleState> States =
            new ConditionalWeakTable<object, ToggleState>();

        private static readonly ConditionalWeakTable<object, SafeAnimatorState> SafeAnimatorStates =
            new ConditionalWeakTable<object, SafeAnimatorState>();

        private void Awake()
        {
            Log = Logger;

            EnableMod = Config.Bind(
                "General",
                "EnableMod",
                true,
                "Enable Toggle Melee Stance."
            );

            FirePerformsMeleeAttack = Config.Bind(
                "Melee",
                "FirePerformsMeleeAttack",
                true,
                "When melee stance is toggled on, pressing the normal Fire action performs one melee attack."
            );

            ReChargeRetryInterval = Config.Bind(
                "Melee",
                "ReChargeRetryInterval",
                0.08f,
                new ConfigDescription(
                    "Seconds between retry attempts when the mod tries to re-enter melee charge after an attack.",
                    new AcceptableValueRange<float>(0.01f, 0.5f)
                )
            );

            ResetMeleeAnimatorBeforeSheathe = Config.Bind(
                "Visual",
                "ResetMeleeAnimatorBeforeSheathe",
                true,
                "Before sheathing a toggled melee weapon, reset its Animator to the cached safe equip state. This prevents stale idle states from causing a slash-frame flash on the next draw."
            );

            DisableWhenMeleeExpansionDetected = Config.Bind(
                "Compatibility",
                "DisableWhenMeleeExpansionDetected",
                false,
                "Deprecated. Kept only for old config compatibility. Toggle Melee Stance no longer auto-disables when The Dragonblade is installed, because it is now the shared prerequisite melee stance module."
            );

            KeepMeleeStanceAfterExternalWeaponSwitch = Config.Bind(
                "Compatibility",
                "KeepMeleeStanceAfterExternalWeaponSwitch",
                false,
                "When another mod changes the current weapon while toggle melee stance is active, keep melee stance instead of exiting it. Default false: exit stance and allow the new weapon to fire normally."
            );

            MeleeInputMode = Config.Bind(
                "Melee",
                "MeleeInputMode",
                MeleeKeyMode.TapToggleHoldQuickMelee,
                "How the melee key works. TapToggleHoldQuickMelee (default): tap to toggle melee stance, hold for the original quick melee. TapQuickMeleeHoldToggle: tap for the original quick melee, hold to toggle melee stance. PressToggles: every press toggles melee stance, with no quick melee. QuickMeleeChordToggle: the melee key works as in the base game, and pressing Fire or the last used weapon key while holding it enters melee stance instead of attacking. In every mode, press the melee key again to leave melee stance."
            );

            EnableTapHoldMeleeKey = Config.Bind(
                "Melee",
                "EnableTapHoldMeleeKey",
                true,
                "Deprecated, read once for migration. Replaced by MeleeInputMode. If this is false while MeleeInputMode is at its default, the next start sets MeleeInputMode to PressToggles and resets this entry to true."
            );

            SwapQuickMeleeAndEquipMelee = Config.Bind(
                "Melee",
                "SwapQuickMeleeAndEquipMelee",
                false,
                "Deprecated, read once for migration. Replaced by MeleeInputMode. If this is true while MeleeInputMode is at its default and EnableTapHoldMeleeKey is true, the next start sets MeleeInputMode to TapQuickMeleeHoldToggle and resets this entry to false."
            );

            MeleeToggleTapThreshold = Config.Bind(
                "Melee",
                "MeleeToggleTapThreshold",
                0.13f,
                new ConfigDescription(
                    "Used only by the TapToggleHoldQuickMelee and TapQuickMeleeHoldToggle input modes. Maximum press duration treated as a tap. A longer press counts as a hold.",
                    new AcceptableValueRange<float>(0.05f, 0.6f)
                )
            );

            WeaponSwitchExitsMeleeStance = Config.Bind(
                "Melee",
                "WeaponSwitchExitsMeleeStance",
                true,
                "When melee stance is on, the weapon selection controls leave it. The weapon slot keys (1-5 by default) switch to that slot. The last used weapon key and weapon cycling with the scroll wheel or gamepad d-pad return to the weapon you held before the stance. However the stance ends, the last used weapon key then takes you back into it, until you switch to another weapon. A slot chosen during a melee attack is applied when the attack finishes. Consumables in slots 3-5 are used without leaving melee stance."
            );

            LogStateChanges = Config.Bind(
                "Debug",
                "LogStateChanges",
                false,
                "Log toggle melee state changes. Keep false for normal gameplay."
            );

            MigrateLegacyMeleeKeySettings();


            if (!InitializeReflection())
            {
                Logger.LogError("Reflection initialization failed. Mod will not patch.");
                return;
            }

            harmony = new Harmony(PluginGuid);

            Patch(mHandleAimInput, prefix: nameof(HandleAimInputPrefix));
            Patch(mHandleMeleeInput, prefix: nameof(HandleMeleeInputPrefix));
            Patch(mPullTrigger, prefix: nameof(PullTriggerPrefix));
            Patch(mReleaseTrigger, prefix: nameof(ReleaseTriggerPrefix));
            Patch(mReportMeleeDone, prefix: nameof(ReportMeleeDonePrefix), postfix: nameof(ReportMeleeDonePostfix));
            Patch(mChargeMelee, postfix: nameof(ChargeMeleePostfix));

            // Optional: restore item pickup / interaction while the toggle melee stance is
            // active. Vanilla Interactable.CanInteract() blocks all interaction whenever the
            // current slot is BasicMelee, which our persistent stance holds indefinitely.
            // If this hook cannot be resolved the rest of the mod still functions.
            if (mCanInteract != null && pGameManagerInstance != null && pGameManagerEquipmentManager != null)
            {
                Patch(mCanInteract, postfix: nameof(CanInteractPostfix));
            }
            else
            {
                Logger.LogWarning(
                    "Interaction-in-melee-stance fix disabled: could not resolve " +
                    "Interactable.CanInteract or GameManager equipment access. " +
                    "Item pickup will be blocked while melee stance is active."
                );
            }

            // Optional: notice when the game itself moves the equipment off the basic melee slot
            // (for example the Endless mode card selection, or any other forced weapon change)
            // while a toggled melee attack is still waiting for its completion event. Without
            // this hook the attack state is only cleared by that event, which cannot arrive once
            // the melee weapon has been put away.
            if (mChangeWeapon != null && pWeaponCurrentSlot != null && basicMeleeSlotValue != null)
            {
                Patch(mChangeWeapon, postfix: nameof(ChangeWeaponPostfix));
            }
            else
            {
                Logger.LogWarning(
                    "Melee slot exit tracking disabled: could not resolve " +
                    "EquipmentManager.ChangeWeapon or EquipmentManager.weaponCurrentSlot. " +
                    "A melee attack interrupted by a forced weapon change may leave the stance state stale."
                );
            }

            // Optional: a weapon selection made while melee stance is on leaves the stance and
            // performs that selection (see HandleWeaponSelectionRequest). Each entry point is
            // patched only when its signature matches, so a game update that changes one of them
            // disables only that entry point.
            if (CanHandleWeaponSlotKeys())
            {
                Patch(mChangeWeapon, prefix: nameof(ChangeWeaponPrefix));
            }
            else
            {
                Logger.LogWarning(
                    "Weapon slot keys will not leave melee stance: could not resolve " +
                    "EquipmentManager.ChangeWeapon(InventorySlot, bool forced, bool navigatedDirectlyTo) " +
                    "or the equipped item members it needs."
                );
            }

            PatchWeaponSelectionEntry(mNextWeapon, nameof(NextWeaponPrefix), "EquipmentManager.NextWeapon()");
            PatchWeaponSelectionEntry(mPreviousWeapon, nameof(PreviousWeaponPrefix), "EquipmentManager.PreviousWeapon(InventorySlot)");
            PatchWeaponSelectionEntry(mSelectLastUsedWeapon, nameof(SelectLastUsedWeaponPrefix), "EquipmentManager.SelectLastUsedWeapon()");

            Logger.LogInfo("Toggle Melee Stance loaded.");
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }

        // Versions before MeleeInputMode chose the melee key behavior with two booleans. When an
        // older config still sets one of them away from its default, it is carried over into
        // MeleeInputMode, but only while MeleeInputMode is still at its own default: a mode the
        // player picked is never overwritten. The booleans are then reset to their defaults in
        // either case, so this runs once and a later return to the default mode stays as chosen.
        private static void MigrateLegacyMeleeKeySettings()
        {
            bool tapHoldDisabled = !EnableTapHoldMeleeKey.Value;
            bool swapped = SwapQuickMeleeAndEquipMelee.Value;

            if (!tapHoldDisabled && !swapped)
                return;

            if (MeleeInputMode.Value == MeleeKeyMode.TapToggleHoldQuickMelee)
            {
                // The swap only ever applied with tap / hold enabled.
                MeleeInputMode.Value = tapHoldDisabled
                    ? MeleeKeyMode.PressToggles
                    : MeleeKeyMode.TapQuickMeleeHoldToggle;

                Log?.LogInfo(
                    "Migrated EnableTapHoldMeleeKey=" + EnableTapHoldMeleeKey.Value +
                    ", SwapQuickMeleeAndEquipMelee=" + SwapQuickMeleeAndEquipMelee.Value +
                    " to MeleeInputMode=" + MeleeInputMode.Value + "."
                );
            }
            else
            {
                Log?.LogInfo(
                    "Ignored the deprecated EnableTapHoldMeleeKey / SwapQuickMeleeAndEquipMelee values " +
                    "because MeleeInputMode is set (" + MeleeInputMode.Value + ")."
                );
            }

            EnableTapHoldMeleeKey.Value = true;
            SwapQuickMeleeAndEquipMelee.Value = false;
        }

        // The one place that decides the melee key behavior.
        private static MeleeKeyMode GetMeleeKeyMode()
        {
            if (MeleeInputMode == null)
                return MeleeKeyMode.TapToggleHoldQuickMelee;

            switch (MeleeInputMode.Value)
            {
                case MeleeKeyMode.TapQuickMeleeHoldToggle:
                case MeleeKeyMode.PressToggles:
                case MeleeKeyMode.QuickMeleeChordToggle:
                    return MeleeInputMode.Value;

                default:
                    return MeleeKeyMode.TapToggleHoldQuickMelee;
            }
        }

        private static bool InitializeReflection()
        {
            equipmentManagerType = AccessTools.TypeByName(
                "PerfectRandom.Sulfur.Core.Items.EquipmentManager"
            );

            weaponType = AccessTools.TypeByName(
                "PerfectRandom.Sulfur.Core.Weapons.Weapon"
            );

            if (equipmentManagerType == null)
            {
                Log?.LogError("Could not find EquipmentManager type.");
                return false;
            }

            if (weaponType == null)
            {
                Log?.LogError("Could not find Weapon type.");
                return false;
            }

            mHandleAimInput = AccessTools.Method(
                equipmentManagerType,
                "HandleAimInput",
                new Type[] { typeof(bool) }
            );

            mHandleMeleeInput = AccessTools.Method(
                equipmentManagerType,
                "HandleMeleeInput",
                new Type[] { typeof(bool) }
            );

            mPullTrigger = AccessTools.Method(equipmentManagerType, "PullTrigger");
            mReleaseTrigger = AccessTools.Method(equipmentManagerType, "ReleaseTrigger");
            mChargeBasicMelee = AccessTools.Method(equipmentManagerType, "ChargeBasicMelee");
            mUseBasicMelee = AccessTools.Method(equipmentManagerType, "UseBasicMelee");
            mOnMeleeDone = AccessTools.Method(equipmentManagerType, "OnMeleeDone");

            mReportMeleeDone = AccessTools.Method(weaponType, "ReportMeleeDone");
            mIsMeleeCharging = AccessTools.Method(weaponType, "IsMeleeCharging");

            mChargeMelee = AccessTools.Method(
                weaponType,
                "ChargeMelee",
                new Type[] { typeof(bool) }
            );

            mSetAlternativeState = AccessTools.Method(
                weaponType,
                "SetAlternativeState",
                new Type[] { typeof(int) }
            );

            pIsMelee = AccessTools.Property(weaponType, "IsMelee");
            pAnimator = FindPropertyRecursive(weaponType, "Animator");

            fAltFireAction = FindFieldRecursive(equipmentManagerType, "altFireAction");
            fMeleeFireAction = FindFieldRecursive(equipmentManagerType, "meleeFireAction");
            fMeleeFireActionAlternative = FindFieldRecursive(equipmentManagerType, "meleeFireActionAlternative");

            // Optional: tells the last used weapon key apart from the game's own calls to
            // SelectLastUsedWeapon (a spent throwable). Without it the key does not go back into
            // the stance after leaving it (see TryReturnToStance).
            fSelectLastUsedAction = FindFieldRecursive(equipmentManagerType, "selectLastUsedAction");

            if (fSelectLastUsedAction != null && !typeof(InputAction).IsAssignableFrom(fSelectLastUsedAction.FieldType))
                fSelectLastUsedAction = null;

            fCurrentHoldable = FindFieldRecursive(equipmentManagerType, "currentHoldable");
            fEquipmentManager = FindFieldRecursive(weaponType, "equipmentManager");

            fIsInMeleeCharge = FindFieldRecursive(
                equipmentManagerType,
                "<isInMeleeCharge>k__BackingField",
                "isInMeleeCharge"
            );

            fMeleePressed = FindFieldRecursive(
                equipmentManagerType,
                "<meleePressed>k__BackingField",
                "meleePressed"
            );

            fAlternativeMeleePressed = FindFieldRecursive(
                equipmentManagerType,
                "<alternativeMeleePressed>k__BackingField",
                "alternativeMeleePressed"
            );

            fAimingInputHeld = FindFieldRecursive(
                equipmentManagerType,
                "<AimingInputHeld>k__BackingField",
                "AimingInputHeld"
            );

            fMeleeInputCooldown = FindFieldRecursive(
                equipmentManagerType,
                "meleeInputCooldown"
            );

            fCurrentParries = FindFieldRecursive(
                weaponType,
                "currentParries"
            );

            // Optional members for the interaction-in-melee-stance fix. These are not part of
            // the required set: if any is missing the mod still loads and only that fix is skipped.
            interactableType = AccessTools.TypeByName(
                "PerfectRandom.Sulfur.Core.World.Interactable"
            );

            gameManagerType = AccessTools.TypeByName(
                "PerfectRandom.Sulfur.Core.GameManager"
            );

            if (interactableType != null)
            {
                mCanInteract = AccessTools.Method(interactableType, "CanInteract", Type.EmptyTypes);
            }

            if (gameManagerType != null)
            {
                pGameManagerInstance = FindPropertyRecursive(gameManagerType, "Instance");
                pGameManagerEquipmentManager = FindPropertyRecursive(gameManagerType, "EquipmentManager");
            }

            // Optional members for tracking the equipment leaving the basic melee slot. The
            // postfix binds only __instance, so the patch does not depend on ChangeWeapon's
            // parameter list (1.0 appended a navigatedDirectlyTo parameter).
            mChangeWeapon = AccessTools.Method(equipmentManagerType, "ChangeWeapon");
            pWeaponCurrentSlot = FindPropertyRecursive(equipmentManagerType, "weaponCurrentSlot");

            if (pWeaponCurrentSlot != null && pWeaponCurrentSlot.PropertyType.IsEnum)
            {
                try
                {
                    basicMeleeSlotValue = Enum.Parse(pWeaponCurrentSlot.PropertyType, "BasicMelee");
                }
                catch (ArgumentException)
                {
                    basicMeleeSlotValue = null;
                }
            }

            ResolveWeaponSelectionMembers();

            return Require(mHandleAimInput, "EquipmentManager.HandleAimInput(bool)") &&
                   Require(mHandleMeleeInput, "EquipmentManager.HandleMeleeInput(bool)") &&
                   Require(mPullTrigger, "EquipmentManager.PullTrigger()") &&
                   Require(mReleaseTrigger, "EquipmentManager.ReleaseTrigger()") &&
                   RequireArity(mChargeBasicMelee, 0, "EquipmentManager.ChargeBasicMelee()") &&
                   RequireArity(mUseBasicMelee, 0, "EquipmentManager.UseBasicMelee()") &&
                   RequireArity(mOnMeleeDone, 0, "EquipmentManager.OnMeleeDone()") &&
                   Require(mReportMeleeDone, "Weapon.ReportMeleeDone()") &&
                   RequireArity(mIsMeleeCharging, 0, "Weapon.IsMeleeCharging()") &&
                   Require(mChargeMelee, "Weapon.ChargeMelee(bool)") &&
                   Require(mSetAlternativeState, "Weapon.SetAlternativeState(int)") &&
                   Require(pIsMelee, "Weapon.IsMelee") &&
                   Require(pAnimator, "Holdable.Animator") &&
                   Require(fAltFireAction, "EquipmentManager.altFireAction") &&
                   Require(fMeleeFireAction, "EquipmentManager.meleeFireAction") &&
                   Require(fMeleeFireActionAlternative, "EquipmentManager.meleeFireActionAlternative") &&
                   Require(fCurrentHoldable, "EquipmentManager.currentHoldable") &&
                   Require(fEquipmentManager, "Weapon.equipmentManager") &&
                   Require(fIsInMeleeCharge, "EquipmentManager.isInMeleeCharge") &&
                   Require(fMeleePressed, "EquipmentManager.meleePressed") &&
                   Require(fAlternativeMeleePressed, "EquipmentManager.alternativeMeleePressed") &&
                   Require(fAimingInputHeld, "EquipmentManager.AimingInputHeld") &&
                   Require(fMeleeInputCooldown, "EquipmentManager.meleeInputCooldown");
        }

        private static bool Require(MemberInfo member, string name)
        {
            if (member != null)
                return true;

            Log?.LogError("Missing required member: " + name);
            return false;
        }

        // For methods that are looked up by name and then invoked with a fixed argument list:
        // a trailing optional parameter added by a game update still resolves by name, but
        // MethodInfo.Invoke would then throw TargetParameterCountException on every call.
        private static bool RequireArity(MethodInfo method, int parameterCount, string name)
        {
            if (!Require(method, name))
                return false;

            int actual = method.GetParameters().Length;

            if (actual == parameterCount)
                return true;

            Log?.LogError(
                "Incompatible signature for required member: " + name +
                " (expected " + parameterCount + " parameter(s), the game method has " +
                actual + ")"
            );
            return false;
        }

        // Members used when a weapon selection ends the stance. All optional: each entry point
        // checks what it needs before it is patched. Methods that are invoked later are looked up
        // with their exact parameter types, so a changed signature leaves them unresolved instead
        // of failing on every call.
        private static void ResolveWeaponSelectionMembers()
        {
            if (pWeaponCurrentSlot == null || !pWeaponCurrentSlot.PropertyType.IsEnum)
                return;

            Type slotType = pWeaponCurrentSlot.PropertyType;

            inventorySlotNoneValue = ParseEnumOrNull(slotType, "None");

            changeWeaponSignatureMatches =
                HasParameterTypes(mChangeWeapon, slotType, typeof(bool), typeof(bool)) &&
                mChangeWeapon.GetParameters()[1].Name == "forced" &&
                mChangeWeapon.GetParameters()[2].Name == "navigatedDirectlyTo";

            mNextWeapon = AccessTools.Method(equipmentManagerType, "NextWeapon", Type.EmptyTypes);
            mPreviousWeapon = AccessTools.Method(equipmentManagerType, "PreviousWeapon", new Type[] { slotType });
            mSelectLastUsedWeapon = AccessTools.Method(equipmentManagerType, "SelectLastUsedWeapon", Type.EmptyTypes);

            pWeaponSwitchingPrevented = FindPropertyRecursive(equipmentManagerType, "weaponSwitchingPrevented");
            pGamepadUsed = FindPropertyRecursive(equipmentManagerType, "GamepadUsed");

            if (pWeaponSwitchingPrevented != null && pWeaponSwitchingPrevented.PropertyType != typeof(bool))
                pWeaponSwitchingPrevented = null;

            if (pGamepadUsed != null && pGamepadUsed.PropertyType != typeof(bool))
                pGamepadUsed = null;

            // equippedItems is Dictionary<InventorySlot, InventoryItem>; the item type is taken from
            // it rather than looked up by name.
            fEquippedItems = FindFieldRecursive(equipmentManagerType, "equippedItems");

            Type[] dictionaryTypes = fEquippedItems != null && fEquippedItems.FieldType.IsGenericType
                ? fEquippedItems.FieldType.GetGenericArguments()
                : null;

            if (dictionaryTypes == null ||
                dictionaryTypes.Length != 2 ||
                dictionaryTypes[0] != slotType ||
                !typeof(System.Collections.IDictionary).IsAssignableFrom(fEquippedItems.FieldType))
            {
                fEquippedItems = null;
                return;
            }

            Type inventoryItemType = dictionaryTypes[1];

            // InventoryItem.Use gained its withKeyBind parameter in 1.0.2.
            mInventoryItemUse = AccessTools.Method(inventoryItemType, "Use", new Type[] { typeof(bool) });
            pInventoryItemDefinition = FindPropertyRecursive(inventoryItemType, "itemDefinition");
            pInventoryItemUseType = FindPropertyRecursive(inventoryItemType, "UseType");

            if (pInventoryItemDefinition != null)
                fItemDefinitionPrefab = FindFieldRecursive(pInventoryItemDefinition.PropertyType, "prefab");

            if (fItemDefinitionPrefab != null && !typeof(UnityEngine.Object).IsAssignableFrom(fItemDefinitionPrefab.FieldType))
                fItemDefinitionPrefab = null;

            if (pInventoryItemUseType != null && pInventoryItemUseType.PropertyType.IsEnum)
                consumableUseTypeValue = ParseEnumOrNull(pInventoryItemUseType.PropertyType, "Consumable");
        }

        private static object ParseEnumOrNull(Type enumType, string name)
        {
            try
            {
                return Enum.Parse(enumType, name);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static bool HasParameterTypes(MethodInfo method, params Type[] parameterTypes)
        {
            if (method == null)
                return false;

            ParameterInfo[] parameters = method.GetParameters();

            if (parameters.Length != parameterTypes.Length)
                return false;

            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].ParameterType != parameterTypes[i])
                    return false;
            }

            return true;
        }

        private static bool IsWeaponSelectionBaseResolved()
        {
            return pWeaponCurrentSlot != null &&
                   basicMeleeSlotValue != null &&
                   inventorySlotNoneValue != null;
        }

        private static bool CanHandleWeaponSlotKeys()
        {
            return IsWeaponSelectionBaseResolved() &&
                   changeWeaponSignatureMatches &&
                   fEquippedItems != null &&
                   pInventoryItemDefinition != null &&
                   fItemDefinitionPrefab != null &&
                   pInventoryItemUseType != null &&
                   consumableUseTypeValue != null &&
                   mInventoryItemUse != null &&
                   pGamepadUsed != null &&
                   pWeaponSwitchingPrevented != null;
        }

        private void PatchWeaponSelectionEntry(MethodInfo original, string prefix, string name)
        {
            if (original != null && IsWeaponSelectionBaseResolved())
            {
                Patch(original, prefix: prefix);
                return;
            }

            Logger.LogWarning(
                "Weapon selection through " + name + " will not leave melee stance: " +
                "the method or the slot members it needs could not be resolved."
            );
        }

        private void Patch(
            MethodInfo original,
            string prefix = null,
            string postfix = null
        )
        {
            HarmonyMethod prefixMethod = null;
            HarmonyMethod postfixMethod = null;

            if (!string.IsNullOrEmpty(prefix))
            {
                prefixMethod = new HarmonyMethod(
                    AccessTools.Method(typeof(Plugin), prefix)
                );
            }

            if (!string.IsNullOrEmpty(postfix))
            {
                postfixMethod = new HarmonyMethod(
                    AccessTools.Method(typeof(Plugin), postfix)
                );
            }

            harmony.Patch(
                original,
                prefix: prefixMethod,
                postfix: postfixMethod
            );
        }

        private static FieldInfo FindFieldRecursive(Type type, params string[] names)
        {
            Type current = type;

            while (current != null)
            {
                foreach (string name in names)
                {
                    FieldInfo field = AccessTools.Field(current, name);

                    if (field != null)
                        return field;
                }

                current = current.BaseType;
            }

            return null;
        }

        private static PropertyInfo FindPropertyRecursive(Type type, string name)
        {
            Type current = type;

            while (current != null)
            {
                PropertyInfo property = AccessTools.Property(current, name);

                if (property != null)
                    return property;

                current = current.BaseType;
            }

            return null;
        }

        private static ToggleState GetState(object equipmentManager)
        {
            return States.GetValue(
                equipmentManager,
                _ => new ToggleState()
            );
        }

        public static bool IsRuntimeReady
        {
            get
            {
                return EnableMod != null && equipmentManagerType != null && weaponType != null;
            }
        }

        public static bool IsMeleeStanceActive(object equipmentManager)
        {
            if (equipmentManager == null || !IsRuntimeReady || !IsModActive())
                return false;

            ToggleState state = GetState(equipmentManager);
            return state != null && state.IsToggled;
        }

        public static bool IsAttackInProgress(object equipmentManager)
        {
            if (equipmentManager == null || !IsRuntimeReady || !IsModActive())
                return false;

            ToggleState state = GetState(equipmentManager);
            return state != null && state.AttackInProgress;
        }

        public static bool IsSheatheAfterAttack(object equipmentManager)
        {
            if (equipmentManager == null || !IsRuntimeReady || !IsModActive())
                return false;

            ToggleState state = GetState(equipmentManager);
            return state != null && state.SheatheAfterAttack;
        }

        public static bool IsMeleeChargeActive(object equipmentManager)
        {
            if (equipmentManager == null || !IsRuntimeReady || !IsModActive())
                return false;

            return IsInMeleeCharge(equipmentManager);
        }

        public static object GetCurrentHoldableForExternal(object equipmentManager)
        {
            if (equipmentManager == null || !IsRuntimeReady)
                return null;

            return GetCurrentHoldable(equipmentManager);
        }

        public static bool IsCurrentHoldableMeleeForExternal(object equipmentManager)
        {
            object holdable = GetCurrentHoldableForExternal(equipmentManager);
            return IsMeleeWeapon(holdable);
        }

        private static bool IsModActive()
        {
            return EnableMod != null && EnableMod.Value;
        }

        private static void HandleAimInputPrefix(
            object __instance,
            ref bool holdingMeleeAction
        )
        {
            if (!IsModActive())
                return;

            ToggleState state = GetState(__instance);

            if (!state.IsToggled)
                return;

            holdingMeleeAction = true;
        }

        private static bool HandleMeleeInputPrefix(
            object __instance,
            bool holdingMeleeAction
        )
        {
            if (!IsModActive())
                return true;

            ToggleState state = GetState(__instance);

            // The game ran its equipment input update this frame (see HandleWeaponSelectionRequest).
            state.LastEquipmentInputTime = Time.time;

            // Mark the stance as actively maintained this frame so the interaction fix knows
            // the equipment input pump is live (see CanInteractPostfix).
            if (state.IsToggled)
                state.LastMaintainTime = Time.time;

            MeleeKeyMode mode = GetMeleeKeyMode();

            // MeleeInputMode can change while the game runs (SULFUR Config applies it live). Tap /
            // hold tracking from the previous mode must not decide a press in the new one.
            if (state.AppliedMeleeKeyMode != mode)
            {
                state.AppliedMeleeKeyMode = mode;
                ResetTapHoldTracking(state, holdingMeleeAction || IsMeleeHeld(__instance));
            }

            if (state.SuppressMeleeUntilReleased)
            {
                bool heldWhileSuppressed = holdingMeleeAction || IsMeleeHeld(__instance);

                state.WasMeleeHeldLastFrame = heldWhileSuppressed;

                if (heldWhileSuppressed)
                {
                    return false;
                }

                state.SuppressMeleeUntilReleased = false;

                if (LogStateChanges.Value)
                {
                    Log?.LogInfo("Melee input released after manual sheathe. Input suppression cleared.");
                }

                return false;
            }

            if (!state.IsToggled && IsAimingInputHeld(__instance) && holdingMeleeAction)
                return true;

            if (!state.IsToggled && IsMeleeInputCoolingDown(__instance))
                return true;

            if (!state.IsToggled && mode != MeleeKeyMode.PressToggles)
            {
                // The melee key is left to the game: it charges on press and swings on release.
                // Fire or the last used weapon key during that charge enters the stance (see
                // TryEnterStanceFromChord).
                if (mode == MeleeKeyMode.QuickMeleeChordToggle)
                    return true;

                bool prefixResult;

                bool handled =
                    mode == MeleeKeyMode.TapQuickMeleeHoldToggle
                        ? HandleTapHoldMeleeInputSwapped(__instance, state, holdingMeleeAction, out prefixResult)
                        : HandleTapHoldMeleeInput(__instance, state, holdingMeleeAction, out prefixResult);

                if (handled)
                {
                    return prefixResult;
                }
            }
            else
            {
                bool meleePressedThisFrame = WasMeleePressedThisFrame(__instance);

                if (meleePressedThisFrame)
                {
                    if (!state.IsToggled)
                    {
                        ToggleOn(__instance, state);
                        return false;
                    }

                    ToggleOff(__instance, state);
                    return false;
                }
            }

            if (state.IsToggled)
            {
                bool meleePressedThisFrame = WasMeleePressedThisFrame(__instance);

                if (meleePressedThisFrame)
                {
                    ToggleOff(__instance, state);
                    return false;
                }

                MaintainToggledMelee(__instance, state);
                return false;
            }

            return true;
        }

        // QuickMeleeChordToggle. The melee key works as in the base game: a press starts the quick
        // melee charge (ChargeBasicMelee) and the release swings it (UseBasicMelee). Pressing Fire
        // or the last used weapon key while that charge is held turns it into the stance instead,
        // the same promotion the swapped tap / hold mode makes at its threshold. The base game does
        // nothing with either input at that point: the melee weapon is drawn and
        // weaponSwitchingPrevented is set. Returns true when the stance was entered; the caller
        // then skips the game's method.
        private static bool TryEnterStanceFromChord(object equipmentManager, string source)
        {
            if (!IsModActive() || equipmentManager == null)
                return false;

            if (GetMeleeKeyMode() != MeleeKeyMode.QuickMeleeChordToggle)
                return false;

            ToggleState state = GetState(equipmentManager);

            if (state.IsToggled ||
                state.AttackInProgress ||
                state.SheatheAfterAttack ||
                state.SuppressMeleeUntilReleased)
            {
                return false;
            }

            // Only while the game is running its equipment input update.
            if (Time.time - state.LastEquipmentInputTime > StanceLivenessWindow)
                return false;

            // A quick melee charge that is still held. ChargeBasicMeleeRoutine sets meleePressed on
            // the press frame and UseBasicMelee clears it when the swing is released, so a key
            // pressed during the swing does not count. isInMeleeCharge is set one frame after the
            // press, which also leaves out the press frame itself: the game reads Fire before the
            // melee key in the same frame, so that Fire has already gone to the previous weapon.
            if (!IsMeleeHeld(equipmentManager) ||
                !GetBoolField(fMeleePressed, equipmentManager) ||
                !IsInMeleeCharge(equipmentManager) ||
                !IsBasicMeleeSlotSelected(equipmentManager))
            {
                return false;
            }

            object currentHoldable = GetCurrentHoldable(equipmentManager);

            if (!IsMeleeWeapon(currentHoldable) || !IsMeleeCharging(currentHoldable))
                return false;

            if (LogStateChanges.Value)
            {
                Log?.LogInfo("Melee key held with " + source + ". Quick melee charge becomes melee stance.");
            }

            // ChargeBasicMelee inside ToggleOn does nothing here: the charge is already running.
            ToggleOn(equipmentManager, state);
            return true;
        }

        private static void ResetTapHoldTracking(ToggleState state, bool meleeHeld)
        {
            state.PendingTapHold = false;
            state.LongPressPassThrough = false;
            state.MeleePressStartTime = 0f;

            // A press that is already in progress is not treated as a new one.
            state.WasMeleeHeldLastFrame = meleeHeld;
        }

        private static bool PullTriggerPrefix(object __instance)
        {
            if (!IsModActive())
                return true;

            ToggleState state = GetState(__instance);

            // QuickMeleeChordToggle: Fire during a quick melee charge enters the stance instead.
            if (!state.IsToggled && TryEnterStanceFromChord(__instance, "Fire"))
                return false;

            if (!state.IsToggled)
                return true;

            if (ExitToggledStateIfExternalNonMeleeHoldable(__instance, state, "PullTrigger"))
            {
                return true;
            }

            if (!FirePerformsMeleeAttack.Value)
                return false;

            object currentHoldable = GetCurrentHoldable(__instance);

            if (!IsMeleeWeapon(currentHoldable))
            {
                MaintainToggledMelee(__instance, state);
                return false;
            }

            if (!IsMeleeCharging(currentHoldable))
            {
                MaintainToggledMelee(__instance, state);
                return false;
            }

            if (state.AttackInProgress)
                return false;

            state.AttackInProgress = true;
            state.SheatheAfterAttack = false;
            state.SuppressMeleeUntilReleased = false;

            if (LogStateChanges.Value)
            {
                Log?.LogInfo("Fire action converted to melee attack.");
            }

            InvokeUseBasicMelee(__instance);

            return false;
        }

        private static bool ReleaseTriggerPrefix(object __instance)
        {
            if (!IsModActive())
                return true;

            ToggleState state = GetState(__instance);

            if (!state.IsToggled &&
                !state.AttackInProgress &&
                !state.SheatheAfterAttack)
            {
                return true;
            }

            // Only the melee weapon held by the stance has its trigger release handled here. A
            // release aimed at any other holdable must reach it: PullTriggerPrefix lets pulls on
            // non-melee holdables through, and swallowing the matching release would leave that
            // weapon's trigger latched (automatic weapons keep firing, others cannot fire again).
            // This also covers the release the game sends to the outgoing firearm when the stance
            // switches to the basic melee slot.
            if (!IsMeleeWeapon(GetCurrentHoldable(__instance)))
                return true;

            return false;
        }

        private static void ReportMeleeDonePrefix(object __instance)
        {
            if (!IsModActive())
                return;

            object equipmentManager = GetEquipmentManagerFromWeapon(__instance);

            if (equipmentManager == null)
                return;

            ToggleState state = GetState(equipmentManager);

            if (!state.IsToggled &&
                !state.AttackInProgress &&
                !state.SheatheAfterAttack)
            {
                return;
            }

            // The game's ReportMeleeDone switches back to the cached slot through OnMeleeDone and
            // ChangeWeapon before our postfix runs; mark that this slot exit is the attack's own
            // completion so ChangeWeaponPostfix leaves it to ReportMeleeDonePostfix.
            state.CompletingMeleeAttack = true;

            PrepareMeleeAnimatorForFutureDraw(__instance);
        }

        private static void ReportMeleeDonePostfix(object __instance)
        {
            if (!IsModActive())
                return;

            object equipmentManager = GetEquipmentManagerFromWeapon(__instance);

            if (equipmentManager == null)
                return;

            ToggleState state = GetState(equipmentManager);

            state.CompletingMeleeAttack = false;

            if (state.SheatheAfterAttack)
            {
                state.AttackInProgress = false;
                state.SheatheAfterAttack = false;
                state.IsToggled = false;
                state.SuppressMeleeUntilReleased = true;
                state.NextChargeAttemptTime = 0f;
                state.PendingTapHold = false;
                state.LongPressPassThrough = false;
                state.WasMeleeHeldLastFrame = false;
                state.MeleePressStartTime = 0f;

                if (LogStateChanges.Value)
                {
                    Log?.LogInfo("Melee attack finished. Staying sheathed.");
                }

                // The stance ends here. A slot selection replayed below is part of this exit, so
                // the remembered slot follows it (ChangeWeaponPostfix).
                RememberStanceAsLastUsed(equipmentManager, state);

                // The game has switched back to the cached weapon and allows weapon changes again,
                // so a selection the player made during the attack can now be performed.
                ReplayPendingWeaponSelection(equipmentManager, state);

                return;
            }

            if (!state.IsToggled)
            {
                state.AttackInProgress = false;
                state.NextChargeAttemptTime = 0f;
                return;
            }

            state.AttackInProgress = false;
            state.NextChargeAttemptTime = Time.time + ClampRetryInterval();

            state.PendingTapHold = false;
            state.LongPressPassThrough = false;
            state.WasMeleeHeldLastFrame = false;
            state.MeleePressStartTime = 0f;

            if (LogStateChanges.Value)
            {
                Log?.LogInfo("Melee attack finished. Re-entering toggled melee stance.");
            }

            InvokeChargeBasicMelee(equipmentManager);
        }

        private static void ChargeMeleePostfix(object __instance, bool state)
        {
            if (!IsModActive())
                return;

            if (!state)
                return;

            CacheSafeMeleeAnimatorStateIfUseful(__instance, "ChargeMelee(true)");
        }

        // ChangeWeapon is where the equipment actually leaves the basic melee slot. A toggled
        // melee attack normally ends through Weapon.ReportMeleeDone, an animation event on the
        // melee weapon. When the game puts that weapon away by itself first (Endless mode card
        // selection deactivates the holdable and then calls OnMeleeDone; inventory changes,
        // forced amulet selection and other mods use forced ChangeWeapon calls), the event never
        // arrives and AttackInProgress / SheatheAfterAttack would stay set indefinitely. Those
        // stale flags stop MaintainToggledMelee from re-entering the stance and made
        // ReleaseTriggerPrefix swallow trigger releases. Resolve them here, at the slot exit.
        private static void ChangeWeaponPostfix(object __instance)
        {
            if (!IsModActive() || __instance == null)
                return;

            ToggleState state;

            if (!States.TryGetValue(__instance, out state) || state == null)
                return;

            // "The stance was the last used item" (see RememberStanceAsLastUsed). A weapon change in
            // the frame the stance ended is part of that exit, so the slot follows it. Any later
            // switch to another slot is an ordinary weapon change and ends it.
            if (state.StanceIsLastUsed)
            {
                if (Time.frameCount == state.StanceExitFrame && !IsBasicMeleeSlotSelected(__instance))
                    state.StanceReturnSlot = pWeaponCurrentSlot.GetValue(__instance, null);
                else if (!IsStanceReturnSlotSelected(__instance, state))
                    ClearStanceAsLastUsed(state);
            }

            if (!state.AttackInProgress && !state.SheatheAfterAttack)
                return;

            // The attack is completing normally; ReportMeleeDonePostfix owns this transition.
            if (state.CompletingMeleeAttack)
                return;

            // Entering the slot, or a ChangeWeapon call that did not change anything.
            if (IsBasicMeleeSlotSelected(__instance))
                return;

            AbandonInterruptedMeleeAttack(__instance, state);
        }

        private static bool IsBasicMeleeSlotSelected(object equipmentManager)
        {
            if (pWeaponCurrentSlot == null || basicMeleeSlotValue == null || equipmentManager == null)
                return false;

            object slot = pWeaponCurrentSlot.GetValue(equipmentManager, null);

            return basicMeleeSlotValue.Equals(slot);
        }

        // Ends the bookkeeping for a melee attack whose weapon was put away before it reported
        // completion, leaving the flags as ReportMeleeDonePostfix would have left them. The game
        // has already committed its own weapon change, so nothing is invoked on the game here:
        // a stance that is still toggled is re-entered by MaintainToggledMelee on its normal
        // retry schedule (or exited by the existing external weapon switch handling).
        private static void AbandonInterruptedMeleeAttack(object equipmentManager, ToggleState state)
        {
            bool sheathe = state.SheatheAfterAttack;

            state.AttackInProgress = false;
            state.SheatheAfterAttack = false;
            state.PendingTapHold = false;
            state.LongPressPassThrough = false;
            state.WasMeleeHeldLastFrame = false;
            state.MeleePressStartTime = 0f;

            if (sheathe)
            {
                // The player had already asked to leave the stance during this attack.
                state.IsToggled = false;
                state.SuppressMeleeUntilReleased = true;
                state.NextChargeAttemptTime = 0f;

                // The stance ends on the weapon the game has switched to.
                RememberStanceAsLastUsed(equipmentManager, state);
            }
            else if (state.IsToggled)
            {
                state.NextChargeAttemptTime = Time.time + ClampRetryInterval();
            }
            else
            {
                state.NextChargeAttemptTime = 0f;
            }

            if (LogStateChanges != null && LogStateChanges.Value)
            {
                Log?.LogInfo(
                    "Melee attack interrupted by a weapon change before it finished. " +
                    (sheathe
                        ? "Staying sheathed."
                        : state.IsToggled
                            ? "Toggle melee stays ON and will re-enter the melee charge."
                            : "Attack state cleared.") +
                    " slot=" + (pWeaponCurrentSlot != null ? pWeaponCurrentSlot.GetValue(equipmentManager, null) : null)
                );
            }
        }

        // A weapon slot key (Slot1-Slot5 call ChangeWeapon with navigatedDirectlyTo). While the
        // stance holds the basic melee slot the game sets weaponSwitchingPrevented, so the key
        // would otherwise be ignored. Forced calls and calls without navigatedDirectlyTo come from
        // the game itself and are left alone.
        private static void ChangeWeaponPrefix(
            object __instance,
            object[] __args,
            bool forced,
            bool navigatedDirectlyTo
        )
        {
            if (forced || !navigatedDirectlyTo || __args == null || __args.Length == 0)
                return;

            // A slot key never skips the game's method: it performs the switch to the chosen slot.
            HandleWeaponSelectionRequest(__instance, WeaponSelectionRequest.Slot, __args[0], "slot key");
        }

        // Weapon cycling (scroll wheel, gamepad d-pad). NextWeapon and PreviousWeapon do nothing
        // while the basic melee slot is selected. When the stance ends here the game's method is
        // skipped (see HandleWeaponSelectionRequest).
        private static bool NextWeaponPrefix(object __instance)
        {
            return !HandleWeaponSelectionRequest(__instance, WeaponSelectionRequest.Next, null, "next weapon");
        }

        private static bool PreviousWeaponPrefix(object __instance, object[] __args)
        {
            // The input path calls PreviousWeapon without a starting slot; using up the last
            // consumable of a gadget slot passes that slot instead.
            if (__args == null || __args.Length == 0 || !inventorySlotNoneValue.Equals(__args[0]))
                return true;

            return !HandleWeaponSelectionRequest(__instance, WeaponSelectionRequest.Previous, null, "previous weapon");
        }

        // The last used weapon key (LastUsedSlot). When the stance ends here the game's method is
        // skipped (see HandleWeaponSelectionRequest).
        private static bool SelectLastUsedWeaponPrefix(object __instance)
        {
            // QuickMeleeChordToggle: the key during a quick melee charge enters the stance instead.
            if (TryEnterStanceFromChord(__instance, "last used weapon key"))
                return false;

            // After the stance has ended, the key goes back into it: the stance counts as one of the
            // two most recently used items the key alternates between.
            if (TryReturnToStance(__instance))
                return false;

            return !HandleWeaponSelectionRequest(__instance, WeaponSelectionRequest.LastUsed, null, "last used weapon");
        }

        // Shared handling for a weapon selection made while the stance is on. It changes only the
        // stance state (through ToggleOff when the game must end its melee charge). Returns true
        // when the game's own selection method must be skipped.
        //
        // The stance stands in for the weapon that was out before it. The last used weapon key and
        // weapon cycling therefore only end the stance and stay on that weapon, the same way the
        // game returns to it after a quick melee, and the way the last used weapon key returns from
        // the amulet. The game's method would otherwise run from the restored weapon and move one
        // weapon further, because lastSelectedSlot and the cycling order ignore the basic melee
        // slot. A slot key ends the stance and lets the game's method switch to the chosen slot
        // with its usual checks.
        private static bool HandleWeaponSelectionRequest(
            object equipmentManager,
            WeaponSelectionRequest request,
            object slot,
            string source
        )
        {
            if (!IsModActive() ||
                WeaponSwitchExitsMeleeStance == null ||
                !WeaponSwitchExitsMeleeStance.Value ||
                equipmentManager == null)
            {
                return false;
            }

            ToggleState state;

            if (!States.TryGetValue(equipmentManager, out state) || state == null)
                return false;

            // The game's own attack completion is changing weapons; ReportMeleeDonePostfix owns it.
            if (state.CompletingMeleeAttack)
                return false;

            // A sheathe is already pending for the current attack (melee key or an earlier selection).
            bool sheathePending = !state.IsToggled && state.SheatheAfterAttack && state.AttackInProgress;

            if (!state.IsToggled && !sheathePending)
                return false;

            // Only while the game is running its equipment input update. Slot callbacks keep firing
            // in states where it is halted (flashback, straitjacket status); those keep vanilla behavior.
            if (Time.time - state.LastEquipmentInputTime > StanceLivenessWindow)
                return false;

            // The last used weapon key and weapon cycling return to the weapon from before the stance.
            bool returnsToStanceWeapon = request != WeaponSelectionRequest.Slot;

            if (request == WeaponSelectionRequest.Slot)
            {
                if (IsDirectConsumableUse(equipmentManager, slot))
                {
                    UseConsumableInStance(equipmentManager, slot);
                    return false;
                }

                // An empty slot is ignored by the game, so the stance stays.
                if (!IsSlotSelectableByChangeWeapon(equipmentManager, slot))
                    return false;
            }

            if (!IsBasicMeleeSlotSelected(equipmentManager))
            {
                // Between melee charges the weapon from before the stance is the one drawn.
                ExitStanceWithoutMeleeWeapon(equipmentManager, state, source);
                return returnsToStanceWeapon;
            }

            if (sheathePending)
            {
                if (returnsToStanceWeapon)
                {
                    // The pending sheathe already returns to the weapon from before the stance.
                    // This also replaces a slot selection made earlier in the same attack.
                    ClearPendingWeaponSelection(state);
                    return true;
                }

                SetPendingWeaponSelection(state, request, slot, source);
                return false;
            }

            if (state.AttackInProgress)
            {
                // ToggleOff only marks the stance to sheathe once the attack finishes; the game
                // still refuses the switch now, so a slot selection is performed after the attack.
                // The sheathe itself returns to the weapon from before the stance.
                ToggleOff(equipmentManager, state);

                if (returnsToStanceWeapon)
                    return true;

                SetPendingWeaponSelection(state, request, slot, source);
                return false;
            }

            // The melee weapon has been selected but ChargeBasicMeleeRoutine has not started its
            // charge yet (one frame). Ending the stance now would let that routine charge the
            // weapon the game switches back to, so the selection is ignored like any switch
            // during a draw.
            if (!IsInMeleeCharge(equipmentManager))
                return false;

            if (LogStateChanges != null && LogStateChanges.Value)
            {
                Log?.LogInfo(
                    "Weapon selected in melee stance (" + source + "). Leaving melee stance" +
                    (returnsToStanceWeapon ? " and returning to the weapon from before it." : ". slot=" + slot)
                );
            }

            // OnMeleeDone (through ToggleOff) clears weaponSwitchingPrevented and restores the
            // cached weapon; a slot selection then continues from there in the game's method.
            ToggleOff(equipmentManager, state);

            // Skip the game's method only when the stance actually left the melee slot.
            return returnsToStanceWeapon && !IsBasicMeleeSlotSelected(equipmentManager);
        }

        // Mirrors the check ChangeWeapon makes before using a consumable from a slot key: the slot
        // holds a consumable, the selection came from a key, and no gamepad is in use.
        private static bool IsDirectConsumableUse(object equipmentManager, object slot)
        {
            object item = GetEquippedItem(equipmentManager, slot);

            if (item == null)
                return false;

            if (!consumableUseTypeValue.Equals(pInventoryItemUseType.GetValue(item, null)))
                return false;

            return !GetBoolProperty(pGamepadUsed, equipmentManager);
        }

        // Uses a consumable from a slot key without leaving the stance. ChangeWeapon would use it
        // itself but returns first while weaponSwitchingPrevented is set. With withKeyBind the
        // item use only consumes the item (and refills the slot from the backpack); it does not
        // change the selected weapon.
        private static void UseConsumableInStance(object equipmentManager, object slot)
        {
            // When switching is allowed the game's own call uses the item; using it here as well
            // would use it twice. The same applies when the slot is already the current one, where
            // the game returns before using anything.
            if (!GetBoolProperty(pWeaponSwitchingPrevented, equipmentManager))
                return;

            if (slot == null || slot.Equals(pWeaponCurrentSlot.GetValue(equipmentManager, null)))
                return;

            object item = GetEquippedItem(equipmentManager, slot);

            if (item == null)
                return;

            if (LogStateChanges != null && LogStateChanges.Value)
            {
                Log?.LogInfo("Consumable used from a slot key in melee stance. Melee stance stays ON. slot=" + slot);
            }

            try
            {
                mInventoryItemUse.Invoke(item, new object[] { true });
            }
            catch (Exception ex)
            {
                Log?.LogError("Failed to invoke InventoryItem.Use: " + ex);
            }
        }

        // Mirrors ChangeWeapon's own acceptance check for a slot: it must hold an item that has a
        // prefab or is a consumable.
        private static bool IsSlotSelectableByChangeWeapon(object equipmentManager, object slot)
        {
            object item = GetEquippedItem(equipmentManager, slot);

            if (item == null)
                return false;

            object definition = pInventoryItemDefinition.GetValue(item, null);
            UnityEngine.Object prefab = definition != null
                ? fItemDefinitionPrefab.GetValue(definition) as UnityEngine.Object
                : null;

            if (prefab != null)
                return true;

            return consumableUseTypeValue.Equals(pInventoryItemUseType.GetValue(item, null));
        }

        private static object GetEquippedItem(object equipmentManager, object slot)
        {
            if (fEquippedItems == null || equipmentManager == null || slot == null)
                return null;

            System.Collections.IDictionary items = fEquippedItems.GetValue(equipmentManager) as System.Collections.IDictionary;

            if (items == null || !items.Contains(slot))
                return null;

            return items[slot];
        }

        // The stance is on but the game is not holding the basic melee slot: it is between melee
        // charges and MaintainToggledMelee is retrying. The game does not block the switch here,
        // so only the stance state is cleared. OnMeleeDone must not be called in this state: it
        // hides the renderers of the weapon that is already drawn and then returns without
        // selecting it again.
        private static void ExitStanceWithoutMeleeWeapon(object equipmentManager, ToggleState state, string source)
        {
            state.IsToggled = false;
            state.AttackInProgress = false;
            state.SheatheAfterAttack = false;
            state.SuppressMeleeUntilReleased = true;
            state.NextChargeAttemptTime = 0f;

            state.PendingTapHold = false;
            state.LongPressPassThrough = false;
            state.WasMeleeHeldLastFrame = false;
            state.MeleePressStartTime = 0f;
            ClearPendingWeaponSelection(state);

            SetMeleePressed(equipmentManager, false);
            SetAlternativeMeleePressed(equipmentManager, false);

            // The stance ends here. A slot key goes on to its slot in the same frame, and the
            // remembered slot follows it (ChangeWeaponPostfix).
            RememberStanceAsLastUsed(equipmentManager, state);

            if (LogStateChanges != null && LogStateChanges.Value)
            {
                Log?.LogInfo(
                    "Weapon selected in melee stance (" + source + ") while the melee weapon was not drawn. " +
                    "Toggle melee OFF."
                );
            }
        }

        // The last used weapon key alternates between the two most recently used weapons, and the
        // stance counts as one of them. Every way the stance ends records it here (ToggleOff, the
        // completed or interrupted sheathe, the retry-window exit, the external weapon switch exit),
        // so the next press of the key goes back into the stance. The slot is taken once the exit
        // has settled: a ChangeWeapon later in the same frame (a slot key, a replayed selection) is
        // part of the exit and moves the slot along (ChangeWeaponPostfix). A later switch to any
        // other slot, every ToggleOn, and turning the option off clear it.
        private static void RememberStanceAsLastUsed(object equipmentManager, ToggleState state)
        {
            if (WeaponSwitchExitsMeleeStance == null ||
                !WeaponSwitchExitsMeleeStance.Value ||
                pWeaponCurrentSlot == null ||
                basicMeleeSlotValue == null ||
                equipmentManager == null)
            {
                ClearStanceAsLastUsed(state);
                return;
            }

            object slot = pWeaponCurrentSlot.GetValue(equipmentManager, null);

            if (slot == null || basicMeleeSlotValue.Equals(slot))
            {
                ClearStanceAsLastUsed(state);
                return;
            }

            state.StanceIsLastUsed = true;
            state.StanceReturnSlot = slot;
            state.StanceExitFrame = Time.frameCount;
        }

        private static void ClearStanceAsLastUsed(ToggleState state)
        {
            state.StanceIsLastUsed = false;
            state.StanceReturnSlot = null;
        }

        private static bool IsStanceReturnSlotSelected(object equipmentManager, ToggleState state)
        {
            if (pWeaponCurrentSlot == null || equipmentManager == null || state.StanceReturnSlot == null)
                return false;

            return state.StanceReturnSlot.Equals(pWeaponCurrentSlot.GetValue(equipmentManager, null));
        }

        // The last used weapon key pressed while the stance is off and was the last used item:
        // enter it through the same ToggleOn path the melee key uses from a weapon. Returns true
        // when the stance was entered; the caller then skips the game's method.
        private static bool TryReturnToStance(object equipmentManager)
        {
            if (!IsModActive() || equipmentManager == null)
                return false;

            ToggleState state;

            if (!States.TryGetValue(equipmentManager, out state) || state == null || !state.StanceIsLastUsed)
                return false;

            if (WeaponSwitchExitsMeleeStance == null || !WeaponSwitchExitsMeleeStance.Value)
            {
                ClearStanceAsLastUsed(state);
                return false;
            }

            if (state.IsToggled ||
                state.AttackInProgress ||
                state.SheatheAfterAttack ||
                state.CompletingMeleeAttack)
            {
                return false;
            }

            // Only while the game is running its equipment input update.
            if (Time.time - state.LastEquipmentInputTime > StanceLivenessWindow)
                return false;

            // Still on the slot the stance exit settled on. A later switch normally clears the flag
            // already; this also covers a game without the ChangeWeapon postfix.
            if (!IsStanceReturnSlotSelected(equipmentManager, state))
            {
                ClearStanceAsLastUsed(state);
                return false;
            }

            if (fEquippedItems != null)
            {
                // The item in the remembered slot is gone (no slot is also fine: nothing in hand).
                if (!state.StanceReturnSlot.Equals(inventorySlotNoneValue) &&
                    GetEquippedItem(equipmentManager, state.StanceReturnSlot) == null)
                {
                    ClearStanceAsLastUsed(state);
                    return false;
                }

                // Without a basic melee weapon ChargeBasicMelee refuses, so the key keeps its
                // normal behavior.
                if (GetEquippedItem(equipmentManager, basicMeleeSlotValue) == null)
                    return false;
            }

            // Leave a held melee key to the melee key handling (tap / hold, or the chord).
            if (IsMeleeHeld(equipmentManager))
                return false;

            // Only the player's key press. The game also calls SelectLastUsedWeapon itself when a
            // throwable runs out, and that must still switch weapons.
            if (!WasPerformedThisFrame(GetInputAction(fSelectLastUsedAction, equipmentManager)))
                return false;

            if (LogStateChanges != null && LogStateChanges.Value)
            {
                Log?.LogInfo("Last used weapon key after leaving melee stance. Going back into melee stance.");
            }

            // ToggleOn clears StanceIsLastUsed.
            ToggleOn(equipmentManager, state);
            return true;
        }

        private static void SetPendingWeaponSelection(
            ToggleState state,
            WeaponSelectionRequest request,
            object slot,
            string source
        )
        {
            // The latest selection made during the attack wins.
            state.PendingWeaponSelection = request;
            state.PendingWeaponSelectionSlot = slot;

            if (LogStateChanges != null && LogStateChanges.Value)
            {
                Log?.LogInfo(
                    "Weapon selected during a melee attack (" + source + "). " +
                    "It will be applied after the attack finishes. slot=" + slot
                );
            }
        }

        private static void ClearPendingWeaponSelection(ToggleState state)
        {
            state.PendingWeaponSelection = WeaponSelectionRequest.None;
            state.PendingWeaponSelectionSlot = null;
        }

        // Performs the slot selection recorded during the attack through the same game method the
        // slot key used, so it goes through the game's normal checks. Only slot keys are recorded:
        // the sheathe itself already returns to the weapon the last used weapon key and weapon
        // cycling ask for. The request is cleared first, whether or not the call succeeds. A
        // selection recorded for an attack that ended through a forced weapon change
        // (AbandonInterruptedMeleeAttack) is not performed: the game has already chosen the weapon,
        // and the request is cleared on the next ToggleOn.
        private static void ReplayPendingWeaponSelection(object equipmentManager, ToggleState state)
        {
            WeaponSelectionRequest request = state.PendingWeaponSelection;
            object slot = state.PendingWeaponSelectionSlot;

            ClearPendingWeaponSelection(state);

            if (request != WeaponSelectionRequest.Slot)
                return;

            // The option may have been turned off while the attack was finishing.
            if (WeaponSwitchExitsMeleeStance == null || !WeaponSwitchExitsMeleeStance.Value)
                return;

            if (LogStateChanges != null && LogStateChanges.Value)
            {
                Log?.LogInfo("Applying the weapon selection made during the melee attack: slot=" + slot);
            }

            try
            {
                mChangeWeapon.Invoke(equipmentManager, new object[] { slot, false, true });
            }
            catch (Exception ex)
            {
                Log?.LogError("Failed to apply the weapon selection made during the melee attack: " + ex);
            }
        }

        private static bool GetBoolProperty(PropertyInfo property, object instance)
        {
            if (property == null || instance == null)
                return false;

            object value = property.GetValue(instance, null);

            return value is bool && (bool)value;
        }

        private static void CanInteractPostfix(ref bool __result)
        {
            // Vanilla already allows interaction; nothing to do.
            if (__result)
                return;

            if (!IsModActive())
                return;

            object equipmentManager = GetLocalEquipmentManager();

            if (equipmentManager == null)
                return;

            ToggleState state = GetState(equipmentManager);

            if (state == null || !state.IsToggled)
                return;

            // Only override while the stance is actively maintained this frame. When the game
            // stops driving equipment input (straitjacket status, flashback, pause) our stamp
            // goes stale and we defer to vanilla, preserving its interaction lockouts. In the
            // live case, vanilla returned false solely because the current slot is BasicMelee,
            // which is exactly the state our persistent stance holds, so interaction is restored.
            if (Time.time - state.LastMaintainTime > StanceLivenessWindow)
                return;

            __result = true;
        }

        private static object GetLocalEquipmentManager()
        {
            if (pGameManagerInstance == null || pGameManagerEquipmentManager == null)
                return null;

            object gameManager = pGameManagerInstance.GetValue(null, null);

            if (gameManager == null)
                return null;

            return pGameManagerEquipmentManager.GetValue(gameManager, null);
        }

        private static void ToggleOn(object equipmentManager, ToggleState state)
        {
            state.IsToggled = true;
            state.AttackInProgress = false;
            state.SheatheAfterAttack = false;
            state.SuppressMeleeUntilReleased = false;
            state.NextChargeAttemptTime = Time.time + ClampRetryInterval();
            state.LastMaintainTime = Time.time;
            state.CompletingMeleeAttack = false;

            state.PendingTapHold = false;
            state.LongPressPassThrough = false;
            state.WasMeleeHeldLastFrame = false;
            state.MeleePressStartTime = 0f;
            ClearPendingWeaponSelection(state);
            ClearStanceAsLastUsed(state);

            if (LogStateChanges.Value)
            {
                Log?.LogInfo("Toggle melee ON.");
            }

            InvokeChargeBasicMelee(equipmentManager);
            SetMeleePressed(equipmentManager, true);
        }

        private static void ToggleOff(object equipmentManager, ToggleState state)
        {
            if (LogStateChanges.Value)
            {
                Log?.LogInfo("Toggle melee OFF.");
            }

            state.IsToggled = false;
            state.NextChargeAttemptTime = 0f;

            state.SuppressMeleeUntilReleased = true;
            state.PendingTapHold = false;
            state.LongPressPassThrough = false;
            state.WasMeleeHeldLastFrame = false;
            state.MeleePressStartTime = 0f;

            SetAlternativeMeleePressed(equipmentManager, false);
            SetMeleePressed(equipmentManager, false);

            object currentHoldable = GetCurrentHoldable(equipmentManager);

            if (state.AttackInProgress)
            {
                state.SheatheAfterAttack = true;

                if (LogStateChanges.Value)
                {
                    Log?.LogInfo("Melee attack is in progress. Will sheathe after current attack finishes.");
                }

                return;
            }

            state.AttackInProgress = false;
            state.SheatheAfterAttack = false;

            CleanCurrentMeleeWeaponState(currentHoldable);
            InvokeOnMeleeDone(equipmentManager);

            // The stance ends here, on the weapon OnMeleeDone restored. A slot key goes on to its
            // slot in the same frame, and the remembered slot follows it (ChangeWeaponPostfix).
            RememberStanceAsLastUsed(equipmentManager, state);
        }

        private static void MaintainToggledMelee(
            object equipmentManager,
            ToggleState state
        )
        {
            if (state.AttackInProgress || state.SheatheAfterAttack)
                return;

            bool altHeld = IsAltFirePressed(equipmentManager);

            SetAlternativeMeleePressed(equipmentManager, altHeld);
            UpdateCurrentMeleeAlternativeAnimator(equipmentManager, altHeld);

            if (IsInMeleeCharge(equipmentManager))
                return;

            if (Time.time < state.NextChargeAttemptTime)
                return;

            state.NextChargeAttemptTime = Time.time + ClampRetryInterval();

            InvokeChargeBasicMelee(equipmentManager);
            SetMeleePressed(equipmentManager, true);
        }

        private static void CacheSafeMeleeAnimatorStateIfUseful(
            object weapon,
            string source
        )
        {
            if (!IsMeleeWeapon(weapon))
                return;

            Animator animator = GetAnimator(weapon);

            if (animator == null)
                return;

            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            string clipName = GetCurrentClipName(animator);

            if (!IsSafeMeleeDrawClip(clipName))
                return;

            SafeAnimatorState safeState = SafeAnimatorStates.GetValue(
                weapon,
                _ => new SafeAnimatorState()
            );

            safeState.FullPathHash = stateInfo.fullPathHash;
            safeState.ShortNameHash = stateInfo.shortNameHash;
            safeState.ClipName = clipName;
            safeState.Valid = true;

            if (LogStateChanges.Value)
            {
                Log?.LogInfo(
                    "Cached safe melee animator state from "
                    + source
                    + ": clip="
                    + clipName
                    + " fullPathHash="
                    + stateInfo.fullPathHash
                );
            }
        }

        private static bool IsSafeMeleeDrawClip(string clipName)
        {
            if (string.IsNullOrEmpty(clipName))
                return false;

            if (ContainsIgnoreCase(clipName, "Slash"))
                return false;

            if (ContainsIgnoreCase(clipName, "Attack"))
                return false;

            if (ContainsIgnoreCase(clipName, "Fire"))
                return false;

            if (ContainsIgnoreCase(clipName, "ADS"))
                return false;

            if (ContainsIgnoreCase(clipName, "ToADS"))
                return false;

            if (ContainsIgnoreCase(clipName, "Charge") ||
                ContainsIgnoreCase(clipName, "Charged"))
                return false;

            return ContainsIgnoreCase(clipName, "Equip");
        }

        private static bool ContainsIgnoreCase(string value, string part)
        {
            return value != null &&
                   part != null &&
                   value.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void PrepareMeleeAnimatorForFutureDraw(object weapon)
        {
            if (!ResetMeleeAnimatorBeforeSheathe.Value)
                return;

            if (!IsMeleeWeapon(weapon))
                return;

            try
            {
                InvokeSetAlternativeState(weapon, 0);

                if (fCurrentParries != null)
                {
                    fCurrentParries.SetValue(weapon, 0);
                }

                Animator animator = GetAnimator(weapon);

                if (animator == null)
                    return;

                animator.SetBool("Charge", false);
                animator.SetBool("Sprinting", false);
                animator.SetBool("AlternativePressed", false);
                animator.ResetTrigger("Parry");

                SafeAnimatorState safeState;

                if (!SafeAnimatorStates.TryGetValue(weapon, out safeState) ||
                    safeState == null ||
                    !safeState.Valid)
                {
                    CacheSafeMeleeAnimatorStateIfUseful(weapon, "PrepareMeleeAnimatorForFutureDraw fallback");

                    if (!SafeAnimatorStates.TryGetValue(weapon, out safeState) ||
                        safeState == null ||
                        !safeState.Valid)
                    {
                        if (LogStateChanges.Value)
                        {
                            Log?.LogInfo("No cached safe melee animator state available for " + weapon);
                        }

                        return;
                    }
                }

                animator.Play(safeState.FullPathHash, 0, 0f);
                animator.Update(0f);

                if (LogStateChanges.Value)
                {
                    Log?.LogInfo(
                        "Reset melee animator to safe draw state before sheathe: "
                        + safeState.ClipName
                        + " hash="
                        + safeState.FullPathHash
                    );
                }
            }
            catch (Exception ex)
            {
                Log?.LogError("Failed to prepare melee animator for future draw: " + ex);
            }
        }

        private static void CleanCurrentMeleeWeaponState(object currentHoldable)
        {
            if (!IsMeleeWeapon(currentHoldable))
                return;

            try
            {
                PrepareMeleeAnimatorForFutureDraw(currentHoldable);
            }
            catch (Exception ex)
            {
                Log?.LogError("Failed to clean melee weapon state: " + ex);
            }
        }

        private static string GetCurrentClipName(Animator animator)
        {
            if (animator == null)
                return string.Empty;

            try
            {
                AnimatorClipInfo[] clips = animator.GetCurrentAnimatorClipInfo(0);

                if (clips != null &&
                    clips.Length > 0 &&
                    clips[0].clip != null)
                {
                    return clips[0].clip.name;
                }
            }
            catch
            {
                return string.Empty;
            }

            return string.Empty;
        }

        private static float ClampRetryInterval()
        {
            if (ReChargeRetryInterval == null)
                return 0.08f;

            float value = ReChargeRetryInterval.Value;

            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0.08f;

            return Mathf.Clamp(value, 0.01f, 0.5f);
        }

        private static bool WasMeleePressedThisFrame(object equipmentManager)
        {
            InputAction melee = GetInputAction(fMeleeFireAction, equipmentManager);
            InputAction meleeAlternative = GetInputAction(fMeleeFireActionAlternative, equipmentManager);

            return WasPerformedThisFrame(melee) ||
                   WasPerformedThisFrame(meleeAlternative);
        }

        private static bool IsMeleeHeld(object equipmentManager)
        {
            InputAction melee = GetInputAction(fMeleeFireAction, equipmentManager);
            InputAction meleeAlternative = GetInputAction(fMeleeFireActionAlternative, equipmentManager);

            bool meleeHeld = melee != null && melee.IsPressed();
            bool meleeAlternativeHeld = meleeAlternative != null && meleeAlternative.IsPressed();

            return meleeHeld || meleeAlternativeHeld;
        }

        private static bool HandleTapHoldMeleeInput(
            object equipmentManager,
            ToggleState state,
            bool holdingMeleeAction,
            out bool prefixResult
        )
        {
            prefixResult = true;

            bool held = holdingMeleeAction || IsMeleeHeld(equipmentManager);
            bool justPressed = held && !state.WasMeleeHeldLastFrame;
            bool justReleased = !held && state.WasMeleeHeldLastFrame;

            state.WasMeleeHeldLastFrame = held;

            if (state.LongPressPassThrough)
            {
                if (justReleased || !held)
                {
                    state.LongPressPassThrough = false;
                    state.PendingTapHold = false;
                    state.MeleePressStartTime = 0f;

                    if (LogStateChanges.Value)
                    {
                        Log?.LogInfo("Long melee input released. Original melee behavior finished.");
                    }
                }

                prefixResult = true;
                return true;
            }

            if (state.PendingTapHold)
            {
                float heldTime = Time.time - state.MeleePressStartTime;

                if (justReleased || !held)
                {
                    state.PendingTapHold = false;

                    if (heldTime <= GetMeleeToggleTapThreshold())
                    {
                        ToggleOn(equipmentManager, state);

                        if (LogStateChanges.Value)
                        {
                            Log?.LogInfo("Melee key tap detected. Toggle melee ON.");
                        }

                        prefixResult = false;
                        return true;
                    }

                    prefixResult = true;
                    return true;
                }

                if (heldTime >= GetMeleeToggleTapThreshold())
                {
                    state.PendingTapHold = false;
                    state.LongPressPassThrough = true;

                    if (LogStateChanges.Value)
                    {
                        Log?.LogInfo("Melee key hold detected. Passing through to original melee behavior.");
                    }

                    prefixResult = true;
                    return true;
                }

                prefixResult = false;
                return true;
            }

            if (justPressed)
            {
                state.PendingTapHold = true;
                state.MeleePressStartTime = Time.time;

                prefixResult = false;
                return true;
            }

            return false;
        }

        // Swapped mapping: tap performs the original quick melee attack, hold toggles melee
        // stance. Unlike the default handler, the initial press is passed through to the game so
        // it starts the vanilla melee charge. A release before the tap threshold then produces a
        // normal vanilla quick-melee swing (the same passthrough path the default handler uses
        // for holds), while crossing the threshold promotes the ongoing charge into our
        // persistent stance.
        private static bool HandleTapHoldMeleeInputSwapped(
            object equipmentManager,
            ToggleState state,
            bool holdingMeleeAction,
            out bool prefixResult
        )
        {
            prefixResult = true;

            bool held = holdingMeleeAction || IsMeleeHeld(equipmentManager);
            bool justPressed = held && !state.WasMeleeHeldLastFrame;

            state.WasMeleeHeldLastFrame = held;

            if (state.PendingTapHold)
            {
                float heldTime = Time.time - state.MeleePressStartTime;

                if (!held)
                {
                    // Released before the threshold: this was a tap. The game already charged on
                    // press and performs the quick melee attack on this release frame, so let it
                    // pass through untouched.
                    state.PendingTapHold = false;

                    if (LogStateChanges.Value)
                    {
                        Log?.LogInfo("Melee key tap detected (swapped). Original quick melee attack.");
                    }

                    prefixResult = true;
                    return true;
                }

                if (heldTime >= GetMeleeToggleTapThreshold())
                {
                    // Held past the threshold: promote the ongoing charge into melee stance and
                    // take over from the game for the rest of this press.
                    state.PendingTapHold = false;
                    ToggleOn(equipmentManager, state);

                    if (LogStateChanges.Value)
                    {
                        Log?.LogInfo("Melee key hold detected (swapped). Toggle melee ON.");
                    }

                    prefixResult = false;
                    return true;
                }

                // Still under the threshold and still held: let the game keep charging so a
                // release right now still fires a quick melee.
                prefixResult = true;
                return true;
            }

            if (justPressed)
            {
                state.PendingTapHold = true;
                state.MeleePressStartTime = Time.time;

                prefixResult = true;
                return true;
            }

            return false;
        }

        private static float GetMeleeToggleTapThreshold()
        {
            if (MeleeToggleTapThreshold == null)
                return 0.13f;

            float value = MeleeToggleTapThreshold.Value;

            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0.13f;

            return Mathf.Clamp(value, 0.05f, 0.6f);
        }

        private static bool ExitToggledStateIfExternalNonMeleeHoldable(
            object equipmentManager,
            ToggleState state,
            string source
        )
        {
            if (equipmentManager == null || state == null)
                return false;

            if (!state.IsToggled)
                return false;

            if (KeepMeleeStanceAfterExternalWeaponSwitch != null &&
                KeepMeleeStanceAfterExternalWeaponSwitch.Value)
            {
                return false;
            }

            object currentHoldable = GetCurrentHoldable(equipmentManager);

            if (currentHoldable == null)
                return false;

            if (IsMeleeWeapon(currentHoldable))
                return false;

            ForceExitToggledStateAfterExternalWeaponSwitch(
                equipmentManager,
                state,
                currentHoldable,
                source
            );

            return true;
        }

        private static void ForceExitToggledStateAfterExternalWeaponSwitch(
            object equipmentManager,
            ToggleState state,
            object currentHoldable,
            string source
        )
        {
            state.IsToggled = false;
            state.AttackInProgress = false;
            state.SheatheAfterAttack = false;
            state.SuppressMeleeUntilReleased = false;
            state.NextChargeAttemptTime = 0f;

            state.PendingTapHold = false;
            state.LongPressPassThrough = false;
            state.WasMeleeHeldLastFrame = false;
            state.MeleePressStartTime = 0f;
            ClearPendingWeaponSelection(state);

            SetMeleePressed(equipmentManager, false);
            SetAlternativeMeleePressed(equipmentManager, false);

            // The stance ends on the weapon the other mod switched to.
            RememberStanceAsLastUsed(equipmentManager, state);

            if (LogStateChanges != null && LogStateChanges.Value)
            {
                Log?.LogInfo(
                    "Exited toggle melee stance because current holdable is no longer melee. " +
                    "source=" + source +
                    " holdable=" + currentHoldable
                );
            }
        }

        private static bool IsAltFirePressed(object equipmentManager)
        {
            InputAction action = GetInputAction(fAltFireAction, equipmentManager);

            return action != null && action.IsPressed();
        }

        private static bool WasPerformedThisFrame(InputAction action)
        {
            return action != null && action.WasPerformedThisFrame();
        }

        private static InputAction GetInputAction(
            FieldInfo field,
            object instance
        )
        {
            if (field == null || instance == null)
                return null;

            return field.GetValue(instance) as InputAction;
        }

        private static object GetCurrentHoldable(object equipmentManager)
        {
            if (fCurrentHoldable == null || equipmentManager == null)
                return null;

            return fCurrentHoldable.GetValue(equipmentManager);
        }

        private static object GetEquipmentManagerFromWeapon(object weapon)
        {
            if (fEquipmentManager == null || weapon == null)
                return null;

            return fEquipmentManager.GetValue(weapon);
        }

        private static bool IsMeleeWeapon(object holdable)
        {
            if (holdable == null)
                return false;

            if (!weaponType.IsInstanceOfType(holdable))
                return false;

            object value = pIsMelee.GetValue(holdable, null);

            return value is bool && (bool)value;
        }

        private static bool IsMeleeCharging(object weapon)
        {
            if (weapon == null || mIsMeleeCharging == null)
                return false;

            object value = mIsMeleeCharging.Invoke(weapon, null);

            return value is bool && (bool)value;
        }

        private static bool IsInMeleeCharge(object equipmentManager)
        {
            return GetBoolField(fIsInMeleeCharge, equipmentManager);
        }

        private static bool IsAimingInputHeld(object equipmentManager)
        {
            return GetBoolField(fAimingInputHeld, equipmentManager);
        }

        private static bool IsMeleeInputCoolingDown(object equipmentManager)
        {
            if (fMeleeInputCooldown == null || equipmentManager == null)
                return false;

            object value = fMeleeInputCooldown.GetValue(equipmentManager);

            if (!(value is float))
                return false;

            return Time.time < (float)value;
        }

        private static bool GetBoolField(FieldInfo field, object instance)
        {
            if (field == null || instance == null)
                return false;

            object value = field.GetValue(instance);

            return value is bool && (bool)value;
        }

        private static void SetMeleePressed(object equipmentManager, bool value)
        {
            SetBoolField(fMeleePressed, equipmentManager, value);
        }

        private static void SetAlternativeMeleePressed(
            object equipmentManager,
            bool value
        )
        {
            SetBoolField(fAlternativeMeleePressed, equipmentManager, value);
        }

        private static void SetBoolField(
            FieldInfo field,
            object instance,
            bool value
        )
        {
            if (field == null || instance == null)
                return;

            field.SetValue(instance, value);
        }

        private static void UpdateCurrentMeleeAlternativeAnimator(
            object equipmentManager,
            bool altHeld
        )
        {
            object currentHoldable = GetCurrentHoldable(equipmentManager);

            if (!IsMeleeWeapon(currentHoldable))
                return;

            Animator animator = GetAnimator(currentHoldable);

            if (animator == null)
                return;

            animator.SetBool("AlternativePressed", altHeld);
        }

        private static Animator GetAnimator(object holdable)
        {
            if (holdable == null || pAnimator == null)
                return null;

            return pAnimator.GetValue(holdable, null) as Animator;
        }

        private static void InvokeChargeBasicMelee(object equipmentManager)
        {
            try
            {
                mChargeBasicMelee.Invoke(equipmentManager, null);
            }
            catch (Exception ex)
            {
                Log?.LogError("Failed to invoke ChargeBasicMelee: " + ex);
            }
        }

        private static void InvokeUseBasicMelee(object equipmentManager)
        {
            try
            {
                mUseBasicMelee.Invoke(equipmentManager, null);
            }
            catch (Exception ex)
            {
                Log?.LogError("Failed to invoke UseBasicMelee: " + ex);
            }
        }

        private static void InvokeOnMeleeDone(object equipmentManager)
        {
            try
            {
                mOnMeleeDone.Invoke(equipmentManager, null);
            }
            catch (Exception ex)
            {
                Log?.LogError("Failed to invoke OnMeleeDone: " + ex);
            }
        }

        private static void InvokeSetAlternativeState(object weapon, int value)
        {
            if (weapon == null || mSetAlternativeState == null)
                return;

            try
            {
                mSetAlternativeState.Invoke(weapon, new object[] { value });
            }
            catch (Exception ex)
            {
                Log?.LogError("Failed to invoke SetAlternativeState: " + ex);
            }
        }

        private sealed class ToggleState
        {
            public bool IsToggled;
            public bool AttackInProgress;
            public bool SheatheAfterAttack;
            public bool SuppressMeleeUntilReleased;
            public float NextChargeAttemptTime;

            public bool PendingTapHold;
            public bool LongPressPassThrough;
            public bool WasMeleeHeldLastFrame;
            public float MeleePressStartTime;

            // Time.time of the last frame the toggle stance was actively maintained. Used by the
            // interaction fix to avoid overriding vanilla lockouts when equipment input is halted.
            public float LastMaintainTime;

            // True between ReportMeleeDonePrefix and ReportMeleeDonePostfix, while the game's own
            // attack completion switches the equipment back off the basic melee slot.
            public bool CompletingMeleeAttack;

            // Time.time of the last frame the game ran its equipment input update
            // (HandleMeleeInput), whether or not the stance is on.
            public float LastEquipmentInputTime;

            // A weapon selection made during a toggled melee attack. It is performed once the
            // attack has finished and the stance has sheathed (ReplayPendingWeaponSelection).
            public WeaponSelectionRequest PendingWeaponSelection;
            public object PendingWeaponSelectionSlot;

            // The MeleeInputMode the tap / hold tracking above belongs to (see HandleMeleeInputPrefix).
            public MeleeKeyMode? AppliedMeleeKeyMode;

            // Set when the stance ended, so the last used weapon key goes back into it (see
            // RememberStanceAsLastUsed). StanceReturnSlot is the slot the exit settled on, and
            // StanceExitFrame the Time.frameCount of the exit.
            public bool StanceIsLastUsed;
            public object StanceReturnSlot;
            public int StanceExitFrame;
        }

        // Values of the MeleeInputMode config entry. The names are stored in the config file, so
        // they must not be renamed.
        public enum MeleeKeyMode
        {
            TapToggleHoldQuickMelee,
            TapQuickMeleeHoldToggle,
            PressToggles,
            QuickMeleeChordToggle
        }

        private enum WeaponSelectionRequest
        {
            None,
            Slot,
            Next,
            Previous,
            LastUsed
        }

        private sealed class SafeAnimatorState
        {
            public bool Valid;
            public int FullPathHash;
            public int ShortNameHash;
            public string ClipName;
        }
    }
}