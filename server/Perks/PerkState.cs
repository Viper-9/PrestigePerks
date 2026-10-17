using System.Text.Json.Serialization;

namespace PrestigePerks.Perks;

/// <summary>/prestigeperks/* 라우트 응답이자 개발용 명령 출력의 바탕. 다음 세션의 클라이언트 특전 화면이 읽는다.</summary>
public sealed record PerkState
{
    [JsonPropertyName("prestigeLevel")]
    public int PrestigeLevel { get; init; }

    /// <summary>확정된 보유 특전.</summary>
    [JsonPropertyName("perks")]
    public IReadOnlyList<int> Perks { get; init; } = [];

    /// <summary>지금 효과가 적용되는 특전 (확정 특전 중 단계 수만큼).</summary>
    [JsonPropertyName("activePerks")]
    public IReadOnlyList<int> ActivePerks { get; init; } = [];

    [JsonPropertyName("coinBalance")]
    public int CoinBalance { get; init; }

    [JsonPropertyName("session")]
    public PerkSession? Session { get; init; }

    /// <summary>다음 리롤 비용. 화면이 닫혀 있으면 null, 무료면 0.</summary>
    [JsonPropertyName("nextRerollCost")]
    public int? NextRerollCost { get; init; }

    /// <summary>요청이 실패한 이유 (<see cref="PerkError"/> 이름). 성공이면 null.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>확정 때 메일로 보낸 코인.</summary>
    [JsonPropertyName("coinsMailed")]
    public int? CoinsMailed { get; init; }

    /// <summary>리롤에 쓴 코인.</summary>
    [JsonPropertyName("rollCost")]
    public int? RollCost { get; init; }
}

public sealed record PerkLockRequest : SPTarkov.Server.Core.Models.Utils.IRequestData
{
    /// <summary>슬롯 번호 (0 부터).</summary>
    [JsonPropertyName("slot")]
    public int Slot { get; init; }

    [JsonPropertyName("locked")]
    public bool Locked { get; init; }
}
