namespace PrestigePerks.Perks;

public enum PerkError
{
    None,
    NoSlots,
    SessionAlreadyOpen,
    NoSession,
    InvalidSlot,
    AllLocked,
    NotEnoughCoins,
}

public readonly record struct RollOutcome(PerkError Error, int Cost)
{
    public bool Success => Error == PerkError.None;
}

/// <summary>
/// 특전 화면 규칙 (dev-docs 2_특전.md 1~2절). 파일·프로필을 모르는 순수 함수라 단위 테스트로 검증한다.
/// </summary>
public static class PerkRules
{
    /// <summary>
    /// 유료 리롤 비용 = 기본가 × (1 + 계수 × 잠근 개수) × 배율^(유료 회차 − 1), 소수점 올림.
    /// </summary>
    /// <param name="paidRound">이번에 할 유료 리롤이 몇 번째인지 (1 부터).</param>
    public static int RerollCost(PerkConfig config, int prestigeLevel, int lockedCount, int paidRound)
    {
        var cost = config.BasePrice(prestigeLevel)
                   * (1 + config.LockCostStep * lockedCount)
                   * Math.Pow(config.PaidCostGrowth, Math.Max(paidRound, 1) - 1);
        // 262.5 같은 값이 부동소수 오차로 262.50000001 이 되어 한 칸 더 올라가지 않게 한다
        return (int)Math.Ceiling(cost - 1e-9);
    }

    /// <summary>다음 리롤 비용. 무료 횟수가 남았으면 0.</summary>
    public static int NextRerollCost(PerkConfig config, int prestigeLevel, PerkSession session) =>
        session.FreeRerollsLeft > 0
            ? 0
            : RerollCost(config, prestigeLevel, session.Slots.Count(slot => slot.Locked), session.PaidRerolls + 1);

    /// <summary>효과를 적용할 특전: 확정 특전의 앞에서부터 단계 수만큼.</summary>
    public static IReadOnlyList<int> ActivePerks(PerkData data, int prestigeLevel) =>
        data.Perks.Where(PerkCatalog.IsKnown).Distinct().Take(Math.Max(prestigeLevel, 0)).ToList();

    /// <summary>보유 슬롯 &lt; 단계이고 화면이 닫혀 있으면 열어야 한다 (새 프레스티지와 소급 유저를 같은 규칙으로).</summary>
    public static bool NeedsSession(PerkData data, int prestigeLevel) =>
        data.Session is null && ActivePerks(data, prestigeLevel).Count < prestigeLevel;

    /// <summary>
    /// 화면을 연다. 이전 특전 + 빈 슬롯(단계까지)을 만들고 빈 슬롯을 자동 추첨한다. 자동 추첨은 무료 횟수에서 빼지 않는다.
    /// 슬롯이 이미 꽉 차 있으면 (재추첨권·개발용) 자동 추첨 없이 무료 리롤부터 시작한다.
    /// </summary>
    public static PerkError OpenSession(PerkData data, int prestigeLevel, string reason, PerkConfig config, Random random)
    {
        if (data.Session is not null)
        {
            return PerkError.SessionAlreadyOpen;
        }

        if (prestigeLevel <= 0)
        {
            return PerkError.NoSlots;
        }

        var slots = ActivePerks(data, prestigeLevel).Select(perk => new PerkSlot { Perk = perk }).ToList();
        while (slots.Count < prestigeLevel)
        {
            slots.Add(new PerkSlot());
        }

        FillEmptySlots(slots, random);
        data.Session = new PerkSession
        {
            Reason = reason,
            Slots = slots,
            FreeRerollsLeft = config.FreeRerolls,
            PaidRerolls = 0,
        };
        return PerkError.None;
    }

    /// <summary>슬롯 잠금. slotIndex 는 0 부터.</summary>
    public static PerkError SetLock(PerkData data, int slotIndex, bool locked)
    {
        if (data.Session is null)
        {
            return PerkError.NoSession;
        }

        if (slotIndex < 0 || slotIndex >= data.Session.Slots.Count || data.Session.Slots[slotIndex].Perk is null)
        {
            return PerkError.InvalidSlot;
        }

        data.Session.Slots[slotIndex].Locked = locked;
        return PerkError.None;
    }

    /// <summary>
    /// 리롤. 잠그지 않은 슬롯을 전부 다시 뽑는다. 무료 횟수가 남았으면 무료, 아니면 잔액에서 비용을 빼고 유료 회차를 올린다.
    /// </summary>
    public static RollOutcome Roll(PerkData data, int prestigeLevel, PerkConfig config, Random random)
    {
        var session = data.Session;
        if (session is null)
        {
            return new RollOutcome(PerkError.NoSession, 0);
        }

        if (session.Slots.All(slot => slot.Locked))
        {
            return new RollOutcome(PerkError.AllLocked, 0);
        }

        var cost = NextRerollCost(config, prestigeLevel, session);
        if (cost > data.CoinBalance)
        {
            return new RollOutcome(PerkError.NotEnoughCoins, cost);
        }

        if (session.FreeRerollsLeft > 0)
        {
            session.FreeRerollsLeft--;
        }
        else
        {
            data.CoinBalance -= cost;
            session.PaidRerolls++;
        }

        Redraw(session.Slots, random);
        return new RollOutcome(PerkError.None, cost);
    }

    /// <summary>확정. 화면 슬롯을 보유 특전으로 저장하고 화면을 닫는다. 메일로 보낼 코인(남은 잔액)을 돌려주고 잔액은 0 이 된다.</summary>
    public static PerkError Confirm(PerkData data, out int coinsToMail)
    {
        coinsToMail = 0;
        if (data.Session is null)
        {
            return PerkError.NoSession;
        }

        data.Perks = data.Session.Slots.Where(slot => slot.Perk is not null).Select(slot => slot.Perk!.Value).ToList();
        data.Session = null;
        coinsToMail = data.CoinBalance;
        data.CoinBalance = 0;
        return PerkError.None;
    }

    /// <summary>빈 슬롯만 뽑는다. 이미 있는 특전(잠금 여부 무관)과 겹치지 않는다.</summary>
    internal static void FillEmptySlots(List<PerkSlot> slots, Random random)
    {
        var taken = slots.Where(slot => slot.Perk is not null).Select(slot => slot.Perk!.Value).ToHashSet();
        var pool = PerkCatalog.Numbers.Where(number => !taken.Contains(number)).ToList();
        foreach (var slot in slots.Where(slot => slot.Perk is null))
        {
            slot.Perk = Take(pool, random);
            slot.Locked = false;
        }
    }

    /// <summary>잠그지 않은 슬롯을 다시 뽑는다. 잠근 특전은 빠지고, 잠그지 않은 이전 특전은 다시 나올 수 있다.</summary>
    internal static void Redraw(List<PerkSlot> slots, Random random)
    {
        var locked = slots.Where(slot => slot.Locked && slot.Perk is not null).Select(slot => slot.Perk!.Value).ToHashSet();
        var pool = PerkCatalog.Numbers.Where(number => !locked.Contains(number)).ToList();
        foreach (var slot in slots.Where(slot => !slot.Locked))
        {
            slot.Perk = Take(pool, random);
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
