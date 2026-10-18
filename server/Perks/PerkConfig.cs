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

    /// <summary>이 회차부터 유료 비용이 더 오르지 않는다. 비용 × 배율^(min(유료 회차, 이 값) − 1).</summary>
    [JsonPropertyName("paidCostCapRound")]
    public int PaidCostCapRound { get; init; } = 3;

    /// <summary>프레스티지 단계마다 주는 무료 리롤 횟수. 프레스티지하면 이 값으로 다시 채운다.</summary>
    [JsonPropertyName("freeRerolls")]
    public int FreeRerolls { get; init; } = 3;

    /// <summary>흔들기: 멈추려는 릴이 한 칸 더 가서 다음 칸 특전이 될 확률 (0~1).</summary>
    [JsonPropertyName("shakeChance")]
    public double ShakeChance { get; init; } = 0.3;

    /// <summary>흔들기: 리버스 (멈춘 뒤 거꾸로 돌아 다른 특전) 확률 (0~1). 나머지는 그대로 멈춘다.</summary>
    [JsonPropertyName("shakeReverseChance")]
    public double ShakeReverseChance { get; init; } = 0.05;

    /// <summary>흔들기는 리롤 뒤 이 시간(초) 안에만 받는다 (릴이 도는 동안).</summary>
    [JsonPropertyName("shakeWindowSeconds")]
    public int ShakeWindowSeconds { get; init; } = 15;

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

        if (LockCostStep < 0 || PaidCostGrowth < 1 || PaidCostCapRound < 1 || FreeRerolls < 0)
        {
            return "lockCostStep >= 0, paidCostGrowth >= 1, paidCostCapRound >= 1, freeRerolls >= 0 이어야 합니다";
        }

        if (ShakeChance < 0 || ShakeReverseChance < 0 || ShakeChance + ShakeReverseChance > 1 || ShakeWindowSeconds < 1)
        {
            return "shakeChance, shakeReverseChance 는 0 이상이고 합이 1 이하, shakeWindowSeconds >= 1 이어야 합니다";
        }

        return null;
    }
}

public sealed record ModConfig
{
    [JsonPropertyName("perks")]
    public PerkConfig Perks { get; init; } = new();
}
