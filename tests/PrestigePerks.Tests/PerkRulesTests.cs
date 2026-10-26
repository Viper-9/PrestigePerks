using PrestigePerks.Perks;
using static PrestigePerks.Tests.TestPerks;

namespace PrestigePerks.Tests;

public class PerkRulesTests
{
    private static readonly PerkConfig Config = new();

    private static PerkData WithSlots(params int[] perks) =>
        new() { Slots = perks.Select(perk => new PerkSlot { Perk = SlotPerk(perk) }).ToList() };

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
        Assert.Equal(Id(1), data.Slots[0].Perk);
        Assert.Equal(Id(24), data.Slots[1].Perk);
        Assert.DoesNotContain(data.Slots[2].Perk, Ids(1, 24));
        Assert.Equal([data.Slots[2].Perk!], newPerks);
        Assert.Equal([data.Slots[2].Perk!], data.UnseenNewPerks);
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
            reappeared = data.Slots[0].Perk == Id(1);
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

        Assert.Equal(PerkError.NothingToRoll, PerkRules.CheckRoll(data, 1));
        Assert.Equal(PerkError.NothingToRoll, PerkRules.Roll(data, 1, Config, new Random()).Error);
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
        Assert.Equal(Ids(1, 8), PerkRules.ActivePerks(data, 2));
        Assert.Empty(PerkRules.ActivePerks(data, 0));
    }

    [Fact]
    public void Dev_all_perks_applies_every_perk_regardless_of_slots()
    {
        var data = WithSlots(1, 8);
        data.DevAllPerks = true;
        Assert.Equal(PerkCatalog.Ids, PerkRules.ActivePerks(data, 1));
        Assert.Equal(PerkCatalog.Ids, PerkRules.ActivePerks(data, 0));
    }

    [Fact]
    public void Roll_does_not_draw_perks_held_in_slots_beyond_the_level()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var data = WithSlots(1, 2, 3);
            data.Rerolls = new PerkRerolls { Level = 3, FreeLeft = 3 };
            PerkRules.Roll(data, 1, Config, new Random(seed));
            Assert.DoesNotContain(data.Slots[0].Perk, Ids(2, 3));
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
        Assert.Equal(PerkError.None, PerkRules.SetPerk(data, 3, 1, Id(24)));
        Assert.Equal(SlotPerks(1, 24, 8), data.Slots.Select(slot => slot.Perk));

        Assert.Equal(PerkError.None, PerkRules.SetPerk(data, 3, 2, Id(1)));
        Assert.Equal(SlotPerks(8, 24, 1), data.Slots.Select(slot => slot.Perk));
        Assert.True(data.Slots[0].Locked);
    }

    [Fact]
    public void Set_rejects_inactive_slots_and_unknown_perks()
    {
        var data = WithSlots(1, 4, 8);
        Assert.Equal(PerkError.InvalidSlot, PerkRules.SetPerk(data, 2, 2, Id(24)));
        Assert.Equal(PerkError.InvalidSlot, PerkRules.SetPerk(data, 3, -1, Id(24)));
        Assert.Equal(PerkError.UnknownPerk, PerkRules.SetPerk(data, 3, 0, Id(999)));
        Assert.Equal(SlotPerks(1, 4, 8), data.Slots.Select(slot => slot.Perk));
    }

    private static PerkData Owning(PerkData data, params (int Perk, int Tier)[] owned)
    {
        foreach (var (perk, tier) in owned)
        {
            data.Owned[Id(perk)] = tier;
        }

        return data;
    }

    [Fact]
    public void Roll_never_draws_owned_perks()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var data = Owning(WithSlots(1, 2, 3), (4, 1), (8, 2), (9, 3));
            data.Rerolls = new PerkRerolls { Level = 3, FreeLeft = 3 };
            PerkRules.Roll(data, 3, Config, new Random(seed));
            Assert.DoesNotContain(data.Slots, slot => slot.Perk == Id(4) || slot.Perk == Id(8) || slot.Perk == Id(9));
        }
    }

    [Fact]
    public void Roll_skips_fixed_slots_and_fixed_slots_do_not_raise_the_cost()
    {
        var data = Owning(WithSlots(1, 2, 3), (1, 1));
        data.Slots[0].Fixed = true;
        data.Rerolls = new PerkRerolls { Level = 3, FreeLeft = 0 };
        Assert.Equal(PerkRules.RerollCost(Config, 3, 0, 1), PerkRules.NextRerollCost(Config, 3, data));

        data.Slots[1].Locked = true;
        Assert.Equal(PerkRules.RerollCost(Config, 3, 1, 1), PerkRules.NextRerollCost(Config, 3, data));

        PerkRules.Roll(data, 3, Config, new Random(5));
        Assert.Equal(Id(1), data.Slots[0].Perk);
        Assert.True(data.Slots[0].Fixed);
        Assert.Equal(Id(2), data.Slots[1].Perk);
    }

    [Fact]
    public void Roll_fills_empty_slots_and_leaves_the_rest_empty_when_unowned_perks_run_out()
    {
        // 20개 중 18개 보유, 미보유 2개뿐인데 리롤 칸은 3개
        var unowned = Ids(24, 32).Order().ToArray();
        var data = new PerkData { Slots = [new PerkSlot(), new PerkSlot { Perk = Id(24) }, new PerkSlot()] };
        foreach (var perk in PerkCatalog.Ids.Except(unowned))
        {
            data.Owned[perk] = 1;
        }

        data.Rerolls = new PerkRerolls { Level = 3, FreeLeft = 3 };
        Assert.Equal(PerkError.None, PerkRules.Roll(data, 3, Config, new Random(2)).Error);
        Assert.Equal(unowned, data.Slots.Where(slot => !slot.IsEmpty).Select(slot => slot.Perk).Order());
        Assert.Single(data.Slots, slot => slot.IsEmpty);
    }

    [Fact]
    public void Roll_is_refused_when_nothing_can_be_rolled_or_the_collection_is_complete()
    {
        var data = Owning(WithSlots(1, 2), (1, 1));
        data.Slots[0].Fixed = true;
        data.Slots[1].Locked = true;
        data.Rerolls = new PerkRerolls { Level = 2, FreeLeft = 3 };
        Assert.Equal(PerkError.NothingToRoll, PerkRules.CheckRoll(data, 2));

        // 빈 칸은 리롤 칸이다
        data.Slots[1] = new PerkSlot();
        Assert.Equal(PerkError.None, PerkRules.CheckRoll(data, 2));

        foreach (var perk in PerkCatalog.Ids)
        {
            data.Owned[perk] = 1;
        }

        Assert.Equal(PerkError.CollectionComplete, PerkRules.CheckRoll(data, 2));
        Assert.Equal(PerkError.CollectionComplete, PerkRules.Roll(data, 2, Config, new Random()).Error);
        Assert.Equal(3, data.Rerolls.FreeLeft);
    }

    [Fact]
    public void New_slots_draw_only_unowned_perks_and_stay_empty_when_none_are_left()
    {
        var data = Owning(WithSlots(1), (2, 1));
        data.Rerolls = new PerkRerolls { Level = 1, FreeLeft = 0 };
        foreach (var perk in PerkCatalog.Ids.Where(perk => perk != Id(1) && perk != Id(3)))
        {
            data.Owned[perk] = 1;
        }

        var (_, newPerks) = PerkRules.EnterLevel(data, 3, Config, new Random(1));

        Assert.Equal(SlotPerks(1, 3, 0), data.Slots.Select(slot => slot.Perk));
        Assert.Equal(Ids(3), newPerks);
        Assert.Equal(Ids(3), data.UnseenNewPerks);
    }

    [Fact]
    public void Tier_cap_follows_prestige_and_is_at_least_one()
    {
        Assert.Equal([1, 1, 1, 2, 2, 3, 3, 3], Enumerable.Range(0, 8).Select(Config.TierCap));
        Assert.Equal(1, (Config with { TierCapByPrestige = [0, 0] }).TierCap(1));
        Assert.Equal(3, (Config with { TierCapByPrestige = [9] }).TierCap(4));
        Assert.Equal(3, Config.TierOpensAt(1, 2));
        Assert.Equal(5, Config.TierOpensAt(1, 3));
        Assert.Null(Config.TierOpensAt(3, 2));
        Assert.Null(Config.TierOpensAt(6, 3));
    }

    [Fact]
    public void Entering_a_level_that_raises_the_tier_cap_records_it_for_the_notice()
    {
        var data = Entered(2, 1);
        Assert.Null(data.UnseenTierCap);
        PerkRules.EnterLevel(data, 3, Config, new Random(1));
        Assert.Equal(2, data.UnseenTierCap);
    }

    [Fact]
    public void Applied_tier_is_owned_tier_up_to_the_cap_and_one_for_unowned_perks()
    {
        var data = Owning(WithSlots(1, 2, 3), (1, 3), (2, 2));
        data.Slots[0].Fixed = data.Slots[1].Fixed = true;

        Assert.Equal(new Dictionary<string, int> { [Id(1)] = 2, [Id(2)] = 2, [Id(3)] = 1 }, PerkRules.ActiveTiers(data, 3, Config));
        Assert.Equal(new Dictionary<string, int> { [Id(1)] = 3, [Id(2)] = 2, [Id(3)] = 1 }, PerkRules.ActiveTiers(data, 5, Config));
        Assert.Equal(new Dictionary<string, double> { [Id(1)] = 1.0, [Id(2)] = 0.75, [Id(3)] = 0.5 }, PerkRules.ActiveScales(data, 5, Config));

        data.DevAllPerks = true;
        Assert.All(PerkRules.ActiveTiers(data, 1, Config).Values, tier => Assert.Equal(3, tier));
    }

    [Fact]
    public void Keep_buys_tier_one_of_a_perk_in_a_slot_and_fixes_that_slot()
    {
        var data = WithSlots(1, 2);
        data.Slots[1].Locked = true;

        Assert.Equal(PerkError.NotInSlot, PerkRules.CheckBuy(data, 2, Config, Id(3), 1));
        Assert.Equal(PerkError.None, PerkRules.Buy(data, 2, Config, Id(2), 1));

        Assert.Equal(1, data.OwnedTier(Id(2)));
        Assert.True(data.Slots[1].Fixed);
        Assert.False(data.Slots[1].Locked);
        Assert.Equal(PerkError.AlreadyOwned, PerkRules.CheckBuy(data, 2, Config, Id(2), 1));
    }

    [Fact]
    public void Higher_tiers_need_the_tier_below_and_the_cap()
    {
        var data = Owning(WithSlots(1), (14, 1));

        Assert.Equal(PerkError.TierCapped, PerkRules.CheckBuy(data, 2, Config, Id(14), 2));
        Assert.Equal(PerkError.TierOrder, PerkRules.CheckBuy(data, 6, Config, Id(14), 3));
        Assert.Equal(PerkError.TierOrder, PerkRules.CheckBuy(data, 6, Config, Id(24), 2));
        Assert.Equal(PerkError.None, PerkRules.Buy(data, 3, Config, Id(14), 2));
        Assert.Equal(PerkError.TierCapped, PerkRules.CheckBuy(data, 4, Config, Id(14), 3));
        Assert.Equal(PerkError.None, PerkRules.Buy(data, 5, Config, Id(14), 3));
        Assert.Equal(3, data.OwnedTier(Id(14)));
        Assert.Equal(PerkError.AlreadyOwned, PerkRules.CheckBuy(data, 6, Config, Id(14), 2));
        Assert.Equal(PerkError.InvalidTier, PerkRules.CheckBuy(data, 6, Config, Id(14), 4));
        Assert.Equal(PerkError.UnknownPerk, PerkRules.CheckBuy(data, 6, Config, Id(999), 1));
    }

    [Fact]
    public void Equip_puts_an_owned_perk_on_a_roll_slot_or_swaps_a_fixed_one()
    {
        var data = Owning(WithSlots(1, 2, 3), (1, 1), (14, 2), (24, 1));
        data.Slots[0].Fixed = true;

        Assert.Equal(PerkError.None, PerkRules.Equip(data, 3, Id(14), 1));
        Assert.Equal(SlotPerks(1, 14, 3), data.Slots.Select(slot => slot.Perk));
        Assert.True(data.Slots[1].Fixed);

        Assert.Equal(PerkError.None, PerkRules.Equip(data, 3, Id(24), 0));
        Assert.Equal(SlotPerks(24, 14, 3), data.Slots.Select(slot => slot.Perk));
        Assert.Equal(1, data.OwnedTier(Id(1)));
    }

    [Fact]
    public void Equip_refuses_locked_slots_unowned_perks_and_perks_already_in_a_slot()
    {
        var data = Owning(WithSlots(1, 2, 3), (1, 1), (14, 1));
        data.Slots[0].Fixed = true;
        data.Slots[2].Locked = true;

        Assert.Equal(PerkError.SlotLocked, PerkRules.Equip(data, 3, Id(14), 2));
        Assert.Equal(PerkError.NotOwned, PerkRules.Equip(data, 3, Id(24), 1));
        Assert.Equal(PerkError.AlreadyEquipped, PerkRules.Equip(data, 3, Id(1), 1));
        Assert.Equal(PerkError.InvalidSlot, PerkRules.Equip(data, 3, Id(14), 3));
        Assert.Equal(SlotPerks(1, 2, 3), data.Slots.Select(slot => slot.Perk));
    }

    [Fact]
    public void Unequip_empties_a_fixed_slot_and_keeps_the_perk_owned()
    {
        var data = Owning(WithSlots(1, 2), (1, 2));
        data.Slots[0].Fixed = true;

        Assert.Equal(PerkError.NotFixed, PerkRules.Unequip(data, 2, 1));
        Assert.Equal(PerkError.None, PerkRules.Unequip(data, 2, 0));
        Assert.True(data.Slots[0].IsEmpty);
        Assert.False(data.Slots[0].Fixed);
        Assert.Equal(2, data.OwnedTier(Id(1)));
        Assert.Equal(Ids(2), PerkRules.ActivePerks(data, 2));
    }

    [Fact]
    public void Fixed_and_empty_slots_cannot_be_locked()
    {
        var data = Owning(new PerkData { Slots = [new PerkSlot { Perk = Id(1), Fixed = true }, new PerkSlot()] }, (1, 1));
        Assert.Equal(PerkError.SlotFixed, PerkRules.SetLock(data, 2, 0, true));
        Assert.Equal(PerkError.SlotEmpty, PerkRules.SetLock(data, 2, 1, true));
        Assert.Equal(PerkError.None, PerkRules.SetLock(data, 2, 1, false));
    }

    [Fact]
    public void Dev_tier_and_collection_reset_keep_slots_consistent()
    {
        var data = Owning(WithSlots(1, 2), (1, 2));
        data.Slots[0].Fixed = true;

        Assert.Equal(PerkError.None, PerkRules.SetOwnedTier(data, Id(2), 3));
        Assert.Equal(3, data.OwnedTier(Id(2)));
        Assert.Equal(PerkError.None, PerkRules.SetOwnedTier(data, Id(1), 0));
        Assert.False(data.Slots[0].Fixed);
        Assert.Equal(PerkError.InvalidTier, PerkRules.SetOwnedTier(data, Id(1), 4));

        PerkRules.ResetCollection(data);
        Assert.Empty(data.Owned);
        Assert.All(data.Slots, slot => Assert.False(slot.Fixed));
    }

    [Fact]
    public void Dev_set_marks_owned_perks_as_fixed()
    {
        var data = Owning(WithSlots(1, 2), (24, 1));
        data.Slots[1].Locked = true;
        PerkRules.SetPerk(data, 2, 1, Id(24));
        Assert.True(data.Slots[1].Fixed);
        Assert.False(data.Slots[1].Locked);
    }

    [Fact]
    public void Tier_config_is_validated()
    {
        Assert.Null(Config.Validate());
        Assert.NotNull((Config with { TierEffectRatio = [0.5, 1] }).Validate());
        Assert.NotNull((Config with { TierEffectRatio = [0.5, 0.75, 1.5] }).Validate());
        Assert.NotNull((Config with { TierPrices = [40, -1, 200] }).Validate());
        Assert.NotNull((Config with { TierCapByPrestige = [] }).Validate());
        Assert.Equal(0.75, Config.TierRatio(2));
        Assert.Equal(0, Config.TierRatio(0));
        Assert.Equal(200, Config.TierPrice(3));
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
        Assert.Equal(Ids(1), PerkRules.ActivePerks(data, config.Slots(3)));
        Assert.Equal(PerkError.InvalidSlot, PerkRules.SetLock(data, config.Slots(3), 1, true));

        Assert.True(PerkRules.Roll(data, 3, config, new Random(5)).Success);
        Assert.DoesNotContain(data.Slots[0].Perk, Ids(4, 8));
        Assert.Equal(SlotPerks(4, 8), data.Slots.Skip(1).Select(slot => slot.Perk));
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
    public void Shipped_config_keeps_the_default_slot_and_tier_values()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "config", PerkConfigLoader.FileName);
        var json = File.ReadAllText(path);
        var config = PerkConfigLoader.Validated(PerkConfigLoader.ParseModConfig(json), warning => Assert.Fail(warning)).Perks;
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], Enumerable.Range(0, 7).Select(config.Slots));
        Assert.Equal(3, config.FreeRerolls);
        Assert.Equal([0.5, 0.75, 1.0], config.TierEffectRatio);
        Assert.Equal([1, 1, 1, 2, 2, 3, 3], config.TierCapByPrestige);
        Assert.Equal([40, 100, 200], config.TierPrices);
        Assert.DoesNotContain("shake", json, StringComparison.OrdinalIgnoreCase);
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
    [Fact]
    public void Prestige_zero_has_no_perks_by_default()
    {
        var data = new PerkData();
        Assert.False(PerkRules.EnterLevel(data, 0, Config, new Random(1)).Changed);
        Assert.Empty(data.Slots);
        Assert.Equal(0, data.Rerolls.FreeLeft);
    }

    [Fact]
    public void Prestige_zero_slots_flag_draws_slots_and_gives_free_rerolls_once()
    {
        var config = Config with { PrestigeLevel0Slots = 2 };
        var data = new PerkData();
        var (changed, newPerks) = PerkRules.EnterLevel(data, 0, config, new Random(1));

        Assert.True(changed);
        Assert.Equal(2, newPerks.Count);
        Assert.Equal(2, PerkRules.ActivePerks(data, config.Slots(0)).Count);
        Assert.Equal(3, data.Rerolls.FreeLeft);
        Assert.Equal(0, data.Rerolls.Level);

        // 다 쓰고 다시 들어와도 P0 에서는 다시 채우지 않는다
        data.Rerolls.FreeLeft = 0;
        Assert.False(PerkRules.EnterLevel(data, 0, config, new Random(1)).Changed);
        Assert.Equal(0, data.Rerolls.FreeLeft);
        Assert.True(PerkRules.Roll(data, 0, config, new Random(2)) is { Success: true, Cost: 25 }); // P0 유료 비용은 P1 기본가
    }

    [Fact]
    public void Prestige_zero_perks_carry_into_prestige_one()
    {
        var config = Config with { PrestigeLevel0Slots = 2, PrestigeLevel1Slots = 3 };
        var data = new PerkData();
        PerkRules.EnterLevel(data, 0, config, new Random(1));
        var before = data.Slots.Select(slot => slot.Perk).ToList();
        data.Rerolls.FreeLeft = 0;

        var (_, newPerks) = PerkRules.EnterLevel(data, 1, config, new Random(1));
        Assert.Equal(before, data.Slots.Take(2).Select(slot => slot.Perk));
        Assert.Single(newPerks);
        Assert.Equal(3, data.Rerolls.FreeLeft);
        Assert.Equal(1, data.Rerolls.Level);
    }

    [Fact]
    public void Prestige_zero_closed_slots_point_to_the_first_level_that_opens_them() =>
        Assert.Equal([null, null, 3, 4, 5, 6], (Config with { PrestigeLevel0Slots = 2 }).SlotOpensAt(0));
}
