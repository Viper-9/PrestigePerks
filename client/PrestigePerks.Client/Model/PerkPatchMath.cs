using System;
using System.Collections.Generic;

namespace PrestigePerks.Model
{
    /// <summary>
    /// 클라이언트 패치 특전의 계수와 범위 자르기 (dev-docs 2_특전.md 6절 "클라이언트 패치 특전 (3차)").
    /// 계수는 단계 III 값이고 서버 catalog 의 III 효과량(서버 config perkAmounts)으로 정한다 (<see cref="Configure"/>).
    /// scale 은 단계 효과 비율(그 단계 효과량 ÷ III, 기본 I 0.5, II 0.75, III 1)이고 배수가 아니라 효과량에 곱한다 (×0.8 의 I 은 ×0.9).
    /// 게임 형식에 의존하지 않아 단위 테스트로 검증한다. 결과가 NaN·무한대이거나 바닐라가 만들 수 없는 범위면 바닐라 값을 그대로 쓴다.
    /// </summary>
    public static class PerkPatchMath
    {
        // 계수는 모두 단계 III 값이다. 주석의 수치는 기본값이고, state 를 받으면 서버 catalog 값으로 바뀐다 (Configure)
        public static float PorterWeight { get; private set; }       // 9: 근력 버프에 +0.2
        public static float StaminaBuff { get; private set; }        // 10: 지구력 버프에 +0.15
        public static float ConsumptionFactor { get; private set; }  // 16: 에너지·수분 소모 ×0.85
        public static float MasteringFactor { get; private set; }    // 24: 숙련도 증가 ×1.3
        public static float SearchFactor { get; private set; }       // 26: 수색 속도 ×1.3
        public static float ChanceFactor { get; private set; }       // 11: 골절·출혈 확률 ×0.8
        public static float RecoilBuff { get; private set; }         // 14: 무기 종류 스킬 반동 버프 x 에 +0.1
        public static float WeaponWearFactor { get; private set; }   // 15: 사격 시 무기 내구도 손실 ×0.8
        public static float LoadTimeFactor { get; private set; }     // 25: 삽탄·탄 빼기 1발 시간 ×0.8
        public static float ArmorWearFactor { get; private set; }    // 15: 방어구 내구도 감소 ×0.8 (28번을 합쳤다)
        public static float ReloadFactor { get; private set; }       // 29: 재장전 애니메이션 속도 ×1.15
        public static float ErgonomicsPoints { get; private set; }   // 30: 레이드 인체공학에 고정 +10
        public static float MalfunctionFactor { get; private set; }  // 31: 사격 고장 확률 ×0.8
        public static float OverheatFactor { get; private set; }     // 31: 발당 과열 ×0.8
        public static float MoveSpeedFactor { get; private set; }    // 32: 걷기·전력질주 이동량 ×1.05
        public static float SurgeryBuff { get; private set; }        // 27: 수술 속도 버프에 +0.25 (효과 시간만)
        public static float CraftTimeFactor { get; private set; }    // 17: 은신처 생산 시간 ×0.8

        static PerkPatchMath()
        {
            Configure(null);
        }

        /// <summary>
        /// 계수를 서버 catalog 의 단계 III 효과량(amount)으로 정한다. state 를 받을 때마다 부른다 (PerkEffects.Set).
        /// 특전이 catalog 에 없거나 쓸 수 없는 값(숫자 아님, 배수 0 이하)이면 기본값이다. 기본값은 서버 기본 수치와 같다 (테스트가 확인).
        /// % 는 배수 <c>1 + amount/100</c>, 더하는 값(points)은 <c>|amount|/100</c>, 고정값(flat)은 <c>|amount|</c>다.
        /// </summary>
        public static void Configure(IList<PerkCatalogEntryDto> catalog)
        {
            PorterWeight = Points(catalog, "porter", 0.2f);         // +20 → +0.2
            StaminaBuff = Points(catalog, "stamina", 0.15f);
            ChanceFactor = Factor(catalog, "tough", 0.8f);          // −20% → ×0.8
            RecoilBuff = Points(catalog, "control", 0.1f);          // 반동 −10 → 반동 버프 +0.1
            WeaponWearFactor = Factor(catalog, "maintenance", 0.8f);
            ArmorWearFactor = WeaponWearFactor;
            ConsumptionFactor = Factor(catalog, "temperance", 0.85f);
            CraftTimeFactor = Factor(catalog, "artisan", 0.8f);
            MasteringFactor = Factor(catalog, "mastery", 1.3f);
            LoadTimeFactor = Factor(catalog, "loading", 0.8f);
            SearchFactor = Factor(catalog, "search", 1.3f);
            SurgeryBuff = Points(catalog, "surgeon", 0.25f);
            ReloadFactor = Factor(catalog, "reload", 1.15f);
            ErgonomicsPoints = Flat(catalog, "handling", 10f);
            MalfunctionFactor = Factor(catalog, "reliability", 0.8f);
            OverheatFactor = MalfunctionFactor;
            MoveSpeedFactor = Factor(catalog, "agility", 1.05f);
        }

        /// <summary>개발용 로그: 지금 계수 (단계 III).</summary>
        public static string Describe()
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(culture,
                "porter +{0:0.###}, stamina +{1:0.###}, tough x{2:0.###}, control +{3:0.###}, maintenance x{4:0.###}, temperance x{5:0.###}, artisan x{6:0.###}, " +
                "mastery x{7:0.###}, loading x{8:0.###}, search x{9:0.###}, surgeon +{10:0.###}, reload x{11:0.###}, handling +{12:0.###}, reliability x{13:0.###}, agility x{14:0.###}",
                PorterWeight, StaminaBuff, ChanceFactor, RecoilBuff, WeaponWearFactor, ConsumptionFactor, CraftTimeFactor,
                MasteringFactor, LoadTimeFactor, SearchFactor, SurgeryBuff, ReloadFactor, ErgonomicsPoints, MalfunctionFactor, MoveSpeedFactor);
        }

        private static float Factor(IList<PerkCatalogEntryDto> catalog, string id, float fallback)
        {
            var entry = Find(catalog, id);
            var factor = entry != null ? (float)(1 + entry.Amount / 100) : fallback;
            return Finite(factor) && factor > 0f ? factor : fallback;
        }

        private static float Points(IList<PerkCatalogEntryDto> catalog, string id, float fallback)
        {
            var entry = Find(catalog, id);
            var value = entry != null ? (float)Math.Abs(entry.Amount / 100) : fallback;
            return Finite(value) && value > 0f ? value : fallback;
        }

        private static float Flat(IList<PerkCatalogEntryDto> catalog, string id, float fallback)
        {
            var entry = Find(catalog, id);
            var value = entry != null ? (float)Math.Abs(entry.Amount) : fallback;
            return Finite(value) && value > 0f ? value : fallback;
        }

        private static PerkCatalogEntryDto Find(IList<PerkCatalogEntryDto> catalog, string id)
        {
            if (catalog == null)
            {
                return null;
            }

            foreach (var entry in catalog)
            {
                if (entry != null && entry.Id == id)
                {
                    return entry;
                }
            }

            return null;
        }

        /// <summary>14: x 상한. 반동 힘 <c>1 − x − 0.1y</c>가 0 이하로 가지 않게.</summary>
        public const float MaxRecoilBuff = 0.8f;

        /// <summary>
        /// 패치 칸별 효과 비율 배열 (0 = 꺼짐, 칸 번호는 <see cref="PerkPatchSlots"/>). 적용 단계(서버 activeTiers, 특전 ID → 단계)를
        /// 특전마다 단계 비율(catalog tiers 의 그 단계 ÷ III, <see cref="PerkAmounts.Ratio"/>)로 바꾼다.
        /// 패치가 없는 특전(Bonus 특전, 모르는 ID)과 범위 밖 단계는 무시하고, 이번 실행 동안 끈 칸(disabled)은 켜지 않는다.
        /// </summary>
        public static float[] BuildScales(IDictionary<string, int> tiers, IList<PerkCatalogEntryDto> catalog, bool[] disabled)
        {
            var scales = new float[PerkPatchSlots.Count];
            if (tiers == null)
            {
                return scales;
            }

            foreach (var pair in tiers)
            {
                var slot = PerkPatchSlots.Index(pair.Key);
                var scale = (float)PerkAmounts.Ratio(Find(catalog, pair.Key), pair.Value);
                if (slot >= 0 && scale > 0f && Finite(scale) && !(disabled != null && slot < disabled.Length && disabled[slot]))
                {
                    scales[slot] = Math.Min(scale, 1f);
                }
            }

            return scales;
        }

        /// <summary>배수 factor(III)를 효과 비율만큼: <c>1 + (factor - 1) * scale</c>. ×0.8 의 I 은 ×0.9.</summary>
        public static float Toward(float factor, float scale) => 1f + (factor - 1f) * scale;

        /// <summary>9: <c>1 + 근력 버프</c> → <c>+ 0.2</c>.</summary>
        public static float CarryingWeight(float vanilla, float scale) => Positive(vanilla + PorterWeight * scale, vanilla);

        /// <summary>
        /// 9: 과적 한도 하나 (<c>설정 × 배율 + 고정</c>). 게임이 짐꾼 없는 배율로 계산한 값이면 짐꾼 배율로 다시 계산하고, 이미 짐꾼이 들어갔거나
        /// 다른 값(다른 모드가 바꿈)이면 그대로 둔다. 배율 getter 가 호출하는 쪽에 인라인되면 getter 패치를 거치지 않아 이렇게 고친다.
        /// </summary>
        /// <param name="current">게임이 계산한 한도.</param>
        /// <param name="config">전역 설정 한도 (배율 전).</param>
        /// <param name="vanillaModifier">짐꾼 없는 배율 (근력 × 체력 효과).</param>
        /// <param name="perkModifier">짐꾼을 넣은 배율.</param>
        /// <param name="absolute">고정 보너스 (체력 효과).</param>
        public static float OverweightLimit(float current, float config, float vanillaModifier, float perkModifier, float absolute)
        {
            var vanilla = config * vanillaModifier + absolute;
            return Math.Abs(current - vanilla) <= 0.01f * Math.Max(1f, Math.Abs(vanilla)) ? config * perkModifier + absolute : current;
        }

        /// <summary>10: <c>max(1, base × (1 + 지구력 버프) + 자극제)</c>에 <c>base × 0.15</c>를 더한다. 지구력 버프에 +0.15와 같다.</summary>
        public static float StaminaCapacity(float vanilla, float baseValue, float scale)
        {
            var value = Math.Max(1f, vanilla + baseValue * StaminaBuff * scale);
            return Finite(value) ? value : vanilla;
        }

        /// <summary>16: 초당 소모량 ×0.85. 음수(회복)는 바닐라에 없지만 들어오면 그대로 둔다.</summary>
        public static float Consumption(float vanilla, float scale) => vanilla > 0f && Finite(vanilla) ? vanilla * Toward(ConsumptionFactor, scale) : vanilla;

        /// <summary>24: 숙련도 증가량 ×1.3. 0 이하(감소)는 그대로.</summary>
        public static float Mastering(float vanilla, float scale) => vanilla > 0f && Finite(vanilla) ? vanilla * Toward(MasteringFactor, scale) : vanilla;

        /// <summary>
        /// 26: 수색 속도는 장비면 <c>1 + 주의력 + 수색</c>, 아니면 <c>1 + 주의력</c>이다 (ActiveSearchContentOperation.SearchContent).
        /// 주의력을 <c>1.3a + 0.3</c>, 수색을 <c>1.3s</c>로 바꾸면 두 식 모두 정확히 ×1.3이 된다.
        /// </summary>
        public static float AttentionLootSpeed(float vanilla, float scale)
        {
            var factor = Toward(SearchFactor, scale);
            var value = vanilla * factor + (factor - 1f);
            return Finite(value) && 1f + value > 0f ? value : vanilla;
        }

        /// <summary>26: <see cref="AttentionLootSpeed"/> 참고.</summary>
        public static float SearchBuffSpeed(float vanilla, float scale)
        {
            var value = vanilla * Toward(SearchFactor, scale);
            return Finite(value) ? value : vanilla;
        }

        /// <summary>
        /// 11: 바닐라 판정 <c>Random(0, cap) &lt; 확률</c>의 cap 을 ÷0.8 한다 (확률 ×0.8). cap 은 <c>1 + 스킬 버프</c>.
        /// </summary>
        public static float ChanceCap(float vanilla, float scale)
        {
            var value = vanilla / Toward(ChanceFactor, scale);
            return vanilla > 0f && Finite(value) ? value : vanilla;
        }

        /// <summary>14: 반동 버프 x 에 +0.1. <see cref="MaxRecoilBuff"/>를 넘기지 않고, 바닐라보다 작아지지 않는다.</summary>
        public static float RecoilSuppression(float vanilla, float scale)
        {
            var value = Math.Min(vanilla + RecoilBuff * scale, MaxRecoilBuff);
            return Finite(value) && value > vanilla ? value : vanilla;
        }

        /// <summary>
        /// 15: 손실은 <c>… × (1 − f)</c>다 (Weapon.GetDurabilityLossOnShot). <c>(1 − f') = (1 − f) × 0.8</c>가 되게 f 를 바꾼다.
        /// </summary>
        public static float WeaponTreatmentFactor(float vanilla, float scale)
        {
            var value = 1f - (1f - vanilla) * Toward(WeaponWearFactor, scale);
            return vanilla < 1f && Finite(value) ? value : vanilla;
        }

        /// <summary>25: 탄 1발 대기 시간 ×0.8.</summary>
        public static float LoadOneAmmoSpeed(float vanilla, float scale) => vanilla > 0f && Finite(vanilla) ? vanilla * Toward(LoadTimeFactor, scale) : vanilla;

        /// <summary>15: 방어구 내구도 감소량 ×0.8. 바닐라가 앞에서 최소 1을 적용한다.</summary>
        public static float ArmorDamage(float vanilla, float scale) => vanilla > 0f && Finite(vanilla) ? vanilla * Toward(ArmorWearFactor, scale) : vanilla;

        /// <summary>29: 재장전 애니메이션 속도 ×1.15.</summary>
        public static float ReloadSpeed(float vanilla, float scale) => vanilla > 0f && Finite(vanilla * Toward(ReloadFactor, scale)) ? vanilla * Toward(ReloadFactor, scale) : vanilla;

        /// <summary>
        /// 27: 바닐라는 파괴된 부위 수술 효과 시간을 <c>÷ (1 + 수술 속도)</c> 한다 (ActiveHealthController.DoMedEffect).
        /// 그 결과에 <c>(1 + s) ÷ (1 + s + 0.25 × 단계 비율)</c>를 곱해 버프 +0.25(III)와 같게 한다.
        /// </summary>
        public static float SurgeryTime(float vanilla, float surgerySpeed, float scale)
        {
            var value = vanilla * (1f + surgerySpeed) / (1f + surgerySpeed + SurgeryBuff * scale);
            return vanilla > 0f && 1f + surgerySpeed > 0f && Finite(value) ? value : vanilla;
        }

        public const float MinCraftSeconds = 5f;      // 17: 서버 최소 생산 시간

        /// <summary>17: 생산 시간 배율(ReductionCoefficient, <c>1 − 스킬 감소</c>) ×0.8.</summary>
        public static float CraftReduction(float vanilla, float scale) => vanilla > 0f && Finite(vanilla) ? vanilla * Toward(CraftTimeFactor, scale) : vanilla;

        /// <summary>17: 비트코인 속도 계수 ÷0.8. 그래픽카드가 없으면 0 그대로.</summary>
        public static float BitcoinSpeed(float vanilla, float scale) => vanilla > 0f && Finite(vanilla) ? vanilla / Toward(CraftTimeFactor, scale) : vanilla;

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
        /// 30: 레이드 인체공학(FirearmController.TotalErgonomicsFunc 결과, 0 이상)에 고정 +10.
        /// 배율이면 반동을 잡으려고 인체공학을 버린 총에서 효과가 거의 없어 고정값으로 한다 (dev-docs 기록/특전_최종_검수.md 1.2절).
        /// </summary>
        public static float Ergonomics(float vanilla, float scale) => vanilla >= 0f && Finite(vanilla) ? vanilla + ErgonomicsPoints * scale : vanilla;

        /// <summary>31: 고장 확률 ×0.8. 합계와 원인별 확률에 같은 배율을 써서 원인 분포는 바닐라와 같다.</summary>
        public static float Malfunction(float vanilla, float scale) => vanilla > 0f && Finite(vanilla) ? vanilla * Toward(MalfunctionFactor, scale) : vanilla;

        /// <summary>31: 내구도 고장 확률(double) ×0.8.</summary>
        public static double Malfunction(double vanilla, float scale) => vanilla > 0d && !double.IsNaN(vanilla) && !double.IsInfinity(vanilla) ? vanilla * Toward(MalfunctionFactor, scale) : vanilla;

        /// <summary>31: 발당 과열 ×0.8.</summary>
        public static float ShotOverheat(float vanilla, float scale) => vanilla > 0f && Finite(vanilla) ? vanilla * Toward(OverheatFactor, scale) : vanilla;

        /// <summary>32: 걷기·전력질주 프레임 이동량(수평 성분 하나) ×1.05.</summary>
        public static float MoveDistance(float vanilla, float scale) => Finite(vanilla) ? vanilla * Toward(MoveSpeedFactor, scale) : vanilla;

        private static float Positive(float value, float vanilla) => Finite(value) && value > 0f ? value : vanilla;

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
