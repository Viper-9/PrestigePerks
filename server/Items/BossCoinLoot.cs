using PrestigePerks.Logging;
using PrestigePerks.Perks;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace PrestigePerks.Items;

/// <summary>
/// 보스 본인의 주머니 목록에 코인을 넣는다 (<see cref="BossCoinDrop"/>). GP 코인 보스 모드가 있으면 GP 코인과 화폐 뽑기를 반반 나누고 스택을 늘린다.
/// GP 코인 보스 모드(PostLoad + 1)가 넣은 GP 코인 가중치를 읽어야 해서 그 뒤인 PostLoad + 2 에 돈다.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 2)]
public class BossCoinLoot(
    ISptLogger<PerksFileLogger> logger,
    BotTable botTable,
    BotConfig botConfig,
    PerkConfigLoader configLoader) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var bossCoins = configLoader.BossCoins;
        if (!bossCoins.Enabled)
        {
            logger.Info("[PrestigePerks] boss coins off");
            return Task.CompletedTask;
        }

        var bosses = new List<string>();
        var shared = false;
        foreach (var (role, bot) in botTable.Types)
        {
            var key = role.ToLowerInvariant();
            if (BossCoinDrop.IsBoss(key) && BossCoinDrop.AddCoinToPockets(bot) is { } withGp)
            {
                shared |= withGp;
                bosses.Add(key);
            }
        }

        var (minStack, maxStack) = bossCoins.StackRange(shared);
        foreach (var key in bosses)
        {
            BossCoinDrop.AddStackSizes(botConfig.CurrencyStackSize, key, minStack, maxStack);
        }

        var missing = BossCoinDrop.Roles.Except(bosses).ToList();
        logger.Info($"[PrestigePerks] boss coins: currency draw {(shared ? "shared with the GP coin mod (about 12.5% per boss)" : "(about 23% per boss)")}, " +
                    $"{minStack}-{maxStack} coins ({bosses.Count} bosses)" +
                    (missing.Count > 0 ? $"; not added: {string.Join(", ", missing)}" : ""));
        return Task.CompletedTask;
    }
}
