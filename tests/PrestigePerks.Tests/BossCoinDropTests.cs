using PrestigePerks.Commands;
using PrestigePerks.Items;
using PrestigePerks.Perks;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Bots;

namespace PrestigePerks.Tests;

public class BossCoinDropTests
{
    private static readonly MongoId Coin = new(PrestigeCoinItem.TemplateId);
    private static readonly MongoId GpCoin = new(BossCoinDrop.GpCoinTemplateId);
    private static readonly MongoId Keycard = new("5c94bbff86f7747ee735c08f");

    [Theory]
    [InlineData("bossBully", true)]
    [InlineData("bosskojaniy", true)]
    [InlineData("bosspartisan", true)]
    [InlineData("bossboar", true)]
    [InlineData("ISBBossCommander", true)]
    [InlineData("isbbossfirefly", true)]
    [InlineData("bossWedge", true)]
    [InlineData("bossboarsniper", false)]
    [InlineData("followerbully", false)]
    [InlineData("ISBFollowerZofia", false)]
    [InlineData("ISBTeamLeader", false)]
    [InlineData("blackDivLead", false)]
    [InlineData("assault", false)]
    [InlineData(null, false)]
    public void Only_the_bosses_themselves_get_coins(string? role, bool expected)
    {
        Assert.Equal(expected, BossCoinDrop.IsTarget(new BossCoinConfig().Roles, role));
    }

    [Fact]
    public void Default_roles_are_the_13_spt_bosses_plus_isb_and_black_division_bosses()
    {
        Assert.Equal(16, BossCoinDrop.DefaultRoles.Count);
        Assert.All(BossCoinDrop.DefaultRoles, role => Assert.Equal(role.ToLowerInvariant(), role));
        Assert.Equal(BossCoinDrop.DefaultRoles, new BossCoinConfig().Roles);
    }

    [Fact]
    public void Generated_role_or_requested_role_can_match()
    {
        var roles = new BossCoinConfig().Roles;
        var knight = new BotBase { Info = new Info { Settings = new BotInfoSettings { Role = "bossKnight" } } };

        Assert.True(BossCoinDrop.IsTarget(roles, new BotGenerationDetails { Role = "bossGluhar" }, new BotBase()));
        Assert.True(BossCoinDrop.IsTarget(roles, new BotGenerationDetails(), knight));
        Assert.False(BossCoinDrop.IsTarget(roles, new BotGenerationDetails { Role = "pmcBEAR" }, new BotBase()));
        Assert.False(BossCoinDrop.IsTarget(roles, null, null));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(0.7, 0.7)]
    public void Roll_gives_coins_at_the_configured_chance(double chance, double expected)
    {
        var config = new BossCoinConfig { Chance = chance, MinStack = 25, MaxStack = 35 };
        var random = new Random(7);
        var counts = new List<int>();
        const int bosses = 20000;

        for (var i = 0; i < bosses; i++)
        {
            if (BossCoinDrop.Roll(config, random) is { } count)
            {
                counts.Add(count);
            }
        }

        Assert.InRange(counts.Count / (double)bosses, expected - 0.01, expected + 0.01);
        Assert.All(counts, count => Assert.InRange(count, 25, 35));
        if (counts.Count > 0)
        {
            Assert.Equal((25, 35), (counts.Min(), counts.Max()));
        }
    }

    [Fact]
    public void Coin_stack_is_one_new_item()
    {
        var stack = BossCoinDrop.CreateStack(30);
        var other = BossCoinDrop.CreateStack(30);

        Assert.Equal(Coin, stack.Template);
        Assert.Equal(30, stack.Upd!.StackObjectsCount);
        Assert.NotEqual(stack.Id, other.Id);
    }

    [Fact]
    public void Coin_goes_to_pockets_then_vest_then_backpack()
    {
        var tried = new List<EquipmentSlots>();

        var slot = BossCoinDrop.Place(slot =>
        {
            tried.Add(slot);
            return slot == EquipmentSlots.TacticalVest ? ItemAddedResult.SUCCESS : ItemAddedResult.NO_SPACE;
        });

        Assert.Equal(EquipmentSlots.TacticalVest, slot);
        Assert.Equal([EquipmentSlots.Pockets, EquipmentSlots.TacticalVest], tried);
    }

    [Fact]
    public void Coin_is_skipped_when_nothing_has_room()
    {
        var tried = new List<EquipmentSlots>();

        Assert.Null(BossCoinDrop.Place(slot =>
        {
            tried.Add(slot);
            return ItemAddedResult.NO_SPACE;
        }));
        Assert.Equal([EquipmentSlots.Pockets, EquipmentSlots.TacticalVest, EquipmentSlots.Backpack], tried);
    }

    [Fact]
    public void Shipped_config_is_70_percent_25_to_35_coins_for_the_default_bosses()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "config", PerkConfigLoader.FileName);
        var config = PerkConfigLoader.Validated(PerkConfigLoader.ParseModConfig(File.ReadAllText(path)), warning => Assert.Fail(warning)).BossCoins;

        Assert.True(config.Enabled);
        Assert.Equal(0.7, config.Chance);
        Assert.Equal((25, 35), (config.MinStack, config.MaxStack));
        Assert.Equal(BossCoinDrop.DefaultRoles, config.Roles);
    }

    [Fact]
    public void Config_roles_are_matched_without_case_and_old_shared_stacks_are_ignored()
    {
        var warnings = new List<string>();
        var config = PerkConfigLoader.Validated(PerkConfigLoader.ParseModConfig(
            """{ "bossCoins": { "chance": 0.5, "roles": ["BossGluhar", "MyModBoss"], "sharedMinStack": 40, "sharedMaxStack": 60 } }"""), warnings.Add).BossCoins;

        Assert.Empty(warnings);
        Assert.Equal(0.5, config.Chance);
        Assert.True(BossCoinDrop.IsTarget(config.Roles, "bossgluhar"));
        Assert.True(BossCoinDrop.IsTarget(config.Roles, "mymodboss"));
        Assert.False(BossCoinDrop.IsTarget(config.Roles, "bosskilla"));
    }

    [Theory]
    [InlineData("""{ "bossCoins": { "chance": -1 } }""")]
    [InlineData("""{ "bossCoins": { "chance": 1.5 } }""")]
    [InlineData("""{ "bossCoins": { "minStack": 0 } }""")]
    [InlineData("""{ "bossCoins": { "minStack": 40, "maxStack": 30 } }""")]
    [InlineData("""{ "bossCoins": { "maxStack": 101 } }""")]
    [InlineData("""{ "bossCoins": { "roles": null } }""")]
    public void Invalid_boss_coins_fall_back_to_defaults(string json)
    {
        var warnings = new List<string>();

        var config = PerkConfigLoader.Validated(PerkConfigLoader.ParseModConfig(json), warnings.Add).BossCoins;

        Assert.Equal((0.7, 25, 35), (config.Chance, config.MinStack, config.MaxStack));
        Assert.Equal(BossCoinDrop.DefaultRoles, config.Roles);
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
