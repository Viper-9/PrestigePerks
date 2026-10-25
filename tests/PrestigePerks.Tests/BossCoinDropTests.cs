using PrestigePerks.Commands;
using PrestigePerks.Items;
using PrestigePerks.Perks;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace PrestigePerks.Tests;

public class BossCoinDropTests
{
    private static readonly MongoId Coin = new(PrestigeCoinItem.TemplateId);
    private static readonly MongoId GpCoin = new(BossCoinDrop.GpCoinTemplateId);
    private static readonly MongoId Keycard = new("5c94bbff86f7747ee735c08f");

    private static BotType Boss(Dictionary<MongoId, double> pockets) =>
        new() { BotInventory = new BotTypeInventory { Items = new ItemPools { Pockets = pockets, SpecialLoot = [] } } };

    /// <summary>
    /// SPT·APBS 화폐 뽑기: 개수를 가중치로 뽑고 화폐 풀에서 가중치로 복원 추출 (화폐 뽑기에는 생성 한도를 넘기지 않는다).
    /// 아이템이 하나면 가중치와 상관없이 그것 (WeightedRandomHelper.GetWeightedValue).
    /// </summary>
    private static List<MongoId> DrawCurrency(Dictionary<double, double> counts, Dictionary<MongoId, double> pool, Random random)
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

        var count = Weighted(counts);
        return Enumerable.Range(0, (int)count).Select(_ => Weighted(pool)).ToList();
    }

    [Fact]
    public void Pocket_coin_takes_the_gp_coin_weight_or_one()
    {
        var withGp = Boss(new() { [GpCoin] = 24719, [Keycard] = 100 });
        var alone = Boss(new() { [Keycard] = 100 });

        Assert.True(BossCoinDrop.AddCoinToPockets(withGp));
        Assert.False(BossCoinDrop.AddCoinToPockets(alone));

        Assert.Equal(24719, withGp.BotInventory!.Items!.Pockets[Coin]);
        Assert.Equal(1, alone.BotInventory!.Items!.Pockets[Coin]);
        Assert.Empty(alone.BotInventory.Items.SpecialLoot);
    }

    [Fact]
    public void Coin_is_added_once_and_missing_data_is_left_alone()
    {
        var bot = Boss([]);
        BossCoinDrop.AddCoinToPockets(bot);

        Assert.Null(BossCoinDrop.AddCoinToPockets(bot));
        Assert.Single(bot.BotInventory!.Items!.Pockets);
        Assert.Null(BossCoinDrop.AddCoinToPockets(null));
        Assert.Null(BossCoinDrop.AddCoinToPockets(new BotType()));
    }

    /// <summary>4.1.2 보스는 원래 화폐가 없어 화폐 풀에는 코인(과 GP 코인)만 있다. 개수 표 {0:20, 1:5, 2:1} (APBS 모든 Tier 와 같음).</summary>
    [Theory]
    [InlineData(false, 6.0 / 26, 0)]
    [InlineData(true, 3.25 / 26, 3.25 / 26)]
    public void Currency_draw_gives_the_coin_to_about_23_or_12_percent(bool withGp, double coinChance, double gpChance)
    {
        var bot = Boss(withGp ? new() { [GpCoin] = 500 } : []);
        BossCoinDrop.AddCoinToPockets(bot);
        var pool = new Dictionary<MongoId, double>(bot.BotInventory!.Items!.Pockets);
        var counts = new Dictionary<double, double> { [0] = 20, [1] = 5, [2] = 1 };
        var random = new Random(4);
        int coin = 0, gp = 0;
        const int bosses = 40000;

        for (var i = 0; i < bosses; i++)
        {
            var drawn = DrawCurrency(counts, pool, random);
            coin += drawn.Contains(Coin) ? 1 : 0;
            gp += drawn.Contains(GpCoin) ? 1 : 0;
        }

        Assert.InRange(coin / (double)bosses, coinChance - 0.01, coinChance + 0.01);
        Assert.InRange(gp / (double)bosses, gpChance - 0.01, gpChance + 0.01);
    }

    [Fact]
    public void Stack_sizes_go_to_the_role_entry_or_default()
    {
        var sizes = new Dictionary<string, Dictionary<string, Dictionary<string, double>>>
        {
            ["assault"] = [],
            ["default"] = new() { ["5449016a4bdc2d6f028b456f"] = new() { ["5000"] = 70 } },
        };

        BossCoinDrop.AddStackSizes(sizes, "bossbully", 20, 25);
        BossCoinDrop.AddStackSizes(sizes, "assault", 2, 3);

        Assert.Equal(["20", "21", "22", "23", "24", "25"], sizes["default"][PrestigeCoinItem.TemplateId].Keys);
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
    public void Shipped_config_stacks_with_and_without_the_gp_coin_mod()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "config", PerkConfigLoader.FileName);
        var config = PerkConfigLoader.Validated(PerkConfigLoader.ParseModConfig(File.ReadAllText(path)), warning => Assert.Fail(warning)).BossCoins;

        Assert.Equal(new BossCoinConfig(), config);
        Assert.True(config.Enabled);
        Assert.Equal((20, 35), config.StackRange(sharedWithGp: false));
        Assert.Equal((40, 60), config.StackRange(sharedWithGp: true));
    }

    [Theory]
    [InlineData("""{ "bossCoins": { "minStack": 0 } }""")]
    [InlineData("""{ "bossCoins": { "minStack": 40, "maxStack": 30 } }""")]
    [InlineData("""{ "bossCoins": { "maxStack": 101 } }""")]
    [InlineData("""{ "bossCoins": { "sharedMinStack": 70, "sharedMaxStack": 60 } }""")]
    [InlineData("""{ "bossCoins": { "sharedMaxStack": 120 } }""")]
    public void Invalid_boss_coins_fall_back_to_defaults(string json)
    {
        var warnings = new List<string>();

        var config = PerkConfigLoader.Validated(PerkConfigLoader.ParseModConfig(json), warnings.Add);

        Assert.Equal(new BossCoinConfig(), config.BossCoins);
        Assert.Contains(warnings, warning => warning.Contains("bossCoins"));
    }

    [Fact]
    public void Tally_counts_coins_gp_coins_and_double_stacks()
    {
        var tally = new BossCoinTally();
        Item Stack(MongoId tpl, int count) => new() { Id = new MongoId(), Template = tpl, Upd = new Upd { StackObjectsCount = count } };

        tally.Add([Stack(Coin, 40), Stack(Keycard, 1)]);
        tally.Add([Stack(Coin, 30), Stack(Coin, 25), Stack(GpCoin, 2)]);
        tally.Add([]);

        Assert.Equal((3, 2, 1, 1), (tally.Bots, tally.WithCoin, tally.MultiStack, tally.WithGp));
        Assert.Equal((40, 55), (tally.MinCoins, tally.MaxCoins));
        Assert.Equal("bosskojaniy: coin 2/3 bots, 40-55 coins (avg 47.5), gp 1/3, 1 bots with 2+ coin stacks", tally.Format("bosskojaniy"));
        Assert.Equal("followerbully: coin 0/0 bots, gp 0/0", new BossCoinTally().Format("followerbully"));
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
