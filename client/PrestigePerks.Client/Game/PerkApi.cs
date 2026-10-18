using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Comfort.Common;
using Newtonsoft.Json;
using PrestigePerks.Model;
using SPT.Common.Http;

namespace PrestigePerks.Game
{
    /// <summary>서버 특전 라우트와 리롤 아이템 이벤트 (dev-docs 2_특전.md 6절 "서버 ↔ 클라이언트").</summary>
    internal static class PerkApi
    {
        private const string StateRoute = "/prestigeperks/state";
        private const string LockRoute = "/prestigeperks/lock";
        private const string ShakeRoute = "/prestigeperks/shake";

        /// <summary>현재 상태. 새 특전 알림은 이 응답으로 받고 서버에서 지워진다. 실패하면 null.</summary>
        public static async Task<PerkStateDto> GetState() =>
            PerkStateDto.Parse(await RequestHandler.PostJsonAsync(StateRoute, "{}"));

        /// <summary>리롤 중 흔들기. slot 은 멈추려는 릴, next 는 그 다음 칸에 보이는 특전. 응답 shakeOutcome 이 결과다.</summary>
        public static async Task<PerkStateDto> Shake(int slot, int next) =>
            PerkStateDto.Parse(await RequestHandler.PostJsonAsync(ShakeRoute, JsonConvert.SerializeObject(new ShakeBody { Slot = slot, Next = next })));

        public static async Task<PerkStateDto> SetLock(int slot, bool locked) =>
            PerkStateDto.Parse(await RequestHandler.PostJsonAsync(LockRoute, JsonConvert.SerializeObject(new LockBody { Slot = slot, Locked = locked })));

        /// <summary>
        /// 리롤. 코인을 인벤토리에서 빼야 해서 바닐라 상인 구매와 같은 대기열로 보낸다.
        /// 응답의 인벤토리 변경(코인 차감)은 바닐라가 콜백 전에 반영한다 (ClientBackendSession.SendCallback → ApplyProfileChanges).
        /// </summary>
        public static Task<IResult> Roll(int expectedCost)
        {
            var completion = new TaskCompletionSource<IResult>();
            var session = PerkGame.Session;
            if (session == null)
            {
                completion.SetResult(null);
                return completion.Task;
            }

            session.SendOperationRightNow(new RollOperation { ExpectedCost = expectedCost }, result => completion.TrySetResult(result));
            return completion.Task;
        }

        private sealed class ShakeBody
        {
            [JsonProperty("slot")] public int Slot;
            [JsonProperty("next")] public int Next;
        }

        private sealed class LockBody
        {
            [JsonProperty("slot")] public int Slot;
            [JsonProperty("locked")] public bool Locked;
        }

        /// <summary>server PerkRollRequest. 바닐라 작업 객체처럼 Action + 인자로 직렬화된다.</summary>
        private sealed class RollOperation
        {
            [JsonProperty("Action")] public string Action => "PrestigePerksRoll";
            [JsonProperty("expectedCost")] public int ExpectedCost { get; set; }
            /// <summary>먼저 쓸 코인 스택. 비우면 서버가 바닐라 결제 순서(스태시 우선, 작은 스택 먼저)로 고른다.</summary>
            [JsonProperty("items")] public string[] Items { get; set; } = new string[0];
        }
    }
}
