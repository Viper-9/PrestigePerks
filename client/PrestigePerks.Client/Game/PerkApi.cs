using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Comfort.Common;
using Newtonsoft.Json;
using PrestigePerks.Model;
using SPT.Common.Http;

namespace PrestigePerks.Game
{
    /// <summary>서버 특전 라우트와 리롤·구매 아이템 이벤트 (dev-docs 2_특전.md 6절 "서버 ↔ 클라이언트").</summary>
    internal static class PerkApi
    {
        private const string StateRoute = "/prestigeperks/state";
        private const string LockRoute = "/prestigeperks/lock";
        private const string EquipRoute = "/prestigeperks/equip";
        private const string UnequipRoute = "/prestigeperks/unequip";
        private const string IconsRoute = "/prestigeperks/icons";
        private const string DevRoute = "/prestigeperks/dev";

        /// <summary>현재 상태. 새 특전 알림은 이 응답으로 받고 서버에서 지워진다. 실패하면 null.</summary>
        public static async Task<PerkStateDto> GetState() =>
            PerkStateDto.Parse(await RequestHandler.PostJsonAsync(StateRoute, "{}"));

        /// <summary>add-on 특전 아이콘 (특전 ID → base64 PNG). 실패하면 빈 사전.</summary>
        public static async Task<Dictionary<string, string>> Icons()
        {
            try
            {
                var response = JsonConvert.DeserializeObject<IconsResponse>(await RequestHandler.PostJsonAsync(IconsRoute, "{}"));
                return response?.Icons ?? new Dictionary<string, string>();
            }
            catch (JsonException)
            {
                return new Dictionary<string, string>();
            }
        }

        /// <summary>F12 개발자 모드를 서버에 알린다 (개발용 Commando 명령을 실행할지).</summary>
        public static Task SetDevMode(bool on) =>
            RequestHandler.PostJsonAsync(DevRoute, JsonConvert.SerializeObject(new DevBody { Dev = on }));

        /// <summary>확정 넣기: 장착 안 된 보유 특전을 슬롯에 놓는다 (확정 칸이면 서로 바뀐다).</summary>
        public static async Task<PerkStateDto> Equip(string perk, int slot) =>
            PerkStateDto.Parse(await RequestHandler.PostJsonAsync(EquipRoute, JsonConvert.SerializeObject(new EquipBody { Perk = perk, Slot = slot })));

        /// <summary>확정 빼기: 그 칸은 빈 칸이 된다.</summary>
        public static async Task<PerkStateDto> Unequip(int slot) =>
            PerkStateDto.Parse(await RequestHandler.PostJsonAsync(UnequipRoute, JsonConvert.SerializeObject(new UnequipBody { Slot = slot })));

        public static async Task<PerkStateDto> SetLock(int slot, bool locked) =>
            PerkStateDto.Parse(await RequestHandler.PostJsonAsync(LockRoute, JsonConvert.SerializeObject(new LockBody { Slot = slot, Locked = locked })));

        /// <summary>
        /// 리롤. 코인을 인벤토리에서 빼야 해서 바닐라 상인 구매와 같은 대기열로 보낸다.
        /// 응답의 인벤토리 변경(코인 차감)은 바닐라가 콜백 전에 반영한다 (ClientBackendSession.SendCallback → ApplyProfileChanges).
        /// </summary>
        public static Task<IResult> Roll(int expectedCost) => Send(new RollOperation { ExpectedCost = expectedCost });

        /// <summary>보유하기(단계 1)·단계 구매. 리롤과 같은 대기열로 보내고 코인 차감은 바닐라가 반영한다. 결과는 이어서 state 로 받는다.</summary>
        public static Task<IResult> Buy(string perk, int tier, int expectedCost) => Send(new BuyOperation { Perk = perk, Tier = tier, ExpectedCost = expectedCost });

        private static Task<IResult> Send(object operation)
        {
            var completion = new TaskCompletionSource<IResult>();
            var session = PerkGame.Session;
            if (session == null)
            {
                completion.SetResult(null);
                return completion.Task;
            }

            session.SendOperationRightNow(operation, result => completion.TrySetResult(result));
            return completion.Task;
        }

        /// <summary>server PerkIconsResponse.</summary>
        private sealed class IconsResponse
        {
            [JsonProperty("icons")] public Dictionary<string, string> Icons { get; set; }
        }

        private sealed class DevBody
        {
            [JsonProperty("dev")] public bool Dev;
        }

        private sealed class EquipBody
        {
            [JsonProperty("perk")] public string Perk;
            [JsonProperty("slot")] public int Slot;
        }

        private sealed class UnequipBody
        {
            [JsonProperty("slot")] public int Slot;
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

        /// <summary>server PerkBuyRequest.</summary>
        private sealed class BuyOperation
        {
            [JsonProperty("Action")] public string Action => "PrestigePerksBuy";
            [JsonProperty("perk")] public string Perk { get; set; }
            [JsonProperty("tier")] public int Tier { get; set; }
            [JsonProperty("expectedCost")] public int ExpectedCost { get; set; }
            [JsonProperty("items")] public string[] Items { get; set; } = new string[0];
        }
    }
}
