using PrestigePerks.Perks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI.Routing;

namespace PrestigePerks.Routes;

/// <summary>
/// 코인을 쓰는 아이템 이벤트 (리롤, 보유하기·단계 구매). 클라이언트 플러그인이 바닐라 상인 구매와 같은 대기열(ClientBackendSession.SendOperationRightNow)로 보낸다.
/// 코인 차감은 응답 ProfileChanges 에 담겨 클라이언트가 바닐라처럼 반영한다. 서버가 인벤토리를 따로 바꾸지 않는다 (dev-docs 0_개요.md 3절).
/// </summary>
[Injectable]
public class PerkItemEventRouter(PerkService perkService)
    : ItemEventRouter(
    [
        new ItemRouteAction<PerkRollRequest>(PerkRollRequest.ActionName,
            (_, pmcData, request, sessionId, output, _) =>
                new ValueTask<SPTarkov.Server.Core.Models.Eft.ItemEvent.ItemEventRouterResponse>(perkService.RollWithCoins(sessionId, pmcData, request, output))),
        new ItemRouteAction<PerkBuyRequest>(PerkBuyRequest.ActionName,
            (_, pmcData, request, sessionId, output, _) =>
                new ValueTask<SPTarkov.Server.Core.Models.Eft.ItemEvent.ItemEventRouterResponse>(perkService.BuyWithCoins(sessionId, pmcData, request, output))),
    ]);
