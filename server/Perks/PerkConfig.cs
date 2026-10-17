using System.Text.Json.Serialization;

namespace PrestigePerks.Perks;

/// <summary>config/config.json 의 "perks" 항목. 리롤 비용 기본가와 계수 (dev-docs 2_특전.md 2절).</summary>
public sealed record PerkConfig
{
    /// <summary>P1 ~ P6 유료 리롤 기본가 (코인).</summary>
    [JsonPropertyName("rerollBasePrice")]
    public List<int> RerollBasePrice { get; init; } = [25, 35, 45, 55, 65, 75];

    /// <summary>잠근 특전 하나당 비용 배율 증가분. 비용 × (1 + 이 값 × 잠근 개수).</summary>
    [JsonPropertyName("lockCostStep")]
    public double LockCostStep { get; init; } = 0.5;

    /// <summary>유료 회차마다 곱하는 배율. 비용 × 이 값^(유료 회차 − 1).</summary>
    [JsonPropertyName("paidCostGrowth")]
    public double PaidCostGrowth { get; init; } = 2;

    /// <summary>화면을 열 때 주는 무료 리롤 횟수.</summary>
    [JsonPropertyName("freeRerolls")]
    public int FreeRerolls { get; init; } = 3;

    /// <summary>단계별 기본가. 표보다 높은 단계는 마지막 값을 쓴다.</summary>
    public int BasePrice(int prestigeLevel) =>
        RerollBasePrice[Math.Clamp(prestigeLevel, 1, RerollBasePrice.Count) - 1];

    /// <summary>값이 잘못됐으면 이유를 돌려준다. null 이면 정상.</summary>
    public string? Validate()
    {
        if (RerollBasePrice is null || RerollBasePrice.Count == 0)
        {
            return "rerollBasePrice 가 비어 있습니다";
        }

        if (RerollBasePrice.Any(price => price < 0))
        {
            return "rerollBasePrice 에 음수가 있습니다";
        }

        if (LockCostStep < 0 || PaidCostGrowth < 1 || FreeRerolls < 0)
        {
            return "lockCostStep >= 0, paidCostGrowth >= 1, freeRerolls >= 0 이어야 합니다";
        }

        return null;
    }
}

public sealed record ModConfig
{
    [JsonPropertyName("perks")]
    public PerkConfig Perks { get; init; } = new();
}
