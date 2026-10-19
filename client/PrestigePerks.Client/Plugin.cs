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
        PerkController.Create();
        PatchOne(harmony, typeof(Patches.MenuTaskBarAwakePatch));
        PatchOne(harmony, typeof(Patches.MenuScreenShowPatch));
        PatchOne(harmony, typeof(Patches.PrestigeClaimDiagnosticPatch));
        // 패치 특전 (3차). 하나가 실패해도 나머지 특전은 동작한다
        PatchOne(harmony, typeof(Patches.CarryingWeightPatch));
        PatchOne(harmony, typeof(Patches.OverweightLimitsPatch));
        PatchOne(harmony, typeof(Patches.StaminaCapacityPatch));
        PatchOne(harmony, typeof(Patches.ExistenceDamagePatch));
        PatchOne(harmony, typeof(Patches.ShotSkillScopePatch));
        PatchOne(harmony, typeof(Patches.MasteringTriggerPatch));
        PatchOne(harmony, typeof(Patches.AttentionLootSpeedPatch));
        PatchOne(harmony, typeof(Patches.SearchBuffSpeedPatch));
        PatchOne(harmony, typeof(Patches.ApplyDamageScopePatch));
        PatchOne(harmony, typeof(Patches.ProbabilityTryPatch));
        PatchOne(harmony, typeof(Patches.RecoilSuppressionPatch));
        PatchOne(harmony, typeof(Patches.ReloadSpeedPatch));
        PatchOne(harmony, typeof(Patches.ErgonomicsPatch));
        PatchOne(harmony, typeof(Patches.MalfunctionChancePatch));
        PatchOne(harmony, typeof(Patches.ShotOverheatPatch));
        PatchOne(harmony, typeof(Patches.WeaponShotWearPatch));
        PatchOne(harmony, typeof(Patches.ArmorWearPatch));
        PatchOne(harmony, typeof(Patches.MoveSpeedPatch));
        PatchOne(harmony, typeof(Patches.MagazineLoadTimePatch));
        PatchOne(harmony, typeof(Patches.MedEffectScopePatch));
        PatchOne(harmony, typeof(Patches.SurgeryTimePatch));
        PatchOne(harmony, typeof(Patches.ProducerReductionPatch));
        PatchOne(harmony, typeof(Patches.ScavCaseReductionPatch));
        PatchOne(harmony, typeof(Patches.BitcoinSpeedPatch));
        PatchOne(harmony, typeof(Patches.CraftTimeFloorPatch));
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
