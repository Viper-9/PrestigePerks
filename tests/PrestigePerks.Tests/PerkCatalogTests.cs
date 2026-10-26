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
}
