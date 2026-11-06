namespace PrestigePerks.Perks;

/// <summary>
/// 액티브 특전 칸 규칙 (dev-docs 94 1절). 칸 하나, P3 에서 열림, 가격 80 / 200 / 350 은 코드 값이다 (config 에 없음).
/// 리롤·잠금·넣기·빼기·구매는 <see cref="PerkRules"/> 가 슬롯 번호 <see cref="SlotIndex"/> 로 받는다.
/// 칸이 열렸다 = <see cref="PerkData.ActivePerkSlot"/> 이 있다 (P3 진입 때 만들고 지우지 않는다).
/// </summary>
public static class ActivePerkRules
{
    /// <summary>통신(lock·equip·unequip)과 규칙 함수에서 액티브 칸을 가리키는 슬롯 번호. 패시브 0~5 다음. 개발용 명령에서는 슬롯 7.</summary>
    public const int SlotIndex = PerkConfig.MaxSlots;

    public const int OpensAt = 3;

    public static IReadOnlyList<int> TierPrices { get; } = [80, 200, 350];

    public static int TierPrice(int tier) => TierPrices[Math.Clamp(tier, 1, PerkConfig.MaxTier) - 1];

    /// <summary>액티브 칸에 적용하는 단계. 칸이 없거나 비었으면 0.</summary>
    public static int AppliedTier(PerkData data, int prestigeLevel, PerkConfig config) =>
        data.ActivePerkSlot?.Perk is { } perk && PerkCatalog.IsActive(perk) ? PerkRules.AppliedTier(data, perk, config.TierCap(prestigeLevel)) : 0;

    /// <summary>리롤·자동 추첨 후보: 미보유 액티브 중 잠금·확정으로 남는 칸의 것 제외. 리롤 칸의 이전 특전은 다시 나올 수 있다.</summary>
    internal static List<string> RollPool(PerkData data)
    {
        var kept = data.ActivePerkSlot is { Rollable: false } slot ? slot.Perk : null;
        return PerkCatalog.ActiveIds.Where(perk => perk != kept && data.OwnedTier(perk) == 0).ToList();
    }
}
