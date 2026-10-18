using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace PrestigePerks.Perks;

/// <summary>/prestigeperks/* 라우트 응답이자 개발용 명령 출력의 바탕. 클라이언트 특전 페이지가 읽는다 (dev-docs 2_특전.md 6절).</summary>
public sealed record PerkState
{
    [JsonPropertyName("prestigeLevel")]
    public int PrestigeLevel { get; init; }

    /// <summary>보유 특전과 잠금 (효과가 적용되는 슬롯만, 단계 수만큼).</summary>
    [JsonPropertyName("slots")]
    public IReadOnlyList<PerkSlot> Slots { get; init; } = [];

    [JsonPropertyName("activePerks")]
    public IReadOnlyList<int> ActivePerks { get; init; } = [];

    [JsonPropertyName("freeRerollsLeft")]
    public int FreeRerollsLeft { get; init; }

    [JsonPropertyName("paidRerolls")]
    public int PaidRerolls { get; init; }

    /// <summary>다음 리롤 비용. 무료면 0.</summary>
    [JsonPropertyName("nextRerollCost")]
    public int NextRerollCost { get; init; }

    /// <summary>다음 유료 리롤이 상한 회차에 닿아 비용이 더 오르지 않는지 (화면의 "고정" 표시).</summary>
    [JsonPropertyName("costFixed")]
    public bool CostFixed { get; init; }

    /// <summary>새 단계 진입 때 자동으로 뽑아 아직 알리지 않은 특전. 클라이언트가 알림을 띄운다.</summary>
    [JsonPropertyName("newPerks")]
    public IReadOnlyList<int> NewPerks { get; init; } = [];

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

    /// <summary>흔들기가 한 칸 더 밀 확률 (0~1). 화면 안내용.</summary>
    [JsonPropertyName("shakeChance")]
    public double ShakeChance { get; init; }

    /// <summary>흔들기 응답: 결과 (<see cref="ShakeOutcome"/> 이름). 흔들기 응답이 아니면 null.</summary>
    [JsonPropertyName("shakeOutcome")]
    public string? ShakeOutcome { get; init; }
}

public sealed record PerkShakeRequest : SPTarkov.Server.Core.Models.Utils.IRequestData
{
    /// <summary>흔든 슬롯 번호 (0 부터). 멈추려는 릴.</summary>
    [JsonPropertyName("slot")]
    public int Slot { get; init; }

    /// <summary>그 릴의 다음 칸에 보이는 특전. 한 칸 더 가면 이 특전이 된다.</summary>
    [JsonPropertyName("next")]
    public int Next { get; init; }
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
