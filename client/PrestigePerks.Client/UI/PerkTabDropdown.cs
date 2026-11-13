using System;
using System.Collections.Generic;
using PrestigePerks.Patches;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PrestigePerks.UI
{
    /// <summary>
    /// 프레스티지 탭 드롭다운 (Briefing 모드 임무 탭 드롭다운과 같은 방식). 프레스티지 탭을 누르면 그 아래로 프레스티지 탭을 복제한
    /// 두 항목(프레스티지, 특전)이 미끄러져 펼쳐진다.
    /// 탭 줄 밑에 두지만 자기 Canvas(overrideSorting)로 그려 아래 화면 내용에 가리지 않는다. 탭 줄의 직접 자식 중 켜진 Tab 이 없게 해서
    /// 탭 줄을 다시 배치하는 다른 모드(Operator Traits)가 이 메뉴를 탭으로 세지 않는다.
    /// 프레스티지 탭 글자는 지금 보는 쪽("프레스티지 ▼" / "특전 ▼")을 따르고, 특전을 보는 동안 프레스티지 탭을 고른 모양으로 칠한다.
    /// 메뉴가 열려 있거나 특전을 보는 동안에는 게임이 고른 탭에 하듯 프레스티지 탭을 탭 줄 맨 뒤로 옮겨 옆 탭 위에 그린다.
    /// </summary>
    internal sealed class PerkTabDropdown : MonoBehaviour
    {
        private const string MenuName = "PrestigePerksTabMenu";
        private const float OpenSeconds = 0.14f;
        private const float CloseSeconds = 0.09f;

        // 항목 사이와 프레스티지 탭과의 간격
        private const float Gap = 2f;
        private const char ArrowGlyph = '▼';
        private static readonly Color BackingColor = new Color(0.04f, 0.05f, 0.05f, 0.92f);

        private global::Tab prestige;
        private Transform newLevelIcon;
        private RectTransform prestigeRect;
        private RectTransform root, clip, slide;
        private CanvasGroup fade;
        private Canvas canvas;
        private readonly List<TMP_Text> prestigeLabels = new List<TMP_Text>();
        private readonly List<global::Tab> items = new List<global::Tab>();
        private readonly List<List<TMP_Text>> itemLabels = new List<List<TMP_Text>>();
        private Func<bool> perksShown, prestigeShown;

        // 프레스티지 탭 원래 글자. LocalizedText 를 못 읽을 때 쓴다
        private string prestigeFallback;
        private string arrow = "";
        private string perksTitle = "";
        private bool open;

        // 펼침 정도 0..1
        private float t;
        private bool paintedPerks;
        private bool failed;

        // 프레스티지 탭을 올리기 전 자리. 올리지 않았으면 -1
        private int restoreIndex = -1;

        // 올려야 하는 동안 (메뉴가 열려 있거나 특전을 보는 동안) 이미 한 번 올렸는지
        private bool raised;

        // 게임 TabSizeController 는 탭 줄의 모든 탭 폭을 가장 긴 탭 글자 폭(TabElement._sizeDefiners)에 맞춘다. 특전을 보는 동안 "특전 ▼"가
        // "프레스티지 ▼"보다 짧아 탭 줄 전체가 줄어들지 않게, 프레스티지 글자일 때의 폭을 기억하는 보이지 않는 칸을 폭 기준에 하나 더 넣는다
        private RectTransform widthKeeper;
        private List<RectTransform> sizeDefiners;

        public Transform Root => root;

        /// <summary>newLevelIcon 은 프레스티지 탭의 "새 단계" 표시 (없으면 null). 탭 안에 있으면 그 글자는 건드리지 않고 항목에서는 끈다.</summary>
        public static PerkTabDropdown Create(global::Tab prestige, GameObject newLevelIcon, Func<bool> perksShown, Func<bool> prestigeShown, Action pickPrestige,
            Action pickPerks)
        {
            var go = new GameObject(MenuName, typeof(RectTransform));
            try
            {
                go.transform.SetParent(prestige.transform.parent, false);
                var dropdown = go.AddComponent<PerkTabDropdown>();
                dropdown.newLevelIcon = newLevelIcon != null && newLevelIcon.transform.IsChildOf(prestige.transform) ? newLevelIcon.transform : null;
                dropdown.Build(prestige, perksShown, prestigeShown, pickPrestige, pickPerks);
                return dropdown;
            }
            catch
            {
                Destroy(go);
                throw;
            }
        }

        /// <summary>프레스티지 탭 글자 TMP 의 글꼴 (특전 페이지가 같은 글꼴을 쓴다).</summary>
        public TMP_FontAsset Font => prestigeLabels.Count > 0 && prestigeLabels[0] != null ? prestigeLabels[0].font : null;

        private void Build(global::Tab tab, Func<bool> perks, Func<bool> prestigeOpen, Action pickPrestige, Action pickPerks)
        {
            prestige = tab;
            prestigeRect = (RectTransform)tab.transform;
            perksShown = perks;
            prestigeShown = prestigeOpen;
            root = (RectTransform)transform;

            foreach (var text in tab.GetComponentsInChildren<TMP_Text>(true))
            {
                if (!string.IsNullOrWhiteSpace(text.text) && !Under(text.transform, newLevelIcon))
                {
                    prestigeLabels.Add(text);
                }
            }

            prestigeFallback = prestigeLabels.Count > 0 ? prestigeLabels[0].text : "";

            canvas = gameObject.AddComponent<Canvas>();
            gameObject.AddComponent<GraphicRaycaster>();
            ApplySorting();

            clip = NewRect("Clip", root);
            clip.anchorMin = new Vector2(0f, 0f);
            clip.anchorMax = new Vector2(1f, 0f);
            clip.pivot = new Vector2(0.5f, 1f);
            clip.gameObject.AddComponent<RectMask2D>();
            fade = clip.gameObject.AddComponent<CanvasGroup>();

            slide = NewRect("Slide", clip);
            slide.anchorMin = Vector2.zero;
            slide.anchorMax = Vector2.one;
            slide.offsetMin = Vector2.zero;
            slide.offsetMax = Vector2.zero;
            // 항목 사이로 아래 내용이 비치지 않게, 메뉴 위 클릭이 아래로 새지 않게
            var backing = slide.gameObject.AddComponent<Image>();
            backing.color = BackingColor;

            AddItem("PrestigePerksMenuPrestige", () => Pick(pickPrestige));
            AddItem("PrestigePerksMenuPerks", () => Pick(pickPerks));

            PerkTabClickPatch.Register(prestige, Toggle);
            // 항목과 숨긴 특전 탭에 따라가지 않게 복제를 끝낸 뒤에 넣는다 (숨긴 탭은 PerkTabHost 가 나중에 복제하지만 꺼져 있어 폭에 쓰이지 않는다)
            AddWidthKeeper();
            Layout();
            CloseNow();
        }

        private void AddWidthKeeper()
        {
            var element = prestige.GetComponent<TabElement>();
            if (element == null || element._sizeDefiners == null)
            {
                Log.Warn("prestige tab has no TabElement size definers; tab widths may change while perks are shown");
                return;
            }

            widthKeeper = NewRect("PrestigePerksWidthKeeper", prestige.transform);
            widthKeeper.sizeDelta = Vector2.zero;
            sizeDefiners = element._sizeDefiners;
            sizeDefiners.Add(widthKeeper);
        }

        // 프레스티지 글자를 보이는 동안에만 다른 폭 기준(글자)의 폭을 따라간다. 같은 프레임에 바꾼 글자는 프레임 끝 레이아웃에서야 폭이 바뀌므로
        // 특전으로 넘어가는 프레임에는 아직 프레스티지 글자 폭이 남아 있다
        private void KeepWidth(bool perks)
        {
            if (widthKeeper == null || perks)
            {
                return;
            }

            var width = 0f;
            foreach (var definer in sizeDefiners)
            {
                if (definer != null && definer != widthKeeper && definer.gameObject.activeInHierarchy)
                {
                    width = Mathf.Max(width, definer.rect.width);
                }
            }

            if (!Mathf.Approximately(widthKeeper.sizeDelta.x, width))
            {
                widthKeeper.sizeDelta = new Vector2(width, 0f);
            }
        }

        private void AddItem(string name, Action onClick)
        {
            var labels = new List<TMP_Text>();
            var tab = PerkTabClone.Make(prestige, slide, name, labels);
            var rect = (RectTransform)tab.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            // 프레스티지 탭의 "새 단계" 표시가 복제본에 따라왔으면 끄고 그 글자는 이름 칸에서 뺀다
            var copiedIcon = CopyOf(newLevelIcon, tab.transform);
            if (copiedIcon != null)
            {
                copiedIcon.gameObject.SetActive(false);
                labels.RemoveAll(label => Under(label.transform, copiedIcon));
            }

            tab.SetInteractable(true);
            tab.UpdateVisual(false);
            tab.gameObject.SetActive(true);
            PerkTabClickPatch.Register(tab, onClick);
            items.Add(tab);
            itemLabels.Add(labels);
        }

        private static bool Under(Transform node, Transform parent) => parent != null && node.IsChildOf(parent);

        // 원본 탭 안의 node 를 복제본에서 같은 경로로 찾는다
        private Transform CopyOf(Transform node, Transform clone)
        {
            if (node == null)
            {
                return null;
            }

            var path = new List<int>();
            for (var current = node; current != prestige.transform; current = current.parent)
            {
                path.Insert(0, current.GetSiblingIndex());
            }

            var copy = clone;
            foreach (var index in path)
            {
                if (index >= copy.childCount)
                {
                    return null;
                }

                copy = copy.GetChild(index);
            }

            return copy;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        // overrideSorting 은 꺼진 동안 정하면 풀릴 수 있어서 켜질 때마다 다시 정한다
        private void ApplySorting()
        {
            if (canvas == null || transform.parent == null)
            {
                return;
            }

            var parentCanvas = transform.parent.GetComponentInParent<Canvas>();
            canvas.overrideSorting = true;
            if (parentCanvas == null)
            {
                return;
            }

            canvas.sortingLayerID = parentCanvas.sortingLayerID;
            canvas.sortingOrder = parentCanvas.sortingOrder + 1;
            canvas.additionalShaderChannels = parentCanvas.rootCanvas.additionalShaderChannels;
        }

        /// <summary>게임 언어가 바뀌었을 수 있어 캐릭터 화면을 열 때마다 부른다.</summary>
        public void Refresh(string perksText)
        {
            perksTitle = perksText;
            arrow = ArrowText();
            CloseNow();
            SyncPrestige();
        }

        private string ArrowText()
        {
            var label = prestigeLabels.Count > 0 ? prestigeLabels[0] : null;
            if (label == null || label.font == null || !label.font.HasCharacter(ArrowGlyph, true, true))
            {
                return "";
            }

            return label.richText ? $" <size=60%>{ArrowGlyph}</size>" : $" {ArrowGlyph}";
        }

        private string PrestigeTitle()
        {
            var localized = prestige != null && prestige.LocalizedText != null ? prestige.LocalizedText.FormattedText : null;
            return string.IsNullOrEmpty(localized) ? prestigeFallback : localized;
        }

        private void Toggle()
        {
            if (open)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        private void Open()
        {
            var perks = perksShown();
            SetLabels(itemLabels[0], PrestigeTitle());
            SetLabels(itemLabels[1], perksTitle);
            items[0].UpdateVisual(!perks && prestigeShown());
            items[1].UpdateVisual(perks);
            Layout();
            open = true;
            clip.gameObject.SetActive(true);
            ApplyAnimation();
        }

        public void Close()
        {
            if (!open)
            {
                return;
            }

            open = false;
            ResetHover();
            ApplyAnimation();
        }

        private void CloseNow()
        {
            open = false;
            t = 0f;
            ResetHover();
            ApplyAnimation();
            if (clip != null)
            {
                clip.gameObject.SetActive(false);
            }
        }

        // 누르면 메뉴가 꺼지면서 마우스 올림 모양이 남으므로 지운다
        private void ResetHover()
        {
            foreach (var item in items)
            {
                if (item != null)
                {
                    item.Hover(false);
                }
            }
        }

        private void Pick(Action pick)
        {
            Close();
            pick();
        }

        private void ApplyAnimation()
        {
            if (slide == null || fade == null)
            {
                return;
            }

            var eased = 1f - Mathf.Pow(1f - t, 3f);
            slide.anchoredPosition = new Vector2(0f, (1f - eased) * clip.rect.height);
            fade.alpha = eased;
            fade.blocksRaycasts = open;
            fade.interactable = open;
        }

        private void Update()
        {
            if (!open || prestige == null)
            {
                return;
            }

            try
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    Close();
                    return;
                }

                if (!Input.GetMouseButtonDown(0) && !Input.GetMouseButtonDown(1))
                {
                    return;
                }

                // 프레스티지 탭 위 클릭은 PerkTabClickPatch 가 열고 닫으므로 여기서는 건드리지 않는다
                var cam = EventCamera();
                if (RectTransformUtility.RectangleContainsScreenPoint(clip, Input.mousePosition, cam)
                    || RectTransformUtility.RectangleContainsScreenPoint(prestigeRect, Input.mousePosition, cam))
                {
                    return;
                }

                Close();
            }
            catch (Exception e)
            {
                Log.Warn($"tab menu input failed: {e.Message}");
            }
        }

        private Camera EventCamera()
        {
            var rootCanvas = canvas != null ? canvas.rootCanvas : null;
            return rootCanvas == null || rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
        }

        private void LateUpdate()
        {
            if (prestige == null || failed)
            {
                return;
            }

            try
            {
                Layout();
                SyncPrestige();
                var target = open ? 1f : 0f;
                if (!Mathf.Approximately(t, target))
                {
                    t = Mathf.MoveTowards(t, target, Time.unscaledDeltaTime / (open ? OpenSeconds : CloseSeconds));
                    ApplyAnimation();
                    if (!open && t <= 0f)
                    {
                        clip.gameObject.SetActive(false);
                    }
                }

                RaisePrestige(open || t > 0f || perksShown());
            }
            catch (Exception e)
            {
                // 매 프레임 로그가 쏟아지지 않게 멈춘다
                failed = true;
                Log.Error($"prestige tab menu failed: {e}");
            }
        }

        // 프레스티지 탭 자리를 따라간다. 다른 모드나 게임이 탭 줄을 다시 배치할 수 있어서 매 프레임 맞춘다
        private void Layout()
        {
            if (prestigeRect == null || root == null)
            {
                return;
            }

            root.anchorMin = prestigeRect.anchorMin;
            root.anchorMax = prestigeRect.anchorMax;
            root.pivot = prestigeRect.pivot;
            root.sizeDelta = prestigeRect.sizeDelta;
            root.anchoredPosition = prestigeRect.anchoredPosition;
            root.localScale = prestigeRect.localScale;

            var h = prestigeRect.rect.height;
            if (h <= 0f)
            {
                return;
            }

            var height = items.Count * h + Mathf.Max(0, items.Count - 1) * Gap;
            if (!Mathf.Approximately(clip.sizeDelta.y, height))
            {
                clip.anchoredPosition = new Vector2(0f, -Gap);
                clip.sizeDelta = new Vector2(0f, height);
            }

            for (var i = 0; i < items.Count; i++)
            {
                var rect = (RectTransform)items[i].transform;
                rect.sizeDelta = new Vector2(0f, h);
                rect.anchoredPosition = new Vector2(0f, -i * (h + Gap));
            }
        }

        // 게임 TabGroup.SelectTab 은 고른 탭을 SetAsLastSibling 으로 맨 뒤에 둬서 옆 탭 위에 그린다. 프레스티지 탭 클릭은 메뉴가 받아
        // 그 일이 일어나지 않으므로 같은 방식으로 올린다. 내릴 때는 그사이 게임이 순서를 바꾸지 않았을 때만 원래 자리로 돌린다.
        // 올려야 하는 상태가 시작될 때 한 번만 올린다. 매 프레임 올리면 같은 방식의 다른 모드(Briefing 임무 탭 드롭다운)가 자기 탭을 올릴 때
        // 서로 맨 뒤 자리를 빼앗아 탭 가장자리가 깜빡인다
        private void RaisePrestige(bool raise)
        {
            var tabTransform = prestige.transform;
            var parent = tabTransform.parent;
            if (parent == null)
            {
                return;
            }

            var last = tabTransform.GetSiblingIndex() == parent.childCount - 1;
            if (raise)
            {
                if (raised)
                {
                    return;
                }

                raised = true;
                if (!last)
                {
                    restoreIndex = tabTransform.GetSiblingIndex();
                    tabTransform.SetAsLastSibling();
                }

                return;
            }

            if (!raised)
            {
                return;
            }

            raised = false;
            if (restoreIndex < 0)
            {
                return;
            }

            if (last && !prestigeShown())
            {
                tabTransform.SetSiblingIndex(Mathf.Min(restoreIndex, parent.childCount - 1));
            }

            restoreIndex = -1;
        }

        // 프레스티지 탭 글자와, 특전을 보는 동안의 고른 모양. LocalizedText 가 언어 갱신 때 글자를 되돌려도 다음 프레임에 다시 맞춘다.
        // 고른 모양은 게임 상태(_selected)를 건드리지 않고 보이는 것만 바꾼다. 특전이 닫히면 UpdateVersion 이 게임 상태대로 되돌린다
        private void SyncPrestige()
        {
            var perks = perksShown();
            KeepWidth(perks);
            SetLabels(prestigeLabels, (perks ? perksTitle : PrestigeTitle()) + arrow);
            if (perks)
            {
                if (prestige._selectedVersion != null && !prestige._selectedVersion.activeSelf)
                {
                    prestige._selectedVersion.SetActive(true);
                }

                if (prestige._normalVersion != null && prestige._normalVersion.activeSelf)
                {
                    prestige._normalVersion.SetActive(false);
                }
            }
            else if (paintedPerks)
            {
                prestige.UpdateVersion();
            }

            paintedPerks = perks;
        }

        private static void SetLabels(List<TMP_Text> labels, string text)
        {
            foreach (var label in labels)
            {
                if (label != null && label.text != text)
                {
                    label.text = text;
                }
            }
        }

        private void OnEnable() => ApplySorting();

        private void OnDisable()
        {
            if (prestige == null)
            {
                return;
            }

            try
            {
                CloseNow();
            }
            catch (Exception e)
            {
                Log.Warn($"tab menu close failed: {e.Message}");
            }
        }

        // 메뉴가 사라지면 프레스티지 탭을 원래 글자와 게임 동작으로 되돌린다
        private void OnDestroy()
        {
            if (sizeDefiners != null && widthKeeper != null)
            {
                sizeDefiners.Remove(widthKeeper);
                Destroy(widthKeeper.gameObject);
            }

            PerkTabClickPatch.Unregister(prestige);
            foreach (var item in items)
            {
                PerkTabClickPatch.Unregister(item);
            }

            if (prestige == null)
            {
                return;
            }

            try
            {
                RaisePrestige(false);
                SetLabels(prestigeLabels, PrestigeTitle());
                prestige.UpdateVersion();
            }
            catch (Exception e)
            {
                Log.Warn($"tab menu restore failed: {e.Message}");
            }
        }
    }
}
