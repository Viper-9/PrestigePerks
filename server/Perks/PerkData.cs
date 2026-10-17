using System.Text.Json.Serialization;

namespace PrestigePerks.Perks;

/// <summary>
/// user/profileData/&lt;프로필ID&gt;/PrestigePerks.json 의 내용. 프로필 파일에는 특전을 저장하지 않는다.
/// </summary>
public sealed class PerkData
{
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>확정된 보유 특전 (슬롯 순서). 효과는 이 목록의 앞에서부터 프레스티지 단계 수만큼만 적용한다.</summary>
    [JsonPropertyName("perks")]
    public List<int> Perks { get; set; } = [];

    /// <summary>특전 화면에서 쓰는 코인 잔액. 확정하면 메일로 지급하고 0 이 된다.</summary>
    [JsonPropertyName("coinBalance")]
    public int CoinBalance { get; set; }

    /// <summary>특전 화면 상태. null 이면 확정(닫힘), 값이 있으면 열림.</summary>
    [JsonPropertyName("session")]
    public PerkSession? Session { get; set; }

    /// <summary>파일을 읽다 예상 못 한 오류가 나 빈 데이터로 대신한 경우. 이 데이터로는 파일을 덮어쓰지 않는다.</summary>
    [JsonIgnore]
    public bool LoadFailed { get; init; }
}

public sealed class PerkSession
{
    /// <summary>화면을 연 이유. <see cref="PerkSessionReason"/></summary>
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = PerkSessionReason.Prestige;

    [JsonPropertyName("slots")]
    public List<PerkSlot> Slots { get; set; } = [];

    [JsonPropertyName("freeRerollsLeft")]
    public int FreeRerollsLeft { get; set; }

    /// <summary>이번 화면에서 한 유료 리롤 횟수. 화면을 열 때마다 0 부터 센다.</summary>
    [JsonPropertyName("paidRerolls")]
    public int PaidRerolls { get; set; }
}

public sealed class PerkSlot
{
    /// <summary>특전 번호. null 이면 빈 슬롯.</summary>
    [JsonPropertyName("perk")]
    public int? Perk { get; set; }

    [JsonPropertyName("locked")]
    public bool Locked { get; set; }
}

public static class PerkSessionReason
{
    /// <summary>보유 슬롯 &lt; 프레스티지 단계 (새 프레스티지, 소급 유저).</summary>
    public const string Prestige = "prestige";

    /// <summary>개발용 명령으로 강제로 연 화면. 재추첨권 화면과 같다 (자동 추첨 없이 무료 리롤부터).</summary>
    public const string Dev = "dev";
}
