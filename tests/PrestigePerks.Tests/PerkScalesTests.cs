using PrestigePerks.Commands;
using PrestigePerks.Perks;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using PmcInfo = SPTarkov.Server.Core.Models.Eft.Common.Tables.Info;

namespace PrestigePerks.Tests;

/// <summary>
/// 서버 효과 비율 (server/Perks/PerkScales.cs, C형 add-on API). PerkScales.Store 는 정적이라 병렬 실행 안 하는 컬렉션에 넣고 끝에 비운다.
/// 장인 테스트에는 PMC·데이터 준비가 없어, 파일이 없는 프로필 ID 로 PerkDataStore 를 만들고 캐시된 데이터를 채운다 (파일·로그를 쓰지 않는다).
/// </summary>
[Collection(PerkCatalogInstallCollection.Name)]
public sealed class PerkScalesTests : IDisposable
{
    private static readonly MongoId ProfileId = new("6a1b2c3d4e5f60718293a4b5");

    private readonly PmcData pmc = new() { Id = ProfileId, Info = new PmcInfo { PrestigeLevel = 3 } };

    public PerkScalesTests()
    {
        // 파일이 없는 프로필이라 Load 가 로그를 쓰지 않는다
        var store = new PerkDataStore(null!);
        var data = store.Get(ProfileId);
        // P3: 슬롯 3칸, 단계 상한 II. artisan 을 II 로 보유하고 확정 칸에 넣었다
        data.Slots = [new PerkSlot { Perk = "artisan", Fixed = true }];
        data.Owned["artisan"] = 2;
        PerkScales.Store = store;
        PerkScales.Config = new PerkConfig();
    }

    public void Dispose()
    {
        PerkScales.Store = null;
        PerkScales.Config = new PerkConfig();
    }

    [Fact]
    public void Scale_of_any_perk_matches_the_artisan_scale_for_artisan()
    {
        Assert.Equal(PerkArtisan.Scale(pmc), PerkScales.Of(pmc, "artisan"));
        Assert.Equal(0.75, PerkScales.Of(pmc, "artisan"));
        Assert.Equal(0, PerkScales.Of(pmc, "mastery"));        // 칸에 없음
        Assert.Equal(0, PerkScales.Of(pmc, "someone.unknown")); // 모르는 특전
        Assert.Equal(0, PerkScales.Of(null, "artisan"));
        Assert.Equal(PerkScales.Of(pmc, "artisan"), PrestigePerks.Api.PerkAddons.Scale(pmc, "artisan"));
        Assert.True(PrestigePerks.Api.PerkAddons.Has(pmc, "artisan"));
        Assert.False(PrestigePerks.Api.PerkAddons.Has(pmc, "mastery"));
    }

    [Fact]
    public void Installed_code_perks_are_read_like_builtin_ones()
    {
        try
        {
            PerkCatalog.Install([new PerkDefinition(0, "viper.sprint", PerkKind.Patch, 10, PerkUnit.Percent, [], new PerkAddonInfo("viper-extra", new Dictionary<string, PerkTexts>(), null))]);
            PerkScales.Store!.Get(ProfileId).Slots.Add(new PerkSlot { Perk = "viper.sprint" });

            // 보유하지 않은 리롤 칸은 I
            Assert.Equal(0.5, PrestigePerks.Api.PerkAddons.Scale(pmc, "viper.sprint"));
        }
        finally
        {
            PerkCatalog.Install([]);
        }

        // add-on 을 빼면 0
        Assert.Equal(0, PrestigePerks.Api.PerkAddons.Scale(pmc, "viper.sprint"));
    }

    [Theory]
    [InlineData("prestige perk scale artisan", "artisan")]
    [InlineData("prestige perk scale 17", "artisan")]
    [InlineData("PRESTIGE PERK SCALE Mastery ", "mastery")]
    [InlineData("prestige perk scale someone.unknown", null)]
    [InlineData("prestige perk scale", null)]
    public void Parses_scale_command(string text, string? perk)
    {
        Assert.Equal(perk, PrestigeCommandoCommand.ParseScaleCommand(text));
    }
}
