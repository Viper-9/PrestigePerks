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
///   /prestigeperks/shake    { "slot": 2, "next": 24 }  리롤 중 흔들기 (멈추려는 릴과 그 다음 칸 특전). 응답 shakeOutcome
/// 리롤은 코인을 인벤토리에서 빼야 해서 아이템 이벤트(<see cref="PerkItemEventRouter"/>)로 받는다.
/// /client/game/start 는 같은 URL 의 라우터가 모두 차례로 실행되고 앞 응답이 output 으로 넘어오므로, output 을 그대로 돌려줘 바닐라 응답을 바꾸지 않는다.
/// </summary>
[Injectable]
public class PerkStaticRouter : StaticRouter
{
    public const string StateRoute = "/prestigeperks/state";
    public const string LockRoute = "/prestigeperks/lock";
    public const string ShakeRoute = "/prestigeperks/shake";

    public PerkStaticRouter(JsonUtil jsonUtil, HttpResponseUtil httpResponseUtil, PerkService perkService)
        : base(jsonUtil,
        [
            new RouteAction<EmptyRequestData>("/client/game/start",
                (_, _, sessionId, output, _) =>
                {
                    perkService.OnGameStart(sessionId);
                    return new ValueTask<string>(output ?? "");
                }),
            new RouteAction<EmptyRequestData>(StateRoute,
                (_, _, sessionId, _, _) => Respond(httpResponseUtil, perkService.GetState(sessionId, markNewPerksSeen: true))),
            // 본문이 비면 StaticRouter 가 EmptyRequestData 를 넘기므로 제네릭 RouteAction 의 강제 형변환을 쓰지 않는다
            new RouteAction(LockRoute,
                async (_, info, sessionId, _, _) => info is PerkLockRequest request
                    ? await Respond(httpResponseUtil, perkService.SetLock(sessionId, request.Slot, request.Locked))
                    : await Respond(httpResponseUtil, new PerkState { Error = nameof(PerkError.InvalidSlot) }),
                typeof(PerkLockRequest)),
            new RouteAction(ShakeRoute,
                async (_, info, sessionId, _, _) => info is PerkShakeRequest request
                    ? await Respond(httpResponseUtil, perkService.Shake(sessionId, request.Slot, request.Next))
                    : await Respond(httpResponseUtil, new PerkState { Error = nameof(PerkError.NoShake) }),
                typeof(PerkShakeRequest)),
        ])
    {
    }

    private static ValueTask<string> Respond(HttpResponseUtil httpResponseUtil, PerkState state) =>
        new(httpResponseUtil.NoBody(state));
}
