using PrestigePerks.Commands;
using PrestigePerks.Items;
using PrestigePerks.Perks;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace PrestigePerks.Tests;

public class BossCoinDropTests
{
    private static readonly MongoId Coin = new(PrestigeCoinItem.TemplateId);
    private static readonly MongoId LabsKeycard = new("5c94bbff86f7747ee735c08f");
    private static readonly MongoId StashKey = new("5d08d21286f774736e7c94c3");

    /// <summary>4.1.2 bosskojaniy 처럼 특수 아이템 2개를 항상 2개 뽑는 보스. special 이 없으면 4.1.2 보스 대부분처럼 0개.</summary>
    private static BotType Boss(params MongoId[] special) =>
        new()
        {
            BotInventory = new BotTypeInventory { Items = new ItemPools { SpecialLoot = special.ToDictionary(tpl => tpl, _ => 1.0) } },
            BotGeneration = new Generation
            {
                Items = new GenerationWeightingItems
                {
                    SpecialItems = new GenerationData
                    {
                        Weights = special.Length == 0 ? new() { [0] = 1, [1] = 0 } : new() { [0] = 0, [special.Length] = 1 },
                        Whitelist = [],
                    },
                },
            },
        };

    private static GenerationData Special(BotType bot) => bot.BotGeneration!.Items!.SpecialItems!;

    /// <summary>
    /// SPT 4.1.2 BotLootGenerator.AddLootFromPool 의 특수 풀 뽑기: 개수를 가중치로 뽑고, 풀에서 가중치로 뽑되
    /// 생성 한도(itemSpawnLimits)에 닿은 아이템은 풀에서 빼고 다시 뽑는다. 아이템이 하나면 가중치와 상관없이 그것.
    /// </summary>
    private static List<MongoId> Draw(GenerationData special, IReadOnlyDictionary<MongoId, double> limits, Random random)
    {
        T Weighted<T>(Dictionary<T, double> values) where T : notnull
        {
            if (values.Count == 1)
            {
                return values.Keys.First();
            }

            var roll = random.NextDouble() * values.Values.Sum();
            foreach (var (key, weight) in values)
            {
                roll -= weight;
                if (roll < 0)
                {
                    return key;
                }
            }

            return values.Keys.Last();
        }

        var pool = new Dictionary<MongoId, double>(special.Whitelist);
        var count = Weighted(special.Weights);
        var spawned = new Dictionary<MongoId, int>();
        var drawn = new List<MongoId>();
        for (var i = 0; i < count && pool.Count > 0; i++)
        {
            var tpl = Weighted(pool);
            if (limits.TryGetValue(tpl, out var limit) && spawned.GetValueOrDefault(tpl) + 1 > limit)
            {
                pool.Remove(tpl);
                i--;
                continue;
            }

            spawned[tpl] = spawned.GetValueOrDefault(tpl) + 1;
            drawn.Add(tpl);
        }

        return drawn;
    }

    [Fact]
    public void Full_chance_adds_one_draw_to_every_count()
    {
        var shifted = BossCoinDrop.ShiftCounts(new() { [0] = 1, [1] = 0 }, 1);

        Assert.Equal(new Dictionary<double, double> { [1] = 1 }, shifted);
    }

    [Fact]
    public void Partial_chance_splits_each_count()
    {
        var shifted = BossCoinDrop.ShiftCounts(new() { [2] = 1 }, 0.4);

        Assert.Equal(0.4, shifted[3], 9);
        Assert.Equal(0.6, shifted[2], 9);
    }

    [Fact]
    public void Coin_goes_to_the_whitelist_of_a_boss_without_special_items()
    {
        var bot = Boss();

        Assert.True(BossCoinDrop.AddCoin(bot, 1));

        Assert.Equal([Coin], Special(bot).Whitelist.Keys);
        Assert.Equal(new Dictionary<double, double> { [1] = 1 }, Special(bot).Weights);
        // 인벤토리 SpecialLoot 에는 넣지 않는다 (합친 풀을 거쳐 화폐 뽑기에 섞이므로)
        Assert.Empty(bot.BotInventory!.Items!.SpecialLoot);
    }

    [Fact]
    public void Existing_special_items_move_to_the_whitelist_with_the_coin()
    {
        var bot = Boss(LabsKeycard, StashKey);

        Assert.True(BossCoinDrop.AddCoin(bot, 1));

        Assert.Equal(1, Special(bot).Whitelist[LabsKeycard]);
        Assert.Equal(1, Special(bot).Whitelist[StashKey]);
        Assert.Equal(BossCoinDrop.CoinWeight, Special(bot).Whitelist[Coin]);
        Assert.Equal(new Dictionary<double, double> { [3] = 1 }, Special(bot).Weights);
        Assert.DoesNotContain(Coin, bot.BotInventory!.Items!.SpecialLoot.Keys);
    }

    [Fact]
    public void Coin_is_added_once_and_zero_chance_or_missing_data_is_left_alone()
    {
        var bot = Boss();
        BossCoinDrop.AddCoin(bot, 1);

        Assert.False(BossCoinDrop.AddCoin(bot, 1));
        Assert.Equal(new Dictionary<double, double> { [1] = 1 }, Special(bot).Weights);

        var untouched = Boss();
        Assert.False(BossCoinDrop.AddCoin(untouched, 0));
        Assert.Empty(Special(untouched).Whitelist);
        Assert.False(BossCoinDrop.AddCoin(null, 1));
        Assert.False(BossCoinDrop.AddCoin(new BotType(), 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Every_generated_boss_gets_the_coin_and_keeps_its_special_items(int specialCount)
    {
        var special = new[] { LabsKeycard, StashKey }.Take(specialCount).ToArray();
        var bot = Boss(special);
        BossCoinDrop.AddCoin(bot, 1);
        var limits = special.ToDictionary(tpl => tpl, _ => 1.0);
        limits[Coin] = 1;
        var random = new Random(1);

        for (var i = 0; i < 2000; i++)
        {
            var drawn = Draw(Special(bot), limits, random);

            Assert.Single(drawn, tpl => tpl == Coin);
            Assert.All(special, tpl => Assert.Contains(tpl, drawn));
        }
    }

    [Fact]
    public void Partial_chance_gives_the_coin_to_that_share_of_bosses_and_keys_stay()
    {
        var bot = Boss(LabsKeycard, StashKey);
        BossCoinDrop.AddCoin(bot, 0.3);
        var limits = new Dictionary<MongoId, double> { [LabsKeycard] = 1, [StashKey] = 1, [Coin] = 1 };
        var random = new Random(2);
        var withCoin = 0;

        for (var i = 0; i < 10000; i++)
        {
            var drawn = Draw(Special(bot), limits, random);
            withCoin += drawn.Contains(Coin) ? 1 : 0;
            Assert.Contains(LabsKeycard, drawn);
            Assert.Contains(StashKey, drawn);
        }

        Assert.InRange(withCoin / 10000.0, 0.28, 0.32);
    }

    [Fact]
    public void Spawn_limit_is_one_coin_per_boss()
    {
        var limits = new Dictionary<string, Dictionary<MongoId, double>> { ["bosskojaniy"] = new() { [StashKey] = 1 } };

        BossCoinDrop.AddSpawnLimit(limits, "bosskojaniy");
        BossCoinDrop.AddSpawnLimit(limits, "bossbully");

        Assert.Equal(1, limits["bosskojaniy"][Coin]);
        Assert.Equal(1, limits["bosskojaniy"][StashKey]);
        Assert.Equal(1, limits["bossbully"][Coin]);
    }

    [Fact]
    public void Stack_sizes_go_to_the_role_entry_or_default()
    {
        var sizes = new Dictionary<string, Dictionary<string, Dictionary<string, double>>>
        {
            ["assault"] = [],
            ["default"] = new() { ["5449016a4bdc2d6f028b456f"] = new() { ["5000"] = 70 } },
        };

        BossCoinDrop.AddStackSizes(sizes, "bossbully", 5, 10);
        BossCoinDrop.AddStackSizes(sizes, "assault", 2, 3);

        Assert.Equal(["5", "6", "7", "8", "9", "10"], sizes["default"][PrestigeCoinItem.TemplateId].Keys);
        Assert.All(sizes["default"][PrestigeCoinItem.TemplateId].Values, weight => Assert.Equal(1, weight));
        Assert.Equal(70, sizes["default"]["5449016a4bdc2d6f028b456f"]["5000"]);
        Assert.Equal(["2", "3"], sizes["assault"][PrestigeCoinItem.TemplateId].Keys);
        Assert.DoesNotContain("bossbully", sizes.Keys);
    }

    [Theory]
    [InlineData("bossBully", true)]
    [InlineData("bosskojaniy", true)]
    [InlineData("bosspartisan", true)]
    [InlineData("bossboar", true)]
    [InlineData("bossboarsniper", false)]
    [InlineData("followerbully", false)]
    [InlineData("followerbigpipe", false)]
    [InlineData("assault", false)]
    public void Only_the_bosses_themselves_get_coins(string role, bool expected)
    {
        Assert.Equal(expected, BossCoinDrop.IsBoss(role));
    }

    [Fact]
    public void Shipped_config_gives_every_boss_five_to_ten_coins()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "config", PerkConfigLoader.FileName);
        var config = PerkConfigLoader.Validated(PerkConfigLoader.ParseModConfig(File.ReadAllText(path)), warning => Assert.Fail(warning)).BossCoins;

        Assert.Equal(new BossCoinConfig(), config);
        Assert.Equal(1, config.Chance);
        Assert.Equal((5, 10), (config.MinStack, config.MaxStack));
    }

    [Theory]
    [InlineData("""{ "bossCoins": { "chance": -0.1 } }""")]
    [InlineData("""{ "bossCoins": { "minStack": 0 } }""")]
    [InlineData("""{ "bossCoins": { "minStack": 20, "maxStack": 10 } }""")]
    [InlineData("""{ "bossCoins": { "maxStack": 101 } }""")]
    public void Invalid_boss_coins_fall_back_to_defaults(string json)
    {
        var warnings = new List<string>();

        var config = PerkConfigLoader.Validated(PerkConfigLoader.ParseModConfig(json), warnings.Add);

        Assert.Equal(new BossCoinConfig(), config.BossCoins);
        Assert.Contains(warnings, warning => warning.Contains("bossCoins"));
    }

    [Fact]
    public void Tally_counts_coins_stacks_and_kept_special_items()
    {
        var tally = new BossCoinTally([LabsKeycard]);
        Item Stack(MongoId tpl, int count) => new() { Id = new MongoId(), Template = tpl, Upd = new Upd { StackObjectsCount = count } };

        tally.Add([Stack(Coin, 6), Stack(LabsKeycard, 1)]);
        tally.Add([Stack(Coin, 5), Stack(Coin, 4)]);
        tally.Add([]);

        Assert.Equal((3, 2, 1), (tally.Bots, tally.WithCoin, tally.MultiStack));
        Assert.Equal((6, 9), (tally.MinCoins, tally.MaxCoins));
        Assert.Equal(1, tally.SpecialFound[LabsKeycard]);
        Assert.Equal("bosskojaniy: coin 2/3 bots, 6-9 coins (avg 7.5), 1 bots with 2+ coin stacks | special kept: labs 1/3",
            tally.Format("bosskojaniy", new Dictionary<MongoId, string> { [LabsKeycard] = "labs" }));
        Assert.Equal("followerbully: coin 0/0 bots", new BossCoinTally([]).Format("followerbully"));
    }

    [Theory]
    [InlineData("prestige coin boss", BossCoinCheck.DefaultBots)]
    [InlineData("prestige coin boss 5", 5)]
    [InlineData("PRESTIGE COIN BOSS 200", 200)]
    public void Parses_boss_check(string text, int expected)
    {
        Assert.True(PrestigeCommandoCommand.IsBossCheck(text));
        Assert.Equal(expected, PrestigeCommandoCommand.ParseBossCheckBots(text));
    }

    [Theory]
    [InlineData("prestige coin boss 0")]
    [InlineData("prestige coin boss 201")]
    [InlineData("prestige coin boss abc")]
    public void Rejects_invalid_boss_check_counts(string text)
    {
        Assert.True(PrestigeCommandoCommand.IsBossCheck(text));
        Assert.Null(PrestigeCommandoCommand.ParseBossCheckBots(text));
    }

    [Fact]
    public void Boss_check_is_not_a_coin_amount()
    {
        Assert.False(PrestigeCommandoCommand.IsBossCheck("prestige coin 10"));
        Assert.Null(PrestigeCommandoCommand.ParseCoinAmount("prestige coin boss"));
    }
}
