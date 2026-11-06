using System.Linq;
using BepInEx;
using BepInEx.Configuration;
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

    /// <summary>F12 설정의 개발자 모드: 개발용 Commando 명령(prestige ...)과 [dev] 진단 로그 (dev-docs 5_개발자_모드.md).</summary>
    internal static ConfigEntry<bool> DevMode;

    /// <summary>액티브 특전 키 (dev-docs 94). 서버 activePerks.enabled 가 false 면 레이드에서 쓰이지 않는다.</summary>
    internal static ConfigEntry<KeyboardShortcut> ActiveKey;

    /// <summary>음파 탐지 잔상 밝기: 맨눈, 야간 투시경, 열화상.</summary>
    internal static ConfigEntry<float> SonarBrightness, SonarBrightnessNight, SonarBrightnessThermal;

    /// <summary>음파 탐지 파동 고리 (발 높이). 끄면 잔상만.</summary>
    internal static ConfigEntry<bool> SonarWaveRing;

    /// <summary>개발자 모드 시험: 슬롯과 상관없이 이 액티브 특전·단계로 레이드 시작 (slot = 액티브 칸 그대로, 단계 0 = 적용 단계).</summary>
    internal static ConfigEntry<string> ActiveTestPerk;
    internal static ConfigEntry<int> ActiveTestTier;
    internal const string ActiveTestSlot = "slot";

    /// <summary>개발자 모드: 액티브 특전 사용 기록·쿨타임 초기화 키, 표시 키 (스크린샷 + 상태 한 줄), 출혈 키 (시험용 출혈).</summary>
    internal static ConfigEntry<KeyboardShortcut> ActiveResetKey, MarkKey, BleedKey;

    private void Awake()
    {
        Instance = this;
        Log.Source = Logger;
        DevMode = Config.Bind("Developer", "Developer mode", false,
            new ConfigDescription("Testing only. Enables the prestige chat commands (coins, perks, tiers) and [dev] diagnostic logs. Takes effect on the server right away.",
                null, new ConfigurationManagerAttributes { Order = 100 }));
        Game.PerkEffects.DevMode = DevMode.Value;
        DevMode.SettingChanged += (_, _) =>
        {
            Game.PerkEffects.DevMode = DevMode.Value;
            PerkController.Instance.SendDevMode();
        };
        BindActive();
        var harmony = new Harmony("com.viper.prestigeperks");
        PerkController.Create();
        PatchOne(harmony, typeof(Patches.LocaleFilePatch));
        PatchOne(harmony, typeof(Patches.MenuTaskBarAwakePatch));
        PatchOne(harmony, typeof(Patches.MenuScreenShowPatch));
        PatchOne(harmony, typeof(Patches.PrestigeStatusRecheckPatch));
        PatchOne(harmony, typeof(Patches.PrestigeClaimDiagnosticPatch));
        PatchOne(harmony, typeof(Patches.PrestigeTransferHeaderPatch));
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
        // 액티브 특전 (dev-docs 93): 레이드 시작
        PatchOne(harmony, typeof(Active.GameStartedPatch));
        PatchOne(harmony, typeof(Active.ComposureRecoilPatch));
        PatchOne(harmony, typeof(Active.ComposureSwayPatch));
        PatchOne(harmony, typeof(Active.ComposureInertiaPatch));
        PatchOne(harmony, typeof(Active.GritDamagePatch));
        foreach (var m in harmony.GetPatchedMethods()) Log.Info($"patched {m.DeclaringType?.Name}.{m.Name}");
        Log.Info($"PrestigePerks {Info.Metadata.Version} loaded");
    }

    private void BindActive()
    {
        const string section = "Active perk";
        ActiveKey = Config.Bind(section, "Use key", new KeyboardShortcut(UnityEngine.KeyCode.V),
            new ConfigDescription("Key that uses the active perk in a raid. Active perks are off when the server config activePerks.enabled is false.",
                null, new ConfigurationManagerAttributes { Order = 100 }));
        SonarBrightness = Config.Bind(section, "Sonar brightness (naked eye)", 1.2f,
            new ConfigDescription("Sonar Pulse echo brightness without night vision or thermal.", new AcceptableValueRange<float>(0f, 3f),
                new ConfigurationManagerAttributes { Order = 90 }));
        SonarBrightnessNight = Config.Bind(section, "Sonar brightness (night vision)", 0.35f,
            new ConfigDescription("Sonar Pulse echo brightness with night vision on.", new AcceptableValueRange<float>(0f, 3f),
                new ConfigurationManagerAttributes { Order = 80 }));
        SonarBrightnessThermal = Config.Bind(section, "Sonar brightness (thermal)", 0.5f,
            new ConfigDescription("Sonar Pulse echo brightness with a thermal sight or goggles on.", new AcceptableValueRange<float>(0f, 3f),
                new ConfigurationManagerAttributes { Order = 70 }));
        SonarWaveRing = Config.Bind(section, "Sonar wave ring", true,
            new ConfigDescription("Shows the Sonar Pulse wave as a ring at foot height. Off = echoes only.",
                null, new ConfigurationManagerAttributes { Order = 60 }));

        var perks = new[] { ActiveTestSlot }.Concat(PrestigePerks.Model.ActivePerkIds.All).ToArray();
        ActiveTestPerk = Config.Bind("Developer", "Active perk test", ActiveTestSlot,
            new ConfigDescription("Developer mode only. Starts the next raid with this active perk instead of the one in the active slot (slot = the active slot). In a raid, press the reset key to switch.",
                new AcceptableValueList<string>(perks), new ConfigurationManagerAttributes { Order = 90 }));
        ActiveTestTier = Config.Bind("Developer", "Active perk test tier", 0,
            new ConfigDescription("Developer mode only. Tier of the active perk in the next raid (0 = the tier the server applies). In a raid, press the reset key to switch.", new AcceptableValueRange<int>(0, 3),
                new ConfigurationManagerAttributes { Order = 80 }));
        ActiveResetKey = Config.Bind("Developer", "Active perk reset key", new KeyboardShortcut(UnityEngine.KeyCode.H, UnityEngine.KeyCode.LeftAlt),
            new ConfigDescription("Developer mode only. In a raid: ends a running effect, switches to the test perk and tier if they changed, otherwise clears the use and cooldown.",
                null, new ConfigurationManagerAttributes { Order = 70 }));
        MarkKey = Config.Bind("Developer", "Mark key", new KeyboardShortcut(UnityEngine.KeyCode.F10),
            new ConfigDescription("Developer mode only. Takes a screenshot and logs the active perk state in one line ([active][MARK]).",
                null, new ConfigurationManagerAttributes { Order = 60 }));
        BleedKey = Config.Bind("Developer", "Bleed key", new KeyboardShortcut(UnityEngine.KeyCode.J, UnityEngine.KeyCode.LeftAlt),
            new ConfigDescription("Developer mode only. In a raid: gives you a light bleeding, pressing again swaps it for a heavy one and back. Does nothing while a blood loss blocker is on.",
                null, new ConfigurationManagerAttributes { Order = 50 }));
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
