// 클라이언트는 nullable 을 끄고 빌드한다. 이 파일을 함께 컴파일하는 테스트(nullable 켬)에서도 같게 본다
#nullable disable

using System.Globalization;
using System.Text.RegularExpressions;

namespace PrestigePerks.Model
{
    /// <summary>
    /// 액티브 특전 문구: "{regen}/s · {duration} s" 의 이름 자리에 단계 값을 넣는다. duration·cooldown 은 사용 제한 값, 나머지는 카탈로그 values.
    /// 모르는 이름은 그대로 둔다. 게임 형식에 의존하지 않아 단위 테스트로 검증한다.
    /// </summary>
    public static class ActivePerkText
    {
        private static readonly Regex Slot = new Regex(@"\{([a-z]+)\}", RegexOptions.Compiled);

        public static string Fill(string template, ActivePerkInfoDto info, int tier)
        {
            if (string.IsNullOrEmpty(template) || info == null)
            {
                return template ?? "";
            }

            return Slot.Replace(template, match =>
            {
                var name = match.Groups[1].Value;
                if (name == "duration")
                {
                    return Number(info.DurationAt(tier));
                }

                if (name == "cooldown")
                {
                    return Number(info.CooldownAt(tier));
                }

                return info.Values != null && info.Values.ContainsKey(name) ? Number(info.Value(name, tier)) : match.Value;
            });
        }

        public static string Number(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
