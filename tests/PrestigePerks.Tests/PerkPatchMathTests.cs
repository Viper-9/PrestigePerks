using PrestigePerks.Model;

namespace PrestigePerks.Tests;

/// <summary>클라이언트 패치 특전 계수와 범위 (client Model/PerkPatchMath.cs, dev-docs 2_특전.md 6절 표).</summary>
public class PerkPatchMathTests
{
    private const float Tolerance = 1e-4f;

    [Fact]
    public void Scales_come_from_applied_tiers_and_ignore_unknown_numbers_and_disabled_perks()
    {
        var disabled = new bool[PerkPatchMath.Size];
        disabled[10] = true;
        var tiers = new Dictionary<int, int> { [9] = 1, [10] = 3, [24] = 2, [32] = 3, [0] = 1, [-1] = 1, [33] = 1, [99] = 1, [26] = 0, [14] = 4 };

        var scales = PerkPatchMath.BuildScales(tiers, [0.5, 0.75, 1.0], disabled);

        Assert.Equal([9, 24, 32], Enumerable.Range(0, scales.Length).Where(perk => scales[perk] > 0));
        Assert.Equal([0.5f, 0.75f, 1f], new[] { scales[9], scales[24], scales[32] });
        Assert.All(PerkPatchMath.BuildScales(null!, null!, disabled), scale => Assert.Equal(0f, scale));
        Assert.Equal(PerkPatchMath.Size, PerkPatchMath.BuildScales(new Dictionary<int, int>(), null!, null!).Length);
        // 비율을 받지 못하면 기본값 (0.5, 0.75, 1)
        Assert.Equal(0.75f, PerkPatchMath.BuildScales(new Dictionary<int, int> { [9] = 2 }, null!, null!)[9]);
    }

    // 기획 README 4절 표: III 은 지금 수치, II 75%, I 50% (효과량에 곱한다)
    [Theory]
    [InlineData(0.5f, 0.9f, 1.111111f, 1.1f, 5f, 1.025f)]
    [InlineData(0.75f, 0.85f, 1.176471f, 1.15f, 7.5f, 1.0375f)]
    [InlineData(1f, 0.8f, 1.25f, 1.2f, 10f, 1.05f)]
    public void Lower_tiers_scale_the_effect_amount(float scale, float tough, float toughCap, float porter, float ergonomics, float agility)
    {
        Assert.Equal(tough, PerkPatchMath.Toward(PerkPatchMath.ChanceFactor, scale), Tolerance);
        Assert.Equal(toughCap, PerkPatchMath.ChanceCap(1f, scale), Tolerance);
        Assert.Equal(porter, PerkPatchMath.CarryingWeight(1f, scale), Tolerance);
        Assert.Equal(ergonomics, PerkPatchMath.Ergonomics(0f, scale), Tolerance);
        Assert.Equal(agility, PerkPatchMath.MoveDistance(1f, scale), Tolerance);
        Assert.Equal(1f - 0.1f * scale * 2f, PerkPatchMath.CraftReduction(1f, scale), Tolerance);
        Assert.Equal(1f / (1f - 0.2f * scale), PerkPatchMath.BitcoinSpeed(1f, scale), Tolerance);
    }

    [Fact]
    public void Tier_three_constants_match_the_shown_amounts()
    {
        Assert.Equal(PerkAmounts.Fraction(9), PerkPatchMath.PorterWeight, Tolerance);
        Assert.Equal(PerkAmounts.Fraction(10), PerkPatchMath.StaminaBuff, Tolerance);
        Assert.Equal(PerkAmounts.Fraction(11), 1 - PerkPatchMath.ChanceFactor, Tolerance);
        Assert.Equal(PerkAmounts.Fraction(14), PerkPatchMath.RecoilBuff, Tolerance);
        Assert.Equal(PerkAmounts.Fraction(15), 1 - PerkPatchMath.WeaponWearFactor, Tolerance);
        Assert.Equal(PerkAmounts.Fraction(15), 1 - PerkPatchMath.ArmorWearFactor, Tolerance);
        Assert.Equal(PerkAmounts.Fraction(16), 1 - PerkPatchMath.ConsumptionFactor, Tolerance);
        Assert.Equal(PerkAmounts.Fraction(17), 1 - PerkPatchMath.CraftTimeFactor, Tolerance);
        Assert.Equal(PerkAmounts.Fraction(24), PerkPatchMath.MasteringFactor - 1, Tolerance);
        Assert.Equal(PerkAmounts.Fraction(25), 1 - PerkPatchMath.LoadTimeFactor, Tolerance);
        Assert.Equal(PerkAmounts.Fraction(26), PerkPatchMath.SearchFactor - 1, Tolerance);
        Assert.Equal(PerkAmounts.Fraction(27), PerkPatchMath.SurgeryBuff, Tolerance);
        Assert.Equal(PerkAmounts.Fraction(29), PerkPatchMath.ReloadFactor - 1, Tolerance);
        Assert.Equal(PerkAmounts.Fraction(30), PerkPatchMath.ErgonomicsPoints, Tolerance);
        Assert.Equal(PerkAmounts.Fraction(31), 1 - PerkPatchMath.MalfunctionFactor, Tolerance);
        Assert.Equal(PerkAmounts.Fraction(31), 1 - PerkPatchMath.OverheatFactor, Tolerance);
        Assert.Equal(PerkAmounts.Fraction(32), PerkPatchMath.MoveSpeedFactor - 1, Tolerance);
    }

    [Theory]
    [InlineData(1.0f, 1.2f)]  // 근력 0
    [InlineData(1.3f, 1.5f)]  // 근력 엘리트
    public void Porter_adds_to_strength_buff(float vanilla, float expected) =>
        Assert.Equal(expected, PerkPatchMath.CarryingWeight(vanilla, 1f), Tolerance);

    // 레이드 실측 (2026-10-21): 설정 26/77, 근력 1.3 → 게임 값 33.8/100.1, 짐꾼 1.5 → 39/115.5
    [Theory]
    [InlineData(33.8f, 26f, 39f)]       // 짐꾼 없이 계산됨 → 다시 계산
    [InlineData(100.1f, 77f, 115.5f)]
    [InlineData(39f, 26f, 39f)]         // 이미 짐꾼이 들어감 → 그대로
    [InlineData(9000f, 26f, 9000f)]     // 다른 모드가 바꿈 (봇 무제한 등) → 그대로
    public void Porter_fixes_overweight_limits_computed_without_it(float current, float config, float expected) =>
        Assert.Equal(expected, PerkPatchMath.OverweightLimit(current, config, 1.3f, 1.5f, 0f), Tolerance);

    [Fact]
    public void Porter_overweight_limits_keep_the_absolute_bonus() =>
        Assert.Equal(26f * 1.5f + 10f, PerkPatchMath.OverweightLimit(26f * 1.3f + 10f, 26f, 1.3f, 1.5f, 10f), Tolerance);

    [Theory]
    [InlineData(138f, 115f, 155.25f)]    // 지구력 0.2: 115 × 1.2 → 115 × 1.35
    [InlineData(195.5f, 115f, 212.75f)]  // 지구력 엘리트 0.7
    [InlineData(299f, 230f, 333.5f)]       // SVM 이 Capacity 를 230 으로 바꾼 경우도 같은 비율
    [InlineData(1f, 0f, 1f)]             // 최소 1 유지
    public void Stamina_adds_fifteen_percent_of_capacity(float vanilla, float capacity, float expected) =>
        Assert.Equal(expected, PerkPatchMath.StaminaCapacity(vanilla, capacity, 1f), Tolerance);

    [Fact]
    public void Consumption_and_mastering_scale_only_positive_values()
    {
        // 엘리트 체력 0.3: 3.2 × 0.7 → × 0.85
        Assert.Equal(3.2f * 0.7f * 0.85f, PerkPatchMath.Consumption(3.2f * 0.7f, 1f), Tolerance);
        Assert.Equal(0f, PerkPatchMath.Consumption(0f, 1f));
        Assert.Equal(-1f, PerkPatchMath.Consumption(-1f, 1f));

        Assert.Equal(2.6f, PerkPatchMath.Mastering(2f, 1f), Tolerance);
        Assert.Equal(-5f, PerkPatchMath.Mastering(-5f, 1f));
        Assert.Equal(0f, PerkPatchMath.Mastering(0f, 1f));
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.2f, 0.1f)]
    [InlineData(1.0f, 0.5f)] // 엘리트: 시체 2.5 → 3.25, 상자 2.0 → 2.6
    public void Search_speed_is_thirty_percent_faster_for_both_container_kinds(float attention, float search)
    {
        var a = PerkPatchMath.AttentionLootSpeed(attention, 1f);
        var s = PerkPatchMath.SearchBuffSpeed(search, 1f);

        // ActiveSearchContentOperation.SearchContent: 장비 1 + a + s, 그 외 1 + a
        Assert.Equal((1f + attention + search) * 1.3f, 1f + a + s, Tolerance);
        Assert.Equal((1f + attention) * 1.3f, 1f + a, Tolerance);
    }

    [Theory]
    [InlineData(1.0f)] // 스킬 0: 판정 cap 1 → 1.25, 확률 ×0.8
    [InlineData(1.6f)] // 엘리트: ÷1.6 → ÷2.0 (×0.5)
    public void Fracture_and_bleeding_cap_is_divided_by_0_8(float cap) =>
        Assert.Equal(cap / 0.8f, PerkPatchMath.ChanceCap(cap, 1f), Tolerance);

    [Theory]
    [InlineData(0f, 0.1f)]
    [InlineData(0.15f, 0.25f)]
    [InlineData(0.75f, 0.8f)] // 상한
    [InlineData(0.9f, 0.9f)]  // 바닐라가 상한보다 크면 그대로
    public void Recoil_buff_adds_ten_points_up_to_the_cap(float vanilla, float expected) =>
        Assert.Equal(expected, PerkPatchMath.RecoilSuppression(vanilla, 1f), Tolerance);

    [Theory]
    [InlineData(0f)]
    [InlineData(0.25f)] // 무기 관리 엘리트
    public void Weapon_wear_is_eighty_percent_of_vanilla(float treatment)
    {
        var factor = PerkPatchMath.WeaponTreatmentFactor(treatment, 1f);
        // Weapon.GetDurabilityLossOnShot: 손실 × (1 − factor)
        Assert.Equal((1f - treatment) * 0.8f, 1f - factor, Tolerance);
        Assert.Equal(1f, PerkPatchMath.WeaponTreatmentFactor(1f, 1f));
    }

    [Fact]
    public void Load_armor_reload_and_surgery_factors()
    {
        Assert.Equal(0.8f * 0.7f, PerkPatchMath.LoadOneAmmoSpeed(0.7f, 1f), Tolerance);
        Assert.Equal(0f, PerkPatchMath.LoadOneAmmoSpeed(0f, 1f));

        Assert.Equal(0.8f, PerkPatchMath.ArmorDamage(1f, 1f), Tolerance); // 바닐라 최소 1 → 0.8
        Assert.Equal(0f, PerkPatchMath.ArmorDamage(0f, 1f));

        Assert.Equal(1.38f, PerkPatchMath.ReloadSpeed(1.2f, 1f), Tolerance);

        // 엘리트 수술 0.4: 효과 시간 ÷1.4 → ÷1.65
        Assert.Equal(100f / 1.65f, PerkPatchMath.SurgeryTime(100f / 1.4f, 0.4f, 1f), Tolerance);
        Assert.Equal(100f / 1.25f, PerkPatchMath.SurgeryTime(100f, 0f, 1f), Tolerance);
        Assert.Equal(-1f, PerkPatchMath.SurgeryTime(-1f, 0.4f, 1f));
    }

    [Fact]
    public void Handling_adds_ten_ergonomics_points()
    {
        Assert.Equal(32f, PerkPatchMath.Ergonomics(22f, 1f), Tolerance);
        Assert.Equal(10f, PerkPatchMath.Ergonomics(0f, 1f), Tolerance); // 인체공학 0 총도 받는다
        Assert.Equal(110f, PerkPatchMath.Ergonomics(100f, 1f), Tolerance); // 조준 속도는 바닐라가 100에서 자른다
        Assert.Equal(-1f, PerkPatchMath.Ergonomics(-1f, 1f));
        Assert.Equal(float.NaN, PerkPatchMath.Ergonomics(float.NaN, 1f));
    }

    [Fact]
    public void Agility_scales_move_distance_by_1_05()
    {
        // 수평 이동량 성분. 뒤로 걷기(음수)도 같은 배율
        Assert.Equal(0.1f * 1.05f, PerkPatchMath.MoveDistance(0.1f, 1f), Tolerance);
        Assert.Equal(-0.1f * 1.05f, PerkPatchMath.MoveDistance(-0.1f, 1f), Tolerance);
        Assert.Equal(0f, PerkPatchMath.MoveDistance(0f, 1f));
        Assert.Equal(float.NaN, PerkPatchMath.MoveDistance(float.NaN, 1f));
    }

    [Fact]
    public void Reliability_scales_malfunction_chances_and_heat_by_0_8()
    {
        Assert.Equal(0.008f, PerkPatchMath.Malfunction(0.01f, 1f), Tolerance);
        Assert.Equal(0.0008d, PerkPatchMath.Malfunction(0.001d, 1f), 1e-9);
        Assert.Equal(0f, PerkPatchMath.Malfunction(0f, 1f));
        Assert.Equal(0d, PerkPatchMath.Malfunction(0d, 1f));
        Assert.Equal(double.NaN, PerkPatchMath.Malfunction(double.NaN, 1f));

        Assert.Equal(1.6f, PerkPatchMath.ShotOverheat(2f, 1f), Tolerance);
        Assert.Equal(0f, PerkPatchMath.ShotOverheat(0f, 1f));
        Assert.Equal(float.PositiveInfinity, PerkPatchMath.ShotOverheat(float.PositiveInfinity, 1f));
    }

    [Fact]
    public void Artisan_shortens_client_timers_with_the_server_floor()
    {
        Assert.Equal(0.625f * 0.8f, PerkPatchMath.CraftReduction(0.625f, 1f), Tolerance);
        Assert.Equal(0f, PerkPatchMath.CraftReduction(0f, 1f));

        // GPU 50장: 1 + 49 × 0.041225 → ÷0.8
        Assert.Equal(3.02f / 0.8f, PerkPatchMath.BitcoinSpeed(3.02f, 1f), Tolerance);
        Assert.Equal(0f, PerkPatchMath.BitcoinSpeed(0f, 1f)); // 그래픽카드 없음

        Assert.Equal(0.5f, PerkPatchMath.CraftReductionFloor(100f, 0.5f));      // 50초, 그대로
        Assert.Equal(5f, 6f * PerkPatchMath.CraftReductionFloor(6f, 0.4f), Tolerance); // 2.4초 → 5초
        Assert.Equal(0.4f, PerkPatchMath.CraftReductionFloor(4f, 0.4f)); // 바닐라 시간 자체가 5초 아래면 그대로
    }

    [Fact]
    public void Invalid_results_fall_back_to_vanilla()
    {
        Assert.Equal(float.NaN, PerkPatchMath.CarryingWeight(float.NaN, 1f));
        Assert.Equal(-0.5f, PerkPatchMath.CarryingWeight(-0.5f, 1f));
        Assert.Equal(float.PositiveInfinity, PerkPatchMath.StaminaCapacity(float.PositiveInfinity, 115f, 1f));
        Assert.Equal(float.NaN, PerkPatchMath.Consumption(float.NaN, 1f));
        Assert.Equal(float.NaN, PerkPatchMath.Mastering(float.NaN, 1f));
        Assert.Equal(-2f, PerkPatchMath.AttentionLootSpeed(-2f, 1f));
        Assert.Equal(float.NaN, PerkPatchMath.SearchBuffSpeed(float.NaN, 1f));
    }
}
