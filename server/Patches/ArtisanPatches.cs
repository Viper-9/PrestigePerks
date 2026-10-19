using System.Reflection;
using HarmonyLib;
using PrestigePerks.Perks;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Services.Hideout;

namespace PrestigePerks.Patches;

// 17번 장인 서버 패치 (dev-docs 2_특전.md 6절 "17번 장인"). 요청한 PMC 프로필의 특전으로 판단한다.
// 클라이언트처럼 패치 클래스마다 따로 등록하고 (ServerPatcher), 예외가 나면 바닐라 값을 그대로 둔다.

/// <summary>일반·연속 제작: GetAdjustedCraftTimeWithSkills 결과 ×0.8, 최소 5초. 비트코인 레시피는 바닐라처럼 제외한다.</summary>
[HarmonyPatch]
internal static class CraftTimePatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(HideoutHelper), nameof(HideoutHelper.GetAdjustedCraftTimeWithSkills));

    private static void Postfix(PmcData pmcData, MongoId recipeId, ref double? __result)
    {
        try
        {
            if (recipeId != HideoutHelper.BitcoinProductionId && PerkArtisan.Has(pmcData))
            {
                __result = PerkArtisan.CraftTime(__result);
            }
        }
        catch (Exception e)
        {
            ServerPatcher.Fail(nameof(CraftTimePatch), e);
        }
    }
}

/// <summary>스캐브 케이스: Fence 배율까지 건 시간(GetScavCaseTime) ×0.8, 최소 5초.</summary>
[HarmonyPatch]
internal static class ScavCaseTimePatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(HideoutController), "GetScavCaseTime");

    private static void Postfix(PmcData pmcData, ref double? __result)
    {
        try
        {
            if (PerkArtisan.Has(pmcData))
            {
                __result = PerkArtisan.CraftTime(__result);
            }
        }
        catch (Exception e)
        {
            ServerPatcher.Fail(nameof(ScavCaseTimePatch), e);
        }
    }
}

/// <summary>컬티스트 제단: 등록하는 시간 ×0.8, 최소 5초. GetCircleCraftingInfo 는 프로필 인자가 없어 등록 함수에서 바꾼다.</summary>
[HarmonyPatch]
internal static class CultistCircleTimePatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(CircleOfCultistService), "RegisterCircleOfCultistProduction");

    private static void Prefix(PmcData pmcData, ref double craftingTime)
    {
        try
        {
            if (PerkArtisan.Has(pmcData))
            {
                craftingTime = PerkArtisan.CraftTime(craftingTime) ?? craftingTime;
            }
        }
        catch (Exception e)
        {
            ServerPatcher.Fail(nameof(CultistCircleTimePatch), e);
        }
    }
}

/// <summary>
/// 비트코인 (바깥): 속도는 UpdateBitcoinFarm 안 지역 변수라, PMC 가 17번이면 그 안에서 부르는 경과 시간을 1.25배로 한다.
/// 요청은 여러 스레드에서 오므로 범위 플래그는 스레드마다 둔다.
/// </summary>
[HarmonyPatch]
internal static class BitcoinFarmScopePatch
{
    [ThreadStatic]
    internal static bool Active;

    private static MethodBase TargetMethod() => AccessTools.Method(typeof(HideoutHelper), "UpdateBitcoinFarm");

    private static void Prefix(PmcData pmcData, out bool __state)
    {
        __state = Active;
        Active = false;
        try
        {
            Active = PerkArtisan.Has(pmcData);
        }
        catch (Exception e)
        {
            ServerPatcher.Fail(nameof(BitcoinFarmScopePatch), e);
        }
    }

    private static void Finalizer(bool __state) => Active = __state;
}

/// <summary>비트코인 (안쪽): 범위 안에서만 경과 시간 ÷0.8.</summary>
[HarmonyPatch]
internal static class BitcoinElapsedPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(HideoutHelper), "GetTimeElapsedSinceLastServerTick");

    private static void Postfix(ref long? __result)
    {
        if (BitcoinFarmScopePatch.Active)
        {
            __result = PerkArtisan.BitcoinElapsed(__result);
        }
    }
}
