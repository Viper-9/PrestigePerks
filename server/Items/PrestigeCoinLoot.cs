using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Config;

namespace PrestigePerks.Items;

/// <summary>
/// 맵 루트에서 코인이 나오는 곳을 정한다. 화폐 분류라 저절로 섞이던 에어드랍·PMC 봇 주머니·배낭·조끼 루트에서는 뺀다.
/// 둘 다 SPT item.json 의 rewardItemBlacklist 를 쓴다 (airdrop useRewardItemBlacklist, PMCLootGenerator 컨테이너 블랙리스트).
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Preload + 10)]
public class PrestigeCoinLoot(ISptLogger<PrestigeCoinLoot> logger, ItemConfig itemConfig) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        itemConfig.RewardItemBlacklist.Add(new MongoId(PrestigeCoinItem.TemplateId));
        logger.Info("[PrestigePerks] prestige coin kept out of airdrops and PMC loot");
        return Task.CompletedTask;
    }
}
