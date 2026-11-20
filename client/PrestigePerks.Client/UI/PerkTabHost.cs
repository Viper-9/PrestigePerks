using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using EFT.UI;
using HarmonyLib;
using UnityEngine;

namespace PrestigePerks.UI
{
    /// <summary>
    /// InventoryScreen(캐릭터 화면)에 붙는 컴포넌트 (Briefing 모드 BriefingTabHost 와 같은 방식). 화면이 열릴 때마다 특전 탭이 없으면 만들고
    /// 게임 탭과의 선택을 이어 준다. 특전은 탭 줄에 따로 보이지 않고 프레스티지 탭 드롭다운(<see cref="PerkTabDropdown"/>)에서 고른다.
    /// 선택에 쓰는 특전 탭은 숨겨 두고 그대로 쓴다. 특전 탭은 게임 TabGroup 배열 밖이라 그룹이 모른다. 그래서 특전을 고르면
    /// TabGroup.TryHide() 로 게임 탭을 내린 뒤 Select 하고, 게임 탭을 누르면 그룹이 그 탭을 고르게 두고 OnSelectionChanged 를 들어 특전 탭만 내린다.
    /// 클릭 없이 게임 탭이 열리는 경우(다른 모드가 그룹을 바로 부를 때)는 <see cref="Patches.TabSelectPatch"/> 로 Tab.Select 를 들어 내린다.
    /// 다른 모드가 같은 방식으로 탭 줄 밑에 숨긴 탭(Briefing 모드 브리핑 탭)도 그룹 밖이라 서로 모르므로, 그 탭도 고르기를 듣고
    /// 특전을 고를 때 골라져 있으면 내린다 (안 그러면 두 패널이 겹쳐 뒤에 있는 쪽이 가려진다).
    /// </summary>
    internal sealed class PerkTabHost : MonoBehaviour
    {
        private const string TabName = "PrestigePerksTab";
        private const string PanelName = "PrestigePerksPanel";

        // 패널은 탭 줄 바로 아래부터 작업 표시줄 위까지 (Briefing 패널 아래 여백과 같은 40)
        private const float TopGap = 4f, BottomMargin = 40f;
        private static readonly FieldInfo TabsField = AccessTools.Field(typeof(InventoryScreen), "_tabs");
        private static readonly FieldInfo SelectedField = AccessTools.Field(typeof(global::Tab), "_selected");

        private InventoryScreen screen;
        private global::Tab tab;
        private GameObject panel;
        // 고르기를 듣는 다른 탭: 게임 탭 + 다른 모드가 탭 줄 밑에 숨긴 탭
        private readonly List<global::Tab> otherTabs = new List<global::Tab>();
        private readonly HashSet<global::Tab> foreignTabs = new HashSet<global::Tab>();
        private bool busy;
        private PerkTabDropdown dropdown;

        private bool PerksShown => panel != null && panel.activeSelf;

        private bool PrestigeShown => screen != null && screen._prestigeScreen != null && screen._prestigeScreen.gameObject.activeSelf;

        public static void AttachOrRefresh(InventoryScreen screen)
        {
            try
            {
                var host = screen.GetComponent<PerkTabHost>();
                if (host == null)
                {
                    host = screen.gameObject.AddComponent<PerkTabHost>();
                }

                host.screen = screen;
                host.StopAllCoroutines();
                host.StartCoroutine(host.AttachWhenReady());
            }
            catch (Exception e)
            {
                Log.Error($"perk tab attach failed: {e}");
            }
        }

        // ShowWithTab 코루틴이 탭 컨트롤러를 Init 할 때까지 최대 120프레임 기다린다
        private IEnumerator AttachWhenReady()
        {
            var created = false;
            for (var frame = 0; frame < 120 && !created; frame++)
            {
                yield return null;
                if (frame < 2 || TabsField.GetValue(screen) == null || screen._tabDictionary == null)
                {
                    continue;
                }

                try
                {
                    created = EnsureCreated();
                }
                catch (Exception e)
                {
                    Log.Error($"perk tab not created: {e}");
                    DiscardPartial();
                    yield break;
                }
            }

            if (!created)
            {
                Log.Warn("tab group was not ready after 120 frames; perk tab skipped");
            }
        }

        private bool EnsureCreated()
        {
            if (!screen._tabDictionary.TryGetValue(EInventoryTab.Prestige, out var prestige) || prestige == null)
            {
                return false;
            }

            if (tab == null)
            {
                dropdown = PerkTabDropdown.Create(prestige, screen._prestigeNewLevelIcon, () => PerksShown, () => PrestigeShown, PickPrestige, PickPerks);
                // 탭 줄에 놓지 않고 메뉴 밑에 꺼 둔다. 탭 줄을 배치하는 다른 모드가 세지 않는다
                tab = PerkTabClone.Make(prestige, dropdown.Root, TabName, null);
                tab.OnSelectionChanged += OnOurSelectionChanged;
                CreatePanel();
                Log.Info("perk tab created");
            }

            tab.Init(new PerkTabController(this));
            tab.SetInteractable(true);
            tab.UpdateVisual(false);
            HideNow();
            // 게임 언어가 바뀌었을 수 있다. 실패하면 글자만 전의 값으로 남는다
            try
            {
                dropdown.Refresh(PerkController.Instance.Locale.Get("ui.title"));
            }
            catch (Exception e)
            {
                Log.Warn($"tab menu refresh failed: {e.Message}");
            }

            SubscribeOtherTabs();
            return true;
        }

        // 프레스티지 화면이 이미 열려 있으면 그대로 둔다. 아니면 사용자가 프레스티지 탭을 누른 것처럼 게임 그룹에 맡긴다
        private void PickPrestige()
        {
            if (!PerksShown && PrestigeShown)
            {
                return;
            }

            if (screen._tabDictionary.TryGetValue(EInventoryTab.Prestige, out var prestige) && prestige != null)
            {
                prestige.HandlePointerClick(false);
            }
        }

        private void PickPerks()
        {
            if (tab != null && panel != null && !panel.activeSelf)
            {
                tab.HandlePointerClick(false);
            }
        }

        // EnsureCreated 가 중간에 실패하면 반쯤 만든 것을 지우고 다음 Show 에서 다시 만든다
        private void DiscardPartial()
        {
            try
            {
                if (tab != null)
                {
                    tab.OnSelectionChanged -= OnOurSelectionChanged;
                    Destroy(tab.gameObject);
                }

                if (dropdown != null)
                {
                    Destroy(dropdown.gameObject);
                }

                if (panel != null)
                {
                    Destroy(panel);
                }
            }
            catch (Exception e)
            {
                Log.Warn($"discard partial perk tab failed: {e.Message}");
            }

            tab = null;
            dropdown = null;
            panel = null;
        }

        // 프레스티지 화면과 같은 부모에 둔다. 내용(특전 페이지)은 열 때마다 PerkController 가 다시 만든다
        private void CreatePanel()
        {
            var prestigeScreen = (RectTransform)screen._prestigeScreen.transform;
            panel = new GameObject(PanelName, typeof(RectTransform));
            panel.layer = prestigeScreen.gameObject.layer;
            panel.transform.SetParent(prestigeScreen.parent, false);
            panel.transform.SetAsLastSibling();
            panel.SetActive(false);
        }

        /// <summary>특전 탭 Select 때 (<see cref="PerkTabController"/>). 자리를 다시 재고 패널을 켠 뒤 페이지를 만든다.</summary>
        public void ShowPanel()
        {
            // 다른 모드 탭이 이 화면에서 우리보다 늦게 만들어졌을 수 있다
            SubscribeOtherTabs();
            ApplyLayout();
            panel.SetActive(true);
            Canvas.ForceUpdateCanvases();
            PerkController.Instance.OpenPage((RectTransform)panel.transform);
        }

        /// <summary>특전 탭 Deselect 때. 페이지를 닫고 패널을 끈다.</summary>
        public void HidePanel()
        {
            PerkController.Instance.ClosePage();
            if (panel != null)
            {
                panel.SetActive(false);
            }
        }

        // 가로는 부모 전체, 세로는 탭 줄 아래부터 작업 표시줄 위까지. 해상도·다른 모드 배치가 바뀌었을 수 있어 열 때마다 다시 잰다
        private void ApplyLayout()
        {
            var rect = (RectTransform)panel.transform;
            var parent = rect.parent as RectTransform;
            float? tabBottom = null;
            try
            {
                if (parent != null && screen._tabDictionary.TryGetValue(EInventoryTab.Prestige, out var prestige) && prestige != null
                    && prestige.transform.parent is RectTransform row)
                {
                    var corners = new Vector3[4];
                    row.GetWorldCorners(corners);
                    tabBottom = corners.Min(c => parent.InverseTransformPoint(c).y);
                }
            }
            catch (Exception e)
            {
                Log.Warn($"tab row bottom edge failed: {e.Message}");
            }

            if (tabBottom == null || parent == null)
            {
                Log.Warn("tab row rect not found; perk panel falls back to the prestige screen rect");
                var prestigeScreen = (RectTransform)screen._prestigeScreen.transform;
                rect.anchorMin = prestigeScreen.anchorMin;
                rect.anchorMax = prestigeScreen.anchorMax;
                rect.pivot = prestigeScreen.pivot;
                rect.anchoredPosition = prestigeScreen.anchoredPosition;
                rect.sizeDelta = prestigeScreen.sizeDelta;
                return;
            }

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(0f, BottomMargin);
            rect.offsetMax = new Vector2(0f, -(parent.rect.yMax - tabBottom.Value + TopGap));
        }

        private void SubscribeOtherTabs()
        {
            UnsubscribeOtherTabs();
            var native = screen._tabDictionary.Values.Where(other => other != null).Distinct().ToList();
            otherTabs.AddRange(native);
            // 탭 줄 밑의 그룹 밖 탭 (우리 드롭다운 밑은 뺀다). 다른 모드 드롭다운 항목도 섞이지만 클릭을 그 모드가 받아 골라지지 않는다
            if (screen._tabDictionary.TryGetValue(EInventoryTab.Prestige, out var prestige) && prestige != null && prestige.transform.parent != null)
            {
                foreach (var other in prestige.transform.parent.GetComponentsInChildren<global::Tab>(true))
                {
                    if (other == null || native.Contains(other) || (dropdown != null && other.transform.IsChildOf(dropdown.transform)))
                    {
                        continue;
                    }

                    otherTabs.Add(other);
                    foreignTabs.Add(other);
                }
            }

            foreach (var other in otherTabs)
            {
                other.OnSelectionChanged += OnOtherSelectionChanged;
            }

            // 클릭 없이 열리는 게임 탭도 듣는다. 그룹 밖 탭(브리핑)은 그 모드가 클릭으로 고르니 OnSelectionChanged 로 충분하다
            foreach (var other in native)
            {
                Patches.TabSelectPatch.Register(other, OnNativeShown);
            }
        }

        private void UnsubscribeOtherTabs()
        {
            foreach (var other in otherTabs)
            {
                Patches.TabSelectPatch.Unregister(other);
                if (other != null)
                {
                    other.OnSelectionChanged -= OnOtherSelectionChanged;
                }
            }

            otherTabs.Clear();
            foreignTabs.Clear();
        }

        private void OnOurSelectionChanged(global::Tab sender, bool shouldSelect)
        {
            if (!shouldSelect || busy)
            {
                return;
            }

            _ = SelectOursAsync();
        }

        private async Task SelectOursAsync()
        {
            busy = true;
            try
            {
                var group = (TabGroup)TabsField.GetValue(screen);
                if (group != null && !await group.TryHide())
                {
                    return;
                }

                // 다른 모드의 그룹 밖 탭(브리핑)이 골라져 있으면 그 패널을 내린다
                SubscribeOtherTabs();
                foreach (var other in foreignTabs.ToList())
                {
                    if (other != null && SelectedField?.GetValue(other) is bool selected && selected && !await other.Deselect())
                    {
                        Log.Warn($"perk tab: {other.name} refused to hide");
                        return;
                    }
                }

                // 특전은 프레스티지 탭 자리의 화면이라 그룹에도 프레스티지 탭을 고른 것으로 둔다. 안 그러면 _selectedTab 이 드롭다운을 열기 전 탭(예: 지도)으로
                // 남아, 그 값을 기준으로 탭을 넘기는 모드(탭 전환 단축키 등)가 엉뚱한 탭으로 가고 화면을 닫을 때 마지막 탭도 그 탭이 된다.
                // 다음 탭 전환 때 그룹이 프레스티지 탭을 Deselect 하면 PrestigeScreen.Close 가 한 번 더 불린다. UIElement.Close(UI.Dispose + 끄기)라
                // 한 번도 열리지 않았거나 이미 닫힌 화면이어도 괜찮다 (Briefing 모드의 임무 화면처럼 Show 에서 채우는 값을 쓰지 않는다).
                if (group != null && screen._tabDictionary.TryGetValue(EInventoryTab.Prestige, out var prestige) && prestige != null)
                {
                    group._selectedTab = prestige;
                }

                tab.Select();
            }
            catch (Exception e)
            {
                Log.Error($"select perk tab failed: {e}");
                HideNow();
            }
            finally
            {
                busy = false;
            }
        }

        private void OnOtherSelectionChanged(global::Tab sender, bool shouldSelect)
        {
            if (shouldSelect && dropdown != null)
            {
                dropdown.Close();
            }

            if (!shouldSelect || !PerksShown)
            {
                return;
            }

            _ = DeselectOursAsync();
        }

        // 클릭 이벤트 없이 게임 탭이 열린 경우(다른 모드의 탭 전환 단축키 등). 클릭이면 OnOtherSelectionChanged 가 먼저 내려서 여기서는 할 일이 없다
        private void OnNativeShown()
        {
            if (dropdown != null)
            {
                dropdown.Close();
            }

            if (!PerksShown)
            {
                return;
            }

            _ = DeselectOursAsync();
        }

        private async Task DeselectOursAsync()
        {
            try
            {
                await tab.Deselect();
            }
            catch (Exception e)
            {
                Log.Warn($"deselect perk tab failed: {e.Message}");
                HideNow();
            }
        }

        private void HideNow()
        {
            if (PerksShown)
            {
                HidePanel();
            }

            if (tab != null)
            {
                tab.UpdateVisual(false);
            }
        }

        // 캐릭터 화면이 닫히면 페이지도 닫고 구독을 푼다. 다음 Show 에서 다시 붙는다
        private void OnDisable()
        {
            StopAllCoroutines();
            HideNow();
            UnsubscribeOtherTabs();
        }

        private void OnDestroy()
        {
            if (tab != null)
            {
                tab.OnSelectionChanged -= OnOurSelectionChanged;
            }
        }
    }

    /// <summary>게임 Tab 이 Select/Deselect 때 부르는 컨트롤러. 특전 패널을 켜고 끄기만 한다.</summary>
    internal sealed class PerkTabController : ITabController
    {
        private readonly PerkTabHost host;

        public PerkTabController(PerkTabHost host) => this.host = host;

        public void Show()
        {
            try
            {
                host.ShowPanel();
            }
            catch (Exception e)
            {
                Log.Error($"perk panel show failed: {e}");
            }
        }

        public Task<bool> TryHide()
        {
            try
            {
                host.HidePanel();
            }
            catch (Exception e)
            {
                Log.Error($"perk panel hide failed: {e}");
            }

            return Task.FromResult(true);
        }
    }
}
