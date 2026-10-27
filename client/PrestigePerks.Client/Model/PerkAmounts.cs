using System;
using System.Collections.Generic;
using System.Globalization;

namespace PrestigePerks.Model
{
    /// <summary>
    /// 단계 III 효과량은 서버 카탈로그(state catalog)에서 받는다. 단계 I·II 는 효과 비율을 곱한다.
    /// 게임 형식에 의존하지 않아 단위 테스트로 검증한다.
    /// </summary>
    public static class PerkAmounts
    {
        /// <summary>단계 비율을 서버에서 받지 못했을 때 쓰는 기본값 (I, II, III).</summary>
        public static readonly double[] DefaultRatios = { 0.5, 0.75, 1.0 };

        /// <summary>감소 부호. 문구 파일과 같은 글자 (U+2212).</summary>
        public const string Minus = "−";

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
        /// pointUnit 은 더해지는 특전(단위 points)의 단위다 ("+22.5%p"). 모르는 단위는 percent 로 본다.
        /// </summary>
        public static string Format(PerkCatalogEntryDto info, double ratio, string pointUnit = "%")
        {
            if (info == null)
            {
                return "";
            }

            var value = Math.Round(info.Amount * ratio, 1, MidpointRounding.AwayFromZero);
            var sign = value < 0 ? Minus : "+";
            var unit = info.Unit == PerkCatalogEntryDto.Flat ? "" : info.Unit == PerkCatalogEntryDto.Points ? pointUnit : "%";
            return sign + Math.Abs(value).ToString("0.#", CultureInfo.InvariantCulture) + unit;
        }
    }
}
