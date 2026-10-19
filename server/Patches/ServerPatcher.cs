using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;

namespace PrestigePerks.Patches;

/// <summary>서버 Harmony 패치 등록 (17번 장인). 하나가 실패해도 나머지는 걸리도록 클래스마다 따로 등록한다.</summary>
[Injectable(InjectionType.Singleton)]
public class ServerPatcher(ISptLogger<ServerPatcher> logger)
{
    private static ISptLogger<ServerPatcher>? log;
    private static readonly HashSet<string> Failed = [];

    private static readonly Type[] Patches =
    [
        typeof(CraftTimePatch),
        typeof(ScavCaseTimePatch),
        typeof(CultistCircleTimePatch),
        typeof(BitcoinFarmScopePatch),
        typeof(BitcoinElapsedPatch),
    ];

    public void PatchAll()
    {
        log = logger;
        var harmony = new Harmony("com.viper.prestigeperks.server");
        foreach (var patch in Patches)
        {
            try
            {
                harmony.CreateClassProcessor(patch).Patch();
            }
            catch (Exception e)
            {
                logger.Error($"[PrestigePerks] patch {patch.Name} failed: {e}");
            }
        }

        foreach (var method in harmony.GetPatchedMethods())
        {
            logger.Info($"[PrestigePerks] patched {method.DeclaringType?.Name}.{method.Name}");
        }
    }

    /// <summary>패치 본문에서 예외가 나면 바닐라 값으로 두고 패치마다 경고를 한 번만 남긴다.</summary>
    internal static void Fail(string patch, Exception e)
    {
        lock (Failed)
        {
            if (!Failed.Add(patch))
            {
                return;
            }
        }

        log?.Warning($"[PrestigePerks] {patch} error, using vanilla value: {e}");
    }
}
