using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PrestigePerks.UI
{
    /// <summary>런타임 Unity UI 를 만드는 도우미. 좌표는 부모 왼쪽 위 기준 (x 오른쪽, y 아래쪽).</summary>
    internal static class UiKit
    {
        // 타르코프 화면 색 (특전 페이지 목업 2안)
        public static readonly Color Text = new Color32(214, 216, 210, 255);
        public static readonly Color Muted = new Color32(138, 144, 146, 255);
        public static readonly Color Dim = new Color32(88, 94, 97, 255);
        public static readonly Color Line = new Color32(52, 60, 64, 255);
        public static readonly Color Gold = new Color32(226, 172, 72, 255);
        public static readonly Color GoldHi = new Color32(238, 196, 110, 255);

        /// <summary>효과 수치: 장착, 미장착.</summary>
        public static readonly Color Green = new Color32(146, 192, 84, 255);
        public static readonly Color GreenDim = new Color32(112, 140, 80, 255);

        public static readonly Color Bad = new Color32(207, 106, 92, 255);
        public static readonly Color Dark = new Color32(18, 20, 21, 255);

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

        /// <summary>스프라이트 그림. type 이 Sliced 면 스프라이트 테두리(9-slice)를, Tiled 면 바둑판으로 깐다.</summary>
        public static Image Sprite(RectTransform rect, Sprite sprite, Color color, UnityEngine.UI.Image.Type type = UnityEngine.UI.Image.Type.Simple)
        {
            var image = Image(rect, color);
            image.sprite = sprite;
            image.type = type;
            if (type == UnityEngine.UI.Image.Type.Sliced)
            {
                image.fillCenter = true;
            }

            return image;
        }

        /// <summary>1px 테두리.</summary>
        public static Image Outline(RectTransform parent, Color color)
        {
            var image = Sprite(Fill(parent, "Outline"), PerkSprites.Outline, color, UnityEngine.UI.Image.Type.Sliced);
            image.fillCenter = false;
            return image;
        }

        /// <summary>
        /// 글자. y 는 글자 줄의 세로 가운데다 (목업 좌표와 같은 기준). 칸 높이는 글자 크기의 1.6배로 잡는다:
        /// 게임 한글 글꼴은 줄 높이가 커서 칸이 그보다 낮으면 TMP Ellipsis 가 줄을 통째로 지운다.
        /// align 은 가로만 고른다 (Left, Center, Right).
        /// </summary>
        public static TextMeshProUGUI Label(Transform parent, string name, float x, float centerY, float width, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Left, FontStyles style = FontStyles.Normal)
        {
            var height = size * 1.6f;
            if (align == TextAlignmentOptions.Right)
            {
                x -= width;
            }
            else if (align == TextAlignmentOptions.Center)
            {
                x -= width / 2;
            }

            return Label(Rect(parent, name, x, centerY - height / 2, width, height), text, size, color, align, style);
        }

        public static TextMeshProUGUI Label(RectTransform rect, string text, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.Left, FontStyles style = FontStyles.Normal)
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
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.richText = true;
            return label;
        }

        /// <summary>글자 폭 (한 줄).</summary>
        public static float Width(TextMeshProUGUI label, string text) => label.GetPreferredValues(text).x;

        /// <summary>그림 버튼. target 이 눌리는 그림이고, 마우스를 올리면 밝아진다.</summary>
        public static Button Button(Image target)
        {
            target.raycastTarget = true;
            var button = target.gameObject.AddComponent<Button>();
            button.targetGraphic = target;
            var colors = button.colors;
            colors.normalColor = new Color(0.9f, 0.9f, 0.9f, 1);
            colors.highlightedColor = Color.white;
            colors.selectedColor = new Color(0.9f, 0.9f, 0.9f, 1);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1);
            colors.disabledColor = Color.white;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            return button;
        }
    }
}
