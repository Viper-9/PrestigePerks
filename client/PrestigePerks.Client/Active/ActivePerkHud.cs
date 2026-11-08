using System;
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
    /// </summary>
    internal sealed class ActivePerkHud
    {
        private const float ReferenceHeight = 1080f;
        private const float HexWidth = 56, RightGap = 28, BottomGap = 128 + 18;
        private const float BarWidth = 96, BarHeight = 3, TextGap = 8, DenyTime = 0.5f;

        // 페이지의 리롤 칸 아이콘 색 (PerkPage.TargetIcon)
        private static readonly Color BrightIcon = new Color32(246, 226, 180, 255);

        private PerkLocale locale;
        private string perk;
        private GameObject root;
        private Image glow, frame, icon, sweep, barFill, keyLine;
        private RectTransform bar, barFillRect;
        private TextMeshProUGUI name, status, seconds, key;
        private HudLook? shownLook;
        private int shownSeconds = -1;
        private float denyUntil;
        private bool denyShown;

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
            var parent = root.transform;

            // 그늘: 육각 왼쪽 190 부터 육각 가운데까지, 오른쪽으로 갈수록 짙다
            UiKit.Sprite(UiKit.Rect(parent, "Shade", hexX - 190, hexY - 4, 190 + HexWidth / 2, hexHeight + 18),
                PerkSprites.Gradient(new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 158), true), Color.white);

            glow = UiKit.Sprite(UiKit.Rect(parent, "Glow", hexX - HexWidth * 0.15f, hexY - hexHeight * 0.15f, HexWidth * 1.3f, hexHeight * 1.3f),
                PerkSprites.HexGlow, new Color(UiKit.GoldHi.r, UiKit.GoldHi.g, UiKit.GoldHi.b, 0.75f));
            var fill = UiKit.Rect(parent, "Fill", hexX, hexY, HexWidth, hexHeight);
            UiKit.Sprite(fill, PerkSprites.HexFill, Color.white);
            var iconSize = Mathf.Round(HexWidth * 0.6f);
            icon = UiKit.Sprite(UiKit.Rect(fill, "Icon", (HexWidth - iconSize) / 2, (hexHeight - iconSize) / 2, iconSize, iconSize), PerkSprites.Icon(perk), Color.white);
            icon.preserveAspect = true;

            // 쿨타임: 남은 만큼 어두운 부채꼴 (위에서 시계 방향). 육각 바탕 그림으로 채워 육각 밖으로 나가지 않는다
            sweep = UiKit.Sprite(UiKit.Rect(parent, "Sweep", hexX, hexY, HexWidth, hexHeight), PerkSprites.HexFill, new Color(0, 0, 0, 0.6f), Image.Type.Filled);
            sweep.fillMethod = Image.FillMethod.Radial360;
            sweep.fillOrigin = (int)Image.Origin360.Top;
            sweep.fillClockwise = true;
            frame = UiKit.Sprite(UiKit.Rect(parent, "Frame", hexX, hexY, HexWidth, hexHeight), PerkSprites.HexFrame, UiKit.Gold);
            seconds = UiKit.Label(parent, "Seconds", hexX + HexWidth / 2, middle, HexWidth, "", 17, UiKit.Text, TextAlignmentOptions.Center, FontStyles.Bold);

            // 키 칸: 육각 아래 가운데에 걸친다
            var keyBox = UiKit.Rect(parent, "Key", hexX + (HexWidth - 44) / 2, hexY + hexHeight - 7, 44, 17);
            UiKit.Image(keyBox, UiKit.Dark);
            keyLine = UiKit.Outline(keyBox, UiKit.Line);
            key = UiKit.Label(keyBox, "Label", 22, 8.5f, 44, KeyText(Plugin.ActiveKey.Value), 12, UiKit.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            key.overflowMode = TextOverflowModes.Overflow;
            // 레이드 중에 F12 에서 키를 바꿔도 칸의 글자가 따라간다
            Plugin.ActiveKey.SettingChanged += OnKeyChanged;

            var textRight = hexX - TextGap;
            name = UiKit.Label(parent, "Name", textRight, middle - 10, 260, $"{locale.PerkName(perk)} {PerkPage.TierName(tier)}", 16, UiKit.Text, TextAlignmentOptions.Right, FontStyles.Bold);
            status = UiKit.Label(parent, "Status", textRight, middle + 10, 260, "", 14, UiKit.Muted, TextAlignmentOptions.Right);
            bar = UiKit.Rect(parent, "Bar", textRight - BarWidth, middle + 22 - BarHeight / 2, BarWidth, BarHeight);
            UiKit.Image(bar, new Color(0, 0, 0, 0.6f));
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

            var whole = (int)Math.Ceiling(Math.Max(0, remaining));
            var deny = Time.time < denyUntil;
            if (shownLook != look || (whole != shownSeconds && (look == HudLook.Running || look == HudLook.Cooldown)) || deny != denyShown)
            {
                shownLook = look;
                shownSeconds = whole;
                denyShown = deny;
                Apply(look, remaining, whole, deny);
            }

            sweep.fillAmount = look == HudLook.Cooldown ? fraction : 0;
            barFillRect.sizeDelta = new Vector2(BarWidth * (look == HudLook.Running ? fraction : 0), BarHeight);
        }

        private void Apply(HudLook look, float remaining, int whole, bool deny)
        {
            var grey = look == HudLook.Cooldown || look == HudLook.NoTarget;
            glow.enabled = look == HudLook.Running;
            frame.color = look == HudLook.Ready ? UiKit.Gold
                : look == HudLook.Running ? UiKit.GoldHi
                : look == HudLook.Used ? Faded(UiKit.Dim, 0.6f)
                : Faded(UiKit.Muted, 0.6f);
            icon.color = look == HudLook.Ready || look == HudLook.Running ? BrightIcon : Faded(UiKit.Text, look == HudLook.Used ? 0.25f : 0.35f);
            keyLine.color = look == HudLook.Ready ? UiKit.Gold : UiKit.Line;
            key.color = look == HudLook.Ready ? UiKit.Text : UiKit.Muted;
            bar.gameObject.SetActive(look == HudLook.Running);
            seconds.text = look == HudLook.Cooldown ? whole.ToString() : "";

            switch (look)
            {
                case HudLook.Ready:
                    status.text = locale.Get("hud.ready");
                    status.color = UiKit.Green;
                    break;
                case HudLook.Running:
                    status.text = locale.Format("hud.running", locale.Get($"perk.{perk}.running"), whole);
                    status.color = UiKit.GoldHi;
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
