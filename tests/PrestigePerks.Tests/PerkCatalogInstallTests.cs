using PrestigePerks.Perks;
using PrestigePerks.Saving;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;

namespace PrestigePerks.Tests;

/// <summary>PerkCatalog 을 바꾸는 테스트. 다른 테스트가 끝난 뒤 따로 돈다 (내장 20개를 가정하는 테스트와 섞이지 않게).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PerkCatalogInstallCollection
{
    public const string Name = "PerkCatalog install";
}

[Collection(PerkCatalogInstallCollection.Name)]
public sealed class PerkCatalogInstallTests : IDisposable
{
    private static PerkDefinition Medic() => new(0, "viper.medic", PerkKind.Bonus, 20, PerkUnit.Percent,
        [new PerkBonus(PerkAddonFile.BonusId("viper.medic", 0), BonusType.HealthRegeneration, 20)],
        new PerkAddonInfo("viper-extra", new Dictionary<string, PerkTexts> { ["en"] = new("Medic", "Health regeneration {0}", null) }, "icons/viper.medic.png"));

    private static PerkDefinition Sprint() => new(0, "viper.sprint", PerkKind.Patch, 10, PerkUnit.Percent, [],
        new PerkAddonInfo("viper-extra", new Dictionary<string, PerkTexts>(), null));

    public void Dispose() => PerkCatalog.Install([]);

    [Fact]
    public void Installed_perks_follow_the_builtin_ones_everywhere()
    {
        Assert.Empty(PerkCatalog.Install([Medic(), Sprint()]));

        Assert.Equal(PerkCatalog.Builtin.Select(perk => perk.Id).Concat(["viper.medic", "viper.sprint"]), PerkCatalog.Ids);
        Assert.True(PerkCatalog.IsKnown("viper.sprint"));
        Assert.Equal("viper.medic", PerkCatalog.Resolve("viper.medic"));
        Assert.Equal("viper.medic", PerkCatalog.Describe("viper.medic"));
        Assert.Contains(new MongoId(PerkAddonFile.BonusId("viper.medic", 0)), PerkCatalog.FilteredBonusIds);
        Assert.Equal(20, PerkCatalog.Numbers.Count);
        Assert.Null(PerkCatalog.IdOf(0));
    }

    [Fact]
    public void State_entries_carry_kind_texts_and_icon_only_for_addons()
    {
        PerkCatalog.Install([Medic(), Sprint()]);
        PerkCatalog.InstallIcons(new Dictionary<string, byte[]> { ["viper.medic"] = [0x89, 0x50, 0x4E, 0x47] });

        var medic = PerkCatalog.Entries.Single(entry => entry.Id == "viper.medic");
        Assert.Equal(("bonus", true, "Medic"), (medic.Kind, medic.Icon, medic.Texts!["en"].Name));
        var sprint = PerkCatalog.Entries.Single(entry => entry.Id == "viper.sprint");
        Assert.Equal(("code", false), (sprint.Kind, sprint.Icon));
        Assert.Null(sprint.Texts);

        // 내장 특전 JSON 은 지금과 같다 (새 필드 없음)
        var json = System.Text.Json.JsonSerializer.Serialize(PerkCatalog.Entries[0]);
        Assert.Equal("""{"id":"experience","amount":30,"unit":"points"}""", json);
    }

    [Fact]
    public void Addons_join_the_roll_pool_and_the_collection()
    {
        PerkCatalog.Install([Medic()]);
        var data = new PerkData { Owned = PerkCatalog.Builtin.ToDictionary(perk => perk.Id, _ => 1) };

        // 내장을 다 가져도 add-on 이 남아 있으면 도감 완성이 아니다
        Assert.Equal(["viper.medic"], PerkRules.RollPool(data, slotCount: 1));
    }

    [Fact]
    public void Addon_bonuses_are_applied_and_filtered_like_builtin_ones()
    {
        PerkCatalog.Install([Medic()]);
        var bonuses = PerkBonusApplier.CreateBonuses(new Dictionary<string, double> { ["viper.medic"] = 0.5 });

        var bonus = Assert.Single(bonuses);
        Assert.Equal((BonusType.HealthRegeneration, 10.0), (bonus.Type, bonus.Value));
        Assert.True(PerkBonusApplier.IsPerkBonus(bonus));
    }

    [Fact]
    public void Addons_clashing_with_builtin_ids_or_bonus_ids_are_skipped()
    {
        var clash = Medic() with { Id = "mastery" };
        var stolenBonus = Medic() with { Id = "viper.thief", Bonuses = [new PerkBonus("8bcbeb65b78be907af6ee2b0", BonusType.ExperienceRate, 5)] };

        var warnings = PerkCatalog.Install([clash, stolenBonus, Sprint()]);

        Assert.Equal(2, warnings.Count);
        Assert.Equal(["viper.sprint"], PerkCatalog.Addons.Select(perk => perk.Id));
    }

    [Fact]
    public void Installing_nothing_restores_the_builtin_catalog()
    {
        PerkCatalog.Install([Medic()]);
        PerkCatalog.Install([]);

        Assert.Equal(PerkCatalog.Builtin.Select(perk => perk.Id), PerkCatalog.Ids);
        Assert.False(PerkCatalog.IsKnown("viper.medic"));
        Assert.Empty(PerkCatalog.Icons);
    }
}
