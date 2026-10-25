using System.Text;
using PrestigePerks.Items;
using PrestigePerks.Perks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Helpers.Items;
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
/// n 번 돌려 코인이 몇 명에게 몇 개 들어갔는지, 원래 특수 아이템(슈트르만 키 등)이 그대로 나오는지 센다.
/// 모든 보스를 레이드에서 만나 확인할 수 없어서 데이터로 확인한다. 추종자·카반 저격 경호원은 대조군으로 0 이어야 한다.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class BossCoinCheck(BotGenerator botGenerator, BotTable botTable, ItemHelper itemHelper, PmcConfig pmcConfig, PerkConfigLoader configLoader)
{
    public const int DefaultBots = 30;
    public const int MaxBots = 200;

    /// <summary>코인이 없어야 하는 역할: 이름만 boss 인 카반 저격 경호원, 추종자.</summary>
    public static readonly IReadOnlyList<string> ControlRoles = ["bossboarsniper", "followerbully", "followerkojaniy"];

    public string Describe(MongoId sessionId, int bots)
    {
        var config = configLoader.BossCoins;
        var text = new StringBuilder($"boss coins check: {bots} generated bots per role (config {config.Chance:P0}, {config.MinStack}-{config.MaxStack} coins)");
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

        var coin = new MongoId(PrestigeCoinItem.TemplateId);
        // SPT 가 실제로 쓰는 특수 풀: whitelist 가 있으면 그것만, 없으면 SpecialLoot (BotLootCacheService)
        var whitelist = type.BotGeneration?.Items?.SpecialItems?.Whitelist;
        var pool = whitelist is { Count: > 0 } ? whitelist.Keys : type.BotInventory?.Items?.SpecialLoot?.Keys ?? Enumerable.Empty<MongoId>();
        var special = pool
            .Where(tpl => tpl != coin)
            .ToDictionary(tpl => tpl, tpl => itemHelper.GetItem(tpl).Value?.Name ?? tpl.ToString());
        var tally = new BossCoinTally(special.Keys);
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

        return tally.Format(role, special);
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

/// <summary>생성한 봇 인벤토리에서 코인과 원래 특수 아이템을 센다 (순수, 단위 테스트).</summary>
public sealed class BossCoinTally(IEnumerable<MongoId> special)
{
    private static readonly MongoId Coin = new(PrestigeCoinItem.TemplateId);

    private readonly Dictionary<MongoId, int> specialFound = special.ToDictionary(tpl => tpl, _ => 0);
    private int coinTotal;

    public int Bots { get; private set; }

    public int WithCoin { get; private set; }

    /// <summary>코인 스택이 둘 이상 나온 봇 수. 화폐 뽑기에 코인이 섞이면 늘어난다.</summary>
    public int MultiStack { get; private set; }

    public int? MinCoins { get; private set; }

    public int? MaxCoins { get; private set; }

    public IReadOnlyDictionary<MongoId, int> SpecialFound => specialFound;

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

        foreach (var tpl in specialFound.Keys.ToList())
        {
            if (list.Any(item => item.Template == tpl))
            {
                specialFound[tpl]++;
            }
        }
    }

    /// <summary>bosskojaniy: coin 30/30 bots, 5-10 coins (avg 7.4) | special kept: item_keycard_lab 30/30, item_key_x 30/30</summary>
    public string Format(string role, IReadOnlyDictionary<MongoId, string>? names = null)
    {
        var text = new StringBuilder($"{role}: coin {WithCoin}/{Bots} bots");
        if (WithCoin > 0)
        {
            text.Append($", {MinCoins}-{MaxCoins} coins (avg {(double)coinTotal / WithCoin:0.0})");
        }

        if (MultiStack > 0)
        {
            text.Append($", {MultiStack} bots with 2+ coin stacks");
        }

        if (specialFound.Count > 0)
        {
            text.Append(" | special kept: ").Append(string.Join(", ", specialFound.Select(pair =>
                $"{(names?.TryGetValue(pair.Key, out var name) == true ? name : pair.Key.ToString())} {pair.Value}/{Bots}")));
        }

        return text.ToString();
    }
}
#endif
