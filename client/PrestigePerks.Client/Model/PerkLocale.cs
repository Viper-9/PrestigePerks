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

        /// <summary>문구가 있는지 (게임 언어 → en).</summary>
        public bool Has(string key) => primary.ContainsKey(key) || fallback.ContainsKey(key);

        /// <summary>특전 이름. 문구가 없는 특전(add-on)은 ID 를 그대로 보인다.</summary>
        public string PerkName(string perk) => Has($"perk.{perk}.name") ? Get($"perk.{perk}.name") : perk ?? "";

        /// <summary>효과 한 줄. 문구의 {0} 에 수치(<see cref="PerkAmounts.Format"/>)를 넣는다. 문구에 숫자를 직접 적지 않는다. 문구가 없으면 수치만.</summary>
        public string PerkEffect(string perk, string value) => Has($"perk.{perk}.effect") ? Format($"perk.{perk}.effect", value) : value;

        /// <summary>수치를 뺀 효과 이름 ("반동"). 선택한 특전 칸의 부제. 문구가 없으면 빈 줄.</summary>
        public string PerkSubject(string perk) => Has($"perk.{perk}.effect") ? Format($"perk.{perk}.effect", "").Trim() : "";

        public string PerkDetail(string perk) => Has($"perk.{perk}.detail") ? Get($"perk.{perk}.detail") : "";
    }
}
