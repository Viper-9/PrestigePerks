using PrestigePerks.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;

namespace PrestigePerks.Tests;

/// <summary>프레스티지 코인 이월·환산 (server/Items/PrestigeCoinConversion.cs, dev-docs 3_프레스티지_코인.md 2·3절).</summary>
public class PrestigeCoinConversionTests
{
    private const long Now = 1_800_000_000;
    private const string Coin = PrestigeCoinItem.TemplateId;
    private const string Bitcoin = PrestigeCoinConversion.BitcoinTemplateId;
    private const string Weapon = "5447a9cd4bdc2dbd208b4567";
    private const string Scope = "544a3a774bdc2d3a388b4567";
    private const string Ammo = "54527a984bdc2d4e668b4567";
    private const string Unpriced = "aaaaaaaaaaaaaaaaaaaaaaaa";

    private static readonly Dictionary<string, double> Prices = new()
    {
        [Bitcoin] = 1_444_322,
        [Weapon] = 250_000,
        [Scope] = 120_000,
        [Ammo] = 1_000,
        [Coin] = 100_000,
    };

    private static double Price(MongoId tpl) => Prices.GetValueOrDefault(tpl.ToString());

    [Fact]
    public void Children_are_summed_with_stack_counts()
    {
        var weapon = Item(Weapon);
        var profile = Profile(pmc: [weapon, Item(Scope, parent: weapon), Item(Ammo, parent: weapon, stack: 30)]);

        var result = Calculate(profile, [weapon.Id]);

        Assert.Equal(4, result.Converted); // 250,000 + 120,000 + 30,000 = 400,000
        Assert.Equal(0, result.Lost);
    }

    [Fact]
    public void Conversion_rounds_down_and_unpriced_items_are_zero()
    {
        var bag = Item(Unpriced);
        var profile = Profile(pmc: [bag, Item(Ammo, parent: bag, stack: 199), Item(Unpriced, parent: bag)]);

        Assert.Equal(1, Calculate(profile, [bag.Id]).Converted); // 199,000 → 1
    }

    [Fact]
    public void Conversion_is_capped_by_bitcoins_filling_the_window_and_the_rest_is_lost()
    {
        var bitcoins = Enumerable.Range(0, 30).Select(_ => Item(Bitcoin)).ToList();
        var profile = Profile(pmc: bitcoins);

        var result = Calculate(profile, bitcoins.Select(item => item.Id), cells: 24);

        Assert.Equal(346, result.Converted); // 24 × 1,444,322 ÷ 100,000
        Assert.Equal(433 - 346, result.Lost); // 30 × 1,444,322 ÷ 100,000 = 433
    }

    [Fact]
    public void Coins_everywhere_in_the_old_profile_carry_over()
    {
        var profile = Profile(
            pmc: [Item(Coin, stack: 100), Item(Coin, stack: 7)],
            scav: [Item(Coin, stack: 3)],
            btr: [Item(Coin, stack: 2), Item(Ammo, stack: 60)]);
        profile.DialogueRecords = Mail(Message(Now - 10, 3600, collected: null, Item(Coin, stack: 50)));

        var result = PrestigeCoinConversion.Calculate(profile, [], 24, Price, Now, out var details);

        Assert.Equal((107L, 3L, 50L, 2L), (details.CarriedPmc, details.CarriedScav, details.CarriedMail, details.CarriedBtr));
        Assert.Equal(346, details.Cap);
        Assert.Equal(162, result.CarriedOver);
        Assert.Equal(0, result.Converted);
        Assert.Equal(162, result.Total);
    }

    [Fact]
    public void Expired_and_collected_mail_is_not_counted()
    {
        var profile = Profile(pmc: []);
        profile.DialogueRecords = Mail(
            Message(Now - 7200, 3600, collected: null, Item(Coin, stack: 40)),
            Message(Now - 10, 3600, collected: true, Item(Coin, stack: 20)),
            Message(Now - 10, 3600, collected: false, Item(Coin, stack: 5)));

        Assert.Equal(5, Calculate(profile, []).CarriedOver);
    }

    [Fact]
    public void Transferred_items_are_converted_not_carried_over()
    {
        var case_ = Item(Unpriced);
        var coinsInside = Item(Coin, parent: case_, stack: 10);
        var profile = Profile(pmc: [case_, coinsInside, Item(Coin, stack: 4)]);

        var result = Calculate(profile, [case_.Id]);

        Assert.Equal(4, result.CarriedOver);
        Assert.Equal(10, result.Converted); // 코인 핸드북 가격 100,000 → 1:1
    }

    [Fact]
    public void Missing_transfer_ids_and_empty_profiles_give_zero()
    {
        Assert.Equal(new PrestigeCoinResult(0, 0, 0), Calculate(Profile(pmc: [Item(Weapon)]), [new MongoId()]));
        Assert.Equal(new PrestigeCoinResult(0, 0, 0), PrestigeCoinConversion.Calculate(new SptProfile(), [new MongoId()], 24, Price, Now));
    }

    [Fact]
    public void Duplicate_and_nested_transfer_ids_count_once()
    {
        var weapon = Item(Weapon);
        var scope = Item(Scope, parent: weapon);
        var profile = Profile(pmc: [weapon, scope]);

        Assert.Equal(3, Calculate(profile, [weapon.Id, scope.Id, weapon.Id]).Converted); // 370,000
    }

    [Fact]
    public void Mail_text_lists_carried_converted_and_lost()
    {
        Assert.Equal("Prestige coins: carried over 12 + converted 346", PrestigeCoinService.Message(new(12, 346, 0)));
        Assert.Equal("Prestige coins: carried over 0 + converted 346 (87 coins lost over the conversion cap)", PrestigeCoinService.Message(new(0, 346, 87)));
    }

    private static PrestigeCoinResult Calculate(SptProfile profile, IEnumerable<MongoId> transfer, int cells = 24) =>
        PrestigeCoinConversion.Calculate(profile, transfer, cells, Price, Now);

    private static Item Item(string tpl, Item? parent = null, double? stack = null) =>
        new()
        {
            Id = new MongoId(),
            Template = tpl,
            ParentId = parent?.Id.ToString() ?? "stash",
            SlotId = parent is null ? "hideout" : "main",
            Upd = stack is null ? null : new Upd { StackObjectsCount = stack },
        };

    private static SptProfile Profile(List<Item> pmc, List<Item>? scav = null, List<Item>? btr = null) =>
        new()
        {
            CharacterData = new Characters
            {
                PmcData = new PmcData { Inventory = new BotBaseInventory { Items = pmc } },
                ScavData = new PmcData { Inventory = new BotBaseInventory { Items = scav ?? [] } },
            },
            BtrDeliveryList = btr is null ? null : [new BtrDelivery { Id = new MongoId(), Items = btr }],
        };

    private static Message Message(long sent, long storage, bool? collected, params Item[] items) =>
        new()
        {
            Id = new MongoId(),
            DateTime = sent,
            MaxStorageTime = storage,
            HasRewards = true,
            RewardCollected = collected,
            Items = new MessageItems { Data = [.. items] },
        };

    private static Dictionary<MongoId, Dialogue> Mail(params Message[] messages) =>
        new() { [new MongoId()] = new Dialogue { Id = new MongoId(), Messages = [.. messages] } };
}
