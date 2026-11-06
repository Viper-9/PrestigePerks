using System.Text.Json.Serialization;

namespace PrestigePerks.Perks;

/// <summary>
/// config/config.json 의 "activePerks". 끄면 클라이언트가 액티브 UI(칸, ACTIVE 탭, 목록, 알림, 레이드 키·HUD)를 없앤다.
/// 서버는 값을 state 로 넘기기만 한다 (리롤은 숨은 액티브 칸도 돌린다. 산 액티브와 코인 기록은 지우지 않는다). dev-docs 94 1절.
/// </summary>
public sealed record ActivePerkConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>액티브 칸이 열리는 프레스티지 단계. 0 이면 프레스티지 전에도 열린다. 열린 칸은 값을 올려도 닫히지 않는다.</summary>
    [JsonPropertyName("opensAt")]
    public int OpensAt { get; init; } = ActivePerkRules.DefaultOpensAt;

    /// <summary>범위를 벗어난 opensAt 을 0~6 의 가까운 끝값으로 맞추고 warn 을 부른다.</summary>
    public ActivePerkConfig Clamped(Action<string> warn)
    {
        var clamped = Math.Clamp(OpensAt, 0, PerkConfig.MaxLevel);
        if (clamped != OpensAt)
        {
            warn($"activePerks.opensAt={OpensAt} 은 0~{PerkConfig.MaxLevel} 밖이라 {clamped} 로 맞춥니다");
        }

        return this with { OpensAt = clamped };
    }
}
