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
/// 맵 루트에서 코인이 나오는 곳을 정한다. 금고에 넣고 (staticLoot transformer, <see cref="SafeCoinDrop"/>),
/// 보스 본인의 특수 아이템 칸에 넣는다 (<see cref="BossCoinDrop"/>).
/// 화폐 분류라 저절로 섞이던 에어드랍·PMC 봇 주머니·배낭·조끼 루트에서는 뺀다.
/// 둘 다 SPT item.json 의 rewardItemBlacklist 를 쓴다 (airdrop useRewardItemBlacklist, PMCLootGenerator 컨테이너 블랙리스트).
/// 금고 스택 수는 코인 템플릿의 StackMinRandom·StackMaxRandom 이고 (<see cref="PrestigeCoinItem"/>), 보스 스택 수는 bot.json currencyStackSize 다.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Preload + 10)]
public class PrestigeCoinLoot(
    ISptLogger<PerksFileLogger> logger,
    LocationTable locationTable,
    BotTable botTable,
    ItemConfig itemConfig,
    BotConfig botConfig,
    PerkConfigLoader configLoader) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        itemConfig.RewardItemBlacklist.Add(new MongoId(PrestigeCoinItem.TemplateId));
        AddSafeCoins();
        AddBossCoins();
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

    private void AddBossCoins()
    {
        var bossCoins = configLoader.BossCoins;
        if (bossCoins.Chance <= 0)
        {
            logger.Info("[PrestigePerks] boss coins off (chance 0)");
            return;
        }

        var bosses = new List<string>();
        foreach (var (role, bot) in botTable.Types)
        {
            var key = role.ToLowerInvariant();
            if (!BossCoinDrop.IsBoss(key) || !BossCoinDrop.AddCoin(bot, bossCoins.Chance))
            {
                continue;
            }

            BossCoinDrop.AddSpawnLimit(botConfig.ItemSpawnLimits, key);
            BossCoinDrop.AddStackSizes(botConfig.CurrencyStackSize, key, bossCoins.MinStack, bossCoins.MaxStack);
            bosses.Add(key);
        }

        var missing = BossCoinDrop.Roles.Except(bosses).ToList();
        logger.Info($"[PrestigePerks] boss coins: {bossCoins.Chance:P0} per boss, {bossCoins.MinStack}-{bossCoins.MaxStack} coins ({bosses.Count} bosses)" +
                    (missing.Count > 0 ? $"; not added: {string.Join(", ", missing)}" : ""));
    }
}
