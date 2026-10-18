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

        public string PerkEffect(int perk) => Get($"perk.{perk}.effect");

        public string PerkDetail(int perk) => Get($"perk.{perk}.detail");
    }
}
