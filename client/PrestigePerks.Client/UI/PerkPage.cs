using System;
using System.Collections.Generic;
using System.Linq;
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
    /// 특전 페이지 (dev-docs 2_특전.md 2절, 목업 2안): 왼쪽 위 장착 슬롯 카드 6장, 왼쪽 아래 특전 20줄 (두 열), 오른쪽 코인·리롤.
    /// 줄과 카드는 5.0 시즌 특전 줄과 프레스티지 보상 카드 스프라이트를 겹쳐 그린다 (<see cref="PerkSprites"/>).
    /// 그리기만 하고 서버 호출은 <see cref="PerkController"/> 가 한다. 열 때마다 화면 크기에 맞춰 다시 만든다.
    /// </summary>
    internal sealed class PerkPage : MonoBehaviour
    {
        private const float ReferenceHeight = 1080f;
        private const float Margin = 16, PanelGap = 12, HeaderTop = 12, PanelHead = 46, SideWidth = 452;
        private const float SlotsTop = 68, SlotsHeight = 238, ListTop = 318;
        private const float CardHeight = 168, CardGap = 12;

        // 슬롯 육각 배지 (목업 1안): 틀 폭, 위쪽 y. 높이는 정육각형 비율(376:436)을 따른다
        private const float HexWidth = 104, HexTop = 32;
        private const float RowHeight = 62, RowGap = 3, ColumnGap = 12;
        private const int RowsPerColumn = 10;
        private const float FlashTime = 0.9f;

        // 릴 띠: 육각 안쪽에서 아이콘이 흐르고 위아래 칸이 조금 비친다
        private const float StripStep = 66;
        private const int StripRows = 5;

        private static readonly Color32 RowBase = new Color32(19, 23, 25, 255);
        private static readonly Color32 SquareTop = new Color32(40, 45, 47, 255), SquareBottom = new Color32(26, 30, 32, 255);
        private static readonly Color RowGlowTint = new Color(96 / 255f, 170 / 255f, 84 / 255f, 0.22f);
        // 잠근 슬롯 배지: 푸른 틀·빛과 아이콘, 리롤 대상: 금색 틀과 아이콘
        private static readonly Color LockedFrame = new Color32(150, 205, 240, 255), LockedIcon = new Color32(220, 236, 246, 255);
        private static readonly Color LockedGlow = new Color(110 / 255f, 170 / 255f, 215 / 255f, 0.45f);
        private static readonly Color TargetIcon = new Color32(246, 226, 180, 255);
        private static readonly Color HoverLine = new Color32(150, 156, 158, 255);

        public event Action<int> LockClicked;
        public event Action RollClicked;
        public event Action CloseClicked;

        /// <summary>리롤 중 흔들기. 멈추려는 릴(슬롯)과 그 다음 칸에 보이는 특전을 넘긴다.</summary>
        public event Action<int, int> ShakeClicked;

        private PerkLocale locale;
        private RectTransform root;
        private TextMeshProUGUI levelLabel, coinsValue, freeValue, nextValue, lockedValue, rollLabel, rollSub, messageLabel;
        private RectTransform freeDots, nextDots, lockedDots;
        private float freeDotsStart, nextDotsStart, lockedDotsStart, sideWidth;
        private Image coinIcon;
        private Button rollButton;
        private Image rollFill, rollOutline;
        private readonly Dictionary<int, Row> rows = new Dictionary<int, Row>();
        private readonly List<Reel> reels = new List<Reel>();

        // 릴 연출: 띠가 위에서 아래로 흐른다. 결과가 오면 감속해 멈추고, 가짜 멈춤이면 한 칸 지나쳤다가 위로 되돌아간다
        private readonly Dictionary<int, Spin> spinning = new Dictionary<int, Spin>();
        private readonly System.Random random = new System.Random();
        private bool shakeReady, shakeUsed, shakeShown;
        // 슬롯 하나만 돌린 리롤만 흔들 수 있다 (서버도 같은 규칙)
        private bool shakeAllowed;
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

        private enum ButtonLook
        {
            Roll,
            Shake,
            Disabled,
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

        /// <summary>목록 한 줄 (5.0 PersonalPerkTemplate 처럼 아이콘 칸 + 이름 + 효과 + 설명).</summary>
        private sealed class Row
        {
            public Image Glow, Square, Icon, Outline, Flash, LockIcon;
            public TextMeshProUGUI Name, Effect, Slot;
            public float Width;
            public bool Hover, Target;
            public float FlashUntil;
        }

        /// <summary>장착 슬롯 (육각 배지). 리롤 때 육각 안에서 릴이 돈다.</summary>
        private sealed class Reel
        {
            public RectTransform Box;
            public Vector2 BoxHome;
            public Image Glow, Fill, Frame, Flash, Hover, FaceIcon, LockIcon;
            public TextMeshProUGUI Header, Name, Effect, Closed;
            public Button LockButton;
            public bool LockHover;
            public float FlashUntil;
            public Color FlashColor = UiKit.GoldHi;
            public float WobbleUntil;
            public bool Locked;
            public bool Open;
            public int CurrentPerk;

            // 돌 때만 보이는 띠 (가운데 칸 + 위아래 칸이 비친다)
            public RectTransform Strip;
            public readonly StripRow[] Rows = new StripRow[StripRows];
            public Vector2 RowHome;
        }

        private sealed class StripRow
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public Image Icon;
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
            // 16:9 가 아니면 (울트라와이드 등) 화면 너비에 맞춘다. 늘어난 너비는 목록 줄과 슬롯 카드가 나눠 쓴다
            var width = UnityEngine.Screen.width / scale;
            var height = ReferenceHeight - bottomInsetPixels / scale;

            root = UiKit.Fill(transform, "Root");
            root.offsetMin = new Vector2(0, bottomInsetPixels / scale);
            BuildBackground();
            BuildHeader(width);

            var leftWidth = width - 2 * Margin - PanelGap - SideWidth;
            BuildSlots(Margin, SlotsTop, leftWidth);
            BuildList(Margin, ListTop, leftWidth, height - Margin + 4 - ListTop);
            BuildSide(width - Margin - SideWidth, SlotsTop, height - Margin + 4 - SlotsTop);
        }

        private void BuildBackground()
        {
            UiKit.Sprite(root, PerkSprites.Gradient(new Color32(24, 30, 34, 255), new Color32(8, 10, 11, 255)), Color.white).raycastTarget = true;
            // 프레스티지 화면처럼 흐릿한 큰 문양
            UiKit.Sprite(UiKit.Rect(root, "Emblem", -140, 260, 700, 796), PerkSprites.Emblem, new Color(1, 1, 1, 0.05f));
            UiKit.Sprite(UiKit.Fill(root, "Vignette"), PerkSprites.Vignette, Color.white);
        }

        private void BuildHeader(float width)
        {
            // 아래 패널 머리띠와 같은 모양·폭 (좌우 여백, 가로 그라데이션, 테두리)
            var barWidth = width - 2 * Margin;
            var header = UiKit.Rect(root, "Header", Margin, HeaderTop, barWidth, PanelHead);
            UiKit.Sprite(header, PerkSprites.Gradient(new Color32(46, 56, 62, 240), new Color32(24, 29, 32, 240), true), Color.white);
            UiKit.Outline(header, UiKit.Line);
            var title = UiKit.Label(header, "Title", 16, 23, 300, locale.Get("ui.title"), 26, UiKit.Text);
            var titleWidth = UiKit.Width(title, title.text);
            levelLabel = UiKit.Label(header, "Level", 16 + titleWidth + 24, 24, 700, "", 16, UiKit.Muted);
            // 프레스티지 숫자를 크게 넣으므로 칸을 넘어도 지우지 않는다
            levelLabel.overflowMode = TextOverflowModes.Overflow;

            var close = UiKit.Rect(header, "Close", barWidth - 16 - 200, 0, 200, PanelHead);
            var closeHit = UiKit.Image(close, Color.clear);
            var closeLabel = UiKit.Label(close, "Label", 200, 23, 200, locale.Get("ui.close"), 26, UiKit.Text, TextAlignmentOptions.Right);
            var closeButton = UiKit.Button(closeHit);
            closeButton.targetGraphic = closeLabel;
            closeButton.onClick.AddListener(() => CloseClicked?.Invoke());
        }

        /// <summary>프레스티지 화면 패널: 왼쪽이 밝은 청회색 머리띠 + 평평한 본문.</summary>
        private RectTransform Panel(string name, float x, float y, float width, float height, string title)
        {
            var panel = UiKit.Rect(root, name, x, y, width, height);
            UiKit.Image(panel, new Color32(17, 21, 23, 230));
            UiKit.Sprite(UiKit.Rect(panel, "Head", 0, 0, width, PanelHead), PerkSprites.Gradient(new Color32(46, 56, 62, 240), new Color32(24, 29, 32, 240), true), Color.white);
            UiKit.Image(UiKit.Rect(panel, "HeadLine", 0, PanelHead, width, 1), new Color32(10, 12, 13, 255));
            UiKit.Outline(panel, UiKit.Line);
            UiKit.Label(panel, "Title", 16, 23, width / 2, title, 22, UiKit.Text);
            return panel;
        }

        private void BuildSlots(float x, float y, float width)
        {
            var panel = Panel("Slots", x, y, width, SlotsHeight, locale.Get("ui.slots"));
            var cardWidth = (width - 32 - (PerkPageModel.MaxSlots - 1) * CardGap) / PerkPageModel.MaxSlots;
            for (var i = 0; i < PerkPageModel.MaxSlots; i++)
            {
                reels.Add(BuildReel(panel, i, 16 + i * (cardWidth + CardGap), 58, cardWidth));
            }
        }

        private Reel BuildReel(RectTransform panel, int index, float x, float y, float width)
        {
            // 카드 바탕·틀 없이 "슬롯 n"과 육각 배지만 둔다. 칸 전체가 잠금 버튼이다
            var card = UiKit.Rect(panel, $"Slot{index}", x, y, width, CardHeight);
            var hexHeight = Mathf.Round(HexWidth * 436 / 376);
            var reel = new Reel
            {
                Box = card,
                BoxHome = card.anchoredPosition,
                Header = UiKit.Label(card, "Header", width / 2, 12, width - 20, locale.Format("ui.slotN", index + 1), 14, UiKit.Muted, TextAlignmentOptions.Center),
                Name = UiKit.Label(card, "Name", width / 2, 160, width - 20, "", 15, UiKit.Text, TextAlignmentOptions.Center),
                Effect = UiKit.Label(card, "Effect", width / 2, 160, width - 20, "", 13, UiKit.Green, TextAlignmentOptions.Center),
            };
            // 이름·효과 글자는 목록 줄에 있다. 릴 연출은 글자를 계속 채우지만 보이지 않는다
            reel.Name.gameObject.SetActive(false);
            reel.Effect.gameObject.SetActive(false);

            reel.Glow = UiKit.Sprite(UiKit.Rect(card, "Glow", (width - HexWidth * 1.3f) / 2, HexTop - hexHeight * 0.15f, HexWidth * 1.3f, hexHeight * 1.3f),
                PerkSprites.HexGlow, LockedGlow);
            // 바탕 그림은 틀과 같은 크기이고 육각이 틀 선 안쪽까지만 그려져 있다
            var fillWidth = HexWidth;
            var fillHeight = hexHeight;
            var fill = UiKit.Rect(card, "Fill", (width - fillWidth) / 2, HexTop, fillWidth, fillHeight);
            reel.Fill = UiKit.Sprite(fill, PerkSprites.HexFill, Color.white);
            // 아이콘과 띠는 육각 안쪽 모양으로 가린다
            fill.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var iconSize = Mathf.Round(HexWidth * 0.6f);
            reel.FaceIcon = UiKit.Sprite(UiKit.Rect(fill, "Icon", (fillWidth - iconSize) / 2, (fillHeight - iconSize) / 2, iconSize, iconSize), null, Color.white);
            reel.FaceIcon.preserveAspect = true;
            reel.FaceIcon.gameObject.SetActive(false);

            reel.Strip = UiKit.Fill(fill, "Strip");
            for (var i = 0; i < StripRows; i++)
            {
                var rowIcon = UiKit.Sprite(UiKit.Rect(reel.Strip, $"Row{i}", (fillWidth - iconSize) / 2, (fillHeight - iconSize) / 2, iconSize, iconSize), null, Color.white);
                rowIcon.preserveAspect = true;
                reel.Rows[i] = new StripRow { Rect = rowIcon.rectTransform, Group = rowIcon.gameObject.AddComponent<CanvasGroup>(), Icon = rowIcon };
            }

            reel.RowHome = reel.Rows[0].Rect.anchoredPosition;
            reel.Strip.gameObject.SetActive(false);

            var frame = UiKit.Rect(card, "Frame", (width - HexWidth) / 2, HexTop, HexWidth, hexHeight);
            reel.Frame = UiKit.Sprite(frame, PerkSprites.HexFrame, Color.white);
            reel.Hover = UiKit.Sprite(UiKit.Fill(frame, "Hover"), PerkSprites.HexFrame, Color.clear);
            reel.Flash = UiKit.Sprite(UiKit.Fill(frame, "Flash"), PerkSprites.HexFrame, Color.clear);
            reel.Closed = UiKit.Label(card, "Closed", width / 2, HexTop + hexHeight / 2, HexWidth - 10, "", 13, UiKit.Dim, TextAlignmentOptions.Center);

            // 자물쇠는 표시만 한다 (잠금 = 흰색, 안 잠김 = 흐리게). "슬롯 n" 오른쪽에 붙인다 (RenderReel)
            reel.LockIcon = UiKit.Sprite(UiKit.Rect(card, "Lock", 0, 5, 14, 14), PerkSprites.Lock, Color.white);
            reel.LockIcon.gameObject.SetActive(false);
            var hit = UiKit.Image(UiKit.Fill(card, "Hit"), Color.clear);
            reel.LockButton = UiKit.Button(hit);
            reel.LockButton.transition = Selectable.Transition.None;
            reel.LockButton.onClick.AddListener(() => LockClicked?.Invoke(index));
            OnHover(hit.gameObject, on =>
            {
                reel.LockHover = on;
                RefreshLock(reel);
            });
            hit.gameObject.SetActive(false);
            return reel;
        }

        /// <summary>아이콘 칸: 어두운 칸에 아이콘 (장착하지 않은 특전은 아이콘을 흐리게). 칸 그림과 아이콘을 돌려준다.</summary>
        private static (Image Square, Image Icon) IconSquare(RectTransform parent, string name, float x, float y, float size)
        {
            var rect = UiKit.Rect(parent, name, x, y, size, size);
            var square = UiKit.Sprite(rect, PerkSprites.Gradient(SquareTop, SquareBottom), Color.white);
            UiKit.Outline(rect, new Color(0, 0, 0, 120 / 255f));
            var iconSize = Mathf.Round(size * 0.88f);
            var icon = UiKit.Sprite(UiKit.Rect(rect, "Icon", (size - iconSize) / 2, (size - iconSize) / 2, iconSize, iconSize), null, Color.white);
            icon.preserveAspect = true;
            return (square, icon);
        }

        private void BuildList(float x, float y, float width, float height)
        {
            var panel = Panel("List", x, y, width, height, locale.Get("ui.list"));
            var title = panel.Find("Title").GetComponent<TextMeshProUGUI>();
            UiKit.Label(panel, "Count", 16 + UiKit.Width(title, title.text) + 12, 24, 80, PerkPageModel.AllPerks.Length.ToString(), 18, UiKit.Muted);

            var rowWidth = (width - 32 - ColumnGap) / 2;
            // 효과는 이름 칸 오른쪽에 맞춰 세운다. 영어 이름이 길어도 겹치지 않게 가장 긴 이름에 맞춘다
            var nameColumn = 66f;
            var measure = UiKit.Label(panel, "Measure", 0, 0, 10, "", 18, UiKit.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            foreach (var perk in PerkPageModel.AllPerks)
            {
                nameColumn = Mathf.Max(nameColumn, UiKit.Width(measure, locale.PerkName(perk)) + 14);
            }

            Destroy(measure.gameObject);

            for (var i = 0; i < PerkPageModel.AllPerks.Length; i++)
            {
                var perk = PerkPageModel.AllPerks[i];
                rows[perk] = BuildRow(panel, perk, 16 + i / RowsPerColumn * (rowWidth + ColumnGap), 58 + i % RowsPerColumn * (RowHeight + RowGap), rowWidth, nameColumn);
            }
        }

        private Row BuildRow(RectTransform panel, int perk, float x, float y, float width, float nameColumn)
        {
            var rect = UiKit.Rect(panel, $"Perk{perk}", x, y, width, RowHeight);
            UiKit.Image(rect, RowBase, raycast: true);
            UiKit.Sprite(UiKit.Fill(rect, "Gradient"), PerkSprites.RowGradient, new Color(1, 1, 1, 0.9f));
            UiKit.Sprite(UiKit.Fill(rect, "Grid"), PerkSprites.Grid, new Color(1, 1, 1, 0.55f), Image.Type.Tiled);
            var row = new Row { Width = width };
            // 5.0 '선택' 줄처럼 왼쪽에서 번지는 초록 (장착한 특전)
            row.Glow = UiKit.Sprite(UiKit.Rect(rect, "Glow", 0, 0, width * 0.55f, RowHeight), PerkSprites.RowGlow, RowGlowTint);
            UiKit.Sprite(UiKit.Rect(rect, "Shadow", width - width / 3, 0, width / 3, RowHeight), PerkSprites.RowShadow, new Color(1, 1, 1, 0.7f));
            UiKit.Image(UiKit.Rect(rect, "Bottom", 0, RowHeight - 1, width, 1), new Color32(8, 10, 11, 255));

            var size = RowHeight - 12;
            (row.Square, row.Icon) = IconSquare(rect, "Square", 6, 6, size);
            row.Icon.sprite = PerkSprites.Icon(perk);
            var x0 = size + 18;
            var effectX = x0 + nameColumn;
            // 이름·효과 한 줄 (설명 줄은 뺐다, 2026-10-20)
            var middle = RowHeight / 2;
            row.Name = UiKit.Label(rect, "Name", x0, middle, nameColumn, locale.PerkName(perk), 18, UiKit.Muted, TextAlignmentOptions.Left, FontStyles.Bold);
            row.Effect = UiKit.Label(rect, "Effect", effectX, middle, width - effectX - 110, locale.PerkEffect(perk), 15, UiKit.GreenDim, TextAlignmentOptions.Left, FontStyles.Bold);
            row.Slot = UiKit.Label(rect, "Slot", width - 14, middle, 100, "", 14, UiKit.Text, TextAlignmentOptions.Right);
            row.LockIcon = UiKit.Sprite(UiKit.Rect(rect, "Lock", 0, middle - 6.5f, 13, 13), PerkSprites.Lock, Color.white);
            row.LockIcon.enabled = false;
            row.Flash = UiKit.Image(UiKit.Fill(rect, "Flash"), Color.clear);
            row.Outline = UiKit.Outline(rect, Color.clear);
            OnHover(rect.gameObject, on =>
            {
                row.Hover = on;
                RefreshOutline(row);
            });
            SetEquipped(row, false);
            return row;
        }

        private void BuildSide(float x, float y, float height)
        {
            sideWidth = SideWidth;
            var w = SideWidth;
            var panel = Panel("Side", x, y, w, height, locale.Get("ui.roll"));

            // 보유 코인
            var art = UiKit.Rect(panel, "Coins", 1, PanelHead + 1, w - 2, 132);
            UiKit.Sprite(art, PerkSprites.Gradient(new Color32(28, 34, 38, 255), new Color32(16, 20, 22, 255)), Color.white);
            UiKit.Sprite(UiKit.Fill(art, "Grid"), PerkSprites.Grid, new Color(1, 1, 1, 0.5f), Image.Type.Tiled);
            // 받침 없이 코인 그림만 크게
            coinIcon = UiKit.Sprite(UiKit.Rect(art, "Coin", 12, 16, 100, 100), PerkSprites.CoinFallback, Color.white);
            coinIcon.preserveAspect = true;
            LoadCoinIcon();
            // 이름과 수치를 코인 그림(세로 가운데 66)에 맞춰 위아래로 둔다
            UiKit.Label(art, "Key", 128, 44, w - 148, locale.Get("ui.coins"), 16, UiKit.Muted);
            coinsValue = UiKit.Label(art, "Value", 126, 84, w - 146, "", 46, UiKit.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            UiKit.Image(UiKit.Rect(panel, "Line1", 1, 179, w - 2, 1), UiKit.Line);

            (freeValue, freeDots, freeDotsStart) = KeyValue(panel, 216, "ui.free");
            (nextValue, nextDots, nextDotsStart) = KeyValue(panel, 268, "ui.next");
            (lockedValue, lockedDots, lockedDotsStart) = KeyValue(panel, 320, "ui.lockedSlots");

            UiKit.Image(UiKit.Rect(panel, "Line2", 1, 372, w - 2, 1), UiKit.Line);

            // 맨 아래: 리롤 버튼. 메시지는 그 바로 위 (리롤 대상은 슬롯의 금색 틀로 보인다)
            var buttonY = height - 16 - 92;
            messageLabel = UiKit.Label(UiKit.Rect(panel, "Message", 18, buttonY - 12 - 72, w - 36, 72), "", 15, UiKit.Bad, TextAlignmentOptions.BottomLeft);
            messageLabel.enableWordWrapping = true;
            messageLabel.overflowMode = TextOverflowModes.Overflow;

            var roll = UiKit.Rect(panel, "Roll", 16, buttonY, w - 32, 92);
            rollFill = UiKit.Sprite(roll, null, Color.white);
            rollOutline = UiKit.Outline(roll, Color.clear);
            rollLabel = UiKit.Label(roll, "Label", (w - 32) / 2, 46 - 12, w - 48, locale.Get("ui.roll"), 38, UiKit.Dark, TextAlignmentOptions.Center, FontStyles.Bold);
            rollSub = UiKit.Label(roll, "Sub", (w - 32) / 2, 46 + 26, w - 48, "", 16, UiKit.Dark, TextAlignmentOptions.Center, FontStyles.Bold);
            rollButton = UiKit.Button(rollFill);
            SetRollLook(ButtonLook.Disabled);
            // 도는 중에는 흔들기 버튼이 된다 (리롤마다 한 번). 다 멈춰야 다시 리롤할 수 있다
            rollButton.onClick.AddListener(() =>
            {
                if (spinning.Count == 0)
                {
                    RollClicked?.Invoke();
                    return;
                }

                var target = ShakeTarget();
                if (!CanShake || target < 0)
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
        }

        /// <summary>점선으로 이은 이름·값 한 줄. 점선은 값 폭에 맞춰 Render 에서 늘인다.</summary>
        private (TextMeshProUGUI Value, RectTransform Dots, float DotsStart) KeyValue(RectTransform panel, float centerY, string key)
        {
            var label = UiKit.Label(panel, key, 18, centerY, 200, locale.Get(key), 16, UiKit.Muted);
            var value = UiKit.Label(panel, key + "Value", sideWidth - 18, centerY, 200, "", 22, UiKit.Text, TextAlignmentOptions.Right, FontStyles.Bold);
            var start = 18 + UiKit.Width(label, label.text) + 12;
            var dots = UiKit.Rect(panel, key + "Dots", start, centerY + 6, 0, 1);
            UiKit.Sprite(dots, PerkSprites.Dots, new Color32(80, 86, 90, 255), Image.Type.Tiled);
            return (value, dots, start);
        }

        private void SetDots(RectTransform dots, float start, TextMeshProUGUI value)
        {
            var end = sideWidth - 18 - UiKit.Width(value, value.text) - 12;
            dots.sizeDelta = new Vector2(Mathf.Max(0, end - start), 1);
        }

        private async void LoadCoinIcon()
        {
            var sprite = await PerkGame.CoinIcon();
            if (sprite != null && coinIcon != null)
            {
                coinIcon.sprite = sprite;
            }
        }

        private static void OnHover(GameObject target, Action<bool> changed)
        {
            var trigger = target.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => changed(true));
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => changed(false));
            trigger.triggers.Add(enter);
            trigger.triggers.Add(exit);
        }

        // 리롤 대상이면 얇은 금색 테두리, 마우스를 올리면 밝은 테두리
        private static void RefreshOutline(Row row) =>
            row.Outline.color = row.Target ? UiKit.Gold : row.Hover ? HoverLine : Color.clear;

        // 슬롯에 마우스를 올리면 자물쇠와 육각 틀이 밝아진다 (누르면 잠금 토글)
        private static void RefreshLock(Reel reel)
        {
            var hover = reel.LockHover && reel.LockButton.interactable;
            reel.LockIcon.color = new Color(1, 1, 1, reel.Locked ? 1f : hover ? 0.7f : 0.32f);
            reel.Hover.color = hover ? new Color(1, 1, 1, 0.45f) : Color.clear;
        }

        private static void SetEquipped(Row row, bool equipped)
        {
            row.Glow.enabled = equipped;
            row.Icon.color = new Color(1, 1, 1, equipped ? 1f : 0.5f);
            row.Name.color = equipped ? UiKit.Text : UiKit.Muted;
            row.Effect.color = equipped ? UiKit.Green : UiKit.GreenDim;
        }

        /// <summary>상태를 그린다. 돌고 있는 릴의 아이콘·이름은 연출이 맡는다.</summary>
        public void Render(PerkStateDto state, int coins, bool busy, string message)
        {
            if (state == null)
            {
                levelLabel.text = locale.Get("ui.loading");
                SetRollLook(ButtonLook.Disabled);
                rollLabel.text = locale.Get("ui.roll");
                rollSub.text = "";
                messageLabel.text = message ?? "";
                return;
            }

            var level = $"<size=24><b><color=#{ColorUtility.ToHtmlStringRGB(UiKit.Gold)}>{state.PrestigeLevel}</color></b></size>";
            levelLabel.text = locale.Format("ui.level", level, state.Slots.Count);
            coinsValue.text = coins.ToString();

            foreach (var pair in rows)
            {
                RenderRow(pair.Key, pair.Value, state);
            }

            for (var i = 0; i < reels.Count; i++)
            {
                RenderReel(i, state, busy);
            }

            freeValue.text = $"{state.FreeRerollsLeft} / 3";

            if (state.NextRerollCost == 0)
            {
                nextValue.text = locale.Get("ui.nextFree");
                nextValue.color = UiKit.Green;
                rollSub.text = locale.Format("ui.rollFree", state.FreeRerollsLeft);
            }
            else
            {
                nextValue.text = locale.Format("ui.nextCoins", state.NextRerollCost) + (state.CostFixed ? $"<size=14>{locale.Get("ui.fixed")}</size>" : "");
                nextValue.color = UiKit.Text;
                rollSub.text = locale.Format("ui.rollPaid", state.NextRerollCost);
            }

            lockedValue.text = $"{PerkPageModel.LockedCount(state)} / {state.Slots.Count}";
            SetDots(freeDots, freeDotsStart, freeValue);
            SetDots(nextDots, nextDotsStart, nextValue);
            SetDots(lockedDots, lockedDotsStart, lockedValue);

            shakeChance = state.ShakeChance;
            rollLabel.text = locale.Get("ui.roll");
            var block = PerkPageModel.CheckRoll(state, coins, busy);
            SetRollLook(block == RollBlock.None ? ButtonLook.Roll : ButtonLook.Disabled);
            messageLabel.color = UiKit.Bad;
            messageLabel.text = message ?? BlockMessage(block, state, coins);
            if (spinning.Count > 0)
            {
                RenderShake();
            }
        }

        private void RenderRow(int perk, Row row, PerkStateDto state)
        {
            var slot = PerkPageModel.SlotOf(state, perk);
            // 돌고 있는 슬롯에 있던 특전은 잠시 장착 표시를 비운다
            var equipped = slot >= 0 && !spinning.ContainsKey(slot);
            var locked = equipped && state.Slots[slot].Locked;
            SetEquipped(row, equipped);
            row.Target = equipped && !locked;
            RefreshOutline(row);
            row.LockIcon.enabled = locked;
            if (!equipped)
            {
                row.Slot.text = "";
                return;
            }

            // 리롤 대상은 금색 테두리만, 잠금은 자물쇠만 더한다
            row.Slot.text = locale.Format("ui.slotN", slot + 1);
            row.LockIcon.rectTransform.anchoredPosition = new Vector2(row.Width - 14 - UiKit.Width(row.Slot, row.Slot.text) - 20, row.LockIcon.rectTransform.anchoredPosition.y);
        }

        private void RenderReel(int index, PerkStateDto state, bool busy)
        {
            var reel = reels[index];
            reel.Open = PerkPageModel.IsSlotOpen(state, index);
            reel.LockButton.gameObject.SetActive(reel.Open);
            reel.LockIcon.gameObject.SetActive(reel.Open);
            if (!reel.Open)
            {
                // 아직 닫힌 슬롯: 흐린 빈 육각에 "P{n}에서 열림"
                reel.Header.color = UiKit.Dim;
                reel.Glow.enabled = false;
                reel.Frame.color = new Color(1, 1, 1, 0.35f);
                reel.Fill.color = new Color(1, 1, 1, 0.6f);
                reel.FaceIcon.gameObject.SetActive(false);
                reel.Closed.text = locale.Format("ui.opensAt", index + 1);
                reel.Name.text = "";
                reel.Effect.text = "";
                return;
            }

            var slot = state.Slots[index];
            reel.Header.color = UiKit.Muted;
            reel.Fill.color = Color.white;
            reel.Closed.text = "";
            reel.Locked = slot.Locked;
            reel.CurrentPerk = slot.Perk;
            // 잠근 슬롯은 푸른 틀과 빛, 리롤 대상은 금색 틀 (한 겹 선)
            reel.Glow.enabled = slot.Locked;
            reel.Frame.color = slot.Locked ? LockedFrame : UiKit.Gold;
            reel.FaceIcon.color = slot.Locked ? LockedIcon : TargetIcon;
            foreach (var row in reel.Rows)
            {
                row.Icon.color = reel.FaceIcon.color;
            }

            var headerWidth = UiKit.Width(reel.Header, reel.Header.text);
            reel.LockIcon.rectTransform.anchoredPosition = new Vector2(reel.Box.rect.width / 2 + headerWidth / 2 + 6, reel.LockIcon.rectTransform.anchoredPosition.y);
            reel.LockButton.interactable = !busy;
            RefreshLock(reel);
            if (spinning.ContainsKey(index))
            {
                return;
            }

            ShowFace(reel, slot.Perk);
        }

        private void ShowFace(Reel reel, int perk)
        {
            reel.FaceIcon.gameObject.SetActive(true);
            reel.FaceIcon.sprite = PerkSprites.Icon(perk);
            reel.Name.text = locale.PerkName(perk);
            reel.Effect.text = locale.PerkEffect(perk);
        }

        private static void HideFace(Reel reel)
        {
            reel.FaceIcon.gameObject.SetActive(false);
            reel.Name.text = "";
            reel.Effect.text = "";
        }

        private void SetRollLook(ButtonLook look)
        {
            switch (look)
            {
                case ButtonLook.Roll:
                    // 타르코프 기본 버튼: 조금 어두운 회색 그라데이션에 어두운 글자
                    rollFill.sprite = PerkSprites.Gradient(new Color32(158, 162, 158, 255), new Color32(98, 102, 102, 255));
                    rollOutline.color = new Color32(186, 190, 186, 255);
                    rollLabel.color = UiKit.Dark;
                    rollSub.color = new Color32(30, 34, 36, 255);
                    break;
                case ButtonLook.Shake:
                    rollFill.sprite = PerkSprites.Gradient(new Color32(238, 196, 110, 255), new Color32(172, 122, 46, 255));
                    rollOutline.color = new Color32(250, 222, 156, 255);
                    rollLabel.color = UiKit.Dark;
                    rollSub.color = new Color32(64, 46, 20, 255);
                    break;
                default:
                    rollFill.sprite = PerkSprites.Gradient(new Color32(50, 56, 60, 255), new Color32(32, 36, 39, 255));
                    rollOutline.color = UiKit.Line;
                    rollLabel.color = rollSub.color = UiKit.Dim;
                    break;
            }

            rollButton.interactable = look != ButtonLook.Disabled;
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

        private bool CanShake => shakeAllowed && shakeReady && !shakeUsed;

        private void RenderShake()
        {
            var available = CanShake && ShakeTarget() >= 0;
            shakeShown = available;
            SetRollLook(available ? ButtonLook.Shake : ButtonLook.Disabled);
            rollLabel.text = locale.Get("ui.shake");
            rollSub.text = !shakeAllowed ? locale.Get("ui.shakeOneSlot")
                : shakeUsed ? locale.Get("ui.shaken")
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
                HideFace(reel);
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
            var list = slots.ToList();
            foreach (var slot in list)
            {
                var reel = reels[slot];
                var spin = new Spin { Started = now };
                ReelSchedule.Fill(spin.Strip, 2, random, PerkPageModel.AllPerks, reel.CurrentPerk);
                spin.Strip.Add(reel.CurrentPerk);
                spin.Pos = spin.LastPos = 2;
                spinning[slot] = spin;
                HideFace(reel);
                DrawStrip(reel, spin);
            }

            shakeAllowed = list.Count == 1;
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

            if (spinning.Count > 0 && shakeShown != (CanShake && ShakeTarget() >= 0))
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
                    reel.Flash.color = new Color(reel.FlashColor.r, reel.FlashColor.g, reel.FlashColor.b, 1 - t);
                }
                else if (reel.Flash.color.a > 0)
                {
                    reel.Flash.color = Color.clear;
                }
            }

            foreach (var row in rows.Values)
            {
                if (now < row.FlashUntil)
                {
                    var t = 1 - (row.FlashUntil - now) / FlashTime;
                    row.Flash.color = new Color(UiKit.GoldHi.r, UiKit.GoldHi.g, UiKit.GoldHi.b, 0.3f * (1 - t));
                }
                else if (row.Flash.color.a > 0)
                {
                    row.Flash.color = Color.clear;
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
            ShowFace(reel, spin.Target);
            reel.FlashUntil = now + FlashTime;
            reel.FlashColor = UiKit.GoldHi;
            reel.Locked = spin.Locked;
            if (rows.TryGetValue(spin.Target, out var row))
            {
                row.FlashUntil = now + FlashTime;
            }

            if (sound != null)
            {
                GameSounds(sound.Value);
            }

            return true;
        }

        // 위치 Pos 의 칸이 가운데. 다음 칸은 위, 지난 칸은 아래에 비친다. 이름 자리에는 가운데에 가장 가까운 칸의 이름
        private void DrawStrip(Reel reel, Spin spin)
        {
            reel.Strip.gameObject.SetActive(true);
            var baseIndex = Mathf.FloorToInt(spin.Pos);
            var bright = spin.Phase == SpinPhase.Fast ? 0.75f : 1f;
            for (var k = 0; k < StripRows; k++)
            {
                var index = baseIndex + k - StripRows / 2;
                var offset = index - spin.Pos;
                var row = reel.Rows[k];
                row.Rect.anchoredPosition = reel.RowHome + new Vector2(0, offset * StripStep);
                var inStrip = index >= 0 && index < spin.Strip.Count;
                row.Group.alpha = inStrip ? Mathf.Lerp(0.25f, bright, Mathf.Clamp01(1 - Mathf.Abs(offset))) : 0;
                if (inStrip)
                {
                    row.Icon.sprite = PerkSprites.Icon(spin.Strip[index]);
                }
            }

            var center = Mathf.Clamp(Mathf.RoundToInt(spin.Pos), 0, spin.Strip.Count - 1);
            reel.Name.text = locale.PerkName(spin.Strip[center]);
            reel.Name.color = spin.Phase == SpinPhase.Fast ? UiKit.Muted : UiKit.Text;
        }

        private static void HideStrip(Reel reel) => reel.Strip.gameObject.SetActive(false);

        private static void GameSounds(EUISoundType sound) => PerkGame.Sound(sound);
    }
}
