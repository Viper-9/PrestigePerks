using PrestigePerks.Model;
using PrestigePerks.Perks;

namespace PrestigePerks.Tests;

/// <summary>17번 장인 서버 계산 (server/Perks/PerkArtisan.cs, dev-docs 2_특전.md 6절 "17번 장인").</summary>
public class PerkArtisanTests
{
    [Theory]
    [InlineData(1000.0, 800.0)]
    [InlineData(6.0, 5.0)]   // 4.8 → 최소 5초
    [InlineData(5.0, 5.0)]   // 바닐라가 이미 최소면 그대로
    [InlineData(3.0, 3.0)]
    [InlineData(43200.0, 34560.0)] // 제단 12h → 9.6h
    public void Craft_time_is_shortened_by_a_fifth_with_a_five_second_floor(double vanilla, double expected) =>
        Assert.Equal(expected, PerkArtisan.CraftTime(vanilla)!.Value, 6);

    [Fact]
    public void Craft_time_keeps_missing_or_invalid_values()
    {
        Assert.Null(PerkArtisan.CraftTime(null));
        Assert.Equal(double.NaN, PerkArtisan.CraftTime(double.NaN));
    }

    [Theory]
    [InlineData(100L, 125L)]
    [InlineData(10L, 13L)] // 12.5 → 반올림
    [InlineData(0L, 0L)]
    [InlineData(-5L, -5L)]
    public void Bitcoin_progresses_a_quarter_faster(long elapsed, long expected) =>
        Assert.Equal(expected, PerkArtisan.BitcoinElapsed(elapsed));

    [Fact]
    public void Bitcoin_keeps_missing_elapsed() => Assert.Null(PerkArtisan.BitcoinElapsed(null));

    // 화면 수치(카탈로그), 서버 생산 시간, 클라이언트 타이머가 같은 배율을 쓴다. 수치를 바꿀 때 한 곳을 빠뜨리지 않게
    [Fact]
    public void Server_and_client_time_factors_follow_the_catalog_amount()
    {
        var amount = PerkCatalog.Builtin.Single(perk => perk.Id == PerkArtisan.Perk).Amount;
        Assert.Equal(1 + amount / 100, PerkArtisan.TimeFactor, 9);
        Assert.Equal(PerkArtisan.TimeFactor, PerkPatchMath.CraftTimeFactor, 6);
    }

    [Theory]
    [InlineData(0.5, 900.0, 111L)]   // I: ×0.9, 100 ÷ 0.9 = 111.1
    [InlineData(0.75, 850.0, 118L)]  // II: ×0.85
    [InlineData(1.0, 800.0, 125L)]   // III: 지금 수치
    public void Lower_tiers_scale_the_time_cut(double scale, double craft, long bitcoin)
    {
        Assert.Equal(craft, PerkArtisan.CraftTime(1000, scale)!.Value, 6);
        Assert.Equal(bitcoin, PerkArtisan.BitcoinElapsed(100, scale));
    }
}
