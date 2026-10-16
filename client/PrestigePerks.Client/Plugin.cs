using BepInEx;
using HarmonyLib;

namespace PrestigePerks;

// 버전은 Directory.Build.props 의 <Version> 에서 온다. 빌드 때 BuildInfo.g.cs 로 만들어진다.
[BepInPlugin("com.viper.prestigeperks", "PrestigePerks", BuildInfo.Version)]
[BepInDependency("wtf.archangel.contentbackportprestiges")]
[BepInDependency("com.wtt.commonlib")]
[BepInDependency("com.wtt.contentbackport")]
public class Plugin : BaseUnityPlugin
{
    internal static Plugin Instance;

    private void Awake()
    {
        Instance = this;
        Log.Source = Logger;
        var harmony = new Harmony("com.viper.prestigeperks");
        foreach (var m in harmony.GetPatchedMethods()) Log.Info($"patched {m.DeclaringType?.Name}.{m.Name}");
        Log.Info($"PrestigePerks {Info.Metadata.Version} loaded");
    }

    // 하나가 실패해도 나머지는 걸리도록 패치 클래스마다 따로 등록한다.
    internal static void PatchOne(Harmony harmony, System.Type patchClass)
    {
        try
        {
            harmony.CreateClassProcessor(patchClass).Patch();
        }
        catch (System.Exception e)
        {
            Log.Error($"patch {patchClass.Name} failed: {e}");
        }
    }
}
