using System.Text.Json.Serialization;

namespace PrestigePerks.Items;

/// <summary>config/config.json 의 "coin" 항목. 프레스티지 코인 아이템 자체 설정.</summary>
public sealed record CoinConfig
{
    /// <summary>stackSize 상한. 코인 1개 = 100,000₽ 라 한 칸에 너무 많이 쌓이지 않게 막는다.</summary>
    public const int MaxStackSize = 1_000;

    /// <summary>코인 한 칸에 쌓이는 최대 개수 (1~1000). 코인 템플릿의 StackMaxSize 가 된다.</summary>
    [JsonPropertyName("stackSize")]
    public int StackSize { get; init; } = 100;

    /// <summary>값이 잘못됐으면 이유를 돌려준다. null 이면 정상.</summary>
    public string? Validate() =>
        StackSize is < 1 or > MaxStackSize ? $"coin.stackSize 는 1~{MaxStackSize} 이어야 합니다" : null;
}
