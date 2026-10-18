using System;
using EFT.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PrestigePerks.UI
{
    /// <summary>
    /// 하단 작업 표시줄의 "특전" 버튼. 바닐라 은신처 버튼을 복제해 같은 모양으로 은신처 오른쪽에 둔다.
    /// 복제본에는 바닐라가 코드로 붙인 클릭 처리(MenuTaskBar.Awake)가 따라오지 않아 우리 처리만 붙는다.
    /// </summary>
    internal static class PerkTaskbarButton
    {
        public static AnimatedToggle Inject(MenuTaskBar taskBar, string label, Action<bool> onToggle)
        {
            if (taskBar._toggleButtons == null || !taskBar._toggleButtons.TryGetValue(EMenuType.Hideout, out var hideout) || hideout == null)
            {
                Log.Warn("taskbar: hideout button not found, perk button not added");
                return null;
            }

            var source = hideout.gameObject;

            // Briefing 모드(TabClone)와 같은 방식: 꺼진 임시 부모 밑에 복제해 복제본의 Awake/OnEnable(번역 구독 등)이 돌기 전에 정리한다
            var holder = new GameObject("PrestigePerksButtonHolder");
            holder.SetActive(false);
            GameObject clone = null;
            try
            {
                clone = UnityEngine.Object.Instantiate(source, holder.transform, false);
                clone.name = "PrestigePerksButton";

                // 은신처 툴팁·번역 문구가 따라오지 않게 뗀다. 글자는 원래 문구가 있던 TMP 에만 넣는다 (아이콘 글리프 TMP 는 그대로)
                foreach (var localized in clone.GetComponentsInChildren<LocalizedText>(true))
                {
                    UnityEngine.Object.DestroyImmediate(localized);
                }

                foreach (var tooltip in clone.GetComponentsInChildren<HoverTooltipArea>(true))
                {
                    UnityEngine.Object.DestroyImmediate(tooltip);
                }

                foreach (var text in clone.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (!string.IsNullOrWhiteSpace(text.text))
                    {
                        text.text = label;
                        text.gameObject.AddComponent<PerkButtonLabel>();
                    }
                }

                var toggle = clone.GetComponent<AnimatedToggle>();
                if (toggle == null)
                {
                    Log.Warn("taskbar: cloned button has no AnimatedToggle");
                    UnityEngine.Object.Destroy(clone);
                    return null;
                }

                // AnimatedToggle.OnPointerClick 이 group.allowSwitchOff 를 읽으므로 group 을 비우면 클릭마다 NRE 가 난다.
                // 바닐라 탭들과 묶이지 않게 자기만의 그룹을 준다 (다시 누르면 꺼진다)
                var ownGroup = clone.GetComponent<ToggleGroup>();
                if (ownGroup == null)
                {
                    ownGroup = clone.AddComponent<ToggleGroup>();
                }

                ownGroup.allowSwitchOff = true;
                toggle.group = ownGroup;
                toggle.isOn = false;
                toggle.interactable = true;

                clone.transform.SetParent(source.transform.parent, false);
                clone.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
                clone.SetActive(true);

                // Awake 가 붙인 리스너(ToggleSilent)는 우리 것으로 바꾼다
                toggle.onValueChanged.RemoveAllListeners();
                toggle.onValueChanged.AddListener(value =>
                {
                    toggle.ToggleSilent(value);
                    onToggle(value);
                });
                toggle.ToggleSilent(false);
                Log.Info("taskbar: perk button added");
                return toggle;
            }
            catch (Exception)
            {
                if (clone != null)
                {
                    UnityEngine.Object.Destroy(clone);
                }

                throw;
            }
            finally
            {
                UnityEngine.Object.Destroy(holder);
            }
        }

        /// <summary>게임 언어가 바뀌었을 때 버튼 이름을 바꾼다. 번역 컴포넌트(LocalizedText)는 떼어 냈으므로 직접 바꾼다.</summary>
        public static void SetLabel(AnimatedToggle button, string label)
        {
            foreach (var marker in button.GetComponentsInChildren<PerkButtonLabel>(true))
            {
                var text = marker.GetComponent<TMP_Text>();
                if (text != null)
                {
                    text.text = label;
                }
            }
        }

        /// <summary>버튼 윗변의 화면 높이(픽셀). 페이지가 작업 표시줄을 덮지 않게 이만큼 위에서 끝낸다.</summary>
        public static float TopPixels(Component button)
        {
            var rect = button.transform as RectTransform;
            var bar = rect != null && rect.parent is RectTransform parent ? parent : rect;
            // 작업 표시줄 높이(1080 기준 약 36). 재지 못할 때 쓴다. 0 이면 페이지가 작업 표시줄을 덮는다
            var fallback = UnityEngine.Screen.height * 38f / 1080f;
            if (bar == null)
            {
                return fallback;
            }

            Canvas.ForceUpdateCanvases();

            var canvas = bar.GetComponentInParent<Canvas>();
            var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var corners = new Vector3[4];
            bar.GetWorldCorners(corners);
            var top = 0f;
            foreach (var corner in corners)
            {
                top = Mathf.Max(top, RectTransformUtility.WorldToScreenPoint(camera, corner).y);
            }

            // 화면 아래 절반을 넘으면 잘못 잰 것이다 (부모가 화면 전체인 경우)
            return top <= 1 || top > UnityEngine.Screen.height * 0.5f ? fallback : top + 2;
        }

        public static TMP_FontAsset FontOf(Component button) => button.GetComponentInChildren<TextMeshProUGUI>(true)?.font;
    }

    /// <summary>복제한 버튼에서 이름 글자가 든 TMP 표시.</summary>
    internal sealed class PerkButtonLabel : MonoBehaviour
    {
    }
}
