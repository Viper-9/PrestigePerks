using SPTarkov.Server.Core.Models.Eft.Common;

namespace PrestigePerks.Perks;

/// <summary>
/// PMC·특전 ID → 효과 비율 (단계 I 0.5, II 0.75, III 1, config tierEffectRatio. 없으면 0). 서버 패치 특전(17번 장인, C형 add-on)이 쓴다.
/// 요청한 PMC 프로필의 특전 데이터(활성 특전: 단계별 슬롯 수만큼, 개발용 all)와 적용 단계로 정한다.
/// </summary>
public static class PerkScales
{
    /// <summary>서버 시작 때 넣는다 (<see cref="PrestigePerksMod"/>). 없으면 특전 없음으로 본다.</summary>
    internal static PerkDataStore? Store { get; set; }

    /// <summary>단계별 슬롯 수·비율을 읽는다. 서버 시작 때 넣는다 (<see cref="PrestigePerksMod"/>).</summary>
    internal static PerkConfig Config { get; set; } = new();

    /// <summary>이 PMC 의 그 특전 효과 비율. 모르는 특전(add-on 을 뺀 경우), PMC 가 아니거나 데이터를 못 읽었으면 0. 호출마다 데이터 캐시를 잠근다.</summary>
    public static double Of(PmcData? pmc, string? id)
    {
        var store = Store;
        if (store is null || !PerkCatalog.IsKnown(id) || pmc?.Info is null || (pmc.SessionId ?? pmc.Id) is not { } profileId || profileId.IsEmpty)
        {
            return 0;
        }

        lock (store.GetLock(profileId))
        {
            var data = store.Get(profileId);
            return !data.LoadFailed && PerkRules.ActiveScales(data, pmc.Info.PrestigeLevel ?? 0, Config).TryGetValue(id!, out var scale) ? scale : 0;
        }
    }
}
