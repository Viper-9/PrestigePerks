using System;
using System.Collections.Generic;
using System.Globalization;

namespace PrestigePerks.Model
{
    /// <summary>
    /// 단계 I·II·III 효과량은 서버 카탈로그(state catalog 의 tiers, 서버 config perkAmounts)에서 받는다.
    /// 패치 효과 비율은 그 단계 효과량 ÷ III 효과량이다 (효과가 선형이라 III 계수에 곱하면 그 단계 효과가 된다).
    /// 게임 형식에 의존하지 않아 단위 테스트로 검증한다.
    /// </summary>
    public static class PerkAmounts
    {
        /// <summary>단계별 수치를 서버에서 받지 못했을 때 쓰는 비율 (I, II, III).</summary>
        public static readonly double[] DefaultRatios = { 0.5, 0.75, 1.0 };

        /// <summary>감소 부호. 문구 파일과 같은 글자 (U+2212).</summary>
        public const string Minus = "−";

        /// <summary>
        /// tier 단계(1~3)의 효과 비율: 그 단계 효과량 ÷ III. 서버가 단계별 수치를 보내지 않았거나 쓸 수 없는 값(III 이 0, 숫자 아님)이면 기본 비율.
        /// 범위 밖 단계는 0.
        /// </summary>
        public static double Ratio(PerkCatalogEntryDto info, int tier)
        {
            if (tier < 1 || tier > 3)
            {
                return 0;
            }

            var tiers = info?.Tiers;
            if (tiers == null || tiers.Count < 3 || tiers[2] == 0)
            {
                return DefaultRatios[tier - 1];
            }

            var ratio = tiers[tier - 1] / tiers[2];
            return double.IsNaN(ratio) || double.IsInfinity(ratio) ? DefaultRatios[tier - 1] : ratio;
        }

        /// <summary>
        /// 효과 수치 문구: "+15%", "−7.5%", "+7.5". 소수 첫째 자리까지 반올림한다 (게임 적용 값은 반올림하지 않는다).
        /// pointUnit 은 더해지는 특전(단위 points)의 단위다 ("+22.5%p"). 모르는 단위는 percent 로 본다.
        /// </summary>
        public static string Format(PerkCatalogEntryDto info, int tier, string pointUnit = "%")
        {
            if (info == null)
            {
                return "";
            }

            var value = Math.Round(info.Amount * Ratio(info, tier), 1, MidpointRounding.AwayFromZero);
            var sign = value < 0 ? Minus : "+";
            var unit = info.Unit == PerkCatalogEntryDto.Flat ? "" : info.Unit == PerkCatalogEntryDto.Points ? pointUnit : "%";
            return sign + Math.Abs(value).ToString("0.#", CultureInfo.InvariantCulture) + unit;
        }
    }
}
