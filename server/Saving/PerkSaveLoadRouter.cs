using PrestigePerks.Perks;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Profile;

namespace PrestigePerks.Saving;

/// <summary>
/// 프로필을 파일에서 메모리로 올릴 때 특전 Bonus 를 넣는다. 서버 시작, 계정 생성, 새 프로필·프레스티지(CreateProfileService 가 저장 후 다시 로드)가 모두 여기를 지난다.
/// SaveServer 가 이 라우터를 주입받으므로 SaveServer 를 쓰는 서비스에 의존하면 안 된다.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class PerkSaveLoadRouter(ISptLogger<PerkSaveLoadRouter> logger, PerkDataStore store, PerkConfigLoader configLoader) : SaveLoadRouter
{
    protected override IEnumerable<HandledRoute> GetHandledRoutes() => [new HandledRoute("prestigeperks-profile", false)];

    protected override SptProfile HandleLoadInternal(SptProfile profile)
    {
        try
        {
            ApplyPerkBonuses(profile, store, configLoader.Config);
        }
        catch (Exception ex)
        {
            logger.Error($"[PrestigePerks] 프로필 {profile.ProfileInfo?.ProfileId} 에 특전을 넣지 못했습니다: {ex}");
        }

        return profile;
    }

    /// <summary>PMC 에 현재 특전 Bonus 를 넣는다 (멱등). 새 단계 진입·리롤·게임 시작 때도 부른다.</summary>
    public static void ApplyPerkBonuses(SptProfile profile, PerkDataStore store, PerkConfig config)
    {
        var profileId = profile.ProfileInfo?.ProfileId;
        var pmc = profile.CharacterData?.PmcData;
        // 계정만 만든 상태(PMC 가 비어 있음)는 건너뛴다
        if (profileId is null || pmc?.Info is null)
        {
            return;
        }

        var data = store.Get(profileId.Value);
        PerkBonusApplier.Apply(pmc, PerkRules.ActivePerks(data, config.Slots(pmc.Info.PrestigeLevel ?? 0)));
    }
}
