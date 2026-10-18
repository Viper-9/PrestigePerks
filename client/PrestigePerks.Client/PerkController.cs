using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using EFT.Communications;
using EFT.UI;
using EFT.UI.Screens;
using PrestigePerks.Game;
using PrestigePerks.Model;
using PrestigePerks.UI;
using UnityEngine;

namespace PrestigePerks
{
    /// <summary>
    /// 특전 페이지 흐름: 작업 표시줄 버튼 → 페이지, 잠금·리롤 → 서버 → 다시 그리기, 메인 메뉴 진입 때 Bonus 반영과 새 특전 알림.
    /// 규칙은 서버가 정하고 여기서는 응답을 그대로 보여 준다 (dev-docs 2_특전.md 2절, 6절).
    /// </summary>
    internal sealed class PerkController : MonoBehaviour
    {
        private const float FirstStopDelay = 0.8f, StopInterval = 0.8f;
        private readonly System.Random random = new System.Random();

        private static PerkController instance;

        /// <summary>파괴됐으면 (Unity null) 다시 만든다. 로딩 중 따로 만든 오브젝트가 정리되어 패치에서 NRE 가 난 적이 있다.</summary>
        public static PerkController Instance => instance != null ? instance : Create();

        private PerkLocale locale;
        private PerkStateDto state;
        private PerkPage page;
        private AnimatedToggle taskbarButton;
        private float bottomInset;
        private bool busy;
        private static bool screenHooked;

        // BepInEx 가 살려 두는 플러그인 오브젝트에 붙인다
        public static PerkController Create()
        {
            if (instance != null)
            {
                return instance;
            }

            if (Plugin.Instance != null)
            {
                var host = Plugin.Instance.gameObject;
                instance = host.GetComponent<PerkController>();
                if (instance == null)
                {
                    instance = host.AddComponent<PerkController>();
                }
            }
            else
            {
                var go = new GameObject("PrestigePerksController");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<PerkController>();
            }

            return instance;
        }

        private PerkLocale Locale => locale ?? (locale = PerkGame.LoadLocale());

        /// <summary>MenuTaskBar.Awake 뒤. 다음 프레임에 버튼을 붙인다 (다른 모드가 같은 줄에 버튼을 넣는 것과 순서를 피한다).</summary>
        public void OnTaskBarAwake(MenuTaskBar taskBar) => StartCoroutine(InjectNextFrame(taskBar));

        private IEnumerator InjectNextFrame(MenuTaskBar taskBar)
        {
            yield return null;
            try
            {
                if (taskBar == null)
                {
                    yield break;
                }

                locale = PerkGame.LoadLocale();
                taskbarButton = PerkTaskbarButton.Inject(taskBar, Locale.Get("taskbar"), OnTaskbarToggle);
                if (taskbarButton != null)
                {
                    UiKit.Font = PerkTaskbarButton.FontOf(taskbarButton) ?? UiKit.Font;
                }

                if (!screenHooked)
                {
                    // 다른 화면(은신처, 상인 등)으로 가면 페이지를 닫는다
                    EftScreenManager.Instance.OnScreenChanged += _ => { if (instance != null) instance.ClosePage(); };
                    // 게임 언어를 바꾸면 버튼 이름과 열린 페이지 문구를 다시 고른다
                    EFT.LocalizationManager.Instance.AddLocaleUpdateListener(() => { if (instance != null) instance.OnLocaleChanged(); });
                    screenHooked = true;
                }
            }
            catch (Exception e)
            {
                Log.Error($"taskbar inject failed: {e}");
            }
        }

        private void OnLocaleChanged()
        {
            try
            {
                locale = PerkGame.LoadLocale();
                if (taskbarButton != null)
                {
                    PerkTaskbarButton.SetLabel(taskbarButton, Locale.Get("taskbar"));
                    UiKit.Font = PerkTaskbarButton.FontOf(taskbarButton) ?? UiKit.Font;
                }

                if (page != null)
                {
                    OpenPage();
                }
            }
            catch (Exception e)
            {
                Log.Warn($"locale change failed: {e.Message}");
            }
        }

        /// <summary>메인 메뉴에 들어올 때마다 (레이드 뒤 포함): 상태를 받아 Bonus 를 반영하고 새 특전을 알린다.</summary>
        public void OnMainMenuShown() => _ = RefreshFromMainMenu();

        private async Task RefreshFromMainMenu()
        {
            try
            {
                var fresh = await PerkApi.GetState();
                if (fresh == null)
                {
                    Log.Warn("main menu: no perk state from server");
                    return;
                }

                Accept(fresh, "main menu");
                RenderPage(null);
            }
            catch (Exception e)
            {
                Log.Warn($"perk state on main menu failed: {e.Message}");
            }
        }

        /// <summary>
        /// 서버 state 응답을 받아들인다: Bonus 반영, 새 특전 알림.
        /// 서버는 state 를 한 번 보내면 새 특전을 "봤음"으로 지우므로, 메인 메뉴든 페이지든 처음 받은 쪽에서 알린다.
        /// </summary>
        private void Accept(PerkStateDto fresh, string source)
        {
            state = fresh;
            PerkBonusSync.Apply(state);
            if (state.NewPerks.Count == 0)
            {
                return;
            }

            var names = string.Join(", ", state.NewPerks.Select(Locale.PerkName));
            Log.Info($"new perks ({source}): {string.Join(", ", state.NewPerks)}");
            try
            {
                NotificationManager.DisplayMessageNotification(Locale.Format("ui.newPerk", names), ENotificationDurationType.Long);
                PerkGame.Sound(EUISoundType.QuestCompleted);
            }
            catch (Exception e)
            {
                Log.Warn($"new perk notification failed: {e}");
            }
        }

        private void OnTaskbarToggle(bool on)
        {
            PerkGame.Sound(EUISoundType.ButtonBottomBarClick);
            if (on)
            {
                OpenPage();
            }
            else
            {
                ClosePage();
            }
        }

        private void OpenPage()
        {
            try
            {
                if (page != null)
                {
                    Destroy(page.gameObject);
                }

                locale = PerkGame.LoadLocale();
                bottomInset = taskbarButton != null ? PerkTaskbarButton.TopPixels(taskbarButton) : 0;
                page = PerkPage.Create(Locale, bottomInset);
                page.LockClicked += slot => _ = ToggleLock(slot);
                page.RollClicked += () => _ = Roll();
                page.ShakeClicked += (slot, next) => _ = Shake(slot, next);
                page.CloseClicked += ClosePage;
                RenderPage(null);
                _ = Reload();
            }
            catch (Exception e)
            {
                Log.Error($"open perk page failed: {e}");
                ClosePage();
            }
        }

        private void ClosePage()
        {
            if (page != null)
            {
                Destroy(page.gameObject);
                page = null;
            }

            busy = false;
            if (taskbarButton != null && taskbarButton.isOn)
            {
                taskbarButton.ToggleSilent(false);
            }
        }

        private async Task Reload()
        {
            try
            {
                var fresh = await PerkApi.GetState();
                if (fresh == null)
                {
                    RenderPage(Locale.Get("ui.network"));
                    return;
                }

                Accept(fresh, "page");
                RenderPage(null);
            }
            catch (Exception e)
            {
                Log.Warn($"perk state failed: {e.Message}");
                RenderPage(Locale.Get("ui.network"));
            }
        }

        private void RenderPage(string message)
        {
            if (page != null)
            {
                page.Render(state, PerkGame.CountCoins(), busy, message);
            }
        }

        private async Task ToggleLock(int slot)
        {
            if (busy || state == null || slot >= state.Slots.Count)
            {
                return;
            }

            busy = true;
            PerkGame.Sound(EUISoundType.MenuCheckBox);
            try
            {
                var fresh = await PerkApi.SetLock(slot, !state.Slots[slot].Locked);
                if (fresh != null)
                {
                    state = fresh;
                }

                busy = false;
                RenderPage(fresh?.Error);
            }
            catch (Exception e)
            {
                Log.Warn($"lock failed: {e.Message}");
                busy = false;
                RenderPage(Locale.Get("ui.network"));
            }
        }

        private async Task Roll()
        {
            if (page == null || state == null || PerkPageModel.CheckRoll(state, PerkGame.CountCoins(), busy) != RollBlock.None)
            {
                return;
            }

            busy = true;
            var targets = PerkPageModel.SpinTargets(state);
            var started = Time.unscaledTime;
            page.StartSpin(targets);
            RenderPage("");
            try
            {
                var result = await PerkApi.Roll(state.NextRerollCost);
                if (result == null || result.Failed)
                {
                    page?.CancelSpin();
                    busy = false;
                    await Reload();
                    RenderPage(RollError(result?.Error));
                    PerkGame.Sound(EUISoundType.ErrorMessage);
                    return;
                }

                var fresh = await PerkApi.GetState();
                if (fresh == null)
                {
                    page?.CancelSpin();
                    busy = false;
                    RenderPage(Locale.Get("ui.network"));
                    return;
                }

                Accept(fresh, "roll");
                StartCoroutine(StopReels(targets, started));
            }
            catch (Exception e)
            {
                Log.Warn($"roll failed: {e}");
                page?.CancelSpin();
                busy = false;
                RenderPage(Locale.Get("ui.network"));
            }
        }

        /// <summary>왼쪽부터 하나씩 멈춘다. 결과는 이미 서버가 정했다.</summary>
        private IEnumerator StopReels(System.Collections.Generic.List<int> targets, float started)
        {
            // 왼쪽 릴부터 차례로 느려지며 멈춘다. 가끔 마지막 릴은 한 칸 지나쳐 멈춘 척하다가 되돌아간다
            var first = Mathf.Max(0, FirstStopDelay - (Time.unscaledTime - started));
            var effect = ReelSchedule.PickEffect(random);
            // 흔들면 갈 다음 칸에 다른 슬롯 특전이 나오지 않게
            var held = new System.Collections.Generic.HashSet<int>(state.Slots.Select(slot => slot.Perk));
            for (var i = 0; i < targets.Count; i++)
            {
                var slot = targets[i];
                if (page != null && slot < state.Slots.Count)
                {
                    page.Land(slot, state.Slots[slot].Perk, state.Slots[slot].Locked, first + i * StopInterval, i == targets.Count - 1 ? effect : ReelEffect.Normal, held);
                }
            }

            page?.EnableShake();
            while (page != null && page.IsSpinning)
            {
                yield return null;
            }

            if (page == null)
            {
                yield break;
            }

            page.CancelSpin();
            PerkGame.Sound(EUISoundType.TradeOperationComplete);
            busy = false;
            RenderPage(null);
            // 반짝임이 끝난 뒤 목록 색을 되돌린다
            yield return new WaitForSecondsRealtime(1f);
            RenderPage(null);
        }

        /// <summary>흔들기: 서버가 확률로 한 칸 더(다음 칸 특전), 리버스(다른 특전), 그대로 중 하나를 정한다.</summary>
        private async Task Shake(int slot, int next)
        {
            try
            {
                var fresh = await PerkApi.Shake(slot, next);
                if (fresh == null || fresh.Error != null)
                {
                    Log.Warn($"shake refused: {fresh?.Error ?? "no response"}");
                    return;
                }

                Accept(fresh, "shake");
                if (page == null || slot >= fresh.Slots.Count)
                {
                    return;
                }

                var result = fresh.Slots[slot];
                switch (fresh.ShakeOutcome)
                {
                    case "Next":
                        page.Nudge(slot, result.Perk, result.Locked);
                        break;
                    case "Reverse":
                        page.Reverse(slot, result.Perk, result.Locked);
                        break;
                    default:
                        return;
                }

                // 릴이 다 멈춘 뒤 응답이 왔으면 리롤 흐름이 이미 끝났으므로 따로 다시 그린다
                if (!busy)
                {
                    StartCoroutine(RenderWhenStopped());
                }
            }
            catch (Exception e)
            {
                Log.Warn($"shake failed: {e.Message}");
            }
        }

        private IEnumerator RenderWhenStopped()
        {
            while (page != null && page.IsSpinning)
            {
                yield return null;
            }

            RenderPage(null);
        }

        private string RollError(string error)
        {
            if (error != null && error.Contains(nameof(RollBlock.AllLocked)))
            {
                return Locale.Get("ui.allLocked");
            }

            if (error != null && error.Contains("CostChanged"))
            {
                return Locale.Get("ui.costChanged");
            }

            if (error != null && error.Contains("NoSlots"))
            {
                return Locale.Get("ui.noSlots");
            }

            return Locale.Format("ui.rollFailed", error ?? Locale.Get("ui.network"));
        }
    }
}
