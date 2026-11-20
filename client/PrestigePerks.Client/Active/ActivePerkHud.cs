using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using EFT.UI;
using PrestigePerks.Game;
using PrestigePerks.Model;
using PrestigePerks.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PrestigePerks.Active
{
    /// <summary>
    /// 레이드 화면 오른쪽 아래의 액티브 특전 칸 (목업 작업파일\mock8_active_hud.py, 1920×1080 기준 값). 특전 페이지와 같은 육각 배지를 쓴다.
    /// 오른쪽 끝 기준이라 21:9 에서도 화면 오른쪽에 붙는다. 글자는 모양·남은 초(정수)가 바뀔 때만 바꾼다 (매 프레임 문자열을 만들지 않는다).
    /// 사용 연출 (dev-docs 6, 목업 7안_액티브연출): 모양이 바뀌는 순간(HudCue)에 화면 가장자리 빛·육각 링을, 지속 중에는 칸 확대·금색 강조를 보인다.
    /// 연출마다 F12 에서 끌 수 있다 (Plugin.Fx*).
    /// </summary>
    internal sealed class ActivePerkHud
    {
        private const float ReferenceHeight = 1080f;
        private const float HexWidth = 56, RightGap = 28, BottomGap = 128 + 18;
        private const float BarWidth = 96, BarHeight = 3, TextGap = 8, DenyTime = 0.5f;

        // 사용 연출 (목업 7안 확정값)
        private const float RunScale = 1.2f, GoldBarHeight = 4.5f, GoldFade = 0.4f, LineFade = 0.45f, BreathPeriod = 1.6f;
        private const float BounceTime = 0.35f, BounceScale = 1.3f, RingTime = 0.65f, RingScale = 2.2f, FlashTime = 0.9f;
        private const float BlinkPeriod = 0.5f, ReadyBlinkTime = 1f;
        private const float GlowAlpha = 0.75f;

        // 페이지의 리롤 칸 아이콘 색 (PerkPage.TargetIcon)
        private static readonly Color BrightIcon = new Color32(246, 226, 180, 255);
        private static readonly Color EdgeGold = new Color32(238, 196, 110, 255);
        private static readonly Color EdgeGray = new Color32(205, 210, 214, 255);

        private PerkLocale locale;
        private string perk;
        private GameObject root;
        private Image glow, frame, icon, sweep, barFill, keyLine, edge, ring, shadeGold, lineTop, lineBottom;
        private RectTransform panel, hex, bar, barFillRect, ringRect, lineTopRect, lineBottomRect;
        private CanvasGroup barGroup;
        private TextMeshProUGUI name, status, seconds, key;
        private HudLook? shownLook;
        private int shownSeconds = -1;
        private float denyUntil;
        private bool denyShown;
        private float barTop;

        // 연출 상태: 시작 시각 (Time.time), 아직 안 했으면 음수 무한대
        private float edgeStart = float.NegativeInfinity, bounceStart = float.NegativeInfinity, ringStart = float.NegativeInfinity;
        private float flashStart = float.NegativeInfinity, readyBlinkStart = float.NegativeInfinity, runStart;
        private bool edgeIsGold;
        private float panelScale = 1f, gold, lines;
        private bool endingLogged;

        public static ActivePerkHud Create(PerkLocale locale, string perk, int tier)
        {
            var hud = new ActivePerkHud { locale = locale, perk = perk };
            hud.Build(tier);
            return hud;
        }

        private void Build(int tier)
        {
            root = new GameObject("PrestigePerksActiveHud", typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // 전투 HUD 위 (게임 확인)
            canvas.sortingOrder = 100;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = Screen.height / ReferenceHeight;

            var width = Screen.width / scaler.scaleFactor;
            var hexHeight = Mathf.Round(HexWidth * 436 / 376);
            var hexX = width - RightGap - HexWidth;
            var hexY = ReferenceHeight - BottomGap - hexHeight;
            var middle = hexY + hexHeight / 2;

            // 화면 가장자리 빛: 칸 뒤에 화면 전체로 깐다. 평소에는 꺼 둔다
            edge = UiKit.Sprite(UiKit.Rect(root.transform, "EdgeLight", 0, 0, width, ReferenceHeight), PerkSprites.EdgeLight(0.58f, 0.82f, 0.22f, 0.5f), Color.clear);
            edge.enabled = false;

            // 칸 전체를 담는 판. 지속 중 확대는 칸 오른쪽 아래(키 칸 아래 끝)를 기준으로 커진다
            panel = UiKit.Rect(root.transform, "Panel", 0, 0, width, ReferenceHeight);
            var pivotX = hexX + HexWidth + 4;
            var pivotY = hexY + hexHeight + 10;
            panel.pivot = new Vector2(pivotX / width, 1 - pivotY / ReferenceHeight);
            panel.anchoredPosition = new Vector2(pivotX, -pivotY);
            var parent = panel.transform;

            // 그늘: 육각 왼쪽 190 부터 육각 가운데까지, 오른쪽으로 갈수록 짙다
            var shadeX = hexX - 190;
            var shadeY = hexY - 4;
            var shadeWidth = 190 + HexWidth / 2;
            var shadeHeight = hexHeight + 18;
            UiKit.Sprite(UiKit.Rect(parent, "Shade", shadeX, shadeY, shadeWidth, shadeHeight),
                PerkSprites.Gradient(new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 158), true), Color.white);
            // 지속 중 금색 강조: 금빛 그늘 + 위아래 금색 선 (오른쪽에서 펼쳐진다)
            shadeGold = UiKit.Sprite(UiKit.Rect(parent, "ShadeGold", shadeX, shadeY, shadeWidth, shadeHeight),
                PerkSprites.Gradient(new Color32(238, 196, 110, 0), new Color32(238, 196, 110, 51), true), Color.clear);
            var lineSprite = PerkSprites.Gradient(new Color32(238, 196, 110, 0), new Color32(238, 196, 110, 230), true);
            lineTopRect = FromRight(UiKit.Rect(parent, "LineTop", shadeX, shadeY, shadeWidth, 1.5f));
            lineTop = UiKit.Sprite(lineTopRect, lineSprite, Color.white);
            lineBottomRect = FromRight(UiKit.Rect(parent, "LineBottom", shadeX, shadeY + shadeHeight - 1.5f, shadeWidth, 1.5f));
            lineBottom = UiKit.Sprite(lineBottomRect, lineSprite, Color.white);

            // 육각 묶음: 가운데를 기준으로 튄다
            hex = Centered(UiKit.Rect(parent, "Hex", hexX, hexY, HexWidth, hexHeight));
            glow = UiKit.Sprite(UiKit.Rect(hex, "Glow", -HexWidth * 0.15f, -hexHeight * 0.15f, HexWidth * 1.3f, hexHeight * 1.3f),
                PerkSprites.HexGlow, Faded(UiKit.GoldHi, GlowAlpha));
            glow.enabled = false;
            var fill = UiKit.Rect(hex, "Fill", 0, 0, HexWidth, hexHeight);
            UiKit.Sprite(fill, PerkSprites.HexFill, Color.white);
            var iconSize = Mathf.Round(HexWidth * 0.6f);
            icon = UiKit.Sprite(UiKit.Rect(fill, "Icon", (HexWidth - iconSize) / 2, (hexHeight - iconSize) / 2, iconSize, iconSize), PerkSprites.Icon(perk), Color.white);
            icon.preserveAspect = true;

            // 쿨타임: 남은 만큼 어두운 부채꼴 (위에서 시계 방향). 육각 바탕 그림으로 채워 육각 밖으로 나가지 않는다
            sweep = UiKit.Sprite(UiKit.Rect(hex, "Sweep", 0, 0, HexWidth, hexHeight), PerkSprites.HexFill, new Color(0, 0, 0, 0.6f), Image.Type.Filled);
            sweep.fillMethod = Image.FillMethod.Radial360;
            sweep.fillOrigin = (int)Image.Origin360.Top;
            sweep.fillClockwise = true;
            frame = UiKit.Sprite(UiKit.Rect(hex, "Frame", 0, 0, HexWidth, hexHeight), PerkSprites.HexFrame, UiKit.Gold);
            seconds = UiKit.Label(hex, "Seconds", HexWidth / 2, hexHeight / 2, HexWidth, "", 17, UiKit.Text, TextAlignmentOptions.Center, FontStyles.Bold);

            // 키 칸: 육각 아래 가운데에 걸친다
            var keyBox = UiKit.Rect(parent, "Key", hexX + (HexWidth - 44) / 2, hexY + hexHeight - 7, 44, 17);
            UiKit.Image(keyBox, UiKit.Dark);
            keyLine = UiKit.Outline(keyBox, UiKit.Line);
            key = UiKit.Label(keyBox, "Label", 22, 8.5f, 44, KeyText(Plugin.ActiveKey.Value), 12, UiKit.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            key.overflowMode = TextOverflowModes.Overflow;
            // 레이드 중에 F12 에서 키를 바꿔도 칸의 글자가 따라간다
            Plugin.ActiveKey.SettingChanged += OnKeyChanged;

            // 육각 링: 육각 틀이 커지며 흐려진다
            ringRect = Centered(UiKit.Rect(parent, "Ring", hexX, hexY, HexWidth, hexHeight));
            ring = UiKit.Sprite(ringRect, PerkSprites.HexFrame, Color.clear);
            ring.enabled = false;

            var textRight = hexX - TextGap;
            name = UiKit.Label(parent, "Name", textRight, middle - 10, 260, $"{locale.PerkName(perk)} {PerkPage.TierName(tier)}", 16, UiKit.Text, TextAlignmentOptions.Right, FontStyles.Bold);
            status = UiKit.Label(parent, "Status", textRight, middle + 10, 260, "", 14, UiKit.Muted, TextAlignmentOptions.Right);
            barTop = middle + 22;
            bar = UiKit.Rect(parent, "Bar", textRight - BarWidth, barTop - BarHeight / 2, BarWidth, BarHeight);
            UiKit.Image(bar, new Color(0, 0, 0, 0.6f));
            barGroup = bar.gameObject.AddComponent<CanvasGroup>();
            barGroup.interactable = false;
            barGroup.blocksRaycasts = false;
            barFillRect = UiKit.Rect(bar, "Fill", 0, 0, BarWidth, BarHeight);
            barFill = UiKit.Image(barFillRect, UiKit.GoldHi);
            UnityEngine.Object.DontDestroyOnLoad(root);
        }

        /// <summary>매 프레임. remaining 은 남은 초, fraction 은 남은 비율 (지속이면 지속 시간, 쿨타임이면 쿨타임 기준).</summary>
        public void Show(HudLook look, float remaining, float fraction)
        {
            if (root == null)
            {
                return;
            }

            var now = Time.time;
            if (shownLook.HasValue && shownLook.Value != look)
            {
                Cue(ActivePerkHudLook.CueOf(shownLook.Value, look), look, now);
            }

            var whole = (int)Math.Ceiling(Math.Max(0, remaining));
            var deny = now < denyUntil;
            if (shownLook != look || (whole != shownSeconds && (look == HudLook.Running || look == HudLook.Cooldown)) || deny != denyShown)
            {
                shownLook = look;
                shownSeconds = whole;
                denyShown = deny;
                Apply(look, remaining, whole, deny);
            }

            sweep.fillAmount = look == HudLook.Cooldown ? fraction : 0;
            Animate(look, whole, fraction, now, Time.deltaTime);
        }

        private void Apply(HudLook look, float remaining, int whole, bool deny)
        {
            var grey = look == HudLook.Cooldown || look == HudLook.NoTarget;
            var ending = Plugin.FxEndingBlink.Value && ActivePerkHudLook.EndingSoon(look, whole);
            frame.color = look == HudLook.Ready ? UiKit.Gold
                : look == HudLook.Running ? UiKit.GoldHi
                : look == HudLook.Used ? Faded(UiKit.Dim, 0.6f)
                : Faded(UiKit.Muted, 0.6f);
            icon.color = look == HudLook.Ready || look == HudLook.Running ? BrightIcon : Faded(UiKit.Text, look == HudLook.Used ? 0.25f : 0.35f);
            keyLine.color = look == HudLook.Ready ? UiKit.Gold : UiKit.Line;
            key.color = look == HudLook.Ready ? UiKit.Text : UiKit.Muted;
            bar.gameObject.SetActive(look == HudLook.Running);
            barFill.color = ending ? UiKit.Bad : UiKit.GoldHi;
            seconds.text = look == HudLook.Cooldown ? whole.ToString() : "";
            seconds.color = Plugin.FxReadyCountdown.Value && ActivePerkHudLook.ReadySoon(look, whole) ? UiKit.GoldHi : UiKit.Text;

            switch (look)
            {
                case HudLook.Ready:
                    status.text = locale.Get("hud.ready");
                    status.color = UiKit.Green;
                    break;
                case HudLook.Running:
                    status.text = locale.Format("hud.running", locale.Get($"perk.{perk}.running"), whole);
                    status.color = ending ? UiKit.Bad : UiKit.GoldHi;
                    break;
                case HudLook.Cooldown:
                    status.text = locale.Format("hud.cooldown", ActivePerkHudLook.Clock(remaining));
                    status.color = UiKit.Muted;
                    break;
                case HudLook.Used:
                    status.text = locale.Get("hud.used");
                    status.color = UiKit.Dim;
                    break;
                default:
                    status.text = locale.Get("hud.noTarget");
                    status.color = UiKit.Muted;
                    break;
            }

            if (deny)
            {
                status.color = UiKit.Bad;
            }

            name.color = grey || look == HudLook.Used ? UiKit.Muted : UiKit.Text;

            if (ending && !endingLogged)
            {
                endingLogged = true;
                ActivePerkLog.Event($"hud fx: {perk} last {ActivePerkHudLook.EndingSoonSeconds} s blink");
            }
        }

        /// <summary>모양이 바뀐 순간의 연출. 켠 연출만 시작하고 무엇을 띄웠는지 로그에 남긴다.</summary>
        private void Cue(HudCue cue, HudLook look, float now)
        {
            if (cue == HudCue.None)
            {
                return;
            }

            var shown = new List<string>();
            switch (cue)
            {
                case HudCue.Use:
                    runStart = now;
                    endingLogged = false;
                    if (Plugin.FxEdgeLight.Value)
                    {
                        StartEdge(true, now);
                        shown.Add("edge gold");
                    }

                    if (Plugin.FxHexRing.Value)
                    {
                        StartRing(UiKit.GoldHi, now);
                        shown.Add("ring gold");
                    }

                    // 즉시 효과는 지속이 없어 확대·금색 강조가 없다
                    if (look == HudLook.Running && Plugin.FxEnlarge.Value) shown.Add($"enlarge x{RunScale}");
                    if (look == HudLook.Running && Plugin.FxGoldPanel.Value) shown.Add("gold panel");
                    break;
                case HudCue.End:
                    if (Plugin.FxEdgeLight.Value)
                    {
                        StartEdge(false, now);
                        shown.Add("edge gray");
                    }

                    if (Plugin.FxHexRing.Value)
                    {
                        StartRing(EdgeGray, now);
                        shown.Add("ring gray");
                    }

                    break;
                case HudCue.ReadyAgain:
                    if (Plugin.FxReadyRing.Value)
                    {
                        StartRing(UiKit.GoldHi, now);
                        flashStart = now;
                        shown.Add("ring gold + flash");
                    }

                    if (Plugin.FxReadyBlink.Value)
                    {
                        readyBlinkStart = now;
                        shown.Add("ready blink");
                    }

                    break;
            }

            ActivePerkLog.Event($"hud fx: {perk} {cue} -> {(shown.Count == 0 ? "none (all off)" : string.Join(", ", shown))}");
        }

        private void StartEdge(bool goldLight, float now)
        {
            edgeIsGold = goldLight;
            edgeStart = now;
            edge.sprite = goldLight ? PerkSprites.EdgeLight(0.58f, 0.82f, 0.22f, 0.5f) : PerkSprites.EdgeLight(0.62f, 0.85f, 0.14f, 0.3f);
        }

        private void StartRing(Color color, float now)
        {
            bounceStart = now;
            ringStart = now;
            ring.color = Faded(color, 0);
        }

        /// <summary>매 프레임 움직이는 연출: 칸 확대, 금색 강조, 육각 튐·링·반짝, 가장자리 빛, 깜빡임.</summary>
        private void Animate(HudLook look, int whole, float fraction, float now, float dt)
        {
            var running = look == HudLook.Running;

            // 칸 확대 (지속 중), 끝나면 원래 크기로
            var scaleTarget = running && Plugin.FxEnlarge.Value ? RunScale : 1f;
            panelScale = Mathf.Abs(panelScale - scaleTarget) < 0.001f ? scaleTarget : Mathf.Lerp(panelScale, scaleTarget, 1 - Mathf.Exp(-dt * 14f));
            SetScale(panel, panelScale, panelScale);

            // 금색 강조: 그늘은 GoldFade, 선은 LineFade 동안 들어오고 나간다. 막대도 그늘만큼 두꺼워진다
            var goldOn = running && Plugin.FxGoldPanel.Value;
            gold = Mathf.MoveTowards(gold, goldOn ? 1f : 0f, dt / GoldFade);
            lines = Mathf.MoveTowards(lines, goldOn ? 1f : 0f, dt / LineFade);
            shadeGold.color = new Color(1, 1, 1, gold);
            var lineScale = EaseOut(lines);
            SetScale(lineTopRect, lineScale, 1);
            SetScale(lineBottomRect, lineScale, 1);
            lineTop.enabled = lineBottom.enabled = lineScale > 0.001f;
            var barHeight = Mathf.Lerp(BarHeight, GoldBarHeight, gold);
            if (bar.sizeDelta.y != barHeight)
            {
                bar.anchoredPosition = new Vector2(bar.anchoredPosition.x, -(barTop - barHeight / 2));
                bar.sizeDelta = new Vector2(BarWidth, barHeight);
            }

            barFillRect.sizeDelta = new Vector2(BarWidth * (running ? fraction : 0), barHeight);

            // 육각 빛: 지속 중 켜짐 (금색 강조면 숨 쉬듯), 쿨타임 끝 반짝
            var flash = (now - flashStart) / FlashTime;
            if (running)
            {
                var breath = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Cos(2 * Mathf.PI * (now - runStart) / BreathPeriod));
                glow.enabled = true;
                glow.color = Faded(UiKit.GoldHi, GlowAlpha * Mathf.Lerp(1f, breath, gold));
            }
            else if (flash < 1)
            {
                glow.enabled = true;
                glow.color = Faded(UiKit.GoldHi, GlowAlpha * (flash < 0.2f ? flash / 0.2f : 1 - (flash - 0.2f) / 0.8f));
            }
            else
            {
                glow.enabled = false;
            }

            // 육각 튐: 1.3배에서 살짝 넘치며 돌아온다
            var bounce = (now - bounceStart) / BounceTime;
            var hexScale = bounce < 1 ? BounceScale - (BounceScale - 1) * EaseOutBack(bounce) : 1f;
            SetScale(hex, hexScale, hexScale);

            // 링: 커지며 흐려진다
            var ringT = (now - ringStart) / RingTime;
            ring.enabled = ringT < 1;
            if (ring.enabled)
            {
                var e = EaseOut(ringT);
                var s = 1 + (RingScale - 1) * e;
                SetScale(ringRect, s, s);
                ring.color = Faded(ring.color, 0.9f * (1 - e));
            }

            // 가장자리 빛: 금빛은 짧게 올라왔다 0.8초에 빠지고, 회색은 조금 늦게 올라와 0.9초에 빠진다
            var edgeTotal = edgeIsGold ? 0.8f : 0.9f;
            var edgeRise = edgeIsGold ? 0.08f : 0.18f;
            var edgeT = now - edgeStart;
            edge.enabled = edgeT < edgeTotal;
            if (edge.enabled)
            {
                var a = edgeT < edgeRise ? edgeT / edgeRise : Mathf.Pow(1 - (edgeT - edgeRise) / (edgeTotal - edgeRise), 2);
                edge.color = Faded(edgeIsGold ? EdgeGold : EdgeGray, a);
            }

            // 깜빡임: 남은 5초 (막대·상태), 쿨타임 끝 "준비됨" 2번
            var ending = running && Plugin.FxEndingBlink.Value && ActivePerkHudLook.EndingSoon(look, whole);
            var readyBlink = now - readyBlinkStart < ReadyBlinkTime && (look == HudLook.Ready || look == HudLook.NoTarget);
            var blinkAlpha = ending ? Blink(now) : readyBlink ? Blink(now - readyBlinkStart) : 1f;
            var barAlpha = ending ? blinkAlpha : 1f;
            if (barGroup.alpha != barAlpha)
            {
                barGroup.alpha = barAlpha;
            }

            status.alpha = ending || readyBlink ? blinkAlpha : 1f;
        }

        /// <summary>
        /// 개발자 초기화 키로 시계를 처음으로 돌렸다. 다음 모양을 전환으로 보지 않는다
        /// (쿨타임 → 준비됨이 쿨타임 끝 연출로 보이지 않게).
        /// </summary>
        public void Restart()
        {
            shownLook = null;
            endingLogged = false;
        }

        // 같은 값이면 건드리지 않는다 (가만히 있을 때 캔버스를 매 프레임 다시 그리지 않게)
        private static void SetScale(Transform transform, float x, float y)
        {
            var current = transform.localScale;
            if (current.x != x || current.y != y)
            {
                transform.localScale = new Vector3(x, y, 1);
            }
        }

        private static float Blink(float t) => t % BlinkPeriod < BlinkPeriod / 2 ? 1f : 0.25f;

        private static float EaseOut(float t) => 1 - (1 - t) * (1 - t);

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1;
            var p = t - 1;
            return 1 + c3 * p * p * p + c1 * p * p;
        }

        /// <summary>UiKit.Rect(왼쪽 위 기준)를 가운데 기준으로 바꾼다 (같은 자리, 가운데를 중심으로 커진다).</summary>
        private static RectTransform Centered(RectTransform rect)
        {
            var size = rect.sizeDelta;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition += new Vector2(size.x / 2, -size.y / 2);
            return rect;
        }

        /// <summary>UiKit.Rect 를 오른쪽 끝 기준으로 바꾼다 (가로로 늘이면 오른쪽에서 펼쳐진다).</summary>
        private static RectTransform FromRight(RectTransform rect)
        {
            rect.pivot = new Vector2(1, 1);
            rect.anchoredPosition += new Vector2(rect.sizeDelta.x, 0);
            rect.localScale = new Vector3(0, 1, 1);
            return rect;
        }

        /// <summary>쓸 수 없을 때 눌렀다: 0.5초 동안 상태 글자를 붉게, 게임 '안 됨' 소리.</summary>
        public void Deny()
        {
            denyUntil = Time.time + DenyTime;
            PerkGame.Sound(EUISoundType.ErrorMessage);
        }

        private void OnKeyChanged(object sender, EventArgs args)
        {
            if (key != null)
            {
                key.text = KeyText(Plugin.ActiveKey.Value);
            }
        }

        public void Destroy()
        {
            Plugin.ActiveKey.SettingChanged -= OnKeyChanged;
            if (root != null)
            {
                UnityEngine.Object.Destroy(root);
                root = null;
            }
        }

        private static Color Faded(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

        /// <summary>"J", "Alt+G" 꼴. 왼쪽·오른쪽 수식 키는 하나로 줄인다.</summary>
        internal static string KeyText(KeyboardShortcut shortcut)
        {
            var parts = shortcut.Modifiers.Select(ModifierName).Distinct().ToList();
            parts.Add(shortcut.MainKey.ToString());
            return string.Join("+", parts);
        }

        private static string ModifierName(KeyCode code)
        {
            switch (code)
            {
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt:
                    return "Alt";
                case KeyCode.LeftControl:
                case KeyCode.RightControl:
                    return "Ctrl";
                case KeyCode.LeftShift:
                case KeyCode.RightShift:
                    return "Shift";
                default:
                    return code.ToString();
            }
        }
    }
}
