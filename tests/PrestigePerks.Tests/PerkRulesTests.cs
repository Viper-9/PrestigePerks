using PrestigePerks.Perks;

namespace PrestigePerks.Tests;

public class PerkRulesTests
{
    private static readonly PerkConfig Config = new();

    private static PerkData WithSlots(params int[] perks) =>
        new() { Slots = perks.Select(perk => new PerkSlot { Perk = perk }).ToList() };

    /// <summary>단계에 들어선 상태 (무료 횟수가 채워지고 빈 슬롯이 뽑힘).</summary>
    private static PerkData Entered(int level, int seed, params int[] perks)
    {
        var data = WithSlots(perks);
        PerkRules.EnterLevel(data, level, Config, new Random(seed));
        return data;
    }

    // dev-docs 2_특전.md 2절 예시표: 3회차부터 고정
    [Theory]
    [InlineData(1, 0, 25, 50, 100)]
    [InlineData(3, 2, 90, 180, 360)]
    [InlineData(6, 0, 75, 150, 300)]
    [InlineData(6, 5, 263, 525, 1050)]
    public void Cost_matches_the_document_table_and_stays_fixed_from_round_three(int level, int locked, int first, int second, int third)
    {
        Assert.Equal(first, PerkRules.RerollCost(Config, level, locked, 1));
        Assert.Equal(second, PerkRules.RerollCost(Config, level, locked, 2));
        Assert.Equal(third, PerkRules.RerollCost(Config, level, locked, 3));
        Assert.Equal(third, PerkRules.RerollCost(Config, level, locked, 4));
        Assert.Equal(third, PerkRules.RerollCost(Config, level, locked, 50));
    }

    [Fact]
    public void Cap_round_comes_from_config()
    {
        var config = Config with { PaidCostCapRound = 1 };
        Assert.Equal(25, PerkRules.RerollCost(config, 1, 0, 5));
        Assert.Equal(200, PerkRules.RerollCost(Config with { PaidCostCapRound = 4 }, 1, 0, 9));
    }

    [Theory]
    [InlineData(1, 25)]
    [InlineData(2, 35)]
    [InlineData(3, 45)]
    [InlineData(4, 55)]
    [InlineData(5, 65)]
    [InlineData(6, 75)]
    [InlineData(7, 75)]
    public void Base_price_per_level(int level, int expected)
    {
        Assert.Equal(expected, Config.BasePrice(level));
    }

    [Fact]
    public void Entering_a_new_level_keeps_perks_draws_the_new_slot_and_refills_free_rerolls()
    {
        var data = WithSlots(1, 24);
        data.Rerolls = new PerkRerolls { Level = 2, FreeLeft = 0, Paid = 4 };

        var (changed, newPerks) = PerkRules.EnterLevel(data, 3, Config, new Random(1));

        Assert.True(changed);
        Assert.Equal(3, data.Slots.Count);
        Assert.Equal(1, data.Slots[0].Perk);
        Assert.Equal(24, data.Slots[1].Perk);
        Assert.DoesNotContain(data.Slots[2].Perk, new[] { 1, 24 });
        Assert.Equal([data.Slots[2].Perk], newPerks);
        Assert.Equal([data.Slots[2].Perk], data.UnseenNewPerks);
        Assert.Equal(3, data.Rerolls.Level);
        Assert.Equal(3, data.Rerolls.FreeLeft);
        Assert.Equal(0, data.Rerolls.Paid);
    }

    [Fact]
    public void Same_level_changes_nothing()
    {
        var data = Entered(2, 1);
        data.Rerolls.FreeLeft = 1;
        data.Rerolls.Paid = 2;
        data.UnseenNewPerks.Clear();

        var (changed, newPerks) = PerkRules.EnterLevel(data, 2, Config, new Random(2));

        Assert.False(changed);
        Assert.Empty(newPerks);
        Assert.Equal(1, data.Rerolls.FreeLeft);
        Assert.Equal(2, data.Rerolls.Paid);
    }

    [Fact]
    public void Retroactive_user_gets_all_slots_drawn()
    {
        var data = new PerkData();
        var (_, newPerks) = PerkRules.EnterLevel(data, 4, Config, new Random(2));

        Assert.Equal(4, data.Slots.Count);
        Assert.Equal(4, data.Slots.Select(slot => slot.Perk).Distinct().Count());
        Assert.Equal(4, newPerks.Count);
        Assert.Equal(3, data.Rerolls.FreeLeft);
    }

    [Fact]
    public void Level_zero_has_no_slots()
    {
        var data = new PerkData();
        Assert.False(PerkRules.EnterLevel(data, 0, Config, new Random()).Changed);
        Assert.Empty(data.Slots);
        Assert.Equal(PerkError.NoSlots, PerkRules.CheckRoll(data, 0));
        Assert.Equal(PerkError.NoSlots, PerkRules.Roll(data, 0, Config, new Random()).Error);
    }

    [Fact]
    public void Roll_never_duplicates_and_never_draws_locked_perks()
    {
        for (var seed = 0; seed < 500; seed++)
        {
            var random = new Random(seed);
            var data = Entered(6, seed);
            var lockedPerks = new[] { data.Slots[0].Perk, data.Slots[3].Perk };
            PerkRules.SetLock(data, 6, 0, true);
            PerkRules.SetLock(data, 6, 3, true);

            PerkRules.Roll(data, 6, Config, random);

            var perks = data.Slots.Select(slot => slot.Perk).ToList();
            Assert.Equal(6, perks.Distinct().Count());
            Assert.Equal(lockedPerks[0], perks[0]);
            Assert.Equal(lockedPerks[1], perks[3]);
            Assert.DoesNotContain(perks.Where((_, i) => i is not 0 and not 3), lockedPerks.Contains);
            Assert.All(perks, perk => Assert.True(PerkCatalog.IsKnown(perk)));
        }
    }

    [Fact]
    public void Unlocked_previous_perks_can_come_back()
    {
        var reappeared = false;
        for (var seed = 0; seed < 200 && !reappeared; seed++)
        {
            var data = Entered(1, seed, 1);
            PerkRules.Roll(data, 1, Config, new Random(seed));
            reappeared = data.Slots[0].Perk == 1;
        }

        Assert.True(reappeared);
    }

    [Fact]
    public void Three_free_rerolls_then_paid_rounds_double_and_stop_at_round_three()
    {
        var data = Entered(3, 4);
        var random = new Random(4);
        PerkRules.SetLock(data, 3, 0, true);
        PerkRules.SetLock(data, 3, 1, true);

        // 무료 3회는 잠금 개수와 상관없이 무료
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(0, PerkRules.NextRerollCost(Config, 3, data));
            var free = PerkRules.Roll(data, 3, Config, random);
            Assert.True(free.Success);
            Assert.Equal(0, free.Cost);
        }

        Assert.Equal(0, data.Rerolls.FreeLeft);

        // P3 잠금 2: 90 → 180 → 360 → 360
        Assert.Equal(90, PerkRules.NextRerollCost(Config, 3, data));
        Assert.Equal(90, PerkRules.Roll(data, 3, Config, random).Cost);
        Assert.Equal(180, PerkRules.Roll(data, 3, Config, random).Cost);
        Assert.Equal(360, PerkRules.Roll(data, 3, Config, random).Cost);
        Assert.Equal(360, PerkRules.Roll(data, 3, Config, random).Cost);
        Assert.Equal(4, data.Rerolls.Paid);
    }

    [Fact]
    public void Paid_cost_uses_lock_count_at_the_moment_of_rolling()
    {
        var data = Entered(3, 5);
        var random = new Random(5);
        data.Rerolls.FreeLeft = 0;

        Assert.Equal(45, PerkRules.Roll(data, 3, Config, random).Cost);   // 잠금 0, 1회차
        PerkRules.SetLock(data, 3, 0, true);
        Assert.Equal(135, PerkRules.Roll(data, 3, Config, random).Cost);  // 45 × 1.5 × 2 = 135
    }

    [Fact]
    public void Prestige_refills_free_rerolls_and_restarts_paid_rounds()
    {
        var data = Entered(1, 10);
        data.Rerolls.FreeLeft = 0;
        PerkRules.Roll(data, 1, Config, new Random(10));
        PerkRules.Roll(data, 1, Config, new Random(11));
        Assert.Equal(2, data.Rerolls.Paid);

        PerkRules.EnterLevel(data, 2, Config, new Random(12));

        Assert.Equal(0, data.Rerolls.Paid);
        Assert.Equal(3, data.Rerolls.FreeLeft);
        Assert.Equal(35, PerkRules.RerollCost(Config, 2, 0, data.Rerolls.Paid + 1));
    }

    [Fact]
    public void Free_rerolls_do_not_stack_across_levels()
    {
        var data = Entered(1, 13);
        Assert.Equal(3, data.Rerolls.FreeLeft);
        PerkRules.EnterLevel(data, 2, Config, new Random(14));
        Assert.Equal(3, data.Rerolls.FreeLeft);
    }

    [Fact]
    public void Roll_is_refused_when_everything_is_locked()
    {
        var data = Entered(1, 7);
        PerkRules.SetLock(data, 1, 0, true);

        Assert.Equal(PerkError.AllLocked, PerkRules.CheckRoll(data, 1));
        Assert.Equal(PerkError.AllLocked, PerkRules.Roll(data, 1, Config, new Random()).Error);
        Assert.Equal(3, data.Rerolls.FreeLeft);
    }

    [Fact]
    public void Lock_rejects_out_of_range_slots()
    {
        var data = Entered(2, 8);
        Assert.Equal(PerkError.None, PerkRules.SetLock(data, 2, 1, true));
        Assert.Equal(PerkError.InvalidSlot, PerkRules.SetLock(data, 2, 2, true));
        Assert.Equal(PerkError.InvalidSlot, PerkRules.SetLock(data, 2, -1, true));
    }

    [Fact]
    public void Active_perks_are_capped_by_prestige_level()
    {
        var data = WithSlots(1, 8, 24);
        Assert.Equal([1, 8], PerkRules.ActivePerks(data, 2));
        Assert.Empty(PerkRules.ActivePerks(data, 0));
    }

    [Fact]
    public void Dev_all_perks_applies_every_perk_regardless_of_slots()
    {
        var data = WithSlots(1, 8);
        data.DevAllPerks = true;
        Assert.Equal(PerkCatalog.Numbers, PerkRules.ActivePerks(data, 1));
        Assert.Equal(PerkCatalog.Numbers, PerkRules.ActivePerks(data, 0));
    }

    [Fact]
    public void Roll_does_not_draw_perks_held_in_slots_beyond_the_level()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var data = WithSlots(1, 2, 3);
            data.Rerolls = new PerkRerolls { Level = 3, FreeLeft = 3 };
            PerkRules.Roll(data, 1, Config, new Random(seed));
            Assert.DoesNotContain(data.Slots[0].Perk, new[] { 2, 3 });
        }
    }

    [Fact]
    public void Config_parses_with_comments()
    {
        var config = PerkConfigLoader.Parse("""
            {
              // comment
              "perks": { "rerollBasePrice": [10, 20], "lockCostStep": 1, "freeRerolls": 1, }
            }
            """);
        Assert.Equal([10, 20], config.RerollBasePrice);
        Assert.Equal(1, config.LockCostStep);
        Assert.Equal(2, config.PaidCostGrowth);
        Assert.Equal(3, config.PaidCostCapRound);
        Assert.Equal(1, config.FreeRerolls);
        Assert.Null(config.Validate());
        Assert.NotNull((config with { RerollBasePrice = [] }).Validate());
        Assert.NotNull((config with { PaidCostCapRound = 0 }).Validate());
    }

    [Fact]
    public void Set_puts_a_perk_in_a_slot_and_swaps_when_another_slot_has_it()
    {
        var data = WithSlots(1, 4, 8);
        data.Slots[0].Locked = true;
        Assert.Equal(PerkError.None, PerkRules.SetPerk(data, 3, 1, 24));
        Assert.Equal([1, 24, 8], data.Slots.Select(slot => slot.Perk));

        Assert.Equal(PerkError.None, PerkRules.SetPerk(data, 3, 2, 1));
        Assert.Equal([8, 24, 1], data.Slots.Select(slot => slot.Perk));
        Assert.True(data.Slots[0].Locked);
    }

    [Fact]
    public void Set_rejects_inactive_slots_and_unknown_perks()
    {
        var data = WithSlots(1, 4, 8);
        Assert.Equal(PerkError.InvalidSlot, PerkRules.SetPerk(data, 2, 2, 24));
        Assert.Equal(PerkError.InvalidSlot, PerkRules.SetPerk(data, 3, -1, 24));
        Assert.Equal(PerkError.UnknownPerk, PerkRules.SetPerk(data, 3, 0, 999));
        Assert.Equal([1, 4, 8], data.Slots.Select(slot => slot.Perk));
    }

    [Fact]
    public void Shake_is_offered_only_when_a_single_slot_is_rolled()
    {
        var data = WithSlots(1, 4, 8);
        Assert.Null(PerkRules.ShakeSlot(data, 3));
        data.Slots[0].Locked = true;
        Assert.Null(PerkRules.ShakeSlot(data, 3));
        data.Slots[2].Locked = true;
        Assert.Equal(1, PerkRules.ShakeSlot(data, 3));
        data.Slots[1].Locked = true;
        Assert.Null(PerkRules.ShakeSlot(data, 3));
        // 단계가 낮으면 앞 슬롯만 돈다
        Assert.Equal(0, PerkRules.ShakeSlot(WithSlots(1, 4, 8), 1));
    }

    [Fact]
    public void Shake_next_takes_the_perk_shown_in_the_next_cell()
    {
        var data = WithSlots(1, 4, 8);
        Assert.Equal(ShakeOutcome.Next, PerkRules.Shake(data, 3, 2, 24, 1.0, 0, new Random(1)));
        Assert.Equal([1, 4, 24], data.Slots.Select(slot => slot.Perk));
    }

    [Fact]
    public void Shake_next_refuses_a_perk_another_slot_has_or_an_unknown_one()
    {
        var data = WithSlots(1, 4, 8);
        Assert.Equal(ShakeOutcome.Stay, PerkRules.Shake(data, 3, 2, 4, 1.0, 0, new Random(1)));
        Assert.Equal(ShakeOutcome.Stay, PerkRules.Shake(data, 3, 2, 999, 1.0, 0, new Random(1)));
        Assert.Equal([1, 4, 8], data.Slots.Select(slot => slot.Perk));
    }

    [Fact]
    public void Shake_reverse_picks_a_free_perk_other_than_the_next_cell()
    {
        for (var seed = 0; seed < 100; seed++)
        {
            var data = WithSlots(1, 4, 8);
            Assert.Equal(ShakeOutcome.Reverse, PerkRules.Shake(data, 3, 0, 24, 0, 1.0, new Random(seed)));
            Assert.DoesNotContain(data.Slots[0].Perk, new[] { 1, 4, 8, 24 });
            Assert.True(PerkCatalog.IsKnown(data.Slots[0].Perk));
        }
    }

    [Fact]
    public void Shake_ignores_locked_and_out_of_range_slots()
    {
        var data = WithSlots(1, 4, 8);
        data.Slots[1].Locked = true;
        Assert.Equal(ShakeOutcome.Stay, PerkRules.Shake(data, 3, 1, 24, 1.0, 0, new Random(1)));
        Assert.Equal(ShakeOutcome.Stay, PerkRules.Shake(data, 3, 3, 24, 1.0, 0, new Random(1)));
        Assert.Equal(ShakeOutcome.Stay, PerkRules.Shake(data, 2, 2, 24, 1.0, 0, new Random(1)));
        Assert.Equal([1, 4, 8], data.Slots.Select(slot => slot.Perk));
    }

    [Fact]
    public void Shake_outcomes_follow_the_configured_chances()
    {
        var random = new Random(11);
        var counts = new Dictionary<ShakeOutcome, int>();
        for (var i = 0; i < 20000; i++)
        {
            var outcome = PerkRules.Shake(WithSlots(1), 1, 0, 24, Config.ShakeChance, Config.ShakeReverseChance, random);
            counts[outcome] = counts.GetValueOrDefault(outcome) + 1;
        }

        Assert.InRange(counts[ShakeOutcome.Next] / 20000.0, Config.ShakeChance - 0.02, Config.ShakeChance + 0.02);
        Assert.InRange(counts[ShakeOutcome.Reverse] / 20000.0, 0.04, 0.06);
        Assert.InRange(counts[ShakeOutcome.Stay] / 20000.0, 1 - Config.ShakeChance - Config.ShakeReverseChance - 0.02, 1 - Config.ShakeChance - Config.ShakeReverseChance + 0.02);
    }

    [Fact]
    public void Shake_config_is_validated()
    {
        Assert.Null(Config.Validate());
        Assert.NotNull((Config with { ShakeChance = 1.5 }).Validate());
        Assert.NotNull((Config with { ShakeChance = 0.6, ShakeReverseChance = 0.5 }).Validate());
        Assert.NotNull((Config with { ShakeWindowSeconds = 0 }).Validate());
    }

    [Fact]
    public void Slot_flags_set_the_slot_count_per_prestige_level()
    {
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 6], Enumerable.Range(0, 8).Select(Config.Slots));
        var allSix = Config with { PrestigeLevel1Slots = 6 };
        Assert.Equal(6, allSix.Slots(1));
        Assert.Equal(2, allSix.Slots(2));
    }

    [Fact]
    public void Slot_flags_open_that_many_slots_on_entering_a_level()
    {
        var config = Config with { PrestigeLevel1Slots = 6 };
        var data = new PerkData();
        PerkRules.EnterLevel(data, 1, config, new Random(3));
        Assert.Equal(6, data.Slots.Count);
        Assert.Equal(6, data.Slots.Select(slot => slot.Perk).Distinct().Count());
        Assert.Equal(6, PerkRules.ActivePerks(data, config.Slots(1)).Count);
    }

    [Fact]
    public void Fewer_slots_keep_the_extra_perks_but_only_the_open_ones_count_and_reroll()
    {
        var config = Config with { PrestigeLevel3Slots = 1 };
        var data = WithSlots(1, 4, 8);
        data.Rerolls = new PerkRerolls { Level = 3, FreeLeft = 1 };
        Assert.Equal([1], PerkRules.ActivePerks(data, config.Slots(3)));
        Assert.Equal(PerkError.InvalidSlot, PerkRules.SetLock(data, config.Slots(3), 1, true));

        Assert.True(PerkRules.Roll(data, 3, config, new Random(5)).Success);
        Assert.DoesNotContain(data.Slots[0].Perk, new[] { 4, 8 });
        Assert.Equal([4, 8], data.Slots.Skip(1).Select(slot => slot.Perk));
    }

    [Fact]
    public void Zero_slots_block_the_roll()
    {
        var data = WithSlots(1, 4);
        data.Rerolls = new PerkRerolls { Level = 2, FreeLeft = 3 };
        Assert.Equal(PerkError.NoSlots, PerkRules.Roll(data, 2, Config with { PrestigeLevel2Slots = 0 }, new Random(1)).Error);
    }

    [Fact]
    public void Free_rerolls_flag_refills_on_a_new_level_and_zero_means_paid_from_the_start()
    {
        var data = new PerkData();
        PerkRules.EnterLevel(data, 1, Config with { FreeRerolls = 10 }, new Random(1));
        Assert.Equal(10, data.Rerolls.FreeLeft);

        var paid = new PerkData();
        var config = Config with { FreeRerolls = 0 };
        PerkRules.EnterLevel(paid, 1, config, new Random(1));
        Assert.Equal(0, paid.Rerolls.FreeLeft);
        Assert.Equal(25, PerkRules.NextRerollCost(config, 1, paid));
    }

    [Fact]
    public void Out_of_range_slot_and_free_reroll_flags_clamp_to_the_nearest_end()
    {
        var warnings = new List<string>();
        var config = PerkConfigLoader.Validated(PerkConfigLoader.ParseModConfig("""
            { "perks": { "freeRerolls": 150, "prestigeLevel1Slots": -3, "prestigeLevel2Slots": 0, "prestigeLevel6Slots": 10 } }
            """), warnings.Add).Perks;

        Assert.Equal(99, config.FreeRerolls);
        Assert.Equal([0, 0, 3, 4, 5, 6], Enumerable.Range(1, 6).Select(config.Slots));
        Assert.Equal(3, warnings.Count);
        Assert.Equal(0, (Config with { FreeRerolls = -1 }).Clamped(_ => { }).FreeRerolls);
        Assert.Null(config.Validate());
    }

    [Fact]
    public void Closed_slots_show_the_next_level_that_opens_them()
    {
        Assert.Equal([null, null, 3, 4, 5, 6], Config.SlotOpensAt(2));
        Assert.Equal([1, 2, 3, 4, 5, 6], Config.SlotOpensAt(0));
        Assert.All(Config.SlotOpensAt(6), Assert.Null);

        // P1 6슬롯, P2 3슬롯: P2 의 뒤 3칸은 더 높은 단계에서 열린다. P3~ 도 3이면 열리지 않는다
        var config = Config with { PrestigeLevel1Slots = 6, PrestigeLevel2Slots = 3, PrestigeLevel3Slots = 3, PrestigeLevel4Slots = 3, PrestigeLevel5Slots = 3, PrestigeLevel6Slots = 5 };
        Assert.Equal([null, null, null, 6, 6, null], config.SlotOpensAt(2));
    }

    [Fact]
    public void Shipped_config_keeps_the_default_slot_and_shake_flags()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "config", PerkConfigLoader.FileName);
        var config = PerkConfigLoader.Validated(PerkConfigLoader.ParseModConfig(File.ReadAllText(path)), warning => Assert.Fail(warning)).Perks;
        Assert.Equal([1, 2, 3, 4, 5, 6], Enumerable.Range(1, 6).Select(config.Slots));
        Assert.Equal(3, config.FreeRerolls);
        Assert.True(config.ShakeEnabled);
    }

    [Fact]
    public void Raising_free_rerolls_in_the_same_level_adds_only_the_difference()
    {
        var data = new PerkData();
        PerkRules.EnterLevel(data, 2, Config, new Random(1));
        data.Rerolls.FreeLeft = 0; // 3회를 다 썼다

        Assert.True(PerkRules.EnterLevel(data, 2, Config with { FreeRerolls = 99 }, new Random(1)).Changed);
        Assert.Equal(96, data.Rerolls.FreeLeft);
        Assert.Equal(99, data.Rerolls.FreeGranted);
        // 같은 설정이면 다시 늘지 않는다
        Assert.False(PerkRules.EnterLevel(data, 2, Config with { FreeRerolls = 99 }, new Random(1)).Changed);
        Assert.Equal(96, data.Rerolls.FreeLeft);
    }

    [Fact]
    public void Lowering_then_raising_free_rerolls_never_gives_more_than_the_setting_minus_used()
    {
        var data = new PerkData();
        PerkRules.EnterLevel(data, 1, Config, new Random(1));
        data.Rerolls.FreeLeft = 2; // 3회 중 1회 썼다

        PerkRules.EnterLevel(data, 1, Config with { FreeRerolls = 1 }, new Random(1));
        Assert.Equal(0, data.Rerolls.FreeLeft);
        PerkRules.EnterLevel(data, 1, Config, new Random(1));
        Assert.Equal(2, data.Rerolls.FreeLeft); // 3회 중 1회 쓴 그대로
    }

    [Fact]
    public void Data_without_free_granted_counts_as_the_default_three()
    {
        var data = WithSlots(1, 4);
        data.Rerolls = new PerkRerolls { Level = 2, FreeLeft = 0 };
        Assert.False(PerkRules.EnterLevel(data, 2, Config, new Random(1)).Changed);
        Assert.Null(data.Rerolls.FreeGranted);

        PerkRules.EnterLevel(data, 2, Config with { FreeRerolls = 5 }, new Random(1));
        Assert.Equal(2, data.Rerolls.FreeLeft);
    }

    [Fact]
    public void A_new_level_refills_to_the_setting_and_records_it()
    {
        var data = WithSlots(1);
        data.Rerolls = new PerkRerolls { Level = 1, FreeLeft = 0, FreeGranted = 3, Paid = 4 };
        PerkRules.EnterLevel(data, 2, Config with { FreeRerolls = 7 }, new Random(1));
        Assert.Equal(7, data.Rerolls.FreeLeft);
        Assert.Equal(7, data.Rerolls.FreeGranted);
        Assert.Equal(0, data.Rerolls.Paid);
    }
}
