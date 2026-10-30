using System.Text.Json.Serialization;

namespace PrestigePerks.Items;

/// <summary>config/config.json 의 "bossCoins" 항목. 보스 코인 드랍 (dev-docs 3_프레스티지_코인.md 4절).</summary>
public sealed record BossCoinConfig
{
    private readonly IReadOnlyList<string>? roles = BossCoinDrop.DefaultRoles;

    /// <summary>false 면 보스에게 코인을 넣지 않는다.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>보스 한 명이 코인을 가질 확률 (0~1, safeCoins.chance 와 같은 형식). 생성이 끝난 봇마다 한 번 굴린다.</summary>
    [JsonPropertyName("chance")]
    public double Chance { get; init; } = 0.7;

    /// <summary>코인 스택 최소·최대 (1~100). 같은 확률로 고른다.</summary>
    [JsonPropertyName("minStack")]
    public int MinStack { get; init; } = 25;

    [JsonPropertyName("maxStack")]
    public int MaxStack { get; init; } = 35;

    /// <summary>코인을 받는 봇 역할 (SPT bots/types 이름, 대소문자 무시). 다른 모드의 보스는 여기에 더한다.</summary>
    [JsonPropertyName("roles")]
    public IReadOnlyList<string>? Roles
    {
        get => roles;
        init => roles = value?.Select(role => role?.ToLowerInvariant() ?? "").ToList();
    }

    /// <summary>값이 잘못됐으면 이유를 돌려준다. null 이면 정상.</summary>
    public string? Validate()
    {
        if (Chance is < 0 or > 1 || double.IsNaN(Chance))
        {
            return "bossCoins.chance 는 0~1 이어야 합니다";
        }

        if (MinStack < 1 || MaxStack > 100 || MinStack > MaxStack)
        {
            return "bossCoins.minStack, maxStack 은 1~100 이고 최소 <= 최대 여야 합니다";
        }

        return Roles is null || Roles.Any(string.IsNullOrWhiteSpace) ? "bossCoins.roles 는 역할 이름 목록이어야 합니다" : null;
    }
}
