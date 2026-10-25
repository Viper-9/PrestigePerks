using System.Collections.Generic;
using Newtonsoft.Json;

namespace PrestigePerks.Model
{
    /// <summary>
    /// 특전 이름·효과와 화면 문구 (Locales/kr.json, en.json, DLL 에 포함). 게임 언어에 파일이 없으면 en, 키가 없으면 키를 그대로 보인다.
    /// </summary>
    public sealed class PerkLocale
    {
        public const string Fallback = "en";

        private readonly Dictionary<string, string> primary;
        private readonly Dictionary<string, string> fallback;

        public PerkLocale(Dictionary<string, string> primary, Dictionary<string, string> fallback)
        {
            this.primary = primary ?? new Dictionary<string, string>();
            this.fallback = fallback ?? new Dictionary<string, string>();
        }

        public static Dictionary<string, string> ParseFile(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new Dictionary<string, string>();
            }

            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            }
            catch (JsonException)
            {
                return new Dictionary<string, string>();
            }
        }

        public string Get(string key)
        {
            if (primary.TryGetValue(key, out var text) || fallback.TryGetValue(key, out text))
            {
                return text;
            }

            return key;
        }

        public string Format(string key, params object[] args)
        {
            var text = Get(key);
            try
            {
                return string.Format(text, args);
            }
            catch (System.FormatException)
            {
                return text;
            }
        }

        public string PerkName(int perk) => Get($"perk.{perk}.name");

        /// <summary>효과 한 줄. 문구의 {0} 에 수치(<see cref="PerkAmounts.Format"/>)를 넣는다. 문구에 숫자를 직접 적지 않는다.</summary>
        public string PerkEffect(int perk, string value) => Format($"perk.{perk}.effect", value);

        /// <summary>수치를 뺀 효과 이름 ("반동"). 선택한 특전 칸의 부제.</summary>
        public string PerkSubject(int perk) => Format($"perk.{perk}.effect", "").Trim();

        public string PerkDetail(int perk) => Get($"perk.{perk}.detail");
    }
}
