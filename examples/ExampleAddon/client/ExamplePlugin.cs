using System;
using System.Reflection;
using BepInEx;
using EFT;
using HarmonyLib;
using PrestigePerks.Api;

namespace PrestigePerks.ExampleAddon
{
    /// <summary>
    /// 예제 클라이언트 add-on (B형, dev-docs 95 Task 7). 서버 perks/example/perks.json 의 "example.porter"(code, points, 10) 효과를 낸다:
    /// 소지 무게 한도 배율에 +0.1 × 단계 비율. 내장 9번 짐꾼과 같은 지점이라 둘 다 장착하면 합쳐진다.
    /// Awake 에서 칸을 받고, 패치 본문은 Has → IsPmc → Scale 순서로 읽는다 (배열 읽기만, 할당 없음).
    /// </summary>
    [BepInPlugin("com.example.prestigeperks.addon", "PrestigePerks Example Add-on", "1.0.0")]
    [BepInDependency("com.viper.prestigeperks")]
    public class ExamplePlugin : BaseUnityPlugin
    {
        /// <summary>서버 perks.json 의 id 와 같아야 한다.</summary>
        public const string PorterId = "example.porter";

        /// <summary>III 효과량 (perks.json amount 10 = +0.1).</summary>
        public const float PorterWeight = 0.1f;

        internal static int PorterSlot = -1;

        private void Awake()
        {
            PorterSlot = PerkAddons.Register(PorterId);
            if (PorterSlot < 0)
            {
                Logger.LogWarning($"{PorterId} registration rejected");
                return;
            }

            try
            {
                new Harmony("com.example.prestigeperks.addon").CreateClassProcessor(typeof(ExamplePorterPatch)).Patch();
                Logger.LogInfo($"{PorterId} registered at slot {PorterSlot}");
            }
            catch (Exception e)
            {
                Logger.LogError($"{PorterId} patch failed: {e}");
            }
        }
    }

    /// <summary>SkillManager.CarryingWeightRelativeModifier getter (내장 CarryingWeightPatch 와 같은 지점).</summary>
    [HarmonyPatch]
    internal static class ExamplePorterPatch
    {
        private static MethodBase TargetMethod() => AccessTools.PropertyGetter(typeof(SkillManager), nameof(SkillManager.CarryingWeightRelativeModifier));

        private static void Postfix(SkillManager __instance, ref float __result)
        {
            var slot = ExamplePlugin.PorterSlot;
            if (!PerkAddons.Has(slot))
            {
                return;
            }

            try
            {
                if (PerkAddons.IsPmc(__instance))
                {
                    __result += ExamplePlugin.PorterWeight * PerkAddons.Scale(slot);
                }
            }
            catch (Exception e)
            {
                PerkAddons.Fail(slot, e);
            }
        }
    }
}
