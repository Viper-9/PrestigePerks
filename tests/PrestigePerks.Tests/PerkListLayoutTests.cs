using PrestigePerks.Model;

namespace PrestigePerks.Tests;

public class PerkListLayoutTests
{
    // 내장 20개는 지금 화면과 같다: 열마다 10줄, 왼쪽 열을 먼저 채운다
    [Fact]
    public void Twenty_perks_keep_the_current_two_columns_of_ten()
    {
        Assert.Equal(10, PerkListLayout.RowsPerColumn(20));
        Assert.False(PerkListLayout.Overflows(20));
        Assert.Equal((0, 0), PerkListLayout.Place(0, 10));
        Assert.Equal((0, 9), PerkListLayout.Place(9, 10));
        Assert.Equal((1, 0), PerkListLayout.Place(10, 10));
        Assert.Equal((1, 9), PerkListLayout.Place(19, 10));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(5, 10)]
    [InlineData(21, 11)]
    [InlineData(24, 12)]
    [InlineData(25, 13)]
    public void Rows_per_column_is_at_least_ten_and_splits_the_rest_evenly(int count, int rows)
    {
        Assert.Equal(rows, PerkListLayout.RowsPerColumn(count));
        Assert.Equal(count > 20, PerkListLayout.Overflows(count));
    }

    [Fact]
    public void Content_height_fits_the_rows_below_the_header()
    {
        // 머리 58 + 줄 10개 × 62 + 사이 9 × 3 + 아래 여백 8
        Assert.Equal(58 + 10 * 62 + 9 * 3 + 8, PerkListLayout.ContentHeight(10));
        Assert.Equal(PerkListLayout.ContentHeight(10) + 2 * 65, PerkListLayout.ContentHeight(12));
    }
}
