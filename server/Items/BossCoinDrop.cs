using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace PrestigePerks.Items;

/// <summary>
/// 보스 봇 데이터에 코인을 넣는다 (순수 함수, 단위 테스트). 근거는 SPT 4.1.2 BotLootGenerator·BotLootCacheService 와
/// APBS(acidphantasm-progressivebotsystem) CustomBotLootGenerator·CustomBotLootCacheService 디컴파일.
/// GP 코인 보스 모드처럼 주머니 목록에 넣는다. 화폐라 주머니 풀에서 걸러지고, 합친 풀을 거쳐 화폐 뽑기로만 나온다.
/// - 4.1.2 보스는 원래 화폐가 없어서 화폐 뽑기({0:20, 1:5, 2:1}, 바닐라 파르티잔 {0:40, …})를 한 번이라도 하면 코인이다: 보스 한 명당 약 23%
/// - APBS 는 레이드 중 뽑는 개수 표를 자기 Tier 파일에서 읽지만 아이템 목록은 SPT 봇 데이터를 쓰고, 보스 화폐 표도 같아 확률이 같다
/// - 화폐는 currencyStackSize[역할][아이템] 에서 스택 수를 고르는데, 항목이 없으면 봇 생성이 예외로 끝난다. 그래서 함께 넣는다
/// - 화폐 뽑기는 무기·예비 탄창보다 뒤라, 조끼·배낭 없이 작은 주머니만 있는 르샬라(bossbully)는 빈칸이 없어 더 낮다
/// 2026-10-25 처음에는 특수 아이템 칸으로 100% 를 줬지만, APBS 가 그 설정을 무시해 레이드에서 0 이 됐다 (2026-10-26). 방식을 하나로 맞췄다.
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

    /// <summary>바닐라 GP 코인. GP 코인 보스 모드(acidphantasm-bosseshavegpcoins)가 보스 주머니 목록에 넣는다.</summary>
    public const string GpCoinTemplateId = "5d235b4d86f7742e017bc88a";

    public static bool IsBoss(string role) => Roles.Contains(role.ToLowerInvariant());

    /// <summary>
    /// 보스 주머니 목록에 코인을 넣는다. GP 코인이 있으면 같은 가중치로 넣어 화폐 뽑기를 반반 나누고, 없으면 1.
    /// 넣었으면 GP 코인이 함께 있는지를, 이미 있거나 넣을 수 없으면 null 을 돌려준다.
    /// GP 코인 보스 모드는 PostLoad + 1 에서 넣으므로 이 함수는 그 뒤에 불러야 GP 코인을 본다.
    /// </summary>
    public static bool? AddCoinToPockets(BotType? bot)
    {
        var coin = new MongoId(PrestigeCoinItem.TemplateId);
        if (bot?.BotInventory?.Items?.Pockets is not { } pockets || pockets.ContainsKey(coin))
        {
            return null;
        }

        var shared = pockets.TryGetValue(new MongoId(GpCoinTemplateId), out var gpWeight) && gpWeight > 0;
        pockets[coin] = shared ? gpWeight : 1;
        return shared;
    }

    /// <summary>
    /// bot.json currencyStackSize 에 코인 스택 수(minStack~maxStack 같은 가중치)를 넣는다.
    /// SPT 는 역할 항목이 있으면 그것을, 없으면 default 를 쓴다 (보스는 4.1.2 기본값에 역할 항목이 없어 default). APBS 도 같은 설정을 읽는다.
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
