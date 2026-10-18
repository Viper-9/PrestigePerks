using System.Text.Json.Serialization;

namespace PrestigePerks.Perks;

/// <summary>
/// user/profileData/&lt;프로필ID&gt;/PrestigePerks.json 의 내용. 프로필 파일에는 특전을 저장하지 않는다.
/// </summary>
public sealed class PerkData
{
    public const int CurrentSchemaVersion = 2;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>보유 특전과 잠금 (슬롯 순서). 효과는 앞에서부터 프레스티지 단계 수만큼만 적용한다. 리롤 결과가 바로 여기에 저장된다.</summary>
    [JsonPropertyName("slots")]
    public List<PerkSlot> Slots { get; set; } = [];

    [JsonPropertyName("rerolls")]
    public PerkRerolls Rerolls { get; set; } = new();

    /// <summary>새 단계 진입 때 자동으로 뽑았지만 아직 클라이언트에 알리지 않은 특전. state 라우트가 알리고 비운다.</summary>
    [JsonPropertyName("unseenNewPerks")]
    public List<int> UnseenNewPerks { get; set; } = [];

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
    [JsonPropertyName("perk")]
    public int Perk { get; set; }

    [JsonPropertyName("locked")]
    public bool Locked { get; set; }
}

public sealed class PerkRerolls
{
    /// <summary>무료·유료 횟수가 속한 프레스티지 단계. 현재 단계가 더 높으면 새 단계로 보고 횟수를 처음으로 되돌린다.</summary>
    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("freeLeft")]
    public int FreeLeft { get; set; }

    /// <summary>이 단계에서 한 유료 리롤 횟수.</summary>
    [JsonPropertyName("paid")]
    public int Paid { get; set; }
}
