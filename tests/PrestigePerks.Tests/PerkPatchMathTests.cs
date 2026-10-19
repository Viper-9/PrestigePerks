using PrestigePerks.Model;

namespace PrestigePerks.Tests;

/// <summary>클라이언트 패치 특전 계수와 범위 (client Model/PerkPatchMath.cs, dev-docs 2_특전.md 6절 표).</summary>
public class PerkPatchMathTests
{
    private const float Tolerance = 1e-4f;

    [Fact]
    public void Active_set_ignores_unknown_numbers_and_disabled_perks()
    {
        var disabled = new bool[PerkPatchMath.Size];
        disabled[10] = true;

        var active = PerkPatchMath.BuildActive([9, 10, 24, 0, -1, 32, 33, 99], disabled);

        Assert.Equal([9, 24, 32], Enumerable.Range(0, active.Length).Where(perk => active[perk]));
        Assert.All(PerkPatchMath.BuildActive(null!, disabled), Assert.False);
        Assert.Equal(PerkPatchMath.Size, PerkPatchMath.BuildActive([], null!).Length);
    }

    [Theory]
    [InlineData(1.0f, 1.15f)] // 근력 0
    [InlineData(1.3f, 1.45f)] // 근력 엘리트
    public void Porter_adds_to_strength_buff(float vanilla, float expected) =>
        Assert.Equal(expected, PerkPatchMath.CarryingWeight(vanilla), Tolerance);

    [Theory]
    [InlineData(138f, 115f, 155.25f)]    // 지구력 0.2: 115 × 1.2 → 115 × 1.35
    [InlineData(195.5f, 115f, 212.75f)]  // 지구력 엘리트 0.7
    [InlineData(299f, 230f, 333.5f)]       // SVM 이 Capacity 를 230 으로 바꾼 경우도 같은 비율
    [InlineData(1f, 0f, 1f)]             // 최소 1 유지
    public void Stamina_adds_fifteen_percent_of_capacity(float vanilla, float capacity, float expected) =>
        Assert.Equal(expected, PerkPatchMath.StaminaCapacity(vanilla, capacity), Tolerance);

    [Fact]
    public void Consumption_and_mastering_scale_only_positive_values()
    {
        // 엘리트 체력 0.3: 3.2 × 0.7 → × 0.85
        Assert.Equal(3.2f * 0.7f * 0.85f, PerkPatchMath.Consumption(3.2f * 0.7f), Tolerance);
        Assert.Equal(0f, PerkPatchMath.Consumption(0f));
        Assert.Equal(-1f, PerkPatchMath.Consumption(-1f));

        Assert.Equal(2.4f, PerkPatchMath.Mastering(2f), Tolerance);
        Assert.Equal(-5f, PerkPatchMath.Mastering(-5f));
        Assert.Equal(0f, PerkPatchMath.Mastering(0f));
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.2f, 0.1f)]
    [InlineData(1.0f, 0.5f)] // 엘리트: 시체 2.5 → 3.0, 상자 2.0 → 2.4
    public void Search_speed_is_twenty_percent_faster_for_both_container_kinds(float attention, float search)
    {
        var a = PerkPatchMath.AttentionLootSpeed(attention);
        var s = PerkPatchMath.SearchBuffSpeed(search);

        // ActiveSearchContentOperation.SearchContent: 장비 1 + a + s, 그 외 1 + a
        Assert.Equal((1f + attention + search) * 1.2f, 1f + a + s, Tolerance);
        Assert.Equal((1f + attention) * 1.2f, 1f + a, Tolerance);
    }

    [Theory]
    [InlineData(1.0f)] // 스킬 0: 판정 cap 1 → 1.176, 확률 ×0.85
    [InlineData(1.6f)] // 엘리트: ÷1.6 → ÷1.882 (×0.531)
    public void Fracture_and_bleeding_cap_is_divided_by_0_85(float cap) =>
        Assert.Equal(cap / 0.85f, PerkPatchMath.ChanceCap(cap), Tolerance);

    [Theory]
    [InlineData(0f, 0.05f)]
    [InlineData(0.15f, 0.2f)]
    [InlineData(0.79f, 0.8f)] // 상한
    [InlineData(0.9f, 0.9f)]  // 바닐라가 상한보다 크면 그대로
    public void Recoil_buff_adds_five_points_up_to_the_cap(float vanilla, float expected) =>
        Assert.Equal(expected, PerkPatchMath.RecoilSuppression(vanilla), Tolerance);

    [Theory]
    [InlineData(0f)]
    [InlineData(0.25f)] // 무기 관리 엘리트
    public void Weapon_wear_is_eighty_percent_of_vanilla(float treatment)
    {
        var factor = PerkPatchMath.WeaponTreatmentFactor(treatment);
        // Weapon.GetDurabilityLossOnShot: 손실 × (1 − factor)
        Assert.Equal((1f - treatment) * 0.8f, 1f - factor, Tolerance);
        Assert.Equal(1f, PerkPatchMath.WeaponTreatmentFactor(1f));
    }

    [Fact]
    public void Load_armor_reload_and_surgery_factors()
    {
        Assert.Equal(0.9f * 0.7f, PerkPatchMath.LoadOneAmmoSpeed(0.7f), Tolerance);
        Assert.Equal(0f, PerkPatchMath.LoadOneAmmoSpeed(0f));

        Assert.Equal(0.8f, PerkPatchMath.ArmorDamage(1f), Tolerance); // 바닐라 최소 1 → 0.8
        Assert.Equal(0f, PerkPatchMath.ArmorDamage(0f));

        Assert.Equal(1.32f, PerkPatchMath.ReloadSpeed(1.2f), Tolerance);

        // 엘리트 수술 0.4: 효과 시간 ÷1.4 → ÷1.6
        Assert.Equal(100f / 1.55f, PerkPatchMath.SurgeryTime(100f / 1.4f, 0.4f), Tolerance);
        Assert.Equal(100f / 1.15f, PerkPatchMath.SurgeryTime(100f, 0f), Tolerance);
        Assert.Equal(-1f, PerkPatchMath.SurgeryTime(-1f, 0.4f));
    }

    [Fact]
    public void Handling_adds_five_ergonomics_points()
    {
        Assert.Equal(27f, PerkPatchMath.Ergonomics(22f), Tolerance);
        Assert.Equal(5f, PerkPatchMath.Ergonomics(0f), Tolerance); // 인체공학 0 총도 받는다
        Assert.Equal(105f, PerkPatchMath.Ergonomics(100f), Tolerance); // 조준 속도는 바닐라가 100에서 자른다
        Assert.Equal(-1f, PerkPatchMath.Ergonomics(-1f));
        Assert.Equal(float.NaN, PerkPatchMath.Ergonomics(float.NaN));
    }

    [Fact]
    public void Agility_scales_move_distance_by_1_05()
    {
        // 수평 이동량 성분. 뒤로 걷기(음수)도 같은 배율
        Assert.Equal(0.1f * 1.05f, PerkPatchMath.MoveDistance(0.1f), Tolerance);
        Assert.Equal(-0.1f * 1.05f, PerkPatchMath.MoveDistance(-0.1f), Tolerance);
        Assert.Equal(0f, PerkPatchMath.MoveDistance(0f));
        Assert.Equal(float.NaN, PerkPatchMath.MoveDistance(float.NaN));
    }

    [Fact]
    public void Reliability_scales_malfunction_chances_and_heat_by_0_8()
    {
        Assert.Equal(0.008f, PerkPatchMath.Malfunction(0.01f), Tolerance);
        Assert.Equal(0.0008d, PerkPatchMath.Malfunction(0.001d), 1e-9);
        Assert.Equal(0f, PerkPatchMath.Malfunction(0f));
        Assert.Equal(0d, PerkPatchMath.Malfunction(0d));
        Assert.Equal(double.NaN, PerkPatchMath.Malfunction(double.NaN));

        Assert.Equal(1.6f, PerkPatchMath.ShotOverheat(2f), Tolerance);
        Assert.Equal(0f, PerkPatchMath.ShotOverheat(0f));
        Assert.Equal(float.PositiveInfinity, PerkPatchMath.ShotOverheat(float.PositiveInfinity));
    }

    [Fact]
    public void Artisan_shortens_client_timers_with_the_server_floor()
    {
        Assert.Equal(0.625f * 0.8f, PerkPatchMath.CraftReduction(0.625f), Tolerance);
        Assert.Equal(0f, PerkPatchMath.CraftReduction(0f));

        // GPU 50장: 1 + 49 × 0.041225 → ÷0.8
        Assert.Equal(3.02f / 0.8f, PerkPatchMath.BitcoinSpeed(3.02f), Tolerance);
        Assert.Equal(0f, PerkPatchMath.BitcoinSpeed(0f)); // 그래픽카드 없음

        Assert.Equal(0.5f, PerkPatchMath.CraftReductionFloor(100f, 0.5f));      // 50초, 그대로
        Assert.Equal(5f, 6f * PerkPatchMath.CraftReductionFloor(6f, 0.4f), Tolerance); // 2.4초 → 5초
        Assert.Equal(0.4f, PerkPatchMath.CraftReductionFloor(4f, 0.4f)); // 바닐라 시간 자체가 5초 아래면 그대로
    }

    [Fact]
    public void Invalid_results_fall_back_to_vanilla()
    {
        Assert.Equal(float.NaN, PerkPatchMath.CarryingWeight(float.NaN));
        Assert.Equal(-0.5f, PerkPatchMath.CarryingWeight(-0.5f));
        Assert.Equal(float.PositiveInfinity, PerkPatchMath.StaminaCapacity(float.PositiveInfinity, 115f));
        Assert.Equal(float.NaN, PerkPatchMath.Consumption(float.NaN));
        Assert.Equal(float.NaN, PerkPatchMath.Mastering(float.NaN));
        Assert.Equal(-2f, PerkPatchMath.AttentionLootSpeed(-2f));
        Assert.Equal(float.NaN, PerkPatchMath.SearchBuffSpeed(float.NaN));
    }
}
