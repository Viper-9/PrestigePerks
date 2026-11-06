using System.Reflection;
using EFT;
using HarmonyLib;

namespace PrestigePerks.Active
{
    /// <summary>레이드 시작 (실현성조사 2.1). GameWorld 게임오브젝트에 레이드 컴포넌트를 붙인다. 레이드가 끝나 오브젝트가 없어지면 같이 없어진다.</summary>
    [HarmonyPatch]
    internal static class GameStartedPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted));

        private static void Postfix(GameWorld __instance) => ActivePerkRaid.Begin(__instance);
    }
}
