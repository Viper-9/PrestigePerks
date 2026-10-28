using PrestigePerks.Perks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils;

namespace PrestigePerks.Routes;

/// <summary>
/// 특전 페이지 라우트. 클라이언트 플러그인이 부른다. 응답은 <see cref="PerkState"/> JSON.
///   /prestigeperks/state    현재 상태 (새 단계면 처리). 새 특전 알림은 이 응답으로 넘기고 지운다
///   /prestigeperks/lock     { "slot": 0, "locked": true }
///   /prestigeperks/equip    { "perk": 14, "slot": 0 }  보유 특전을 칸에 넣는다 (확정 칸이면 서로 바꾼다)
///   /prestigeperks/unequip  { "slot": 0 }  확정 칸을 비운다
///   /prestigeperks/icons    add-on 특전 아이콘 { "icons": { id: base64 PNG } } (PerkIconsResponse, 클라이언트가 실행마다 한 번)
/// 리롤과 구매는 코인을 인벤토리에서 빼야 해서 아이템 이벤트(<see cref="PerkItemEventRouter"/>)로 받는다.
/// /client/game/start 는 같은 URL 의 라우터가 모두 차례로 실행되고 앞 응답이 output 으로 넘어오므로, output 을 그대로 돌려줘 바닐라 응답을 바꾸지 않는다.
/// </summary>
[Injectable]
public class PerkStaticRouter : StaticRouter
{
    public const string StateRoute = "/prestigeperks/state";
    public const string LockRoute = "/prestigeperks/lock";
    public const string EquipRoute = "/prestigeperks/equip";
    public const string UnequipRoute = "/prestigeperks/unequip";
    public const string IconsRoute = "/prestigeperks/icons";

    public PerkStaticRouter(JsonUtil jsonUtil, HttpResponseUtil httpResponseUtil, PerkService perkService)
        : base(jsonUtil,
        [
            new RouteAction<EmptyRequestData>("/client/game/start",
                (_, _, sessionId, output, _) =>
                {
                    perkService.OnGameStart(sessionId);
                    return new ValueTask<string>(output ?? "");
                }),
            new RouteAction<EmptyRequestData>(IconsRoute,
                (_, _, _, _, _) => new ValueTask<string>(jsonUtil.Serialize(perkService.Icons()) ?? "{}")),
            new RouteAction<EmptyRequestData>(StateRoute,
                (_, _, sessionId, _, _) => Respond(httpResponseUtil, perkService.GetState(sessionId, markNewPerksSeen: true))),
            // 본문이 비면 StaticRouter 가 EmptyRequestData 를 넘기므로 제네릭 RouteAction 의 강제 형변환을 쓰지 않는다
            new RouteAction(LockRoute,
                async (_, info, sessionId, _, _) => info is PerkLockRequest request
                    ? await Respond(httpResponseUtil, perkService.SetLock(sessionId, request.Slot, request.Locked))
                    : await Respond(httpResponseUtil, new PerkState { Error = nameof(PerkError.InvalidSlot) }),
                typeof(PerkLockRequest)),
            new RouteAction(EquipRoute,
                async (_, info, sessionId, _, _) => info is PerkEquipRequest request
                    ? await Respond(httpResponseUtil, perkService.Equip(sessionId, request.Perk, request.Slot))
                    : await Respond(httpResponseUtil, new PerkState { Error = nameof(PerkError.InvalidSlot) }),
                typeof(PerkEquipRequest)),
            new RouteAction(UnequipRoute,
                async (_, info, sessionId, _, _) => info is PerkUnequipRequest request
                    ? await Respond(httpResponseUtil, perkService.Unequip(sessionId, request.Slot))
                    : await Respond(httpResponseUtil, new PerkState { Error = nameof(PerkError.InvalidSlot) }),
                typeof(PerkUnequipRequest)),
        ])
    {
    }

    private static ValueTask<string> Respond(HttpResponseUtil httpResponseUtil, PerkState state) =>
        new(httpResponseUtil.NoBody(state));
}
