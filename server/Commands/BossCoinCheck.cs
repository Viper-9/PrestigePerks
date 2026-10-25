using System.Text;
using PrestigePerks.Items;
using PrestigePerks.Perks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace PrestigePerks.Commands;

// 개발용 명령은 Dev 빌드에만 넣는다 (PrestigeCommandoCommand 참고)
#if DEV_COMMANDS

/// <summary>
/// 개발용 "prestige coin boss [n]": 보스마다 SPT 봇 생성(BotGenerator.PrepareAndGenerateBot, 레이드에서 쓰는 것과 같은 경로)을
/// n 번 돌려 코인과 GP 코인이 몇 명에게 들어갔는지 센다. 모든 보스를 레이드에서 만나 확인할 수 없어서 데이터로 확인한다.
/// 레이드 밖이라 APBS 대신 SPT 생성기를 타지만, 코인은 아이템 목록에만 넣고 APBS 보스 화폐 표도 같아 확률이 같다.
/// 추종자·카반 저격 경호원은 대조군으로 0 이어야 한다.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class BossCoinCheck(BotGenerator botGenerator, BotTable botTable, PmcConfig pmcConfig, PerkConfigLoader configLoader)
{
    public const int DefaultBots = 30;
    public const int MaxBots = 200;

    /// <summary>코인이 없어야 하는 역할: 이름만 boss 인 카반 저격 경호원, 추종자.</summary>
    public static readonly IReadOnlyList<string> ControlRoles = ["bossboarsniper", "followerbully", "followerkojaniy"];

    public string Describe(MongoId sessionId, int bots)
    {
        var coin = new MongoId(PrestigeCoinItem.TemplateId);
        var gp = new MongoId(BossCoinDrop.GpCoinTemplateId);
        var shared = BossCoinDrop.Roles.Any(role => botTable.Types.GetValueOrDefault(role)?.BotInventory?.Items?.Pockets is { } pockets
                                                     && pockets.ContainsKey(coin) && pockets.ContainsKey(gp));
        var (minStack, maxStack) = configLoader.BossCoins.StackRange(shared);
        var text = new StringBuilder($"boss coins check: {bots} generated bots per role, " +
                                     $"{(shared ? "shared with GP coins (about 12.5% each)" : "about 23% per boss")}, {minStack}-{maxStack} coins");
        foreach (var role in BossCoinDrop.Roles.Concat(ControlRoles))
        {
            text.Append('\n').Append(DescribeRole(sessionId, role, bots));
        }

        return text.ToString();
    }

    private string DescribeRole(MongoId sessionId, string role, int bots)
    {
        if (!botTable.Types.TryGetValue(role, out var type) || type is null)
        {
            return $"{role}: not in bot database";
        }

        var tally = new BossCoinTally();
        try
        {
            for (var i = 0; i < bots; i++)
            {
                var bot = botGenerator.PrepareAndGenerateBot(sessionId, Details(role));
                tally.Add(bot.Inventory?.Items ?? []);
            }
        }
        catch (Exception ex)
        {
            return $"{role}: ERROR after {tally.Bots} bots: {ex.GetType().Name}: {ex.Message}";
        }

        return tally.Format(role);
    }

    /// <summary>BotController.GetBotGenerationDetailsForWave 와 같은 값 (PMC 가 아닌 봇, 맵 없음).</summary>
    private BotGenerationDetails Details(string role) =>
        new()
        {
            IsPmc = false,
            Side = "Savage",
            Role = role,
            PlayerLevel = 1,
            BotRelativeLevelDeltaMax = pmcConfig.BotRelativeLevelDelta.Max,
            BotRelativeLevelDeltaMin = pmcConfig.BotRelativeLevelDelta.Min,
            BotCountToGenerate = 1,
            BotDifficulty = "normal",
            IsPlayerScav = false,
        };
}

/// <summary>생성한 봇 인벤토리에서 코인과 GP 코인을 센다 (순수, 단위 테스트).</summary>
public sealed class BossCoinTally
{
    private static readonly MongoId Coin = new(PrestigeCoinItem.TemplateId);
    private static readonly MongoId GpCoin = new(BossCoinDrop.GpCoinTemplateId);

    private int coinTotal;

    public int Bots { get; private set; }

    public int WithCoin { get; private set; }

    /// <summary>GP 코인을 가진 봇 수. GP 코인 보스 모드가 있으면 반반 나누는지 본다.</summary>
    public int WithGp { get; private set; }

    /// <summary>코인 스택이 둘 이상 나온 봇 수. 화폐를 두 번 뽑을 때(1/26) 나올 수 있다.</summary>
    public int MultiStack { get; private set; }

    public int? MinCoins { get; private set; }

    public int? MaxCoins { get; private set; }

    public void Add(IEnumerable<Item> items)
    {
        var list = items.ToList();
        Bots++;
        var stacks = list.Where(item => item.Template == Coin).ToList();
        if (stacks.Count > 0)
        {
            var coins = stacks.Sum(item => (int)(item.Upd?.StackObjectsCount ?? 1));
            WithCoin++;
            coinTotal += coins;
            MinCoins = Math.Min(MinCoins ?? coins, coins);
            MaxCoins = Math.Max(MaxCoins ?? coins, coins);
            if (stacks.Count > 1)
            {
                MultiStack++;
            }
        }

        if (list.Any(item => item.Template == GpCoin))
        {
            WithGp++;
        }
    }

    /// <summary>bosskojaniy: coin 26/200 bots, 40-60 coins (avg 49.8), gp 24/200</summary>
    public string Format(string role)
    {
        var text = new StringBuilder($"{role}: coin {WithCoin}/{Bots} bots");
        if (WithCoin > 0)
        {
            text.Append($", {MinCoins}-{MaxCoins} coins (avg {(double)coinTotal / WithCoin:0.0})");
        }

        text.Append($", gp {WithGp}/{Bots}");
        if (MultiStack > 0)
        {
            text.Append($", {MultiStack} bots with 2+ coin stacks");
        }

        return text.ToString();
    }
}
#endif
