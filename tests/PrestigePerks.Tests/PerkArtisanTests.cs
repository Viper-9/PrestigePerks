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
}
