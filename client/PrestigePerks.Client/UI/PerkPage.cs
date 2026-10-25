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
    /// 특전 페이지 (dev-docs 2_특전.md 2절, 목업 2안 + 개편 3안): 왼쪽 위 장착 슬롯 6칸, 왼쪽 아래 특전 20줄 (두 열), 오른쪽 코인·리롤·선택한 특전.
    /// 줄과 카드는 5.0 시즌 특전 줄과 프레스티지 보상 카드 스프라이트를 겹쳐 그린다 (<see cref="PerkSprites"/>).
    /// 그리기와 선택·드래그만 하고 서버 호출은 <see cref="PerkController"/> 가 한다. 열 때마다 화면 크기에 맞춰 다시 만든다.
    /// </summary>
    internal sealed class PerkPage : MonoBehaviour
    {
        private const float ReferenceHeight = 1080f;

        // 내용 폭 상한 (16:9 의 1920). 21:9 처럼 더 넓으면 가운데에 두고 배경만 화면 전체에 깐다
        private const float MaxContentWidth = 1920f;
        private const float Margin = 16, PanelGap = 12, HeaderTop = 12, PanelHead = 46, SideWidth = 452;
        private const float SlotsTop = 68, SlotsHeight = 238, ListTop = 318;
        private const float CardHeight = 168, CardGap = 12;

        // 슬롯 육각 배지 (목업 1안): 틀 폭, 위쪽 y. 높이는 정육각형 비율(376:436)을 따른다
        private const float HexWidth = 104, HexTop = 32;
        private const float RowHeight = 62, RowGap = 3, ColumnGap = 12;
        private const int RowsPerColumn = 10;
        private const float FlashTime = 0.9f;

        // 특전 줄 오른쪽 단계 칸 (I II III)
        private const float TierCellWidth = 24, TierCellHeight = 18, TierCellGap = 4;

        // 릴 띠: 육각 안쪽에서 아이콘이 흐르고 위아래 칸이 조금 비친다
        private const float StripStep = 66;
        private const int StripRows = 5;

        private static readonly Color32 RowBase = new Color32(19, 23, 25, 255);
        private static readonly Color32 SquareTop = new Color32(40, 45, 47, 255), SquareBottom = new Color32(26, 30, 32, 255);
        private static readonly Color RowGlowTint = new Color(96 / 255f, 170 / 255f, 84 / 255f, 0.22f);
        // 잠근 칸·확정 칸 배지: 푸른 틀·빛과 아이콘, 리롤 칸·빈 칸: 금색 틀과 아이콘
        private static readonly Color LockedFrame = new Color32(150, 205, 240, 255), LockedIcon = new Color32(220, 236, 246, 255);
        private static readonly Color LockedGlow = new Color(110 / 255f, 170 / 255f, 215 / 255f, 0.45f);
        private static readonly Color TargetIcon = new Color32(246, 226, 180, 255);
        private static readonly Color HoverLine = new Color32(150, 156, 158, 255);
        private static readonly Color SelectedLine = new Color32(232, 234, 230, 255);
        // 단계 칸: 미보유(흐림), 보유(밝음). 적용 중이면 초록 테두리
        // 보유한 칸이 한눈에 보이게 대비를 크게 둔다 (2026-10-25 게임 확인 뒤)
        private static readonly Color32 TierOffFill = new Color32(18, 21, 23, 255), TierOnFill = new Color32(96, 104, 106, 255);
        private static readonly Color32 TierOffLine = new Color32(40, 46, 49, 255), TierOnLine = new Color32(176, 182, 184, 255);
        private static readonly Color TierOffText = new Color32(64, 70, 73, 255), TierOnText = new Color32(240, 242, 238, 255);

        // 화면 제목은 패널 제목보다 조금 밝게
        private static readonly Color TitleText = new Color32(232, 234, 230, 255);

        // 버튼 모양: 타르코프 기본 버튼(밝은 회색 그라데이션), 꺼진 버튼·보조 버튼(어두운 그라데이션)
        private static readonly Color32 ButtonTop = new Color32(158, 162, 158, 255), ButtonBottom = new Color32(98, 102, 102, 255);
        private static readonly Color ButtonLine = new Color32(186, 190, 186, 255);
        private static readonly Color32 DarkTop = new Color32(50, 56, 60, 255), DarkBottom = new Color32(32, 36, 39, 255);

        // 버튼 색 틴트 평소 값 (마우스를 올리면 1.0): 기본 버튼 0.9, 보조 버튼 0.87 (≈ ×1.15 밝아짐)
        private const float ButtonTint = 0.9f, DarkTint = 0.87f;

        // 버튼 아래 줄의 코인 아이콘: 높이와 숫자까지 간격 (리롤·구매 버튼이 같은 크기라 하나로)
        private const float CoinHeight = 16, CoinGap = 5;

        public event Action<int> LockClicked;
        public event Action RollClicked;
        public event Action CloseClicked;

        /// <summary>보유하기(단계 1)·단계 구매. 특전, 단계, 화면에서 본 가격.</summary>
        public event Action<int, int, int> BuyClicked;

        /// <summary>장착 안 된 보유 특전을 슬롯에 놓았다. 특전, 슬롯 (0 부터).</summary>
        public event Action<int, int> EquipDropped;

        /// <summary>확정 칸을 목록에 놓았다. 슬롯 (0 부터).</summary>
        public event Action<int> UnequipDropped;

        private PerkLocale locale;
        // root 는 화면 전체 (배경, 드래그 카드), content 는 폭 상한을 둔 내용 (머리줄과 패널)
        private RectTransform root, content;
        private TextMeshProUGUI levelLabel, coinsValue, freeValue, lockedValue, rollLabel, rollSub, messageLabel;
        private RectTransform freeDots, lockedDots;
        private float freeDotsStart, lockedDotsStart, sideWidth;
        private Image coinIcon, rollCoin;
        private Button rollButton;
        private Image rollFill, rollOutline;
        private readonly Dictionary<int, Row> rows = new Dictionary<int, Row>();
        private readonly List<Reel> reels = new List<Reel>();

        // 목록 패널: 확정 칸을 끌면 테두리와 "여기에 놓으면 빠집니다"
        private RectTransform listPanel;
        private Image listDropOutline;
        private TextMeshProUGUI listDropLabel;

        // 목록 보기: 전체(20줄 고정 자리) 또는 보유만 (보유한 줄을 같은 두 열 순서로 위에서부터 채운다). 페이지를 열면 전체
        private bool ownedOnly;
        private TextMeshProUGUI listEmpty;
        private readonly List<Tab> tabs = new List<Tab>();
        private readonly List<Vector2> rowPlaces = new List<Vector2>();

        // 선택한 특전 칸
        private Picked picked;
        private int selectedPerk;

        // 마지막으로 그린 상태 (선택·드래그·확인 시간이 바뀌면 다시 그린다). 구매 메시지는 선택한 특전 칸 구매 버튼 아래
        private PerkStateDto lastState;
        private int lastCoins;
        private bool lastBusy;
        private string lastMessage, lastBuyMessage;

        // 드래그: 목록의 보유 특전(dragPerk) 또는 확정 칸(dragSlot). 끄는 동안 작은 카드가 마우스를 따라온다
        private int dragPerk, dragSlot = -1, dropReel = -1;
        private bool overList;
        private int dragEndFrame = -10;
        private RectTransform ghost;

        // 릴 연출: 띠가 위에서 아래로 흐른다. 결과가 오면 감속해 멈추고, 가짜 멈춤이면 한 칸 지나쳤다가 위로 되돌아간다
        private readonly Dictionary<int, Spin> spinning = new Dictionary<int, Spin>();
        private readonly System.Random random = new System.Random();

        private enum SpinPhase
        {
            Fast,
            Landing,
            FakeHold,
            SnapBack,
            ReverseHold,
            ReverseSpin,
        }

        private enum ButtonLook
        {
            Roll,
            Disabled,
        }

        private sealed class Spin
        {
            public readonly List<int> Strip = new List<int>();
            public IReadOnlyList<int> Pool;
            public float Pos, LastPos;
            public SpinPhase Phase;
            public float Started, PhaseStart, From, To, Duration;
            public float LandAt = float.MaxValue;
            public int Target, Result, Stop;
            public bool Locked;
            public ReelEffect Effect;
            public ICollection<int> Held;
        }

        /// <summary>목록 한 줄 (5.0 PersonalPerkTemplate 처럼 아이콘 칸 + 이름 + 효과 + 단계 칸).</summary>
        private sealed class Row
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public Image Glow, Square, Icon, Outline, Flash;
            public TextMeshProUGUI Name, Effect;
            public readonly Image[] TierFill = new Image[PerkPageModel.MaxTier];
            public readonly Image[] TierLine = new Image[PerkPageModel.MaxTier];
            public readonly TextMeshProUGUI[] TierLabel = new TextMeshProUGUI[PerkPageModel.MaxTier];
            public bool Hover, Target, Selected;
            public float FlashUntil;
        }

        /// <summary>목록 머리줄의 보기 탭 (전체 / 도감). 고른 탭은 타르코프 기본 버튼, 아닌 탭은 어두운 보조 버튼.</summary>
        private sealed class Tab
        {
            public bool Owned, Hover;
            public Button Button;
            public Image Fill, Outline;
            public TextMeshProUGUI Label;
        }

        /// <summary>장착 슬롯 (육각 배지). 리롤 때 육각 안에서 릴이 돈다.</summary>
        private sealed class Reel
        {
            public RectTransform Box;
            public CanvasGroup Group;
            public Image Glow, Fill, Frame, Flash, Hover, FaceIcon, LockIcon, FixedMark;
            public TextMeshProUGUI Header, Name, Effect, Closed;
            public RectTransform TierBox;
            public Image TierFill, TierLine;
            public TextMeshProUGUI TierLabel;
            public Button LockButton;
            public bool LockHover;
            public float FlashUntil;
            public Color FlashColor = UiKit.GoldHi;
            public SlotKind Kind;
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

        /// <summary>오른쪽 "선택한 특전" 칸. 상태와 상관없이 모양은 하나이고 글자와 버튼만 바뀐다.</summary>
        private sealed class Picked
        {
            public RectTransform Body;
            public Image Square, Icon, StatusLock, StatusMark;
            public TextMeshProUGUI Empty, Name, Status, Subject, Value, Tier, Next, Hint, BuyLabel, BuySub, BuyMessage;
            public RectTransform Buy;
            public Image BuyFill, BuyOutline, BuyCoin;
            public Button BuyButton;
            public float Width;
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
            // 16:9 보다 좁으면 (16:10, 4:3) 화면 너비에 맞춘다. 늘어난 너비는 목록 줄과 슬롯 카드가 나눠 쓴다.
            // 21:9 처럼 넓으면 내용 폭은 1920 에서 멈추고 가운데에 둔다 (줄 가운데가 비지 않게, 16:9 와 같은 모양)
            var width = UnityEngine.Screen.width / scale;
            var height = ReferenceHeight - bottomInsetPixels / scale;
            var contentWidth = Mathf.Min(width, MaxContentWidth);
            var left = (width - contentWidth) / 2;

            root = UiKit.Fill(transform, "Root");
            root.offsetMin = new Vector2(0, bottomInsetPixels / scale);
            BuildBackground(left);
            content = UiKit.Rect(root, "Content", left, 0, contentWidth, height);
            BuildHeader(contentWidth);

            var leftWidth = contentWidth - 2 * Margin - PanelGap - SideWidth;
            BuildSlots(Margin, SlotsTop, leftWidth);
            BuildList(Margin, ListTop, leftWidth, height - Margin + 4 - ListTop);
            BuildSide(contentWidth - Margin - SideWidth, SlotsTop, height - Margin + 4 - SlotsTop);
            // 코인 그림이 이미 받아 둔 것이면 바로 끝나므로 그림을 쓰는 칸을 다 만든 뒤 부른다
            LoadCoinIcon();
        }

        private void BuildBackground(float left)
        {
            UiKit.Sprite(root, PerkSprites.Gradient(new Color32(24, 30, 34, 255), new Color32(8, 10, 11, 255)), Color.white).raycastTarget = true;
            // 프레스티지 화면처럼 흐릿한 큰 문양. 내용과 같이 움직이고 비네트 아래에 깔린다
            UiKit.Sprite(UiKit.Rect(root, "Emblem", left - 140, 260, 700, 796), PerkSprites.Emblem, new Color(1, 1, 1, 0.05f));
            UiKit.Sprite(UiKit.Fill(root, "Vignette"), PerkSprites.Vignette, Color.white);
        }

        private void BuildHeader(float width)
        {
            // 아래 패널 머리띠와 같은 모양·폭 (좌우 여백, 가로 그라데이션, 테두리)
            var barWidth = width - 2 * Margin;
            var header = UiKit.Rect(content, "Header", Margin, HeaderTop, barWidth, PanelHead);
            UiKit.Sprite(header, PerkSprites.Gradient(new Color32(46, 56, 62, 240), new Color32(24, 29, 32, 240), true), Color.white);
            UiKit.Outline(header, UiKit.Line);
            var title = UiKit.Label(header, "Title", 16, 23, 300, locale.Get("ui.title"), 26, TitleText);
            var titleWidth = UiKit.Width(title, title.text);
            levelLabel = UiKit.Label(header, "Level", 16 + titleWidth + 24, 24, 900, "", 16, UiKit.Muted);
            // 프레스티지 숫자를 크게 넣으므로 칸을 넘어도 지우지 않는다
            levelLabel.overflowMode = TextOverflowModes.Overflow;

            // 작은 BACK 버튼. 제목과 같은 크기 글자였을 때는 두 번째 제목처럼 보였다
            var close = SmallButton(header, "Close", barWidth - 8 - 104, 8, 104, 30, locale.Get("ui.close"), 16);
            close.Label.color = UiKit.Text;
            OnHover(close.Fill.gameObject, on => close.Outline.color = on ? HoverLine : UiKit.Line);
            close.Button.onClick.AddListener(() => CloseClicked?.Invoke());
        }

        /// <summary>
        /// 작은 버튼 (BACK, 목록 탭): 어두운 보조 버튼 모양으로 만든다. 마우스를 올리면 색 틴트로 바탕이 밝아진다 (테두리는 부르는 쪽이 OnHover 로).
        /// 눌러도 선택 상태로 남지 않게 내비게이션을 끈다 (선택 상태면 마우스를 올려도 밝아지지 않는다).
        /// </summary>
        private static (Button Button, Image Fill, Image Outline, TextMeshProUGUI Label) SmallButton(RectTransform parent, string name, float x, float y,
            float width, float height, string text, float size)
        {
            var rect = UiKit.Rect(parent, name, x, y, width, height);
            var fill = UiKit.Sprite(rect, PerkSprites.Gradient(DarkTop, DarkBottom), Color.white);
            var outline = UiKit.Outline(rect, UiKit.Line);
            var label = UiKit.Label(rect, "Label", width / 2, height / 2, width, text, size, UiKit.Muted, TextAlignmentOptions.Center, FontStyles.Bold);
            var button = UiKit.Button(fill);
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            SetTint(button, DarkTint);
            return (button, fill, outline, label);
        }

        /// <summary>버튼 색 틴트의 평소 값 (마우스를 올리면 1.0).</summary>
        private static void SetTint(Button button, float normal)
        {
            var colors = button.colors;
            colors.normalColor = colors.selectedColor = new Color(normal, normal, normal, 1);
            button.colors = colors;
        }

        /// <summary>프레스티지 화면 패널: 왼쪽이 밝은 청회색 머리띠 + 평평한 본문.</summary>
        private RectTransform Panel(string name, float x, float y, float width, float height, string title)
        {
            var panel = UiKit.Rect(content, name, x, y, width, height);
            UiKit.Image(panel, new Color32(17, 21, 23, 230));
            Head(panel, 0, width, title);
            UiKit.Outline(panel, UiKit.Line);
            return panel;
        }

        /// <summary>패널 머리띠 (청회색 가로 그라데이션 + 아래 선 + 제목).</summary>
        private static void Head(RectTransform panel, float y, float width, string title)
        {
            UiKit.Sprite(UiKit.Rect(panel, "Head", 0, y, width, PanelHead), PerkSprites.Gradient(new Color32(46, 56, 62, 240), new Color32(24, 29, 32, 240), true), Color.white);
            UiKit.Image(UiKit.Rect(panel, "HeadLine", 0, y + PanelHead, width, 1), new Color32(10, 12, 13, 255));
            UiKit.Label(panel, "Title", 16, y + 23, width / 2, title, 22, UiKit.Text);
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
            // 카드 바탕·틀 없이 "슬롯 n"과 육각 배지, 그 아래 단계 배지만 둔다. 칸 전체가 버튼이다
            var card = UiKit.Rect(panel, $"Slot{index}", x, y, width, CardHeight);
            var hexHeight = Mathf.Round(HexWidth * 436 / 376);
            var reel = new Reel
            {
                Box = card,
                Group = card.gameObject.AddComponent<CanvasGroup>(),
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

            // 단계 배지: 육각 아래 가운데에 걸친 작은 칸 (그 칸에 지금 적용 중인 단계)
            reel.TierBox = UiKit.Rect(card, "Tier", (width - 34) / 2, HexTop + hexHeight - 11, 34, 20);
            reel.TierFill = UiKit.Image(reel.TierBox, UiKit.Dark);
            reel.TierLine = UiKit.Outline(reel.TierBox, UiKit.Gold);
            reel.TierLabel = UiKit.Label(reel.TierBox, "Label", 17, 10, 34, "", 13, UiKit.Text, TextAlignmentOptions.Center, FontStyles.Bold);

            // 자물쇠(잠금·풀림)나 ◆(확정)는 표시만 한다. "슬롯 n" 오른쪽에 붙인다 (RenderReel)
            reel.LockIcon = UiKit.Sprite(UiKit.Rect(card, "Lock", 0, 5, 14, 14), PerkSprites.Lock, Color.white);
            reel.LockIcon.gameObject.SetActive(false);
            reel.FixedMark = Diamond(card, "Fixed", 0, 13, 8);
            reel.FixedMark.gameObject.SetActive(false);
            var hit = UiKit.Image(UiKit.Fill(card, "Hit"), Color.clear);
            reel.LockButton = UiKit.Button(hit);
            reel.LockButton.transition = Selectable.Transition.None;
            reel.LockButton.onClick.AddListener(() => OnSlotClicked(index));
            OnHover(hit.gameObject, on =>
            {
                reel.LockHover = on;
                RefreshLock(reel);
            });
            On(hit.gameObject, EventTriggerType.BeginDrag, data => BeginSlotDrag(index, data));
            On(hit.gameObject, EventTriggerType.Drag, MoveDrag);
            On(hit.gameObject, EventTriggerType.EndDrag, EndDrag);
            hit.gameObject.SetActive(false);
            return reel;
        }

        /// <summary>◆ 표시: 45도 돌린 작은 정사각형. centerY 는 세로 가운데.</summary>
        private static Image Diamond(RectTransform parent, string name, float x, float centerY, float size)
        {
            var rect = UiKit.Rect(parent, name, x, centerY, size, size);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localRotation = Quaternion.Euler(0, 0, 45);
            return UiKit.Image(rect, UiKit.Text);
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
            listPanel = Panel("List", x, y, width, height, locale.Get("ui.list"));
            // 머리줄 제목 오른쪽에 보기 탭 두 개: 전체 / 도감 (보유만). 보유 수는 맨 위 머리줄 "도감 n / 20" 에만 둔다
            var title = listPanel.Find("Title").GetComponent<TextMeshProUGUI>();
            var measure = UiKit.Label(listPanel, "Measure", 0, 0, 10, "", 14, UiKit.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            var tabX = 16 + UiKit.Width(title, title.text) + 22;
            foreach (var owned in new[] { false, true })
            {
                var text = locale.Get(owned ? "ui.tabCollection" : "ui.tabAll");
                var tabWidth = UiKit.Width(measure, text) + 26;
                tabs.Add(BuildTab(listPanel, tabX, tabWidth, text, owned));
                tabX += tabWidth + 6;
            }

            listEmpty = UiKit.Label(listPanel, "Empty", width / 2, height / 2, width - 32, locale.Get("ui.ownedEmpty"), 16, UiKit.Muted, TextAlignmentOptions.Center);
            listEmpty.enabled = false;
            listDropLabel = UiKit.Label(listPanel, "Drop", width - 16, 23, width / 2, locale.Get("ui.dropUnequip"), 16, SelectedLine, TextAlignmentOptions.Right);
            listDropLabel.enabled = false;

            var rowWidth = (width - 32 - ColumnGap) / 2;
            // 효과는 이름 칸 오른쪽에 맞춰 세운다. 영어 이름이 길어도 겹치지 않게 가장 긴 이름에 맞추고 28 띄운다
            var nameColumn = 66f;
            measure.fontSize = 18;
            foreach (var perk in PerkPageModel.AllPerks)
            {
                nameColumn = Mathf.Max(nameColumn, UiKit.Width(measure, locale.PerkName(perk)) + 28);
            }

            Destroy(measure.gameObject);

            for (var i = 0; i < PerkPageModel.AllPerks.Length; i++)
            {
                var perk = PerkPageModel.AllPerks[i];
                rowPlaces.Add(new Vector2(16 + i / RowsPerColumn * (rowWidth + ColumnGap), -(58 + i % RowsPerColumn * (RowHeight + RowGap))));
                rows[perk] = BuildRow(listPanel, perk, rowPlaces[i].x, -rowPlaces[i].y, rowWidth, nameColumn);
            }

            // 확정 칸을 끄는 동안 목록 전체 테두리 (줄 위에 그린다)
            listDropOutline = UiKit.Outline(listPanel, Color.clear);
        }

        /// <summary>목록 보기 탭 하나 (머리줄 y 9, 높이 28). 누르면 그 보기로 바꾼다.</summary>
        private Tab BuildTab(RectTransform panel, float x, float width, string text, bool owned)
        {
            var (button, fill, outline, label) = SmallButton(panel, owned ? "TabOwned" : "TabAll", x, 9, width, 28, text, 14);
            var tab = new Tab { Owned = owned, Button = button, Fill = fill, Outline = outline, Label = label };
            OnHover(fill.gameObject, on =>
            {
                tab.Hover = on;
                RefreshTab(tab);
            });
            button.onClick.AddListener(() =>
            {
                if (ownedOnly == owned || ghost != null)
                {
                    return;
                }

                ownedOnly = owned;
                GameSounds(EUISoundType.ButtonOver);
                Rerender();
            });
            RefreshTab(tab);
            return tab;
        }

        // 고른 탭은 타르코프 기본 버튼 (밝은 회색, 어두운 글자), 아닌 탭은 어두운 보조 버튼 (마우스를 올리면 밝은 테두리·글자)
        private void RefreshTab(Tab tab)
        {
            var on = tab.Owned == ownedOnly;
            tab.Fill.sprite = on ? PerkSprites.Gradient(ButtonTop, ButtonBottom) : PerkSprites.Gradient(DarkTop, DarkBottom);
            tab.Outline.color = on ? ButtonLine : tab.Hover ? HoverLine : UiKit.Line;
            tab.Label.color = on ? UiKit.Dark : tab.Hover ? UiKit.Text : UiKit.Muted;
            SetTint(tab.Button, on ? ButtonTint : DarkTint);
        }

        // 보기 탭과 줄 자리. 전체는 고정 자리, 보유만은 보유한 줄을 위에서부터 채우고 나머지는 숨긴다
        private void RenderListView(PerkStateDto state)
        {
            foreach (var tab in tabs)
            {
                RefreshTab(tab);
            }

            // 확정 칸을 끄는 동안에는 오른쪽에 "여기에 놓으면 빠집니다"가 나온다
            listEmpty.enabled = ownedOnly && PerkPageModel.OwnedCount(state) == 0;

            var place = 0;
            foreach (var perk in PerkPageModel.AllPerks)
            {
                var row = rows[perk];
                var show = !ownedOnly || PerkPageModel.OwnedTier(state, perk) > 0;
                row.Rect.gameObject.SetActive(show);
                if (show)
                {
                    row.Rect.anchoredPosition = rowPlaces[ownedOnly ? place : Array.IndexOf(PerkPageModel.AllPerks, perk)];
                    place++;
                }
            }
        }

        private Row BuildRow(RectTransform panel, int perk, float x, float y, float width, float nameColumn)
        {
            var rect = UiKit.Rect(panel, $"Perk{perk}", x, y, width, RowHeight);
            UiKit.Image(rect, RowBase, raycast: true);
            UiKit.Sprite(UiKit.Fill(rect, "Gradient"), PerkSprites.RowGradient, new Color(1, 1, 1, 0.9f));
            UiKit.Sprite(UiKit.Fill(rect, "Grid"), PerkSprites.Grid, new Color(1, 1, 1, 0.55f), Image.Type.Tiled);
            var row = new Row { Rect = rect, Group = rect.gameObject.AddComponent<CanvasGroup>() };
            // 5.0 '선택' 줄처럼 왼쪽에서 번지는 초록 (장착한 특전)
            row.Glow = UiKit.Sprite(UiKit.Rect(rect, "Glow", 0, 0, width * 0.55f, RowHeight), PerkSprites.RowGlow, RowGlowTint);
            UiKit.Sprite(UiKit.Rect(rect, "Shadow", width - width / 3, 0, width / 3, RowHeight), PerkSprites.RowShadow, new Color(1, 1, 1, 0.7f));
            UiKit.Image(UiKit.Rect(rect, "Bottom", 0, RowHeight - 1, width, 1), new Color32(8, 10, 11, 255));

            var size = RowHeight - 12;
            (row.Square, row.Icon) = IconSquare(rect, "Square", 6, 6, size);
            row.Icon.sprite = PerkSprites.Icon(perk);
            var x0 = size + 18;
            var effectX = x0 + nameColumn;
            var tiersWidth = PerkPageModel.MaxTier * TierCellWidth + (PerkPageModel.MaxTier - 1) * TierCellGap;
            // 이름·효과 한 줄 (설명 줄은 뺐다, 2026-10-20). 오른쪽 끝에 단계 칸 I II III
            var middle = RowHeight / 2;
            row.Name = UiKit.Label(rect, "Name", x0, middle, nameColumn, locale.PerkName(perk), 18, UiKit.Muted, TextAlignmentOptions.Left, FontStyles.Bold);
            row.Effect = UiKit.Label(rect, "Effect", effectX, middle, width - effectX - tiersWidth - 28, "", 15, UiKit.GreenDim, TextAlignmentOptions.Left, FontStyles.Bold);
            for (var t = 0; t < PerkPageModel.MaxTier; t++)
            {
                var cell = UiKit.Rect(rect, $"Tier{t + 1}", width - 14 - tiersWidth + t * (TierCellWidth + TierCellGap), middle - TierCellHeight / 2, TierCellWidth, TierCellHeight);
                row.TierFill[t] = UiKit.Image(cell, TierOffFill);
                row.TierLine[t] = UiKit.Outline(cell, TierOffLine);
                row.TierLabel[t] = UiKit.Label(cell, "Label", TierCellWidth / 2, TierCellHeight / 2, TierCellWidth, TierName(t + 1), 11, UiKit.Dim, TextAlignmentOptions.Center, FontStyles.Bold);
            }

            row.Flash = UiKit.Image(UiKit.Fill(rect, "Flash"), Color.clear);
            row.Outline = UiKit.Outline(rect, Color.clear);
            OnHover(rect.gameObject, on =>
            {
                row.Hover = on;
                RefreshOutline(row);
            });
            On(rect.gameObject, EventTriggerType.PointerClick, _ => OnRowClicked(perk));
            On(rect.gameObject, EventTriggerType.BeginDrag, data => BeginRowDrag(perk, data));
            On(rect.gameObject, EventTriggerType.Drag, MoveDrag);
            On(rect.gameObject, EventTriggerType.EndDrag, EndDrag);
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
            // 이름과 수치를 코인 그림(세로 가운데 66)에 맞춰 위아래로 둔다
            UiKit.Label(art, "Key", 128, 44, w - 148, locale.Get("ui.coins"), 16, UiKit.Muted);
            coinsValue = UiKit.Label(art, "Value", 126, 84, w - 146, "", 46, UiKit.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            UiKit.Image(UiKit.Rect(panel, "Line1", 1, 179, w - 2, 1), UiKit.Line);

            // 수치는 두 줄만 (다음 비용은 리롤 버튼 아래 줄, 확정 수는 슬롯 ◆ 로 보인다)
            (freeValue, freeDots, freeDotsStart) = KeyValue(panel, 212, "ui.free");
            (lockedValue, lockedDots, lockedDotsStart) = KeyValue(panel, 256, "ui.lockedSlots");

            // 리롤 버튼은 수치 바로 아래 (4안 A). 패널 맨 아래 예전 버튼 자리는 일부러 비워 둔다: 손에 익은 자리를 눌러도 코인을 쓰지 않게.
            // 크기는 구매 버튼과 같다 (높이 62, 글자 22, 아래 줄 13). 높이 92·글자 38 일 때는 페이지에서 혼자 너무 컸다
            const float rollTop = 294, rollHeight = 62;
            var roll = UiKit.Rect(panel, "Roll", 16, rollTop, w - 32, rollHeight);
            rollFill = UiKit.Sprite(roll, null, Color.white);
            rollOutline = UiKit.Outline(roll, Color.clear);
            rollLabel = UiKit.Label(roll, "Label", (w - 32) / 2, 23, w - 48, locale.Get("ui.rollButton"), 22, UiKit.Dark, TextAlignmentOptions.Center, FontStyles.Bold);
            rollSub = UiKit.Label(roll, "Sub", (w - 32) / 2, 46, w - 48, "", 13, UiKit.Dark, TextAlignmentOptions.Center, FontStyles.Bold);
            rollCoin = CoinImage(roll, 46);
            rollButton = UiKit.Button(rollFill);
            SetLook(rollFill, rollOutline, rollLabel, rollSub, rollButton, ButtonLook.Disabled);
            rollButton.onClick.AddListener(() =>
            {
                if (spinning.Count == 0)
                {
                    RollClicked?.Invoke();
                }
            });

            // 리롤·잠금·넣기/빼기가 실패했을 때만 버튼 바로 아래 한 줄. 리롤할 수 없는 이유(코인 부족, 리롤할 슬롯 없음 등)는
            // 꺼진 버튼과 슬롯·코인 수로 보이므로 적지 않는다. 자리를 늘 비워 두어 메시지가 떠도 아래 칸이 움직이지 않는다
            messageLabel = UiKit.Label(panel, "Message", 18, rollTop + rollHeight + 24, w - 36, "", 15, UiKit.Bad);

            // 선택한 특전 칸: 패널 안의 두 번째 머리띠 (메시지 자리 아래 고정)
            const float pickTop = rollTop + rollHeight + 40;
            UiKit.Image(UiKit.Rect(panel, "Line2", 1, pickTop - 1, w - 2, 1), UiKit.Line);
            var pick = UiKit.Rect(panel, "Picked", 0, pickTop, w, PanelHead + 240);
            Head(pick, 0, w, locale.Get("ui.selected"));
            BuildPicked(pick, w);
        }

        /// <summary>버튼 아래 줄 앞의 작은 코인 그림 (자리와 폭은 <see cref="CoinLine"/> 이 정한다). centerY 는 세로 가운데.</summary>
        private Image CoinImage(RectTransform parent, float centerY)
        {
            var image = UiKit.Sprite(UiKit.Rect(parent, "Coin", 0, centerY - CoinHeight / 2, CoinHeight, CoinHeight), coinIcon.sprite, Color.white);
            image.gameObject.SetActive(false);
            return image;
        }

        private void BuildPicked(RectTransform pick, float w)
        {
            var top = PanelHead;
            picked = new Picked { Width = w, Body = UiKit.Rect(pick, "Body", 0, top, w, 240) };
            var body = picked.Body;
            picked.Empty = UiKit.Label(body, "Empty", w / 2, 90, w - 32, locale.Get("ui.selectNone"), 16, UiKit.Muted, TextAlignmentOptions.Center);

            (picked.Square, picked.Icon) = IconSquare(body, "Square", 16, 16, 50);
            picked.Name = UiKit.Label(body, "Name", 80, 30, w - 96 - 130, "", 20, UiKit.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            picked.Status = UiKit.Label(body, "Status", w - 16, 30, 130, "", 14, UiKit.Muted, TextAlignmentOptions.Right);
            picked.StatusLock = UiKit.Sprite(UiKit.Rect(body, "StatusLock", 0, 23.5f, 13, 13), PerkSprites.Lock, Color.white);
            picked.StatusMark = Diamond(body, "StatusMark", 0, 30, 7);
            picked.Subject = UiKit.Label(body, "Subject", 80, 54, w - 96, "", 14, UiKit.Muted);

            picked.Value = UiKit.Label(body, "Value", 16, 100, 160, "", 30, UiKit.Green, TextAlignmentOptions.Left, FontStyles.Bold);
            picked.Tier = UiKit.Label(body, "Tier", 16, 103, 120, "", 15, UiKit.Muted);
            picked.Next = UiKit.Label(body, "Next", w - 16, 103, 160, "", 15, UiKit.Muted, TextAlignmentOptions.Right);
            picked.Hint = UiKit.Label(UiKit.Rect(body, "Hint", 16, 124, w - 32, 40), "", 14, UiKit.Muted, TextAlignmentOptions.TopLeft);
            picked.Hint.enableWordWrapping = true;

            picked.Buy = UiKit.Rect(body, "Buy", 16, 166, w - 32, 62);
            picked.BuyFill = UiKit.Sprite(picked.Buy, null, Color.white);
            picked.BuyOutline = UiKit.Outline(picked.Buy, Color.clear);
            picked.BuyLabel = UiKit.Label(picked.Buy, "Label", (w - 32) / 2, 23, w - 48, "", 22, UiKit.Dark, TextAlignmentOptions.Center, FontStyles.Bold);
            picked.BuySub = UiKit.Label(picked.Buy, "Sub", (w - 32) / 2, 46, w - 48, "", 13, UiKit.Dark, TextAlignmentOptions.Center);
            picked.BuyCoin = CoinImage(picked.Buy, 46);
            picked.BuyButton = UiKit.Button(picked.BuyFill);
            picked.BuyButton.onClick.AddListener(OnBuyClicked);

            // 구매 실패 문구는 구매 버튼 바로 아래 (글줄 가운데 = 버튼 아래 + 20). 길면 아래로 줄을 바꾼다
            picked.BuyMessage = UiKit.Label(UiKit.Rect(body, "BuyMessage", 16, 248 - 8, w - 32, 48), "", 15, UiKit.Bad, TextAlignmentOptions.TopLeft);
            picked.BuyMessage.enableWordWrapping = true;
            picked.BuyMessage.overflowMode = TextOverflowModes.Overflow;
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
                coinIcon.sprite = rollCoin.sprite = picked.BuyCoin.sprite = sprite;
                // 버튼 아래 줄의 코인 폭은 그림 비율을 따르므로 다시 맞춘다
                if (lastState != null)
                {
                    Rerender();
                }
            }
        }

        private static void OnHover(GameObject target, Action<bool> changed)
        {
            On(target, EventTriggerType.PointerEnter, _ => changed(true));
            On(target, EventTriggerType.PointerExit, _ => changed(false));
        }

        /// <summary>마우스 이벤트 하나를 단다. 한 오브젝트의 EventTrigger 하나에 모은다.</summary>
        private static void On(GameObject target, EventTriggerType type, Action<PointerEventData> handler)
        {
            var trigger = target.GetComponent<EventTrigger>() ?? target.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(data => handler(data as PointerEventData));
            trigger.triggers.Add(entry);
        }

        // 선택한 줄은 흰 테두리, 리롤 대상은 얇은 금색 테두리, 마우스를 올리면 밝은 테두리
        private static void RefreshOutline(Row row) =>
            row.Outline.color = row.Selected ? SelectedLine : row.Target ? UiKit.Gold : row.Hover ? HoverLine : Color.clear;

        // 슬롯에 마우스를 올리면 표시와 육각 틀이 밝아진다 (리롤 칸·잠긴 칸은 누르면 잠금 토글, 확정 칸은 선택). 드래그 중에는 놓을 곳 표시가 우선이다
        private void RefreshLock(Reel reel)
        {
            var hover = reel.LockHover && reel.LockButton.interactable && reel.Kind != SlotKind.Empty;
            // 잠근 칸은 닫힌 자물쇠, 리롤 칸은 풀린 자물쇠 (흐리게, 마우스를 올리면 밝게)
            reel.LockIcon.sprite = reel.Locked ? PerkSprites.Lock : PerkSprites.Unlock;
            reel.LockIcon.color = new Color(1, 1, 1, reel.Locked ? 1f : hover ? 0.8f : 0.45f);
            var index = reels.IndexOf(reel);
            if (dragPerk > 0)
            {
                // 놓을 수 있는 칸은 흰 테두리, 마우스가 올라간 칸은 더 밝게
                reel.Hover.color = PerkPageModel.CanDrop(lastState, index) ? new Color(1, 1, 1, index == dropReel ? 1f : 0.45f) : Color.clear;
                return;
            }

            reel.Hover.color = hover && dragSlot < 0 ? new Color(1, 1, 1, 0.45f) : Color.clear;
        }

        private static void SetEquipped(Row row, bool equipped)
        {
            row.Glow.enabled = equipped;
            row.Icon.color = new Color(1, 1, 1, equipped ? 1f : 0.5f);
            row.Name.color = equipped ? UiKit.Text : UiKit.Muted;
            row.Effect.color = equipped ? UiKit.Green : UiKit.GreenDim;
        }

        internal static string TierName(int tier) => tier == 1 ? "I" : tier == 2 ? "II" : tier == 3 ? "III" : "";

        /// <summary>
        /// 상태를 그린다. 돌고 있는 릴의 아이콘·이름은 연출이 맡는다.
        /// message 는 리롤 버튼 아래 (리롤·잠금·넣기/빼기·통신), buyMessage 는 선택한 특전 칸 구매 버튼 아래 (구매 실패).
        /// </summary>
        public void Render(PerkStateDto state, int coins, bool busy, string message, string buyMessage = null)
        {
            lastState = state;
            lastCoins = coins;
            lastBusy = busy;
            lastMessage = message;
            lastBuyMessage = buyMessage;
            if (state == null)
            {
                levelLabel.text = locale.Get("ui.loading");
                SetLook(rollFill, rollOutline, rollLabel, rollSub, rollButton, ButtonLook.Disabled);
                rollLabel.text = locale.Get("ui.rollButton");
                CoinLine(rollSub, rollCoin, "", false, false);
                messageLabel.text = message ?? "";
                RenderPicked(null, coins, busy);
                return;
            }

            var level = $"<size=24><b><color=#{ColorUtility.ToHtmlStringRGB(UiKit.Gold)}>{state.PrestigeLevel}</color></b></size>";
            var tierCap = $"<b><color=#{ColorUtility.ToHtmlStringRGB(UiKit.Text)}>{TierName(PerkPageModel.TierCap(state))}</color></b>";
            var collection = $"<b><color=#{ColorUtility.ToHtmlStringRGB(UiKit.Text)}>{PerkPageModel.OwnedCount(state)}</color></b>";
            levelLabel.text = locale.Format("ui.level", level, tierCap, collection, PerkPageModel.AllPerks.Length);
            coinsValue.text = coins.ToString();

            foreach (var pair in rows)
            {
                RenderRow(pair.Key, pair.Value, state);
            }

            RenderListView(state);

            for (var i = 0; i < reels.Count; i++)
            {
                RenderReel(i, state, busy);
            }

            freeValue.text = $"{state.FreeRerollsLeft} / {state.FreeRerolls}";
            lockedValue.text = $"{PerkPageModel.LockedCount(state)} / {state.Slots.Count}";
            SetDots(freeDots, freeDotsStart, freeValue);
            SetDots(lockedDots, lockedDotsStart, lockedValue);

            rollLabel.text = locale.Get("ui.rollButton");
            var block = PerkPageModel.CheckRoll(state, coins, busy);
            var rollEnabled = block == RollBlock.None;
            SetLook(rollFill, rollOutline, rollLabel, rollSub, rollButton, rollEnabled ? ButtonLook.Roll : ButtonLook.Disabled);
            // 버튼 아래 줄: 도는 중, 무료, 또는 코인 그림 + 다음 비용 (고정이면 표시)
            if (spinning.Count > 0)
            {
                CoinLine(rollSub, rollCoin, locale.Get("ui.rolling"), false, false);
            }
            else if (state.NextRerollCost == 0)
            {
                CoinLine(rollSub, rollCoin, locale.Get("ui.nextFree"), false, false);
            }
            else
            {
                CoinLine(rollSub, rollCoin, state.NextRerollCost + (state.CostFixed ? locale.Get("ui.fixed") : ""), true, rollEnabled);
            }

            messageLabel.color = UiKit.Bad;
            messageLabel.text = message ?? "";
            RenderPicked(state, coins, busy);
            RenderDragMarks();
        }

        private void Rerender() => Render(lastState, lastCoins, lastBusy, lastMessage, lastBuyMessage);

        /// <summary>
        /// 버튼 아래 줄. withCoin 이면 코인 그림(높이 CoinHeight, 폭은 그림 비율) + CoinGap + 글자를 한 묶음으로 가운데에 둔다.
        /// 글자 칸은 가운데 정렬 그대로 두고 왼쪽 여백만 그림 자리만큼 준다. 꺼진 버튼이면 그림도 흐리게.
        /// </summary>
        private static void CoinLine(TextMeshProUGUI sub, Image coin, string text, bool withCoin, bool enabled)
        {
            sub.text = text;
            sub.margin = Vector4.zero;
            coin.gameObject.SetActive(withCoin);
            if (!withCoin)
            {
                return;
            }

            var sprite = coin.sprite;
            var coinWidth = sprite != null && sprite.rect.height > 0 ? CoinHeight * sprite.rect.width / sprite.rect.height : CoinHeight;
            var textWidth = UiKit.Width(sub, text);
            sub.margin = new Vector4(coinWidth + CoinGap, 0, 0, 0);
            var center = sub.rectTransform.anchoredPosition.x + sub.rectTransform.sizeDelta.x / 2;
            coin.rectTransform.sizeDelta = new Vector2(coinWidth, CoinHeight);
            coin.rectTransform.anchoredPosition = new Vector2(center - (coinWidth + CoinGap + textWidth) / 2, coin.rectTransform.anchoredPosition.y);
            coin.color = new Color(1, 1, 1, enabled ? 1f : 0.35f);
        }

        private void RenderRow(int perk, Row row, PerkStateDto state)
        {
            var slot = PerkPageModel.SlotOf(state, perk);
            // 돌고 있는 슬롯에 있던 특전은 잠시 장착 표시를 비운다
            var equipped = slot >= 0 && !spinning.ContainsKey(slot);
            SetEquipped(row, equipped);
            row.Target = equipped && PerkPageModel.KindOf(state, slot) == SlotKind.Roll;
            row.Selected = perk == selectedPerk;
            RefreshOutline(row);

            var shown = PerkPageModel.ShownTier(state, perk);
            row.Effect.text = locale.PerkEffect(perk, PerkPageModel.EffectValue(state, perk, shown));
            // 단계 칸: 흐림 = 미보유, 밝음 = 보유, 초록 테두리 = 지금 적용 중
            var owned = PerkPageModel.OwnedTier(state, perk);
            var applied = equipped ? shown : 0;
            for (var t = 1; t <= PerkPageModel.MaxTier; t++)
            {
                var on = t <= owned;
                row.TierFill[t - 1].color = on ? TierOnFill : TierOffFill;
                row.TierLine[t - 1].color = t == applied ? UiKit.Green : on ? TierOnLine : TierOffLine;
                row.TierLabel[t - 1].color = t == applied && !on ? UiKit.Green : on ? TierOnText : TierOffText;
            }
        }

        private void RenderReel(int index, PerkStateDto state, bool busy)
        {
            var reel = reels[index];
            reel.Kind = PerkPageModel.KindOf(state, index);
            reel.Open = reel.Kind != SlotKind.Closed;
            reel.LockButton.gameObject.SetActive(reel.Open);
            reel.LockIcon.gameObject.SetActive(reel.Open && (reel.Kind == SlotKind.Roll || reel.Kind == SlotKind.Locked));
            reel.FixedMark.gameObject.SetActive(reel.Kind == SlotKind.Fixed);
            if (!reel.Open)
            {
                // 아직 닫힌 슬롯: 흐린 빈 육각에 "P{n}에서 열림"
                reel.Header.color = UiKit.Dim;
                reel.Glow.enabled = false;
                reel.Frame.color = new Color(1, 1, 1, 0.35f);
                reel.Fill.color = new Color(1, 1, 1, 0.6f);
                reel.FaceIcon.gameObject.SetActive(false);
                reel.TierBox.gameObject.SetActive(false);
                reel.Closed.text = PerkPageModel.OpensAt(state, index) is int level ? locale.Format("ui.opensAt", level) : locale.Get("ui.slotClosed");
                reel.Name.text = "";
                reel.Effect.text = "";
                return;
            }

            var slot = state.Slots[index];
            var blue = reel.Kind == SlotKind.Locked || reel.Kind == SlotKind.Fixed;
            reel.Header.color = UiKit.Muted;
            reel.Fill.color = Color.white;
            reel.Closed.text = "";
            reel.Locked = slot.Locked;
            reel.CurrentPerk = slot.Perk;
            // 잠근 칸·확정 칸은 푸른 틀과 빛, 리롤 칸·빈 칸은 금색 틀 (한 겹 선)
            reel.Glow.enabled = blue;
            reel.Frame.color = blue ? LockedFrame : UiKit.Gold;
            reel.FaceIcon.color = blue ? LockedIcon : TargetIcon;
            foreach (var row in reel.Rows)
            {
                row.Icon.color = reel.FaceIcon.color;
            }

            var headerWidth = UiKit.Width(reel.Header, reel.Header.text);
            var markX = reel.Box.rect.width / 2 + headerWidth / 2 + 6;
            reel.LockIcon.rectTransform.anchoredPosition = new Vector2(markX, reel.LockIcon.rectTransform.anchoredPosition.y);
            reel.FixedMark.rectTransform.anchoredPosition = new Vector2(markX + 4, reel.FixedMark.rectTransform.anchoredPosition.y);
            reel.LockButton.interactable = !busy;
            RefreshLock(reel);

            // 단계 배지: 빈 칸과 도는 칸은 비운다
            var showTier = !slot.IsEmpty && !spinning.ContainsKey(index);
            reel.TierBox.gameObject.SetActive(showTier);
            if (showTier)
            {
                reel.TierLabel.text = TierName(PerkPageModel.ShownTier(state, slot.Perk));
                reel.TierLine.color = reel.Frame.color;
                reel.TierLabel.color = blue ? LockedIcon : TargetIcon;
            }

            if (spinning.ContainsKey(index))
            {
                return;
            }

            ShowFace(reel, slot.Perk);
        }

        private void ShowFace(Reel reel, int perk)
        {
            if (perk <= 0)
            {
                HideFace(reel);
                return;
            }

            reel.FaceIcon.gameObject.SetActive(true);
            reel.FaceIcon.sprite = PerkSprites.Icon(perk);
            reel.Name.text = locale.PerkName(perk);
        }

        private static void HideFace(Reel reel)
        {
            reel.FaceIcon.gameObject.SetActive(false);
            reel.Name.text = "";
            reel.Effect.text = "";
        }

        /// <summary>오른쪽 "선택한 특전" 칸. 안내 한 줄과 코인을 쓰는 버튼 하나 (넣기·빼기는 드래그).</summary>
        private void RenderPicked(PerkStateDto state, int coins, bool busy)
        {
            var pick = PerkPageModel.Select(state, selectedPerk, coins);
            var none = pick.Kind == SelectionKind.None;
            picked.Empty.gameObject.SetActive(none);
            foreach (Transform child in picked.Body)
            {
                if (child != picked.Empty.transform)
                {
                    child.gameObject.SetActive(!none);
                }
            }

            if (none)
            {
                return;
            }

            var perk = pick.Perk;
            var equipped = pick.Slot >= 0;
            picked.Icon.sprite = PerkSprites.Icon(perk);
            picked.Icon.color = new Color(1, 1, 1, equipped ? 1f : 0.5f);
            picked.Name.text = locale.PerkName(perk);
            picked.Subject.text = locale.PerkSubject(perk);

            // 오른쪽 위는 슬롯 상태만: "슬롯 n" (잠금이면 자물쇠, 확정이면 ◆) 또는 장착 안 됨. 보유 여부는 아래 안내 줄
            picked.Status.text = equipped ? locale.Format("ui.slotN", pick.Slot + 1) : locale.Get("ui.notEquipped");
            // 표시는 슬롯 머리줄처럼 글자 오른쪽에 둔다: 표시 자리만큼 글자를 왼쪽으로 민다
            var locked = pick.Kind == SelectionKind.LockedSlot;
            var fixedSlot = pick.Kind == SelectionKind.Fixed;
            var markSpace = locked ? 20 : fixedSlot ? 16 : 0;
            picked.Status.margin = new Vector4(0, 0, markSpace, 0);
            var textRight = picked.Width - 16 - markSpace;
            picked.StatusLock.gameObject.SetActive(locked);
            picked.StatusLock.rectTransform.anchoredPosition = new Vector2(textRight + 6, picked.StatusLock.rectTransform.anchoredPosition.y);
            picked.StatusMark.gameObject.SetActive(fixedSlot);
            picked.StatusMark.rectTransform.anchoredPosition = new Vector2(textRight + 10, picked.StatusMark.rectTransform.anchoredPosition.y);

            picked.Value.text = PerkPageModel.EffectValue(state, perk, pick.Tier);
            picked.Value.color = equipped ? UiKit.Green : UiKit.GreenDim;
            picked.Tier.text = locale.Format("ui.tier", TierName(pick.Tier));
            picked.Tier.rectTransform.anchoredPosition = new Vector2(16 + UiKit.Width(picked.Value, picked.Value.text) + 10, picked.Tier.rectTransform.anchoredPosition.y);
            // 다음 단계 값 (II·III 구매가 남았을 때)
            picked.Next.text = pick.NextTier > 1 ? locale.Format("ui.nextTier", PerkPageModel.EffectValue(state, perk, pick.NextTier)) : "";

            // 안내 줄은 보유 여부만 보인다
            var owned = pick.Kind == SelectionKind.Fixed || pick.Kind == SelectionKind.Owned;
            picked.Hint.text = locale.Get(owned ? "ui.hintOwned" : "ui.notOwned");
            picked.BuyMessage.text = lastBuyMessage ?? "";

            picked.Buy.gameObject.SetActive(pick.NextTier > 0);
            if (pick.NextTier == 0)
            {
                return;
            }

            picked.BuyLabel.text = pick.NextTier == 1 ? locale.Get("ui.keep")
                : locale.Format("ui.buyTier", TierName(pick.NextTier));
            var enabled = pick.Block == BuyBlock.None && !busy && spinning.Count == 0;
            SetLook(picked.BuyFill, picked.BuyOutline, picked.BuyLabel, picked.BuySub, picked.BuyButton,
                enabled ? ButtonLook.Roll : ButtonLook.Disabled);
            // 아래 줄: 코인 그림 + 가격. 단계 상한에 막히면 열리는 프레스티지만
            if (pick.Block == BuyBlock.Capped && pick.OpensAt is int at)
            {
                picked.BuySub.fontStyle = FontStyles.Normal;
                CoinLine(picked.BuySub, picked.BuyCoin, locale.Format("ui.availableAt", at), false, false);
            }
            else
            {
                picked.BuySub.fontStyle = FontStyles.Bold;
                CoinLine(picked.BuySub, picked.BuyCoin, pick.Price.ToString(), true, enabled);
            }
        }

        private static void SetLook(Image fill, Image outline, TextMeshProUGUI label, TextMeshProUGUI sub, Button button, ButtonLook look)
        {
            switch (look)
            {
                case ButtonLook.Roll:
                    // 타르코프 기본 버튼: 조금 어두운 회색 그라데이션에 어두운 글자
                    fill.sprite = PerkSprites.Gradient(ButtonTop, ButtonBottom);
                    outline.color = ButtonLine;
                    label.color = UiKit.Dark;
                    sub.color = new Color32(30, 34, 36, 255);
                    break;
                default:
                    fill.sprite = PerkSprites.Gradient(DarkTop, DarkBottom);
                    outline.color = UiKit.Line;
                    label.color = sub.color = UiKit.Dim;
                    break;
            }

            button.interactable = look != ButtonLook.Disabled;
        }

        public void ShowStatus(string text, Color color)
        {
            messageLabel.text = text;
            messageLabel.color = color;
        }

        // ---- 선택·구매 ----

        private bool JustDragged => Time.frameCount - dragEndFrame <= 1;

        private void OnRowClicked(int perk)
        {
            if (JustDragged)
            {
                return;
            }

            Select(perk);
        }

        private void OnSlotClicked(int index)
        {
            if (JustDragged || lastState == null)
            {
                return;
            }

            switch (PerkPageModel.KindOf(lastState, index))
            {
                case SlotKind.Fixed:
                    // 확정 칸은 잠글 필요가 없어 누르면 그 특전을 고른다
                    Select(lastState.Slots[index].Perk);
                    break;
                case SlotKind.Roll:
                case SlotKind.Locked:
                    LockClicked?.Invoke(index);
                    break;
            }
        }

        private void Select(int perk)
        {
            Pick(perk);
            GameSounds(EUISoundType.ButtonOver);
            Rerender();
        }

        // 구매 메시지는 고른 특전에 대한 것이라 다른 특전을 고르면 지운다
        private void Pick(int perk)
        {
            if (perk != selectedPerk)
            {
                lastBuyMessage = null;
            }

            selectedPerk = perk;
        }

        // 누르면 바로 산다. 서버가 목표 단계를 받으므로 연달아 눌러도 한 번만 산다
        private void OnBuyClicked()
        {
            var pick = PerkPageModel.Select(lastState, selectedPerk, lastCoins);
            if (pick.NextTier == 0 || pick.Block != BuyBlock.None || lastBusy || spinning.Count > 0)
            {
                return;
            }

            BuyClicked?.Invoke(pick.Perk, pick.NextTier, pick.Price);
        }

        // ---- 드래그 ----

        private bool CanStartDrag => lastState != null && !lastBusy && spinning.Count == 0;

        private void BeginRowDrag(int perk, PointerEventData data)
        {
            if (!CanStartDrag || !PerkPageModel.CanDragRow(lastState, perk))
            {
                return;
            }

            dragPerk = perk;
            Pick(perk);
            BeginGhost(perk, data);
        }

        private void BeginSlotDrag(int index, PointerEventData data)
        {
            if (!CanStartDrag || !PerkPageModel.CanDragSlot(lastState, index))
            {
                return;
            }

            dragSlot = index;
            Pick(lastState.Slots[index].Perk);
            BeginGhost(selectedPerk, data);
        }

        // 작은 카드 (아이콘, 이름, 값 · 단계)가 마우스를 따라온다
        private void BeginGhost(int perk, PointerEventData data)
        {
            var tier = PerkPageModel.ShownTier(lastState, perk);
            ghost = UiKit.Rect(root, "Ghost", 0, 0, 210, 52);
            ghost.anchorMin = ghost.anchorMax = new Vector2(0.5f, 0.5f);
            var group = ghost.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            UiKit.Image(ghost, new Color32(17, 21, 23, 240));
            UiKit.Outline(ghost, SelectedLine);
            var (_, icon) = IconSquare(ghost, "Square", 7, 7, 38);
            icon.sprite = PerkSprites.Icon(perk);
            UiKit.Label(ghost, "Name", 54, 17, 150, locale.PerkName(perk), 15, UiKit.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            UiKit.Label(ghost, "Value", 54, 36, 150, $"{PerkPageModel.EffectValue(lastState, perk, tier)}  ·  {locale.Format("ui.tier", TierName(tier))}", 12, UiKit.Green);
            GameSounds(EUISoundType.MenuInstallModFunc);
            MoveDrag(data);
        }

        private void MoveDrag(PointerEventData data)
        {
            if (ghost == null || data == null)
            {
                return;
            }

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(root, data.position, null, out var local))
            {
                ghost.anchoredPosition = local + new Vector2(14, -14);
            }

            var reel = dragPerk > 0 ? ReelAt(data.position) : -1;
            var list = dragSlot >= 0 && RectTransformUtility.RectangleContainsScreenPoint(listPanel, data.position, null);
            if (reel != dropReel || list != overList)
            {
                dropReel = reel;
                overList = list;
                RenderDragMarks();
            }
        }

        private void EndDrag(PointerEventData data)
        {
            if (ghost == null)
            {
                return;
            }

            var perk = dragPerk;
            var slot = dragSlot;
            var target = data != null && perk > 0 ? ReelAt(data.position) : -1;
            var toList = data != null && slot >= 0 && RectTransformUtility.RectangleContainsScreenPoint(listPanel, data.position, null);
            CancelDrag();
            dragEndFrame = Time.frameCount;

            // 잠긴 칸·닫힌 칸에는 놓을 수 없다. 확정 칸은 목록 어디에 놓아도 빠진다. 확인 창은 없다
            if (perk > 0 && target >= 0 && PerkPageModel.CanDrop(lastState, target))
            {
                EquipDropped?.Invoke(perk, target);
            }
            else if (slot >= 0 && toList)
            {
                UnequipDropped?.Invoke(slot);
            }
            else
            {
                Rerender();
            }
        }

        private void CancelDrag()
        {
            if (ghost != null)
            {
                Destroy(ghost.gameObject);
                ghost = null;
            }

            dragPerk = 0;
            dragSlot = -1;
            dropReel = -1;
            overList = false;
            RenderDragMarks();
        }

        private int ReelAt(Vector2 screen)
        {
            for (var i = 0; i < reels.Count; i++)
            {
                if (reels[i].Open && RectTransformUtility.RectangleContainsScreenPoint(reels[i].Box, screen, null))
                {
                    return i;
                }
            }

            return -1;
        }

        // 끄는 동안: 놓을 수 있는 칸 흰 테두리, 잠긴 칸 어둡게, 원래 줄·칸 흐리게. 확정 칸을 끌면 목록 전체 테두리와 "여기에 놓으면 빠집니다"
        private void RenderDragMarks()
        {
            if (lastState == null)
            {
                return;
            }

            for (var i = 0; i < reels.Count; i++)
            {
                var reel = reels[i];
                var dim = (dragPerk > 0 && reel.Open && !PerkPageModel.CanDrop(lastState, i)) || i == dragSlot;
                reel.Group.alpha = dim ? 0.35f : 1f;
                RefreshLock(reel);
            }

            foreach (var pair in rows)
            {
                pair.Value.Group.alpha = pair.Key == dragPerk ? 0.4f : 1f;
            }

            listDropLabel.enabled = dragSlot >= 0;
            listDropOutline.color = dragSlot >= 0 ? new Color(SelectedLine.r, SelectedLine.g, SelectedLine.b, overList ? 1f : 0.55f) : Color.clear;
        }

        // ---- 릴 ----

        /// <summary>릴을 돌리기 시작한다 (서버 결과가 오기 전부터 빨리 돈다). 지금 특전에서 출발한다. 띠에는 리롤 후보(미보유)만 흐른다.</summary>
        public void StartSpin(IEnumerable<int> slots)
        {
            CancelDrag();
            var now = Time.unscaledTime;
            var pool = PerkPageModel.RollPool(lastState);
            IReadOnlyList<int> strip = pool.Count >= 3 ? pool : PerkPageModel.AllPerks;
            foreach (var slot in slots)
            {
                var reel = reels[slot];
                var spin = new Spin { Started = now, Pool = strip };
                ReelSchedule.Fill(spin.Strip, 2, random, strip, reel.CurrentPerk);
                spin.Strip.Add(reel.CurrentPerk);
                spin.Pos = spin.LastPos = 2;
                spinning[slot] = spin;
                HideFace(reel);
                reel.TierBox.gameObject.SetActive(false);
                DrawStrip(reel, spin);
            }

            lastBuyMessage = null;
            SetLook(rollFill, rollOutline, rollLabel, rollSub, rollButton, ButtonLook.Disabled);
            CoinLine(rollSub, rollCoin, locale.Get("ui.rolling"), false, false);
            GameSounds(EUISoundType.MenuCheckBox);
        }

        /// <summary>결과가 왔다. delay 초 뒤부터 감속해 perk 에서 멈춘다 (0 이면 빈 칸). effect 는 멈춤 연출 (<see cref="ReelEffect"/>).</summary>
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

            foreach (var reel in reels)
            {
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
                if (ghost != null)
                {
                    CancelDrag();
                    Rerender();
                }
                else
                {
                    CloseClicked?.Invoke();
                }
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
                    ReelSchedule.Fill(spin.Strip, Mathf.CeilToInt(spin.Pos) + 3, random, spin.Pool, -1);
                    if (now >= spin.LandAt)
                    {
                        (spin.Result, spin.Stop) = ReelSchedule.Extend(spin.Strip, Mathf.CeilToInt(spin.Pos), spin.Target, spin.Effect, random, spin.Pool, spin.Held);
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
                            return Finish(reel, spin, now, EUISoundType.MenuInstallMag);
                        }

                        // 결과처럼 멈춘다. 잠시 뒤 갑자기 거꾸로 돌거나(역회전), 결과 칸으로 끌려 올라간다(가짜 멈춤)
                        Begin(spin, spin.Effect == ReelEffect.Reverse ? SpinPhase.ReverseHold : SpinPhase.FakeHold, now, spin.Pos, spin.Pos,
                            spin.Effect == ReelEffect.Reverse ? ReelSchedule.ReverseHold : ReelSchedule.FakeHold);
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
                        Begin(spin, SpinPhase.SnapBack, now, spin.Stop, spin.Result, ReelSchedule.SnapBackTime);
                        GameSounds(EUISoundType.MenuInstallModFunc);
                    }

                    break;
                case SpinPhase.ReverseHold:
                    if (now - spin.PhaseStart >= spin.Duration)
                    {
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
                        return Finish(reel, spin, now, EUISoundType.MenuWeaponAssemble);
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

        // 위치 Pos 의 칸이 가운데. 다음 칸은 위, 지난 칸은 아래에 비친다. 빈 칸(0)은 그림 없이 지나간다
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
                var inStrip = index >= 0 && index < spin.Strip.Count && spin.Strip[index] > 0;
                row.Group.alpha = inStrip ? Mathf.Lerp(0.25f, bright, Mathf.Clamp01(1 - Mathf.Abs(offset))) : 0;
                if (inStrip)
                {
                    row.Icon.sprite = PerkSprites.Icon(spin.Strip[index]);
                }
            }

            var center = Mathf.Clamp(Mathf.RoundToInt(spin.Pos), 0, spin.Strip.Count - 1);
            reel.Name.text = spin.Strip[center] > 0 ? locale.PerkName(spin.Strip[center]) : "";
            reel.Name.color = spin.Phase == SpinPhase.Fast ? UiKit.Muted : UiKit.Text;
        }

        private static void HideStrip(Reel reel) => reel.Strip.gameObject.SetActive(false);

        private static void GameSounds(EUISoundType sound) => PerkGame.Sound(sound);
    }
}
