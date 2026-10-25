using PrestigePerks.Logging;
using PrestigePerks.Perks;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace PrestigePerks.Items;

/// <summary>
/// 맵 루트에서 코인이 나오는 곳을 정한다. 금고에 넣는다 (staticLoot transformer, <see cref="SafeCoinDrop"/>).
/// 보스는 다른 모드보다 늦게 도는 <see cref="BossCoinLoot"/> 가 맡는다.
/// 화폐 분류라 저절로 섞이던 에어드랍·PMC 봇 주머니·배낭·조끼 루트에서는 뺀다.
/// 둘 다 SPT item.json 의 rewardItemBlacklist 를 쓴다 (airdrop useRewardItemBlacklist, PMCLootGenerator 컨테이너 블랙리스트).
/// 금고 스택 수는 코인 템플릿의 StackMinRandom·StackMaxRandom 이다 (<see cref="PrestigeCoinItem"/>).
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Preload + 10)]
public class PrestigeCoinLoot(
    ISptLogger<PerksFileLogger> logger,
    LocationTable locationTable,
    ItemConfig itemConfig,
    PerkConfigLoader configLoader) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        itemConfig.RewardItemBlacklist.Add(new MongoId(PrestigeCoinItem.TemplateId));
        AddSafeCoins();
        return Task.CompletedTask;
    }

    private void AddSafeCoins()
    {
        var safeCoins = configLoader.SafeCoins;
        if (safeCoins.Chance <= 0)
        {
            logger.Info("[PrestigePerks] safe coins off (chance 0); prestige coin kept out of airdrops and PMC loot");
            return;
        }

        var maps = 0;
        foreach (var location in locationTable.GetDictionary().Values)
        {
            if (location?.StaticLoot is not { } staticLoot)
            {
                continue;
            }

            // 금고가 없는 맵은 그대로 둔다. 루트를 만들 때마다 새로 읽으므로 이미 넣었으면 건너뛴다 (AddCoin)
            staticLoot.AddTransformer(loot =>
            {
                SafeCoinDrop.AddCoin(loot, safeCoins.Chance);
                return loot;
            });
            maps++;
        }

        logger.Info($"[PrestigePerks] safe coins: {safeCoins.Chance:P0} per safe, {safeCoins.MinStack}-{safeCoins.MaxStack} coins ({maps} maps); prestige coin kept out of airdrops and PMC loot");
    }
}
