using PrestigePerks.Items;
using PrestigePerks.Perks;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace PrestigePerks.Tests;

public class SafeCoinDropTests
{
    private static readonly MongoId Safe = new(SafeCoinDrop.SafeTemplateId);
    private static readonly MongoId Coin = new(PrestigeCoinItem.TemplateId);
    private static readonly MongoId Roubles = new("5449016a4bdc2d6f028b456f");

    /// <summary>4.1.2 Customs 금고와 비슷한 분포: 아이템 0~5개, 가중치 합 228,465.</summary>
    private static Dictionary<MongoId, StaticLootDetails> CustomsLike() =>
        new()
        {
            [Safe] = new StaticLootDetails
            {
                ItemCountDistribution =
                [
                    new() { Count = 0, RelativeProbability = 104 },
                    new() { Count = 1, RelativeProbability = 15398 },
                    new() { Count = 2, RelativeProbability = 15853 },
                    new() { Count = 3, RelativeProbability = 16171 },
                    new() { Count = 4, RelativeProbability = 16642 },
                    new() { Count = 5, RelativeProbability = 13256 },
                ],
                ItemDistribution = [new() { Tpl = Roubles, RelativeProbability = 228465 }],
            },
        };

    private static List<(int, double)> Counts(StaticLootDetails safe) =>
        safe.ItemCountDistribution.Select(entry => (entry.Count!.Value, (double)entry.RelativeProbability!.Value)).ToList();

    [Fact]
    public void One_draw_chance_equals_draw_probability()
    {
        Assert.Equal(0.2, SafeCoinDrop.ChanceForDrawProbability([(1, 1)], 0.2), 9);
        Assert.Equal(1 - 0.8 * 0.8, SafeCoinDrop.ChanceForDrawProbability([(2, 1)], 0.2), 9);
        Assert.Equal(0, SafeCoinDrop.ChanceForDrawProbability([(0, 1)], 0.5), 9);
    }

    [Theory]
    [InlineData(0.05)]
    [InlineData(0.2)]
    [InlineData(0.6)]
    public void Draw_probability_reaches_requested_safe_chance(double chance)
    {
        var counts = Counts(CustomsLike()[Safe]);

        var p = SafeCoinDrop.DrawProbabilityForChance(counts, chance);

        Assert.Equal(chance, SafeCoinDrop.ChanceForDrawProbability(counts, p), 6);
    }

    [Fact]
    public void Unreachable_chance_stops_at_the_cap()
    {
        // 아이템 0개 금고가 절반이면 금고 확률은 0.5 를 넘을 수 없다
        Assert.Equal(SafeCoinDrop.MaxDrawProbability, SafeCoinDrop.DrawProbabilityForChance([(0, 1), (1, 1)], 0.9));
    }

    [Fact]
    public void Added_weight_gives_the_safe_chance()
    {
        var loot = CustomsLike();

        var weight = SafeCoinDrop.AddCoin(loot, 0.2);

        Assert.NotNull(weight);
        var safe = loot[Safe];
        Assert.Equal(weight, safe.ItemDistribution.Single(item => item.Tpl == Coin).RelativeProbability);
        var p = weight!.Value / (228465 + weight.Value);
        Assert.Equal(0.2, SafeCoinDrop.ChanceForDrawProbability(Counts(safe), p), 5);
    }

    [Fact]
    public void Coin_is_added_once()
    {
        var loot = CustomsLike();

        SafeCoinDrop.AddCoin(loot, 0.2);
        var second = SafeCoinDrop.AddCoin(loot, 0.2);

        Assert.Null(second);
        Assert.Single(loot[Safe].ItemDistribution, item => item.Tpl == Coin);
    }

    [Fact]
    public void Maps_without_safes_and_zero_chance_are_left_alone()
    {
        var other = new Dictionary<MongoId, StaticLootDetails>
        {
            [new MongoId("578f87b7245977356274f2cd")] = CustomsLike()[Safe],
        };
        var loot = CustomsLike();

        Assert.Null(SafeCoinDrop.AddCoin(other, 0.2));
        Assert.Null(SafeCoinDrop.AddCoin(null, 0.2));
        Assert.Null(SafeCoinDrop.AddCoin(loot, 0));
        Assert.DoesNotContain(loot[Safe].ItemDistribution, item => item.Tpl == Coin);
    }

    [Fact]
    public void Shipped_config_drops_five_to_ten_coins_in_one_of_five_safes()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "config", PerkConfigLoader.FileName);
        var config = PerkConfigLoader.Validated(PerkConfigLoader.ParseModConfig(File.ReadAllText(path)), warning => Assert.Fail(warning)).SafeCoins;

        Assert.Equal(new SafeCoinConfig(), config);
        Assert.Equal(0.2, config.Chance);
        Assert.Equal((5, 10), (config.MinStack, config.MaxStack));
    }

    [Theory]
    [InlineData("""{ "safeCoins": { "chance": 1.5 } }""")]
    [InlineData("""{ "safeCoins": { "minStack": 0 } }""")]
    [InlineData("""{ "safeCoins": { "minStack": 20, "maxStack": 10 } }""")]
    [InlineData("""{ "safeCoins": { "maxStack": 101 } }""")]
    public void Invalid_safe_coins_fall_back_to_defaults(string json)
    {
        var warnings = new List<string>();

        var config = PerkConfigLoader.Validated(PerkConfigLoader.ParseModConfig(json), warnings.Add);

        Assert.Equal(new SafeCoinConfig(), config.SafeCoins);
        Assert.Contains(warnings, warning => warning.Contains("safeCoins"));
    }

    [Fact]
    public void Coin_template_stack_range_follows_config()
    {
        var props = PrestigeCoinItem.BuildDetails(bundleAvailable: false, new SafeCoinConfig { MinStack = 3, MaxStack = 7 }).OverrideProperties!;

        Assert.Equal((3, 7), (props.StackMinRandom, props.StackMaxRandom));
    }
}
