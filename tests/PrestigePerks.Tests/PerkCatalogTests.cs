using PrestigePerks.Perks;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;

namespace PrestigePerks.Tests;

public class PerkCatalogTests
{
    [Fact]
    public void Has_the_19_fixed_perk_numbers()
    {
        Assert.Equal([1, 2, 3, 4, 8, 9, 10, 11, 12, 14, 15, 16, 17, 24, 25, 26, 27, 28, 29], PerkCatalog.Numbers);
    }

    [Fact]
    public void Retired_numbers_are_never_used()
    {
        Assert.Empty(PerkCatalog.Numbers.Intersect(PerkCatalog.RetiredNumbers));
    }

    // 프로필 메모리와 저장 필터가 이 값으로 찾는다. 바뀌면 이전 Bonus 가 남으므로 값 자체를 고정한다
    [Theory]
    [InlineData(1, "8bcbeb65b78be907af6ee2b0", BonusType.ExperienceRate, 20.0, null)]
    [InlineData(2, "5dad7e290a1ca8cc07e58bb7", BonusType.SkillGroupLevelingBoost, 20.0, BonusSkillType.Physical)]
    [InlineData(3, "f1360d70ed3bb18289339797", BonusType.SkillGroupLevelingBoost, 20.0, BonusSkillType.Combat)]
    [InlineData(4, "26b6ac04721b710794837e2c", BonusType.SkillGroupLevelingBoost, 20.0, BonusSkillType.Practical)]
    [InlineData(4, "24656f22d00ab74d124b8174", BonusType.SkillGroupLevelingBoost, 20.0, BonusSkillType.Mental)]
    [InlineData(8, "bfa913d120f3845fffd516e0", BonusType.ScavCooldownTimer, -25.0, null)]
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

    [Fact]
    public void Keys_are_unique()
    {
        Assert.Equal(PerkCatalog.All.Count, PerkCatalog.All.Select(perk => perk.Key).Distinct().Count());
    }
}
