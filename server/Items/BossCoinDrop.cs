using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace PrestigePerks.Items;

/// <summary>
/// 보스 봇 데이터에 코인을 넣는다 (순수 함수, 단위 테스트). 근거는 SPT 4.1.2 BotLootGenerator·BotLootCacheService 디컴파일.
/// 주머니 루트는 0개를 뽑을 수 있어 100% 가 안 되므로, 루트 중 가장 먼저 채워지는 특수 아이템 칸(specialItems)을 쓴다.
/// 루트보다 무기·예비 탄창이 먼저 들어가므로 빈칸이 없으면 코인은 빠진다: 조끼·배낭 없이 1×2 주머니 5칸뿐인 레쉘라(bossbully)는
/// 약 35~40% 만 받는다 (2026-10-26 확인 11/30). 100% 로 하려면 탄창 칸을 빼앗아야 해서 그대로 둔다.
/// - 코인은 specialItems.whitelist 에 넣는다. 인벤토리 SpecialLoot 에 넣으면 합친 풀을 거쳐 화폐 뽑기(currency)에도 섞인다
/// - whitelist 가 있으면 SPT 는 SpecialLoot 대신 whitelist 만 특수 풀로 쓰므로, 비어 있으면 SpecialLoot 를 먼저 옮긴다
/// - 뽑는 개수 분포를 chance 만큼 1 늘린다. 아이템마다 생성 한도(itemSpawnLimits)가 1 이면 같은 아이템은 다시 뽑히지 않아
///   개수 = 풀 크기일 때 전부 나온다 (슈트르만 키 2개, 사니타르 키카드 1개도 그대로 나온다)
/// - 화폐는 currencyStackSize[역할][아이템] 에서 스택 수를 고르는데, 항목이 없으면 봇 생성이 예외로 끝난다. 그래서 함께 넣는다
/// </summary>
public static class BossCoinDrop
{
    /// <summary>
    /// 보스 본인 역할 (SPT bots/types 파일 이름). 추종자와 카반 저격 경호원(bossboarsniper)은 넣지 않는다.
    /// GP 코인·레가 메달 모드처럼 이름에 boss 가 들어갔는지로 고르지 않는 이유: bossboarsniper 는 이름만 boss 다.
    /// </summary>
    public static readonly IReadOnlyList<string> Roles =
    [
        "bossboar", "bossbully", "bossgluhar", "bosskilla", "bosskillaagro", "bossknight", "bosskojaniy",
        "bosskolontay", "bosspartisan", "bosssanitar", "bosstagilla", "bosstagillaagro", "bosszryachiy",
    ];

    /// <summary>
    /// 특수 풀의 코인 가중치. 아주 작게 둬서 개수가 늘지 않은 봇(chance &lt; 1)은 원래 아이템을 먼저 뽑는다.
    /// 개수가 늘어난 봇은 원래 아이템이 한도로 빠진 뒤 코인 하나만 남아 가중치와 상관없이 나온다.
    /// </summary>
    internal const double CoinWeight = 1e-6;

    public static bool IsBoss(string role) => Roles.Contains(role.ToLowerInvariant());

    /// <summary>
    /// 뽑는 개수 분포를 chance 만큼 1 늘린다: P'(k+1) += chance × P(k), P'(k) += (1 − chance) × P(k). 가중치 0 은 뺀다.
    /// 가중치 합이 0 이면 (4.1.2 레쉘라·글루하르·킬라·파르티잔의 {"0": 0}) SPT 는 항목이 하나뿐이라 그 개수(0)를 고르므로 {0: 1} 로 본다.
    /// </summary>
    internal static Dictionary<double, double> ShiftCounts(Dictionary<double, double> weights, double chance)
    {
        if (weights.Values.Sum() <= 0)
        {
            weights = new Dictionary<double, double> { [weights.Count == 1 ? weights.Keys.First() : 0] = 1 };
        }

        var shifted = new Dictionary<double, double>();
        void Add(double count, double weight)
        {
            if (weight > 0)
            {
                shifted[count] = shifted.GetValueOrDefault(count) + weight;
            }
        }

        foreach (var (count, weight) in weights)
        {
            Add(count + 1, weight * chance);
            Add(count, weight * (1 - chance));
        }

        return shifted;
    }

    /// <summary>보스 봇 데이터의 특수 아이템 칸에 코인을 넣는다. 이미 있거나, chance 가 0 이거나, 특수 아이템 설정이 없으면 그대로 둔다.</summary>
    public static bool AddCoin(BotType? bot, double chance)
    {
        var coin = new MongoId(PrestigeCoinItem.TemplateId);
        if (chance <= 0 || bot?.BotGeneration?.Items?.SpecialItems is not { } special)
        {
            return false;
        }

        var whitelist = special.Whitelist ?? [];
        if (whitelist.ContainsKey(coin))
        {
            return false;
        }

        if (whitelist.Count == 0)
        {
            foreach (var (tpl, weight) in bot.BotInventory?.Items?.SpecialLoot ?? [])
            {
                whitelist.TryAdd(tpl, weight);
            }
        }

        whitelist[coin] = CoinWeight;
        special.Whitelist = whitelist;
        special.Weights = ShiftCounts(special.Weights ?? new Dictionary<double, double> { [0] = 1 }, chance);
        return true;
    }

    /// <summary>bot.json itemSpawnLimits[역할] 에 코인 한도 1 을 넣는다. 한도가 있어야 특수 풀에서 같은 아이템을 다시 뽑지 않는다.</summary>
    public static void AddSpawnLimit(Dictionary<string, Dictionary<MongoId, double>> limits, string role)
    {
        if (!limits.TryGetValue(role, out var roleLimits))
        {
            limits[role] = roleLimits = [];
        }

        roleLimits[new MongoId(PrestigeCoinItem.TemplateId)] = 1;
    }

    /// <summary>
    /// bot.json currencyStackSize 에 코인 스택 수(minStack~maxStack 같은 가중치)를 넣는다.
    /// SPT 는 역할 항목이 있으면 그것을, 없으면 default 를 쓴다 (보스는 4.1.2 기본값에 역할 항목이 없어 default).
    /// </summary>
    public static void AddStackSizes(Dictionary<string, Dictionary<string, Dictionary<string, double>>> sizes, string role, int minStack, int maxStack)
    {
        var key = sizes.ContainsKey(role) ? role : "default";
        if (!sizes.TryGetValue(key, out var currencies))
        {
            sizes[key] = currencies = [];
        }

        currencies[PrestigeCoinItem.TemplateId] = Enumerable.Range(minStack, maxStack - minStack + 1)
            .ToDictionary(stack => stack.ToString(), _ => 1.0);
    }
}
