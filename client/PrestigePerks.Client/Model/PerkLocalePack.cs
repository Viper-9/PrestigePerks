using System;
using System.Collections.Generic;

namespace PrestigePerks.Model
{
    /// <summary>
    /// 플러그인 폴더의 언어 파일 (BepInEx/plugins/PrestigePerks/Locales/&lt;게임 언어 코드&gt;.json). 형식은 DLL 의 Locales/en.json 과 같고, 같은 키는 DLL 문구를 덮어쓴다.
    /// "game:" 으로 시작하는 키는 게임 문구(프레스티지 창 등, server PrestigeLocales 와 같은 키)라 앞머리를 떼고 게임 언어에 넣는다.
    /// 게임 형식에 의존하지 않아 단위 테스트로 검증한다.
    /// </summary>
    public static class PerkLocalePack
    {
        public const string GamePrefix = "game:";

        /// <summary>파일 하나를 특전 문구와 게임 문구로 나눈다.</summary>
        public static (Dictionary<string, string> Texts, Dictionary<string, string> Game) Split(Dictionary<string, string> file)
        {
            var texts = new Dictionary<string, string>();
            var game = new Dictionary<string, string>();
            foreach (var pair in file ?? new Dictionary<string, string>())
            {
                if (pair.Key.StartsWith(GamePrefix, StringComparison.Ordinal))
                {
                    var key = pair.Key.Substring(GamePrefix.Length);
                    if (key.Length > 0)
                    {
                        game[key] = pair.Value;
                    }
                }
                else
                {
                    texts[pair.Key] = pair.Value;
                }
            }

            return (texts, game);
        }

        /// <summary>state catalog 의 add-on 문구 중 그 언어 것을 특전 키(perk.&lt;id&gt;.name/effect/detail)로. 빈 값은 넣지 않는다.</summary>
        public static Dictionary<string, string> CatalogTexts(IEnumerable<PerkCatalogEntryDto> catalog, string culture)
        {
            var result = new Dictionary<string, string>();
            if (catalog == null || culture == null)
            {
                return result;
            }

            foreach (var entry in catalog)
            {
                if (entry?.Id == null || entry.Texts == null || !entry.Texts.TryGetValue(culture, out var texts) || texts == null)
                {
                    continue;
                }

                Add(result, $"perk.{entry.Id}.name", texts.Name);
                Add(result, $"perk.{entry.Id}.effect", texts.Effect);
                Add(result, $"perk.{entry.Id}.detail", texts.Detail);
            }

            return result;
        }

        private static void Add(Dictionary<string, string> texts, string key, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                texts[key] = value;
            }
        }

        /// <summary>basis 에 overlay 를 덮어쓴 새 사전. 둘 다 바꾸지 않는다.</summary>
        public static Dictionary<string, string> Merge(Dictionary<string, string> basis, Dictionary<string, string> overlay)
        {
            var merged = basis != null ? new Dictionary<string, string>(basis) : new Dictionary<string, string>();
            if (overlay != null)
            {
                foreach (var pair in overlay)
                {
                    merged[pair.Key] = pair.Value;
                }
            }

            return merged;
        }
    }
}
