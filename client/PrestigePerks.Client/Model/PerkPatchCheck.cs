using System;

namespace PrestigePerks.Model
{
    /// <summary>패치 한 곳이 값을 바꾸는 방식 (<see cref="PerkPatchCheck.Expected"/>). None 은 자동 판정 없이 횟수·값만 남긴다.</summary>
    public enum PerkCheckForm
    {
        None,
        /// <summary>before + |값|/100 (짐꾼 근력 버프).</summary>
        Add,
        /// <summary>before + extra × |값|/100 (스태미나: extra 는 기본 최대치).</summary>
        AddBase,
        /// <summary>min(before + |값|/100, 0.8), 바닐라보다 작아지지 않음 (반동 버프).</summary>
        AddCapped,
        /// <summary>before + |값| (인체공학 고정값). before 가 0 이상일 때만.</summary>
        Flat,
        /// <summary>before × (1 + 값/100). before 가 양수일 때만.</summary>
        Factor,
        /// <summary>before × (1 + 값/100). 음수도 (이동량 성분).</summary>
        FactorSigned,
        /// <summary>before ÷ (1 + 값/100). before 가 양수일 때만 (골절·출혈 판정 상한, 비트코인 속도).</summary>
        Inverse,
        /// <summary>before × f + (f − 1), f = 1 + 값/100 (주의력 수색 속도).</summary>
        Search,
        /// <summary>1 − (1 − before) × (1 + 값/100). before 가 1 미만일 때만 (무기 내구도 손실 감소율).</summary>
        Treatment,
        /// <summary>before × (1 + extra) ÷ (1 + extra + |값|/100). before 가 양수일 때만 (수술 시간, extra 는 수술 속도 스킬).</summary>
        Surgery,
    }

    /// <summary>
    /// 개발자 모드 로그의 자동 판정 (F12, <c>[perk][CHECK]</c>). 패치가 바꾼 값을 서버 config 의 그 단계 수치(catalog tiers)에서 직접 계산한 값과 비교한다.
    /// 패치 계수(<see cref="PerkPatchMath"/>)를 거치지 않고 계산하므로 config → 서버 → state → 클라이언트 계수 → 패치 전체가 맞는지 본다.
    /// </summary>
    public static class PerkPatchCheck
    {
        /// <summary>tierValue 는 그 단계의 화면 수치 (부호 포함, 예: 반동 −40, 짐꾼 +80). NaN 이면 판정하지 않는다.</summary>
        public static float Expected(PerkCheckForm form, float before, double tierValue, float extra = 1f)
        {
            var points = (float)Math.Abs(tierValue / 100);
            var factor = (float)(1 + tierValue / 100);
            switch (form)
            {
                case PerkCheckForm.Add:
                    return before + points;
                case PerkCheckForm.AddBase:
                    return Math.Max(1f, before + extra * points);
                case PerkCheckForm.AddCapped:
                    return Math.Max(before, Math.Min(before + points, PerkPatchMath.MaxRecoilBuff));
                case PerkCheckForm.Flat:
                    return before >= 0f ? before + (float)Math.Abs(tierValue) : before;
                case PerkCheckForm.Factor:
                    return before > 0f ? before * factor : before;
                case PerkCheckForm.FactorSigned:
                    return before * factor;
                case PerkCheckForm.Inverse:
                    return before > 0f ? before / factor : before;
                case PerkCheckForm.Search:
                    return before * factor + (factor - 1f);
                case PerkCheckForm.Treatment:
                    return before < 1f ? 1f - (1f - before) * factor : before;
                case PerkCheckForm.Surgery:
                    return before > 0f ? before * (1f + extra) / (1f + extra + points) : before;
                default:
                    return float.NaN;
            }
        }

        /// <summary>계산 순서 차이만 넘기는 오차 (상대 0.1%, 0 근처는 절대 0.0001).</summary>
        public static bool Matches(float after, float expected) =>
            Math.Abs(after - expected) <= 1e-3f * Math.Max(0.1f, Math.Abs(expected));
    }
}
