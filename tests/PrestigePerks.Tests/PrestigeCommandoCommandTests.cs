using PrestigePerks.Commands;

namespace PrestigePerks.Tests;

public class PrestigeCommandoCommandTests
{
    [Theory]
    [InlineData("prestige coin", 1)]
    [InlineData("prestige coin 10", 10)]
    [InlineData("prestige coin 1000", 1000)]
    [InlineData("prestige coin 7 ", 7)]
    public void Parses_valid_amounts(string text, int expected)
    {
        Assert.Equal(expected, PrestigeCommandoCommand.ParseCoinAmount(text));
    }

    [Theory]
    [InlineData("prestige coin 0")]
    [InlineData("prestige coin 1001")]
    [InlineData("prestige coin -1")]
    [InlineData("prestige coin abc")]
    [InlineData("prestige coin 99999999999")]
    [InlineData("prestige coins 3")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_invalid_input(string? text)
    {
        Assert.Null(PrestigeCommandoCommand.ParseCoinAmount(text));
    }
}
