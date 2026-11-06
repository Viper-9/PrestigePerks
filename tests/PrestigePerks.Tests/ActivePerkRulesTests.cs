using PrestigePerks.Perks;
using static PrestigePerks.Tests.TestPerks;

namespace PrestigePerks.Tests;

/// <summary>액티브 특전 칸 규칙 (dev-docs 94 1절, 93 Task 3). 칸 하나, 기본 P1 에서 열림 (config opensAt), 슬롯 번호 6, 가격 80 / 200 / 350.</summary>
public class ActivePerkRulesTests
{
    private static readonly PerkConfig Config = new();
    private const int A = ActivePerkRules.SlotIndex;

    private static PerkData Entered(int level, int seed, params int[] perks)
    {
        var data = new PerkData { Slots = perks.Select(perk => new PerkSlot { Perk = SlotPerk(perk) }).ToList() };
        PerkRules.EnterLevel(data, level, Config, new Random(seed));
        return data;
    }

    [Fact]
    public void Active_slot_opens_at_prestige_one_with_a_drawn_active_perk()
    {
        Assert.Null(Entered(0, 1).ActivePerkSlot);
        var data = Entered(1, 1);
        Assert.Contains(data.ActivePerkSlot!.Perk, PerkCatalog.ActiveIds);
        Assert.Contains(data.ActivePerkSlot.Perk!, data.UnseenNewPerks);
        Assert.All(data.Slots, slot => Assert.False(PerkCatalog.IsActive(slot.Perk)));
    }

    [Fact]
    public void Opens_at_zero_opens_the_active_slot_before_any_prestige()
    {
        var data = new PerkData();
        var (changed, newPerks) = PerkRules.EnterLevel(data, 0, Config, new Random(1), activeOpensAt: 0);
        Assert.True(changed);
        Assert.Contains(data.ActivePerkSlot!.Perk, PerkCatalog.ActiveIds);
        Assert.Equal([data.ActivePerkSlot.Perk!], newPerks);
        Assert.Empty(data.Slots);
        Assert.Equal(Config.FreeRerolls, data.Rerolls.FreeLeft);
        Assert.Equal(PerkError.None, PerkRules.CheckRoll(data, 0));
    }

    [Fact]
    public void Opens_at_waits_for_the_configured_prestige_level()
    {
        var data = new PerkData();
        PerkRules.EnterLevel(data, 2, Config, new Random(1), activeOpensAt: 3);
        Assert.Null(data.ActivePerkSlot);
        PerkRules.EnterLevel(data, 3, Config, new Random(1), activeOpensAt: 3);
        Assert.NotNull(data.ActivePerkSlot);
    }

    [Fact]
    public void An_open_active_slot_stays_when_opens_at_goes_up()
    {
        var data = Entered(1, 1);
        var perk = data.ActivePerkSlot!.Perk;
        PerkRules.EnterLevel(data, 1, Config, new Random(2), activeOpensAt: 3);
        Assert.Equal(perk, data.ActivePerkSlot!.Perk);
    }

    [Fact]
    public void Opens_at_comes_from_config_and_is_clamped()
    {
        Assert.Equal(1, PerkConfigLoader.ParseModConfig("{}").ActivePerks.OpensAt);
        Assert.Equal(0, PerkConfigLoader.ParseModConfig("""{ "activePerks": { "opensAt": 0 } }""").ActivePerks.OpensAt);
        var warnings = new List<string>();
        Assert.Equal(6, PerkConfigLoader.Validated(PerkConfigLoader.ParseModConfig("""{ "activePerks": { "opensAt": 9 } }"""), warnings.Add).ActivePerks.OpensAt);
        Assert.Equal(0, PerkConfigLoader.Validated(PerkConfigLoader.ParseModConfig("""{ "activePerks": { "opensAt": -1 } }"""), warnings.Add).ActivePerks.OpensAt);
        Assert.Equal(2, warnings.Count);
    }

    [Fact]
    public void Entering_again_keeps_the_active_slot()
    {
        var data = Entered(3, 1);
        var perk = data.ActivePerkSlot!.Perk;
        PerkRules.EnterLevel(data, 4, Config, new Random(9));
        Assert.Equal(perk, data.ActivePerkSlot!.Perk);
    }

    [Fact]
    public void Drawing_skips_owned_active_perks()
    {
        var data = new PerkData { Owned = PerkCatalog.ActiveIds.Where(id => id != "grit").ToDictionary(id => id, _ => 1) };
        PerkRules.EnterLevel(data, 3, Config, new Random(1));
        Assert.Equal("grit", data.ActivePerkSlot!.Perk);
    }

    [Fact]
    public void Reroll_rolls_the_active_slot_from_the_active_pool_only()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var data = Entered(3, seed);
            Assert.True(PerkRules.Roll(data, 3, Config, new Random(seed + 100)).Success);
            Assert.True(PerkCatalog.IsActive(data.ActivePerkSlot!.Perk));
            Assert.All(data.Slots, slot => Assert.False(PerkCatalog.IsActive(slot.Perk)));
        }
    }

    [Fact]
    public void Locked_active_slot_is_kept_and_raises_the_cost()
    {
        var data = Entered(3, 1);
        data.Rerolls.FreeLeft = 0;
        var before = PerkRules.NextRerollCost(Config, 3, data);
        Assert.Equal(PerkError.None, PerkRules.SetLock(data, 3, A, true));
        Assert.Equal(PerkRules.RerollCost(Config, 3, 1, 1), PerkRules.NextRerollCost(Config, 3, data));
        Assert.True(PerkRules.NextRerollCost(Config, 3, data) > before);

        var perk = data.ActivePerkSlot!.Perk;
        PerkRules.Roll(data, 3, Config, new Random(5));
        Assert.Equal(perk, data.ActivePerkSlot!.Perk);
    }

    [Fact]
    public void Roll_is_allowed_when_only_the_active_slot_can_roll()
    {
        var data = Entered(3, 1);
        foreach (var slot in data.Slots)
        {
            slot.Locked = true;
        }

        Assert.Equal(PerkError.None, PerkRules.CheckRoll(data, 3));
        data.ActivePerkSlot!.Locked = true;
        Assert.Equal(PerkError.NothingToRoll, PerkRules.CheckRoll(data, 3));
    }

    [Fact]
    public void Collection_is_complete_only_when_both_pools_are_empty()
    {
        var data = Entered(3, 1);
        data.Owned = PerkCatalog.Ids.ToDictionary(id => id, _ => 1);
        foreach (var slot in data.Slots.Where(slot => !slot.IsEmpty))
        {
            slot.Fixed = true;
        }

        data.Slots.Add(new PerkSlot());
        Assert.Equal(PerkError.None, PerkRules.CheckRoll(data, 4));
        foreach (var id in PerkCatalog.ActiveIds)
        {
            data.Owned[id] = 1;
        }

        Assert.Equal(PerkError.CollectionComplete, PerkRules.CheckRoll(data, 4));
    }

    [Fact]
    public void Active_prices_are_80_200_350_and_passive_prices_come_from_config()
    {
        int[] tiers = [1, 2, 3];
        Assert.Equal([80, 200, 350], tiers.Select(tier => PerkRules.TierPrice(Config, "grit", tier)));
        Assert.Equal([40, 100, 200], tiers.Select(tier => PerkRules.TierPrice(Config, "porter", tier)));
    }

    [Fact]
    public void Keeping_an_active_perk_needs_it_in_the_active_slot_and_fixes_the_slot()
    {
        var data = Entered(3, 1);
        var inSlot = data.ActivePerkSlot!.Perk!;
        var other = PerkCatalog.ActiveIds.First(id => id != inSlot);
        Assert.Equal(PerkError.NotInSlot, PerkRules.CheckBuy(data, 3, Config, other, 1));
        data.ActivePerkSlot.Locked = true;
        Assert.Equal(PerkError.None, PerkRules.Buy(data, 3, Config, inSlot, 1));
        Assert.True(data.ActivePerkSlot.Fixed);
        Assert.False(data.ActivePerkSlot.Locked);
        Assert.Equal(PerkError.None, PerkRules.CheckBuy(data, 3, Config, inSlot, 2));
        // P3 상한은 II. III 은 II 를 사기 전이라 TierOrder
        Assert.Equal(PerkError.TierOrder, PerkRules.CheckBuy(data, 3, Config, inSlot, 3));
        data.Owned[inSlot] = 2;
        Assert.Equal(PerkError.TierCapped, PerkRules.CheckBuy(data, 3, Config, inSlot, 3));
    }

    [Fact]
    public void Slots_reject_the_wrong_kind()
    {
        var data = Entered(3, 1);
        data.Owned["grit"] = 1;
        data.Owned["porter"] = 1;
        Assert.Equal(PerkError.WrongKind, PerkRules.Equip(data, 3, "grit", 0));
        Assert.Equal(PerkError.WrongKind, PerkRules.Equip(data, 3, "porter", A));
        Assert.Equal(PerkError.WrongKind, PerkRules.SetPerk(data, 3, 0, "grit"));
        Assert.Equal(PerkError.WrongKind, PerkRules.SetPerk(data, 3, A, "porter"));
    }

    [Fact]
    public void Equip_and_unequip_use_the_active_slot_index()
    {
        var data = Entered(3, 1);
        var owned = PerkCatalog.ActiveIds.First(id => id != data.ActivePerkSlot!.Perk);
        data.Owned[owned] = 2;
        Assert.Equal(PerkError.None, PerkRules.Equip(data, 3, owned, A));
        Assert.Equal(owned, data.ActivePerkSlot!.Perk);
        Assert.True(data.ActivePerkSlot.Fixed);
        Assert.Equal(PerkError.AlreadyEquipped, PerkRules.Equip(data, 3, owned, A));
        Assert.Equal(PerkError.None, PerkRules.Unequip(data, 3, A));
        Assert.Null(data.ActivePerkSlot.Perk);
        Assert.Equal(PerkError.NotFixed, PerkRules.Unequip(data, 3, A));
    }

    [Fact]
    public void Dev_set_puts_an_active_perk_into_the_active_slot()
    {
        var data = Entered(3, 1);
        Assert.Equal(PerkError.None, PerkRules.SetPerk(data, 3, A, "grit"));
        Assert.Equal("grit", data.ActivePerkSlot!.Perk);
        Assert.False(data.ActivePerkSlot.Fixed);
        data.Owned["focus"] = 1;
        Assert.Equal(PerkError.None, PerkRules.SetPerk(data, 3, A, "focus"));
        Assert.True(data.ActivePerkSlot.Fixed);
    }

    [Fact]
    public void Active_slot_index_is_invalid_before_the_slot_opens()
    {
        var data = Entered(0, 1);
        Assert.Equal(PerkError.InvalidSlot, PerkRules.SetLock(data, 0, A, true));
        Assert.Equal(PerkError.InvalidSlot, PerkRules.Unequip(data, 0, A));
        Assert.Equal(PerkError.InvalidSlot, PerkRules.SetPerk(data, 0, A, "grit"));
    }

    [Fact]
    public void Applied_tier_is_the_owned_tier_up_to_the_cap_or_one()
    {
        var data = Entered(5, 1);
        var perk = data.ActivePerkSlot!.Perk!;
        Assert.Equal(1, ActivePerkRules.AppliedTier(data, 5, Config));
        data.Owned[perk] = 3;
        Assert.Equal(3, ActivePerkRules.AppliedTier(data, 5, Config));
        Assert.Equal(2, ActivePerkRules.AppliedTier(data, 3, Config));
        data.ActivePerkSlot.Perk = null;
        Assert.Equal(0, ActivePerkRules.AppliedTier(data, 5, Config));
        Assert.Equal(0, ActivePerkRules.AppliedTier(new PerkData(), 5, Config));
    }

    [Fact]
    public void Dev_all_perks_and_bonus_scales_stay_passive()
    {
        var data = Entered(3, 1);
        data.DevAllPerks = true;
        Assert.Empty(PerkRules.ActivePerks(data, 3).Intersect(PerkCatalog.ActiveIds));
        Assert.Empty(PerkRules.ActiveTiers(data, 3, Config).Keys.Intersect(PerkCatalog.ActiveIds));
    }

    [Fact]
    public void First_aid_buffs_follow_the_catalog_per_tier()
    {
        var buffs = ActivePerkBuffs.Build();
        Assert.Equal(["PrestigePerks_FirstAid_1", "PrestigePerks_FirstAid_2", "PrestigePerks_FirstAid_3"], buffs.Keys.Order());
        Assert.Equal("PrestigePerks_FirstAid_2", ActivePerkBuffs.FirstAidBuffName(2));
        var tier2 = buffs["PrestigePerks_FirstAid_2"];
        Assert.Equal(new ActiveBuffLine("HealthRate", 4, true, 45), tier2[0]);
        Assert.Equal(new ActiveBuffLine("RemoveAllBloodLosses", 0, true, 45), tier2[1]);
        Assert.Equal(new ActiveBuffLine("HealthRate", 3, true, 30), buffs["PrestigePerks_FirstAid_1"][0]);
        Assert.Equal(new ActiveBuffLine("RemoveAllBloodLosses", 0, true, 60), buffs["PrestigePerks_FirstAid_3"][1]);
    }

    [Fact]
    public void Clearing_an_owned_active_perk_unfixes_the_active_slot()
    {
        var data = Entered(3, 1);
        var perk = data.ActivePerkSlot!.Perk!;
        PerkRules.Buy(data, 3, Config, perk, 1);
        Assert.Equal(PerkError.None, PerkRules.SetOwnedTier(data, perk, 0));
        Assert.False(data.ActivePerkSlot.Fixed);
        PerkRules.Buy(data, 3, Config, perk, 1);
        PerkRules.ResetCollection(data);
        Assert.False(data.ActivePerkSlot.Fixed);
    }
}
