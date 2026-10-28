using System;
using System.Reflection;
using BepInEx;
using EFT;
using HarmonyLib;
using PrestigePerks.Api;

namespace PrestigePerks.ExampleAddon
{
    /// <summary>
    /// Example client add-on. Gives "example.porter" (code, points, 10 in the server's perks/example/perks.json) its effect:
    /// +0.1 × tier scale on the carry weight multiplier. The built-in Porter perk patches the same getter, so the two add up.
    /// Registers its slot in Awake; the patch body reads Has, then IsPmc, then Scale (array reads only, no allocation).
    /// </summary>
    [BepInPlugin("com.example.prestigeperks.addon", "PrestigePerks Example Add-on", "1.0.0")]
    [BepInDependency("com.viper.prestigeperks")]
    public class ExamplePlugin : BaseUnityPlugin
    {
        /// <summary>Must match the id in the server's perks.json.</summary>
        public const string PorterId = "example.porter";

        /// <summary>Tier III effect (perks.json amount 10 = +0.1).</summary>
        public const float PorterWeight = 0.1f;

        internal static int PorterSlot = -1;

        private void Awake()
        {
            PorterSlot = PerkAddons.Register(PorterId);
            if (PorterSlot < 0)
            {
                Logger.LogWarning($"{PorterId} registration rejected");
                return;
            }

            try
            {
                new Harmony("com.example.prestigeperks.addon").CreateClassProcessor(typeof(ExamplePorterPatch)).Patch();
                Logger.LogInfo($"{PorterId} registered at slot {PorterSlot}");
            }
            catch (Exception e)
            {
                Logger.LogError($"{PorterId} patch failed: {e}");
            }
        }
    }

    /// <summary>SkillManager.CarryingWeightRelativeModifier getter (the same spot as the built-in Porter patch).</summary>
    [HarmonyPatch]
    internal static class ExamplePorterPatch
    {
        private static MethodBase TargetMethod() => AccessTools.PropertyGetter(typeof(SkillManager), nameof(SkillManager.CarryingWeightRelativeModifier));

        private static void Postfix(SkillManager __instance, ref float __result)
        {
            var slot = ExamplePlugin.PorterSlot;
            if (!PerkAddons.Has(slot))
            {
                return;
            }

            try
            {
                if (PerkAddons.IsPmc(__instance))
                {
                    __result += ExamplePlugin.PorterWeight * PerkAddons.Scale(slot);
                }
            }
            catch (Exception e)
            {
                PerkAddons.Fail(slot, e);
            }
        }
    }
}
