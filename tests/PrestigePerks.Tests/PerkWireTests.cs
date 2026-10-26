using PrestigePerks.Perks;

namespace PrestigePerks.Tests;

/// <summary>서버 문자열 ID ↔ 클라이언트 통신 번호 (server/Perks/PerkWire.cs).</summary>
public class PerkWireTests
{
    [Fact]
    public void Numbers_from_the_client_become_ids()
    {
        Assert.Equal("mastery", PerkWire.ToId(24));
        // 모르는 번호는 카탈로그에 없는 ID 가 되어 규칙이 UnknownPerk 로 거부한다
        Assert.Equal("999", PerkWire.ToId(999));
        Assert.False(PerkCatalog.IsKnown(PerkWire.ToId(999)));
    }

    [Fact]
    public void Ids_become_numbers_and_unknown_ids_are_left_out()
    {
        Assert.Equal(24, PerkWire.ToNumber("mastery"));
        Assert.Equal(0, PerkWire.ToNumber(null));
        Assert.Equal(0, PerkWire.ToNumber("someone.sprint"));
        Assert.Equal([1, 24], PerkWire.ToNumbers(["experience", null, "someone.sprint", "mastery"]));
        Assert.Equal(
            new Dictionary<int, int> { [14] = 2 },
            PerkWire.ToNumberKeys(new Dictionary<string, int> { ["control"] = 2, ["someone.sprint"] = 3 }));
    }

    [Fact]
    public void Slots_keep_their_state()
    {
        var state = PerkWire.ToState(new PerkSlot { Perk = "reload", Locked = true });
        Assert.Equal((29, true, false), (state.Perk, state.Locked, state.Fixed));
        Assert.True(PerkWire.ToState(new PerkSlot()).IsEmpty);
    }
}
