using PrestigePerks.Perks;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;

namespace PrestigePerks.Tests;

public class PerkCatalogTests
{
    [Fact]
    public void Has_the_20_fixed_perk_numbers()
    {
        Assert.Equal([1, 2, 3, 4, 8, 9, 10, 11, 14, 15, 16, 17, 24, 25, 26, 27, 29, 30, 31, 32], PerkCatalog.Numbers);
    }

    [Fact]
    public void Retired_numbers_are_never_used()
    {
        Assert.Empty(PerkCatalog.Numbers.Intersect(PerkCatalog.RetiredNumbers));
    }

    // 프로필 메모리와 저장 필터가 이 값으로 찾는다. 바뀌면 이전 Bonus 가 남으므로 값 자체를 고정한다
    [Theory]
    [InlineData(1, "8bcbeb65b78be907af6ee2b0", BonusType.ExperienceRate, 30.0, null)]
    [InlineData(2, "5dad7e290a1ca8cc07e58bb7", BonusType.SkillGroupLevelingBoost, 30.0, BonusSkillType.Physical)]
    [InlineData(3, "f1360d70ed3bb18289339797", BonusType.SkillGroupLevelingBoost, 30.0, BonusSkillType.Combat)]
    [InlineData(4, "26b6ac04721b710794837e2c", BonusType.SkillGroupLevelingBoost, 30.0, BonusSkillType.Practical)]
    [InlineData(4, "24656f22d00ab74d124b8174", BonusType.SkillGroupLevelingBoost, 30.0, BonusSkillType.Mental)]
    [InlineData(8, "bfa913d120f3845fffd516e0", BonusType.ScavCooldownTimer, -30.0, null)]
    public void Bonus_ids_and_values_are_pinned(int number, string id, BonusType type, double value, BonusSkillType? skill)
    {
        Assert.True(PerkCatalog.TryGet(number, out var perk));
        Assert.Equal(PerkKind.Bonus, perk.Kind);
        Assert.Contains(new PerkBonus(id, type, value, skill), perk.Bonuses);
    }

    [Fact]
    public void Only_the_five_bonus_perks_have_bonuses()
    {
        var bonusPerks = PerkCatalog.All.Where(perk => perk.Kind == PerkKind.Bonus).Select(perk => perk.Number);
        Assert.Equal([1, 2, 3, 4, 8], bonusPerks);
        Assert.Equal(6, PerkCatalog.All.Sum(perk => perk.Bonuses.Count));
        Assert.All(PerkCatalog.All.Where(perk => perk.Kind == PerkKind.Patch), perk => Assert.Empty(perk.Bonuses));
    }

    [Fact]
    public void Bonus_ids_are_valid_and_unique()
    {
        var ids = PerkCatalog.All.SelectMany(perk => perk.Bonuses).Select(bonus => bonus.Id).Concat(PerkCatalog.RetiredBonusIds).ToList();
        Assert.All(ids, id => Assert.True(MongoId.IsValidMongoId(id), id));
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(ids.Count, PerkCatalog.FilteredBonusIds.Count);
    }

    // 특전 데이터 파일(schema 4)에 영구히 저장되는 ID. 바뀌면 산 특전이 "모르는 특전"이 되므로 값 자체를 고정한다
    [Fact]
    public void Has_the_20_fixed_perk_ids()
    {
        Assert.Equal(
            ["experience", "conditioning", "combat", "learning", "scav", "porter", "stamina", "tough", "control", "maintenance",
             "temperance", "artisan", "mastery", "loading", "search", "surgeon", "reload", "handling", "reliability", "agility"],
            PerkCatalog.Ids);
        Assert.Equal(PerkCatalog.All.Count, PerkCatalog.Ids.Distinct().Count());
    }

    [Fact]
    public void Numbers_and_ids_map_both_ways()
    {
        Assert.All(PerkCatalog.All, perk =>
        {
            Assert.Equal(perk.Id, PerkCatalog.IdOf(perk.Number));
            Assert.Equal(perk.Number, PerkCatalog.NumberOf(perk.Id));
            Assert.True(PerkCatalog.IsKnown(perk.Id));
        });
        Assert.Equal("mastery", PerkCatalog.IdOf(24));
        Assert.Null(PerkCatalog.IdOf(5));
        Assert.Null(PerkCatalog.IdOf(0));
        Assert.Null(PerkCatalog.NumberOf("someone.sprint"));
        Assert.Null(PerkCatalog.NumberOf(null));
        Assert.False(PerkCatalog.IsKnown("someone.sprint"));
        Assert.False(PerkCatalog.IsKnown((string?)null));
        Assert.False(PerkCatalog.IsKnown("24"));
    }

    [Fact]
    public void Describe_shows_id_and_number()
    {
        Assert.Equal("mastery(24)", PerkCatalog.Describe("mastery"));
        Assert.Equal("someone.sprint", PerkCatalog.Describe("someone.sprint"));
        Assert.Equal("empty", PerkCatalog.Describe((string?)null));
        Assert.Equal("mastery(24)", PerkCatalog.Describe(24));
    }

    // 화면 수치의 출처. 클라이언트 문구가 이 값을 그대로 보이므로 값 자체를 고정한다 (dev-docs 2_특전.md 3절 표)
    [Fact]
    public void Has_the_fixed_tier_three_amounts_and_units()
    {
        Assert.Equal(
            [30, 30, 30, 30, -30, 20, 15, -20, -10, -20, -15, -20, 30, -20, 30, 25, 15, 10, -20, 5],
            PerkCatalog.All.Select(perk => perk.Amount));
        Assert.Equal([1, 2, 3, 4, 8, 9, 10, 14, 27], PerkCatalog.All.Where(perk => perk.Unit == PerkUnit.Points).Select(perk => perk.Number));
        Assert.Equal([30], PerkCatalog.All.Where(perk => perk.Unit == PerkUnit.Flat).Select(perk => perk.Number));
    }

    [Fact]
    public void Bonus_perk_amount_is_its_bonus_value()
    {
        Assert.All(PerkCatalog.All.Where(perk => perk.Kind == PerkKind.Bonus),
            perk => Assert.All(perk.Bonuses, bonus => Assert.Equal(bonus.Value, perk.Amount)));
    }

    [Fact]
    public void State_entries_follow_the_catalog_order()
    {
        Assert.Equal(PerkCatalog.Ids, PerkCatalog.Entries.Select(entry => entry.Id));
        Assert.Equal(PerkCatalog.All.Select(perk => perk.Amount), PerkCatalog.Entries.Select(entry => entry.Amount));
        Assert.Equal("points", PerkCatalog.Entries[0].Unit);
        Assert.Equal("percent", PerkCatalog.Entries.Single(entry => entry.Id == "mastery").Unit);
        Assert.Equal("flat", PerkCatalog.Entries.Single(entry => entry.Id == "handling").Unit);
    }

    [Fact]
    public void Known_only_drops_empty_and_unknown_perks()
    {
        Assert.Equal(["experience", "mastery"], PerkCatalog.KnownOnly(["experience", null, "someone.sprint", "mastery"]));
        Assert.Equal(new Dictionary<string, int> { ["control"] = 2 },
            PerkCatalog.KnownOnly(new Dictionary<string, int> { ["control"] = 2, ["someone.sprint"] = 3 }));
    }

    [Fact]
    public void Slot_state_hides_unknown_perks_as_empty()
    {
        var state = PerkSlotState.From(new PerkSlot { Perk = "reload", Locked = true });
        Assert.Equal(("reload", true, false), (state.Perk, state.Locked, state.Fixed));
        Assert.True(PerkSlotState.From(new PerkSlot()).IsEmpty);
        Assert.True(PerkSlotState.From(new PerkSlot { Perk = "someone.sprint" }).IsEmpty);
    }

    [Theory]
    [InlineData("24", "mastery")]
    [InlineData("mastery", "mastery")]
    [InlineData("MASTERY", "mastery")]
    [InlineData("5", null)]
    [InlineData("someone.sprint", null)]
    [InlineData(null, null)]
    public void Resolve_takes_a_number_or_an_id(string? token, string? id)
    {
        Assert.Equal(id, PerkCatalog.Resolve(token));
    }

    [Fact]
    public void Has_the_10_fixed_active_perks()
    {
        Assert.Equal(
            ["firstaid", "secondwind", "sonarpulse", "steadfast", "focus", "emergencysurgery", "composure", "grit", "resupply", "fieldrepair"],
            PerkCatalog.ActiveIds);
        Assert.Equal([41, 42, 43, 44, 45, 46, 47, 48, 49, 50], PerkCatalog.BuiltinActive.Select(perk => perk.Number));
        Assert.All(PerkCatalog.BuiltinActive, perk =>
        {
            Assert.Equal(PerkKind.Active, perk.Kind);
            Assert.Empty(perk.Bonuses);
            Assert.NotNull(perk.Active);
            Assert.Equal(3, perk.Active!.Cooldown.Count);
            Assert.Equal(3, perk.Active.Duration.Count);
            Assert.All(perk.Active.Values.Values, tiers => Assert.Equal(3, tiers.Count));
        });
        Assert.Empty(PerkCatalog.ActiveIds.Intersect(PerkCatalog.Ids));
        Assert.Empty(PerkCatalog.BuiltinActive.Select(perk => perk.Number).Intersect(PerkCatalog.Numbers.Concat(PerkCatalog.RetiredNumbers)));
    }

    [Fact]
    public void Active_values_are_pinned()
    {
        ActivePerkSpec Spec(string id) => PerkCatalog.BuiltinActive.Single(perk => perk.Id == id).Active!;

        Assert.Equal(ActiveLimit.OncePerRaid, Spec("firstaid").Limit);
        Assert.Equal([30, 45, 60], Spec("firstaid").Duration);
        Assert.Equal([3, 3.5, 4], Spec("firstaid").Values["regen"]);
        Assert.Equal([30, 45, 60], Spec("firstaid").Values["bloodstop"]);
        Assert.Equal(ActiveLimit.Cooldown, Spec("secondwind").Limit);
        Assert.Equal([300, 240, 180], Spec("secondwind").Cooldown);
        Assert.Equal([0, 0, 0], Spec("secondwind").Duration);
        Assert.Equal([20, 30, 40], Spec("sonarpulse").Duration);
        Assert.Equal([50, 65, 80], Spec("sonarpulse").Values["radius"]);
        Assert.Equal([600, 600, 600], Spec("steadfast").Cooldown);
        Assert.Equal([60, 80, 100], Spec("steadfast").Duration);
        Assert.Equal([80, 140, 200], Spec("focus").Duration);
        Assert.Equal([30, 50, 70], Spec("emergencysurgery").Values["restore"]);
        Assert.Equal([300, 300, 300], Spec("composure").Cooldown);
        Assert.Equal([30, 45, 60], Spec("composure").Duration);
        Assert.Equal([10, 12, 15], Spec("composure").Values["reduction"]);
        Assert.Equal([5, 8, 10], Spec("composure").Values["inertia"]);
        Assert.Equal([20, 30, 40], Spec("grit").Duration);
        Assert.Equal([10, 12, 15], Spec("grit").Values["reduction"]);
        Assert.Equal([5, 6, 7.5], Spec("grit").Values["head"]);
        Assert.Equal([30, 55, 70], Spec("resupply").Values["fill"]);
        Assert.Equal([15, 25, 35], Spec("fieldrepair").Values["repair"]);
    }

    [Fact]
    public void Active_perks_are_found_by_id_and_number_but_stay_out_of_the_passive_lists()
    {
        Assert.True(PerkCatalog.IsKnown("grit"));
        Assert.True(PerkCatalog.IsActive("grit"));
        Assert.False(PerkCatalog.IsActive("porter"));
        Assert.False(PerkCatalog.IsActive(null));
        Assert.True(PerkCatalog.IsKnown(48));
        Assert.Equal("grit", PerkCatalog.IdOf(48));
        Assert.Equal(48, PerkCatalog.NumberOf("grit"));
        Assert.Equal("grit", PerkCatalog.Resolve("48"));
        Assert.Equal("grit", PerkCatalog.Resolve("GRIT"));
        Assert.Equal("grit(48)", PerkCatalog.Describe("grit"));
        Assert.DoesNotContain("grit", PerkCatalog.Ids);
        Assert.DoesNotContain(PerkCatalog.All, perk => perk.Kind == PerkKind.Active);
        Assert.DoesNotContain(PerkCatalog.Entries, entry => entry.Id == "grit");
        Assert.Equal(20, PerkCatalog.Numbers.Count);
    }

    [Fact]
    public void Active_entries_carry_kind_limit_and_values_as_json()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(PerkCatalog.ActiveEntries.Single(entry => entry.Id == "secondwind"));
        Assert.Contains("\"kind\":\"active\"", json);
        Assert.Contains("\"limit\":\"cooldown\"", json);
        Assert.Contains("\"cooldown\":[300,240,180]", json);
        Assert.Contains("\"limit\":\"raid\"", System.Text.Json.JsonSerializer.Serialize(PerkCatalog.ActiveEntries[0]));
        Assert.Contains("\"regen\":[3,3.5,4]", System.Text.Json.JsonSerializer.Serialize(PerkCatalog.ActiveEntries[0]));
        Assert.DoesNotContain("\"active\"", System.Text.Json.JsonSerializer.Serialize(PerkCatalog.Entries[0]));
        Assert.Equal(PerkCatalog.ActiveIds, PerkCatalog.ActiveEntries.Select(entry => entry.Id));
    }
}
