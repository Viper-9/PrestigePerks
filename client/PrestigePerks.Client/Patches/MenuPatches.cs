using System;
using System.Reflection;
using EFT;
using EFT.UI;
using EFT.UI.Matchmaker;
using HarmonyLib;

namespace PrestigePerks.Patches
{
    /// <summary>작업 표시줄이 만들어지면 "특전" 버튼을 붙인다.</summary>
    [HarmonyPatch]
    internal static class MenuTaskBarAwakePatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(MenuTaskBar), nameof(MenuTaskBar.Awake));

        private static void Postfix(MenuTaskBar __instance)
        {
            try
            {
                PerkController.Instance.OnTaskBarAwake(__instance);
            }
            catch (Exception e)
            {
                Log.Error($"MenuTaskBar.Awake postfix: {e}");
            }
        }
    }

    /// <summary>메인 메뉴에 들어올 때마다 (레이드 뒤 포함) 특전 상태를 받아 Bonus 를 반영하고 새 특전을 알린다. SPT·WTT 도 같은 지점을 쓴다.</summary>
    [HarmonyPatch]
    internal static class MenuScreenShowPatch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(MenuScreen), nameof(MenuScreen.Show), new[] { typeof(Profile), typeof(MatchmakerPlayersController), typeof(ESessionMode) });

        private static void Postfix()
        {
            try
            {
                PerkController.Instance.OnMainMenuShown();
            }
            catch (Exception e)
            {
                Log.Error($"MenuScreen.Show postfix: {e}");
            }
        }
    }
}
