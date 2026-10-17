using PrestigePerks.Commands;
using PrestigePerks.Perks;

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

    [Theory]
    [InlineData("prestige perk status", PrestigeCommandoCommand.PerkAction.Status, 0)]
    [InlineData("prestige perk OPEN", PrestigeCommandoCommand.PerkAction.Open, 0)]
    [InlineData("prestige perk roll", PrestigeCommandoCommand.PerkAction.Roll, 0)]
    [InlineData("prestige perk lock 1", PrestigeCommandoCommand.PerkAction.Lock, 1)]
    [InlineData("prestige perk unlock 6", PrestigeCommandoCommand.PerkAction.Unlock, 6)]
    [InlineData("prestige perk confirm", PrestigeCommandoCommand.PerkAction.Confirm, 0)]
    [InlineData("prestige perk balance 0", PrestigeCommandoCommand.PerkAction.Balance, 0)]
    [InlineData("prestige perk balance 500 ", PrestigeCommandoCommand.PerkAction.Balance, 500)]
    [InlineData("prestige perk reset", PrestigeCommandoCommand.PerkAction.Reset, 0)]
    public void Parses_perk_commands(string text, PrestigeCommandoCommand.PerkAction action, int argument)
    {
        Assert.Equal((action, argument), PrestigeCommandoCommand.ParsePerkCommand(text));
    }

    [Theory]
    [InlineData("prestige perk")]
    [InlineData("prestige perk lock")]
    [InlineData("prestige perk lock 0")]
    [InlineData("prestige perk lock 7")]
    [InlineData("prestige perk balance")]
    [InlineData("prestige perk balance 100001")]
    [InlineData("prestige perk roll 2")]
    [InlineData("prestige perk dance")]
    [InlineData("prestige perk 1")]
    [InlineData(null)]
    public void Rejects_invalid_perk_commands(string? text)
    {
        Assert.Null(PrestigeCommandoCommand.ParsePerkCommand(text));
    }

    [Fact]
    public void Formats_state_for_chat()
    {
        var text = PrestigeCommandoCommand.FormatState(new PerkState
        {
            PrestigeLevel = 3,
            CoinBalance = 120,
            ActivePerks = [1, 8],
            Session = new PerkSession
            {
                Reason = PerkSessionReason.Prestige,
                Slots = [new PerkSlot { Perk = 1, Locked = true }, new PerkSlot { Perk = 24 }, new PerkSlot()],
                FreeRerollsLeft = 0,
                PaidRerolls = 1,
            },
            NextRerollCost = 90,
        });

        Assert.Equal(
            "P3 | coins 120 | active: experience(1), scav(8)\n" +
            "screen (prestige):  [1] experience(1) (locked)  [2] mastery(24)  [3] empty\n" +
            "free rerolls 0 | paid rerolls 1 | next reroll: 90 coins",
            text);
    }
}
