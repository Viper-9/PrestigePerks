namespace PrestigePerks.Perks;

/// <summary>흔들기 결과.</summary>
public enum ShakeOutcome
{
    /// <summary>그대로 멈춘다.</summary>
    Stay,
    /// <summary>한 칸 더 가서 다음 칸 특전이 된다.</summary>
    Next,
    /// <summary>거꾸로 돌아 다른 특전이 된다.</summary>
    Reverse,
}

public enum PerkError
{
    None,
    NoSlots,
    InvalidSlot,
    AllLocked,
    NotEnoughCoins,
    /// <summary>클라이언트가 본 비용과 서버가 계산한 비용이 다르다 (화면이 오래된 상태).</summary>
    CostChanged,
    /// <summary>개발용 무료 리롤 명령인데 무료 횟수가 없다.</summary>
    NoFreeRerolls,
    /// <summary>흔들 리롤이 없다 (이미 흔들었거나, 시간이 지났거나, 리롤하지 않았다).</summary>
    NoShake,
    /// <summary>모르는 특전 번호 (개발용 set 명령).</summary>
    UnknownPerk,
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
            : RerollCost(config, prestigeLevel, ActiveSlots(data, prestigeLevel).Count(slot => slot.Locked), data.Rerolls.Paid + 1);

    /// <summary>효과가 적용되는 슬롯: 앞에서부터 단계 수만큼.</summary>
    public static IReadOnlyList<PerkSlot> ActiveSlots(PerkData data, int prestigeLevel) =>
        data.Slots.Take(Math.Max(prestigeLevel, 0)).ToList();

    /// <summary>효과를 적용할 특전.</summary>
    public static IReadOnlyList<int> ActivePerks(PerkData data, int prestigeLevel) =>
        ActiveSlots(data, prestigeLevel).Select(slot => slot.Perk).Where(PerkCatalog.IsKnown).Distinct().ToList();

    /// <summary>
    /// 새 단계 진입 처리. 단계가 기록보다 높으면 무료 횟수를 다시 채우고 유료 회차를 0 으로 되돌린다.
    /// 슬롯이 단계보다 적으면 빈 슬롯을 "전체 − 보유 특전"에서 자동 추첨한다 (무료 횟수 차감 없음). 새 프레스티지와 소급 유저가 같은 규칙이다.
    /// </summary>
    /// <returns>데이터가 바뀌었는지와 새로 뽑은 특전.</returns>
    public static (bool Changed, IReadOnlyList<int> NewPerks) EnterLevel(PerkData data, int prestigeLevel, PerkConfig config, Random random)
    {
        if (prestigeLevel <= 0)
        {
            return (false, []);
        }

        var changed = false;
        if (prestigeLevel > data.Rerolls.Level)
        {
            data.Rerolls = new PerkRerolls { Level = prestigeLevel, FreeLeft = config.FreeRerolls, Paid = 0 };
            changed = true;
        }

        var newPerks = new List<int>();
        var pool = PerkCatalog.Numbers.Except(data.Slots.Select(slot => slot.Perk)).ToList();
        while (data.Slots.Count < prestigeLevel && Take(pool, random) is { } perk)
        {
            data.Slots.Add(new PerkSlot { Perk = perk });
            newPerks.Add(perk);
        }

        if (newPerks.Count > 0)
        {
            data.UnseenNewPerks.AddRange(newPerks);
            changed = true;
        }

        return (changed, newPerks);
    }

    /// <summary>슬롯 잠금. slotIndex 는 0 부터.</summary>
    public static PerkError SetLock(PerkData data, int prestigeLevel, int slotIndex, bool locked)
    {
        if (slotIndex < 0 || slotIndex >= ActiveSlots(data, prestigeLevel).Count)
        {
            return PerkError.InvalidSlot;
        }

        data.Slots[slotIndex].Locked = locked;
        return PerkError.None;
    }

    /// <summary>리롤할 수 있는지 (코인 제외).</summary>
    public static PerkError CheckRoll(PerkData data, int prestigeLevel)
    {
        var slots = ActiveSlots(data, prestigeLevel);
        if (slots.Count == 0)
        {
            return PerkError.NoSlots;
        }

        return slots.All(slot => slot.Locked) ? PerkError.AllLocked : PerkError.None;
    }

    /// <summary>
    /// 리롤. 잠그지 않은 슬롯을 전부 다시 뽑는다. 무료 횟수가 남았으면 무료 −1, 아니면 유료 회차 +1.
    /// 유료면 비용(<see cref="RollOutcome.Cost"/>)을 부르는 쪽이 미리 받아 두어야 한다.
    /// </summary>
    public static RollOutcome Roll(PerkData data, int prestigeLevel, PerkConfig config, Random random)
    {
        var error = CheckRoll(data, prestigeLevel);
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

        // 단계보다 뒤의 슬롯(단계가 내려간 경우)은 효과가 없지만 겹치지 않게 후보에서 뺀다
        Redraw(data.Slots.Take(prestigeLevel).ToList(), random, data.Slots.Skip(prestigeLevel).Select(slot => slot.Perk));
        return new RollOutcome(PerkError.None, cost);
    }

    /// <summary>잠그지 않은 슬롯을 다시 뽑는다. 잠근 특전은 빠지고, 잠그지 않은 이전 특전은 다시 나올 수 있다.</summary>
    /// <summary>
    /// 흔들기: 멈추려는 슬롯 하나. <paramref name="nextPerk"/> 는 화면에서 그 릴의 다음 칸에 보이는 특전이다.
    /// reverseChance 확률로 리버스(다른 특전), nextChance 확률로 다음 칸 특전, 나머지는 그대로.
    /// 다음 칸 특전이 다른 슬롯과 겹치거나 모르는 특전이면 한 칸 더 가지 않는다 (그대로).
    /// </summary>
    public static ShakeOutcome Shake(PerkData data, int prestigeLevel, int slotIndex, int nextPerk, double nextChance, double reverseChance, Random random)
    {
        var active = ActiveSlots(data, prestigeLevel);
        if (slotIndex < 0 || slotIndex >= active.Count || active[slotIndex].Locked)
        {
            return ShakeOutcome.Stay;
        }

        var taken = data.Slots.Select(slot => slot.Perk).ToHashSet();
        var roll = random.NextDouble();
        if (roll < reverseChance)
        {
            var pool = PerkCatalog.Numbers.Where(number => !taken.Contains(number) && number != nextPerk).ToList();
            if (Take(pool, random) is { } perk)
            {
                active[slotIndex].Perk = perk;
                return ShakeOutcome.Reverse;
            }

            return ShakeOutcome.Stay;
        }

        if (roll < reverseChance + nextChance && PerkCatalog.IsKnown(nextPerk) && !taken.Contains(nextPerk))
        {
            active[slotIndex].Perk = nextPerk;
            return ShakeOutcome.Next;
        }

        return ShakeOutcome.Stay;
    }

    /// <summary>
    /// 개발용: 슬롯에 특전을 직접 넣는다. 다른 슬롯이 그 특전을 갖고 있으면 서로 바꾼다 (겹치지 않게). 잠금은 그대로 둔다.
    /// </summary>
    public static PerkError SetPerk(PerkData data, int prestigeLevel, int slotIndex, int perk)
    {
        if (slotIndex < 0 || slotIndex >= ActiveSlots(data, prestigeLevel).Count)
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
            data.Slots[other].Perk = data.Slots[slotIndex].Perk;
        }

        data.Slots[slotIndex].Perk = perk;
        return PerkError.None;
    }

    internal static void Redraw(List<PerkSlot> slots, Random random, IEnumerable<int>? alsoExclude = null)
    {
        var locked = slots.Where(slot => slot.Locked).Select(slot => slot.Perk).Concat(alsoExclude ?? []).ToHashSet();
        var pool = PerkCatalog.Numbers.Where(number => !locked.Contains(number)).ToList();
        foreach (var slot in slots.Where(slot => !slot.Locked))
        {
            if (Take(pool, random) is { } perk)
            {
                slot.Perk = perk;
            }
        }
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
