using PrestigePerks.Perks;

namespace PrestigePerks.Tests;

public class PerkBetaTests
{
    // 테스트는 일반 빌드라 베타 규칙이 꺼져 있어야 한다. 정식 배포에 베타가 켜져 나가지 않게 한다
    [Fact]
    public void Beta_is_off_in_normal_builds()
    {
        Assert.False(PerkBeta.Enabled);
        Assert.Equal(2, PerkBeta.Level(2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(6)]
    public void Beta_opens_all_six_slots_regardless_of_prestige(int prestige)
    {
        Assert.Equal(6, PerkBeta.Level(prestige, enabled: true));
        Assert.Equal(prestige, PerkBeta.Level(prestige, enabled: false));
    }
}
