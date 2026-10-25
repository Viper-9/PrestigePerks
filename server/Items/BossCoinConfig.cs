using System.Text.Json.Serialization;

namespace PrestigePerks.Items;

/// <summary>config/config.json 의 "bossCoins" 항목. 보스 코인 드랍 (dev-docs 3_프레스티지_코인.md 4절).</summary>
public sealed record BossCoinConfig
{
    /// <summary>보스 한 명이 코인을 들고 있을 확률 (0~1). 0 이면 보스에게 코인을 넣지 않는다.</summary>
    [JsonPropertyName("chance")]
    public double Chance { get; init; } = 1;

    /// <summary>보스가 들고 있는 코인 스택의 최소·최대 (1~100). SPT bot.json 의 currencyStackSize 에 넣는다.</summary>
    [JsonPropertyName("minStack")]
    public int MinStack { get; init; } = 5;

    [JsonPropertyName("maxStack")]
    public int MaxStack { get; init; } = 10;

    /// <summary>값이 잘못됐으면 이유를 돌려준다. null 이면 정상.</summary>
    public string? Validate()
    {
        if (Chance is < 0 or > 1 || double.IsNaN(Chance))
        {
            return "bossCoins.chance 는 0~1 이어야 합니다";
        }

        if (MinStack < 1 || MaxStack > 100 || MinStack > MaxStack)
        {
            return "bossCoins.minStack, maxStack 은 1~100 이고 minStack <= maxStack 이어야 합니다";
        }

        return null;
    }
}
