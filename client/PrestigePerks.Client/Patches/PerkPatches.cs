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

namespace PrestigePerks.Patches
{
    // 클라이언트 패치 특전 (dev-docs 2_특전.md 6절 "클라이언트 패치 특전 (3차)").
    // 공통 규칙: PerkEffects.Has → PerkTarget.IsPmc 순서로 거르고, 본문은 계수만 곱한다 (LINQ·문자열·로그·new 금지).
    // 원래 함수를 건너뛰지 않는다. 예외가 나면 그 특전만 끈다. 패치 클래스마다 Plugin.PatchOne 으로 따로 등록한다.

    /// <summary>9 짐꾼: 소지 무게 한도 배율 <c>1 + 근력 버프</c>에 +0.15. 장비 화면·체력 패널 표시도 같은 값을 읽는다.</summary>
    [HarmonyPatch]
    internal static class CarryingWeightPatch
    {
        private const int Perk = 9;

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
                    __result = PerkPatchMath.CarryingWeight(__result);
                    PerkEffects.Record(Perk, before, __result);
                }
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }
    }

    /// <summary>10 체력: 다리 스태미나 최대치에 <c>baseValue × 0.2</c>를 더한다 (지구력 버프 +0.2). baseValue 는 SVM 이 바꾼 Stamina.Capacity 다.</summary>
    [HarmonyPatch]
    internal static class StaminaCapacityPatch
    {
        private const int Perk = 10;

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
                    __result = PerkPatchMath.StaminaCapacity(__result, baseValue);
                    PerkEffects.Record(Perk, before, __result);
                }
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }
    }

    /// <summary>16 절제: 레이드 중 에너지·수분 초당 소모 ×0.75. 소유자는 Effect.HealthController(ActiveHealthController).Player.</summary>
    [HarmonyPatch]
    internal static class ExistenceDamagePatch
    {
        private const int Perk = 16;

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
                    __result = PerkPatchMath.Consumption(__result);
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
        private const int Perk = 24;

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

    /// <summary>24 숙련 (안쪽): 플래그가 켜져 있고 숙련도면 증가량 ×1.2. Mastering 은 OnTrigger 를 재정의하지 않아 여기를 지난다.</summary>
    [HarmonyPatch]
    internal static class MasteringTriggerPatch
    {
        private const int Perk = 24;

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
                    val = PerkPatchMath.Mastering(val);
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
        private const int Perk = 26;

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
                    __result = PerkPatchMath.AttentionLootSpeed(__result);
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
        private const int Perk = 26;

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
                    __result = PerkPatchMath.SearchBuffSpeed(__result);
                }
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }
    }

    /// <summary>
    /// 11 강골·12 지혈 (바깥): PMC 본인이 맞는 동안 골절·출혈 판정 설정 객체를 정적 필드에 둔다. 안쪽 Try 패치는 이 객체와 같을 때만 cap 을 ÷0.75 한다.
    /// 같은 설정 객체를 봇도 쓰므로 범위가 필요하다. 이벤트 안에서 다른 플레이어가 맞는 중첩 호출에 대비해 이전 값을 __state 로 되돌린다.
    /// </summary>
    [HarmonyPatch]
    internal static class ApplyDamageScopePatch
    {
        internal static EffectsSettings.ProbabilitySetting FractureFall, FractureBullet, HeavyBleeding, LightBleeding;

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.ApplyDamage));

        private static void Prefix(ActiveHealthController __instance, out Scope __state)
        {
            __state = new Scope(FractureFall, FractureBullet, HeavyBleeding, LightBleeding);
            FractureFall = FractureBullet = HeavyBleeding = LightBleeding = null;
            var bones = PerkEffects.Has(11);
            var bleed = PerkEffects.Has(12);
            if (!bones && !bleed)
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
                if (bones)
                {
                    FractureFall = effects.Fracture.FallingProbability;
                    FractureBullet = effects.Fracture.BulletHitProbability;
                }

                if (bleed)
                {
                    HeavyBleeding = effects.HeavyBleeding.Probability;
                    LightBleeding = effects.LightBleeding.Probability;
                }
            }
            catch (Exception e)
            {
                FractureFall = FractureBullet = HeavyBleeding = LightBleeding = null;
                PerkEffects.Disable(bones ? 11 : 12, e);
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

    /// <summary>11·12 (안쪽): 바깥에서 둔 골절·출혈 설정 객체의 판정이면 cap ÷0.75 (확률 ×0.75). 그 외 Try 는 그대로.</summary>
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

            if (ReferenceEquals(__instance, ApplyDamageScopePatch.FractureBullet) || ReferenceEquals(__instance, ApplyDamageScopePatch.FractureFall))
            {
                var before = cap;
                cap = PerkPatchMath.ChanceCap(cap);
                PerkEffects.Record(11, before, cap);
            }
            else if (ReferenceEquals(__instance, ApplyDamageScopePatch.HeavyBleeding) || ReferenceEquals(__instance, ApplyDamageScopePatch.LightBleeding))
            {
                var before = cap;
                cap = PerkPatchMath.ChanceCap(cap);
                PerkEffects.Record(12, before, cap);
            }
        }
    }

    /// <summary>14 제어: 무기 종류 스킬 반동 버프 x 에 +0.05. 무기를 들 때와 스킬 동기화 때만 불린다.</summary>
    [HarmonyPatch]
    internal static class RecoilSuppressionPatch
    {
        private const int Perk = 14;

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
                    __result.RecoilSupression.x = PerkPatchMath.RecoilSuppression(__result.RecoilSupression.x);
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
    /// 29 재장전: 재장전 애니메이션 속도 ×1.1. WeaponBuffsInfo 는 클래스라 결과 필드를 바로 고친다.
    /// 팔 부상 때 바닐라는 이 값을 쓰지 않고 1로 되돌리므로(SetAnimatorAndProceduralValues) 그 규칙은 그대로다.
    /// </summary>
    [HarmonyPatch]
    internal static class ReloadSpeedPatch
    {
        private const int Perk = 29;

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
                    __result.ReloadSpeed = PerkPatchMath.ReloadSpeed(__result.ReloadSpeed);
                    PerkEffects.Record(Perk, before, __result.ReloadSpeed);
                }
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }
    }

    /// <summary>15 정비: 사격 시 무기 내구도 손실 ×0.75. 손실은 <c>× (1 − skillWeaponTreatmentFactor)</c>라 인자를 바꾼다. 판정은 무기 소유자 ID.</summary>
    [HarmonyPatch]
    internal static class WeaponShotWearPatch
    {
        private const int Perk = 15;

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
                    skillWeaponTreatmentFactor = PerkPatchMath.WeaponTreatmentFactor(skillWeaponTreatmentFactor);
                    PerkEffects.Record(Perk, before, skillWeaponTreatmentFactor);
                }
            }
            catch (Exception e)
            {
                PerkEffects.Disable(Perk, e);
            }
        }
    }

    /// <summary>28 방탄: 방어구 내구도 감소 ×0.8. 총탄(ApplyDamage)과 폭발(ApplyExplosionDurabilityDamage) 모두 이 함수를 지난다. 판정은 방어구 소유자 ID.</summary>
    [HarmonyPatch]
    internal static class ArmorWearPatch
    {
        private const int Perk = 28;

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
                    armorDamage = PerkPatchMath.ArmorDamage(armorDamage);
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
    /// 27 군의관 (바깥): PMC 본인의 의료품 효과를 만드는 동안 체력 컨트롤러를 둔다. 효과 시간은 DoMedEffect 의 지역 변수라
    /// 그 안에서만 불리는 UseTimeFor 결과를 고친다 (2026-10-18 결정: 효과 시간만, 애니메이션은 바닐라).
    /// </summary>
    [HarmonyPatch]
    internal static class MedEffectScopePatch
    {
        private const int Perk = 27;

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
        private const int Perk = 27;

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
                    __result = PerkPatchMath.SurgeryTime(__result, health._skills.SurgerySpeed.Value);
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
    /// 25 장전: 삽탄·탄 빼기 1발 시간 ×0.85. 시간은 비동기 LoadMagazine/UnloadMagazine 의 지역 변수라 그 값을 받는 Process 생성자에서 바꾼다.
    /// ContinuousLoadAmmo 도 같은 LoadMagazine 을 불러 같이 적용된다. 판정은 인자 InventoryController.ID.
    /// </summary>
    [HarmonyPatch]
    internal static class MagazineLoadTimePatch
    {
        private const int Perk = 25;

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
                    loadOneAmmoSpeed = PerkPatchMath.LoadOneAmmoSpeed(loadOneAmmoSpeed);
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
        private const int Perk = 17;

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
                    __result = PerkPatchMath.CraftReduction(__result);
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
        private const int Perk = 17;

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
                __result = PerkPatchMath.CraftReduction(__result);
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
        private const int Perk = 17;

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
                __result = PerkPatchMath.BitcoinSpeed(__result);
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
        private const int Perk = 17;

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
