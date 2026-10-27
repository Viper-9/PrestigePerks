using System;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.Hideout;
using EFT.InventoryLogic;
using HarmonyLib;
using PrestigePerks.Game;
using PrestigePerks.Model;
using UnityEngine;

namespace PrestigePerks.Patches
{
    // 클라이언트 패치 특전 (dev-docs 2_특전.md 6절 "클라이언트 패치 특전 (3차)").
    // 공통 규칙: PerkEffects.Has → PerkTarget.IsPmc 순서로 거르고, 본문은 계수만 곱한다 (LINQ·문자열·로그·new 금지).
    // 원래 함수를 건너뛰지 않는다. 예외가 나면 그 특전만 끈다. 패치 클래스마다 Plugin.PatchOne 으로 따로 등록한다.

    /// <summary>9 짐꾼: 소지 무게 한도 배율 <c>1 + 근력 버프</c>에 +0.2. 장비 화면·체력 패널 표시도 같은 값을 읽는다.</summary>
    [HarmonyPatch]
    internal static class CarryingWeightPatch
    {
        private const int Perk = PerkPatchSlots.Porter;

        private static MethodBase TargetMethod() => AccessTools.PropertyGetter(typeof(SkillManager), nameof(SkillManager.CarryingWeightRelativeModifier));

        private static void Postfix(SkillManager __instance, ref float __result)
        {
            if (!PerkEffects.Has(Perk))
            {
                return;
            }

            try
            {
                if (PerkTarget.IsPmc(__instance))
                {
                    var before = __result;
                    __result = PerkPatchMath.CarryingWeight(__result, PerkEffects.Scale(Perk));
                    PerkEffects.Record(Perk, before, __result);
                }
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }
    }

    /// <summary>
    /// 9 짐꾼 (레이드 과적 한도): 배율 getter 는 한 줄이라 호출하는 쪽에 인라인될 수 있다. 다른 모드(SAIN)가 UpdateWeightLimits 를 패치하면
    /// 다시 컴파일된 본문이 getter 패치를 거치지 않아 짐꾼 없이 한도를 계산한다 (2026-10-21 실측: 33.8 = 26 × 1.3).
    /// 계산이 끝난 뒤 짐꾼 없는 값이면 짐꾼 배율로 다시 계산한다. 근력 버프는 getter 를 거치지 않고 직접 읽는다.
    /// </summary>
    [HarmonyPatch]
    internal static class OverweightLimitsPatch
    {
        private const int Perk = PerkPatchSlots.Porter;

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(PhysicalBase), nameof(PhysicalBase.UpdateWeightLimits));

        private static void Postfix(PhysicalBase __instance)
        {
            if (!PerkEffects.Has(Perk))
            {
                return;
            }

            try
            {
                if (!(__instance._playerBridge?.iPlayer is Player player) || !PerkTarget.IsPmc(player))
                {
                    return;
                }

                var health = player.HealthController;
                var vanilla = (1f + (float)player.Skills.StrengthBuffLiftWeightInc) * health.CarryingWeightRelativeModifier;
                var perk = PerkPatchMath.CarryingWeight(1f + (float)player.Skills.StrengthBuffLiftWeightInc, PerkEffects.Scale(Perk)) * health.CarryingWeightRelativeModifier;
                var absolute = health.CarryingWeightAbsoluteModifier;
                var stamina = Singleton<GlobalConfiguration>.Instance.Stamina;
                var before = __instance.BaseOverweightLimits.x;
                __instance.BaseOverweightLimits = Fix(__instance.BaseOverweightLimits, stamina.BaseOverweightLimits, vanilla, perk, absolute);
                __instance.WalkOverweightLimits = Fix(__instance.WalkOverweightLimits, stamina.WalkOverweightLimits, vanilla, perk, absolute);
                __instance.SprintOverweightLimits = Fix(__instance.SprintOverweightLimits, stamina.SprintOverweightLimits, vanilla, perk, absolute);
                __instance.WalkSpeedOverweightLimits = Fix(__instance.WalkSpeedOverweightLimits, stamina.WalkSpeedOverweightLimits, vanilla, perk, absolute);
                PerkEffects.Record(Perk, before, __instance.BaseOverweightLimits.x);
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }

        private static Vector2 Fix(Vector2 current, Vector2 config, float vanilla, float perk, float absolute) =>
            new Vector2(
                PerkPatchMath.OverweightLimit(current.x, config.x, vanilla, perk, absolute),
                PerkPatchMath.OverweightLimit(current.y, config.y, vanilla, perk, absolute));
    }

    /// <summary>10 체력: 다리 스태미나 최대치에 <c>baseValue × 0.15</c>를 더한다 (지구력 버프 +0.15). baseValue 는 SVM 이 바꾼 Stamina.Capacity 다.</summary>
    [HarmonyPatch]
    internal static class StaminaCapacityPatch
    {
        private const int Perk = PerkPatchSlots.Stamina;

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Physical), nameof(Physical.BaseStaminaFunction));

        private static void Postfix(Physical __instance, float baseValue, ref float __result)
        {
            if (!PerkEffects.Has(Perk))
            {
                return;
            }

            try
            {
                if (PerkTarget.IsPmc(__instance._player))
                {
                    var before = __result;
                    __result = PerkPatchMath.StaminaCapacity(__result, baseValue, PerkEffects.Scale(Perk));
                    PerkEffects.Record(Perk, before, __result);
                }
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }
    }

    /// <summary>16 절제: 레이드 중 에너지·수분 초당 소모 ×0.85. 소유자는 Effect.HealthController(ActiveHealthController).Player.</summary>
    [HarmonyPatch]
    internal static class ExistenceDamagePatch
    {
        private const int Perk = PerkPatchSlots.Temperance;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ActiveHealthController.Existence), nameof(ActiveHealthController.Existence.GetEnergyDamage));
            yield return AccessTools.Method(typeof(ActiveHealthController.Existence), nameof(ActiveHealthController.Existence.GetHydrationDamage));
        }

        private static void Postfix(ActiveHealthController.Existence __instance, ref float __result)
        {
            if (!PerkEffects.Has(Perk))
            {
                return;
            }

            try
            {
                if (PerkTarget.IsPmc(__instance.HealthController?.Player))
                {
                    var before = __result;
                    __result = PerkPatchMath.Consumption(__result, PerkEffects.Scale(Perk));
                    PerkEffects.Record(Perk, before, __result);
                }
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }
    }

    /// <summary>
    /// 24 숙련 (바깥): PMC 본인이 쏠 때만 플래그를 켠다. 경로 ExecuteShotSkill → WeaponShotAction.Complete → (이벤트, 동기) → Mastering.OnTrigger.
    /// 스캐브 레이드 플레이어도 이 함수를 지나므로 여기서 판정한다. finalizer 는 예외가 나도 플래그를 끈다.
    /// </summary>
    [HarmonyPatch]
    internal static class ShotSkillScopePatch
    {
        private const int Perk = PerkPatchSlots.Mastery;

        /// <summary>PMC 본인의 사격 숙련도 처리 중.</summary>
        internal static bool PmcShot;

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Player), nameof(Player.ExecuteShotSkill));

        private static void Prefix(Player __instance)
        {
            if (!PerkEffects.Has(Perk))
            {
                return;
            }

            try
            {
                PmcShot = PerkTarget.IsPmc(__instance);
            }
            catch (Exception e)
            {
                PmcShot = false;
                PerkEffects.Disable(Perk, e);
            }
        }

        private static void Finalizer() => PmcShot = false;
    }

    /// <summary>24 숙련 (안쪽): 플래그가 켜져 있고 숙련도면 증가량 ×1.3. Mastering 은 OnTrigger 를 재정의하지 않아 여기를 지난다.</summary>
    [HarmonyPatch]
    internal static class MasteringTriggerPatch
    {
        private const int Perk = PerkPatchSlots.Mastery;

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(BaseSkill), nameof(BaseSkill.OnTrigger));

        private static void Prefix(BaseSkill __instance, ref float val)
        {
            if (!ShotSkillScopePatch.PmcShot)
            {
                return;
            }

            try
            {
                if (__instance is Mastering)
                {
                    var before = val;
                    val = PerkPatchMath.Mastering(val, PerkEffects.Scale(Perk));
                    PerkEffects.Record(Perk, before, val);
                }
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }
    }

    /// <summary>
    /// 26 수색: 속도 ×1.3. 속도는 비동기 SearchContent 안에서 <c>1 + 주의력 (+ 수색)</c>으로 계산되어 인자로 바꿀 수 없다 (transpiler 금지).
    /// 두 값은 SearchContent 만 읽으므로(IInventoryProfileSkillInfo) getter 를 고쳐 두 식 모두 ×1.3이 되게 한다. 판정은 SkillManager == PMC.Skills (= _profile).
    /// </summary>
    [HarmonyPatch]
    internal static class AttentionLootSpeedPatch
    {
        private const int Perk = PerkPatchSlots.Search;

        private static MethodBase TargetMethod() => AccessTools.PropertyGetter(typeof(SkillManager), nameof(SkillManager.AttentionLootSpeedValue));

        private static void Postfix(SkillManager __instance, ref float __result)
        {
            if (!PerkEffects.Has(Perk))
            {
                return;
            }

            try
            {
                if (PerkTarget.IsPmc(__instance))
                {
                    var before = __result;
                    __result = PerkPatchMath.AttentionLootSpeed(__result, PerkEffects.Scale(Perk));
                    PerkEffects.Record(Perk, before, __result);
                }
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }
    }

    /// <summary>26 수색: <see cref="AttentionLootSpeedPatch"/> 참고.</summary>
    [HarmonyPatch]
    internal static class SearchBuffSpeedPatch
    {
        private const int Perk = PerkPatchSlots.Search;

        private static MethodBase TargetMethod() => AccessTools.PropertyGetter(typeof(SkillManager), nameof(SkillManager.SearchBuffSpeedValue));

        private static void Postfix(SkillManager __instance, ref float __result)
        {
            if (!PerkEffects.Has(Perk))
            {
                return;
            }

            try
            {
                if (PerkTarget.IsPmc(__instance))
                {
                    __result = PerkPatchMath.SearchBuffSpeed(__result, PerkEffects.Scale(Perk));
                }
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }
    }

    /// <summary>
    /// 11 강인 (바깥): PMC 본인이 맞는 동안 골절·출혈 판정 설정 객체를 정적 필드에 둔다. 안쪽 Try 패치는 이 객체와 같을 때만 cap 을 ÷0.8 한다.
    /// 같은 설정 객체를 봇도 쓰므로 범위가 필요하다. 이벤트 안에서 다른 플레이어가 맞는 중첩 호출에 대비해 이전 값을 __state 로 되돌린다.
    /// </summary>
    [HarmonyPatch]
    internal static class ApplyDamageScopePatch
    {
        private const int Perk = PerkPatchSlots.Tough;

        internal static EffectsSettings.ProbabilitySetting FractureFall, FractureBullet, HeavyBleeding, LightBleeding;

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.ApplyDamage));

        private static void Prefix(ActiveHealthController __instance, out Scope __state)
        {
            __state = new Scope(FractureFall, FractureBullet, HeavyBleeding, LightBleeding);
            FractureFall = FractureBullet = HeavyBleeding = LightBleeding = null;
            if (!PerkEffects.Has(Perk))
            {
                return;
            }

            try
            {
                if (!PerkTarget.IsPmc(__instance.Player))
                {
                    return;
                }

                var effects = Singleton<GlobalConfiguration>.Instance.Health.Effects;
                FractureFall = effects.Fracture.FallingProbability;
                FractureBullet = effects.Fracture.BulletHitProbability;
                HeavyBleeding = effects.HeavyBleeding.Probability;
                LightBleeding = effects.LightBleeding.Probability;
            }
            catch (Exception e)
            {
                FractureFall = FractureBullet = HeavyBleeding = LightBleeding = null;
                PerkEffects.Disable(Perk, e);
            }
        }

        private static void Finalizer(Scope __state)
        {
            FractureFall = __state.FractureFall;
            FractureBullet = __state.FractureBullet;
            HeavyBleeding = __state.HeavyBleeding;
            LightBleeding = __state.LightBleeding;
        }

        /// <summary>중첩 호출 때 되돌릴 이전 값. 구조체라 할당이 없다.</summary>
        internal readonly struct Scope
        {
            public readonly EffectsSettings.ProbabilitySetting FractureFall, FractureBullet, HeavyBleeding, LightBleeding;

            public Scope(EffectsSettings.ProbabilitySetting fall, EffectsSettings.ProbabilitySetting bullet, EffectsSettings.ProbabilitySetting heavy, EffectsSettings.ProbabilitySetting light)
            {
                FractureFall = fall;
                FractureBullet = bullet;
                HeavyBleeding = heavy;
                LightBleeding = light;
            }
        }
    }

    /// <summary>11 (안쪽): 바깥에서 둔 골절·출혈 설정 객체의 판정이면 cap ÷0.8 (확률 ×0.8). 그 외 Try 는 그대로. 둘 다 11번으로 센다.</summary>
    [HarmonyPatch]
    internal static class ProbabilityTryPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(EffectsSettings.ProbabilitySetting), nameof(EffectsSettings.ProbabilitySetting.Try));

        private static void Prefix(EffectsSettings.ProbabilitySetting __instance, ref float cap)
        {
            if (__instance == null)
            {
                return;
            }

            if (ReferenceEquals(__instance, ApplyDamageScopePatch.FractureBullet) || ReferenceEquals(__instance, ApplyDamageScopePatch.FractureFall)
                || ReferenceEquals(__instance, ApplyDamageScopePatch.HeavyBleeding) || ReferenceEquals(__instance, ApplyDamageScopePatch.LightBleeding))
            {
                var before = cap;
                cap = PerkPatchMath.ChanceCap(cap, PerkEffects.Scale(PerkPatchSlots.Tough));
                PerkEffects.Record(PerkPatchSlots.Tough, before, cap);
            }
        }
    }

    /// <summary>14 제어: 무기 종류 스킬 반동 버프 x 에 +0.1. 무기를 들 때와 스킬 동기화 때만 불린다.</summary>
    [HarmonyPatch]
    internal static class RecoilSuppressionPatch
    {
        private const int Perk = PerkPatchSlots.Control;

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(SkillManager), nameof(SkillManager.GetWeaponInfo));

        private static void Postfix(SkillManager __instance, SkillManager.WeaponBuffsInfo __result)
        {
            if (!PerkEffects.Has(Perk) || __result == null)
            {
                return;
            }

            try
            {
                if (PerkTarget.IsPmc(__instance))
                {
                    var before = __result.RecoilSupression.x;
                    __result.RecoilSupression.x = PerkPatchMath.RecoilSuppression(__result.RecoilSupression.x, PerkEffects.Scale(Perk));
                    PerkEffects.Record(Perk, before, __result.RecoilSupression.x);
                }
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }
    }

    /// <summary>
    /// 29 재장전: 재장전 애니메이션 속도 ×1.15. WeaponBuffsInfo 는 클래스라 결과 필드를 바로 고친다.
    /// 팔 부상 때 바닐라는 이 값을 쓰지 않고 1로 되돌리므로(SetAnimatorAndProceduralValues) 그 규칙은 그대로다.
    /// </summary>
    [HarmonyPatch]
    internal static class ReloadSpeedPatch
    {
        private const int Perk = PerkPatchSlots.Reload;

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(SkillManager), nameof(SkillManager.GetWeaponInfo));

        private static void Postfix(SkillManager __instance, SkillManager.WeaponBuffsInfo __result)
        {
            if (!PerkEffects.Has(Perk) || __result == null)
            {
                return;
            }

            try
            {
                if (PerkTarget.IsPmc(__instance))
                {
                    var before = __result.ReloadSpeed;
                    __result.ReloadSpeed = PerkPatchMath.ReloadSpeed(__result.ReloadSpeed, PerkEffects.Scale(Perk));
                    PerkEffects.Record(Perk, before, __result.ReloadSpeed);
                }
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }
    }

    /// <summary>
    /// 30 조작: 레이드 인체공학에 고정 +10. 조준 속도·흔들림·팔 스태미나에만 쓰이고 무기 정보 화면 숫자는 바닐라처럼 그대로다.
    /// 결과는 _deferredErgonomic 캐시 값이라 무기·장비·스킬이 바뀔 때만 다시 계산된다. 팔 부상이면 바닐라가 0을 돌려주므로 그대로 둔다.
    /// </summary>
    [HarmonyPatch]
    internal static class ErgonomicsPatch
    {
        private const int Perk = PerkPatchSlots.Handling;

        private const EPhysicalCondition ArmDamaged = EPhysicalCondition.LeftArmDamaged | EPhysicalCondition.RightArmDamaged;

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Player.FirearmController), nameof(Player.FirearmController.TotalErgonomicsFunc));

        private static void Postfix(Player.FirearmController __instance, ref float __result)
        {
            if (!PerkEffects.Has(Perk))
            {
                return;
            }

            try
            {
                var player = __instance._player;
                if (PerkTarget.IsPmc(player) && !player.MovementContext.PhysicalConditionContainsAny(ArmDamaged))
                {
                    var before = __result;
                    __result = PerkPatchMath.Ergonomics(__result, PerkEffects.Scale(Perk));
                    PerkEffects.Record(Perk, before, __result);
                }
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }
    }

    /// <summary>
    /// 31 신뢰성 (고장): 사격 고장 확률 ×0.8. 합계와 원인별 확률(out)을 같이 줄여 GetMalfunctionState 의 원인·종류 분포는 바닐라와 같다.
    /// 바닐라는 내구도 93~100% 에서 내구도·탄약·탄창 확률을 0 으로 만들므로 그때는 과열 고장만 남는다.
    /// </summary>
    [HarmonyPatch]
    internal static class MalfunctionChancePatch
    {
        private const int Perk = PerkPatchSlots.Reliability;

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Player.FirearmController), nameof(Player.FirearmController.GetTotalMalfunctionChance));

        private static void Postfix(Player.FirearmController __instance, ref double durabilityMalfChance, ref float magMalfChance, ref float ammoMalfChance, ref float overheatMalfChance, ref float __result)
        {
            if (!PerkEffects.Has(Perk) || !(__result > 0f))
            {
                return;
            }

            try
            {
                if (PerkTarget.IsPmc(__instance._player))
                {
                    var before = __result;
                    durabilityMalfChance = PerkPatchMath.Malfunction(durabilityMalfChance, PerkEffects.Scale(Perk));
                    magMalfChance = PerkPatchMath.Malfunction(magMalfChance, PerkEffects.Scale(Perk));
                    ammoMalfChance = PerkPatchMath.Malfunction(ammoMalfChance, PerkEffects.Scale(Perk));
                    overheatMalfChance = PerkPatchMath.Malfunction(overheatMalfChance, PerkEffects.Scale(Perk));
                    __result = PerkPatchMath.Malfunction(__result, PerkEffects.Scale(Perk));
                    PerkEffects.Record(Perk, before, __result);
                }
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }
    }

    /// <summary>31 신뢰성 (과열): 발당 과열 ×0.8. Weapon.OnShot 만 부른다 (그 밖은 디버그 창). 판정은 무기 소유자 ID.</summary>
    [HarmonyPatch]
    internal static class ShotOverheatPatch
    {
        private const int Perk = PerkPatchSlots.Reliability;

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Weapon), nameof(Weapon.GetShotOverheat));

        private static void Postfix(Weapon __instance, ref float __result)
        {
            if (!PerkEffects.Has(Perk))
            {
                return;
            }

            try
            {
                if (PerkTarget.IsPmcOwnerId(__instance.Owner?.ID))
                {
                    var before = __result;
                    __result = PerkPatchMath.ShotOverheat(__result, PerkEffects.Scale(Perk));
                    PerkEffects.Record(Perk, before, __result);
                }
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }
    }

    /// <summary>15 정비 (무기): 사격 시 무기 내구도 손실 ×0.8. 손실은 <c>× (1 − skillWeaponTreatmentFactor)</c>라 인자를 바꾼다. 판정은 무기 소유자 ID.</summary>
    [HarmonyPatch]
    internal static class WeaponShotWearPatch
    {
        private const int Perk = PerkPatchSlots.Maintenance;

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Weapon), nameof(Weapon.OnShot));

        private static void Prefix(Weapon __instance, ref float skillWeaponTreatmentFactor)
        {
            if (!PerkEffects.Has(Perk))
            {
                return;
            }

            try
            {
                if (PerkTarget.IsPmcOwnerId(__instance.Owner?.ID))
                {
                    var before = skillWeaponTreatmentFactor;
                    skillWeaponTreatmentFactor = PerkPatchMath.WeaponTreatmentFactor(skillWeaponTreatmentFactor, PerkEffects.Scale(Perk));
                    PerkEffects.Record(Perk, before, skillWeaponTreatmentFactor);
                }
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }
    }

    /// <summary>
    /// 15 정비 (방어구): 방어구 내구도 감소 ×0.8. 총탄(ApplyDamage)과 폭발(ApplyExplosionDurabilityDamage) 모두 이 함수를 지난다. 판정은 방어구 소유자 ID.
    /// 예전 28번(방탄)을 합쳤다 (2026-10-19).
    /// </summary>
    [HarmonyPatch]
    internal static class ArmorWearPatch
    {
        private const int Perk = PerkPatchSlots.Maintenance;

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(ArmorComponent), nameof(ArmorComponent.ApplyDurabilityDamage));

        private static void Prefix(ArmorComponent __instance, ref float armorDamage)
        {
            if (!PerkEffects.Has(Perk))
            {
                return;
            }

            try
            {
                if (PerkTarget.IsPmcOwnerId(__instance.Item?.Owner?.ID))
                {
                    var before = armorDamage;
                    armorDamage = PerkPatchMath.ArmorDamage(armorDamage, PerkEffects.Scale(Perk));
                    PerkEffects.Record(Perk, before, armorDamage);
                }
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }
    }

    /// <summary>
    /// 32 기동: 서서 걷기(Run)·전력질주(Sprint) 중 이번 프레임 수평 이동량 ×1.05. 바닐라와 OldTarkovMovement 상태 모두 이 함수로 이동한다
    /// (ClientPlayerMovementContext 는 base 를 부른다). 점프·넘기·엎드리기·낙하는 상태가 달라 빠진다. 수직(중력)은 그대로.
    /// 근력처럼 애니메이터 Speed·SprintSpeed 를 올리는 방식은 값만 오르고 실제 속도는 1% 안팎이라 쓰지 않는다 (2026-10-19 은신처 측정).
    /// 애니메이션은 그대로라 발이 5% 미끄러지지만 눈에 띄지 않는다. 게임 속도 상한(CharacterController.SpeedLimit)은 그대로 적용된다.
    /// </summary>
    [HarmonyPatch]
    internal static class MoveSpeedPatch
    {
        private const int Perk = PerkPatchSlots.Agility;

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(MovementContext), nameof(MovementContext.ApplyMotion), new[] { typeof(Vector3), typeof(float) });

        private static void Prefix(MovementContext __instance, ref Vector3 motion)
        {
            if (!PerkEffects.Has(Perk))
            {
                return;
            }

            try
            {
                var state = __instance.CurrentState?.Name ?? EPlayerState.None;
                if ((state == EPlayerState.Run || state == EPlayerState.Sprint) && PerkTarget.IsPmc(__instance.SkillManager))
                {
                    var before = motion.x;
                    motion.x = PerkPatchMath.MoveDistance(motion.x, PerkEffects.Scale(Perk));
                    motion.z = PerkPatchMath.MoveDistance(motion.z, PerkEffects.Scale(Perk));
                    PerkEffects.Record(Perk, before, motion.x);
                }
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }
    }

    /// <summary>
    /// 27 군의관 (바깥): PMC 본인의 의료품 효과를 만드는 동안 체력 컨트롤러를 둔다. 효과 시간은 DoMedEffect 의 지역 변수라
    /// 그 안에서만 불리는 UseTimeFor 결과를 고친다 (2026-10-18 결정: 효과 시간만, 애니메이션은 바닐라).
    /// </summary>
    [HarmonyPatch]
    internal static class MedEffectScopePatch
    {
        private const int Perk = PerkPatchSlots.Surgeon;

        internal static ActiveHealthController PmcHealth;

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.DoMedEffect));

        private static void Prefix(ActiveHealthController __instance, out ActiveHealthController __state)
        {
            __state = PmcHealth;
            PmcHealth = null;
            if (!PerkEffects.Has(Perk))
            {
                return;
            }

            try
            {
                if (PerkTarget.IsPmc(__instance.Player))
                {
                    PmcHealth = __instance;
                }
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }

        private static void Finalizer(ActiveHealthController __state) => PmcHealth = __state;
    }

    /// <summary>
    /// 27 군의관 (안쪽): 파괴된 부위에 수술 키트를 쓸 때(바닐라 조건과 같음) 효과 시간에 <c>(1 + s) ÷ (1 + s + 0.2)</c>.
    /// bodyPart 인자는 DoMedEffect 가 고른 실제 부위다.
    /// </summary>
    [HarmonyPatch]
    internal static class SurgeryTimePatch
    {
        private const int Perk = PerkPatchSlots.Surgeon;

        // params 배열을 매번 만들지 않게
        private static readonly EDamageEffectType[] DestroyedPart = { EDamageEffectType.DestroyedPart };

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(HealthEffectsComponent), nameof(HealthEffectsComponent.UseTimeFor));

        private static void Postfix(HealthEffectsComponent __instance, EBodyPart bodyPart, ref float __result)
        {
            var health = MedEffectScopePatch.PmcHealth;
            if (health == null)
            {
                return;
            }

            try
            {
                if (health.IsBodyPartDestroyed(bodyPart) && __instance.AffectsAny(DestroyedPart))
                {
                    var before = __result;
                    __result = PerkPatchMath.SurgeryTime(__result, health._skills.SurgerySpeed.Value, PerkEffects.Scale(Perk));
                    PerkEffects.Record(Perk, before, __result);
                }
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }
    }

    /// <summary>
    /// 25 장전: 삽탄·탄 빼기 1발 시간 ×0.8. 시간은 비동기 LoadMagazine/UnloadMagazine 의 지역 변수라 그 값을 받는 Process 생성자에서 바꾼다.
    /// ContinuousLoadAmmo 도 같은 LoadMagazine 을 불러 같이 적용된다. 판정은 인자 InventoryController.ID.
    /// </summary>
    [HarmonyPatch]
    internal static class MagazineLoadTimePatch
    {
        private const int Perk = PerkPatchSlots.Loading;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Constructor(typeof(Player.PlayerInventoryController.LoadMagazineProcess),
                new[] { typeof(InventoryController), typeof(Magazine), typeof(Ammo), typeof(int), typeof(bool), typeof(float) });
            yield return AccessTools.Constructor(typeof(Player.PlayerInventoryController.UnloadMagazineProcess),
                new[] { typeof(InventoryController), typeof(Magazine), typeof(float), typeof(bool) });
        }

        private static void Prefix(InventoryController inventoryController, ref float loadOneAmmoSpeed)
        {
            if (!PerkEffects.Has(Perk))
            {
                return;
            }

            try
            {
                if (PerkTarget.IsPmcOwnerId(inventoryController?.ID))
                {
                    var before = loadOneAmmoSpeed;
                    loadOneAmmoSpeed = PerkPatchMath.LoadOneAmmoSpeed(loadOneAmmoSpeed, PerkEffects.Scale(Perk));
                    PerkEffects.Record(Perk, before, loadOneAmmoSpeed);
                }
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }
    }

    // 17 장인 (클라이언트). 서버가 생산 시간을 정하고, 새로 시작한 생산의 화면 타이머만 클라이언트가 계산한다.
    // 재접속 때 진행 중인 생산은 서버 ProductionTime 을 그대로 읽는다 (ProducingItemController.UpdateFromProductionData).
    // 은신처는 메인 세션 PMC 것뿐이라 소유자 판정이 필요 없다.

    /// <summary>
    /// 17 장인: 일반 제작(ItemsProducer)·연속 생산(PermanentProducer, 비트코인 제외)의 시간 배율 ×0.8.
    /// 기반 getter 를 패치하지만 인스턴스 형식으로 걸러 스캐브 케이스(override 가 base 를 부름)·제단·비트코인에는 걸지 않는다.
    /// UpdateProductionTimeReduction 대신 getter 를 고쳐 특전 상태를 받기 전 초기화된 생산기에도 맞게 적용된다.
    /// </summary>
    [HarmonyPatch]
    internal static class ProducerReductionPatch
    {
        private const int Perk = PerkPatchSlots.Artisan;

        private static MethodBase TargetMethod() => AccessTools.PropertyGetter(typeof(ItemsProducerBase), nameof(ItemsProducerBase.ReductionCoefficient));

        private static void Postfix(ItemsProducerBase __instance, ref float __result)
        {
            if (!PerkEffects.Has(Perk))
            {
                return;
            }

            try
            {
                if (__instance is ItemsProducer || (__instance is PermanentProducer && !(__instance is BitcoinProducer)))
                {
                    var before = __result;
                    __result = PerkPatchMath.CraftReduction(__result, PerkEffects.Scale(Perk));
                    PerkEffects.Record(Perk, before, __result);
                }
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }
    }

    /// <summary>17 장인: 스캐브 케이스 시간 배율(Fence 배율 포함) ×0.8.</summary>
    [HarmonyPatch]
    internal static class ScavCaseReductionPatch
    {
        private const int Perk = PerkPatchSlots.Artisan;

        private static MethodBase TargetMethod() => AccessTools.PropertyGetter(typeof(ScavCaseProducer), nameof(ScavCaseProducer.ReductionCoefficient));

        private static void Postfix(ref float __result)
        {
            if (!PerkEffects.Has(Perk))
            {
                return;
            }

            try
            {
                var before = __result;
                __result = PerkPatchMath.CraftReduction(__result, PerkEffects.Scale(Perk));
                PerkEffects.Record(Perk, before, __result);
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }
    }

    /// <summary>17 장인: 비트코인 속도 계수(그래픽카드 수) ÷0.8. 서버 UpdateBitcoinFarm 과 같은 비율이다.</summary>
    [HarmonyPatch]
    internal static class BitcoinSpeedPatch
    {
        private const int Perk = PerkPatchSlots.Artisan;

        private static MethodBase TargetMethod() => AccessTools.PropertyGetter(typeof(PermanentProducer), nameof(PermanentProducer.ResourceRelatedSpeedCoefficient));

        private static void Postfix(PermanentProducer __instance, ref float __result)
        {
            if (!PerkEffects.Has(Perk) || !(__instance is BitcoinProducer))
            {
                return;
            }

            try
            {
                var before = __result;
                __result = PerkPatchMath.BitcoinSpeed(__result, PerkEffects.Scale(Perk));
                PerkEffects.Record(Perk, before, __result);
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }
    }

    /// <summary>17 장인: 서버처럼 생산 시간 최소 5초. 새 생산 타이머를 만드는 곳에서 배율을 고친다.</summary>
    [HarmonyPatch]
    internal static class CraftTimeFloorPatch
    {
        private const int Perk = PerkPatchSlots.Artisan;

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(BaseHideoutScheme), nameof(BaseHideoutScheme.GetProducingItem));

        private static void Prefix(BaseHideoutScheme __instance, ref float reductionCoefficient)
        {
            if (!PerkEffects.Has(Perk))
            {
                return;
            }

            try
            {
                reductionCoefficient = PerkPatchMath.CraftReductionFloor(__instance.productionTime, reductionCoefficient);
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }
    }
}
