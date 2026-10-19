using PrestigePerks.Perks;
using PrestigePerks.Prestiges;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Enums.Hideout;
using SPTarkov.Server.Core.Utils.Json;

namespace PrestigePerks.Tests;

/// <summary>프레스티지 조건 적용 (server/Prestiges, dev-docs 1_프레스티지_조건.md 1절·5절).</summary>
public class PrestigeConditionTests
{
    private static readonly string[] Ids = PrestigeConditionRules.ElementIds;

    [Fact]
    public void Shipped_config_is_valid_and_matches_defaults()
    {
        var shipped = PerkConfigLoader.ParseModConfig(File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "config", PerkConfigLoader.FileName))).Prestige;
        Assert.Null(shipped.Validate());

        var defaults = new PrestigeConfig().Stages;
        Assert.Equal(defaults.Count, shipped.Stages.Count);
        for (var i = 0; i < defaults.Count; i++)
        {
            var (expected, actual) = (defaults[i], shipped.Stages[i]);
            Assert.Equal(expected.Level, actual.Level);
            Assert.Equal(expected.Roubles, actual.Roubles);
            Assert.Equal(expected.TransferMultiplier, actual.TransferMultiplier);
            Assert.Equal(expected.NewBeginningLevel, actual.NewBeginningLevel);
            Assert.Equal(expected.Skills ?? [], actual.Skills ?? []);
            Assert.Equal(expected.Hideout ?? [], actual.Hideout ?? []);
            Assert.Empty(actual.ExtraQuests ?? []);
        }
    }

    [Fact]
    public void Defaults_set_backport_data_to_the_planned_values()
    {
        var prestige = BackportPrestige();
        var quests = NewBeginningQuests();

        var result = PrestigeConditionRules.Apply(prestige, quests, new PrestigeConfig());

        Assert.Empty(result.Skipped);
        Assert.Equal(3, result.Removed); // P1 은신처 3개

        var p1 = prestige.Elements[0];
        Assert.Equal(15, Value(p1, "Level"));
        Assert.Equal(5_000_000, Value(p1, "HasItem"));
        Assert.DoesNotContain(p1.Conditions, c => c.ConditionType == "HideoutArea");
        Assert.Contains(p1.Conditions, c => c.ConditionType == "Quest"); // 퀘스트 조건은 건드리지 않음
        Assert.Equal(0.9, p1.TransferConfigs.SkillConfig.TransferMultiplier);
        Assert.Equal(0.9, p1.TransferConfigs.MasteringConfig.TransferMultiplier);

        var p2 = prestige.Elements[1];
        Assert.Equal(25, Value(p2, "Level"));
        Assert.Equal(10_000_000, Value(p2, "HasItem"));
        Assert.Equal(3, p2.Conditions.Count(c => c.ConditionType == "Skill"));
        Assert.Equal(1, Hideout(p2, HideoutAreas.IntelligenceCenter));
        Assert.Equal(2, Hideout(p2, HideoutAreas.Kitchen));

        var p3 = prestige.Elements[2];
        Assert.Equal(12_000_000, Value(p3, "HasItem"));
        Assert.Equal(0.7, p3.TransferConfigs.SkillConfig.TransferMultiplier);

        var p4 = prestige.Elements[3];
        Assert.Equal(15, Skill(p4, "Strength"));
        Assert.Equal(12, Skill(p4, "Charisma"));

        var p6 = prestige.Elements[5];
        Assert.Equal(3, Hideout(p6, HideoutAreas.Security));
        Assert.Equal(0.5, p6.TransferConfigs.MasteringConfig.TransferMultiplier);

        Assert.Equal(15, StartLevel(quests, 0));
        Assert.Equal(25, StartLevel(quests, 1));
        Assert.Equal(35, StartLevel(quests, 2)); // NB-P3 ~ P6 는 그대로
        Assert.Equal(47, StartLevel(quests, 5));
    }

    [Fact]
    public void Describe_reads_back_the_applied_values()
    {
        var prestige = BackportPrestige();
        var quests = NewBeginningQuests();
        PrestigeConditionRules.Apply(prestige, quests, new PrestigeConfig());

        var p1 = PrestigeConditionRules.Describe(prestige.Elements[0]);
        Assert.Contains("level 15,", p1);
        Assert.Contains("roubles 5,000,000", p1);
        Assert.Contains("skills none, hideout none", p1);
        Assert.Contains($"quests {PrestigeConditionRules.NewBeginningLevelConditions[0].Quest}", p1);
        Assert.Contains("keep skill 10% mastering 10%, window 8x3", p1);
        Assert.Contains("skills Strength 10 Endurance 10 Charisma 7, hideout 11:1 9:2 8:2", PrestigeConditionRules.Describe(prestige.Elements[1]));
        Assert.Equal("P1 15, P2 25, P3 35, P4 40, P5 47, P6 47", PrestigeConditionRules.DescribeNewBeginningLevels(quests));
    }

    [Fact]
    public void Condition_ids_are_kept()
    {
        var prestige = BackportPrestige();
        var before = prestige.Elements[1].Conditions.Select(c => c.Id).ToList();

        PrestigeConditionRules.Apply(prestige, NewBeginningQuests(), new PrestigeConfig());

        Assert.Equal(before, prestige.Elements[1].Conditions.Select(c => c.Id));
    }

    [Fact]
    public void Applying_twice_changes_nothing_the_second_time()
    {
        var prestige = BackportPrestige();
        var quests = NewBeginningQuests();
        PrestigeConditionRules.Apply(prestige, quests, new PrestigeConfig());

        var again = PrestigeConditionRules.Apply(prestige, quests, new PrestigeConfig());

        Assert.Equal(0, again.Changed);
        Assert.Equal(0, again.Removed);
        Assert.Empty(again.Skipped);
    }

    [Fact]
    public void Missing_fields_leave_the_data_untouched()
    {
        var prestige = BackportPrestige();
        var config = Config(new PrestigeStageConfig { Level = 20 });

        var result = PrestigeConditionRules.Apply(prestige, NewBeginningQuests(), config);

        var p1 = prestige.Elements[0];
        Assert.Equal(20, Value(p1, "Level"));
        Assert.Equal(10_000_000, Value(p1, "HasItem"));
        Assert.Equal(3, p1.Conditions.Count(c => c.ConditionType == "HideoutArea"));
        Assert.Equal(0.95, p1.TransferConfigs.SkillConfig.TransferMultiplier);
        Assert.Equal(1, result.Changed);
    }

    [Fact]
    public void Missing_elements_and_conditions_are_skipped_and_the_rest_applied()
    {
        var prestige = BackportPrestige();
        prestige.Elements.RemoveAt(5); // P6 없음
        prestige.Elements[0].Conditions.RemoveAll(c => c.ConditionType == "HasItem");
        var quests = NewBeginningQuests();
        quests.Remove(PrestigeConditionRules.NewBeginningLevelConditions[1].Quest);

        var result = PrestigeConditionRules.Apply(prestige, quests, new PrestigeConfig());

        Assert.Contains(result.Skipped, s => s.Contains("P1 루블"));
        Assert.Contains(result.Skipped, s => s.Contains(Ids[5]));
        Assert.Contains(result.Skipped, s => s.Contains("P2 New Beginning"));
        Assert.Equal(3, result.Skipped.Count);
        Assert.Equal(15, Value(prestige.Elements[0], "Level"));
        Assert.Equal(15, StartLevel(quests, 0));
    }

    [Fact]
    public void Configured_targets_missing_from_the_data_are_skipped_not_added()
    {
        var prestige = BackportPrestige();
        var config = Config(new PrestigeStageConfig { Skills = new() { ["Vitality"] = 5 }, Hideout = new() { [(int)HideoutAreas.Gym] = 1 } }, index: 1);

        var result = PrestigeConditionRules.Apply(prestige, NewBeginningQuests(), config);

        var p2 = prestige.Elements[1];
        Assert.DoesNotContain(p2.Conditions, c => c.ConditionType is "Skill" or "HideoutArea");
        Assert.Equal(6, result.Removed);
        Assert.Equal(2, result.Skipped.Count);
    }

    [Fact]
    public void Extra_quests_are_added_only_when_the_quest_exists()
    {
        const string traderQuest = "aaaaaaaaaaaaaaaaaaaaaaaa";
        const string traderCondition = "bbbbbbbbbbbbbbbbbbbbbbbb";
        const string missingQuest = "cccccccccccccccccccccccc";
        var prestige = BackportPrestige();
        var quests = NewBeginningQuests();
        quests[traderQuest] = Quest(traderQuest, []);
        var config = Config(new PrestigeStageConfig
        {
            ExtraQuests = [new() { Quest = traderQuest, Condition = traderCondition }, new() { Quest = missingQuest, Condition = "dddddddddddddddddddddddd" }],
        });

        var result = PrestigeConditionRules.Apply(prestige, quests, config);
        PrestigeConditionRules.Apply(prestige, quests, config);

        var added = Assert.Single(prestige.Elements[0].Conditions, c => c.Id == traderCondition);
        Assert.Equal("Quest", added.ConditionType);
        Assert.Equal(traderQuest, added.Target!.Item);
        Assert.Equal([QuestStatusEnum.Success], added.Status!);
        Assert.Equal(1, result.Added);
        Assert.Contains(result.Skipped, s => s.Contains(missingQuest));
    }

    [Fact]
    public void Invalid_config_is_rejected_and_falls_back_per_section()
    {
        Assert.NotNull((new PrestigeConfig() with { Stages = [new()] }).Validate());
        Assert.NotNull(Config(new PrestigeStageConfig { TransferMultiplier = 1.5 }).Validate());
        Assert.NotNull(Config(new PrestigeStageConfig { Hideout = new() { [11] = -1 } }).Validate());
        Assert.NotNull(Config(new PrestigeStageConfig { ExtraQuests = [new() { Quest = "short", Condition = "bbbbbbbbbbbbbbbbbbbbbbbb" }] }).Validate());

        var warnings = new List<string>();
        var config = PerkConfigLoader.Validated(
            PerkConfigLoader.ParseModConfig("""{ "perks": { "freeRerolls": 5 }, "prestige": { "stages": [ { "level": 15 } ] } }"""),
            warnings.Add);

        Assert.Equal(5, config.Perks.FreeRerolls); // perks 는 그대로
        Assert.Equal(6, config.Prestige.Stages.Count); // prestige 만 기본값
        Assert.Single(warnings);
    }

    [Fact]
    public void Missing_prestige_section_uses_defaults()
    {
        var config = PerkConfigLoader.Validated(PerkConfigLoader.ParseModConfig("""{ "perks": {} }"""), _ => Assert.Fail("no warning expected"));
        Assert.Equal(15, config.Prestige.Stages[0].Level);
    }

    /// <summary>1단계(또는 지정 단계)만 값이 있고 나머지 단계는 빈 설정.</summary>
    private static PrestigeConfig Config(PrestigeStageConfig stage, int index = 0) =>
        new() { Stages = Enumerable.Range(0, Ids.Length).Select(i => i == index ? stage : new PrestigeStageConfig()).ToList() };

    private static double? Value(PrestigeElement element, string type) => element.Conditions.Single(c => c.ConditionType == type).Value;

    private static double? Skill(PrestigeElement element, string skill) => element.Conditions.Single(c => c.ConditionType == "Skill" && c.Target!.Item == skill).Value;

    private static double? Hideout(PrestigeElement element, HideoutAreas area) => element.Conditions.Single(c => c.ConditionType == "HideoutArea" && c.AreaType == area).Value;

    private static double? StartLevel(Dictionary<MongoId, Quest> quests, int stage)
    {
        var (quest, condition) = PrestigeConditionRules.NewBeginningLevelConditions[stage];
        return quests[quest].Conditions.AvailableForStart!.Single(c => c.Id == condition).Value;
    }

    /// <summary>백포트 모드 1.1.0 prestiges.json 과 같은 모양 (값은 백포트 값).</summary>
    private static Prestige BackportPrestige()
    {
        Dictionary<string, int> low = new() { ["Strength"] = 10, ["Endurance"] = 10, ["Charisma"] = 7 };
        Dictionary<HideoutAreas, int> lowHideout = new() { [HideoutAreas.IntelligenceCenter] = 1, [HideoutAreas.RestSpace] = 2, [HideoutAreas.Kitchen] = 2 };
        Dictionary<HideoutAreas, int> highHideout = new() { [HideoutAreas.IntelligenceCenter] = 2, [HideoutAreas.Security] = 3, [HideoutAreas.RestSpace] = 3 };
        Dictionary<string, int> max = new() { ["Strength"] = 20, ["Endurance"] = 20, ["Charisma"] = 20 };
        return new Prestige
        {
            Elements =
            [
                Element(0, 25, [], lowHideout, 10_000_000, 0.95),
                Element(1, 30, low, lowHideout, 15_000_000, 0.9),
                Element(2, 35, low, lowHideout, 15_000_000, 0.85),
                Element(3, 40, new() { ["Strength"] = 15, ["Endurance"] = 15, ["Charisma"] = 12 }, lowHideout, 15_000_000, 0.8),
                Element(4, 47, max, highHideout, 20_000_000, 0.75),
                Element(5, 47, max, highHideout, 20_000_000, 0.7),
            ],
        };
    }

    private static PrestigeElement Element(int stage, int level, Dictionary<string, int> skills, Dictionary<HideoutAreas, int> hideout, long roubles, double multiplier)
    {
        var conditions = new List<QuestCondition>
        {
            Condition("Level", level),
            Condition("Quest", null, new ListOrT<string>(null, PrestigeConditionRules.NewBeginningLevelConditions[stage].Quest)),
        };
        conditions.AddRange(skills.Select(skill => Condition("Skill", skill.Value, new ListOrT<string>(null, skill.Key))));
        conditions.AddRange(hideout.Select(area => Condition("HideoutArea", area.Value, area: area.Key)));
        conditions.Add(Condition("HasItem", roubles, new ListOrT<string>([PrestigeConditionRules.RoubleTemplateId], null)));
        return new PrestigeElement
        {
            Id = Ids[stage],
            Conditions = conditions,
            TransferConfigs = new TransferConfigs
            {
                StashConfig = new StashPrestigeConfig { XCellCount = 8, YCellCount = 3 + stage },
                SkillConfig = new PrestigeSkillConfig { TransferMultiplier = multiplier },
                MasteringConfig = new PrestigeMasteringConfig { TransferMultiplier = multiplier },
            },
        };
    }

    private static QuestCondition Condition(string type, double? value, ListOrT<string>? target = null, HideoutAreas? area = null) =>
        new() { Id = new MongoId(), ConditionType = type, DynamicLocale = false, Value = value, Target = target, AreaType = area, CompareMethod = ">=" };

    private static Dictionary<MongoId, Quest> NewBeginningQuests()
    {
        int[] original = [25, 30, 35, 40, 47, 47];
        return PrestigeConditionRules.NewBeginningLevelConditions
            .Select((ids, i) => Quest(ids.Quest, [Condition("PrestigeLevel", i), Condition("Level", original[i]) with { Id = ids.Condition }]))
            .ToDictionary(quest => quest.Id);
    }

    private static Quest Quest(MongoId id, List<QuestCondition> startConditions) =>
        new()
        {
            Id = id,
            CanShowNotificationsInGame = false,
            Conditions = new QuestConditionTypes { AvailableForStart = startConditions },
            Description = "",
            Name = "",
            TraderId = new MongoId(),
            Location = "any",
            Image = "",
            Type = QuestTypeEnum.PickUp,
            Restartable = false,
            Side = "Pmc",
        };
}
