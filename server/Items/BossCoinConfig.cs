using System.Text.Json.Serialization;

namespace PrestigePerks.Items;

/// <summary>config/config.json 의 "bossCoins" 항목. 보스 코인 드랍 (dev-docs 3_프레스티지_코인.md 4절).</summary>
public sealed record BossCoinConfig
{
    /// <summary>false 면 보스에게 코인을 넣지 않는다.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>코인 스택 최소·최대 (1~100, 보스 한 명당 약 23%). SPT bot.json 의 currencyStackSize 에 넣는다.</summary>
    [JsonPropertyName("minStack")]
    public int MinStack { get; init; } = 20;

    [JsonPropertyName("maxStack")]
    public int MaxStack { get; init; } = 35;

    /// <summary>GP 코인 보스 모드가 있어 화폐 뽑기를 반반 나눌 때 (약 12.5%) 의 스택. 기대값을 맞추려고 늘린다.</summary>
    [JsonPropertyName("sharedMinStack")]
    public int SharedMinStack { get; init; } = 40;

    [JsonPropertyName("sharedMaxStack")]
    public int SharedMaxStack { get; init; } = 60;

    /// <summary>GP 코인 보스 모드가 있는지에 따른 스택 범위.</summary>
    public (int Min, int Max) StackRange(bool sharedWithGp) => sharedWithGp ? (SharedMinStack, SharedMaxStack) : (MinStack, MaxStack);

    /// <summary>값이 잘못됐으면 이유를 돌려준다. null 이면 정상.</summary>
    public string? Validate()
    {
        foreach (var (name, min, max) in new[]
                 {
                     ("minStack, maxStack", MinStack, MaxStack),
                     ("sharedMinStack, sharedMaxStack", SharedMinStack, SharedMaxStack),
                 })
        {
            if (min < 1 || max > 100 || min > max)
            {
                return $"bossCoins.{name} 은 1~100 이고 최소 <= 최대 여야 합니다";
            }
        }

        return null;
    }
}
