using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace PrestigePerks.Items;

/// <summary>
/// 금고 루트 목록(staticLoot)에 코인을 넣을 가중치 계산 (순수 함수, 단위 테스트).
/// SPT 는 금고마다 아이템 수를 itemcountDistribution 에서 뽑고, 그 수만큼 itemDistribution 에서 가중치로 뽑는다
/// (allowDuplicateItemsInStaticContainers 기본 true: 복원 추출). 맵마다 가중치 합과 개수 분포가 달라서,
/// 같은 가중치를 넣으면 맵마다 확률이 달라진다. 그래서 "금고 하나에 코인이 있을 확률"이 되는 가중치를 맵마다 계산한다.
/// </summary>
public static class SafeCoinDrop
{
    /// <summary>바닐라 금고 (3×3). 은행 금고(SafeBank)는 현금만 나오는 다른 컨테이너라 넣지 않는다.</summary>
    public const string SafeTemplateId = "578f8782245977354405a1e3";

    /// <summary>한 번 뽑을 때 코인이 나올 확률의 상한. 아이템 수 0 인 금고가 있어 확률 1 은 닿을 수 없다.</summary>
    internal const double MaxDrawProbability = 0.99;

    /// <summary>한 번 뽑을 때 코인이 나올 확률이 p 일 때 금고에 코인이 하나 이상 있을 확률: 1 − Σ P(k)(1 − p)^k.</summary>
    internal static double ChanceForDrawProbability(IReadOnlyList<(int Count, double Weight)> counts, double p)
    {
        var total = counts.Sum(entry => entry.Weight);
        if (total <= 0)
        {
            return 0;
        }

        return 1 - counts.Sum(entry => entry.Weight / total * Math.Pow(1 - p, entry.Count));
    }

    /// <summary>금고 확률이 chance 가 되는 한 번 뽑기 확률 (이분 탐색). 닿을 수 없으면 <see cref="MaxDrawProbability"/>.</summary>
    internal static double DrawProbabilityForChance(IReadOnlyList<(int Count, double Weight)> counts, double chance)
    {
        double low = 0, high = MaxDrawProbability;
        if (ChanceForDrawProbability(counts, high) <= chance)
        {
            return high;
        }

        for (var i = 0; i < 60; i++)
        {
            var mid = (low + high) / 2;
            if (ChanceForDrawProbability(counts, mid) < chance)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return (low + high) / 2;
    }

    /// <summary>
    /// 금고 루트 목록에 코인을 넣는다 (staticLoot transformer). 금고가 없거나, 코인이 이미 있거나, 넣을 수 없으면 그대로 둔다.
    /// 넣은 가중치를 돌려준다.
    /// </summary>
    public static float? AddCoin(Dictionary<MongoId, StaticLootDetails>? staticLoot, double chance)
    {
        var coin = new MongoId(PrestigeCoinItem.TemplateId);
        if (chance <= 0
            || staticLoot is null
            || !staticLoot.TryGetValue(new MongoId(SafeTemplateId), out var safe)
            || safe?.ItemDistribution is null
            || safe.ItemCountDistribution is null
            || safe.ItemDistribution.Any(item => item.Tpl == coin))
        {
            return null;
        }

        var counts = safe.ItemCountDistribution
            .Where(entry => entry.Count is >= 0 && entry.RelativeProbability is > 0)
            .Select(entry => (entry.Count!.Value, (double)entry.RelativeProbability!.Value))
            .ToList();
        var totalWeight = safe.ItemDistribution.Sum(item => (double)(item.RelativeProbability ?? 0));
        if (totalWeight <= 0 || counts.All(entry => entry.Item1 == 0))
        {
            return null;
        }

        var p = DrawProbabilityForChance(counts, chance);
        var weight = (float)(p / (1 - p) * totalWeight);
        safe.ItemDistribution = safe.ItemDistribution.Append(new ItemDistribution { Tpl = coin, RelativeProbability = weight }).ToList();
        return weight;
    }
}
