using System;
using System.Reflection;
using EFT;
using EFT.Animations;
using HarmonyLib;
using UnityEngine;

namespace PrestigePerks.Active
{
    /// <summary>냉정 패치가 보는 값. 패치는 Active 를 가장 먼저 보고 바로 빠져나온다. 대상은 내 무기 애니메이션 하나 (ReferenceEquals).</summary>
    internal static class ComposureState
    {
        public static bool Active;
        public static ProceduralWeaponAnimation Weapon;
        public static float Recoil = 1f, Sway = 1f, Inertia = 1f;
        public static bool RecoilChecked;

        // 패치 하나가 예외를 내면 그 레이드 동안 그 패치만 끈다 (경고 한 번)
        public static bool RecoilBroken, SwayBroken, InertiaBroken;

        public static void Clear()
        {
            Active = false;
            Weapon = null;
            Recoil = Sway = Inertia = 1f;
            RecoilChecked = false;
        }

        public static void Broke(ref bool flag, string patch, Exception e)
        {
            flag = true;
            Log.Warn($"[active] composure {patch} patch disabled for this raid: {e.Message}");
        }
    }

    /// <summary>
    /// 냉정 (실현성조사 3.7): 반동·조준 흔들림 −reduction%, 마우스 관성 −inertia%, 숨 참기 무제한. 패시브 제어(14)와 달리 스킬 경로가 아니라
    /// 프레임마다 쓰이는 값에 곱하므로 무기를 바꿔도 그대로다. 시작·끝에 OnAimOrPoseChanged 로 흔들림·관성을 다시 계산한다.
    /// </summary>
    internal sealed class Composure : ActivePerkEffect
    {
        private Physical physical;
        private float holdBreath;

        public override void RaidStarted(ActivePerkUse use)
        {
            ComposureState.Clear();
            ComposureState.RecoilBroken = ComposureState.SwayBroken = ComposureState.InertiaBroken = false;
        }

        public override void Start(ActivePerkUse use)
        {
            var weapon = use.Player.ProceduralWeaponAnimation;
            var swayBefore = weapon.Breath.Intensity;
            var inertiaBefore = weapon.MotionReact.SwayFactors;
            ComposureState.Weapon = weapon;
            ComposureState.Recoil = ComposureState.Sway = 1f - use.Value("reduction") / 100f;
            ComposureState.Inertia = 1f - use.Value("inertia") / 100f;
            ComposureState.RecoilChecked = false;
            ComposureState.Active = true;
            weapon.OnAimOrPoseChanged();
            ActivePerkLog.Check("composure.sway", Mathf.Abs(weapon.Breath.Intensity - Vanilla(weapon) * ComposureState.Sway) < 0.001f,
                $"breath intensity {swayBefore:F3} -> {weapon.Breath.Intensity:F3} (x{ComposureState.Sway:F2})");
            ActivePerkLog.Check("composure.inertia", weapon.MotionReact.SwayFactors.x <= inertiaBefore.x + 0.0001f,
                $"sway factors {inertiaBefore} -> {weapon.MotionReact.SwayFactors} (x{ComposureState.Inertia:F3})");

            // 숨 참기: 소모를 0 으로 (Compute 라 바로 반영), 산소를 채워 숨 참기를 바로 시작할 수 있게
            physical = use.Player.Physical as Physical;
            if (physical != null)
            {
                holdBreath = physical.BaseHoldBreathConsumption;
                physical.BaseHoldBreathConsumption = 0f;
                physical.Oxygen.UpdateStamina(physical.Oxygen.TotalCapacity);
            }

            ActivePerkLog.Check("composure.breath", physical != null && physical.BaseHoldBreathConsumption == 0f,
                physical == null ? "player physical is not Physical" : $"hold breath consumption {holdBreath:F2} -> 0");
        }

        public override void Stop(ActivePerkUse use)
        {
            var weapon = ComposureState.Weapon;
            if (!ComposureState.RecoilChecked)
            {
                ActivePerkLog.Skip("composure.recoil", "no shot while running");
            }

            ComposureState.Clear();
            if (weapon != null)
            {
                weapon.OnAimOrPoseChanged();
                ActivePerkLog.Check("composure.restored", Mathf.Abs(weapon.Breath.Intensity - Vanilla(weapon)) < 0.001f,
                    $"breath intensity {weapon.Breath.Intensity:F3}, vanilla {Vanilla(weapon):F3}");
            }

            if (physical != null)
            {
                physical.BaseHoldBreathConsumption = holdBreath;
                ActivePerkLog.Check("composure.breath", physical.BaseHoldBreathConsumption == holdBreath, $"hold breath consumption restored to {holdBreath:F2}");
                physical = null;
            }
        }

        public override string Describe(ActivePerkUse use)
        {
            var weapon = use.Player.ProceduralWeaponAnimation;
            return $"breath intensity {weapon.Breath.Intensity:F3}, sway factors {weapon.MotionReact.SwayFactors}";
        }

        // OnAimOrPoseChanged 의 원래 계산
        private static float Vanilla(ProceduralWeaponAnimation weapon) => weapon.IntensityByPoseLevel * weapon.IntensityByAiming;
    }

    /// <summary>냉정 반동: Shoot(str) 의 str 이 손·카메라 반동 세기에 곱해진다 (옛 반동 모드도 같은 입구).</summary>
    [HarmonyPatch]
    internal static class ComposureRecoilPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(ProceduralWeaponAnimation), nameof(ProceduralWeaponAnimation.Shoot));

        private static void Prefix(ProceduralWeaponAnimation __instance, ref float str)
        {
            if (!ComposureState.Active || ComposureState.RecoilBroken || !ReferenceEquals(__instance, ComposureState.Weapon))
            {
                return;
            }

            try
            {
                var before = str;
                str *= ComposureState.Recoil;
                if (!ComposureState.RecoilChecked)
                {
                    ComposureState.RecoilChecked = true;
                    ActivePerkLog.Check("composure.recoil", str < before, $"first shot recoil strength {before:F3} -> {str:F3}");
                }
            }
            catch (Exception e)
            {
                ComposureState.Broke(ref ComposureState.RecoilBroken, "recoil", e);
            }
        }
    }

    /// <summary>냉정 조준 흔들림: 자세·조준이 바뀔 때 다시 계산되는 Breath.Intensity 에 곱한다.</summary>
    [HarmonyPatch]
    internal static class ComposureSwayPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(ProceduralWeaponAnimation), nameof(ProceduralWeaponAnimation.OnAimOrPoseChanged));

        private static void Postfix(ProceduralWeaponAnimation __instance)
        {
            if (!ComposureState.Active || ComposureState.SwayBroken || !ReferenceEquals(__instance, ComposureState.Weapon))
            {
                return;
            }

            try
            {
                __instance.Breath.Intensity *= ComposureState.Sway;
            }
            catch (Exception e)
            {
                ComposureState.Broke(ref ComposureState.SwayBroken, "sway", e);
            }
        }
    }

    /// <summary>냉정 마우스 관성: 무기 무게로 정해지는 MotionReact.SwayFactors 에 곱한다.</summary>
    [HarmonyPatch]
    internal static class ComposureInertiaPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(ProceduralWeaponAnimation), nameof(ProceduralWeaponAnimation.UpdateSwayFactors));

        private static void Postfix(ProceduralWeaponAnimation __instance)
        {
            if (!ComposureState.Active || ComposureState.InertiaBroken || !ReferenceEquals(__instance, ComposureState.Weapon))
            {
                return;
            }

            try
            {
                __instance.MotionReact.SwayFactors *= ComposureState.Inertia;
            }
            catch (Exception e)
            {
                ComposureState.Broke(ref ComposureState.InertiaBroken, "inertia", e);
            }
        }
    }
}
