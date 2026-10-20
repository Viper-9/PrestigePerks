using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace PrestigePerks.Perks;

/// <summary>
/// 17번 장인: 은신처 생산 시간 ×0.8 (제작, 스캐브 케이스, 컬티스트 제단, 비트코인). dev-docs 2_특전.md 6절 "17번 장인".
/// 계산은 순수 함수로 두어 단위 테스트하고, 특전 보유 여부는 요청한 PMC 프로필의 특전 데이터로 판단한다.
/// </summary>
public static class PerkArtisan
{
    public const int Perk = 17;
    public const double TimeFactor = 0.8;

    /// <summary>바닐라 서버의 최소 생산 시간 (HideoutHelper.GetAdjustedCraftTimeWithSkills).</summary>
    public const double MinSeconds = 5;

    /// <summary>서버 시작 때 넣는다 (<see cref="PrestigePerksMod"/>). 없으면 특전 없음으로 본다.</summary>
    internal static PerkDataStore? Store { get; set; }

    /// <summary>단계별 슬롯 수를 읽는다. 서버 시작 때 넣는다 (<see cref="PrestigePerksMod"/>).</summary>
    internal static PerkConfig Config { get; set; } = new();

    /// <summary>생산 시간 ×0.8, 최소 5초. 바닐라가 이미 5초 아래면 그대로.</summary>
    public static double? CraftTime(double? vanilla)
    {
        if (vanilla is not { } time || double.IsNaN(time) || double.IsInfinity(time) || time <= MinSeconds)
        {
            return vanilla;
        }

        return Math.Max(time * TimeFactor, MinSeconds);
    }

    /// <summary>비트코인: 속도 ÷0.8. 경과 시간에 걸어 진행도가 1.25배로 쌓이게 한다.</summary>
    public static long? BitcoinElapsed(long? vanilla) =>
        vanilla is { } elapsed && elapsed > 0 ? (long)Math.Round(elapsed / TimeFactor, MidpointRounding.AwayFromZero) : vanilla;

    /// <summary>이 PMC 가 17번을 적용받는가. 특전 데이터의 활성 특전(단계별 슬롯 수만큼, 개발용 all)으로 판단한다.</summary>
    public static bool Has(PmcData? pmc)
    {
        var store = Store;
        if (store is null || pmc?.Info is null || (pmc.SessionId ?? pmc.Id) is not { } id || id.IsEmpty)
        {
            return false;
        }

        lock (store.GetLock(id))
        {
            var data = store.Get(id);
            return !data.LoadFailed && PerkRules.ActivePerks(data, Config.Slots(pmc.Info.PrestigeLevel ?? 0)).Contains(Perk);
        }
    }
}
