using System;
using System.Linq;
using System.Reflection;
using EFT.Prestige;
using EFT.UI.Prestige;
using HarmonyLib;
using PrestigePerks.Game;

namespace PrestigePerks.Patches
{
    /// <summary>
    /// 개발용: 프레스티지 화면이 "얻기" 버튼을 정할 때 (화면을 열거나 상태가 바뀔 때) 클라이언트 판정을 남긴다.
    /// 버튼은 PrestigeLevel.QuestStatus == AvailableForFinish 일 때만 열린다. 조건마다 현재값과 충족 여부를 찍는다.
    /// 서버가 Dev 빌드일 때만 남긴다.
    /// </summary>
    [HarmonyPatch]
    internal static class PrestigeClaimDiagnosticPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(PrestigeScreen), nameof(PrestigeScreen.UpdateClaimButton));

        private static void Postfix(PrestigeLevel prestige)
        {
            if (!PerkEffects.DevMode || prestige?.Template == null)
            {
                return;
            }

            try
            {
                var lines = prestige.Template.Conditions.Select(condition =>
                    prestige.ProgressCheckers.TryGetValue(condition, out var checker)
                        ? $"  {condition.GetType().Name} {condition.id} necessary={condition.IsNecessary} getter={checker.HasGetter()} current={checker.CurrentValue} {condition.compareMethod} {condition.value} done={checker.Test()}"
                        : $"  {condition.GetType().Name} {condition.id}: no progress checker");
                Log.Info($"[dev] prestige {prestige.Template.Id} status {prestige.QuestStatus} (button needs AvailableForFinish)\n{string.Join("\n", lines.ToArray())}");
            }
            catch (Exception e)
            {
                Log.Warn($"[dev] prestige diagnostic failed: {e.Message}");
            }
        }
    }
}
