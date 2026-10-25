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

                Describe(clone.transform);
                SwapIcon(clone.transform);

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
                var gap = clone.AddComponent<PerkTaskbarGap>();
                gap.Hideout = source.transform as RectTransform;
                // 메인 메뉴 버튼은 은신처와 다른 부모 밑에 있어 형제에서 찾지 않고 작업 표시줄 목록에서 꺼낸다
                gap.Left = taskBar._toggleButtons.TryGetValue(EMenuType.MainMenu, out var mainMenu) && mainMenu != null ? mainMenu.transform as RectTransform : null;
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

        /// <summary>
        /// 은신처 아이콘 그림만 우리 것(육각 + 별)으로 바꾼다. 색·크기는 그대로 두어 마우스오버·선택 때
        /// 버튼 Animator 가 입히는 색 변화를 바닐라 버튼과 똑같이 받는다.
        /// 아이콘은 이름 글자가 아닌 작은 정사각형 Image 중 가장 왼쪽 것으로 찾는다 (바탕 Image 는 버튼만큼 넓다).
        /// </summary>
        private static void SwapIcon(Transform button)
        {
            var sprite = PerkSprites.TaskbarIcon;
            Image icon = null;
            var iconX = float.MaxValue;
            foreach (var image in button.GetComponentsInChildren<Image>(true))
            {
                var size = image.rectTransform.rect.size;
                if (image.sprite == null || size.x <= 0f || size.x > 48f || Mathf.Abs(size.x - size.y) > size.x * 0.4f)
                {
                    continue;
                }

                var x = image.rectTransform.position.x;
                if (x < iconX)
                {
                    iconX = x;
                    icon = image;
                }
            }

            if (icon == null || sprite == null)
            {
                Log.Warn("taskbar: icon image not found, hideout icon kept");
                return;
            }

            Log.Info($"taskbar: icon {icon.name} ({icon.sprite.name}) replaced");
            // 버튼의 HorizontalLayoutGroup 이 Image 원본 크기(우리 그림 128px)로 자리를 잡지 않게 바닐라 아이콘 크기로 고정한다
            var keep = icon.GetComponent<LayoutElement>() ?? icon.gameObject.AddComponent<LayoutElement>();
            var native = icon.rectTransform.rect.size;
            keep.minWidth = keep.preferredWidth = native.x;
            keep.minHeight = keep.preferredHeight = native.y;
            keep.flexibleWidth = keep.flexibleHeight = 0f;
            icon.sprite = sprite;
            icon.overrideSprite = null;
            icon.preserveAspect = true;
            icon.type = Image.Type.Simple;
        }

        // 버튼 구조를 한 번 남긴다 (게임 업데이트로 아이콘·마우스오버 구조가 바뀌었을 때 고치기 위해)
        private static void Describe(Transform button)
        {
            var line = new System.Text.StringBuilder("taskbar: hideout button structure");
            foreach (var t in button.GetComponentsInChildren<Transform>(true))
            {
                var depth = 0;
                for (var p = t; p != button && p != null; p = p.parent)
                {
                    depth++;
                }

                line.AppendLine().Append(' ', depth * 2 + 2).Append(t.name).Append(" [");
                foreach (var c in t.GetComponents<Component>())
                {
                    if (c is Transform)
                    {
                        continue;
                    }

                    line.Append(c.GetType().Name);
                    if (c is Image image && image.sprite != null)
                    {
                        line.Append('(').Append(image.sprite.name).Append(' ').Append(image.rectTransform.rect.size.ToString("0")).Append(')');
                    }

                    line.Append(' ');
                }

                line.Append(']');
            }

            Log.Info(line.ToString());
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

    /// <summary>
    /// 은신처와 특전 버튼 사이를 바닐라 버튼 사이(메인 메뉴 버튼 오른쪽 끝 → 은신처 버튼 왼쪽 끝)와 같게 맞춘다.
    /// 작업 표시줄이 처음 그려진 뒤 한 번 재고, 모자란 만큼 은신처와 특전 사이에 빈 칸(LayoutElement)을 넣는다.
    /// </summary>
    internal sealed class PerkTaskbarGap : MonoBehaviour
    {
        public RectTransform Hideout, Left;
        private bool done;

        private void LateUpdate()
        {
            if (done || Hideout == null)
            {
                return;
            }

            try
            {
                var parent = transform.parent as RectTransform;
                if (Left == null)
                {
                    done = true;
                    Log.Info("taskbar gap: main menu button not found, not adjusted");
                    return;
                }

                // 아직 그려지지 않았으면 (폭 0) 다음 프레임에 다시 잰다. 재기 전에 레이아웃을 다시 계산해 옛 위치를 재지 않게 한다
                LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
                var vanilla = Gap(Left, Hideout);
                var ours = Gap(Hideout, (RectTransform)transform);
                if (vanilla == null || ours == null)
                {
                    return;
                }

                done = true;
                var scale = parent.lossyScale.x > 0f ? parent.lossyScale.x : 1f;
                // 같은 줄 안의 두 버튼은 겹칠 수 없으므로 음수는 잘못 잰 값이다 (0 으로 본다). 빈 칸은 바닐라 간격을 넘지 않는다
                var extra = (vanilla.Value - Mathf.Max(ours.Value, 0f)) / scale;
                var group = parent.GetComponent<HorizontalLayoutGroup>();
                Log.Info($"taskbar gap: vanilla {vanilla.Value / scale:0.#}, perk {ours.Value / scale:0.#}, layout spacing {(group != null ? group.spacing.ToString("0.#") : "none")}");
                if (extra < 1f)
                {
                    return;
                }

                if (group != null)
                {
                    // 빈 칸도 layout spacing 만큼 한 번 더 띄우므로 그만큼 뺀다
                    var spacer = new GameObject("PrestigePerksGap", typeof(RectTransform), typeof(LayoutElement));
                    spacer.transform.SetParent(parent, false);
                    spacer.transform.SetSiblingIndex(transform.GetSiblingIndex());
                    var width = Mathf.Max(extra - group.spacing, 0f);
                    var element = spacer.GetComponent<LayoutElement>();
                    element.minWidth = element.preferredWidth = width;
                    element.flexibleWidth = 0f;
                }
                else
                {
                    var rect = (RectTransform)transform;
                    rect.anchoredPosition += new Vector2(extra, 0f);
                }
            }
            catch (Exception e)
            {
                done = true;
                Log.Warn($"taskbar gap failed: {e.Message}");
            }
        }

        private static readonly Vector3[] Corners = new Vector3[4];

        // 왼쪽 버튼 사각형의 오른쪽 끝 → 오른쪽 버튼 사각형의 왼쪽 끝 (월드 좌표). 아직 크기가 없으면 null.
        // 선택·마우스오버 때 보이는 버튼 바탕이 이 사각형이라 눈에 보이는 간격과 같다
        private static float? Gap(RectTransform left, RectTransform right)
        {
            if (left.rect.width <= 0f || right.rect.width <= 0f)
            {
                return null;
            }

            left.GetWorldCorners(Corners);
            var leftEnd = Corners[2].x;
            right.GetWorldCorners(Corners);
            return Corners[0].x - leftEnd;
        }
    }

    /// <summary>복제한 버튼에서 이름 글자가 든 TMP 표시.</summary>
    internal sealed class PerkButtonLabel : MonoBehaviour
    {
    }
}
