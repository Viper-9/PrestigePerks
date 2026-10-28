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
    /// 특전 페이지 흐름: 작업 표시줄 버튼 → 페이지, 잠금·리롤·구매·넣기/빼기 → 서버 → 다시 그리기, 메인 메뉴 진입 때 Bonus 반영과 새 특전·단계 상한 알림.
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

        // add-on 아이콘은 실행마다 한 번만 묻는다 (실패해도 다시 묻지 않고 기본 아이콘)
        private static bool iconsRequested;

        // 효과 코드가 없는 code 특전은 실행마다 한 번만 알린다
        private static readonly System.Collections.Generic.HashSet<string> warnedCodePerks = new System.Collections.Generic.HashSet<string>();

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

        private PerkLocale Locale => locale ?? (locale = LoadLocale());

        /// <summary>게임 언어 문구. 이미 받은 state 가 있으면 그 catalog 의 add-on 문구도 쓴다.</summary>
        private PerkLocale LoadLocale()
        {
            var loaded = PerkGame.LoadLocale();
            if (state != null)
            {
                loaded.UseCatalog(state.Catalog);
            }

            return loaded;
        }

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

                locale = LoadLocale();
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
                locale = LoadLocale();
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
        public void OnMainMenuShown()
        {
            PerkEffects.FlushDevLog();
            _ = RefreshFromMainMenu();
        }

        /// <summary>F12 에서 개발자 모드를 바꿨을 때. 로그인 전이면 다음 메인 메뉴에서 알린다.</summary>
        public void SendDevMode()
        {
            if (PerkGame.Session == null)
            {
                return;
            }

            _ = SendDevModeAsync(Plugin.DevMode.Value);
        }

        private static async Task SendDevModeAsync(bool on)
        {
            try
            {
                await PerkApi.SetDevMode(on);
                Log.Info($"developer mode {(on ? "on" : "off")}");
            }
            catch (Exception e)
            {
                Log.Warn($"developer mode change failed: {e.Message}");
            }
        }

        private async Task RefreshFromMainMenu()
        {
            try
            {
                // 서버를 다시 켰을 수 있어 메인 메뉴마다 알린다. 개발자 모드를 끄면 서버가 개발용 all 을 끄므로 state 보다 먼저
                await PerkApi.SetDevMode(Plugin.DevMode.Value);
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
            // 목록 줄(이름 칸 폭)을 그리기 전에 add-on 문구를 넣는다
            Locale.UseCatalog(state.Catalog);
            RequestIcons();
            WarnUnregisteredCodePerks();
            PerkBonusSync.Apply(state);
            PerkEffects.Set(state.ActiveTiers, state.TierEffectRatio);
            if (state.NewPerks.Count == 0 && state.NewTierCap == null)
            {
                return;
            }

            // 새 특전과 단계 상한을 알림 한 줄에 함께 보인다
            var parts = new System.Collections.Generic.List<string>();
            if (state.NewPerks.Count > 0)
            {
                parts.Add(Locale.Format("ui.newPerk", string.Join(", ", state.NewPerks.Select(Locale.PerkName))));
            }

            if (state.NewTierCap is int cap)
            {
                parts.Add(Locale.Format("ui.newTierCap", PerkPage.TierName(cap)));
            }

            Log.Info($"new perks ({source}): {string.Join(", ", state.NewPerks)}, tier cap {state.NewTierCap?.ToString() ?? "-"}");
            try
            {
                NotificationManager.DisplayMessageNotification(string.Join("  ·  ", parts), ENotificationDurationType.Long);
                PerkGame.Sound(EUISoundType.QuestCompleted);
            }
            catch (Exception e)
            {
                Log.Warn($"new perk notification failed: {e}");
            }
        }

        /// <summary>catalog 에 아이콘이 있다고 한 add-on 특전의 아이콘을 실행마다 한 번 받는다. 받으면 열린 페이지를 다시 그린다.</summary>
        private void RequestIcons()
        {
            if (iconsRequested || !state.Catalog.Any(entry => entry.Icon && !PerkSprites.HasIcon(entry.Id)))
            {
                return;
            }

            iconsRequested = true;
            _ = FetchIcons();
        }

        /// <summary>서버에 code 특전이 있는데 그 효과를 낼 클라이언트 add-on 이 칸을 등록하지 않았으면 경고한다 (목록에는 보이지만 효과가 없다).</summary>
        private void WarnUnregisteredCodePerks()
        {
            foreach (var entry in state.Catalog)
            {
                if (entry.Kind == PerkCatalogEntryDto.CodeKind && !PerkPatchSlots.IsRegistered(entry.Id) && warnedCodePerks.Add(entry.Id))
                {
                    Log.Warn($"perk {entry.Id} is a code perk but no client add-on registered it; it has no effect");
                }
            }
        }

        private async Task FetchIcons()
        {
            try
            {
                var icons = await PerkApi.Icons();
                var added = 0;
                foreach (var pair in icons)
                {
                    try
                    {
                        if (PerkSprites.AddIcon(pair.Key, Convert.FromBase64String(pair.Value ?? "")))
                        {
                            added++;
                        }
                    }
                    catch (FormatException)
                    {
                        Log.Warn($"perk add-on icon unreadable: {pair.Key}");
                    }
                }

                Log.Info($"perk add-on icons: {added} / {icons.Count}");
                if (added > 0 && page != null)
                {
                    page.Redraw();
                }
            }
            catch (Exception e)
            {
                Log.Warn($"perk add-on icons failed: {e.Message}");
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

                locale = LoadLocale();
                bottomInset = taskbarButton != null ? PerkTaskbarButton.TopPixels(taskbarButton) : 0;
                page = PerkPage.Create(Locale, bottomInset);
                page.LockClicked += slot => _ = ToggleLock(slot);
                page.RollClicked += () => _ = Roll();
                page.BuyClicked += (perk, tier, price) => _ = Buy(perk, tier, price);
                page.EquipDropped += (perk, slot) => _ = Change(() => PerkApi.Equip(perk, slot), "equip", perk);
                page.UnequipDropped += slot => _ = Change(() => PerkApi.Unequip(slot), "unequip");
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

        /// <summary>message 는 리롤 버튼 아래, buyMessage 는 구매 버튼 아래 (구매 실패만).</summary>
        private void RenderPage(string message, string buyMessage = null)
        {
            if (page != null)
            {
                page.Render(state, PerkGame.CountCoins(), busy, message, buyMessage);
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
            // 멈출 칸 바로 위에 다른 슬롯 특전이 나오지 않게
            var held = new System.Collections.Generic.HashSet<string>(state.Slots.Where(slot => !slot.IsEmpty).Select(slot => slot.Perk));
            for (var i = 0; i < targets.Count; i++)
            {
                var slot = targets[i];
                if (page != null && slot < state.Slots.Count)
                {
                    page.Land(slot, state.Slots[slot].Perk, state.Slots[slot].Locked, first + i * StopInterval, i == targets.Count - 1 ? effect : ReelEffect.Normal, held);
                }
            }

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
            // 첫 리롤 칸의 새 특전을 골라 "영구 보유" 버튼이 바로 보이게
            if (targets.Count > 0)
            {
                page.SelectPerk(PerkPageModel.PerkInSlot(state, targets[0]));
            }

            RenderPage(null);
            // 반짝임이 끝난 뒤 목록 색을 되돌린다
            yield return new WaitForSecondsRealtime(1f);
            RenderPage(null);
        }

        /// <summary>
        /// 보유하기·단계 구매 (코인). 리롤처럼 아이템 이벤트로 보내고 결과는 state 로 받는다.
        /// 이미 그 단계를 가졌으면 서버가 결제 없이 성공으로 돌려준다 (두 번 눌림).
        /// </summary>
        private async Task Buy(string perk, int tier, int price)
        {
            if (busy || state == null || page == null || page.IsSpinning)
            {
                return;
            }

            busy = true;
            RenderPage("");
            try
            {
                var result = await PerkApi.Buy(perk, tier, price);
                busy = false;
                if (result == null || result.Failed)
                {
                    await Reload();
                    RenderPage(null, BuyError(result?.Error, price));
                    PerkGame.Sound(EUISoundType.ErrorMessage);
                    return;
                }

                var fresh = await PerkApi.GetState();
                if (fresh == null)
                {
                    RenderPage(null, Locale.Get("ui.network"));
                    return;
                }

                Accept(fresh, "buy");
                PerkGame.Sound(EUISoundType.TradeOperationComplete);
                RenderPage(null);
                // 슬롯 틀, 줄, 새로 산 단계 칸이 반짝인다
                page?.Flash(perk, tier);
            }
            catch (Exception e)
            {
                Log.Warn($"buy failed: {e}");
                busy = false;
                RenderPage(null, Locale.Get("ui.network"));
            }
        }

        /// <summary>
        /// 확정 넣기·빼기 (무료). 효과가 바뀌므로 응답을 Accept 해 Bonus 와 패치 특전을 다시 넣는다.
        /// flashPerk 가 있으면 (넣기) 성공 뒤 그 특전의 슬롯 틀과 줄이 반짝인다.
        /// </summary>
        private async Task Change(Func<Task<PerkStateDto>> request, string what, string flashPerk = null)
        {
            if (busy || state == null)
            {
                return;
            }

            busy = true;
            try
            {
                var fresh = await request();
                busy = false;
                if (fresh == null)
                {
                    RenderPage(Locale.Get("ui.network"));
                    return;
                }

                if (fresh.Error != null)
                {
                    Log.Warn($"{what} refused: {fresh.Error}");
                    state = fresh;
                    RenderPage(fresh.Error);
                    PerkGame.Sound(EUISoundType.ErrorMessage);
                    return;
                }

                Accept(fresh, what);
                PerkGame.Sound(EUISoundType.MenuInstallMag);
                RenderPage(null);
                if (flashPerk != null)
                {
                    page?.Flash(flashPerk, 0);
                }
            }
            catch (Exception e)
            {
                Log.Warn($"{what} failed: {e.Message}");
                busy = false;
                RenderPage(Locale.Get("ui.network"));
            }
        }

        private string BuyError(string error, int price)
        {
            if (error != null && error.Contains("CostChanged"))
            {
                return Locale.Get("ui.costChanged");
            }

            if (error != null && error.Contains("NotEnoughCoins"))
            {
                return Locale.Format("ui.notEnoughCoinsBuy", price, PerkGame.CountCoins());
            }

            return Locale.Format("ui.buyFailed", error ?? Locale.Get("ui.network"));
        }

        private string RollError(string error)
        {
            if (error != null && error.Contains("CostChanged"))
            {
                return Locale.Get("ui.costChanged");
            }

            if (error != null && error.Contains("NoSlots"))
            {
                return Locale.Get("ui.noSlots");
            }

            if (error != null && error.Contains("NothingToRoll"))
            {
                return Locale.Get("ui.nothingToRoll");
            }

            if (error != null && error.Contains("CollectionComplete"))
            {
                return Locale.Get("ui.collectionComplete");
            }

            return Locale.Format("ui.rollFailed", error ?? Locale.Get("ui.network"));
        }
    }
}
