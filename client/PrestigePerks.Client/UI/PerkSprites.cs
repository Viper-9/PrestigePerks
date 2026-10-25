using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PrestigePerks.UI
{
    /// <summary>
    /// 특전 페이지 그림. DLL 에 넣은 PNG(Assets\Icons, Assets\Sprites)를 읽어 Sprite 로 만들고 계속 쓴다.
    /// 5.0 시즌 특전 줄(PerkTemplate)과 4.1.2 프레스티지 보상 카드 스프라이트다 (4.1.2 에 없는 것도 있어 전부 PNG 로 넣었다).
    /// </summary>
    internal static class PerkSprites
    {
        private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

        /// <summary>특전 아이콘 (128px). 목록에서 50px 안팎으로 줄어 보이므로 mipmap 과 트릴리니어로 거칠지 않게 한다.</summary>
        public static Sprite Icon(int perk) => Load($"Icons.perk_{perk}", true);

        public static Sprite RowGradient => Load("Sprites.Sesons_PerkTemplate_Background-Gradient", false);
        public static Sprite RowGlow => Load("Sprites.Sesons_PerkTemplate_Background-GradientWhite", false);
        public static Sprite RowShadow => Load("Sprites.Sesons_PerkTemplate_Background-Shadow", false);

        /// <summary>격자 무늬. Image.Type.Tiled 로 깐다.</summary>
        public static Sprite Grid => Load("Sprites.Sesons_PerkTemplate_Background-Grid", false, repeat: true);

        public static Sprite Lock => Load("Sprites.Seasons_PerkTemplate_Icon-lock", true);

        /// <summary>작업 표시줄 "특전" 버튼 아이콘: 슬롯 배지 육각 + 별 (흰색, 색은 버튼 애니메이션이 입힌다).</summary>
        public static Sprite TaskbarIcon => Load("Sprites.Taskbar_Perk", true);

        /// <summary>풀린 자물쇠: Icon-lock 에서 고리를 올리고 오른쪽 다리를 끊어 그렸다.</summary>
        public static Sprite Unlock => Load("Sprites.Seasons_PerkTemplate_Icon-unlock", true);

        /// <summary>
        /// 슬롯 육각 배지: 안쪽 바탕, 한 겹 틀(흰색, 색을 입혀 쓴다), 틀 바깥 빛(흰색, 틀보다 1.3배). 목업 1안 모양을
        /// 게임 그림(94px)을 늘리면 흐려서 4배 크기로 다시 그렸다 (tools/draw-hex-badge.py).
        /// </summary>
        public static Sprite HexFill => Load("Sprites.Hex_Fill", true);
        public static Sprite HexFrame => Load("Sprites.Hex_Frame", true);
        public static Sprite HexGlow => Load("Sprites.Hex_Glow", true);

        /// <summary>코인 아이템 아이콘을 못 만들 때 (인벤토리에 코인이 없을 때) 대신 쓴다.</summary>
        public static Sprite CoinFallback => Load("Sprites.Season_Texture", true);

        public static Sprite Emblem => Load("Sprites.Frame_Seasons", false);
        public static Sprite Vignette => Load("Sprites.Spectator_ShadingVignette", false);

        /// <summary>1px 테두리 (Sliced, 가운데 비움). 색은 Image.color 로 준다.</summary>
        public static Sprite Outline
        {
            get
            {
                if (cache.TryGetValue("outline", out var sprite) && sprite != null)
                {
                    return sprite;
                }

                var pixels = new Color32[9];
                for (var i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = i == 4 ? new Color32(255, 255, 255, 0) : new Color32(255, 255, 255, 255);
                }

                return cache["outline"] = Make(pixels, 3, 3, FilterMode.Point, TextureWrapMode.Clamp, new Vector4(1, 1, 1, 1));
            }
        }

        /// <summary>점선 (6px 마다 점 하나). Tiled 로 깐다.</summary>
        public static Sprite Dots
        {
            get
            {
                if (cache.TryGetValue("dots", out var sprite) && sprite != null)
                {
                    return sprite;
                }

                var pixels = new Color32[6];
                for (var i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = new Color32(255, 255, 255, (byte)(i == 0 ? 255 : 0));
                }

                return cache["dots"] = Make(pixels, 6, 1, FilterMode.Point, TextureWrapMode.Repeat, Vector4.zero);
            }
        }

        /// <summary>세로(위→아래) 또는 가로(왼쪽→오른쪽) 그라데이션. 늘려서 쓴다.</summary>
        public static Sprite Gradient(Color32 from, Color32 to, bool horizontal = false)
        {
            var key = $"grad {from} {to} {horizontal}";
            if (cache.TryGetValue(key, out var sprite) && sprite != null)
            {
                return sprite;
            }

            const int steps = 64;
            var pixels = new Color32[steps];
            for (var i = 0; i < steps; i++)
            {
                // 텍스처는 아래 줄부터라 세로는 거꾸로 채운다
                var t = horizontal ? i / (steps - 1f) : 1 - i / (steps - 1f);
                pixels[i] = Color32.Lerp(from, to, t);
            }

            return cache[key] = Make(pixels, horizontal ? steps : 1, horizontal ? 1 : steps, FilterMode.Bilinear, TextureWrapMode.Clamp, Vector4.zero);
        }

        // border: 왼쪽, 아래, 오른쪽, 위 (Sprite.Create 순서)
        private static Sprite Load(string name, bool mipmaps, Vector4 border = default, bool repeat = false)
        {
            if (cache.TryGetValue(name, out var sprite) && sprite != null)
            {
                return sprite;
            }

            byte[] bytes;
            using (var stream = typeof(PerkSprites).Assembly.GetManifestResourceStream($"PrestigePerks.{name}.png"))
            {
                if (stream == null)
                {
                    Log.Warn($"sprite missing: {name}");
                    return null;
                }

                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    bytes = memory.ToArray();
                }
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipmaps);
            if (!texture.LoadImage(bytes, true))
            {
                Log.Warn($"sprite unreadable: {name}");
                return null;
            }

            texture.name = name;
            texture.filterMode = mipmaps ? FilterMode.Trilinear : FilterMode.Bilinear;
            texture.anisoLevel = mipmaps ? 4 : 1;
            texture.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            return cache[name] = Wrap(texture, border);
        }

        private static Sprite Make(Color32[] pixels, int width, int height, FilterMode filter, TextureWrapMode wrap, Vector4 border)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = filter, wrapMode = wrap };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Wrap(texture, border);
        }

        // 게임이 장면을 바꿀 때 UnloadUnusedAssets 로 지우지 않게 한다
        private static Sprite Wrap(Texture2D texture, Vector4 border)
        {
            texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }
    }
}
