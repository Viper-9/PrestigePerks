// 클라이언트는 nullable 을 끄고 빌드한다 (빈 칸·없는 특전 = null). 이 파일을 함께 컴파일하는 테스트(nullable 켬)에서도 같게 본다
#nullable disable

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace PrestigePerks.Model
{
    /// <summary>서버 /prestigeperks/* 응답 (server/Perks/PerkState.cs). 게임 형식에 의존하지 않아 단위 테스트로 검증한다.</summary>
    public sealed class PerkStateDto
    {
        [JsonProperty("prestigeLevel")] public int PrestigeLevel;
        [JsonProperty("slots")] public List<PerkSlotDto> Slots = new List<PerkSlotDto>();
        /// <summary>효과가 적용되는 특전 ID.</summary>
        [JsonProperty("activePerks")] public List<string> ActivePerks = new List<string>();
        [JsonProperty("freeRerollsLeft")] public int FreeRerollsLeft;

        /// <summary>단계마다 주는 무료 리롤 수 (서버 config freeRerolls). 화면 "남은 / 이 값".</summary>
        [JsonProperty("freeRerolls")] public int FreeRerolls = 3;
        [JsonProperty("paidRerolls")] public int PaidRerolls;
        [JsonProperty("nextRerollCost")] public int NextRerollCost;
        [JsonProperty("costFixed")] public bool CostFixed;
        [JsonProperty("newPerks")] public List<string> NewPerks = new List<string>();
        [JsonProperty("bonuses")] public List<PerkBonusDto> Bonuses = new List<PerkBonusDto>();
        [JsonProperty("bonusIds")] public List<string> BonusIds = new List<string>();
        [JsonProperty("error")] public string Error;
        [JsonProperty("rollCost")] public int? RollCost;

        /// <summary>효과를 적용하는 특전 ID 와 단계 (1~3).</summary>
        [JsonProperty("activeTiers")] public Dictionary<string, int> ActiveTiers = new Dictionary<string, int>();

        /// <summary>도감: 보유한 특전 ID 와 산 단계.</summary>
        [JsonProperty("owned")] public Dictionary<string, int> Owned = new Dictionary<string, int>();

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

        /// <summary>특전 목록 (서버 PerkCatalog 순서). 화면 목록·수치의 유일한 출처다.</summary>
        [JsonProperty("catalog")] public List<PerkCatalogEntryDto> Catalog = new List<PerkCatalogEntryDto>();

        private Dictionary<string, PerkCatalogEntryDto> byId = new Dictionary<string, PerkCatalogEntryDto>();
        private List<PerkCatalogEntryDto> byIdSource = new List<PerkCatalogEntryDto>();

        /// <summary>목록 순서의 특전 ID.</summary>
        [JsonIgnore] public IReadOnlyList<string> CatalogIds => Catalog.Select(entry => entry.Id).ToList();

        /// <summary>카탈로그의 특전. 없으면 (빈 칸, 서버가 모르는 특전) null.</summary>
        public PerkCatalogEntryDto Info(string perk)
        {
            if (perk == null)
            {
                return null;
            }

            // 테스트는 Catalog 를 바꿔 끼우므로 목록이 바뀌면 다시 만든다
            if (!ReferenceEquals(byIdSource, Catalog))
            {
                byIdSource = Catalog;
                byId = new Dictionary<string, PerkCatalogEntryDto>();
                foreach (var entry in Catalog)
                {
                    if (entry?.Id != null && !byId.ContainsKey(entry.Id))
                    {
                        byId[entry.Id] = entry;
                    }
                }
            }

            return byId.TryGetValue(perk, out var info) ? info : null;
        }

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
                state.Slots = (state.Slots ?? new List<PerkSlotDto>()).Where(slot => slot != null).ToList();
                state.ActivePerks = state.ActivePerks ?? new List<string>();
                state.NewPerks = state.NewPerks ?? new List<string>();
                state.Bonuses = state.Bonuses ?? new List<PerkBonusDto>();
                state.BonusIds = state.BonusIds ?? new List<string>();
                state.ActiveTiers = state.ActiveTiers ?? new Dictionary<string, int>();
                state.Owned = state.Owned ?? new Dictionary<string, int>();
                state.TierOpensAt = state.TierOpensAt ?? new List<int?>();
                state.TierPrices = state.TierPrices ?? new List<int>();
                state.TierEffectRatio = state.TierEffectRatio ?? new List<double>();
                state.Catalog = (state.Catalog ?? new List<PerkCatalogEntryDto>()).Where(entry => !string.IsNullOrEmpty(entry?.Id)).ToList();
                state.DropUnknownPerks();
                return state;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        // 카탈로그에 없는 특전은 그릴 수 없으니 버린다. 서버가 이미 빼고 보내므로 방어용이다 (dev-docs 96 결정 5)
        private void DropUnknownPerks()
        {
            foreach (var slot in Slots)
            {
                if (slot.IsEmpty)
                {
                    // "" 도 빈 칸이다. 페이지가 null 로 비교하므로 null 로 맞춘다
                    slot.Perk = null;
                }
                else if (Info(slot.Perk) == null)
                {
                    slot.Perk = null;
                    slot.Locked = false;
                    slot.Fixed = false;
                }
            }

            ActivePerks = ActivePerks.Where(perk => Info(perk) != null).ToList();
            NewPerks = NewPerks.Where(perk => Info(perk) != null).ToList();
            ActiveTiers = ActiveTiers.Where(pair => Info(pair.Key) != null).ToDictionary(pair => pair.Key, pair => pair.Value);
            Owned = Owned.Where(pair => Info(pair.Key) != null).ToDictionary(pair => pair.Key, pair => pair.Value);
        }
    }

    public sealed class PerkSlotDto
    {
        /// <summary>특전 ID. null 이면 빈 칸.</summary>
        [JsonProperty("perk")] public string Perk;
        [JsonProperty("locked")] public bool Locked;

        /// <summary>확정 칸 (보유한 특전을 넣은 칸).</summary>
        [JsonProperty("fixed")] public bool Fixed;

        [JsonIgnore] public bool IsEmpty => string.IsNullOrEmpty(Perk);
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

    /// <summary>server PerkCatalogEntry.</summary>
    public sealed class PerkCatalogEntryDto
    {
        public const string Percent = "percent", Points = "points", Flat = "flat";

        /// <summary>add-on 특전 종류 (server PerkCatalogEntry.BonusKind, CodeKind).</summary>
        public const string BonusKind = "bonus", CodeKind = "code";

        [JsonProperty("id")] public string Id;

        /// <summary>단계 III 효과량 (부호 포함, % 는 숫자만).</summary>
        [JsonProperty("amount")] public double Amount;
        [JsonProperty("unit")] public string Unit;

        /// <summary>add-on 특전 종류 ("bonus", "code"). 내장 특전은 null.</summary>
        [JsonProperty("kind")] public string Kind;

        /// <summary>add-on 특전 문구 (게임 언어 코드 → 문구). 내장 특전과 문구 없는 add-on 은 null.</summary>
        [JsonProperty("texts")] public Dictionary<string, PerkTextsDto> Texts;

        /// <summary>add-on 아이콘이 있다 (/prestigeperks/icons 로 받는다).</summary>
        [JsonProperty("icon")] public bool Icon;
    }

    /// <summary>add-on 특전 문구 한 언어 (server PerkTextsEntry).</summary>
    public sealed class PerkTextsDto
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("effect")] public string Effect;
        [JsonProperty("detail")] public string Detail;
    }
}
