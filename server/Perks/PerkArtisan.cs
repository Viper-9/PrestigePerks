using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace PrestigePerks.Perks;

/// <summary>
/// 17번 장인: 은신처 생산 시간 ×0.8 (단계 III. I·II 는 효과량 0.2 에 단계 비율을 곱한다) (제작, 스캐브 케이스, 컬티스트 제단, 비트코인). dev-docs 2_특전.md 6절 "17번 장인".
/// 계산은 순수 함수로 두어 단위 테스트하고, 특전 보유 여부는 요청한 PMC 프로필의 특전 데이터로 판단한다.
/// </summary>
public static class PerkArtisan
{
    public const int Perk = 17;
    /// <summary>단계 III 의 시간 배율.</summary>
    public const double TimeFactor = 0.8;

    /// <summary>바닐라 서버의 최소 생산 시간 (HideoutHelper.GetAdjustedCraftTimeWithSkills).</summary>
    public const double MinSeconds = 5;

    /// <summary>서버 시작 때 넣는다 (<see cref="PrestigePerksMod"/>). 없으면 특전 없음으로 본다.</summary>
    internal static PerkDataStore? Store { get; set; }

    /// <summary>단계별 슬롯 수를 읽는다. 서버 시작 때 넣는다 (<see cref="PrestigePerksMod"/>).</summary>
    internal static PerkConfig Config { get; set; } = new();

    /// <summary>효과 비율 scale 의 시간 배율: 1 − 0.2 × scale.</summary>
    public static double Factor(double scale) => 1 - (1 - TimeFactor) * scale;

    /// <summary>생산 시간 ×(1 − 0.2·scale), 최소 5초. 바닐라가 이미 5초 아래면 그대로.</summary>
    public static double? CraftTime(double? vanilla, double scale = 1)
    {
        if (vanilla is not { } time || double.IsNaN(time) || double.IsInfinity(time) || time <= MinSeconds)
        {
            return vanilla;
        }

        return Math.Max(time * Factor(scale), MinSeconds);
    }

    /// <summary>비트코인: 속도 ÷배율. 경과 시간에 걸어 진행도가 III 에서 1.25배로 쌓이게 한다.</summary>
    public static long? BitcoinElapsed(long? vanilla, double scale = 1) =>
        vanilla is { } elapsed && elapsed > 0 ? (long)Math.Round(elapsed / Factor(scale), MidpointRounding.AwayFromZero) : vanilla;

    /// <summary>이 PMC 가 17번을 적용받는가.</summary>
    public static bool Has(PmcData? pmc) => Scale(pmc) > 0;

    /// <summary>이 PMC 의 17번 효과 비율 (없으면 0). 특전 데이터의 활성 특전(단계별 슬롯 수만큼, 개발용 all)과 적용 단계로 정한다.</summary>
    public static double Scale(PmcData? pmc)
    {
        var store = Store;
        if (store is null || pmc?.Info is null || (pmc.SessionId ?? pmc.Id) is not { } id || id.IsEmpty)
        {
            return 0;
        }

        lock (store.GetLock(id))
        {
            var data = store.Get(id);
            return !data.LoadFailed && PerkRules.ActiveScales(data, pmc.Info.PrestigeLevel ?? 0, Config).TryGetValue(Perk, out var scale) ? scale : 0;
        }
    }
}
