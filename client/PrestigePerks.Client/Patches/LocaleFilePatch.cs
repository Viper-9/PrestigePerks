using System;
using System.Reflection;
using EFT;
using HarmonyLib;
using PrestigePerks.Game;

namespace PrestigePerks.Patches
{
    /// <summary>
    /// 플러그인 폴더 언어 파일의 게임 문구("game:" 키)를 게임 언어에 넣는다. 서버 언어(서버 PrestigeLocales 포함)가 들어올 때마다 그 뒤에 덮어써서 파일이 이긴다.
    /// 모든 언어 갱신이 UpdateLocale(localeId, Locale) 을 지나고, Locale.Merge 는 같은 키를 덮어쓴다.
    /// </summary>
    [HarmonyPatch]
    internal static class LocaleFilePatch
    {
        private static bool applying;

        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(LocalizationManager), nameof(LocalizationManager.UpdateLocale), new[] { typeof(string), typeof(Locale) });

        private static void Postfix(LocalizationManager __instance, string localeId)
        {
            if (applying)
            {
                return;
            }

            try
            {
                var texts = PerkLocaleFolder.Game(localeId);
                if (texts == null)
                {
                    return;
                }

                applying = true;
                __instance.UpdateLocale(localeId, __instance.ConvertToLocale(texts));
            }
            catch (Exception e)
            {
                Log.Error($"LocalizationManager.UpdateLocale postfix: {e}");
            }
            finally
            {
                applying = false;
            }
        }
    }
}
