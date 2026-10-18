using System;
using System.Collections.Generic;
using EFT.UI;
using PrestigePerks.Game;
using PrestigePerks.Model;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PrestigePerks.UI
{
    /// <summary>
    /// 특전 페이지 (dev-docs 2_특전.md 2절, 목업 A안): 위 특전 19개 목록, 아래 슬롯 1~6 릴, 오른쪽 코인·리롤.
    /// 그리기만 하고 서버 호출은 <see cref="PerkController"/> 가 한다. 열 때마다 화면 크기에 맞춰 다시 만든다.
    /// </summary>
    internal sealed class PerkPage : MonoBehaviour
    {
        private const float ReferenceHeight = 1080f;
        private const float Pad = 24, HeaderHeight = 56, RightWidth = 320, Gap = 16;
        private const float CellHeight = 64, CellGap = 6, ReelHeight = 210, ReelGap = 8;
        private const int Columns = 5;
        private const float FlashTime = 0.9f, RowHeight = 40f;
        private const int StripRows = 5;

        public event Action<int> LockClicked;
        public event Action RollClicked;
        public event Action CloseClicked;

        /// <summary>리롤 중 흔들기. 멈추려는 릴(슬롯)과 그 다음 칸에 보이는 특전을 넘긴다.</summary>
        public event Action<int, int> ShakeClicked;

        private PerkLocale locale;
        private RectTransform root;
        private TextMeshProUGUI levelLabel, detailLabel, coinsValue, freeValue, nextValue, nextSub, rollLabel, rollSub, messageLabel;
        private readonly List<Image> pips = new List<Image>();
        private Button rollButton;
        private Image rollFill;
        private readonly Dictionary<int, Cell> cells = new Dictionary<int, Cell>();
        private readonly List<Reel> reels = new List<Reel>();
        private string hint;

        // 릴 연출: 띠가 위에서 아래로 흐른다. 결과가 오면 감속해 멈추고, 가짜 멈춤이면 한 칸 지나쳤다가 위로 되돌아간다
        private readonly Dictionary<int, Spin> spinning = new Dictionary<int, Spin>();
        private readonly System.Random random = new System.Random();
        private bool shakeReady, shakeUsed, shakeShown;
        private double shakeChance;

        private enum SpinPhase
        {
            Fast,
            Landing,
            FakeHold,
            SnapBack,
            ReverseHold,
            ReverseSpin,
            NudgeHold,
            Nudge,
        }

        private sealed class Spin
        {
            public readonly List<int> Strip = new List<int>();
            public float Pos, LastPos;
            public SpinPhase Phase;
            public float Started, PhaseStart, From, To, Duration;
            public float LandAt = float.MaxValue;
            public int Target, Result, Stop;
            public bool Locked;
            public ReelEffect Effect;
            public ICollection<int> Held;
            public int? NudgeTo; // 흔들기로 한 칸 더 갈 특전 (멈추는 순간 적용)
            public int? ReverseTo; // 흔들기 리버스로 갈 특전 (멈춘 뒤 거꾸로 돈다)
        }

        private sealed class Cell
        {
            public Image Border;
            public Image Fill;
            public TextMeshProUGUI SlotRef;
            public float FlashUntil;
        }

        private sealed class Reel
        {
            public Image Border;
            public TextMeshProUGUI Header, State, Name, Effect;
            public Button LockButton;
            public Image LockBorder;
            public TextMeshProUGUI LockLabel;
            public float FlashUntil;
            public Color FlashColor = UiKit.Gold;
            public RectTransform Box;
            public Vector2 BoxHome;
            public float WobbleUntil;
            public bool Locked;
            public bool Open;
            public int CurrentPerk;

            // 돌 때만 보이는 띠 (가운데 칸 + 위아래 칸이 비친다)
            public readonly TextMeshProUGUI[] Rows = new TextMeshProUGUI[StripRows];
            public Vector2 RowHome;
        }

        public static PerkPage Create(PerkLocale locale, float bottomInsetPixels)
        {
            var go = new GameObject("PrestigePerksPage", typeof(RectTransform));
            DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // LootNet 의 기록 창(490)과 같은 층. 작업 표시줄 위 영역만 덮어 아래 버튼은 그대로 눌린다
            canvas.sortingOrder = 490;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = UnityEngine.Screen.height / ReferenceHeight;
            go.AddComponent<GraphicRaycaster>();
            var page = go.AddComponent<PerkPage>();
            page.locale = locale;
            page.Build(scaler.scaleFactor, bottomInsetPixels);
            return page;
        }

        private void Build(float scale, float bottomInsetPixels)
        {
            var width = UnityEngine.Screen.width / scale;
            var inset = bottomInsetPixels / scale;
            hint = locale.Get("ui.hint");

            root = UiKit.Fill(transform, "Root");
            root.offsetMin = new Vector2(0, inset);
            UiKit.Image(root, UiKit.Screen, raycast: true);

            // 머리줄
            var header = UiKit.Rect(root, "Header", 0, 0, width, HeaderHeight);
            UiKit.Image(header, UiKit.Header);
            UiKit.Image(UiKit.Rect(header, "Line", 0, HeaderHeight - 1, width, 1), UiKit.Line);
            UiKit.Label(header, "Title", Pad, 8, 220, 40, locale.Get("ui.title"), 30, UiKit.Text, TextAlignmentOptions.Left, FontStyles.Bold | FontStyles.UpperCase);
            levelLabel = UiKit.Label(header, "Level", Pad + 140, 8, 520, 40, "", 18, UiKit.Muted, TextAlignmentOptions.Left);
            var close = UiKit.Button(header, "Close", width - Pad - 130, 10, 130, 36, locale.Get("ui.close") + "  ✕", 16, UiKit.Line2, UiKit.Panel2, UiKit.Text);
            close.Button.onClick.AddListener(() => CloseClicked?.Invoke());

            var top = HeaderHeight + 18;
            var leftWidth = width - 2 * Pad - RightWidth - Gap;
            BuildCatalog(Pad, top, leftWidth);
            BuildSide(Pad + leftWidth + Gap, top);
        }

        private void BuildCatalog(float x, float y, float width)
        {
            SectionHeader(x, y, width, $"{locale.Get("ui.list")} · {PerkPageModel.AllPerks.Length}",
                $"<color=#{ColorUtility.ToHtmlStringRGB(UiKit.Gold)}>■</color> {locale.Get("ui.legendOwned")}   <color=#{ColorUtility.ToHtmlStringRGB(UiKit.Lock)}>■</color> {locale.Get("ui.legendLocked")}");

            var cellWidth = (width - (Columns - 1) * CellGap) / Columns;
            var gridY = y + 36;
            for (var i = 0; i < PerkPageModel.AllPerks.Length; i++)
            {
                var perk = PerkPageModel.AllPerks[i];
                var cx = x + i % Columns * (cellWidth + CellGap);
                var cy = gridY + i / Columns * (CellHeight + CellGap);
                var (border, inner) = UiKit.Box(root, $"Perk{perk}", cx, cy, cellWidth, CellHeight, UiKit.Line, UiKit.Panel);
                UiKit.Label(inner, "Name", 8, 5, cellWidth - 56, 26, locale.PerkName(perk), 19, UiKit.Text, TextAlignmentOptions.Left, FontStyles.Bold);
                UiKit.Label(inner, "No", cellWidth - 48, 7, 38, 16, $"#{perk}", 11, UiKit.Dim, TextAlignmentOptions.Right);
                UiKit.Label(inner, "Effect", 8, 33, cellWidth - 90, 24, locale.PerkEffect(perk), 13, UiKit.Muted, TextAlignmentOptions.Left);
                var slotRef = UiKit.Label(inner, "Slot", cellWidth - 86, 40, 76, 18, "", 12, UiKit.Gold, TextAlignmentOptions.Right, FontStyles.Bold);
                border.raycastTarget = true;
                AddHover(border.gameObject, perk);
                cells[perk] = new Cell { Border = border, Fill = inner.GetComponent<Image>(), SlotRef = slotRef };
            }

            var rows = (PerkPageModel.AllPerks.Length + Columns - 1) / Columns;
            var detailY = gridY + rows * (CellHeight + CellGap) + 4;
            detailLabel = UiKit.Label(root, "Detail", x, detailY, width, 26, hint, 14, UiKit.Muted, TextAlignmentOptions.Left);

            var slotsY = detailY + 40;
            SectionHeader(x, slotsY, width, locale.Get("ui.slots"), "");
            var reelWidth = (width - (PerkPageModel.MaxSlots - 1) * ReelGap) / PerkPageModel.MaxSlots;
            for (var i = 0; i < PerkPageModel.MaxSlots; i++)
            {
                reels.Add(BuildReel(i, x + i * (reelWidth + ReelGap), slotsY + 36, reelWidth));
            }
        }

        private Reel BuildReel(int index, float x, float y, float width)
        {
            var (border, inner) = UiKit.Box(root, $"Reel{index}", x, y, width, ReelHeight, UiKit.Line2, UiKit.ReelBg);
            var head = UiKit.Rect(inner, "Head", 0, 0, width - 2, 26);
            UiKit.Image(head, UiKit.Panel);
            var reel = new Reel
            {
                Border = border,
                Header = UiKit.Label(head, "Text", 8, 3, width - 90, 20, locale.Format("ui.slotN", index + 1), 13, UiKit.Muted, TextAlignmentOptions.Left, FontStyles.Bold),
                State = UiKit.Label(head, "State", width - 90, 3, 80, 20, "", 12, UiKit.Lock, TextAlignmentOptions.Right),
                Name = UiKit.Label(inner, "Name", 6, 52, width - 14, 44, "", 28, UiKit.Text, TextAlignmentOptions.Center, FontStyles.Bold),
                Effect = UiKit.Label(inner, "Effect", 8, 124, width - 18, 22, "", 13, UiKit.Muted, TextAlignmentOptions.Top),
            };

            // 띠: 이름 자리를 가운데로 위아래 칸이 잘려 보이게 가린다
            var strip = UiKit.Rect(inner, "Strip", 0, 26, width - 2, 96);
            strip.gameObject.AddComponent<RectMask2D>();
            for (var i = 0; i < StripRows; i++)
            {
                reel.Rows[i] = UiKit.Label(strip, $"Row{i}", 6, 52 - 26, width - 14, 44, "", 28, UiKit.Muted, TextAlignmentOptions.Center, FontStyles.Bold);
            }

            reel.RowHome = reel.Rows[0].rectTransform.anchoredPosition;
            reel.Box = border.rectTransform;
            reel.BoxHome = reel.Box.anchoredPosition;
            var lockButton = UiKit.Button(inner, "Lock", (width - 2) / 2 - 58, ReelHeight - 44, 116, 32, "", 14, UiKit.Line2, UiKit.ReelBg, UiKit.Muted);
            lockButton.Button.onClick.AddListener(() => LockClicked?.Invoke(index));
            reel.LockButton = lockButton.Button;
            reel.LockBorder = lockButton.Border;
            reel.LockLabel = lockButton.Label;
            return reel;
        }

        private void BuildSide(float x, float y)
        {
            var coins = Box(x, y, 96, "ui.coins");
            coinsValue = UiKit.Label(coins, "Value", 14, 28, RightWidth - 28, 40, "", 32, UiKit.Coin, TextAlignmentOptions.Left, FontStyles.Bold);
            UiKit.Label(coins, "Sub", 14, 68, RightWidth - 28, 20, locale.Get("ui.coinsSub"), 12, UiKit.Muted);

            var free = Box(x, y + 108, 110, "ui.free");
            freeValue = UiKit.Label(free, "Value", 14, 28, RightWidth - 28, 40, "", 30, UiKit.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            for (var i = 0; i < 3; i++)
            {
                var (_, pip) = UiKit.Box(free, $"Pip{i}", 14 + i * 34, 68, 28, 9, UiKit.Gold, UiKit.Panel);
                pips.Add(pip.GetComponent<Image>());
            }

            UiKit.Label(free, "Sub", 14, 84, RightWidth - 28, 20, locale.Get("ui.freeSub"), 12, UiKit.Muted);

            var next = Box(x, y + 230, 96, "ui.next");
            nextValue = UiKit.Label(next, "Value", 14, 28, RightWidth - 28, 40, "", 30, UiKit.Gold, TextAlignmentOptions.Left, FontStyles.Bold);
            nextSub = UiKit.Label(next, "Sub", 14, 68, RightWidth - 28, 20, "", 12, UiKit.Muted);

            var roll = UiKit.Button(root, "Roll", x, y + 346, RightWidth, 88, "", 30, UiKit.Gold, UiKit.Gold, UiKit.Dark);
            rollButton = roll.Button;
            rollFill = roll.Fill;
            // 도는 중에는 흔들기 버튼이 된다 (리롤마다 한 번). 다 멈춰야 다시 리롤할 수 있다
            rollButton.onClick.AddListener(() =>
            {
                if (spinning.Count == 0)
                {
                    RollClicked?.Invoke();
                    return;
                }

                var target = ShakeTarget();
                if (!shakeReady || shakeUsed || target < 0)
                {
                    return;
                }

                var spin = spinning[target];
                shakeUsed = true;
                Wobble(new[] { target });
                GameSounds(EUISoundType.MenuWeaponDisassemble);
                RenderShake();
                ShakeClicked?.Invoke(target, spin.Strip[spin.Stop + 1]);
            });
            var labelRect = (RectTransform)roll.Label.transform;
            labelRect.offsetMin = new Vector2(4, 30);
            rollLabel = roll.Label;
            rollLabel.text = locale.Get("ui.roll");
            rollLabel.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            rollSub = UiKit.Label(rollFill.rectTransform, "Sub", 4, 52, RightWidth - 10, 26, "", 16, UiKit.Dark, TextAlignmentOptions.Center, FontStyles.Bold);

            messageLabel = UiKit.Label(root, "Message", x, y + 446, RightWidth, 80, "", 14, UiKit.Bad);
        }

        private RectTransform Box(float x, float y, float height, string titleKey)
        {
            var (_, inner) = UiKit.Box(root, titleKey, x, y, RightWidth, height, UiKit.Line, UiKit.Panel);
            UiKit.Label(inner, "Key", 14, 8, RightWidth - 28, 18, locale.Get(titleKey), 13, UiKit.Muted, TextAlignmentOptions.Left, FontStyles.UpperCase);
            return inner;
        }

        private void SectionHeader(float x, float y, float width, string title, string right)
        {
            UiKit.Label(root, "SectionTitle", x, y, width / 2, 26, title, 17, UiKit.Text, TextAlignmentOptions.Left, FontStyles.Bold | FontStyles.UpperCase);
            if (!string.IsNullOrEmpty(right))
            {
                var label = UiKit.Label(root, "SectionRight", x + width / 2, y + 2, width / 2, 24, right, 13, UiKit.Muted, TextAlignmentOptions.Right);
                label.richText = true;
            }

            UiKit.Image(UiKit.Rect(root, "SectionLine", x, y + 28, width, 1), UiKit.Line);
        }

        private void AddHover(GameObject target, int perk)
        {
            var trigger = target.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                detailLabel.text = $"<b>{locale.PerkName(perk)}</b> — {locale.PerkEffect(perk)} · {locale.PerkDetail(perk)}";
                detailLabel.color = UiKit.Text;
            });
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ =>
            {
                detailLabel.text = hint;
                detailLabel.color = UiKit.Muted;
            });
            trigger.triggers.Add(enter);
            trigger.triggers.Add(exit);
        }

        /// <summary>상태를 그린다. 돌고 있는 릴의 이름은 연출이 맡는다.</summary>
        public void Render(PerkStateDto state, int coins, bool busy, string message)
        {
            if (state == null)
            {
                levelLabel.text = locale.Get("ui.loading");
                rollButton.interactable = false;
                messageLabel.text = message ?? "";
                return;
            }

            levelLabel.text = locale.Format("ui.level", state.PrestigeLevel, state.Slots.Count);
            coinsValue.text = coins.ToString();

            foreach (var pair in cells)
            {
                var slot = PerkPageModel.SlotOf(state, pair.Key);
                var spinningHere = slot >= 0 && spinning.ContainsKey(slot);
                var locked = slot >= 0 && state.Slots[slot].Locked;
                var color = slot < 0 || spinningHere ? UiKit.Line : locked ? UiKit.Lock : UiKit.Gold;
                pair.Value.Border.color = color;
                pair.Value.Fill.color = slot < 0 || spinningHere ? UiKit.Panel : Color.Lerp(UiKit.Panel, color, 0.12f);
                pair.Value.SlotRef.text = slot < 0 || spinningHere ? "" : locale.Format("ui.slotN", slot + 1);
                pair.Value.SlotRef.color = locked ? UiKit.Lock : UiKit.Gold;
            }

            for (var i = 0; i < reels.Count; i++)
            {
                RenderReel(i, state, busy);
            }

            freeValue.text = $"{state.FreeRerollsLeft} / 3";
            for (var i = 0; i < pips.Count; i++)
            {
                pips[i].color = i < state.FreeRerollsLeft ? UiKit.Gold : UiKit.Panel;
            }

            var lockedCount = PerkPageModel.LockedCount(state);
            if (state.NextRerollCost == 0)
            {
                nextValue.text = locale.Get("ui.nextFree");
                nextValue.color = UiKit.Gold;
                nextSub.text = locale.Format("ui.nextSubFree", lockedCount);
                rollSub.text = locale.Format("ui.rollFree", state.FreeRerollsLeft);
            }
            else
            {
                nextValue.text = locale.Format("ui.nextCoins", state.NextRerollCost);
                nextValue.color = UiKit.Coin;
                nextSub.text = locale.Format("ui.nextSubPaid", lockedCount, state.PaidRerolls + 1) + (state.CostFixed ? locale.Get("ui.fixed") : "");
                rollSub.text = locale.Format("ui.rollPaid", state.NextRerollCost);
            }

            shakeChance = state.ShakeChance;
            rollLabel.text = locale.Get("ui.roll");
            var block = PerkPageModel.CheckRoll(state, coins, busy);
            rollButton.interactable = block == RollBlock.None;
            rollFill.color = block == RollBlock.None ? UiKit.Gold : UiKit.Panel2;
            rollLabel.color = rollSub.color = block == RollBlock.None ? UiKit.Dark : UiKit.Dim;
            messageLabel.color = UiKit.Bad;
            messageLabel.text = message ?? BlockMessage(block, state, coins);
            if (spinning.Count > 0)
            {
                RenderShake();
            }
        }

        // 흔들 릴: 감속 중인 릴 중 가장 먼저 멈출 릴. 없으면 -1
        private int ShakeTarget()
        {
            var best = -1;
            var bestEnd = float.MaxValue;
            foreach (var pair in spinning)
            {
                var spin = pair.Value;
                var end = spin.PhaseStart + spin.Duration;
                if (spin.Phase == SpinPhase.Landing && spin.Effect == ReelEffect.Normal && spin.NudgeTo == null && spin.ReverseTo == null && end < bestEnd)
                {
                    best = pair.Key;
                    bestEnd = end;
                }
            }

            return best;
        }

        private void RenderShake()
        {
            var available = shakeReady && !shakeUsed && ShakeTarget() >= 0;
            shakeShown = available;
            rollButton.interactable = available;
            rollFill.color = available ? UiKit.Bad : UiKit.Panel2;
            rollLabel.color = rollSub.color = available ? UiKit.Dark : UiKit.Dim;
            rollLabel.text = locale.Get("ui.shake");
            rollSub.text = shakeUsed ? locale.Get("ui.shaken")
                : shakeReady ? locale.Format("ui.shakeSub", Mathf.RoundToInt((float)(shakeChance * 100)))
                : locale.Get("ui.rolling");
        }

        /// <summary>결과가 와서 흔들 수 있다 (서버가 이 리롤의 흔들기를 받는다).</summary>
        public void EnableShake()
        {
            shakeReady = true;
            if (spinning.Count > 0)
            {
                RenderShake();
            }
        }

        private void Wobble(IEnumerable<int> slots)
        {
            foreach (var slot in slots)
            {
                reels[slot].WobbleUntil = Time.unscaledTime + 0.4f;
            }
        }

        /// <summary>흔들기 결과 한 칸 더: 멈추려던 칸에서 한 칸 더 넘어가 perk 에서 멈춘다.</summary>
        public void Nudge(int slot, int perk, bool locked) => Redirect(slot, perk, locked, reverse: false);

        /// <summary>흔들기 결과 리버스: 멈춘 뒤 갑자기 거꾸로 돌아 perk 에서 멈춘다.</summary>
        public void Reverse(int slot, int perk, bool locked) => Redirect(slot, perk, locked, reverse: true);

        // 도는 릴이면 멈추는 순간 적용하고, 이미 멈춘 릴이면 (응답이 늦은 경우) 지금 칸에서 바로 시작한다
        private void Redirect(int slot, int perk, bool locked, bool reverse)
        {
            if (!spinning.TryGetValue(slot, out var spin))
            {
                var reel = reels[slot];
                spin = new Spin { Started = Time.unscaledTime, Target = reel.CurrentPerk, LandAt = 0 };
                // 거꾸로 돌 칸이 있도록 앞을 채운다
                ReelSchedule.Fill(spin.Strip, ReelSchedule.ReverseItems + 2, random, PerkPageModel.AllPerks, reel.CurrentPerk);
                spin.Strip.Add(reel.CurrentPerk);
                spin.Pos = spin.LastPos = spin.Strip.Count - 1;
                spin.Phase = SpinPhase.Landing;
                spin.Duration = float.MaxValue;
                spinning[slot] = spin;
                reel.Name.text = "";
                reel.Effect.text = "";
            }

            spin.Locked = locked;
            if (reverse)
            {
                spin.ReverseTo = perk;
            }
            else
            {
                spin.NudgeTo = perk;
            }

            if (spin.Duration == float.MaxValue)
            {
                Settle(reels[slot], spin, Time.unscaledTime, null);
                DrawStrip(reels[slot], spin);
            }
        }

        private void RenderReel(int index, PerkStateDto state, bool busy)
        {
            var reel = reels[index];
            reel.Open = PerkPageModel.IsSlotOpen(state, index);
            reel.LockButton.gameObject.SetActive(reel.Open);
            if (!reel.Open)
            {
                reel.Border.color = UiKit.Line;
                reel.State.text = "";
                reel.Name.text = locale.Format("ui.opensAt", index + 1);
                reel.Name.fontSize = 18;
                reel.Name.color = UiKit.Dim;
                reel.Effect.text = "";
                return;
            }

            var slot = state.Slots[index];
            reel.Locked = slot.Locked;
            reel.CurrentPerk = slot.Perk;
            reel.State.text = slot.Locked ? locale.Get("ui.lock") : "";
            reel.LockLabel.text = slot.Locked ? "■ " + locale.Get("ui.lock") : "□ " + locale.Get("ui.unlocked");
            reel.LockLabel.color = slot.Locked ? UiKit.Lock : UiKit.Muted;
            reel.LockBorder.color = slot.Locked ? UiKit.Lock : UiKit.Line2;
            reel.LockButton.interactable = !busy;
            if (spinning.ContainsKey(index))
            {
                return;
            }

            reel.Name.fontSize = 28;
            reel.Name.color = UiKit.Text;
            reel.Name.text = locale.PerkName(slot.Perk);
            reel.Effect.text = locale.PerkEffect(slot.Perk);
            if (Time.unscaledTime >= reel.FlashUntil)
            {
                reel.Border.color = slot.Locked ? UiKit.Lock : UiKit.Line2;
            }
        }

        private string BlockMessage(RollBlock block, PerkStateDto state, int coins)
        {
            switch (block)
            {
                case RollBlock.NoSlots:
                    return locale.Get("ui.noSlots");
                case RollBlock.AllLocked:
                    return locale.Get("ui.allLocked");
                case RollBlock.NotEnoughCoins:
                    return locale.Format("ui.notEnoughCoins", state.NextRerollCost, coins);
                default:
                    return "";
            }
        }

        public void ShowStatus(string text, Color color)
        {
            messageLabel.text = text;
            messageLabel.color = color;
        }

        /// <summary>릴을 돌리기 시작한다 (서버 결과가 오기 전부터 빨리 돈다). 지금 특전에서 출발한다.</summary>
        public void StartSpin(IEnumerable<int> slots)
        {
            var now = Time.unscaledTime;
            foreach (var slot in slots)
            {
                var reel = reels[slot];
                var spin = new Spin { Started = now };
                ReelSchedule.Fill(spin.Strip, 2, random, PerkPageModel.AllPerks, reel.CurrentPerk);
                spin.Strip.Add(reel.CurrentPerk);
                spin.Pos = spin.LastPos = 2;
                spinning[slot] = spin;
                reel.Name.text = "";
                reel.Effect.text = "";
                DrawStrip(reel, spin);
            }

            shakeReady = false;
            shakeUsed = false;
            RenderShake();
            GameSounds(EUISoundType.MenuCheckBox);
        }

        /// <summary>결과가 왔다. delay 초 뒤부터 감속해 perk 에서 멈춘다. effect 는 멈춤 연출 (<see cref="ReelEffect"/>).</summary>
        public void Land(int slot, int perk, bool locked, float delay, ReelEffect effect, ICollection<int> held = null)
        {
            if (!spinning.TryGetValue(slot, out var spin))
            {
                return;
            }

            spin.Target = perk;
            spin.Locked = locked;
            spin.Effect = effect;
            spin.Held = held;
            spin.LandAt = Time.unscaledTime + delay;
        }

        public void CancelSpin()
        {
            foreach (var slot in spinning.Keys)
            {
                HideStrip(reels[slot]);
            }

            spinning.Clear();
        }

        public bool IsSpinning => spinning.Count > 0;

        private void Update()
        {
            var now = Time.unscaledTime;
            List<int> finished = null;
            foreach (var pair in spinning)
            {
                if (Advance(reels[pair.Key], pair.Value, now))
                {
                    (finished ?? (finished = new List<int>())).Add(pair.Key);
                }
            }

            if (finished != null)
            {
                foreach (var slot in finished)
                {
                    spinning.Remove(slot);
                }
            }

            if (spinning.Count > 0 && shakeShown != (shakeReady && !shakeUsed && ShakeTarget() >= 0))
            {
                RenderShake();
            }

            foreach (var reel in reels)
            {
                if (now < reel.WobbleUntil)
                {
                    var left = (reel.WobbleUntil - now) / 0.4f;
                    reel.Box.anchoredPosition = reel.BoxHome + new Vector2(Mathf.Sin(now * 70f) * 7f * left, Mathf.Cos(now * 53f) * 2f * left);
                }
                else if (reel.Box.anchoredPosition != reel.BoxHome)
                {
                    reel.Box.anchoredPosition = reel.BoxHome;
                }

                if (now < reel.FlashUntil)
                {
                    var t = 1 - (reel.FlashUntil - now) / FlashTime;
                    reel.Border.color = Color.Lerp(reel.FlashColor, reel.Locked ? UiKit.Lock : UiKit.Line2, t);
                }
            }

            foreach (var cell in cells.Values)
            {
                if (now < cell.FlashUntil)
                {
                    var t = 1 - (cell.FlashUntil - now) / FlashTime;
                    cell.Fill.color = Color.Lerp(new Color(UiKit.Gold.r, UiKit.Gold.g, UiKit.Gold.b, 1) * 0.6f, UiKit.Panel, t);
                }
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CloseClicked?.Invoke();
            }
        }

        // 띠를 움직인다. 결과에서 멈췄으면 true
        private bool Advance(Reel reel, Spin spin, float now)
        {
            var dt = Time.unscaledDeltaTime;
            switch (spin.Phase)
            {
                case SpinPhase.Fast:
                    spin.Pos += ReelSchedule.Speed * Mathf.Clamp01((now - spin.Started) / ReelSchedule.SpinUp) * dt;
                    ReelSchedule.Fill(spin.Strip, Mathf.CeilToInt(spin.Pos) + 3, random, PerkPageModel.AllPerks, -1);
                    if (now >= spin.LandAt)
                    {
                        (spin.Result, spin.Stop) = ReelSchedule.Extend(spin.Strip, Mathf.CeilToInt(spin.Pos), spin.Target, spin.Effect, random, PerkPageModel.AllPerks, spin.Held);
                        Begin(spin, SpinPhase.Landing, now, spin.Pos, spin.Stop, ReelSchedule.LandingDuration(spin.Stop - spin.Pos) * (spin.Effect == ReelEffect.FakeOut ? ReelSchedule.FakeLandingStretch : 1f));
                    }

                    break;
                case SpinPhase.Landing:
                    var u = (now - spin.PhaseStart) / spin.Duration;
                    // 가짜 멈춤이면 미끼 칸이 끝에서 오래 기어 들어온다
                    spin.Pos = Mathf.Lerp(spin.From, spin.To, ReelSchedule.EaseOut(u, EasePower(spin)));
                    // 느려진 뒤부터 칸이 넘어갈 때마다 딸깍
                    if (Mathf.FloorToInt(spin.Pos + 0.5f) != Mathf.FloorToInt(spin.LastPos + 0.5f) && dt > 0 && (spin.Pos - spin.LastPos) / dt < 7f)
                    {
                        GameSounds(EUISoundType.ButtonOver);
                    }

                    if (u >= 1)
                    {
                        spin.Pos = spin.To;
                        if (spin.Effect == ReelEffect.Normal)
                        {
                            return Settle(reel, spin, now, EUISoundType.MenuInstallMag);
                        }

                        if (spin.Effect == ReelEffect.Reverse)
                        {
                            // 결과처럼 멈춘다 (효과 문구까지). 잠시 뒤 갑자기 거꾸로 돈다
                            Begin(spin, SpinPhase.ReverseHold, now, spin.Pos, spin.Pos, ReelSchedule.ReverseHold);
                            reel.Effect.text = locale.PerkEffect(spin.Strip[spin.Stop]);
                            GameSounds(EUISoundType.MenuInstallMag);
                            break;
                        }

                        // 결과처럼 멈춘다 (효과 문구까지). 결과 칸은 바로 아래에 걸려 보인다
                        Begin(spin, SpinPhase.FakeHold, now, spin.Pos, spin.Pos, ReelSchedule.FakeHold);
                        reel.Effect.text = locale.PerkEffect(spin.Strip[spin.Stop]);
                        GameSounds(EUISoundType.MenuInstallMag);
                    }

                    break;
                case SpinPhase.FakeHold:
                    // 결과 쪽으로 두 번 움찔하며 끌려가려다 버틴다
                    var held = (now - spin.PhaseStart) / spin.Duration;
                    var lastHeld = (now - dt - spin.PhaseStart) / spin.Duration;
                    spin.Pos = spin.Stop - ReelSchedule.Tug(held);
                    foreach (var (at, _) in ReelSchedule.Tugs)
                    {
                        if (lastHeld < at && held >= at)
                        {
                            GameSounds(EUISoundType.ButtonOver);
                        }
                    }

                    if (held >= 1)
                    {
                        // 갑자기 위로 끌려 올라가 결과 칸으로 돌아온다
                        reel.Effect.text = "";
                        Begin(spin, SpinPhase.SnapBack, now, spin.Stop, spin.Result, ReelSchedule.SnapBackTime);
                        GameSounds(EUISoundType.MenuInstallModFunc);
                    }

                    break;
                case SpinPhase.ReverseHold:
                    if (now - spin.PhaseStart >= spin.Duration)
                    {
                        reel.Effect.text = "";
                        reel.FlashUntil = now + FlashTime * 0.5f;
                        reel.FlashColor = UiKit.Bad;
                        Begin(spin, SpinPhase.ReverseSpin, now, spin.Stop, spin.Result, ReelSchedule.ReverseDuration(spin.Stop - spin.Result));
                        GameSounds(EUISoundType.MenuWeaponDisassemble);
                    }

                    break;
                case SpinPhase.ReverseSpin:
                    var r = (now - spin.PhaseStart) / spin.Duration;
                    spin.Pos = Mathf.Lerp(spin.From, spin.To, ReelSchedule.EaseOutCubic(r));
                    if (Mathf.FloorToInt(spin.Pos + 0.5f) != Mathf.FloorToInt(spin.LastPos + 0.5f) && dt > 0 && (spin.LastPos - spin.Pos) / dt < 7f)
                    {
                        GameSounds(EUISoundType.ButtonOver);
                    }

                    if (r >= 1)
                    {
                        spin.Pos = spin.To;
                        return Settle(reel, spin, now, EUISoundType.MenuWeaponAssemble);
                    }

                    break;
                case SpinPhase.NudgeHold:
                    if (now - spin.PhaseStart >= spin.Duration)
                    {
                        // 툭 하고 한 칸 더 넘어간다
                        reel.Effect.text = "";
                        reel.FlashUntil = now + FlashTime * 0.5f;
                        reel.FlashColor = UiKit.Bad;
                        Begin(spin, SpinPhase.Nudge, now, spin.Pos, Mathf.Round(spin.Pos) + 1, ReelSchedule.NudgeTime);
                        GameSounds(EUISoundType.MenuInstallModGear);
                    }

                    break;
                case SpinPhase.Nudge:
                    var n = (now - spin.PhaseStart) / spin.Duration;
                    spin.Pos = Mathf.LerpUnclamped(spin.From, spin.To, ReelSchedule.EaseOutBack(n, 1.6f));
                    if (n >= 1)
                    {
                        spin.Pos = spin.To;
                        return Finish(reel, spin, now, EUISoundType.MenuWeaponAssemble);
                    }

                    break;
                default:
                    var back = (now - spin.PhaseStart) / spin.Duration;
                    spin.Pos = Mathf.LerpUnclamped(spin.From, spin.To, ReelSchedule.EaseOutBack(back));
                    // 칸 하나를 휙 지나갈 때 딸깍
                    if (Mathf.FloorToInt(spin.Pos + 0.5f) != Mathf.FloorToInt(spin.LastPos + 0.5f))
                    {
                        GameSounds(EUISoundType.ButtonOver);
                    }
                    if (back >= 1)
                    {
                        spin.Pos = spin.To;
                        return Settle(reel, spin, now, EUISoundType.MenuWeaponAssemble);
                    }

                    break;
            }

            DrawStrip(reel, spin);
            spin.LastPos = spin.Pos;
            return false;
        }

        private static int EasePower(Spin spin) => spin.Effect == ReelEffect.FakeOut ? ReelSchedule.FakeEasePower : 3;

        private static void Begin(Spin spin, SpinPhase phase, float now, float from, float to, float duration)
        {
            spin.Phase = phase;
            spin.PhaseStart = now;
            spin.From = from;
            spin.To = to;
            spin.Duration = Mathf.Max(duration, 0.01f);
        }

        // 멈추는 순간. 흔들기로 바뀐 릴이면 멈춘 듯 서 있다가 한 칸 더 간다 (false). 아니면 끝 (true)
        private bool Settle(Reel reel, Spin spin, float now, EUISoundType? sound)
        {
            if (spin.ReverseTo is int other)
            {
                // 멈춘 척 서 있다가 거꾸로 돈다. 결과는 지나간 칸에 심는다 (보이는 범위 밖)
                spin.ReverseTo = null;
                var stop = Mathf.RoundToInt(spin.Pos);
                spin.Pos = stop;
                var result = Mathf.Max(stop - ReelSchedule.ReverseItems, 0);
                spin.Strip[result] = other;
                for (var k = Mathf.Max(result - 1, 0); k <= result + 1; k++)
                {
                    if (k != result && spin.Strip[k] == other)
                    {
                        spin.Strip[k] = spin.Strip[stop];
                    }
                }

                spin.Stop = stop;
                spin.Result = result;
                spin.Target = other;
                Begin(spin, SpinPhase.ReverseHold, now, stop, stop, ReelSchedule.ReverseHold);
                reel.Effect.text = locale.PerkEffect(spin.Strip[stop]);
                GameSounds(EUISoundType.MenuInstallMag);
                return false;
            }

            if (spin.NudgeTo is not int next)
            {
                return Finish(reel, spin, now, sound);
            }

            spin.NudgeTo = null;
            var here = Mathf.RoundToInt(spin.Pos);
            spin.Pos = here;
            ReelSchedule.Fill(spin.Strip, here + 4, random, PerkPageModel.AllPerks, next);
            spin.Strip[here + 1] = next;
            if (spin.Strip[here + 2] == next)
            {
                spin.Strip[here + 2] = spin.Strip[here];
            }

            spin.Target = next;
            Begin(spin, SpinPhase.NudgeHold, now, here, here, ReelSchedule.NudgeHold);
            reel.Effect.text = locale.PerkEffect(spin.Strip[here]);
            if (sound != null)
            {
                GameSounds(EUISoundType.MenuInstallMag);
            }

            return false;
        }

        // sound 가 null 이면 소리 없이
        private bool Finish(Reel reel, Spin spin, float now, EUISoundType? sound)
        {
            HideStrip(reel);
            reel.CurrentPerk = spin.Target;
            reel.Name.fontSize = 28;
            reel.Name.color = UiKit.Text;
            reel.Name.text = locale.PerkName(spin.Target);
            reel.Effect.text = locale.PerkEffect(spin.Target);
            reel.FlashUntil = now + FlashTime;
            reel.FlashColor = UiKit.Gold;
            reel.Locked = spin.Locked;
            if (cells.TryGetValue(spin.Target, out var cell))
            {
                cell.FlashUntil = now + FlashTime;
            }

            if (sound != null)
            {
                GameSounds(sound.Value);
            }

            return true;
        }

        // 위치 Pos 의 칸이 가운데. 다음 칸은 위, 지난 칸은 아래에 비친다
        private void DrawStrip(Reel reel, Spin spin)
        {
            var baseIndex = Mathf.FloorToInt(spin.Pos);
            var bright = spin.Phase == SpinPhase.Fast ? UiKit.Muted : UiKit.Text;
            for (var k = 0; k < StripRows; k++)
            {
                var index = baseIndex + k - StripRows / 2;
                var offset = index - spin.Pos;
                var row = reel.Rows[k];
                row.rectTransform.anchoredPosition = reel.RowHome + new Vector2(0, offset * RowHeight);
                row.text = index >= 0 && index < spin.Strip.Count ? locale.PerkName(spin.Strip[index]) : "";
                row.color = Color.Lerp(UiKit.Dim, bright, Mathf.Clamp01(1 - Mathf.Abs(offset)));
            }
        }

        private static void HideStrip(Reel reel)
        {
            foreach (var row in reel.Rows)
            {
                row.text = "";
            }
        }

        private static void GameSounds(EUISoundType sound) => PerkGame.Sound(sound);
    }
}
