using System.Collections.Generic;
using Newtonsoft.Json;

namespace PrestigePerks.Model
{
    /// <summary>서버 /prestigeperks/* 응답 (server/Perks/PerkState.cs). 게임 형식에 의존하지 않아 단위 테스트로 검증한다.</summary>
    public sealed class PerkStateDto
    {
        [JsonProperty("prestigeLevel")] public int PrestigeLevel;
        [JsonProperty("slots")] public List<PerkSlotDto> Slots = new List<PerkSlotDto>();
        [JsonProperty("activePerks")] public List<int> ActivePerks = new List<int>();
        [JsonProperty("freeRerollsLeft")] public int FreeRerollsLeft;
        [JsonProperty("paidRerolls")] public int PaidRerolls;
        [JsonProperty("nextRerollCost")] public int NextRerollCost;
        [JsonProperty("costFixed")] public bool CostFixed;
        [JsonProperty("newPerks")] public List<int> NewPerks = new List<int>();
        [JsonProperty("bonuses")] public List<PerkBonusDto> Bonuses = new List<PerkBonusDto>();
        [JsonProperty("bonusIds")] public List<string> BonusIds = new List<string>();
        [JsonProperty("error")] public string Error;
        [JsonProperty("rollCost")] public int? RollCost;
        [JsonProperty("shakeChance")] public double ShakeChance;

        /// <summary>서버가 개발용 빌드다. 개발용 진단 로그를 남긴다.</summary>
        [JsonProperty("dev")] public bool Dev;

        /// <summary>흔들기 응답: Stay, Next (한 칸 더), Reverse (거꾸로 돌아 다른 특전). 흔들기 응답이 아니면 null.</summary>
        [JsonProperty("shakeOutcome")] public string ShakeOutcome;

        /// <summary>JSON 을 읽는다. 비었거나 형식이 틀리면 null.</summary>
        public static PerkStateDto Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var state = JsonConvert.DeserializeObject<PerkStateDto>(json);
                if (state == null)
                {
                    return null;
                }

                // 서버가 null 을 보내도 목록은 비어 있는 것으로 다룬다
                state.Slots = state.Slots ?? new List<PerkSlotDto>();
                state.ActivePerks = state.ActivePerks ?? new List<int>();
                state.NewPerks = state.NewPerks ?? new List<int>();
                state.Bonuses = state.Bonuses ?? new List<PerkBonusDto>();
                state.BonusIds = state.BonusIds ?? new List<string>();
                return state;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    public sealed class PerkSlotDto
    {
        [JsonProperty("perk")] public int Perk;
        [JsonProperty("locked")] public bool Locked;
    }

    /// <summary>서버 Bonus (바닐라 프로필 Bonuses 와 같은 형식). type·skillType 은 이름 문자열로 받는다.</summary>
    public sealed class PerkBonusDto
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("type")] public string Type;
        [JsonProperty("value")] public double Value;
        [JsonProperty("skillType")] public string SkillType;
        [JsonProperty("passive")] public bool? Passive;
        [JsonProperty("production")] public bool? Production;
        [JsonProperty("visible")] public bool? Visible;
    }
}
