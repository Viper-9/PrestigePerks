using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using HarmonyLib;

namespace PrestigePerks.Active
{
    /// <summary>투혼 패치가 보는 값. 몸통 피해는 종류별 배열에, 머리 피해는 따로 더한다 (할당 없음).</summary>
    internal static class GritState
    {
        public const int Enemy = 0, Bleeding = 1, Fall = 2;

        public static bool Active, Broken;
        public static ActiveHealthController Health;
        public static float Factor = 1f, HeadFactor = 1f;
        public static readonly int[] Count = new int[3];
        public static readonly float[] Before = new float[3], After = new float[3];
        public static int HeadCount;
        public static float HeadBefore, HeadAfter;

        // 개발자 모드 grit.health: 실제로 줄어든 체력. Pending = 지금 ApplyDamage 의 예상 피해 (재지 않으면 -1)
        public static int RealHits, NotMeasured;
        public static float RealLost, RealWant, HealthBefore, Pending = -1;

        public static void Record(int kind, bool head, float before, float after)
        {
            if (head)
            {
                HeadCount++;
                HeadBefore += before;
                HeadAfter += after;
                return;
            }

            Count[kind]++;
            Before[kind] += before;
            After[kind] += after;
        }

        public static void Reset()
        {
            Array.Clear(Count, 0, Count.Length);
            Array.Clear(Before, 0, Before.Length);
            Array.Clear(After, 0, After.Length);
            HeadCount = 0;
            HeadBefore = HeadAfter = 0;
            RealHits = NotMeasured = 0;
            RealLost = RealWant = 0;
            Pending = -1;
        }

        /// <summary>
        /// 줄인 피해가 체력에 그대로 들어가는지 재기 전 (ApplyDamage 안의 배율을 곱한 예상 피해). 부위 체력이 모자라면 남는 피해가 다른 부위로 번져 비교할 수 없으므로 재지 않는다.
        /// </summary>
        public static void BeforeHit(ActiveHealthController health, EBodyPart part, float damage, EDamageType type)
        {
            Pending = -1;
            if (damage <= 0f)
            {
                return;
            }

            if (health.DamageMultiplier > 1f || part != EBodyPart.Head || !type.IsEnemyDamage())
            {
                damage *= health.DamageMultiplier;
            }

            if (type.IsEnvironmental())
            {
                damage *= Singleton<GlobalConfiguration>.Instance.Health.ProfileHealthSettings.BodyPartsSettings[part].EnvironmentDamageMultiplier;
            }

            if (!health.IsAlive || health.DamageCoeff <= 0f || health.GetBodyPartHealth(part).Current <= damage)
            {
                NotMeasured++;
                return;
            }

            HealthBefore = health.GetBodyPartHealth(EBodyPart.Common).Current;
            Pending = damage;
        }

        public static void AfterHit(ActiveHealthController health)
        {
            RealLost += HealthBefore - health.GetBodyPartHealth(EBodyPart.Common).Current;
            RealWant += Pending;
            RealHits++;
            Pending = -1;
        }
    }

    /// <summary>
    /// 투혼 (실현성조사 3.8 최종 결정): 지속 동안 적 공격(저격 제외)·출혈·낙하 피해를 몸통(머리 밖 모든 부위) −reduction%, 머리 −head% (2026-11-08: 헬멧 없이 한 방을 버티는 구간을 좁히려고 머리는 절반). 강인(11) ApplyDamageScopePatch 와 같은 메서드지만
    /// 서로 값을 건드리지 않아 순서는 상관없다. grit.reduced 는 패치가 계산한 비율, grit.health 는 실제로 줄어든 체력을 본다.
    /// </summary>
    internal sealed class Grit : ActivePerkEffect
    {
        // 체력은 float 합이라 한 대마다 오차가 조금 생긴다
        private const float HealthTolerance = 0.005f;

        public override void RaidStarted(ActivePerkUse use) => GritState.Broken = false;

        public override void Start(ActivePerkUse use)
        {
            GritState.Reset();
            GritState.Health = use.Health;
            GritState.Factor = 1f - use.Value("reduction") / 100f;
            GritState.HeadFactor = 1f - use.Value("head") / 100f;
            GritState.Active = true;
        }

        public override void Stop(ActivePerkUse use)
        {
            GritState.Active = false;
            GritState.Health = null;
            var total = GritState.Count[GritState.Enemy] + GritState.Count[GritState.Bleeding] + GritState.Count[GritState.Fall];
            var before = GritState.Before[GritState.Enemy] + GritState.Before[GritState.Bleeding] + GritState.Before[GritState.Fall];
            var after = GritState.After[GritState.Enemy] + GritState.After[GritState.Bleeding] + GritState.After[GritState.Fall];
            var ratio = before > 0 ? after / before : 0f;
            if (total == 0 && GritState.HeadCount == 0)
            {
                ActivePerkLog.Skip("grit.reduced", "no enemy, bleeding or fall damage while running");
                return;
            }

            if (total == 0)
            {
                ActivePerkLog.Skip("grit.reduced", "no damage below the head while running");
            }
            else
            {
                ActivePerkLog.Check("grit.reduced", Math.Abs(ratio - GritState.Factor) < 0.001f,
                    $"hits enemy {GritState.Count[GritState.Enemy]}, bleeding {GritState.Count[GritState.Bleeding]}, fall {GritState.Count[GritState.Fall]}; "
                    + $"damage {before:F1} -> {after:F1} (x{ratio:F3}, want x{GritState.Factor:F3})");
            }

            if (GritState.HeadCount == 0)
            {
                ActivePerkLog.Skip("grit.head", "no head damage while running");
            }
            else
            {
                var headRatio = GritState.HeadAfter / GritState.HeadBefore;
                ActivePerkLog.Check("grit.head", Math.Abs(headRatio - GritState.HeadFactor) < 0.001f,
                    $"head hits {GritState.HeadCount}; damage {GritState.HeadBefore:F1} -> {GritState.HeadAfter:F1} (x{headRatio:F3}, want x{GritState.HeadFactor:F3})");
            }
            if (GritState.RealHits == 0)
            {
                ActivePerkLog.Skip("grit.health", $"all {GritState.NotMeasured} hits took more than the body part had left");
                return;
            }

            ActivePerkLog.Check("grit.health", Math.Abs(GritState.RealLost - GritState.RealWant) <= HealthTolerance * GritState.RealHits,
                $"health really lost {GritState.RealLost:F2}, want {GritState.RealWant:F2} in {GritState.RealHits} hits, not measured {GritState.NotMeasured}");
        }

        public override string Describe(ActivePerkUse use) =>
            $"hits {GritState.Count[GritState.Enemy]} / {GritState.Count[GritState.Bleeding]} / {GritState.Count[GritState.Fall]}, damage {GritState.Before[0] + GritState.Before[1] + GritState.Before[2]:F1} -> {GritState.After[0] + GritState.After[1] + GritState.After[2]:F1}, "
            + $"head {GritState.HeadCount} hits {GritState.HeadBefore:F1} -> {GritState.HeadAfter:F1}";
    }

    [HarmonyPatch]
    internal static class GritDamagePatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.ApplyDamage));

        // 인자 이름은 원본과 같아야 한다 (ApplyDamage(EBodyPart bodyPart, float damage, DamageInfo damageInfo))
        private static void Prefix(ActiveHealthController __instance, EBodyPart bodyPart, ref float damage, DamageInfo damageInfo)
        {
            if (!GritState.Active || GritState.Broken || !ReferenceEquals(__instance, GritState.Health))
            {
                return;
            }

            try
            {
                var type = damageInfo.DamageType;
                var kind = type.IsBleeding() ? GritState.Bleeding
                    : type == EDamageType.Fall ? GritState.Fall
                    : type.IsEnemyDamage() && type != EDamageType.Sniper ? GritState.Enemy
                    : -1;
                if (kind < 0)
                {
                    return;
                }

                var head = bodyPart == EBodyPart.Head;
                var before = damage;
                damage *= head ? GritState.HeadFactor : GritState.Factor;
                GritState.Record(kind, head, before, damage);
            }
            catch (Exception e)
            {
                GritState.Broken = true;
                Log.Warn($"[active] grit damage patch disabled for this raid: {e.Message}");
                return;
            }

            if (!Game.PerkEffects.DevMode)
            {
                return;
            }

            // 재기만 한다. 실패해도 피해 감소는 그대로
            try
            {
                GritState.BeforeHit(__instance, bodyPart, damage, damageInfo.DamageType);
            }
            catch (Exception e)
            {
                GritState.Pending = -1;
                ActivePerkLog.Error("grit health before", e);
            }
        }

        private static void Postfix(ActiveHealthController __instance)
        {
            if (GritState.Pending < 0f || !ReferenceEquals(__instance, GritState.Health))
            {
                return;
            }

            try
            {
                GritState.AfterHit(__instance);
            }
            catch (Exception e)
            {
                GritState.Pending = -1;
                ActivePerkLog.Error("grit health after", e);
            }
        }
    }
}
