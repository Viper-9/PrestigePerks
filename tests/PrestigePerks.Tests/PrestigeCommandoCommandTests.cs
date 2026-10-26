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
    [InlineData("prestige coin preview", true)]
    [InlineData("PRESTIGE COIN PREVIEW ", true)]
    [InlineData("prestige coin preview 3", false)]
    [InlineData("prestige coin 3", false)]
    [InlineData(null, false)]
    public void Parses_coin_preview(string? text, bool expected)
    {
        Assert.Equal(expected, PrestigeCommandoCommand.IsCoinPreview(text));
        if (expected)
        {
            Assert.Null(PrestigeCommandoCommand.ParseCoinAmount(text));
        }
    }

    [Theory]
    [InlineData("prestige perk status", PrestigeCommandoCommand.PerkAction.Status, 0)]
    [InlineData("prestige perk ROLL", PrestigeCommandoCommand.PerkAction.Roll, 0)]
    [InlineData("prestige perk lock 1", PrestigeCommandoCommand.PerkAction.Lock, 1)]
    [InlineData("prestige perk unlock 6", PrestigeCommandoCommand.PerkAction.Unlock, 6)]
    [InlineData("prestige perk free 0", PrestigeCommandoCommand.PerkAction.Free, 0)]
    [InlineData("prestige perk free 100 ", PrestigeCommandoCommand.PerkAction.Free, 100)]
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
    [InlineData("prestige perk free")]
    [InlineData("prestige perk free 101")]
    [InlineData("prestige perk roll 2")]
    [InlineData("prestige perk open")]
    [InlineData("prestige perk confirm")]
    [InlineData("prestige perk balance 10")]
    [InlineData("prestige perk dance")]
    [InlineData("prestige perk 1")]
    [InlineData(null)]
    public void Rejects_invalid_perk_commands(string? text)
    {
        Assert.Null(PrestigeCommandoCommand.ParsePerkCommand(text));
    }

    [Theory]
    [InlineData("prestige perk set 1 2", 1, 2)]
    [InlineData("  PRESTIGE PERK SET 6 29 ", 6, 29)]
    public void Parses_set_command(string text, int slot, int perk)
    {
        Assert.Equal((slot, perk), PrestigeCommandoCommand.ParseSetCommand(text));
    }

    [Theory]
    [InlineData("prestige perk set 0 2")]
    [InlineData("prestige perk set 7 2")]
    [InlineData("prestige perk set 1 999")]
    [InlineData("prestige perk set 1")]
    [InlineData("prestige perk set")]
    [InlineData(null)]
    public void Rejects_invalid_set_commands(string? text)
    {
        Assert.Null(PrestigeCommandoCommand.ParseSetCommand(text));
    }

    [Theory]
    [InlineData("prestige perk all on", true)]
    [InlineData(" PRESTIGE PERK ALL OFF ", false)]
    public void Parses_all_command(string text, bool on)
    {
        Assert.Equal(on, PrestigeCommandoCommand.ParseAllCommand(text));
    }

    [Theory]
    [InlineData("prestige perk all")]
    [InlineData("prestige perk all yes")]
    [InlineData(null)]
    public void Rejects_invalid_all_commands(string? text)
    {
        Assert.Null(PrestigeCommandoCommand.ParseAllCommand(text));
    }

    [Fact]
    public void Formats_state_for_chat()
    {
        var text = PrestigeCommandoCommand.FormatState(new PerkState
        {
            PrestigeLevel = 3,
            ActivePerks = [1, 24, 8],
            ActiveTiers = new Dictionary<int, int> { [1] = 1, [24] = 2, [8] = 1 },
            Owned = new Dictionary<int, int> { [24] = 3 },
            TierCap = 2,
            Slots = [new PerkSlotState { Perk = 1, Locked = true }, new PerkSlotState { Perk = 24, Fixed = true }, new PerkSlotState { Perk = 8 }, new PerkSlotState()],
            NewPerks = [8],
            FreeRerollsLeft = 0,
            PaidRerolls = 1,
            NextRerollCost = 90,
        });

        Assert.Equal(
            "P3 | tier cap II | active: experience(1) I, mastery(24) II, scav(8) I\n" +
            "new perks: scav(8)\n" +
            "slots:  [1] experience(1) I (locked)  [2] mastery(24) II (fixed)  [3] scav(8) I  [4] empty\n" +
            "collection 1 / 20: mastery(24) III\n" +
            "free rerolls 0 | paid rerolls 1 | next reroll: 90 coins",
            text);
    }

    [Theory]
    [InlineData("prestige perk tier 14 2", 14, 2)]
    [InlineData("prestige perk tier 1 0", 1, 0)]
    public void Parses_tier_commands(string text, int perk, int tier)
    {
        Assert.Equal((perk, tier), PrestigeCommandoCommand.ParseTierCommand(text));
    }

    [Theory]
    [InlineData("prestige perk tier 14 4")]
    [InlineData("prestige perk tier 999 1")]
    [InlineData("prestige perk tier 14")]
    public void Rejects_invalid_tier_commands(string text)
    {
        Assert.Null(PrestigeCommandoCommand.ParseTierCommand(text));
    }
}
