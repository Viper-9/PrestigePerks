using PrestigePerks.Items;
using PrestigePerks.Perks;

namespace PrestigePerks.Tests;

public class PrestigeCoinItemTests
{
    [Fact]
    public void Uses_dedicated_bundle_when_available()
    {
        var details = PrestigeCoinItem.BuildDetails(bundleAvailable: true);

        Assert.Equal(PrestigeCoinItem.BundleKey, details.OverrideProperties!.Prefab!.Path);
    }

    [Fact]
    public void Keeps_tarcoin_model_when_bundle_missing()
    {
        var details = PrestigeCoinItem.BuildDetails(bundleAvailable: false);

        Assert.Null(details.OverrideProperties!.Prefab);
        Assert.Equal(PrestigeCoinItem.TarCoinTemplateId, details.ItemTplToClone.ToString());
    }

    [Fact]
    public void Template_id_is_fixed_and_valid()
    {
        var details = PrestigeCoinItem.BuildDetails(bundleAvailable: false);

        Assert.Equal(PrestigeCoinItem.TemplateId, details.NewId.ToString());
        Assert.Matches("^[0-9a-f]{24}$", PrestigeCoinItem.TemplateId);
    }

    [Fact]
    public void Cannot_be_turned_into_roubles()
    {
        var props = PrestigeCoinItem.BuildDetails(bundleAvailable: false).OverrideProperties!;

        Assert.True(props.IsUnsaleable);
        Assert.True(props.InsuranceDisabled);
        Assert.False(props.CanSellOnRagfair);
        Assert.Equal(100, props.StackMaxSize);
    }

    [Fact]
    public void Stack_size_follows_config()
    {
        var props = PrestigeCoinItem.BuildDetails(bundleAvailable: false, coin: new CoinConfig { StackSize = 500 }).OverrideProperties!;

        Assert.Equal(500, props.StackMaxSize);
    }

    [Theory]
    [InlineData("""{ "coin": { "stackSize": 0 } }""")]
    [InlineData("""{ "coin": { "stackSize": 1001 } }""")]
    public void Invalid_stack_size_falls_back_to_default(string json)
    {
        var warnings = new List<string>();

        var config = PerkConfigLoader.Validated(PerkConfigLoader.ParseModConfig(json), warnings.Add);

        Assert.Equal(100, config.Coin.StackSize);
        Assert.Contains(warnings, warning => warning.Contains("coin.stackSize"));
    }

    [Fact]
    public void Has_english_and_korean_names()
    {
        var locales = PrestigeCoinItem.BuildDetails(bundleAvailable: false).Locales;

        Assert.Equal("en", locales.Keys.First());
        Assert.Equal("프레스티지 코인", locales["kr"].Name);
    }
}

public class PrestigeCoinApiTests
{
    [Fact]
    public void Api_exposes_the_dedicated_coin()
    {
        Assert.Equal("9fc8dc52dd7dbf55fc27ac09", PrestigePerks.Api.PrestigeCoin.TemplateId.ToString());
        Assert.Equal(100_000, PrestigePerks.Api.PrestigeCoin.RoublesPerCoin);
    }
}
