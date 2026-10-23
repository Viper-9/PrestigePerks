using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT.UI;
using HarmonyLib;
using PrestigePerks.Game;
using UnityEngine;
using UnityEngine.UI;

namespace PrestigePerks.Patches
{
    /// <summary>
    /// 프레스티지 전달 창의 머리말 패널(문구 + "(8x5)") 너비를 아래 그리드 패널 너비에 맞춘다 (dev-docs 3_프레스티지_코인.md 7절).
    /// 바닐라는 머리말 패널이 그리드보다 넓다. 창 크기는 단계마다 8x3 ~ 8x8 이라 너비는 늘 8칸이다.
    /// 그리드는 전달 창이 보일 때(ShowState → ValidateTransform) 배치되므로, ValidateTransform 이 한 프레임 뒤 다시 계산한 다음에 맞춘다.
    /// Init 시점에는 그리드 패널 너비가 아직 84 라 쓸 수 없다 (2026-10-23 로그).
    /// 구조: Transfer [VerticalLayoutGroup, ContentSizeFitter] 아래 Header [LayoutElement, HorizontalLayoutGroup], ItemsToTransfer, FitItemsTableButton.
    /// Transfer 너비는 Header 의 LayoutElement 선호 너비를 따른다.
    /// </summary>
    [HarmonyPatch]
    internal static class PrestigeTransferHeaderPatch
    {
        private const int WaitFrames = 3;

        // 이보다 좁으면 그리드가 아직 배치되지 않은 것으로 보고 건드리지 않는다 (8칸이면 수백 단위)
        private const float MinGridWidth = 200;

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(PrestigeTransferItemsState), nameof(PrestigeTransferItemsState.ValidateTransform));

        private static void Postfix(PrestigeTransferItemsState __instance)
        {
            try
            {
                Plugin.Instance.StartCoroutine(Align(__instance._sizeLabel.rectTransform, (RectTransform)__instance._transferView.transform));
            }
            catch (Exception e)
            {
                Log.Error($"PrestigeTransferItemsState.ValidateTransform postfix: {e}");
            }
        }

        private static IEnumerator Align(RectTransform label, RectTransform grid)
        {
            for (var i = 0; i < WaitFrames; i++)
            {
                yield return null;
            }

            try
            {
                if (label == null || grid == null)
                {
                    yield break;
                }

                // 크기 라벨과 그리드의 공통 부모 아래에서 각자 속한 자식이 머리말 패널, 그리드 패널이다
                var gridPath = Ancestors(grid).ToList();
                var header = Ancestors(label).TakeWhile(t => !gridPath.Contains(t)).LastOrDefault();
                var common = header?.parent as RectTransform;
                var gridPanel = common == null ? null : gridPath.TakeWhile(t => t != common).LastOrDefault();
                if (header == null || gridPanel == null || header == label)
                {
                    Log.Warn("prestige transfer header: header or grid panel not found, width not changed");
                    yield break;
                }

                LayoutRebuilder.ForceRebuildLayoutImmediate(gridPanel);
                var width = gridPanel.rect.width * gridPanel.lossyScale.x / header.lossyScale.x;
                if (PerkEffects.DevMode)
                {
                    Log.Info($"[dev] prestige transfer header before: grid {width:0.#}, {Describe(common)}");
                }

                if (width < MinGridWidth)
                {
                    Log.Warn($"prestige transfer header: grid panel not laid out yet ({width:0.#}), width not changed");
                    yield break;
                }

                var fitter = header.GetComponent<ContentSizeFitter>();
                if (fitter != null)
                {
                    fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                }

                // Unity 객체라 ?? 대신 명시적으로 비교한다
                var element = header.GetComponent<LayoutElement>();
                if (element == null)
                {
                    element = header.gameObject.AddComponent<LayoutElement>();
                }

                element.preferredWidth = width;
                element.flexibleWidth = 0;
                header.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                LayoutRebuilder.ForceRebuildLayoutImmediate(common);

                if (PerkEffects.DevMode)
                {
                    Log.Info($"[dev] prestige transfer header after: target {width:0.#}, {Describe(common)}");
                }
            }
            catch (Exception e)
            {
                Log.Warn($"prestige transfer header width failed: {e.Message}");
            }
        }

        private static IEnumerable<RectTransform> Ancestors(RectTransform start)
        {
            for (var t = start; t != null; t = t.parent as RectTransform)
            {
                yield return t;
            }
        }

        // 개발용: 공통 부모와 자식들의 이름, 너비, 레이아웃 컴포넌트
        private static string Describe(RectTransform common)
        {
            string One(RectTransform t) =>
                $"{t.name} w={t.rect.width:0.#} anchors=({t.anchorMin.x:0.##},{t.anchorMax.x:0.##}) [{string.Join(",", t.GetComponents<Component>().Where(c => c is ILayoutElement || c is ILayoutController).Select(c => c.GetType().Name).ToArray())}]";

            var children = Enumerable.Range(0, common.childCount).Select(i => common.GetChild(i) as RectTransform).Where(t => t != null);
            return $"{One(common)}\n  " + string.Join("\n  ", children.Select(One).ToArray());
        }
    }
}
