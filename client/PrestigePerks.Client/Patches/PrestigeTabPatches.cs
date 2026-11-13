using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT.UI;
using HarmonyLib;
using PrestigePerks.UI;
using UnityEngine.EventSystems;

namespace PrestigePerks.Patches
{
    /// <summary>캐릭터 화면이 열릴 때마다 프레스티지 탭 드롭다운과 특전 탭을 붙인다. 오버로드가 여럿이라 EInventoryTab 인자를 가진 가장 긴 것을 고른다.</summary>
    [HarmonyPatch]
    internal static class InventoryScreenShowPatch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.GetDeclaredMethods(typeof(InventoryScreen))
                .Where(m => m.Name == "Show" && m.GetParameters().Any(p => p.ParameterType == typeof(EInventoryTab)))
                .OrderByDescending(m => m.GetParameters().Length)
                .FirstOrDefault() ?? throw new MissingMethodException(nameof(InventoryScreen), "Show(…, EInventoryTab, …)");

        private static void Postfix(InventoryScreen __instance)
        {
            try
            {
                PerkTabHost.AttachOrRefresh(__instance);
            }
            catch (Exception e)
            {
                Log.Error($"InventoryScreen.Show postfix: {e}");
            }
        }
    }

    /// <summary>
    /// 등록한 탭은 마우스로 눌렀을 때 게임 선택 대신 등록한 동작을 한다. 프레스티지 탭(드롭다운 열기)과 드롭다운 항목에 쓴다.
    /// HandlePointerClick 이 아니라 OnPointerClick 을 막아 코드에서 HandlePointerClick 을 부르는 길은 그대로 둔다.
    /// 고른 탭은 CanHandlePointerClick 이 false 라 게임이 클릭을 버리지만, 여기서는 그 전에 받으므로 고른 상태에서도 열린다.
    /// 누를 수 없는 탭(레이드 중 프레스티지 탭)은 게임에 맡긴다. Briefing 모드도 같은 메서드를 막지만 서로 다른 탭만 맡는다.
    /// </summary>
    [HarmonyPatch(typeof(global::Tab), nameof(global::Tab.OnPointerClick))]
    internal static class PerkTabClickPatch
    {
        private static readonly Dictionary<global::Tab, Action> Handlers = new Dictionary<global::Tab, Action>();

        public static void Register(global::Tab tab, Action onClick) => Handlers[tab] = onClick;

        public static void Unregister(global::Tab tab)
        {
            if (!ReferenceEquals(tab, null))
            {
                Handlers.Remove(tab);
            }
        }

        private static bool Prefix(global::Tab __instance, PointerEventData eventData)
        {
            try
            {
                if (!Handlers.TryGetValue(__instance, out var onClick) || !__instance.Interactable)
                {
                    return true;
                }

                if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
                {
                    return false;
                }

                onClick();
                return false;
            }
            catch (Exception e)
            {
                Log.Error($"tab click failed: {e}");
                // 실패하면 게임 동작대로 (프레스티지 탭이면 프레스티지 화면이 열린다)
                return true;
            }
        }
    }
}
