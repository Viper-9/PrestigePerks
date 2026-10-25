namespace PrestigePerks.Perks;

public enum PerkError
{
    None,
    NoSlots,
    InvalidSlot,
    /// <summary>리롤할 칸이 없다 (열린 칸이 모두 잠금 또는 확정, 빈 칸도 없음).</summary>
    NothingToRoll,
    /// <summary>뽑을 미보유 특전이 없다 (도감 완성).</summary>
    CollectionComplete,
    NotEnoughCoins,
    /// <summary>클라이언트가 본 비용과 서버가 계산한 비용이 다르다 (화면이 오래된 상태).</summary>
    CostChanged,
    /// <summary>개발용 무료 리롤 명령인데 무료 횟수가 없다.</summary>
    NoFreeRerolls,
    /// <summary>모르는 특전 번호.</summary>
    UnknownPerk,
    /// <summary>단계가 1~3 밖이다.</summary>
    InvalidTier,
    /// <summary>이미 그 단계를 가졌다. 구매는 결제 없이 성공으로 돌려준다 (두 번 눌림).</summary>
    AlreadyOwned,
    /// <summary>단계 I 은 리롤로 나와 지금 열린 칸에 있는 특전만 살 수 있다.</summary>
    NotInSlot,
    /// <summary>바로 아래 단계를 갖지 않았다.</summary>
    TierOrder,
    /// <summary>지금 프레스티지의 단계 상한보다 높다.</summary>
    TierCapped,
    /// <summary>보유하지 않은 특전은 확정 칸에 넣을 수 없다.</summary>
    NotOwned,
    /// <summary>이미 칸에 있는 특전이다 (슬롯끼리 옮기기는 없다).</summary>
    AlreadyEquipped,
    /// <summary>잠긴 칸에는 확정 특전을 놓을 수 없다.</summary>
    SlotLocked,
    /// <summary>확정 칸은 잠글 수 없다.</summary>
    SlotFixed,
    /// <summary>빈 칸은 잠글 수 없다.</summary>
    SlotEmpty,
    /// <summary>확정 칸이 아니라 뺄 수 없다.</summary>
    NotFixed,
}

public readonly record struct RollOutcome(PerkError Error, int Cost)
{
    public bool Success => Error == PerkError.None;
}

/// <summary>
/// 특전 규칙 (dev-docs 2_특전.md 1~2절). 파일·프로필·인벤토리를 모르는 순수 함수라 단위 테스트로 검증한다.
/// 코인 결제는 부르는 쪽(<see cref="PerkService"/>)이 <see cref="CheckRoll"/> 과 <see cref="NextRerollCost"/> 로 확인하고 받은 뒤 <see cref="Roll"/> 한다.
/// </summary>
public static class PerkRules
{
    /// <summary>
    /// 유료 리롤 비용 = 기본가 × (1 + 계수 × 잠근 개수) × 배율^(min(유료 회차, 상한 회차) − 1), 소수점 올림.
    /// </summary>
    /// <param name="paidRound">이번에 할 유료 리롤이 몇 번째인지 (1 부터).</param>
    public static int RerollCost(PerkConfig config, int prestigeLevel, int lockedCount, int paidRound)
    {
        var round = Math.Clamp(paidRound, 1, Math.Max(config.PaidCostCapRound, 1));
        var cost = config.BasePrice(prestigeLevel)
                   * (1 + config.LockCostStep * lockedCount)
                   * Math.Pow(config.PaidCostGrowth, round - 1);
        // 262.5 같은 값이 부동소수 오차로 262.50000001 이 되어 한 칸 더 올라가지 않게 한다
        return (int)Math.Ceiling(cost - 1e-9);
    }

    /// <summary>다음 리롤 비용. 무료 횟수가 남았으면 0.</summary>
    public static int NextRerollCost(PerkConfig config, int prestigeLevel, PerkData data) =>
        data.Rerolls.FreeLeft > 0
            ? 0
            : RerollCost(config, prestigeLevel, ActiveSlots(data, config.Slots(prestigeLevel)).Count(slot => slot.Locked), data.Rerolls.Paid + 1);

    /// <summary>효과가 적용되는 슬롯: 앞에서부터 slotCount 개 (단계별 슬롯 수, <see cref="PerkConfig.Slots"/>).</summary>
    public static IReadOnlyList<PerkSlot> ActiveSlots(PerkData data, int slotCount) =>
        data.Slots.Take(Math.Max(slotCount, 0)).ToList();

    /// <summary>효과를 적용할 특전. 개발용 <see cref="PerkData.DevAllPerks"/> 면 슬롯과 상관없이 전부. 빈 칸은 건너뛴다.</summary>
    public static IReadOnlyList<int> ActivePerks(PerkData data, int slotCount) =>
        data.DevAllPerks
            ? PerkCatalog.Numbers
            : ActiveSlots(data, slotCount).Select(slot => slot.Perk).Where(PerkCatalog.IsKnown).Distinct().ToList();

    /// <summary>칸에 있는 특전에 적용하는 단계: 보유한 특전은 보유 단계(상한 이하), 미보유 특전은 I.</summary>
    public static int AppliedTier(PerkData data, int perk, int tierCap) => Math.Max(1, Math.Min(data.OwnedTier(perk), tierCap));

    /// <summary>효과를 적용할 특전과 단계. 개발용 all 은 모든 특전을 III 으로.</summary>
    public static IReadOnlyDictionary<int, int> ActiveTiers(PerkData data, int prestigeLevel, PerkConfig config)
    {
        if (data.DevAllPerks)
        {
            return PerkCatalog.Numbers.ToDictionary(perk => perk, _ => PerkConfig.MaxTier);
        }

        var cap = config.TierCap(prestigeLevel);
        return ActivePerks(data, config.Slots(prestigeLevel)).ToDictionary(perk => perk, perk => AppliedTier(data, perk, cap));
    }

    /// <summary>효과를 적용할 특전과 효과 비율 (<see cref="PerkConfig.TierEffectRatio"/>). Bonus 와 17번 장인이 쓴다.</summary>
    public static IReadOnlyDictionary<int, double> ActiveScales(PerkData data, int prestigeLevel, PerkConfig config) =>
        ActiveTiers(data, prestigeLevel, config).ToDictionary(pair => pair.Key, pair => config.TierRatio(pair.Value));

    /// <summary>
    /// 새 단계 진입 처리. 단계가 기록보다 높으면 무료 횟수를 다시 채우고 유료 회차를 0 으로 되돌린다. 단계 상한이 올랐으면 알림으로 남긴다.
    /// 같은 단계에서 config freeRerolls 가 이 단계에 준 값과 다르면 차이만큼 남은 무료 횟수를 고친다 (0 아래로는 내리지 않는다).
    /// P0(프레스티지 전)은 prestigeLevel0Slots 가 1 이상일 때만 처리하고, 무료 횟수는 처음 한 번 준다 (P1 로 올라가면 다시 채운다).
    /// 슬롯이 단계의 슬롯 수보다 적으면 새 칸을 "전체 − 칸에 있는 특전 − 보유 특전"에서 자동 추첨한다 (무료 횟수 차감 없음). 후보가 없으면 빈 칸.
    /// </summary>
    /// <returns>데이터가 바뀌었는지와 새로 뽑은 특전.</returns>
    public static (bool Changed, IReadOnlyList<int> NewPerks) EnterLevel(PerkData data, int prestigeLevel, PerkConfig config, Random random)
    {
        if (prestigeLevel < 0 || (prestigeLevel == 0 && config.Slots(0) == 0))
        {
            return (false, []);
        }

        var changed = false;
        // P0 은 기록 단계(0)보다 높아질 수 없어 처음 한 번(아직 준 적 없음)을 따로 본다
        if (prestigeLevel > data.Rerolls.Level || (prestigeLevel == 0 && data.Rerolls.FreeGranted is null))
        {
            if (config.TierCap(prestigeLevel) > config.TierCap(data.Rerolls.Level))
            {
                data.UnseenTierCap = config.TierCap(prestigeLevel);
            }

            data.Rerolls = new PerkRerolls { Level = prestigeLevel, FreeLeft = config.FreeRerolls, FreeGranted = config.FreeRerolls, Paid = 0 };
            changed = true;
        }
        else if ((data.Rerolls.FreeGranted ?? PerkConfig.DefaultFreeRerolls) is var granted && granted != config.FreeRerolls)
        {
            // 같은 단계에서 freeRerolls 를 바꿨다: 차이만큼만 더하거나 뺀다. 이미 쓴 횟수는 그대로 쳐서 설정을 오가도 늘지 않는다
            data.Rerolls.FreeLeft = Math.Max(data.Rerolls.FreeLeft + config.FreeRerolls - granted, 0);
            data.Rerolls.FreeGranted = config.FreeRerolls;
            changed = true;
        }

        var newPerks = new List<int>();
        var pool = PerkCatalog.Numbers.Except(data.Slots.Select(slot => slot.Perk)).Where(perk => data.OwnedTier(perk) == 0).ToList();
        while (data.Slots.Count < config.Slots(prestigeLevel))
        {
            var perk = Take(pool, random);
            data.Slots.Add(new PerkSlot { Perk = perk ?? PerkSlot.Empty });
            changed = true;
            if (perk is { } drawn)
            {
                newPerks.Add(drawn);
            }
        }

        data.UnseenNewPerks.AddRange(newPerks);
        return (changed, newPerks);
    }

    /// <summary>슬롯 잠금. slotIndex 는 0 부터. 확정 칸과 빈 칸은 잠글 수 없다 (풀기는 늘 된다).</summary>
    public static PerkError SetLock(PerkData data, int slotCount, int slotIndex, bool locked)
    {
        if (slotIndex < 0 || slotIndex >= ActiveSlots(data, slotCount).Count)
        {
            return PerkError.InvalidSlot;
        }

        var slot = data.Slots[slotIndex];
        if (locked && slot.Fixed)
        {
            return PerkError.SlotFixed;
        }

        if (locked && slot.IsEmpty)
        {
            return PerkError.SlotEmpty;
        }

        slot.Locked = locked;
        return PerkError.None;
    }

    /// <summary>리롤할 수 있는지 (코인 제외).</summary>
    public static PerkError CheckRoll(PerkData data, int slotCount)
    {
        var slots = ActiveSlots(data, slotCount);
        if (slots.Count == 0)
        {
            return PerkError.NoSlots;
        }

        if (!slots.Any(slot => slot.Rollable))
        {
            return PerkError.NothingToRoll;
        }

        return RollPool(data, slotCount).Count == 0 ? PerkError.CollectionComplete : PerkError.None;
    }

    /// <summary>
    /// 리롤. 리롤 칸과 빈 칸을 전부 다시 뽑는다. 무료 횟수가 남았으면 무료 −1, 아니면 유료 회차 +1.
    /// 유료면 비용(<see cref="RollOutcome.Cost"/>)을 부르는 쪽이 미리 받아 두어야 한다.
    /// </summary>
    public static RollOutcome Roll(PerkData data, int prestigeLevel, PerkConfig config, Random random)
    {
        var slotCount = config.Slots(prestigeLevel);
        var error = CheckRoll(data, slotCount);
        if (error != PerkError.None)
        {
            return new RollOutcome(error, 0);
        }

        var cost = NextRerollCost(config, prestigeLevel, data);
        if (data.Rerolls.FreeLeft > 0)
        {
            data.Rerolls.FreeLeft--;
        }
        else
        {
            data.Rerolls.Paid++;
        }

        var pool = RollPool(data, slotCount);
        foreach (var slot in ActiveSlots(data, slotCount).Where(slot => slot.Rollable))
        {
            // 남은 미보유 특전이 리롤 칸보다 적으면 나머지 칸은 비워 둔다
            slot.Perk = Take(pool, random) ?? PerkSlot.Empty;
        }

        return new RollOutcome(PerkError.None, cost);
    }

    /// <summary>
    /// 리롤 후보: 미보유 특전 중 잠긴 칸·확정 칸에 없는 것. 리롤 칸의 이전 특전은 다시 나올 수 있다.
    /// 열린 슬롯보다 뒤의 슬롯(단계가 내려갔거나 슬롯 수를 줄인 경우)은 효과가 없지만 겹치지 않게 뺀다.
    /// </summary>
    private static List<int> RollPool(PerkData data, int slotCount)
    {
        var active = ActiveSlots(data, slotCount);
        var kept = active.Where(slot => !slot.Rollable).Select(slot => slot.Perk)
            .Concat(data.Slots.Skip(active.Count).Select(slot => slot.Perk))
            .ToHashSet();
        return PerkCatalog.Numbers.Where(perk => !kept.Contains(perk) && data.OwnedTier(perk) == 0).ToList();
    }

    /// <summary>
    /// 단계 구매를 확인한다 (코인 제외). I 은 지금 열린 칸에 있는 미보유 특전만, II·III 은 바로 아래 단계를 가졌고 상한 이하일 때만.
    /// 이미 그 단계를 가졌으면 <see cref="PerkError.AlreadyOwned"/> (부르는 쪽이 결제 없이 성공으로 돌려준다).
    /// </summary>
    public static PerkError CheckBuy(PerkData data, int prestigeLevel, PerkConfig config, int perk, int tier)
    {
        if (!PerkCatalog.IsKnown(perk))
        {
            return PerkError.UnknownPerk;
        }

        if (tier is < 1 or > PerkConfig.MaxTier)
        {
            return PerkError.InvalidTier;
        }

        var owned = data.OwnedTier(perk);
        if (owned >= tier)
        {
            return PerkError.AlreadyOwned;
        }

        if (owned != tier - 1)
        {
            return PerkError.TierOrder;
        }

        if (tier > config.TierCap(prestigeLevel))
        {
            return PerkError.TierCapped;
        }

        return tier == 1 && ActiveSlots(data, config.Slots(prestigeLevel)).All(slot => slot.Perk != perk) ? PerkError.NotInSlot : PerkError.None;
    }

    /// <summary>단계 구매 (결제 뒤). I 을 사면 그 칸이 바로 확정 칸이 되고, 잠긴 칸이었다면 잠금이 풀린다.</summary>
    public static PerkError Buy(PerkData data, int prestigeLevel, PerkConfig config, int perk, int tier)
    {
        var error = CheckBuy(data, prestigeLevel, config, perk, tier);
        if (error != PerkError.None)
        {
            return error;
        }

        data.Owned[perk] = tier;
        if (tier == 1)
        {
            var slot = ActiveSlots(data, config.Slots(prestigeLevel)).First(slot => slot.Perk == perk);
            slot.Fixed = true;
            slot.Locked = false;
        }

        return PerkError.None;
    }

    /// <summary>
    /// 확정 넣기: 칸에 없는 보유 특전을 리롤 칸·빈 칸·확정 칸에 놓는다. 리롤 칸의 미보유 특전은 사라지고, 확정 칸이면 서로 바뀐다 (원래 특전은 목록으로).
    /// 잠긴 칸에는 놓을 수 없다.
    /// </summary>
    public static PerkError Equip(PerkData data, int slotCount, int perk, int slotIndex)
    {
        var active = ActiveSlots(data, slotCount);
        if (slotIndex < 0 || slotIndex >= active.Count)
        {
            return PerkError.InvalidSlot;
        }

        if (!PerkCatalog.IsKnown(perk))
        {
            return PerkError.UnknownPerk;
        }

        if (data.OwnedTier(perk) == 0)
        {
            return PerkError.NotOwned;
        }

        if (active.Any(slot => slot.Perk == perk))
        {
            return PerkError.AlreadyEquipped;
        }

        var target = active[slotIndex];
        if (target.Locked)
        {
            return PerkError.SlotLocked;
        }

        // 효과 없는 뒤쪽 슬롯(슬롯 수를 줄인 경우)에 남아 있던 같은 특전은 비운다 (겹치지 않게)
        foreach (var hidden in data.Slots.Skip(active.Count).Where(slot => slot.Perk == perk))
        {
            hidden.Perk = PerkSlot.Empty;
            hidden.Fixed = false;
        }

        target.Perk = perk;
        target.Fixed = true;
        return PerkError.None;
    }

    /// <summary>확정 빼기: 그 칸은 빈 칸이 되어 다음 리롤 때 채워지고, 특전은 보유한 채 목록에 남는다.</summary>
    public static PerkError Unequip(PerkData data, int slotCount, int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= ActiveSlots(data, slotCount).Count)
        {
            return PerkError.InvalidSlot;
        }

        var slot = data.Slots[slotIndex];
        if (!slot.Fixed)
        {
            return PerkError.NotFixed;
        }

        slot.Perk = PerkSlot.Empty;
        slot.Fixed = false;
        return PerkError.None;
    }

    /// <summary>
    /// 개발용: 슬롯에 특전을 직접 넣는다. 다른 슬롯이 그 특전을 갖고 있으면 서로 바꾼다 (겹치지 않게).
    /// 보유한 특전이 들어간 칸은 확정 칸, 아니면 리롤 칸이 된다 (잠금은 미보유일 때만 그대로).
    /// </summary>
    public static PerkError SetPerk(PerkData data, int slotCount, int slotIndex, int perk)
    {
        if (slotIndex < 0 || slotIndex >= ActiveSlots(data, slotCount).Count)
        {
            return PerkError.InvalidSlot;
        }

        if (!PerkCatalog.IsKnown(perk))
        {
            return PerkError.UnknownPerk;
        }

        var other = data.Slots.FindIndex(slot => slot.Perk == perk);
        if (other >= 0 && other != slotIndex)
        {
            Put(data, data.Slots[other], data.Slots[slotIndex].Perk);
        }

        Put(data, data.Slots[slotIndex], perk);
        return PerkError.None;
    }

    /// <summary>개발용: 보유 단계를 정한다 (0 이면 도감에서 뺀다). 확정 칸의 특전을 빼면 그 칸은 리롤 칸이 된다.</summary>
    public static PerkError SetOwnedTier(PerkData data, int perk, int tier)
    {
        if (!PerkCatalog.IsKnown(perk))
        {
            return PerkError.UnknownPerk;
        }

        if (tier is < 0 or > PerkConfig.MaxTier)
        {
            return PerkError.InvalidTier;
        }

        if (tier == 0)
        {
            data.Owned.Remove(perk);
            foreach (var slot in data.Slots.Where(slot => slot.Perk == perk))
            {
                slot.Fixed = false;
            }
        }
        else
        {
            data.Owned[perk] = tier;
        }

        return PerkError.None;
    }

    /// <summary>개발용: 도감을 비운다. 확정 칸은 특전을 그대로 둔 리롤 칸이 된다.</summary>
    public static void ResetCollection(PerkData data)
    {
        data.Owned.Clear();
        foreach (var slot in data.Slots)
        {
            slot.Fixed = false;
        }
    }

    private static void Put(PerkData data, PerkSlot slot, int perk)
    {
        slot.Perk = perk;
        slot.Fixed = !slot.IsEmpty && data.OwnedTier(perk) > 0;
        slot.Locked = slot.Locked && !slot.Fixed && !slot.IsEmpty;
    }

    private static int? Take(List<int> pool, Random random)
    {
        if (pool.Count == 0)
        {
            return null;
        }

        var index = random.Next(pool.Count);
        var number = pool[index];
        pool.RemoveAt(index);
        return number;
    }
}
