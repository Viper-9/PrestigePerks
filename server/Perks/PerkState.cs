using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace PrestigePerks.Perks;

/// <summary>/prestigeperks/* 라우트 응답이자 개발용 명령 출력의 바탕. 클라이언트 특전 페이지가 읽는다 (dev-docs 2_특전.md 6절).</summary>
public sealed record PerkState
{
    [JsonPropertyName("prestigeLevel")]
    public int PrestigeLevel { get; init; }

    /// <summary>슬롯 (효과가 적용되는 슬롯만, 단계 수만큼). 특전 null 은 빈 칸.</summary>
    [JsonPropertyName("slots")]
    public IReadOnlyList<PerkSlotState> Slots { get; init; } = [];

    /// <summary>효과가 적용되는 특전 ID.</summary>
    [JsonPropertyName("activePerks")]
    public IReadOnlyList<string> ActivePerks { get; init; } = [];

    [JsonPropertyName("freeRerollsLeft")]
    public int FreeRerollsLeft { get; init; }

    /// <summary>단계마다 주는 무료 리롤 수 (config freeRerolls). 화면 "남은 / 이 값" 표시용.</summary>
    [JsonPropertyName("freeRerolls")]
    public int FreeRerolls { get; init; }

    [JsonPropertyName("paidRerolls")]
    public int PaidRerolls { get; init; }

    /// <summary>다음 리롤 비용. 무료면 0.</summary>
    [JsonPropertyName("nextRerollCost")]
    public int NextRerollCost { get; init; }

    /// <summary>다음 유료 리롤이 상한 회차에 닿아 비용이 더 오르지 않는지 (화면의 "고정" 표시).</summary>
    [JsonPropertyName("costFixed")]
    public bool CostFixed { get; init; }

    /// <summary>새 단계 진입 때 자동으로 뽑아 아직 알리지 않은 특전 ID. 클라이언트가 알림을 띄운다.</summary>
    [JsonPropertyName("newPerks")]
    public IReadOnlyList<string> NewPerks { get; init; } = [];

    /// <summary>지금 PMC 에 들어가 있어야 하는 특전 Bonus (바닐라 BonusDescriptor 형식). 클라이언트가 BonusController 에 넣는다.</summary>
    [JsonPropertyName("bonuses")]
    public IReadOnlyList<Bonus> Bonuses { get; init; } = [];

    /// <summary>특전 Bonus ID 전체 (현재 + 삭제한 특전). 클라이언트가 이 ID 들을 먼저 빼고 <see cref="Bonuses"/> 를 넣는다.</summary>
    [JsonPropertyName("bonusIds")]
    public IReadOnlyList<string> BonusIds { get; init; } = [];

    /// <summary>요청이 실패한 이유 (<see cref="PerkError"/> 이름). 성공이면 null.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>리롤에 쓴 코인 (개발용 명령 출력).</summary>
    [JsonPropertyName("rollCost")]
    public int? RollCost { get; init; }

    /// <summary>서버가 개발용(Dev) 빌드다. 클라이언트가 개발용 진단 로그(특전 효과 계산값)를 남긴다.</summary>
    [JsonPropertyName("dev")]
    public bool Dev { get; init; }

    /// <summary>슬롯 칸(6칸)마다 더 높은 어느 단계에서 열리는지. 열렸거나 열리지 않는 칸은 null (<see cref="PerkConfig.SlotOpensAt"/>).</summary>
    [JsonPropertyName("slotOpensAt")]
    public IReadOnlyList<int?> SlotOpensAt { get; init; } = [];

    /// <summary>효과를 적용하는 특전 ID 와 단계 (1~3). 클라이언트 패치가 tierEffectRatio 로 효과량을 정한다.</summary>
    [JsonPropertyName("activeTiers")]
    public IReadOnlyDictionary<string, int> ActiveTiers { get; init; } = new Dictionary<string, int>();

    /// <summary>도감: 보유한 특전 ID 와 산 단계.</summary>
    [JsonPropertyName("owned")]
    public IReadOnlyDictionary<string, int> Owned { get; init; } = new Dictionary<string, int>();

    /// <summary>지금 프레스티지의 단계 상한 (1~3).</summary>
    [JsonPropertyName("tierCap")]
    public int TierCap { get; init; } = 1;

    /// <summary>단계 II·III 이 열리는 프레스티지 (이미 열렸거나 열리지 않으면 null). [0] = II, [1] = III. 화면 "Available at Pn".</summary>
    [JsonPropertyName("tierOpensAt")]
    public IReadOnlyList<int?> TierOpensAt { get; init; } = [];

    /// <summary>단계 I·II·III 가격 (config tierPrices).</summary>
    [JsonPropertyName("tierPrices")]
    public IReadOnlyList<int> TierPrices { get; init; } = [];

    /// <summary>단계 I·II·III 효과 비율 (config tierEffectRatio).</summary>
    [JsonPropertyName("tierEffectRatio")]
    public IReadOnlyList<double> TierEffectRatio { get; init; } = [];

    /// <summary>프레스티지로 올라 아직 알리지 않은 단계 상한. 클라이언트가 새 특전 알림에 붙인다.</summary>
    [JsonPropertyName("newTierCap")]
    public int? NewTierCap { get; init; }

    /// <summary>구매에 쓴 코인 (구매 응답). 이미 가진 단계면 0.</summary>
    [JsonPropertyName("buyCost")]
    public int? BuyCost { get; init; }

    /// <summary>특전 목록 (표시 순서, III 효과량, 단위). 클라이언트는 이것으로 목록과 수치를 그린다. 오류 응답에도 실린다 (클라이언트가 오류 응답을 새 state 로 받는다).</summary>
    [JsonPropertyName("catalog")]
    public IReadOnlyList<PerkCatalogEntry> Catalog { get; init; } = PerkCatalog.Entries;
}

/// <summary>state 의 catalog 한 줄. 단위는 문자열로 보낸다 (enum 직렬화 방식에 기대지 않는다).</summary>
public sealed record PerkCatalogEntry
{
    public const string Percent = "percent", Points = "points", Flat = "flat";

    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    /// <summary>단계 III 효과량 (부호 포함, % 는 숫자만).</summary>
    [JsonPropertyName("amount")]
    public double Amount { get; init; }

    [JsonPropertyName("unit")]
    public string Unit { get; init; } = Percent;
}

/// <summary>state 응답의 슬롯 한 칸.</summary>
public sealed record PerkSlotState
{
    /// <summary>특전 ID. null 이면 빈 칸.</summary>
    [JsonPropertyName("perk")]
    public string? Perk { get; init; }

    [JsonPropertyName("locked")]
    public bool Locked { get; init; }

    [JsonPropertyName("fixed")]
    public bool Fixed { get; init; }

    [JsonIgnore]
    public bool IsEmpty => Perk is null;

    /// <summary>모르는 특전(add-on 을 뺀 경우)은 빈 칸으로 보낸다. 데이터 파일 정리(PerkDataFile.Normalize)가 이미 빈 칸으로 바꾸므로 방어용이다.</summary>
    public static PerkSlotState From(PerkSlot slot) =>
        PerkCatalog.IsKnown(slot.Perk) ? new() { Perk = slot.Perk, Locked = slot.Locked, Fixed = slot.Fixed } : new();
}

public sealed record PerkEquipRequest : SPTarkov.Server.Core.Models.Utils.IRequestData
{
    /// <summary>넣을 보유 특전 ID.</summary>
    [JsonPropertyName("perk")]
    public string? Perk { get; init; }

    /// <summary>놓을 슬롯 번호 (0 부터).</summary>
    [JsonPropertyName("slot")]
    public int Slot { get; init; }
}

public sealed record PerkUnequipRequest : SPTarkov.Server.Core.Models.Utils.IRequestData
{
    /// <summary>뺄 확정 칸 (0 부터).</summary>
    [JsonPropertyName("slot")]
    public int Slot { get; init; }
}

public sealed record PerkLockRequest : SPTarkov.Server.Core.Models.Utils.IRequestData
{
    /// <summary>슬롯 번호 (0 부터).</summary>
    [JsonPropertyName("slot")]
    public int Slot { get; init; }

    [JsonPropertyName("locked")]
    public bool Locked { get; init; }
}

/// <summary>
/// 리롤 아이템 이벤트 (/client/game/profile/items/moving, Action = <see cref="ActionName"/>).
/// 코인을 인벤토리에서 빼야 해서 바닐라 상인 구매와 같은 경로를 쓴다.
/// </summary>
public sealed record PerkRollRequest : BaseInteractionRequestData
{
    public const string ActionName = "PrestigePerksRoll";

    /// <summary>클라이언트가 화면에서 본 비용. 서버가 계산한 값과 다르면 거부한다 (오래된 화면).</summary>
    [JsonPropertyName("expectedCost")]
    public int ExpectedCost { get; init; }

    /// <summary>먼저 쓸 코인 스택 ID (바닐라 결제처럼 우선순위만 정한다). 비워도 된다.</summary>
    [JsonPropertyName("items")]
    public List<string>? Items { get; init; }
}

/// <summary>
/// 보유하기·단계 구매 아이템 이벤트 (Action = <see cref="ActionName"/>). 리롤처럼 인벤토리 코인으로 결제한다.
/// 이미 그 단계를 가졌으면 결제 없이 성공으로 돌려준다 (두 번 눌림).
/// </summary>
public sealed record PerkBuyRequest : BaseInteractionRequestData
{
    public const string ActionName = "PrestigePerksBuy";

    /// <summary>살 특전 ID.</summary>
    [JsonPropertyName("perk")]
    public string? Perk { get; init; }

    /// <summary>사려는 단계 (1 = 보유하기).</summary>
    [JsonPropertyName("tier")]
    public int Tier { get; init; }

    /// <summary>클라이언트가 화면에서 본 가격. 서버 값과 다르면 거부한다 (오래된 화면).</summary>
    [JsonPropertyName("expectedCost")]
    public int ExpectedCost { get; init; }

    /// <summary>먼저 쓸 코인 스택 ID. 비워도 된다.</summary>
    [JsonPropertyName("items")]
    public List<string>? Items { get; init; }
}
