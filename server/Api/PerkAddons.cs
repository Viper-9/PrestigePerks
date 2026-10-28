using PrestigePerks.Perks;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace PrestigePerks.Api;

/// <summary>
/// 서버 DLL add-on(C형)이 PrestigePerks DLL 을 참조해 읽는 특전 상태. 특전은 서버 perks/&lt;add-on&gt;/perks.json 에 "kind": "code" 로 정의한다.
/// 요청한 PMC(SessionId)의 특전 데이터로 판단하며, 매 호출이 데이터 캐시를 잠그므로 자주 불리는 곳에서는 요청마다 한 번 읽어 둔다.
/// 특전 데이터는 OnLoadOrder.Preload + 10 뒤에 읽을 수 있다.
/// </summary>
public static class PerkAddons
{
    /// <summary>효과 비율 (단계 I 0.5, II 0.75, III 1, config tierEffectRatio 따름). 꺼져 있거나 모르는 특전이면 0. III 효과량에 곱한다.</summary>
    public static double Scale(PmcData? pmc, string id) => PerkScales.Of(pmc, id);

    /// <summary>이 PMC 에 그 특전 효과가 켜져 있는가 (열린 슬롯에 있고 단계 비율 &gt; 0). 모르는 특전이면 false.</summary>
    public static bool Has(PmcData? pmc, string id) => Scale(pmc, id) > 0;
}
