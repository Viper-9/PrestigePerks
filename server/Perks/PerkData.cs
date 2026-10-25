using System.Text.Json.Serialization;

namespace PrestigePerks.Perks;

/// <summary>
/// user/profileData/&lt;프로필ID&gt;/PrestigePerks.json 의 내용. 프로필 파일에는 특전을 저장하지 않는다.
/// </summary>
public sealed class PerkData
{
    public const int CurrentSchemaVersion = 3;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>슬롯 순서의 특전과 상태 (리롤 칸·빈 칸·잠긴 칸·확정 칸). 효과는 앞에서부터 프레스티지 단계 수만큼만 적용한다. 리롤 결과가 바로 여기에 저장된다.</summary>
    [JsonPropertyName("slots")]
    public List<PerkSlot> Slots { get; set; } = [];

    /// <summary>도감: 코인으로 산 특전과 산 단계 (1~3). 모든 프레스티지에서 유지한다. schema 2 파일은 비어 있다.</summary>
    [JsonPropertyName("owned")]
    public Dictionary<int, int> Owned { get; set; } = [];

    [JsonPropertyName("rerolls")]
    public PerkRerolls Rerolls { get; set; } = new();

    /// <summary>새 단계 진입 때 자동으로 뽑았지만 아직 클라이언트에 알리지 않은 특전. state 라우트가 알리고 비운다.</summary>
    [JsonPropertyName("unseenNewPerks")]
    public List<int> UnseenNewPerks { get; set; } = [];

    /// <summary>프레스티지로 올랐지만 아직 클라이언트에 알리지 않은 단계 상한. state 라우트가 알리고 비운다.</summary>
    [JsonPropertyName("unseenTierCap")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? UnseenTierCap { get; set; }

    /// <summary>보유한 단계. 없으면 0.</summary>
    public int OwnedTier(int perk) => Owned.TryGetValue(perk, out var tier) ? tier : 0;

    /// <summary>개발용 (Dev 빌드만): 슬롯과 상관없이 모든 특전을 적용한다. 상용 빌드는 읽을 때 끈다 (<see cref="PerkDataFile.Normalize"/>).</summary>
    [JsonPropertyName("devAllPerks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool DevAllPerks { get; set; }

    /// <summary>schema 1 의 확정 특전. 읽을 때만 쓰고 슬롯으로 옮긴 뒤 비운다.</summary>
    [JsonPropertyName("perks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<int>? LegacyPerks { get; set; }

    /// <summary>파일을 읽다 예상 못 한 오류가 나 빈 데이터로 대신한 경우. 이 데이터로는 파일을 덮어쓰지 않는다.</summary>
    [JsonIgnore]
    public bool LoadFailed { get; init; }
}

public sealed class PerkSlot
{
    /// <summary>빈 칸 번호. 확정 특전을 빼서 생기거나, 뽑을 미보유 특전이 없을 때 생긴다. 다음 리롤 때 채운다.</summary>
    public const int Empty = 0;

    /// <summary>특전 번호. <see cref="Empty"/> 면 빈 칸.</summary>
    [JsonPropertyName("perk")]
    public int Perk { get; set; }

    /// <summary>잠긴 칸: 풀 때까지 리롤에서 빠지고 리롤 비용을 올린다.</summary>
    [JsonPropertyName("locked")]
    public bool Locked { get; set; }

    /// <summary>확정 칸: 보유한 특전을 넣은 칸. 리롤에서 빠지고 리롤 비용에 영향이 없다. 잠금과 함께 켜지지 않는다.</summary>
    [JsonPropertyName("fixed")]
    public bool Fixed { get; set; }

    [JsonIgnore]
    public bool IsEmpty => Perk == Empty;

    /// <summary>다음 리롤 때 다시 뽑는 칸 (리롤 칸·빈 칸).</summary>
    [JsonIgnore]
    public bool Rollable => !Locked && !Fixed;
}

public sealed class PerkRerolls
{
    /// <summary>무료·유료 횟수가 속한 프레스티지 단계. 현재 단계가 더 높으면 새 단계로 보고 횟수를 처음으로 되돌린다.</summary>
    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("freeLeft")]
    public int FreeLeft { get; set; }

    /// <summary>
    /// 이 단계에 준 무료 리롤 횟수 (그때의 config freeRerolls). 설정이 바뀌면 그 차이만큼 남은 횟수를 늘리거나 줄인다 (<see cref="PerkRules.EnterLevel"/>).
    /// 없으면(이 값이 생기기 전 데이터) 기본값 <see cref="PerkConfig.DefaultFreeRerolls"/> 를 준 것으로 본다.
    /// </summary>
    [JsonPropertyName("freeGranted")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? FreeGranted { get; set; }

    /// <summary>이 단계에서 한 유료 리롤 횟수.</summary>
    [JsonPropertyName("paid")]
    public int Paid { get; set; }
}
