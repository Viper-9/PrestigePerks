using PrestigePerks.Perks;

namespace PrestigePerks.Tests;

public class PerkRulesTests
{
    private static readonly PerkConfig Config = new();

    // dev-docs 2_특전.md 2절 예시표
    [Theory]
    [InlineData(1, 0, 25, 50, 100)]
    [InlineData(3, 2, 90, 180, 360)]
    [InlineData(6, 0, 75, 150, 300)]
    public void Cost_matches_the_document_table(int level, int locked, int first, int second, int third)
    {
        Assert.Equal(first, PerkRules.RerollCost(Config, level, locked, 1));
        Assert.Equal(second, PerkRules.RerollCost(Config, level, locked, 2));
        Assert.Equal(third, PerkRules.RerollCost(Config, level, locked, 3));
    }

    [Fact]
    public void Cost_rounds_up()
    {
        // P6 잠금 5: 75 × 3.5 = 262.5 → 263, 다음 525
        Assert.Equal(263, PerkRules.RerollCost(Config, 6, 5, 1));
        Assert.Equal(525, PerkRules.RerollCost(Config, 6, 5, 2));
        Assert.Equal(788, PerkRules.RerollCost(Config, 6, 5, 1) + PerkRules.RerollCost(Config, 6, 5, 2));
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
    public void Open_keeps_previous_perks_and_draws_the_new_slot_without_using_free_rerolls()
    {
        var data = new PerkData { Perks = [1, 24] };
        Assert.Equal(PerkError.None, PerkRules.OpenSession(data, 3, PerkSessionReason.Prestige, Config, new Random(1)));

        var slots = data.Session!.Slots;
        Assert.Equal(3, slots.Count);
        Assert.Equal(1, slots[0].Perk);
        Assert.Equal(24, slots[1].Perk);
        Assert.NotNull(slots[2].Perk);
        Assert.DoesNotContain(slots[2].Perk!.Value, new[] { 1, 24 });
        Assert.Equal(3, data.Session.FreeRerollsLeft);
        Assert.Equal(0, data.Session.PaidRerolls);
    }

    [Fact]
    public void Retroactive_user_gets_all_slots_drawn()
    {
        var data = new PerkData();
        Assert.True(PerkRules.NeedsSession(data, 4));
        PerkRules.OpenSession(data, 4, PerkSessionReason.Prestige, Config, new Random(2));

        Assert.Equal(4, data.Session!.Slots.Count);
        Assert.All(data.Session.Slots, slot => Assert.NotNull(slot.Perk));
        Assert.Equal(4, data.Session.Slots.Select(slot => slot.Perk).Distinct().Count());
        Assert.Equal(3, data.Session.FreeRerollsLeft);
    }

    [Fact]
    public void No_session_needed_when_slots_are_full_or_level_zero()
    {
        Assert.False(PerkRules.NeedsSession(new PerkData { Perks = [1, 2] }, 2));
        Assert.False(PerkRules.NeedsSession(new PerkData(), 0));
        Assert.Equal(PerkError.NoSlots, PerkRules.OpenSession(new PerkData(), 0, PerkSessionReason.Dev, Config, new Random()));
    }

    [Fact]
    public void Forced_open_with_full_slots_skips_auto_draw()
    {
        var data = new PerkData { Perks = [1, 2] };
        PerkRules.OpenSession(data, 2, PerkSessionReason.Dev, Config, new Random(3));

        Assert.Equal([1, 2], data.Session!.Slots.Select(slot => slot.Perk!.Value));
        Assert.Equal(3, data.Session.FreeRerollsLeft);
        Assert.Equal(PerkError.SessionAlreadyOpen, PerkRules.OpenSession(data, 2, PerkSessionReason.Dev, Config, new Random()));
    }

    [Fact]
    public void Roll_never_duplicates_and_never_draws_locked_perks()
    {
        for (var seed = 0; seed < 500; seed++)
        {
            var random = new Random(seed);
            var data = new PerkData();
            PerkRules.OpenSession(data, 6, PerkSessionReason.Prestige, Config, random);
            var lockedPerks = new[] { data.Session!.Slots[0].Perk!.Value, data.Session.Slots[3].Perk!.Value };
            PerkRules.SetLock(data, 0, true);
            PerkRules.SetLock(data, 3, true);

            PerkRules.Roll(data, 6, Config, random);

            var perks = data.Session.Slots.Select(slot => slot.Perk!.Value).ToList();
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
            var data = new PerkData { Perks = [1] };
            PerkRules.OpenSession(data, 1, PerkSessionReason.Dev, Config, new Random(seed));
            PerkRules.Roll(data, 1, Config, new Random(seed));
            reappeared = data.Session!.Slots[0].Perk == 1;
        }

        Assert.True(reappeared);
    }

    [Fact]
    public void Three_free_rerolls_then_paid_rounds_double()
    {
        var data = new PerkData { CoinBalance = 1000 };
        var random = new Random(4);
        PerkRules.OpenSession(data, 3, PerkSessionReason.Prestige, Config, random);
        PerkRules.SetLock(data, 0, true);
        PerkRules.SetLock(data, 1, true);

        // 무료 3회는 잠금 개수와 상관없이 무료
        for (var i = 0; i < 3; i++)
        {
            var free = PerkRules.Roll(data, 3, Config, random);
            Assert.True(free.Success);
            Assert.Equal(0, free.Cost);
        }

        Assert.Equal(0, data.Session!.FreeRerollsLeft);
        Assert.Equal(1000, data.CoinBalance);

        // P3 잠금 2: 90 → 180 → 360
        Assert.Equal(90, PerkRules.NextRerollCost(Config, 3, data.Session));
        Assert.Equal(90, PerkRules.Roll(data, 3, Config, random).Cost);
        Assert.Equal(180, PerkRules.Roll(data, 3, Config, random).Cost);
        Assert.Equal(360, PerkRules.Roll(data, 3, Config, random).Cost);
        Assert.Equal(3, data.Session.PaidRerolls);
        Assert.Equal(1000 - 630, data.CoinBalance);
    }

    [Fact]
    public void Paid_cost_uses_lock_count_at_the_moment_of_rolling()
    {
        var data = new PerkData { CoinBalance = 1000 };
        var random = new Random(5);
        PerkRules.OpenSession(data, 3, PerkSessionReason.Prestige, Config, random);
        data.Session!.FreeRerollsLeft = 0;

        Assert.Equal(45, PerkRules.Roll(data, 3, Config, random).Cost);   // 잠금 0, 1회차
        PerkRules.SetLock(data, 0, true);
        Assert.Equal(135, PerkRules.Roll(data, 3, Config, random).Cost);  // 45 × 1.5 × 2 = 135
    }

    [Fact]
    public void Paid_roll_is_refused_without_enough_coins()
    {
        var data = new PerkData { CoinBalance = 24 };
        var random = new Random(6);
        PerkRules.OpenSession(data, 1, PerkSessionReason.Prestige, Config, random);
        data.Session!.FreeRerollsLeft = 0;
        var before = data.Session.Slots[0].Perk;

        var outcome = PerkRules.Roll(data, 1, Config, random);

        Assert.Equal(PerkError.NotEnoughCoins, outcome.Error);
        Assert.Equal(25, outcome.Cost);
        Assert.Equal(24, data.CoinBalance);
        Assert.Equal(0, data.Session.PaidRerolls);
        Assert.Equal(before, data.Session.Slots[0].Perk);
    }

    [Fact]
    public void Roll_is_refused_when_everything_is_locked_or_screen_closed()
    {
        var data = new PerkData();
        Assert.Equal(PerkError.NoSession, PerkRules.Roll(data, 1, Config, new Random()).Error);

        PerkRules.OpenSession(data, 1, PerkSessionReason.Prestige, Config, new Random(7));
        PerkRules.SetLock(data, 0, true);
        Assert.Equal(PerkError.AllLocked, PerkRules.Roll(data, 1, Config, new Random()).Error);
        Assert.Equal(3, data.Session!.FreeRerollsLeft);
    }

    [Fact]
    public void Lock_rejects_out_of_range_slots()
    {
        var data = new PerkData();
        Assert.Equal(PerkError.NoSession, PerkRules.SetLock(data, 0, true));
        PerkRules.OpenSession(data, 2, PerkSessionReason.Prestige, Config, new Random(8));
        Assert.Equal(PerkError.InvalidSlot, PerkRules.SetLock(data, 2, true));
        Assert.Equal(PerkError.InvalidSlot, PerkRules.SetLock(data, -1, true));
    }

    [Fact]
    public void Confirm_saves_slots_closes_screen_and_returns_coins_to_mail()
    {
        var data = new PerkData { CoinBalance = 70 };
        PerkRules.OpenSession(data, 2, PerkSessionReason.Prestige, Config, new Random(9));
        var slots = data.Session!.Slots.Select(slot => slot.Perk!.Value).ToList();

        Assert.Equal(PerkError.None, PerkRules.Confirm(data, out var coins));
        Assert.Equal(slots, data.Perks);
        Assert.Null(data.Session);
        Assert.Equal(70, coins);
        Assert.Equal(0, data.CoinBalance);
        Assert.Equal(PerkError.NoSession, PerkRules.Confirm(data, out _));
    }

    [Fact]
    public void Paid_round_restarts_when_the_screen_opens_again()
    {
        var data = new PerkData { CoinBalance = 1000 };
        var random = new Random(10);
        PerkRules.OpenSession(data, 1, PerkSessionReason.Prestige, Config, random);
        data.Session!.FreeRerollsLeft = 0;
        PerkRules.Roll(data, 1, Config, random);
        PerkRules.Roll(data, 1, Config, random);
        PerkRules.Confirm(data, out _);

        data.CoinBalance = 1000;
        PerkRules.OpenSession(data, 1, PerkSessionReason.Dev, Config, random);
        Assert.Equal(0, data.Session!.PaidRerolls);
        Assert.Equal(3, data.Session.FreeRerollsLeft);
    }

    [Fact]
    public void Active_perks_are_capped_by_prestige_level()
    {
        var data = new PerkData { Perks = [1, 8, 24] };
        Assert.Equal([1, 8], PerkRules.ActivePerks(data, 2));
        Assert.Empty(PerkRules.ActivePerks(data, 0));
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
        Assert.Equal(1, config.FreeRerolls);
        Assert.Null(config.Validate());
        Assert.NotNull((config with { RerollBasePrice = [] }).Validate());
    }
}
