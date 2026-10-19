using System;
using System.Collections.Generic;

namespace PrestigePerks.Model
{
    /// <summary>
    /// 클라이언트 패치 특전의 계수와 범위 자르기 (dev-docs 2_특전.md 6절 "클라이언트 패치 특전 (3차)").
    /// 게임 형식에 의존하지 않아 단위 테스트로 검증한다. 결과가 NaN·무한대이거나 바닐라가 만들 수 없는 범위면 바닐라 값을 그대로 쓴다.
    /// </summary>
    public static class PerkPatchMath
    {
        /// <summary>특전 번호로 찾는 배열 크기. 가장 큰 번호 32.</summary>
        public const int Size = 33;

        public const float PorterWeight = 0.15f;      // 9: 근력 버프에 +0.15
        public const float StaminaBuff = 0.15f;       // 10: 지구력 버프에 +0.15
        public const float ConsumptionFactor = 0.85f; // 16: 에너지·수분 소모 ×0.85
        public const float MasteringFactor = 1.2f;    // 24: 숙련도 증가 ×1.2
        public const float SearchFactor = 1.2f;       // 26: 수색 속도 ×1.2
        public const float ChanceFactor = 0.85f;      // 11: 골절·출혈 확률 ×0.85
        public const float RecoilBuff = 0.05f;        // 14: 무기 종류 스킬 반동 버프 x 에 +0.05
        public const float WeaponWearFactor = 0.8f;   // 15: 사격 시 무기 내구도 손실 ×0.8
        public const float LoadTimeFactor = 0.9f;     // 25: 삽탄·탄 빼기 1발 시간 ×0.9
        public const float ArmorWearFactor = 0.8f;    // 15: 방어구 내구도 감소 ×0.8 (28번을 합쳤다)
        public const float ReloadFactor = 1.1f;       // 29: 재장전 애니메이션 속도 ×1.1
        public const float ErgonomicsPoints = 5f;     // 30: 레이드 인체공학에 고정 +5
        public const float MalfunctionFactor = 0.8f;  // 31: 사격 고장 확률 ×0.8
        public const float OverheatFactor = 0.8f;     // 31: 발당 과열 ×0.8
        public const float MoveSpeedFactor = 1.05f;   // 32: 걷기·전력질주 이동량 ×1.05

        /// <summary>14: x 상한. 반동 힘 <c>1 − x − 0.1y</c>가 0 이하로 가지 않게.</summary>
        public const float MaxRecoilBuff = 0.8f;

        /// <summary>활성 특전 배열. 범위 밖 번호는 무시하고, 이번 실행 동안 끈 특전(disabled)은 켜지 않는다.</summary>
        public static bool[] BuildActive(IEnumerable<int> perks, bool[] disabled)
        {
            var active = new bool[Size];
            if (perks == null)
            {
                return active;
            }

            foreach (var perk in perks)
            {
                if (perk > 0 && perk < Size && !(disabled != null && perk < disabled.Length && disabled[perk]))
                {
                    active[perk] = true;
                }
            }

            return active;
        }

        /// <summary>9: <c>1 + 근력 버프</c> → <c>+ 0.15</c>.</summary>
        public static float CarryingWeight(float vanilla) => Positive(vanilla + PorterWeight, vanilla);

        /// <summary>10: <c>max(1, base × (1 + 지구력 버프) + 자극제)</c>에 <c>base × 0.15</c>를 더한다. 지구력 버프에 +0.15와 같다.</summary>
        public static float StaminaCapacity(float vanilla, float baseValue)
        {
            var value = Math.Max(1f, vanilla + baseValue * StaminaBuff);
            return Finite(value) ? value : vanilla;
        }

        /// <summary>16: 초당 소모량 ×0.85. 음수(회복)는 바닐라에 없지만 들어오면 그대로 둔다.</summary>
        public static float Consumption(float vanilla) => vanilla > 0f && Finite(vanilla) ? vanilla * ConsumptionFactor : vanilla;

        /// <summary>24: 숙련도 증가량 ×1.2. 0 이하(감소)는 그대로.</summary>
        public static float Mastering(float vanilla) => vanilla > 0f && Finite(vanilla) ? vanilla * MasteringFactor : vanilla;

        /// <summary>
        /// 26: 수색 속도는 장비면 <c>1 + 주의력 + 수색</c>, 아니면 <c>1 + 주의력</c>이다 (ActiveSearchContentOperation.SearchContent).
        /// 주의력을 <c>1.2a + 0.2</c>, 수색을 <c>1.2s</c>로 바꾸면 두 식 모두 정확히 ×1.2가 된다.
        /// </summary>
        public static float AttentionLootSpeed(float vanilla)
        {
            var value = vanilla * SearchFactor + (SearchFactor - 1f);
            return Finite(value) && 1f + value > 0f ? value : vanilla;
        }

        /// <summary>26: <see cref="AttentionLootSpeed"/> 참고.</summary>
        public static float SearchBuffSpeed(float vanilla)
        {
            var value = vanilla * SearchFactor;
            return Finite(value) ? value : vanilla;
        }

        /// <summary>
        /// 11: 바닐라 판정 <c>Random(0, cap) &lt; 확률</c>의 cap 을 ÷0.85 한다 (확률 ×0.85). cap 은 <c>1 + 스킬 버프</c>.
        /// </summary>
        public static float ChanceCap(float vanilla)
        {
            var value = vanilla / ChanceFactor;
            return vanilla > 0f && Finite(value) ? value : vanilla;
        }

        /// <summary>14: 반동 버프 x 에 +0.05. <see cref="MaxRecoilBuff"/>를 넘기지 않고, 바닐라보다 작아지지 않는다.</summary>
        public static float RecoilSuppression(float vanilla)
        {
            var value = Math.Min(vanilla + RecoilBuff, MaxRecoilBuff);
            return Finite(value) && value > vanilla ? value : vanilla;
        }

        /// <summary>
        /// 15: 손실은 <c>… × (1 − f)</c>다 (Weapon.GetDurabilityLossOnShot). <c>(1 − f') = (1 − f) × 0.8</c>가 되게 f 를 바꾼다.
        /// </summary>
        public static float WeaponTreatmentFactor(float vanilla)
        {
            var value = 1f - (1f - vanilla) * WeaponWearFactor;
            return vanilla < 1f && Finite(value) ? value : vanilla;
        }

        /// <summary>25: 탄 1발 대기 시간 ×0.9.</summary>
        public static float LoadOneAmmoSpeed(float vanilla) => vanilla > 0f && Finite(vanilla) ? vanilla * LoadTimeFactor : vanilla;

        /// <summary>15: 방어구 내구도 감소량 ×0.8. 바닐라가 앞에서 최소 1을 적용한다.</summary>
        public static float ArmorDamage(float vanilla) => vanilla > 0f && Finite(vanilla) ? vanilla * ArmorWearFactor : vanilla;

        /// <summary>29: 재장전 애니메이션 속도 ×1.1.</summary>
        public static float ReloadSpeed(float vanilla) => vanilla > 0f && Finite(vanilla * ReloadFactor) ? vanilla * ReloadFactor : vanilla;

        public const float SurgeryBuff = 0.15f;       // 27: 수술 속도 버프에 +0.15 (효과 시간만)

        /// <summary>
        /// 27: 바닐라는 파괴된 부위 수술 효과 시간을 <c>÷ (1 + 수술 속도)</c> 한다 (ActiveHealthController.DoMedEffect).
        /// 그 결과에 <c>(1 + s) ÷ (1 + s + 0.15)</c>를 곱해 버프 +0.15와 같게 한다.
        /// </summary>
        public static float SurgeryTime(float vanilla, float surgerySpeed)
        {
            var value = vanilla * (1f + surgerySpeed) / (1f + surgerySpeed + SurgeryBuff);
            return vanilla > 0f && 1f + surgerySpeed > 0f && Finite(value) ? value : vanilla;
        }

        public const float CraftTimeFactor = 0.8f;    // 17: 은신처 생산 시간 ×0.8
        public const float MinCraftSeconds = 5f;      // 17: 서버 최소 생산 시간

        /// <summary>17: 생산 시간 배율(ReductionCoefficient, <c>1 − 스킬 감소</c>) ×0.8.</summary>
        public static float CraftReduction(float vanilla) => vanilla > 0f && Finite(vanilla) ? vanilla * CraftTimeFactor : vanilla;

        /// <summary>17: 비트코인 속도 계수 ÷0.8. 그래픽카드가 없으면 0 그대로.</summary>
        public static float BitcoinSpeed(float vanilla) => vanilla > 0f && Finite(vanilla) ? vanilla / CraftTimeFactor : vanilla;

        /// <summary>
        /// 17: 서버처럼 최소 5초. <c>baseTime × reduction</c>이 5초보다 짧아지면 5초가 되는 배율을 돌려준다. 바닐라 시간이 이미 5초 아래면 그대로.
        /// </summary>
        public static float CraftReductionFloor(float baseTime, float reduction)
        {
            if (!(baseTime > MinCraftSeconds) || !(reduction > 0f) || baseTime * reduction >= MinCraftSeconds)
            {
                return reduction;
            }

            return MinCraftSeconds / baseTime;
        }

        /// <summary>
        /// 30: 레이드 인체공학(FirearmController.TotalErgonomicsFunc 결과, 0 이상)에 고정 +5.
        /// 배율이면 반동을 잡으려고 인체공학을 버린 총에서 효과가 거의 없어 고정값으로 한다 (dev-docs 기록/특전_최종_검수.md 1.2절).
        /// </summary>
        public static float Ergonomics(float vanilla) => vanilla >= 0f && Finite(vanilla) ? vanilla + ErgonomicsPoints : vanilla;

        /// <summary>31: 고장 확률 ×0.8. 합계와 원인별 확률에 같은 배율을 써서 원인 분포는 바닐라와 같다.</summary>
        public static float Malfunction(float vanilla) => vanilla > 0f && Finite(vanilla) ? vanilla * MalfunctionFactor : vanilla;

        /// <summary>31: 내구도 고장 확률(double) ×0.8.</summary>
        public static double Malfunction(double vanilla) => vanilla > 0d && !double.IsNaN(vanilla) && !double.IsInfinity(vanilla) ? vanilla * MalfunctionFactor : vanilla;

        /// <summary>31: 발당 과열 ×0.8.</summary>
        public static float ShotOverheat(float vanilla) => vanilla > 0f && Finite(vanilla) ? vanilla * OverheatFactor : vanilla;

        /// <summary>32: 걷기·전력질주 프레임 이동량(수평 성분 하나) ×1.05.</summary>
        public static float MoveDistance(float vanilla) => Finite(vanilla) ? vanilla * MoveSpeedFactor : vanilla;

        private static float Positive(float value, float vanilla) => Finite(value) && value > 0f ? value : vanilla;

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
