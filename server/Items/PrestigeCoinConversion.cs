using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;

namespace PrestigePerks.Items;

/// <summary>프레스티지 때 받는 코인. 이월 = 가진 코인(1:1, 상한 없음), 환산 = 전달 창 아이템 가치 (상한까지), 손실 = 상한을 넘은 만큼.</summary>
public readonly record struct PrestigeCoinResult(int CarriedOver, int Converted, int Lost)
{
    public int Total => CarriedOver + Converted;
}

/// <summary>로그용 내역. 이월은 출처별, 환산은 전달 아이템 수·루블 가치·상한.</summary>
public readonly record struct PrestigeCoinDetails(
    int TransferRequests,
    int TransferItems,
    double TransferRoubles,
    int TransferCells,
    int Cap,
    long CarriedPmc,
    long CarriedScav,
    long CarriedMail,
    long CarriedBtr)
{
    public override string ToString() =>
        $"carried over: PMC {CarriedPmc}, scav {CarriedScav}, unclaimed mail {CarriedMail}, BTR {CarriedBtr}; " +
        $"transfer: {TransferRequests} requested, {TransferItems} items with children, {TransferRoubles:N0} roubles, cap {Cap} ({TransferCells} cells)";
}

/// <summary>
/// 이월·환산 계산 (dev-docs 3_프레스티지_코인.md 2·3절, 6절 "처리 지점"). 이전 프로필은 읽기만 한다.
/// </summary>
public static class PrestigeCoinConversion
{
    /// <summary>비트코인 (Physical Bitcoin). 환산 상한 = 전달 창 칸 수 × 이 아이템 핸드북 가격 ÷ 코인 단가.</summary>
    public const string BitcoinTemplateId = "59faff1d86f7746c51718c9c";

    /// <param name="oldProfile">프레스티지 전 프로필 (CreateProfile 이 만든 복제본)</param>
    /// <param name="transferItemIds">전달 창에 넣은 아이템 ID (하위 아이템은 PMC 인벤토리에서 찾는다)</param>
    /// <param name="transferCells">이번 단계 전달 창 칸 수 (xCellCount × yCellCount)</param>
    /// <param name="price">핸드북 가격 (루블, 없으면 0)</param>
    /// <param name="now">현재 시각 (유닉스 초). 만료된 메일을 뺀다</param>
    public static PrestigeCoinResult Calculate(SptProfile oldProfile, IEnumerable<MongoId> transferItemIds, int transferCells, Func<MongoId, double> price, long now) =>
        Calculate(oldProfile, transferItemIds, transferCells, price, now, out _);

    /// <inheritdoc cref="Calculate(SptProfile, IEnumerable{MongoId}, int, Func{MongoId, double}, long)"/>
    public static PrestigeCoinResult Calculate(SptProfile oldProfile, IEnumerable<MongoId> transferItemIds, int transferCells, Func<MongoId, double> price, long now, out PrestigeCoinDetails details)
    {
        var pmcItems = oldProfile.CharacterData?.PmcData?.Inventory?.Items ?? [];
        var requested = transferItemIds.ToList();
        var transferred = TransferredItems(pmcItems, requested);

        var value = transferred.Sum(item => price(item.Template) * StackCount(item));
        var worth = ToCoins(value);
        var cap = Cap(transferCells, price);
        var converted = Math.Min(worth, (long)cap);

        var transferredIds = transferred.Select(item => item.Id).ToHashSet();
        var pmc = CountCoins(pmcItems.Where(item => !transferredIds.Contains(item.Id)));
        var scav = CountCoins(oldProfile.CharacterData?.ScavData?.Inventory?.Items ?? []);
        var mail = CountCoins(UnclaimedMailItems(oldProfile, now));
        var btr = CountCoins((oldProfile.BtrDeliveryList ?? []).SelectMany(delivery => delivery.Items ?? []));
        var carried = pmc + scav + mail + btr;

        details = new PrestigeCoinDetails(requested.Count, transferred.Count, value, Math.Max(transferCells, 0), cap, pmc, scav, mail, btr);
        return new PrestigeCoinResult(Clamp(carried), Clamp(converted), Clamp(worth - converted));
    }

    /// <summary>환산 상한 = 전달 창 칸 수 × 비트코인 핸드북 가격 ÷ 코인 단가, 내림.</summary>
    public static int Cap(int transferCells, Func<MongoId, double> price) => Clamp(ToCoins(Math.Max(transferCells, 0) * price(BitcoinTemplateId)));

    /// <summary>전달 아이템과 그 하위 아이템 (부착물, 컨테이너 내용물). 같은 아이템은 한 번만 센다.</summary>
    internal static List<Item> TransferredItems(List<Item> items, IEnumerable<MongoId> rootIds)
    {
        var byId = new Dictionary<MongoId, Item>();
        var children = new Dictionary<string, List<Item>>();
        foreach (var item in items)
        {
            byId.TryAdd(item.Id, item);
            if (item.ParentId is { } parent)
            {
                if (!children.TryGetValue(parent, out var list))
                {
                    children[parent] = list = [];
                }

                list.Add(item);
            }
        }

        var result = new List<Item>();
        var seen = new HashSet<MongoId>();
        var stack = new Stack<Item>(rootIds.Where(byId.ContainsKey).Select(id => byId[id]));
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            if (!seen.Add(item.Id))
            {
                continue;
            }

            result.Add(item);
            foreach (var child in children.GetValueOrDefault(item.Id.ToString()) ?? [])
            {
                stack.Push(child);
            }
        }

        return result;
    }

    /// <summary>받지 않았고(보상 미수령) 만료되지 않은 메일의 첨부 아이템. 받은 아이템은 메일에서 빠지므로 남은 것만 있다.</summary>
    private static IEnumerable<Item> UnclaimedMailItems(SptProfile profile, long now) =>
        (profile.DialogueRecords?.Values ?? Enumerable.Empty<Dialogue>())
        .SelectMany(dialogue => dialogue.Messages ?? [])
        .Where(message => message.RewardCollected != true && message.DateTime + (message.MaxStorageTime ?? 0) >= now)
        .SelectMany(message => message.Items?.Data ?? []);

    private static long CountCoins(IEnumerable<Item> items) =>
        items.Where(item => item.Template == PrestigeCoinItem.TemplateId).Sum(item => (long)StackCount(item));

    private static double StackCount(Item item) => Math.Max(item.Upd?.StackObjectsCount ?? 1, 0);

    private static long ToCoins(double roubles) => roubles > 0 ? (long)Math.Floor(roubles / PrestigeCoinItem.PriceRoubles) : 0;

    private static int Clamp(long value) => (int)Math.Clamp(value, 0, int.MaxValue);
}
