using PrestigePerks.Perks;
using SPTarkov.Server.Core.Models.Enums;

namespace PrestigePerks.Tests;

public class PerkAddonFileTests
{
    private readonly List<string> warnings = [];

    private IReadOnlyList<PerkDefinition> Parse(string json) => PerkAddonFile.Parse(json, "viper-extra", warnings.Add);

    [Fact]
    public void Reads_a_bonus_perk_with_texts_and_icon()
    {
        var perks = Parse("""
            { "perks": [ {
              "id": "viper.medic", "kind": "bonus", "unit": "percent",
              "bonuses": [ { "type": "HealthRegeneration", "value": 20 } ],
              "icon": "icons/viper.medic.png",
              "texts": { "en": { "name": "Medic", "effect": "Health regeneration {0}" }, "kr": { "name": "의무병" } }
            } ] }
            """);

        var perk = Assert.Single(perks);
        Assert.Equal(("viper.medic", PerkKind.Bonus, 20.0, PerkUnit.Percent, 0), (perk.Id, perk.Kind, perk.Amount, perk.Unit, perk.Number));
        var bonus = Assert.Single(perk.Bonuses);
        Assert.Equal((BonusType.HealthRegeneration, 20.0, (BonusSkillType?)null), (bonus.Type, bonus.Value, bonus.SkillType));
        Assert.Equal(PerkAddonFile.BonusId("viper.medic", 0), bonus.Id);
        Assert.Equal("viper-extra", perk.Addon!.Source);
        Assert.Equal("icons/viper.medic.png", perk.Addon.IconPath);
        Assert.Equal("Medic", perk.Addon.Texts["en"].Name);
        Assert.Equal("의무병", perk.Addon.Texts["kr"].Name);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Reads_a_patch_perk_and_skill_group_bonuses()
    {
        var perks = Parse("""
            { "perks": [
              { "id": "viper.sprint", "kind": "code", "unit": "percent", "amount": 10 },
              { "id": "viper.study", "kind": "bonus", "unit": "points",
                "bonuses": [ { "type": "SkillGroupLevelingBoost", "value": 15, "skillType": "Practical" },
                             { "type": "SkillGroupLevelingBoost", "value": 15, "skillType": "Mental" } ] }
            ] }
            """);

        Assert.Equal(["viper.sprint", "viper.study"], perks.Select(perk => perk.Id));
        Assert.Equal((PerkKind.Patch, 10.0), (perks[0].Kind, perks[0].Amount));
        Assert.Empty(perks[0].Bonuses);
        Assert.Equal([BonusSkillType.Practical, BonusSkillType.Mental], perks[1].Bonuses.Select(bonus => bonus.SkillType!.Value));
        Assert.Equal([PerkAddonFile.BonusId("viper.study", 0), PerkAddonFile.BonusId("viper.study", 1)], perks[1].Bonuses.Select(bonus => bonus.Id));
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData("viper.medic", true)]
    [InlineData("a.b", true)]
    [InlineData("viper_2.medic-kit.v2", true)]
    [InlineData("medic", false)]          // 점 없음 (내장 형식)
    [InlineData("Viper.Medic", false)]    // 대문자
    [InlineData(".medic", false)]
    [InlineData("viper.", false)]
    [InlineData("viper medic.x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Ids_must_be_lowercase_author_dot_perk(string? id, bool valid)
    {
        Assert.Equal(valid, PerkAddonFile.IsValidId(id));
    }

    [Fact]
    public void Long_ids_are_rejected()
    {
        Assert.False(PerkAddonFile.IsValidId("a." + new string('b', 63)));
        Assert.True(PerkAddonFile.IsValidId("a." + new string('b', 62)));
    }

    // 하나씩 잘못된 특전은 경고하고 건너뛴다. 나머지는 읽는다
    [Theory]
    [InlineData("""{ "id": "medic", "kind": "code", "unit": "percent", "amount": 5 }""")]
    [InlineData("""{ "id": "viper.x", "kind": "magic", "unit": "percent", "amount": 5 }""")]
    [InlineData("""{ "id": "viper.x", "kind": "code", "unit": "kg", "amount": 5 }""")]
    [InlineData("""{ "id": "viper.x", "kind": "code", "unit": "percent" }""")]
    [InlineData("""{ "id": "viper.x", "kind": "code", "unit": "percent", "amount": 0 }""")]
    [InlineData("""{ "id": "viper.x", "kind": "code", "unit": "percent", "amount": 101 }""")]
    [InlineData("""{ "id": "viper.x", "kind": "code", "unit": "percent", "amount": 5, "bonuses": [ { "type": "ExperienceRate", "value": 5 } ] }""")]
    [InlineData("""{ "id": "viper.x", "kind": "bonus", "unit": "percent" }""")]
    [InlineData("""{ "id": "viper.x", "kind": "bonus", "unit": "percent", "bonuses": [] }""")]
    [InlineData("""{ "id": "viper.x", "kind": "bonus", "unit": "percent", "bonuses": [ { "type": "StashRows", "value": 5 } ] }""")]
    [InlineData("""{ "id": "viper.x", "kind": "bonus", "unit": "percent", "bonuses": [ { "type": "NotAType", "value": 5 } ] }""")]
    [InlineData("""{ "id": "viper.x", "kind": "bonus", "unit": "percent", "bonuses": [ { "type": "SkillGroupLevelingBoost", "value": 5 } ] }""")]
    [InlineData("""{ "id": "viper.x", "kind": "bonus", "unit": "percent", "bonuses": [ { "type": "ExperienceRate", "value": 5, "skillType": "Combat" } ] }""")]
    [InlineData("""{ "id": "viper.x", "kind": "bonus", "unit": "percent", "bonuses": [ { "type": "ExperienceRate", "value": 0 } ] }""")]
    [InlineData("""{ "id": "viper.x", "kind": "bonus", "unit": "percent", "bonuses": [ { "type": "ExperienceRate", "value": -101 } ] }""")]
    [InlineData("""{ "id": "viper.x", "kind": "bonus", "unit": "percent", "bonuses": [ { "type": "ExperienceRate", "value": 5 }, { "type": "QuestMoneyReward", "value": 6 } ] }""")]
    [InlineData("""{ "id": "viper.x", "kind": "bonus", "unit": "percent", "bonuses": [ { "type": "ExperienceRate", "value": 1 }, { "type": "ExperienceRate", "value": 1 }, { "type": "ExperienceRate", "value": 1 }, { "type": "ExperienceRate", "value": 1 }, { "type": "ExperienceRate", "value": 1 } ] }""")]
    public void Invalid_perks_are_skipped_with_a_warning(string bad)
    {
        var perks = Parse($$"""{ "perks": [ {{bad}}, { "id": "viper.ok", "kind": "code", "unit": "flat", "amount": 3 } ] }""");

        Assert.Equal(["viper.ok"], perks.Select(perk => perk.Id));
        Assert.Single(warnings);
        Assert.Contains("viper-extra", warnings[0]);
    }

    [Fact]
    public void Duplicate_ids_in_one_file_keep_the_first()
    {
        var perks = Parse("""
            { "perks": [ { "id": "viper.x", "kind": "code", "unit": "flat", "amount": 3 },
                         { "id": "viper.x", "kind": "code", "unit": "flat", "amount": 4 } ] }
            """);

        Assert.Equal(3, Assert.Single(perks).Amount);
        Assert.Single(warnings);
    }

    [Fact]
    public void Bonus_amount_field_is_ignored_with_a_warning()
    {
        var perk = Assert.Single(Parse("""
            { "perks": [ { "id": "viper.x", "kind": "bonus", "unit": "points", "amount": 99, "bonuses": [ { "type": "ExperienceRate", "value": 5 } ] } ] }
            """));

        Assert.Equal(5, perk.Amount);
        Assert.Single(warnings);
    }

    [Fact]
    public void Effect_text_without_a_value_slot_is_dropped()
    {
        var perk = Assert.Single(Parse("""
            { "perks": [ { "id": "viper.x", "kind": "code", "unit": "flat", "amount": 3,
                           "texts": { "en": { "name": "X", "effect": "No value here" } } } ] }
            """));

        Assert.Equal("X", perk.Addon!.Texts["en"].Name);
        Assert.Null(perk.Addon.Texts["en"].Effect);
        Assert.Single(warnings);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "perks": 3 }""")]
    [InlineData("")]
    public void Broken_files_give_nothing_and_one_warning(string json)
    {
        Assert.Empty(Parse(json));
        Assert.Single(warnings);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_allowed()
    {
        Assert.Single(Parse("""
            { // add-on
              "perks": [ { "id": "viper.x", "kind": "code", "unit": "flat", "amount": 3, }, ],
            }
            """));
        Assert.Empty(warnings);
    }

    // 저장 필터·메모리 프로필이 이 값으로 찾는다. 같은 입력이면 매번 같은 값이고 MongoId 로 읽힌다
    [Fact]
    public void Bonus_ids_are_stable_24_hex_and_differ_by_index()
    {
        var first = PerkAddonFile.BonusId("viper.medic", 0);
        Assert.Equal(first, PerkAddonFile.BonusId("viper.medic", 0));
        Assert.Matches("^[0-9a-f]{24}$", first);
        Assert.NotEqual(first, PerkAddonFile.BonusId("viper.medic", 1));
        Assert.NotEqual(first, PerkAddonFile.BonusId("viper.medik", 0));
        Assert.Equal(first, new SPTarkov.Server.Core.Models.Common.MongoId(first).ToString());
        Assert.DoesNotContain(first, PerkCatalog.Builtin.SelectMany(perk => perk.Bonuses).Select(bonus => bonus.Id));
    }

    // 허용 목록은 디컴파일로 계산 위치를 확인한 타입만 (dev-docs 95 "허용 BonusType"). 게임 확인에서 빠진 타입이 있으면 여기도 고친다
    [Fact]
    public void Allowed_bonus_types_are_pinned()
    {
        Assert.Equal(
            new[]
            {
                BonusType.ExperienceRate, BonusType.SkillGroupLevelingBoost, BonusType.ScavCooldownTimer,
                BonusType.HealthRegeneration, BonusType.EnergyRegeneration, BonusType.HydrationRegeneration,
                BonusType.QuestMoneyReward, BonusType.RagfairCommission, BonusType.InsuranceReturnTime,
                BonusType.FuelConsumption, BonusType.RepairWeaponBonus, BonusType.RepairArmorBonus,
            }.Order(),
            PerkAddonFile.AllowedBonusTypes.Order());
        Assert.DoesNotContain(BonusType.StashRows, PerkAddonFile.AllowedBonusTypes);
        Assert.DoesNotContain(BonusType.StashSize, PerkAddonFile.AllowedBonusTypes);
    }
}
