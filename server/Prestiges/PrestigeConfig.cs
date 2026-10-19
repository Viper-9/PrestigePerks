using System.Text.Json.Serialization;

namespace PrestigePerks.Prestiges;

/// <summary>config/config.json 의 "prestige" 항목. 단계별 프레스티지 조건 (dev-docs 1_프레스티지_조건.md 1절, 5절).</summary>
public sealed record PrestigeConfig
{
    /// <summary>P1 ~ P6 순서. 단계 수는 프레스티지 원소 수(6)와 같아야 한다.</summary>
    [JsonPropertyName("stages")]
    public List<PrestigeStageConfig> Stages { get; init; } = DefaultStages();

    /// <summary>값이 잘못됐으면 이유를 돌려준다. null 이면 정상.</summary>
    public string? Validate()
    {
        if (Stages is null || Stages.Count != PrestigeConditionRules.ElementIds.Length)
        {
            return $"prestige.stages 는 {PrestigeConditionRules.ElementIds.Length}개여야 합니다";
        }

        for (var i = 0; i < Stages.Count; i++)
        {
            var error = Stages[i]?.Validate();
            if (Stages[i] is null || error is not null)
            {
                return $"prestige.stages[{i}] (P{i + 1}): {error ?? "비어 있습니다"}";
            }
        }

        return null;
    }

    private static List<PrestigeStageConfig> DefaultStages()
    {
        Dictionary<string, int> skillsLow = new() { ["Strength"] = 10, ["Endurance"] = 10, ["Charisma"] = 7 };
        Dictionary<int, int> hideoutLow = new() { [11] = 1, [9] = 2, [8] = 2 };
        Dictionary<int, int> hideoutHigh = new() { [11] = 2, [1] = 3, [9] = 3 };
        return
        [
            new() { Level = 15, Hideout = [], Roubles = 5_000_000, TransferMultiplier = 0.9, NewBeginningLevel = 15 },
            new() { Level = 25, Skills = new(skillsLow), Hideout = new(hideoutLow), Roubles = 10_000_000, TransferMultiplier = 0.8, NewBeginningLevel = 25 },
            new() { Level = 35, Skills = new(skillsLow), Hideout = new(hideoutLow), Roubles = 12_000_000, TransferMultiplier = 0.7 },
            new() { Level = 40, Skills = new() { ["Strength"] = 15, ["Endurance"] = 15, ["Charisma"] = 12 }, Hideout = new(hideoutLow), Roubles = 15_000_000, TransferMultiplier = 0.6 },
            new() { Level = 47, Skills = new() { ["Strength"] = 20, ["Endurance"] = 20, ["Charisma"] = 20 }, Hideout = new(hideoutHigh), Roubles = 20_000_000, TransferMultiplier = 0.5 },
            new() { Level = 47, Skills = new() { ["Strength"] = 20, ["Endurance"] = 20, ["Charisma"] = 20 }, Hideout = new(hideoutHigh), Roubles = 20_000_000, TransferMultiplier = 0.5 },
        ];
    }
}

/// <summary>한 단계의 조건. 값이 없는(null) 항목은 원래 데이터를 그대로 둔다.</summary>
public sealed record PrestigeStageConfig
{
    /// <summary>필요 레벨 (Level 조건).</summary>
    [JsonPropertyName("level")]
    public int? Level { get; init; }

    /// <summary>스킬 이름 → 필요 레벨 (Skill 조건). 여기 없는 스킬 조건은 지운다.</summary>
    [JsonPropertyName("skills")]
    public Dictionary<string, int>? Skills { get; init; }

    /// <summary>은신처 시설 번호(areaType) → 필요 레벨 (HideoutArea 조건). 여기 없는 시설 조건은 지운다. 빈 값이면 은신처 조건을 모두 지운다.</summary>
    [JsonPropertyName("hideout")]
    public Dictionary<int, int>? Hideout { get; init; }

    /// <summary>필요 루블 (루블 HasItem 조건).</summary>
    [JsonPropertyName("roubles")]
    public long? Roubles { get; init; }

    /// <summary>스킬·숙련도 진행도 중 잃는 비율 (0~1). 유지율 = 1 − 이 값.</summary>
    [JsonPropertyName("transferMultiplier")]
    public double? TransferMultiplier { get; init; }

    /// <summary>이 단계의 New Beginning 퀘스트 시작 레벨.</summary>
    [JsonPropertyName("newBeginningLevel")]
    public int? NewBeginningLevel { get; init; }

    /// <summary>프레스티지 조건에 추가할 퀘스트 (상인 모드 연동 자리). 퀘스트가 없으면 건너뛰고 경고한다.</summary>
    [JsonPropertyName("extraQuests")]
    public List<ExtraQuestConfig>? ExtraQuests { get; init; }

    internal string? Validate()
    {
        if (Level is < 1 || NewBeginningLevel is < 1)
        {
            return "level, newBeginningLevel 은 1 이상이어야 합니다";
        }

        if (Roubles is < 0 || TransferMultiplier is < 0 or > 1)
        {
            return "roubles >= 0, transferMultiplier 는 0~1 이어야 합니다";
        }

        if ((Skills?.Values.AsEnumerable() ?? []).Concat(Hideout?.Values.AsEnumerable() ?? []).Any(value => value < 0))
        {
            return "skills, hideout 값에 음수가 있습니다";
        }

        if ((ExtraQuests ?? []).Any(quest => quest is null || !IsMongoId(quest.Quest) || !IsMongoId(quest.Condition)))
        {
            return "extraQuests 의 quest, condition 은 24자리 ID 여야 합니다";
        }

        return null;
    }

    private static bool IsMongoId(string? id) => id is { Length: 24 } && id.All(Uri.IsHexDigit);
}

/// <summary>프레스티지 조건에 넣을 퀘스트. 조건 ID 는 고정값으로 정한다 (공유 상인 모드 가이드).</summary>
public sealed record ExtraQuestConfig
{
    [JsonPropertyName("quest")]
    public string Quest { get; init; } = "";

    [JsonPropertyName("condition")]
    public string Condition { get; init; } = "";
}
