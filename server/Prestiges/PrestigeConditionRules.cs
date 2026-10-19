using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils.Json;

namespace PrestigePerks.Prestiges;

/// <summary>
/// 프레스티지 조건을 설정값으로 바꾼다 (dev-docs 1_프레스티지_조건.md 5절). 조건은 원소 ID + 종류 + 대상으로 찾아 값만 바꾸고
/// 조건 ID 는 그대로 둔다. 못 찾은 것은 건너뛰고 결과에 남긴다 (호출하는 쪽이 경고 로그).
/// </summary>
public static class PrestigeConditionRules
{
    /// <summary>P1 ~ P6 프레스티지 원소 ID (SPT 4.1 P1~P4, 백포트 모드 P5·P6).</summary>
    public static readonly string[] ElementIds =
    [
        "672df12f97f0469cea52f55e",
        "672df4281ab8d9c8849a0c88",
        "683da91d6f472cfa738c52f2",
        "6842f121000d98ce33b9a60f",
        "68d3ddb4fc101237e601d774",
        "68d3e6f46a7ba36646713fa6",
    ];

    /// <summary>NB-P1 ~ NB-P6 퀘스트와 시작 레벨 조건 (AvailableForStart) ID. 백포트 모드 1.1.0.</summary>
    public static readonly (string Quest, string Condition)[] NewBeginningLevelConditions =
    [
        ("6761f28a022f60bb320f3e95", "695030570ff3a824dca38f1f"),
        ("6761ff17cdc36bd66102e9d0", "6950972eeb0b25da3e0aed12"),
        ("6848100b00afffa81f09e365", "69511fc0f998a782a77a75d7"),
        ("68481881f43abfdda2058369", "69512adcef09bb1f92d61726"),
        ("68d3eabc22a258476b064851", "695133abbc408241ab50136f"),
        ("68d3eb2b2712147eab000f21", "695143e5e7c8295e43b34c66"),
    ];

    public const string RoubleTemplateId = "5449016a4bdc2d6f028b456f";

    private const string LevelType = "Level";
    private const string SkillType = "Skill";
    private const string HideoutType = "HideoutArea";
    private const string HasItemType = "HasItem";
    private const string QuestType = "Quest";

    public static PrestigeApplyResult Apply(Prestige prestige, IReadOnlyDictionary<MongoId, Quest> quests, PrestigeConfig config)
    {
        var result = new PrestigeApplyResult();
        for (var i = 0; i < config.Stages.Count && i < ElementIds.Length; i++)
        {
            var stage = config.Stages[i];
            var name = $"P{i + 1}";
            var element = prestige.Elements?.FirstOrDefault(e => e.Id == ElementIds[i]);
            if (element is null)
            {
                result.Skip($"{name} 프레스티지 원소 {ElementIds[i]} 없음");
            }
            else
            {
                ApplyElement(element, stage, quests, name, result);
            }

            if (stage.NewBeginningLevel is { } startLevel)
            {
                ApplyNewBeginningLevel(quests, NewBeginningLevelConditions[i], startLevel, name, result);
            }
        }

        return result;
    }

    private static void ApplyElement(PrestigeElement element, PrestigeStageConfig stage, IReadOnlyDictionary<MongoId, Quest> quests, string name, PrestigeApplyResult result)
    {
        var conditions = element.Conditions ??= [];

        if (stage.Level is { } level)
        {
            SetValue(conditions.Where(c => c.ConditionType == LevelType), level, $"{name} 레벨 조건", result);
        }

        if (stage.Skills is { } skills)
        {
            ApplyKeyed(conditions, SkillType, SingleTarget, skills, key => $"{name} 스킬 {key} 조건", result);
        }

        if (stage.Hideout is { } hideout)
        {
            var areas = hideout.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value);
            ApplyKeyed(conditions, HideoutType, c => c.AreaType is { } area ? ((int)area).ToString() : null, areas, key => $"{name} 은신처 {key} 조건", result);
        }

        if (stage.Roubles is { } roubles)
        {
            SetValue(conditions.Where(c => c.ConditionType == HasItemType && Targets(c).Contains(RoubleTemplateId)), roubles, $"{name} 루블 조건", result);
        }

        if (stage.TransferMultiplier is { } multiplier)
        {
            if (element.TransferConfigs?.SkillConfig is { } skill && element.TransferConfigs.MasteringConfig is { } mastering)
            {
                result.Count(skill.TransferMultiplier != multiplier);
                result.Count(mastering.TransferMultiplier != multiplier);
                skill.TransferMultiplier = multiplier;
                mastering.TransferMultiplier = multiplier;
            }
            else
            {
                result.Skip($"{name} transferConfigs 없음");
            }
        }

        foreach (var extra in stage.ExtraQuests ?? [])
        {
            AddQuest(conditions, extra, quests, name, result);
        }
    }

    /// <summary>스킬·은신처처럼 대상별로 여러 개인 조건. 설정에 있는 대상은 값을 바꾸고, 없는 대상의 조건은 지운다.</summary>
    private static void ApplyKeyed(List<QuestCondition> conditions, string type, Func<QuestCondition, string?> keyOf, Dictionary<string, int> wanted, Func<string, string> describe, PrestigeApplyResult result)
    {
        var removed = conditions.RemoveAll(c => c.ConditionType == type && (keyOf(c) is not { } key || !wanted.ContainsKey(key)));
        result.Removed += removed;
        foreach (var (key, value) in wanted)
        {
            SetValue(conditions.Where(c => c.ConditionType == type && keyOf(c) == key), value, describe(key), result);
        }
    }

    private static void SetValue(IEnumerable<QuestCondition> matches, double value, string description, PrestigeApplyResult result)
    {
        var condition = matches.FirstOrDefault();
        if (condition is null)
        {
            result.Skip($"{description} 없음");
            return;
        }

        result.Count(condition.Value != value);
        condition.Value = value;
    }

    private static void AddQuest(List<QuestCondition> conditions, ExtraQuestConfig extra, IReadOnlyDictionary<MongoId, Quest> quests, string name, PrestigeApplyResult result)
    {
        if (conditions.Any(c => c.Id == extra.Condition))
        {
            return;
        }

        if (!quests.ContainsKey(extra.Quest))
        {
            result.Skip($"{name} 추가 퀘스트 {extra.Quest} 없음 (조건에 넣지 않음)");
            return;
        }

        conditions.Add(new QuestCondition
        {
            Id = extra.Condition,
            ConditionType = QuestType,
            DynamicLocale = false,
            Index = conditions.Count,
            Target = new ListOrT<string>(null, extra.Quest),
            Status = [QuestStatusEnum.Success],
            AvailableAfter = 0,
            Dispersion = 0,
            GlobalQuestCounterId = "",
            ParentId = "",
            VisibilityConditions = [],
        });
        result.Added++;
    }

    private static void ApplyNewBeginningLevel(IReadOnlyDictionary<MongoId, Quest> quests, (string Quest, string Condition) ids, int level, string name, PrestigeApplyResult result)
    {
        if (!quests.TryGetValue(ids.Quest, out var quest))
        {
            result.Skip($"{name} New Beginning 퀘스트 {ids.Quest} 없음");
            return;
        }

        SetValue((quest.Conditions?.AvailableForStart ?? []).Where(c => c.Id == ids.Condition), level, $"{name} New Beginning 시작 레벨 조건 {ids.Condition}", result);
    }

    /// <summary>
    /// 로그용 한 줄 요약 (실제 데이터를 다시 읽는다).
    /// 예: "level 25, roubles 10,000,000, skills Strength 10 Endurance 10 Charisma 7, hideout 11:1 9:2 8:2, quests 6761ff17…, keep skill 20% mastering 20%, window 8x4"
    /// </summary>
    public static string Describe(PrestigeElement element)
    {
        var conditions = element.Conditions ?? [];
        string Join(IEnumerable<string> parts) => parts.DefaultIfEmpty("none").Aggregate((a, b) => $"{a} {b}");

        var level = conditions.FirstOrDefault(c => c.ConditionType == LevelType)?.Value;
        var roubles = conditions.FirstOrDefault(c => c.ConditionType == HasItemType && Targets(c).Contains(RoubleTemplateId))?.Value;
        var skills = Join(conditions.Where(c => c.ConditionType == SkillType).Select(c => $"{SingleTarget(c)} {c.Value}"));
        var hideout = Join(conditions.Where(c => c.ConditionType == HideoutType).Select(c => $"{(int?)c.AreaType}:{c.Value}"));
        var quests = Join(conditions.Where(c => c.ConditionType == QuestType).Select(c => SingleTarget(c) ?? "?"));
        var transfer = element.TransferConfigs;
        var stash = transfer?.StashConfig;
        string Keep(double? multiplier) => multiplier is { } m ? $"{(1 - m) * 100:0.#}%" : "?";
        return $"level {level?.ToString() ?? "none"}, roubles {roubles?.ToString("N0") ?? "none"}, skills {skills}, hideout {hideout}, quests {quests}, " +
               $"keep skill {Keep(transfer?.SkillConfig?.TransferMultiplier)} mastering {Keep(transfer?.MasteringConfig?.TransferMultiplier)}, window {stash?.XCellCount}x{stash?.YCellCount}";
    }

    /// <summary>로그용: NB-P1 ~ P6 시작 레벨 ("P1 15, P2 25, …"). 못 찾으면 "?".</summary>
    public static string DescribeNewBeginningLevels(IReadOnlyDictionary<MongoId, Quest> quests) =>
        string.Join(", ", NewBeginningLevelConditions.Select((ids, i) =>
        {
            var value = quests.TryGetValue(ids.Quest, out var quest)
                ? quest.Conditions?.AvailableForStart?.FirstOrDefault(c => c.Id == ids.Condition)?.Value
                : null;
            return $"P{i + 1} {value?.ToString() ?? "?"}";
        }));

    private static string? SingleTarget(QuestCondition condition) => condition.Target?.IsItem == true ? condition.Target.Item : condition.Target?.List?.FirstOrDefault();

    private static IEnumerable<string> Targets(QuestCondition condition) =>
        condition.Target?.IsItem == true ? [condition.Target.Item!] : condition.Target?.List ?? [];
}

/// <summary>적용 결과. Changed = 값이 바뀐 조건 수, Removed = 지운 조건 수, Added = 추가한 퀘스트 조건 수.</summary>
public sealed class PrestigeApplyResult
{
    public int Changed { get; set; }
    public int Removed { get; set; }
    public int Added { get; set; }
    public List<string> Skipped { get; } = [];

    internal void Count(bool changed)
    {
        if (changed)
        {
            Changed++;
        }
    }

    internal void Skip(string reason) => Skipped.Add(reason);
}
