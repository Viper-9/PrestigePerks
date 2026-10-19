using System.Text.Json.Serialization;
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

    /// <summary>흔들기 켜기/끄기. 끄면 리롤 중 흔들기 버튼이 나오지 않고 서버도 받지 않는다.</summary>
    [JsonPropertyName("shakeEnabled")]
    public bool ShakeEnabled { get; init; } = true;

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

    /// <summary>이 단계에서 열리는 슬롯 수. 음수 단계는 0, P6 보다 높으면 P6 값.</summary>
    public int Slots(int prestigeLevel) => prestigeLevel < 0 ? 0 : LevelSlots[Math.Min(prestigeLevel, MaxLevel)];

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

    [JsonPropertyName("prestige")]
    public PrestigeConfig Prestige { get; init; } = new();
}
