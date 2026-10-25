using System.Text.Json.Serialization;
using PrestigePerks.Items;
using PrestigePerks.Prestiges;

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

    /// <summary>프레스티지 단계마다 주는 무료 리롤 횟수 (0~99). 프레스티지하면 이 값으로 다시 채운다.</summary>
    [JsonPropertyName("freeRerolls")]
    public int FreeRerolls { get; init; } = DefaultFreeRerolls;

    public const int MaxSlots = 6, MaxLevel = 6, MaxFreeRerolls = 99, DefaultFreeRerolls = 3;

    /// <summary>P0(프레스티지 전) ~ P6 에서 열리는 슬롯 수 (0~6). 앞에서부터 이 수만큼 특전 효과가 난다. P0 은 기본 0 (특전 없음).</summary>
    [JsonPropertyName("prestigeLevel0Slots")]
    public int PrestigeLevel0Slots { get; init; }

    [JsonPropertyName("prestigeLevel1Slots")]
    public int PrestigeLevel1Slots { get; init; } = 1;

    [JsonPropertyName("prestigeLevel2Slots")]
    public int PrestigeLevel2Slots { get; init; } = 2;

    [JsonPropertyName("prestigeLevel3Slots")]
    public int PrestigeLevel3Slots { get; init; } = 3;

    [JsonPropertyName("prestigeLevel4Slots")]
    public int PrestigeLevel4Slots { get; init; } = 4;

    [JsonPropertyName("prestigeLevel5Slots")]
    public int PrestigeLevel5Slots { get; init; } = 5;

    [JsonPropertyName("prestigeLevel6Slots")]
    public int PrestigeLevel6Slots { get; init; } = 6;

    /// <summary>단계 I·II·III 의 효과 비율. 특전 수치(III)의 효과량에 곱한다 (×0.8 의 I 은 ×0.9).</summary>
    [JsonPropertyName("tierEffectRatio")]
    public List<double> TierEffectRatio { get; init; } = [0.5, 0.75, 1.0];

    /// <summary>P0 ~ P6 에서 쓸 수 있는 가장 높은 단계 (1~3). 표보다 높은 단계는 마지막 값.</summary>
    [JsonPropertyName("tierCapByPrestige")]
    public List<int> TierCapByPrestige { get; init; } = [1, 1, 1, 2, 2, 3, 3];

    /// <summary>단계 I·II·III 가격 (코인). I 은 리롤로 나온 특전을 보유하는 값.</summary>
    [JsonPropertyName("tierPrices")]
    public List<int> TierPrices { get; init; } = [40, 100, 200];

    public const int MaxTier = 3;

    /// <summary>단계별 기본가. 표보다 높은 단계는 마지막 값을 쓴다.</summary>
    public int BasePrice(int prestigeLevel) =>
        RerollBasePrice[Math.Clamp(prestigeLevel, 1, RerollBasePrice.Count) - 1];

    /// <summary>이 단계에서 열리는 슬롯 수. 음수 단계는 0, P6 보다 높으면 P6 값.</summary>
    public int Slots(int prestigeLevel) => prestigeLevel < 0 ? 0 : LevelSlots[Math.Min(prestigeLevel, MaxLevel)];

    /// <summary>이 단계의 단계 상한 (1~3). 칸이 열려 있으면 설정과 상관없이 최소 I 이라 열린 칸의 특전이 꺼지지 않는다.</summary>
    public int TierCap(int prestigeLevel) =>
        TierCapByPrestige is { Count: > 0 } caps
            ? Math.Clamp(caps[Math.Clamp(prestigeLevel, 0, caps.Count - 1)], 1, MaxTier)
            : 1;

    /// <summary>tier 단계(1~3)의 효과 비율. 범위 밖이면 0 (효과 없음).</summary>
    public double TierRatio(int tier) => tier >= 1 && tier <= TierEffectRatio.Count ? TierEffectRatio[tier - 1] : 0;

    /// <summary>tier 단계(1~3)의 가격.</summary>
    public int TierPrice(int tier) => TierPrices[Math.Clamp(tier, 1, MaxTier) - 1];

    /// <summary>지금 단계보다 높은 어느 단계에서 tier 단계가 열리는지. 이미 열렸거나 열리지 않으면 null.</summary>
    public int? TierOpensAt(int prestigeLevel, int tier)
    {
        for (var level = Math.Max(prestigeLevel, 0); level <= MaxLevel; level++)
        {
            if (TierCap(level) >= tier)
            {
                return level == Math.Max(prestigeLevel, 0) ? null : level;
            }
        }

        return null;
    }

    private int[] LevelSlots => [PrestigeLevel0Slots, PrestigeLevel1Slots, PrestigeLevel2Slots, PrestigeLevel3Slots, PrestigeLevel4Slots, PrestigeLevel5Slots, PrestigeLevel6Slots];

    /// <summary>
    /// 슬롯 칸마다 지금 단계보다 높은 어느 단계에서 열리는지 (닫힌 칸 안내용). 이미 열렸거나 더 높은 단계에서도 열리지 않으면 null.
    /// </summary>
    public IReadOnlyList<int?> SlotOpensAt(int prestigeLevel)
    {
        var opensAt = new List<int?>();
        for (var index = 0; index < MaxSlots; index++)
        {
            int? at = null;
            for (var level = Math.Max(prestigeLevel, 0) + 1; index >= Slots(prestigeLevel) && at is null && level <= MaxLevel; level++)
            {
                if (Slots(level) > index)
                {
                    at = level;
                }
            }

            opensAt.Add(at);
        }

        return opensAt;
    }

    /// <summary>범위를 벗어난 슬롯 수(0~6)와 무료 리롤(0~99)을 가까운 끝값으로 맞춘다. 맞춘 값마다 warn 을 부른다.</summary>
    public PerkConfig Clamped(Action<string> warn)
    {
        int Clamp(string name, int value, int max)
        {
            var clamped = Math.Clamp(value, 0, max);
            if (clamped != value)
            {
                warn($"{name}={value} 은 0~{max} 밖이라 {clamped} 로 맞춥니다");
            }

            return clamped;
        }

        return this with
        {
            FreeRerolls = Clamp("freeRerolls", FreeRerolls, MaxFreeRerolls),
            PrestigeLevel0Slots = Clamp("prestigeLevel0Slots", PrestigeLevel0Slots, MaxSlots),
            PrestigeLevel1Slots = Clamp("prestigeLevel1Slots", PrestigeLevel1Slots, MaxSlots),
            PrestigeLevel2Slots = Clamp("prestigeLevel2Slots", PrestigeLevel2Slots, MaxSlots),
            PrestigeLevel3Slots = Clamp("prestigeLevel3Slots", PrestigeLevel3Slots, MaxSlots),
            PrestigeLevel4Slots = Clamp("prestigeLevel4Slots", PrestigeLevel4Slots, MaxSlots),
            PrestigeLevel5Slots = Clamp("prestigeLevel5Slots", PrestigeLevel5Slots, MaxSlots),
            PrestigeLevel6Slots = Clamp("prestigeLevel6Slots", PrestigeLevel6Slots, MaxSlots),
        };
    }

    /// <summary>값이 잘못됐으면 이유를 돌려준다. null 이면 정상. 슬롯 수와 무료 리롤은 먼저 <see cref="Clamped"/> 로 맞춘다.</summary>
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

        if (LockCostStep < 0 || PaidCostGrowth < 1 || PaidCostCapRound < 1)
        {
            return "lockCostStep >= 0, paidCostGrowth >= 1, paidCostCapRound >= 1 이어야 합니다";
        }

        if (TierEffectRatio is not { Count: MaxTier } || TierEffectRatio.Any(ratio => ratio is < 0 or > 1))
        {
            return "tierEffectRatio 는 0~1 값 3개여야 합니다";
        }

        if (TierPrices is not { Count: MaxTier } || TierPrices.Any(price => price < 0))
        {
            return "tierPrices 는 0 이상 값 3개여야 합니다";
        }

        if (TierCapByPrestige is null || TierCapByPrestige.Count == 0)
        {
            return "tierCapByPrestige 가 비어 있습니다";
        }

        return null;
    }
}

public sealed record ModConfig
{
    [JsonPropertyName("perks")]
    public PerkConfig Perks { get; init; } = new();

    [JsonPropertyName("prestige")]
    public PrestigeConfig Prestige { get; init; } = new();

    [JsonPropertyName("safeCoins")]
    public SafeCoinConfig SafeCoins { get; init; } = new();
}
