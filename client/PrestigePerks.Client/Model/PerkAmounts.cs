using System;
using System.Collections.Generic;
using System.Globalization;

namespace PrestigePerks.Model
{
    /// <summary>
    /// 특전별 단계 III 효과량 (dev-docs 2_특전.md 3절 표). 단계 I·II 는 이 값에 효과 비율(서버 tierEffectRatio)을 곱한다.
    /// 화면 문구(효과 줄의 숫자)와 클라이언트 패치(<see cref="PerkPatchMath"/>)가 같은 값을 쓴다. Bonus 특전 값은 서버 카탈로그와 같아야 한다 (테스트가 확인).
    /// 게임 형식에 의존하지 않아 단위 테스트로 검증한다.
    /// </summary>
    public static class PerkAmounts
    {
        /// <summary>단계 비율을 서버에서 받지 못했을 때 쓰는 기본값 (I, II, III).</summary>
        public static readonly double[] DefaultRatios = { 0.5, 0.75, 1.0 };

        /// <summary>감소 부호. 문구 파일과 같은 글자 (U+2212).</summary>
        public const string Minus = "−";

        // 문구 방향의 부호를 붙인 III 효과량. Percent 가 false 면 고정값 (30 인체공학)
        private static readonly Dictionary<int, (double Amount, bool Percent)> Table = new Dictionary<int, (double, bool)>
        {
            [1] = (30, true),    // 레이드 경험치
            [2] = (30, true),    // 신체 스킬 성장
            [3] = (30, true),    // 전투 스킬 성장
            [4] = (30, true),    // 실무·정신 스킬 성장
            [8] = (-30, true),   // 스캐브 대기 시간
            [9] = (20, true),    // 소지 무게 한도
            [10] = (15, true),   // 스태미나
            [11] = (-20, true),  // 골절·출혈 확률
            [14] = (-10, true),  // 반동
            [15] = (-20, true),  // 무기·방어구 내구도 감소
            [16] = (-15, true),  // 에너지·수분 소모
            [17] = (-20, true),  // 은신처 생산 시간
            [24] = (30, true),   // 무기 숙련도 성장
            [25] = (-20, true),  // 삽탄·탄 빼기 시간
            [26] = (30, true),   // 수색 속도
            [27] = (25, true),   // 수술 속도
            [29] = (15, true),   // 탄창 교체 속도
            [30] = (10, false),  // 무기 인체공학 (고정값)
            [31] = (-20, true),  // 고장·과열
            [32] = (5, true),    // 이동 속도
        };

        // 게임 보너스 합에 더해지는 % 특전 (dev-docs 2_특전.md 3절 표의 %p). 스킬·은신처 보너스와 더해져서 체감 비율은 상황마다 다르다.
        // 화면 단위는 언어 문구가 정한다 (한국어 %p, 영어 %). 나머지 % 특전은 게임 결과에 곱한다
        private static readonly HashSet<int> Points = new HashSet<int> { 1, 2, 3, 4, 8, 9, 10, 14, 27 };

        public static bool IsKnown(int perk) => Table.ContainsKey(perk);

        /// <summary>III 효과량 (부호 포함, % 는 숫자만). 모르는 특전은 0.</summary>
        public static double Amount(int perk) => Table.TryGetValue(perk, out var entry) ? entry.Amount : 0;

        /// <summary>III 효과량을 0~1 비율로 (30% → 0.3, 인체공학 10 → 10). 패치 계수에 쓴다.</summary>
        public static double Fraction(int perk) =>
            Table.TryGetValue(perk, out var entry) ? Math.Abs(entry.Percent ? entry.Amount / 100 : entry.Amount) : 0;

        /// <summary>tier 단계(1~3)의 효과 비율. 서버가 보낸 값이 없거나 모자라면 기본값.</summary>
        public static double Ratio(IList<double> ratios, int tier)
        {
            if (tier < 1 || tier > 3)
            {
                return 0;
            }

            return ratios != null && ratios.Count >= tier ? ratios[tier - 1] : DefaultRatios[tier - 1];
        }

        /// <summary>
        /// 효과 수치 문구: "+15%", "−7.5%", "+7.5". 소수 첫째 자리까지 반올림한다 (게임 적용 값은 반올림하지 않는다).
        /// pointUnit 은 더해지는 특전(<see cref="Points"/>)의 단위다 ("+22.5%p").
        /// </summary>
        public static string Format(int perk, double ratio, string pointUnit = "%")
        {
            if (!Table.TryGetValue(perk, out var entry))
            {
                return "";
            }

            var value = Math.Round(entry.Amount * ratio, 1, MidpointRounding.AwayFromZero);
            var sign = value < 0 ? Minus : "+";
            var unit = !entry.Percent ? "" : Points.Contains(perk) ? pointUnit : "%";
            return sign + Math.Abs(value).ToString("0.#", CultureInfo.InvariantCulture) + unit;
        }
    }
}
