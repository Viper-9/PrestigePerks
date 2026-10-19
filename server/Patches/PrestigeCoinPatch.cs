using System.Reflection;
using HarmonyLib;
using PrestigePerks.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Eft.Profile;

namespace PrestigePerks.Patches;

/// <summary>
/// 프레스티지 전달 아이템 → 코인 (dev-docs 3_프레스티지_코인.md 6절 "처리 지점").
/// prefix 에서 이월·환산을 계산하고 prestige.Items 를 비워 바닐라 전달 메일을 막는다. postfix 에서 코인 메일을 보낸다.
/// 계산이 실패하면 prestige.Items 를 그대로 둔다 (아이템을 잃는 것보다 바닐라대로 넘어가는 편이 낫다).
/// </summary>
[HarmonyPatch]
internal static class PrestigeCoinPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(PrestigeHelper), nameof(PrestigeHelper.ProcessPendingPrestige));

    private static void Prefix(SptProfile oldProfile, PendingPrestige prestige, out PrestigeCoinResult? __state)
    {
        __state = null;
        try
        {
            __state = PrestigeCoinService.Instance?.Prepare(oldProfile, prestige);
        }
        catch (Exception e)
        {
            ServerPatcher.Fail(nameof(PrestigeCoinPatch), e);
        }
    }

    private static void Postfix(SptProfile newProfile, PrestigeCoinResult? __state)
    {
        if (__state is not { } result)
        {
            return;
        }

        try
        {
            PrestigeCoinService.Instance?.Deliver(newProfile, result);
        }
        catch (Exception e)
        {
            ServerPatcher.Fail(nameof(PrestigeCoinPatch), e);
        }
    }
}
