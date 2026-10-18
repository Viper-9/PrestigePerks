using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PrestigePerks.UI
{
    /// <summary>런타임 Unity UI 를 만드는 도우미. 좌표는 부모 왼쪽 위 기준 (x 오른쪽, y 아래쪽).</summary>
    internal static class UiKit
    {
        // 게임은 선형 색공간이라 알파 248 도 밝은 글자가 비쳐 보인다. 불투명하게 둔다
        public static readonly Color Screen = new Color32(12, 14, 14, 255);
        public static readonly Color Header = new Color32(10, 12, 12, 255);
        public static readonly Color Panel = new Color32(18, 21, 20, 255);
        public static readonly Color Panel2 = new Color32(25, 29, 27, 255);
        public static readonly Color ReelBg = new Color32(11, 13, 12, 255);
        public static readonly Color Line = new Color32(42, 47, 44, 255);
        public static readonly Color Line2 = new Color32(58, 64, 60, 255);
        public static readonly Color Text = new Color32(217, 214, 198, 255);
        public static readonly Color Muted = new Color32(134, 138, 128, 255);
        public static readonly Color Dim = new Color32(85, 90, 84, 255);
        public static readonly Color Gold = new Color32(201, 181, 124, 255);
        public static readonly Color Lock = new Color32(126, 165, 193, 255);
        public static readonly Color Coin = new Color32(121, 184, 134, 255);
        public static readonly Color Bad = new Color32(207, 106, 92, 255);
        public static readonly Color Dark = new Color32(16, 18, 16, 255);

        /// <summary>게임 글꼴. 작업 표시줄 버튼 글자에서 가져온다 (언어별 대체 글꼴은 게임이 관리).</summary>
        public static TMP_FontAsset Font;

        public static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        /// <summary>부모를 꽉 채우는 RectTransform (inset 만큼 안쪽).</summary>
        public static RectTransform Fill(Transform parent, string name, float inset = 0)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            return rect;
        }

        public static Image Image(RectTransform rect, Color color, bool raycast = false)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        /// <summary>테두리 상자: 바깥 Image 가 테두리 색, 안쪽 Image 가 바탕. 안쪽을 돌려준다.</summary>
        public static (Image Border, RectTransform Inner) Box(Transform parent, string name, float x, float y, float width, float height, Color border, Color fill, float thickness = 1)
        {
            var outer = Rect(parent, name, x, y, width, height);
            var borderImage = Image(outer, border);
            var inner = Fill(outer, "Inner", thickness);
            Image(inner, fill);
            return (borderImage, inner);
        }

        public static TextMeshProUGUI Label(Transform parent, string name, float x, float y, float width, float height, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.TopLeft, FontStyles style = FontStyles.Normal)
        {
            // 게임 한글 글꼴은 줄 높이가 커서 칸이 글자 크기의 1.6배보다 낮으면 TMP Ellipsis 가 줄을 통째로 지운다. 칸을 위아래로 넓힌다
            var minHeight = size * 1.6f;
            if (height < minHeight)
            {
                y -= (minHeight - height) / 2;
                height = minHeight;
            }

            var rect = Rect(parent, name, x, y, width, height);
            return Label(rect, text, size, color, align, style);
        }

        public static TextMeshProUGUI Label(RectTransform rect, string text, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.TopLeft, FontStyles style = FontStyles.Normal)
        {
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (Font != null)
            {
                label.font = Font;
            }

            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = align;
            label.fontStyle = style;
            label.raycastTarget = false;
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return label;
        }

        /// <summary>테두리 있는 버튼. 글자를 돌려준다.</summary>
        public static (Button Button, Image Border, Image Fill, TextMeshProUGUI Label) Button(Transform parent, string name, float x, float y, float width, float height,
            string text, float size, Color border, Color fill, Color textColor)
        {
            var (borderImage, inner) = Box(parent, name, x, y, width, height, border, fill);
            borderImage.raycastTarget = true;
            var button = borderImage.gameObject.AddComponent<Button>();
            button.targetGraphic = borderImage;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1);
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.6f);
            button.colors = colors;
            var label = Label(Fill(inner, "Label", 2), text, size, textColor, TextAlignmentOptions.Center, FontStyles.Bold);
            return (button, borderImage, inner.GetComponent<Image>(), label);
        }
    }
}
