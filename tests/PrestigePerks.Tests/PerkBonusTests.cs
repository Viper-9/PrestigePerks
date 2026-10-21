using PrestigePerks.Perks;
using PrestigePerks.Saving;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;
using PmcInfo = SPTarkov.Server.Core.Models.Eft.Common.Tables.Info;
using ProfileInfo = SPTarkov.Server.Core.Models.Eft.Profile.Info;

namespace PrestigePerks.Tests;

public class PerkBonusTests
{
    // 바닐라 도서관 Bonus (hideout/areas.json)
    private static Bonus HideoutBonus() => new()
    {
        Id = new MongoId("64f5b9e5fa34f11b380756f8"),
        Type = BonusType.SkillGroupLevelingBoost,
        SkillType = BonusSkillType.Practical,
        Value = 30,
        IsPassive = true,
        IsProduction = false,
        IsVisible = true,
    };

    private static PmcData Pmc(params Bonus[] bonuses) => new()
    {
        Info = new PmcInfo { PrestigeLevel = 3 },
        Bonuses = bonuses.ToList(),
    };

    private static List<string> Ids(PmcData pmc) => pmc.Bonuses!.Select(bonus => bonus.Id.ToString()).ToList();

    [Fact]
    public void Apply_adds_the_bonuses_of_bonus_perks_only()
    {
        var pmc = Pmc(HideoutBonus());
        PerkBonusApplier.Apply(pmc, [1, 4, 8, 24]);

        Assert.Equal(["64f5b9e5fa34f11b380756f8", "8bcbeb65b78be907af6ee2b0", "26b6ac04721b710794837e2c", "24656f22d00ab74d124b8174", "bfa913d120f3845fffd516e0"], Ids(pmc));
        var scav = pmc.Bonuses!.Single(bonus => bonus.Type == BonusType.ScavCooldownTimer);
        Assert.Equal(-30, scav.Value);
        Assert.True(scav.IsPassive);
        Assert.False(scav.IsProduction);
    }

    [Fact]
    public void Apply_is_idempotent()
    {
        var pmc = Pmc(HideoutBonus());
        PerkBonusApplier.Apply(pmc, [1, 2, 3]);
        var once = Ids(pmc);
        PerkBonusApplier.Apply(pmc, [1, 2, 3]);
        PerkBonusApplier.Apply(pmc, [1, 2, 3]);
        Assert.Equal(once, Ids(pmc));
    }

    [Fact]
    public void Apply_replaces_old_perk_bonuses_and_keeps_vanilla_ones()
    {
        var pmc = Pmc(HideoutBonus());
        PerkBonusApplier.Apply(pmc, [1, 8]);
        PerkBonusApplier.Apply(pmc, [3]);
        Assert.Equal(["64f5b9e5fa34f11b380756f8", "f1360d70ed3bb18289339797"], Ids(pmc));

        PerkBonusApplier.Apply(pmc, []);
        Assert.Equal(["64f5b9e5fa34f11b380756f8"], Ids(pmc));
    }

    [Fact]
    public void Apply_leaves_null_bonuses_alone_when_there_is_nothing_to_add()
    {
        var pmc = new PmcData();
        PerkBonusApplier.Apply(pmc, [24]);
        Assert.Null(pmc.Bonuses);

        PerkBonusApplier.Apply(pmc, [1]);
        Assert.Equal(["8bcbeb65b78be907af6ee2b0"], Ids(pmc));
    }

    private static SptProfile Profile(PmcData pmc) => new()
    {
        ProfileInfo = new ProfileInfo { ProfileId = new MongoId("6a8328d2c1ae327dd00378cf"), Username = "test" },
        CharacterData = new Characters { PmcData = pmc, ScavData = new PmcData { Bonuses = [] } },
    };

    private static JsonUtil CreateJsonUtil() =>
        new([new SptJsonConverterRegistrator(), new PerkJsonConverterRegistrator()]);

    [Fact]
    public void Saved_profile_json_has_no_perk_bonus_ids()
    {
        var pmc = Pmc(HideoutBonus());
        PerkBonusApplier.Apply(pmc, [1, 2, 3, 4, 8]);
        var profile = Profile(pmc);

        var json = CreateJsonUtil().Serialize(profile, true)!;

        foreach (var id in PerkCatalog.FilteredBonusIds)
        {
            Assert.DoesNotContain(id.ToString(), json);
        }

        Assert.Contains("64f5b9e5fa34f11b380756f8", json);
        // 메모리 프로필은 그대로
        Assert.Equal(7, pmc.Bonuses!.Count);
        Assert.Same(pmc, profile.CharacterData!.PmcData);
    }

    [Fact]
    public void Saved_profile_reloads_as_vanilla_profile()
    {
        var pmc = Pmc(HideoutBonus());
        PerkBonusApplier.Apply(pmc, [1, 8]);
        var jsonUtil = CreateJsonUtil();
        var json = jsonUtil.Serialize(Profile(pmc))!;

        var loaded = jsonUtil.Deserialize<SptProfile>(json)!;

        Assert.Equal(["64f5b9e5fa34f11b380756f8"], Ids(loaded.CharacterData!.PmcData!));
        Assert.Equal(3, loaded.CharacterData.PmcData!.Info!.PrestigeLevel);
        Assert.Equal("test", loaded.ProfileInfo!.Username);
    }

    [Fact]
    public void Converter_only_filters_perk_ids()
    {
        var pmc = Pmc(HideoutBonus());
        pmc.Bonuses!.Add(new Bonus { Id = new MongoId("bfa913d120f3845fffd516e1"), Type = BonusType.ScavCooldownTimer, Value = -25 });
        PerkBonusApplier.Apply(pmc, [8]);

        var stripped = PerkProfileConverter.StripPerkBonuses(Profile(pmc));

        Assert.Equal(["64f5b9e5fa34f11b380756f8", "bfa913d120f3845fffd516e1"], Ids(stripped.CharacterData!.PmcData!));
    }

    [Fact]
    public void Profile_without_perk_bonuses_is_not_copied()
    {
        var profile = Profile(Pmc(HideoutBonus()));
        Assert.Same(profile, PerkProfileConverter.StripPerkBonuses(profile));
    }
}
