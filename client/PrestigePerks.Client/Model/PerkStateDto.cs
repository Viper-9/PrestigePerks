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

        /// <summary>단계마다 주는 무료 리롤 수 (서버 config freeRerolls). 화면 "남은 / 이 값".</summary>
        [JsonProperty("freeRerolls")] public int FreeRerolls = 3;
        [JsonProperty("paidRerolls")] public int PaidRerolls;
        [JsonProperty("nextRerollCost")] public int NextRerollCost;
        [JsonProperty("costFixed")] public bool CostFixed;
        [JsonProperty("newPerks")] public List<int> NewPerks = new List<int>();
        [JsonProperty("bonuses")] public List<PerkBonusDto> Bonuses = new List<PerkBonusDto>();
        [JsonProperty("bonusIds")] public List<string> BonusIds = new List<string>();
        [JsonProperty("error")] public string Error;
        [JsonProperty("rollCost")] public int? RollCost;

        /// <summary>효과를 적용하는 특전과 단계 (1~3).</summary>
        [JsonProperty("activeTiers")] public Dictionary<int, int> ActiveTiers = new Dictionary<int, int>();

        /// <summary>도감: 보유한 특전과 산 단계.</summary>
        [JsonProperty("owned")] public Dictionary<int, int> Owned = new Dictionary<int, int>();

        /// <summary>지금 프레스티지의 단계 상한 (1~3).</summary>
        [JsonProperty("tierCap")] public int TierCap = 1;

        /// <summary>단계 II·III 이 열리는 프레스티지. [0] = II, [1] = III. 이미 열렸거나 열리지 않으면 null.</summary>
        [JsonProperty("tierOpensAt")] public List<int?> TierOpensAt = new List<int?>();

        /// <summary>단계 I·II·III 가격.</summary>
        [JsonProperty("tierPrices")] public List<int> TierPrices = new List<int>();

        /// <summary>단계 I·II·III 효과 비율.</summary>
        [JsonProperty("tierEffectRatio")] public List<double> TierEffectRatio = new List<double>();

        /// <summary>프레스티지로 오른 단계 상한 (알림용). 없으면 null.</summary>
        [JsonProperty("newTierCap")] public int? NewTierCap;

        /// <summary>구매에 쓴 코인 (구매 응답).</summary>
        [JsonProperty("buyCost")] public int? BuyCost;

        /// <summary>슬롯 칸마다 더 높은 어느 단계에서 열리는지. 열렸거나 열리지 않는 칸은 null.</summary>
        [JsonProperty("slotOpensAt")] public List<int?> SlotOpensAt = new List<int?>();

        /// <summary>서버가 개발용 빌드다. 개발용 진단 로그를 남긴다.</summary>
        [JsonProperty("dev")] public bool Dev;

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
                state.ActiveTiers = state.ActiveTiers ?? new Dictionary<int, int>();
                state.Owned = state.Owned ?? new Dictionary<int, int>();
                state.TierOpensAt = state.TierOpensAt ?? new List<int?>();
                state.TierPrices = state.TierPrices ?? new List<int>();
                state.TierEffectRatio = state.TierEffectRatio ?? new List<double>();
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
        /// <summary>특전 번호. 0 이면 빈 칸.</summary>
        [JsonProperty("perk")] public int Perk;
        [JsonProperty("locked")] public bool Locked;

        /// <summary>확정 칸 (보유한 특전을 넣은 칸).</summary>
        [JsonProperty("fixed")] public bool Fixed;

        [JsonIgnore] public bool IsEmpty => Perk == 0;
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
