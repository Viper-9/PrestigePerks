using System.Reflection;
using HarmonyLib;
using PrestigePerks.Items;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;

namespace PrestigePerks.Patches;

/// <summary>
/// 보스 코인 (dev-docs 3_프레스티지_코인.md 4절). 봇 생성이 끝난 직후 코인을 넣는다.
/// ISB 는 같은 메서드 후처리에서 장비를 프리셋으로 통째로 바꾸므로 (priority 0) 그보다 뒤에 돌아야 코인이 남는다.
/// 예외가 나면 SPT 가 그 봇을 버려 보스가 안 나오므로 여기서 모두 잡는다.
/// </summary>
[HarmonyPatch]
internal static class BossCoinPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(BotGenerator), nameof(BotGenerator.PrepareAndGenerateBot));

    [HarmonyPriority(Priority.Last)]
    [HarmonyAfter(
        "com.samc137.isbspecialforces.botpresets.server",
        "com.samc137.isbspecialforces.dogtags.server",
        "com.samc137.isbspecialforces.containmenthealth")]
    private static void Postfix(MongoId sessionId, BotGenerationDetails botGenerationDetails, BotBase __result)
    {
        try
        {
            BossCoinService.Instance?.Add(sessionId, botGenerationDetails, __result);
        }
        catch (Exception e)
        {
            ServerPatcher.Fail(nameof(BossCoinPatch), e);
        }
    }
}
