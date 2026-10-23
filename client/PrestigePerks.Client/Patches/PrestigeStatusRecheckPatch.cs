using System;
using System.Reflection;
using EFT.Prestige;
using EFT.Quests;
using EFT.UI.Prestige;
using HarmonyLib;

namespace PrestigePerks.Patches
{
    /// <summary>
    /// 프레스티지 화면이 "얻기" 버튼을 정하기 전에 상태를 한 번 더 판정한다.
    /// 바닐라는 로그인 때와 조건 값 변경 이벤트 때만 Started → AvailableForFinish 로 바꾼다.
    /// 메일로 받은 루블(플리 판매 대금, 보험 반환 등)은 이벤트를 일으키지 않아,
    /// 마지막 조건이 그렇게 채워지면 재접속 전까지 "조건 미달성"으로 남는다 (2026-10-23 확인).
    /// 조건이 하나라도 안 맞으면 CheckForStatusChange 가 아무것도 바꾸지 않는다.
    /// </summary>
    [HarmonyPatch]
    internal static class PrestigeStatusRecheckPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(PrestigeScreen), nameof(PrestigeScreen.UpdateClaimButton));

        private static void Prefix(PrestigeLevel prestige)
        {
            try
            {
                if (prestige?.QuestStatus == EQuestStatus.Started)
                {
                    prestige.CheckForStatusChange(EQuestStatus.AvailableForFinish, notify: true, fromServer: false, canFail: false);
                }
            }
            catch (Exception e)
            {
                Log.Warn($"prestige status recheck failed: {e.Message}");
            }
        }
    }
}
